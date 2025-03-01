namespace TrippinEdi;

public interface IDiscoveryService
{
    string GetInferPrompt(IEnumerable<string> interests, IEnumerable<string> dislikes, string[] pastDiscoveries);
    Task<IEnumerable<string>> InferAsync(IEnumerable<string> interests, IEnumerable<string> dislikes, string[] pastDiscoveries, float temperature, OutputHandler output);
    Task<IEnumerable<string>> EvaluateAsync(IEnumerable<string> dislikes, string[] pastDiscoveries, IEnumerable<string> pendingDiscoveries, OutputHandler output);
    Task<IEnumerable<string>> CompactAsync(IEnumerable<string> pastDiscoveries, OutputHandler output);
}
