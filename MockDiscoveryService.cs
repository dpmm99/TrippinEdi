namespace TrippinEdi;

public class MockDiscoveryService : IDiscoveryService
{
    public string GetInferPrompt(List<string> interests, List<string> dislikes, List<string> pastDiscoveries)
    {
        return "MockDiscoveryService has no prompt.";
    }

    public async Task<List<string>> InferAsync(List<string> interests, List<string> dislikes, List<string> pastDiscoveries, float temperature, OutputHandler output)
    {
        // Simulate an async call to an external service
        await Task.Delay(1000);

        return pastDiscoveries.Contains("Improve dithering patterns with error diffusion for higher quality images in constrained environments.") ?
            [
                //A few the same
                "Implement FFT for audio processing, enabling efficient sound manipulation on limited systems.",
                "Use behavior trees in game AI for dynamic decision-making, offering a more efficient alternative to finite state machines.",
                //A few different
                "Employ multi-octave Perlin noise for detailed terrain generation, enhancing realism in procedural content.",
                "Implement a custom memory allocator for optimized memory usage in game development.",
                "Use Voronoi diagrams to create intricate dungeon layouts or creature patterns in game design",
            ] : [
                "Improve dithering patterns with error diffusion for higher quality images in constrained environments.",
                "Implement FFT for audio processing, enabling efficient sound manipulation on limited systems.",
                "Use behavior trees in game AI for dynamic decision-making, offering a more efficient alternative to finite state machines.",
                "Enhance pathfinding efficiency in games using A* with jump point optimization for quicker route calculations.",
                "Utilize Thumb mode and DMA for faster data transfers, reducing CPU load and enhancing performance on the Nintendo DS.",
            ];
    }

    public async Task<List<string>> EvaluateAsync(List<string> dislikes, List<string> pastDiscoveries, List<string> pendingDiscoveries, OutputHandler output)
    {
        // Simulate evaluation
        await Task.Delay(1000);
        return pendingDiscoveries.Where(p => !pastDiscoveries.Any(d => p.Contains(d))).ToList();
    }

    public async Task<List<string>> CompactAsync(List<string> pastDiscoveries, OutputHandler output)
    {
        // Simulate compaction
        await Task.Delay(1000);
        return pastDiscoveries.Distinct().ToList();
    }
}
