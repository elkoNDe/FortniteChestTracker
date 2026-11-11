namespace FortniteChestTracker;

public sealed record ChestEvent(double Second, string Kind, string Blueprint, bool Destroyed);

public sealed record MatchResult(
    string SessionId,
    DateTime StartedUtc,
    string PlayerName,
    string? EpicId,
    string Playlist,
    int Chests,
    int Destroyed,
    int AmmoBoxes,
    int OtherContainers,
    IReadOnlyList<ChestEvent> Events)
{
    // A separate optional breakdown. Owner counts above remain the only source
    // of personal totals, records, averages and charts.
    public IReadOnlyList<TeamMemberResult> TeamMembers { get; init; } = Array.Empty<TeamMemberResult>();
    public int? Placement { get; init; } = null;
}

public sealed record TeamMemberResult(
    string PlayerKey, string PlayerName, bool IsOwner,
    int? Chests, int? Destroyed, int? AmmoBoxes);

/// <summary>Only the four standard Zero Build Battle Royale playlists are counted.</summary>
public static class PlaylistRules
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Playlist_NoBuildBR_Solo",
        "Playlist_NoBuildBR_Duo",
        "Playlist_NoBuildBR_Trio",
        "Playlist_NoBuildBR_Squad"
    };

    public static bool IsAllowed(string? playlist) =>
        playlist is not null && Allowed.Contains(playlist.Trim());
}

public static class ChestTypes
{
    public static string Classify(string blueprint)
    {
        var name = blueprint.ToLowerInvariant();
        if (name.Contains("ammo")) return "Municion";
        if (name.Contains("food") || name.Contains("cooler")) return "Comida";
        if (name.Contains("cannon")) return "Canon";
        if (name.Contains("rarechest") || name.Contains("legendary") || name.Contains("rare_chest")) return "Raro";
        if (name.Contains("buried")) return "Enterrado";
        if (name.Contains("relic") || name.Contains("extraction")) return "Reliquias";
        if (name.Contains("spriteposter") || name.Contains("booty")) return "Especial";
        if (name.Contains("chest")) return "Normal";
        return "Otro";
    }

    public static bool IsChest(string kind) => kind is "Raro" or "Enterrado" or "Reliquias" or "Especial" or "Normal";
}

public static class Paths
{
    public static string DefaultDemos => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");

    public static string DefaultData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteChestTracker");
}
