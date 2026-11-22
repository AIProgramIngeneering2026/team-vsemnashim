using System.Collections.Immutable;
using System.Text.Json;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.MatchAnalysis.Metadata;

public sealed record SnapshotPlayer(int Slot, string AccountId, int HeroId, int Team);

public sealed class ValidatedMatchSnapshot
{
    public string SchemaVersion => "1";
    public string Source => "deadlock-api.com";
    public string MatchId { get; }
    public DateTimeOffset FetchedAt { get; }
    public double Duration { get; }
    public JsonElement Raw { get; }
    public ImmutableArray<SnapshotPlayer> Players { get; }

    private ValidatedMatchSnapshot(string id, DateTimeOffset fetchedAt, double duration, JsonElement raw,
        ImmutableArray<SnapshotPlayer> players)
    {
        MatchId = id;
        FetchedAt = fetchedAt;
        Duration = duration;
        Raw = raw.Clone();
        Players = players;
    }

    public static Result<ValidatedMatchSnapshot> Read(ReadOnlyMemory<byte> bytes, string expectedId, DateTimeOffset now)
    {
        if (bytes.Length > 32 * 1024 * 1024)
            return Result<ValidatedMatchSnapshot>.Fail(new("SOURCE_RESPONSE_TOO_LARGE", "Превышен лимит снимка."));
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (HasDuplicateKeys(root))
                return Invalid("body", "duplicate_key");
            var info = root.Field("match_info");
            if (info.ValueKind != JsonValueKind.Object)
                return Invalid("/match_info", "expected_object");
            var id = info.Field("match_id").Id();
            if (id is null || id == "0" || id != expectedId)
                return Invalid("/match_info/match_id", "id_mismatch");
            if (info.Field("duration_s").NonNegativeNumber() is not double duration || duration == 0)
                return Invalid("/match_info/duration_s", "expected_positive_duration");
            var players = info.Field("players");
            if (players.ValueKind != JsonValueKind.Array || players.GetArrayLength() is < 1 or > 64)
                return Invalid("/match_info/players", "invalid_player_count");

            var roster = ImmutableArray.CreateBuilder<SnapshotPlayer>();
            var slots = new HashSet<int>();
            foreach (var player in players.EnumerateArray())
            {
                string path = $"/match_info/players/{roster.Count}";
                if (player.Field("player_slot").NonNegativeInt() is not int slot || !slots.Add(slot))
                    return Invalid(path + "/player_slot", "invalid_or_duplicate_slot");
                if (player.Field("account_id").Id() is not string accountId)
                    return Invalid(path + "/account_id", "invalid_id");
                if (player.Field("hero_id").NonNegativeInt() is not int hero)
                    return Invalid(path + "/hero_id", "invalid_id");
                if (player.Field("team").NonNegativeInt() is not int team || team > 1)
                    return Invalid(path + "/team", "invalid_team");
                roster.Add(new(slot, accountId, hero, team));
            }
            return Result<ValidatedMatchSnapshot>.Ok(new(id, now, duration, root, roster.ToImmutable()));
        }
        catch (JsonException)
        {
            return Invalid("body", "invalid_json");
        }
    }

    private static bool HasDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(HasDuplicateKeys);
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(p => !seen.Add(p.Name) || HasDuplicateKeys(p.Value));
    }

    private static Result<ValidatedMatchSnapshot> Invalid(string field, string reason) =>
        Result<ValidatedMatchSnapshot>.Fail(Error.Field("SOURCE_DATA_INVALID", "source", field, reason));
}
