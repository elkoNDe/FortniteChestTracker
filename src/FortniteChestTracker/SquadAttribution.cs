namespace FortniteChestTracker;

// Observations from a replay, not guaranteed complete for other characters.
// Null counts mean the player's character could not be linked to a pawn;
// they are NOT interchangeable with zero recorded openings.
public sealed record SquadCandidate(int PlayerId, int? TeamIndex, string Key, string Name);
public sealed record SquadOpening(uint? Pawn, string Kind, bool Destroyed);

public static class SquadAttribution
{
    public static IReadOnlyList<TeamMemberResult> DetectTeammates(
        SquadCandidate owner,
        IEnumerable<SquadCandidate> players,
        IReadOnlyDictionary<uint, uint> pawnToState,
        IReadOnlyDictionary<uint, int> stateToPlayer,
        IEnumerable<SquadOpening> openings)
    {
        if (owner.TeamIndex is not int team || team <= 0)
            return Array.Empty<TeamMemberResult>();

        // Map only uniquely resolved pawn -> player links. If multiple identities
        // claim the same pawn, do not invent an attribution.
        var pawnToPlayer = new Dictionary<uint, int>();
        foreach (var (pawn, state) in pawnToState)
            if (stateToPlayer.TryGetValue(state, out var playerId))
                pawnToPlayer[pawn] = playerId;

        var buckets = openings
            .Where(e => e.Pawn is uint && pawnToPlayer.ContainsKey(e.Pawn.Value))
            .GroupBy(e => pawnToPlayer[e.Pawn!.Value])
            .ToDictionary(g => g.Key, g => g.ToArray());

        var linked = pawnToPlayer.Values.ToHashSet();
        return players
            .Where(p => p.TeamIndex == team && p.PlayerId != owner.PlayerId)
            .GroupBy(p => p.PlayerId)
            .Select(g => g.First())
            .Select(p =>
            {
                if (!linked.Contains(p.PlayerId))
                    return new TeamMemberResult(p.Key, p.Name, false, null, null, null);
                var found = buckets.GetValueOrDefault(p.PlayerId, Array.Empty<SquadOpening>());
                return new TeamMemberResult(p.Key, p.Name, false,
                    found.Count(e => ChestTypes.IsChest(e.Kind)),
                    found.Count(e => ChestTypes.IsChest(e.Kind) && e.Destroyed),
                    found.Count(e => e.Kind == "Municion"));
            })
            .OrderBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
