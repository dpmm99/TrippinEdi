#define UseInteractExecutor
using LLama;
using LLama.Common;
using LLama.Native;
using System.Diagnostics.CodeAnalysis;
#if UseInteractExecutor //Simpler to use and handles context shifting, but currently crashes at random roughly once every 2-4 responses
using System.Text;
using System.Text.RegularExpressions;
#endif

namespace TrippinEdi;

public class DiscoveryService : IDiscoveryService
{
    private LLamaWeights? _model;
    private ModelParams? _modelParameters;

    private const string _factGenerationTemplate = """
        User's interests:
        {interests}

        User dislikes:
        {dislikes}

        Already gave facts about:
        {pastdiscoveries}

        Examples of what NOT to write:
        Too broad: Pixel art often involves a limited color palette and small sizes.
        Too broad and not a statement: Creating a physics-based camera system for smooth 2D tracking.
        Too common-knowledge: You can greatly optimize code for older/embedded systems via bitwise operations.
        Too common-knowledge: Older/embedded systems necessitate optimized memory usage.
        Not a fact: Implementing specific memory allocation strategies for real-time rendering in games.
        
        Example of a good complete response (except it should be longer):
        While a square root operation takes dozens to hundreds of CPU cycles, Doom has a very fast square root estimate function.
        You can use a priority heap for efficient A* pathfinding.
        Procedural music systems using Markov chains can generate endless variations of background music.

        Instructions:
        List a bunch of somewhat niche, very specific facts (topics) within the user's areas of interest in the form of statements. Exclude any content related to their dislikes. Exclude anything vaguely related to facts they were already given. Focus on specific techniques, practical applications, and lesser-known but highly relevant aspects of these fields. Provide concrete examples, one-line explanations, or real-world applications that can be directly applied to enhance skills or projects in these areas. Avoid overly broad facts or obscure niches that are irrelevant to the specified interests. Just provide one fact per line, no titles, no **bold**. The user has deep familiarity with all the listed topics. The goal is to give the user more new facts and concepts that are interesting to learn about--NOT to dance around the same topics.
        Provide 30 responses meeting these criteria; they don't have to cover every listed interest. After considering each response, state to yourself in one word whether it meets the specificity/nicheness requirements. And make doubly sure they are really specific statements and are not repeats. You can think all you need up front, as long as the thinking appears between <think> and </think>.
        You may first think through sub-topics within the user's interests so that you DO NOT mention similar facts to any of the above!
        """;

    private const string _evaluationTemplate = """
        User dislikes:
        {dislikes}

        Already gave the user facts about:
        {pastdiscoveries}
        
        The pending discoveries to evaluate:
        <PendingDiscoveries>
        {pendingdiscoveries}
        </PendingDiscoveries>
        
        Example fact: Quadtree spatial partitioning can reduce physics collision checks from O(n^2) to O(n log n) in 2D games.

        Instructions:
        Evaluate each pending discovery (facts about the user's topics of interest, between the PendingDiscoveries XML tags) to ensure it is relevant to the user's interests and sufficiently different from facts already given to the user, indicated above. Provide no facts that are even close to the same as past discoveries or as each other, e.g., no facts about the same algorithm, approach, or event. In the final response, simply repeat each pending discovery that is sufficiently distant from all the facts already given to the user AND sufficiently distant from their dislikes. Do not acknowledge the prompt and do not write a conclusion; your output must be exactly one line per acceptable pending discovery, or it will break the program you support. If there are no good pending discoveries, simply don't respond.
        However, if any of them are just a topic with no real information, instead of repeating it as-is, provide a specific detail of the topic itself (e.g., "Using specific data structures for efficient pathfinding" -> "You can use a priority heap for efficient A* pathfinding"). Ensure the resulting facts are meaningful information, in statement form, that the user can easily search for and find more detailed results about. You can think all you need up front, as long as the thinking appears between <think> and </think>.
        """;

    private const string _compactingTemplate = """
        <PastDiscoveries>
        {pastdiscoveries}
        </PastDiscoveries>
        
        Instructions:
        Given the above list of past discoveries (until </PastDiscoveries>), which should be one fact per line, provide a shortened version of each, also one per line. The shortened version should be just 2-5 words that capture the essence of the fact for similarity comparison purposes. The goal is to minimize the amount of text when asking for discoveries not in this list later on. Do not acknowledge the prompt or write a conclusion; write only the shortened facts, in the same order that they were given.
        """;

    public string GetInferPrompt(List<string> interests, List<string> dislikes, List<string> pastDiscoveries)
    {
        return _factGenerationTemplate.Replace("{interests}", string.Join("\n", interests))
            .Replace("{dislikes}", string.Join("\n", dislikes))
            .Replace("{pastdiscoveries}", string.Join("\n", pastDiscoveries));
    }

    public async Task<List<string>> InferAsync(List<string> interests, List<string> dislikes, List<string> pastDiscoveries, float temperature, OutputHandler output)
    {
        var prompt = GetInferPrompt(interests, dislikes, pastDiscoveries);

        return await InferLinesAsync(prompt, temperature, output);
    }

    public async Task<List<string>> EvaluateAsync(List<string> dislikes, List<string> pastDiscoveries, List<string> pendingDiscoveries, OutputHandler output)
    {
        var prompt = _evaluationTemplate.Replace("{dislikes}", string.Join("\n", dislikes))
            .Replace("{pastdiscoveries}", string.Join("\n", pastDiscoveries))
            .Replace("{pendingdiscoveries}", string.Join("\n", pendingDiscoveries));

        return await InferLinesAsync(prompt, 0, output);
    }

    public async Task<List<string>> CompactAsync(List<string> pastDiscoveries, OutputHandler output)
    {
        var prompt = _compactingTemplate.Replace("{pastdiscoveries}", string.Join("\n", pastDiscoveries));
        return await InferLinesAsync(prompt, 0, output);
    }

    [MemberNotNull(nameof(_model))]
    [MemberNotNull(nameof(_modelParameters))]
    private void LoadModel()
    {
        if (_modelParameters != null)
        {
            while (_model == null) Thread.Sleep(100);
            return;
        }

        //Get the biggest GGUF in the current directory and use that. If there is none, then revert to my FuseO1 at a fixed path.
        var ggufs = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.gguf", SearchOption.TopDirectoryOnly);
        Array.Sort(ggufs, (a, b) => new FileInfo(b).Length.CompareTo(new FileInfo(a).Length));

        _modelParameters = new ModelParams(ggufs.FirstOrDefault() ?? @"C:\AI\FuseO1-DeekSeekR1-QwQ-SkyT1-32B-Preview-Q4_K_M.gguf")
        {
            ContextSize = 8192, //TODO: would be great if we could load just the vocab and calculate the context size needed. 4096 + 32 * past discoveries would be a decent context size guess, but it has to increase the more the user uses the program.
            GpuLayerCount = 99, //Just as many as it can handle
            FlashAttention = true,
            TypeK = GGMLType.GGML_TYPE_Q8_0,
            TypeV = GGMLType.GGML_TYPE_Q8_0,
            BatchSize = 2048, //TODO: Experimenting with batch sizes to see if that pertains to the crash
            UBatchSize = 2048,
        };
        _model = LLamaWeights.LoadFromFile(_modelParameters);
        NativeLogConfig.llama_log_set(new FileLogger("llamacpp_log.txt")); //I'm told you have to set it again after loading the model in order to catch a crash reason.
    }

    private async Task<List<string>> InferLinesAsync(string prompt, float temperature, OutputHandler output)
    {
        LoadModel();

#if UseInteractExecutor
        //Wrap the prompt in the template. Note: has a chance to misinterpret the start/end special tokens since InteractiveExecutor doesn't have a better way to do it.
        var template = new LLamaTemplate(_model) { AddAssistant = true };
        template.Add("user", prompt);
        prompt = Encoding.UTF8.GetString(template.Apply());

        var inferenceParams = new InferenceParams()
        {
            SamplingPipeline = new DistributionSamplingPipelineThatStops(_model, temperature),
            MaxTokens = 4096,
        };

        using var context = _model.CreateContext(_modelParameters);
        var executor = new InteractiveExecutor(context);
        var currentResult = new StringBuilder();
        var results = new List<string>();
        var thinking = false;
        do
        {
            if (thinking) //Only if it was cut off on the first call to InferAsync
            {
                results.Clear();
                thinking = false;
                prompt = "</think>"; //Try to make it finish thinking
                output.WriteLine("\n-- Forced </think> --", ConsoleColor.DarkGray);
            }

            await foreach (var text in executor.InferAsync(prompt, inferenceParams, default))
            {
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

                output.Write(text, ConsoleColor.Gray);
            }

            //I don't remember if the InferAsync loop spits out text for the newline before the EOS, so just assume it doesn't.
            var finalResult = numberStartRegex.Replace(currentResult.ToString(), "");
            if (finalResult.Length > 4) results.Add(finalResult);
        } while (thinking);

        output.WriteLine("\n-- End of generation --", ConsoleColor.DarkGray);

        return results;
#else
        var service = new BatchedInferenceService(_model, _modelParameters, temperature, output);
        return await service.InferAsync(prompt);
#endif
    }

#if UseInteractExecutor
    private static readonly Regex numberStartRegex = new Regex(@"^(\d+\.|-)\s*(\*\*.*\*\*:\s+)?", RegexOptions.Compiled); //Removes "1. **bold**:" or just "1. " or "- "
#endif
}
