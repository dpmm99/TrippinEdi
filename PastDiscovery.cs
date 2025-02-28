namespace TrippinEdi;

internal class PastDiscovery
{
    public int Id { get; set; }
    public required string Text { get; set; }
    public string? CompactedText { get; set; }
    public DateTime DiscoveredAt { get; set; }
}
