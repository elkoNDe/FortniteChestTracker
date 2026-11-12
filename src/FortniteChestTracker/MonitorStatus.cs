namespace FortniteChestTracker;

// This small immutable snapshot is shared by the tray and the offline HTML.
// The browser compares CheckedUtc with its clock to spot a dead monitor.
public sealed record MonitorStatus(
    string State,
    DateTimeOffset CheckedUtc,
    int PendingCount,
    DateTimeOffset? LastImportedUtc = null,
    string? Detail = null)
{
    public static MonitorStatus Starting() =>
        new("starting", DateTimeOffset.UtcNow, 0);
}
