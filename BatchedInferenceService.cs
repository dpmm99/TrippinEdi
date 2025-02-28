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

    public BatchedInferenceService(LLamaWeights model, IContextParams @params, float temperature, OutputHandler output)
    {
        _model = model;
        _temperature = temperature;
        _output = output;
        _executor = new BatchedExecutor(model, @params);
        _decoder = new StreamingTokenDecoder(_executor.Context);
        _vocab = model.Vocab;
    }

    public async Task<List<string>> InferAsync(string promptText, CancellationToken cancellationToken = default)
    {
        // Wrap the prompt in the template. Note: has a chance to misinterpret the start/end special tokens since LLamaTemplate can't output embeddings, as far as I can see.
        var template = new LLamaTemplate(_model) { AddAssistant = true };
        template.Add("user", promptText);
        var templatedPrompt = Encoding.UTF8.GetString(template.Apply());

        // Set up the conversation
        var conversation = _executor.Create();
        var sampler = new DistributionSamplingPipelineThatStops(_model, _temperature);

        // Initialize response tracking
        var results = new List<string>();
        var currentResult = new StringBuilder();
        var thinking = false;

        // Initial prompt
        conversation.Prompt(_executor.Context.Tokenize(templatedPrompt, addBos: true, special: true));

        do
        {
            if (thinking) //Only if it was cut off on the first call to InferAsync
            {
                results.Clear();
                thinking = false;
                //TODO: force add this token to the conversation
                var stopThinkingTokens = _model.Tokenize("</think>", false, true, Encoding.UTF8);
                _decoder.AddRange(stopThinkingTokens);
                _output.WriteLine("\n-- Forced </think> --", ConsoleColor.DarkGray);
            }

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

                // Check for thinking markers
                if (!thinking && text == "</think>") break; //I saw it get stuck in a loop once where it kept doing what I asked but then ending with </think> and repeating.
                if (currentResult.Length > 5 && currentResult.Length < 20 && (currentResult.ToString(0, 6) == "</Past" || currentResult.ToString(0, 6) == "</Pend")) //It also keeps ending with </PastDiscoveries> and then either repeating that several times or looping when I ask it for shortened text.
                {
                    currentResult.Clear();
                    thinking = false;
                    break;
                }

                currentResult.Append(text);
                if (text == "<think>") thinking = true;
                if (text == "</think>") //Done thinking -> don't want the thoughts
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

                        //Cut off inference if we've already seen this line. Models like to get stuck in a loop and waste time.
                        if (results.Contains(line)) break; //TODO: Would be good to force it to </think> and keep going.

                        if (line.Length > 4) results.Add(line);
                    }
                }

                _output.Write(text, ConsoleColor.Gray);

                // Prompt next token
                conversation.Prompt(token);
            }

            //Assume it doesn't spit out text for the newline before the EOS.
            var finalResult = numberStartRegex.Replace(currentResult.ToString(), "");
            if (finalResult.Length > 4) results.Add(finalResult);
        } while (thinking);

        conversation.Dispose();
        sampler.Dispose();
        return results;
    }

    private static readonly Regex numberStartRegex = new Regex(@"^(\d+\.|-)\s*(\*\*.*\*\*:\s+)?", RegexOptions.Compiled); //Removes "1. **bold**:" or just "1. " or "- "
}
