namespace FortniteChestTracker;

/// <summary>
/// The advertised odds are per eligible chest, independent of previous attempts.
/// A cumulative chance is a historical estimate, never a progress-to-guarantee bar.
/// All times are UTC; the last four Saturdays are projected, not Epic-confirmed.
/// </summary>
public static class TicketOdds
{
    public const int BaseDenominator = 137_500;
    public static readonly DateTimeOffset SeasonStartUtc =
        new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public sealed record TreasureWindow(
        DateTimeOffset StartUtc, DateTimeOffset EndUtc,
        double Multiplier, bool Confirmed);

    // Confirmed: FortniteStatus, Nov 7 2026 (1.5x / 2x).
    // Provisional: the reported Saturday pattern (11am and 6pm PST).
    // These later dates/boosts MUST NOT be presented as official confirmations.
    // To change a new Epic announcement, update just this list and publish a patch.
    private static TreasureWindow At(int month, int day, int hour, double multiplier, bool confirmed)
    {
        var start = new DateTimeOffset(2026, month, day, hour, 0, 0, TimeSpan.Zero);
        return new TreasureWindow(start, start.AddHours(2), multiplier, confirmed);
    }

    public static IReadOnlyList<TreasureWindow> Windows { get; } = Array.AsReadOnly(new[]
    {
        At(11, 7, 19, 1.5, true),
        At(11, 8, 2, 2.0, true),
        At(11, 14, 19, 1.5, false),
        At(11, 15, 2, 1.5, false),
        At(11, 21, 19, 1.5, false),
        At(11, 22, 2, 1.5, false),
        At(11, 28, 19, 1.5, false),
        At(11, 29, 2, 1.5, false),
        At(12, 5, 19, 1.5, false),
        At(12, 6, 2, 1.5, false)
    });

    public sealed record Estimate(
        int OwnChests,
        int PartyChests,
        double OwnPercent,
        double PartyPercent,
        int BoostedOwnChests,
        int ProvisionalOwnChests,
        int ApproximateOwnChests,
        double CurrentMultiplier,
        bool CurrentMultiplierProvisional,
        DateTimeOffset? NextHourUtc,
        double NextHourMultiplier,
        bool NextHourConfirmed);

    public static Estimate Calculate(TrackerSnapshot snapshot, DateTimeOffset? currentTime = null)
    {
        var now = currentTime ?? DateTimeOffset.UtcNow;
        var ownCount = 0;
        var teamCount = 0;
        var boostCount = 0;
        var predictedCount = 0;
        var coarseCount = 0;
        var ownLogSurvival = 0.0;
        var partyLogSurvival = 0.0;

        void AddAttempts(DateTimeOffset utc, int count, bool own, bool approximate)
        {
            if (utc < SeasonStartUtc || count <= 0) return;
            var hour = WindowAt(utc);
            var multiplier = hour?.Multiplier ?? 1.0;
            var logSurvival = count * Math.Log(1.0 - multiplier / BaseDenominator);
            partyLogSurvival += logSurvival;
            teamCount += count;
            if (!own) return;
            ownLogSurvival += logSurvival;
            ownCount += count;
            if (multiplier > 1.0) boostCount += count;
            if (hour is { Confirmed: false }) predictedCount += count;
            if (approximate) coarseCount += count;
        }

        foreach (var match in snapshot.Matches)
        {
            if (!PlaylistRules.IsAllowed(match.Playlist)) continue;
            var started = new DateTimeOffset(MatchTimes.FromReplayUtc(match.Date));
            // The total in the existing dashboard includes destroyed chests.
            // They DO count as an attempt, exactly like ordinary openings.
            // Historical replays can have fewer stored chest_events than their
            // totals. Fall back to match start rather than dropping those chests.
            var remaining = Math.Max(0, match.Chests);
            foreach (var e in match.ChestEvents)
            {
                if (remaining == 0) break;
                if (!ChestTypes.IsChest(e.Kind) || !double.IsFinite(e.Second) ||
                    e.Second < 0 || e.Second > 43_200) continue;
                AddAttempts(started.AddSeconds(e.Second), 1, own: true, approximate: false);
                remaining--;
            }
            AddAttempts(started, remaining, own: true, approximate: true);

            // Teammate timestamps are NOT present in the squad data.
            // We only know observed totals, so match-start bonuses for party
            // are an approximation and must be labelled as such.
            foreach (var teammate in match.TeamMembers.Where(t => !t.IsOwner))
                AddAttempts(started, Math.Max(0, teammate.Chests ?? 0),
                    own: false, approximate: true);
        }

        var current = WindowAt(now);
        var next = Windows.FirstOrDefault(w => w.StartUtc > now);
        return new Estimate(
            ownCount, teamCount,
            Percent(ownLogSurvival), Percent(partyLogSurvival),
            boostCount, predictedCount, coarseCount,
            current?.Multiplier ?? 1.0,
            current is { Confirmed: false },
            next?.StartUtc, next?.Multiplier ?? 1.0, next?.Confirmed ?? false);
    }

    private static TreasureWindow? WindowAt(DateTimeOffset instant) =>
        Windows.FirstOrDefault(w => instant >= w.StartUtc && instant < w.EndUtc);

    private static double Percent(double logSurvival) =>
        Math.Clamp((1.0 - Math.Exp(logSurvival)) * 100.0, 0.0, 100.0);
}
