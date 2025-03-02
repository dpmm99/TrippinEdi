using LLama;
using LLama.Abstractions;
using LLama.Batched;
using LLama.Native;
using System.Text;
using System.Text.RegularExpressions;

namespace TrippinEdi;

public class BatchedInferenceService
{
    private readonly LLamaWeights _model;
    private readonly float _temperature;
    private readonly OutputHandler _output;
    private readonly StreamingTokenDecoder _decoder;
    private readonly BatchedExecutor _executor;
    private readonly SafeLlamaModelHandle.Vocabulary _vocab;

    private readonly string[] _loopBreakers = ["--hold on, I've already mentioned that. Let me come up with something fresh.\n",
        "--I notice that's redundant information. I have to try again with a different approach.\n",
        "--that's the same thing again. Let's explore a different angle with only new information.\n",
        "--I just realized I'm repeating myself. Let me pivot to a new fact.\n",
        "--hmm, that's duplicate information. I have to offer something novel instead, so let's try again.\n",
        "--oops, I've said that before. Let me expand my perspective and come up with something unfamiliar.\n",
        "--that's a repeat observation. Let me switch tracks and introduce a genuinely clever unique point.\n",
        "--no, I see I'm retracing steps there. I have to present something unexpected for the next fact.\n",
        "--rather than echo what's been said, let me share a meaningful new insight.\n",
        "--instead of repeating that point, I must offer something not already stated.\n",
        "--wait, that's a repeat. Let's try a different approach for the next fact--it can't be something the user already knows.\n"];

    public BatchedInferenceService(LLamaWeights model, IContextParams @params, float temperature, OutputHandler output)
    {
        _model = model;
        _temperature = temperature;
        _output = output;
        _executor = new BatchedExecutor(model, @params);
        _decoder = new StreamingTokenDecoder(_executor.Context);
        _vocab = model.Vocab;
    }

    public async Task<List<string>> InferAsync(string promptText, IEnumerable<Func<bool, List<string>, StringBuilder, string, bool>>? earlyStopEvaluators = null, CancellationToken cancellationToken = default)
    {
        // Wrap the prompt in the template. Note: has a chance to misinterpret the start/end special tokens since LLamaTemplate can't output embeddings, as far as I can see.
        //Can check _model.Metadata.ContainsKey("tokenizer.chat_template") 
        var template = new LLamaTemplate(_model) { AddAssistant = true };
        template.Add("user", promptText);
        var templatedPrompt = Encoding.UTF8.GetString(template.Apply());

        // Set up the conversation
        using var conversation = _executor.Create();
        using var sampler = new DistributionSamplingPipelineThatStops(_model, _temperature);

        // Initialize response tracking
        var results = new List<string>();
        var currentResult = new StringBuilder();

        // Initial prompt
        conversation.Prompt(_executor.Context.Tokenize(templatedPrompt, addBos: true, special: true)
            .Concat(_executor.Context.Tokenize("<think>\rOkay, ", false, true)).ToArray()); // Force thinking
        var thinking = true;
        var firstIteration = true;

        do
        {
            if (thinking && !firstIteration) //Only if it was cut off on the first call to InferAsync
            {
                results.Clear();
                thinking = false;

                //Force add </think> to the conversation
                var stopThinkingTokens = _model.Tokenize("</think>", false, true, Encoding.UTF8);
                conversation.Prompt(stopThinkingTokens);
                _output.WriteLine("\n-- Forced </think> --", ConsoleColor.DarkGray);
            }
            firstIteration = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                // Run inference
                var decodeResult = await _executor.Infer(cancellationToken);
                if (decodeResult != DecodeResult.Ok)
                    throw new Exception($"Inference failed with result: {decodeResult}");

                if (!conversation.RequiresSampling)
                    continue;

                // Sample next token
                var token = conversation.Sample(sampler);

                // Check for end of sequence
                if (token.IsEndOfGeneration(_vocab))
                    break;

                // Process token
                _decoder.Add(token);
                var text = _decoder.Read();
                currentResult.Append(text);

                //I saw it get stuck in a loop once where it kept doing what I asked but then ending with </think> and repeating.
                //Added the results.Count > 0 condition because when I tried Mistral Small 3, it stated, "I will conclude my thoughts with </think>.</think>"
                if (!thinking && results.Count > 0 && (currentResult.EndsWith("</think>") || (currentResult.Length < 20 && currentResult.StartsWith("</think>"))))
                {
                    currentResult.Clear();
                    break;
                }
                if (currentResult.Length < 20 && (currentResult.StartsWith("</Past") || currentResult.StartsWith("</Pend"))) //It also keeps ending with </PastDiscoveries> and then either repeating that several times or looping when I ask it for shortened text.
                {
                    currentResult.Clear();
                    thinking = false;
                    break;
                }

                //Done thinking -> don't want the thoughts
                if (currentResult.EndsWith("</think>") || (currentResult.Length < 20 && currentResult.StartsWith("</think>")))
                {
                    results.Clear();
                    currentResult.Clear();
                    thinking = false;
                }

                // Handle line breaks and results
                if (text.Contains('\n'))
                {
                    var currentResultLines = currentResult.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    currentResult.Clear();
                    //It should be rare, ESPECIALLY in this application, but in theory, it is possible for a token to contain a newline and more text after that, so we might keep some text.
                    if (currentResultLines.Length > 1) currentResult.Append(currentResultLines[1]);

                    //The model I tested with isn't smart enough to understand the "no titles or numbering" instruction, so remove numbering.
                    if (currentResultLines.Length >= 1)
                    {
                        var line = numberStartRegex.Replace(currentResultLines[0], "");

                        //Inject some extra text to try to fix the loop if we've already seen this line. Models like to get stuck in a loop and waste time.
                        if (results.Contains(line))
                        {
                            var loopBreaker = _loopBreakers[Random.Shared.Next(_loopBreakers.Length)];
                            conversation.Prompt(_model.Tokenize(text.Split("\n")[0].TrimEnd('.') + loopBreaker, false, false, Encoding.UTF8));
                            _output.Write("-- Model started repeating itself; injected text to try to stop it --\n", ConsoleColor.DarkGray);

                            //Temporarily ban that first token competely in the sampler //TODO: Optimally, we would ban the most important word(s), too.
                            sampler.BanToken(_model.Tokenize(line, false, false, Encoding.UTF8)[0], banDuration: 6);

                            continue;
                        }

                        if (line.Length > 4) results.Add(line);
                    }
                }

                _output.Write(text, ConsoleColor.Gray);

                // Prompt next token
                conversation.Prompt(token);

                // Check early stop evaluators
                if (earlyStopEvaluators != null)
                {
                    foreach (var evaluator in earlyStopEvaluators)
                    {
                        if (evaluator(thinking, results, currentResult, text))
                        {
                            thinking = false;
                            break;
                        }
                    }
                }
            }

            //Assume it doesn't spit out text for the newline before the EOS.
            var finalResult = numberStartRegex.Replace(currentResult.ToString(), "");
            if (finalResult.Length > 4) results.Add(finalResult);
            currentResult.Clear();
        } while ((thinking || results.Count < 1) && !cancellationToken.IsCancellationRequested); //TODO: A limit on results.Count is bad when summarizing especially

        return results;
    }

    private static readonly Regex numberStartRegex = new Regex(@"^(\d+\.|-)\s*(\*\*.*\*\*:\s+)?", RegexOptions.Compiled); //Removes "1. **bold**:" or just "1. " or "- "
}
