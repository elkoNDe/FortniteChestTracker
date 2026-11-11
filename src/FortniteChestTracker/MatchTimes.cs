namespace FortniteChestTracker;

/// <summary>
/// Fortnite's UtcTimeStartedMatch is UTC, but Unreal.Core.FDateTime reads
/// ticks with DateTimeKind.Unspecified. Do not let ToUniversalTime()
/// mistakenly interpret those UTC ticks as the PC's local time zone.
/// </summary>
public static class MatchTimes
{
    public static DateTime FromReplayUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// Restores a time imported by old versions that incorrectly applied
    /// ToUniversalTime() to UTC replay ticks carrying Unspecified kind.
    /// This is best-effort based on the current machine's time zone.
    /// Original values remain backed up in SQLite.
    /// </summary>
    public static DateTime RepairLegacyUtc(DateTime oldStoredUtc, TimeZoneInfo zone)
    {
        var utc = FromReplayUtc(oldStoredUtc);
        // Previous code mistakenly did: local-clock UTC ticks -> ToUniversalTime().
        // Its inverse normally is: erroneous UTC -> local clock, re-tagged as UTC.
        var originalUtcClock = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return DateTime.SpecifyKind(originalUtcClock, DateTimeKind.Utc);
    }
}
