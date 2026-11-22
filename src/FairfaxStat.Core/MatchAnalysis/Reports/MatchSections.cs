using System.Collections.Immutable;
using System.Text.Json;
using FairfaxStat.Core.MatchAnalysis.Metadata;

namespace FairfaxStat.Core.MatchAnalysis.Reports;

internal sealed partial class MatchSections(BuildReportInput input, ReportWarnings warnings, CancellationToken cancellation)
{
    private const int MaxRows = 20_000;
    private readonly JsonElement info = input.Snapshot.Raw.GetProperty("match_info");
    private readonly HashSet<int> slots = input.Snapshot.Players.Select(p => p.Slot).ToHashSet();
    private double? Time(JsonElement value) => value.NonNegativeNumber() is double seconds &&
        seconds <= input.Snapshot.Duration ? seconds : null;

    internal ImmutableSortedDictionary<string, ReportSection> Read() =>
        new Dictionary<string, ReportSection>
        {
            ["items"] = PlayerSeries<ItemRow>("items", "items", AddItem, rows => rows.OrderBy(r => r.TimeS)
                .ThenBy(r => r.PlayerSlot).ThenBy(r => ulong.Parse(r.ItemId)).ThenBy(r => r.Action, StringComparer.Ordinal)),
            ["deaths"] = PlayerSeries<DeathRow>("deaths", "death_details", AddDeath,
                rows => rows.OrderBy(r => r.TimeS).ThenBy(r => r.VictimSlot).ThenBy(r => r.KillerSlot)),
            ["objectives"] = Objectives(),
            ["economy"] = StatSeries("economy", "net_worth"),
            ["damage"] = StatSeries("damage", "hero_damage_total"),
            ["trajectories"] = Paths()
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private ReportSection PlayerSeries<T>(string section, string field,
        Func<JsonElement, int, List<T>, SectionIssue?> add, Func<IEnumerable<T>, IEnumerable<T>> sort)
    {
        var output = new List<T>();
        foreach (var player in info.GetProperty("players").EnumerateArray())
        {
            int slot = player.GetProperty("player_slot").GetInt32();
            var entries = player.Field(field);
            if (ArrayIssue(entries) is { } issue)
                return issue.Describe(section, warnings);
            foreach (var entry in entries.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                if (add(entry, slot, output) is { } problem)
                    return problem.Describe(section, warnings);
                if (output.Count > MaxRows)
                    return SectionIssue.TooLarge.Describe(section, warnings);
            }
        }
        return Available(sort(output.Distinct()));
    }

    private static SectionIssue? ArrayIssue(JsonElement field) => field.ValueKind switch
    {
        JsonValueKind.Array => null,
        JsonValueKind.Undefined or JsonValueKind.Null => SectionIssue.Missing,
        _ => SectionIssue.Invalid
    };

    private static ReportSection Available<T>(IEnumerable<T> rows) =>
        new("available", null, JsonSerializer.SerializeToElement(rows, MatchReport.Json));

    private sealed record ItemRow(int PlayerSlot, double TimeS, string Action, string ItemId, string? ItemName);
    private sealed record DeathRow(double TimeS, int VictimSlot, int? KillerSlot);
    private sealed record ObjectiveRow(double TimeS, string ObjectiveId, int? Team);

    private SectionIssue? AddItem(JsonElement row, int slot, List<ItemRow> output)
    {
        if (row.Field("item_id").Id() is not string id || Time(row.Field("game_time_s")) is not double bought ||
            row.Field("flags").NonNegativeInt() is not int flags || row.Field("upgrade_id").Id() is not string upgrade)
            return SectionIssue.Invalid;
        if (flags is not (0 or 1))
            return SectionIssue.Unsupported;
        // The v1 fixture distinguishes shop items (upgrade_id 1) from ability unlocks and upgrades.
        if (upgrade != "1")
            return SectionIssue.Unsupported;
        if (Time(row.Field("sold_time_s")) is not double sold || (sold > 0 && sold < bought))
            return SectionIssue.Invalid;
        string? name = input.Catalog?.Items.GetValueOrDefault(id);
        if (name is null)
            warnings.Add("LABEL_UNAVAILABLE", "/sections/items", "В справочнике нет подписи предмета.");
        output.Add(new(slot, bought, "purchase", id, name));
        if (sold > 0)
            output.Add(new(slot, sold, "sale", id, name));
        return null;
    }

    private SectionIssue? AddDeath(JsonElement row, int slot, List<DeathRow> output)
    {
        if (Time(row.Field("game_time_s")) is not double time)
            return SectionIssue.Invalid;
        var killer = row.Field("killer_player_slot").NonNegativeInt();
        output.Add(new(time, slot, killer is int k && slots.Contains(k) ? k : null));
        return null;
    }

    private ReportSection Objectives()
    {
        const string section = "objectives";
        var data = info.Field(section);
        if (ArrayIssue(data) is { } issue)
            return issue.Describe(section, warnings);
        if (data.GetArrayLength() > MaxRows)
            return SectionIssue.TooLarge.Describe(section, warnings);
        var rows = new List<ObjectiveRow>();
        foreach (var entry in data.EnumerateArray())
        {
            var id = entry.Field("team_objective_id").Id() ?? entry.Field("legacy_objective_id").Id();
            if (Time(entry.Field("destroyed_time_s")) is not double time || id is null)
                return SectionIssue.Invalid.Describe(section, warnings);
            var team = entry.Field("team").NonNegativeInt();
            rows.Add(new(time, id, team is 0 or 1 ? team : null));
        }
        return Available(rows.Distinct().OrderBy(r => r.TimeS).ThenBy(r => r.ObjectiveId, StringComparer.Ordinal).ThenBy(r => r.Team));
    }
}
