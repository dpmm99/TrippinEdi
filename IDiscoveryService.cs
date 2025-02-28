namespace TrippinEdi;

public interface IDiscoveryService
{
    string GetInferPrompt(List<string> interests, List<string> dislikes, List<string> pastDiscoveries);
    Task<List<string>> InferAsync(List<string> interests, List<string> dislikes, List<string> pastDiscoveries, float temperature, OutputHandler output);
    Task<List<string>> EvaluateAsync(List<string> dislikes, List<string> pastDiscoveries, List<string> pendingDiscoveries, OutputHandler output);
    Task<List<string>> CompactAsync(List<string> pastDiscoveries, OutputHandler output);
}
