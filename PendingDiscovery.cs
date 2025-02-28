namespace TrippinEdi;

internal class PendingDiscovery
{
    public int Id { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; }
}
