using System.Text.RegularExpressions;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Items;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteChestTracker;

/// <summary>
/// Extrae interacciones del propietario de la repeticion. No accede al proceso ni a la memoria de Fortnite.
/// Parte de las tecnicas para NetFieldExports estan adaptadas de LED-N/Fortnite-ChestCounter (MIT).
/// </summary>
public sealed class ReplayAnalyzer : FortniteReplayReader.ReplayReader
{
    private sealed record Search(double At, string Key, string Label, uint? Pawn, bool Destroyed, bool Stale);

    private readonly Dictionary<uint, string> _keys = new();
    private readonly Dictionary<uint, string> _labels = new();
    private readonly Dictionary<uint, double> _openedAt = new();
    private readonly Dictionary<uint, bool> _searchedChannels = new();
    private readonly Dictionary<uint, uint> _searchingPawns = new();
    private readonly Dictionary<string, uint?> _lastOpener = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<Search> _events = new();
    private readonly Dictionary<uint, uint> _pawnToState = new();
    private readonly Dictionary<uint, int> _stateToPlayer = new();
    private static readonly Regex ActorSuffix = new(@"_UAID_.*$", RegexOptions.Compiled);

    static ReplayAnalyzer()
    {
        NetFieldParser.AliasResolver = path =>
        {
            if (path.Contains(':') || path.EndsWith("_ClassNetCache", StringComparison.Ordinal)) return null;
            var name = path[(path.LastIndexOf('/') + 1)..];
            return path.Contains("/Containers/", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                ? GenericContainer.GroupPath : null;
        };
    }

    public ReplayAnalyzer() : base(parseMode: ParseMode.Normal) { }

    private static double Time => Unreal.Core.ReplayReader<FortniteReplay>.CurrentTime;

    private string PathOf(uint? guid) => guid is uint g && g != 0 && _netGuidCache.TryGetPathName(g, out var path) ? path : "";

    private static string Clean(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return ActorSuffix.Replace(dot >= 0 ? name[(dot + 1)..] : name, "");
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelOpened(channelIndex, actor);
        var channel = Channels[channelIndex];
        var actorPath = PathOf(channel?.ActorId);
        var classPath = PathOf(channel?.ArchetypeId);
        var label = Clean(string.IsNullOrEmpty(classPath) ? actorPath : classPath);
        _keys[channelIndex] = !string.IsNullOrEmpty(actorPath) ? actorPath : $"channel:{channel?.ActorId}:{channelIndex}";
        _labels[channelIndex] = label;
        _openedAt[channelIndex] = Time;
        _searchedChannels.Remove(channelIndex);
        _searchingPawns.Remove(channelIndex);
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);
        switch (exportGroup)
        {
            case PlayerPawn pawn when pawn.PlayerState is uint state && state != 0:
                if (Channels[channelIndex]?.ActorId is uint pawnGuid)
                    _pawnToState[pawnGuid] = state;
                break;
            case FortPlayerState state:
                if (Channels[channelIndex]?.ActorId is uint stateGuid && (state.PlayerId is not null || state.PlayerID is not null))
                    _stateToPlayer[stateGuid] = state.PlayerId is uint pid ? (int)pid : state.PlayerID!.Value;
                break;
            case BaseContainer container:
                Track(channelIndex, container);
                break;
        }
    }

    private void Track(uint channelIndex, BaseContainer container)
    {
        if (container.SearchingPawn is uint searcher && searcher != 0)
            _searchingPawns[channelIndex] = searcher;

        var key = _keys.GetValueOrDefault(channelIndex, $"channel:{channelIndex}");
        var label = _labels.GetValueOrDefault(channelIndex, "");
        var stale = Time - _openedAt.GetValueOrDefault(channelIndex, -10) < 0.5;
        var searched = _searchedChannels.GetValueOrDefault(channelIndex);
        if (container.bAlreadySearched == false)
        {
            _searchedChannels[channelIndex] = false; // se ha repuesto el contenido
            return;
        }
        if (container.bAlreadySearched == true && !searched)
        {
            _searchedChannels[channelIndex] = true;
            var opener = _searchingPawns.TryGetValue(channelIndex, out var pawn) ? pawn : (uint?)null;
            // Si el actor acaba de aparecer y ya estaba abierto, no inventamos una interaccion.
            var previouslySeen = stale && _lastOpener.TryGetValue(key, out var previous) && previous == opener;
            if (!previouslySeen)
            {
                _seen.Add(key);
                _lastOpener[key] = opener;
                _events.Add(new Search(Time, key, label, opener, false, stale));
            }
            return;
        }
        if (container.bDestroyed && container.SearchingPawn is uint destroyer && destroyer != 0 &&
            (_seen.Add(key) || !searched))
        {
            _searchedChannels[channelIndex] = true;
            _lastOpener[key] = destroyer;
            _events.Add(new Search(Time, key, label, destroyer, true, stale));
        }
    }

    public MatchResult Analyze(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var replay = ReadReplay(stream);
        var recorderState = replay.GameData?.RecorderId;
        if (recorderState is null || recorderState == 0)
            throw new InvalidDataException("No se ha podido identificar al propietario de la repeticion.");

        // Solo el jugador que grabo la repeticion; nunca contamos cofres de rivales o companeros.
        var targetPawns = _pawnToState.Where(p => p.Value == recorderState.Value).Select(p => p.Key).ToHashSet();
        var playerId = _stateToPlayer.GetValueOrDefault(recorderState.Value, -1);
        var player = replay.PlayerData?.FirstOrDefault(p => p.Id == playerId);
        if (targetPawns.Count == 0)
            throw new InvalidDataException("No se pudo vincular el personaje con el propietario de la repeticion.");
        var events = _events
            .Where(e => e.Pawn is uint pawn && targetPawns.Contains(pawn) && (!e.Stale || !e.Key.StartsWith("channel:", StringComparison.Ordinal)))
            .OrderBy(e => e.At)
            .Select(e => new ChestEvent(e.At, ChestTypes.Classify(e.Label), e.Label, e.Destroyed))
            .ToList();
        var chests = events.Where(e => ChestTypes.IsChest(e.Kind)).ToArray();
        var playlist = replay.GameData?.CurrentPlaylist ?? "";
        if (string.IsNullOrWhiteSpace(playlist) && chests.Length == 0)
            throw new InvalidDataException("La repeticion no parece ser Battle Royale o no contiene eventos reconocibles.");
        var sessionId = replay.GameData?.GameSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidDataException("No se ha recibido el identificador de partida. No se guarda para evitar duplicados.");
        var started = replay.GameData?.UtcTimeStartedMatch ?? replay.Info?.Timestamp ?? DateTime.UtcNow;
        // UtcTimeStartedMatch arrives with Kind=Unspecified even though its ticks are
        // UTC. ToUniversalTime() treated them as local and shifted matches by 1/2h.
        var playerName = string.IsNullOrWhiteSpace(player?.PlayerName) ? "Jugador" : player.PlayerName;
        var breakdown = new List<TeamMemberResult>
        {
            new("owner", playerName, true,
                chests.Length, chests.Count(e => e.Destroyed),
                events.Count(e => e.Kind == "Municion"))
        };

        // The replay captures an in-game team, not necessarily the player's
        // social lobby/party. Only teammates with the same positive TeamIndex
        // are considered; enemy chests never contribute to this breakdown.
        if (player?.Id is int ownerId && replay.PlayerData is not null)
        {
            var candidates = replay.PlayerData
                .Where(p => p.Id is int)
                .Select(p => new SquadCandidate(
                    p.Id!.Value,
                    p.TeamIndex,
                     $"player:{p.Id.Value}",
                    string.IsNullOrWhiteSpace(p.PlayerName) ? $"Jugador {p.Id.Value}" : p.PlayerName))
                .ToArray();
            var ownerCandidate = candidates.FirstOrDefault(p => p.PlayerId == ownerId);
            if (ownerCandidate is not null)
            {
                var observations = _events
                    .Where(e => e.Pawn is uint &&
                        (!e.Stale || !e.Key.StartsWith("channel:", StringComparison.Ordinal)))
                    .Select(e => new SquadOpening(e.Pawn, ChestTypes.Classify(e.Label), e.Destroyed))
                    .ToArray();
                breakdown.AddRange(SquadAttribution.DetectTeammates(
                    ownerCandidate, candidates, _pawnToState, _stateToPlayer, observations));
            }
        }

        // PlayerData.Placement can be missing; TeamStats.Position is the same
        // fallback used by the upstream replay reader for the recorder.
        int? placement = player?.Placement ?? (int?)replay.TeamStats?.Position;
        if (placement is <= 0 or > 100) placement = null;
        return new MatchResult(sessionId, MatchTimes.FromReplayUtc(started), playerName, player?.EpicId,
            playlist, chests.Length, chests.Count(e => e.Destroyed), events.Count(e => e.Kind == "Municion"),
            events.Count(e => !ChestTypes.IsChest(e.Kind) && e.Kind != "Municion"), chests)
        { TeamMembers = breakdown, Placement = placement };
    }
}
