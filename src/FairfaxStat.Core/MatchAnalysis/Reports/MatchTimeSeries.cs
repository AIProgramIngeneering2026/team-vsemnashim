using System.Text.Json;
using FairfaxStat.Core.MatchAnalysis.Metadata;

namespace FairfaxStat.Core.MatchAnalysis.Reports;

internal sealed partial class MatchSections
{
    private ReportSection StatSeries(string section, string field)
    {
        var points = new SortedDictionary<(int Slot, double Time), double>();
        foreach (var player in info.GetProperty("players").EnumerateArray())
        {
            int slot = player.GetProperty("player_slot").GetInt32();
            var stats = player.Field("stats");
            if (ArrayIssue(stats) is { } issue)
                return issue.Describe(section, warnings);
            foreach (var row in stats.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                var raw = row.Field(field);
                if (raw.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                {
                    var fault = section == "damage" && info.Field("damage_matrix").ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        ? SectionIssue.Unsupported : SectionIssue.Missing;
                    return fault.Describe(section, warnings);
                }
                double? amount = section == "economy" ? raw.NonNegativeInt() : raw.NonNegativeNumber();
                if (amount is null || Time(row.Field("time_stamp_s")) is not double time)
                    return SectionIssue.Invalid.Describe(section, warnings);
                var key = (slot, time);
                if (points.TryGetValue(key, out var prior) && prior != amount)
                    return SectionIssue.Invalid.Describe(section, warnings);
                points[key] = amount.Value;
                if (points.Count > MaxRows)
                    return SectionIssue.TooLarge.Describe(section, warnings);
            }
        }
        return section == "economy"
            ? Available(points.Select(p => new { player_slot = p.Key.Slot, time_s = p.Key.Time, net_worth = (int)p.Value }))
            : Available(points.Select(p => new { player_slot = p.Key.Slot, time_s = p.Key.Time, hero_damage_total = p.Value }));
    }

    private ReportSection Paths()
    {
        const string section = "trajectories";
        var block = info.Field("match_paths");
        if (block.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return SectionIssue.Missing.Describe(section, warnings);
        var paths = block.Field("paths");
        if (block.ValueKind != JsonValueKind.Object || block.Field("version").NonNegativeInt() is not int version)
            return SectionIssue.Invalid.Describe(section, warnings);
        if (version != 1)
            return SectionIssue.Unsupported.Describe(section, warnings);
        if (ArrayIssue(paths) is { } issue)
            return issue.Describe(section, warnings);
        if (block.Field("interval_s").NonNegativeNumber() is not > 0 ||
            block.Field("x_resolution").NonNegativeInt() is not > 0 || block.Field("y_resolution").NonNegativeInt() is not > 0)
            return SectionIssue.Invalid.Describe(section, warnings);
        if (paths.GetArrayLength() > 64)
            return SectionIssue.TooLarge.Describe(section, warnings);
        if (paths.GetArrayLength() == 0)
            return Available(Array.Empty<object>());

        var seen = new HashSet<int>();
        foreach (var path in paths.EnumerateArray())
        {
            var x = path.Field("x_pos");
            var y = path.Field("y_pos");
            if (path.Field("player_slot").NonNegativeInt() is not int slot || !slots.Contains(slot) || !seen.Add(slot) ||
                x.ValueKind != JsonValueKind.Array || y.ValueKind != JsonValueKind.Array || x.GetArrayLength() != y.GetArrayLength())
                return SectionIssue.Invalid.Describe(section, warnings);
            if (x.GetArrayLength() > MaxRows)
                return SectionIssue.TooLarge.Describe(section, warnings);
            if (!ValidAxis(path, "x", x, block) || !ValidAxis(path, "y", y, block))
                return SectionIssue.Invalid.Describe(section, warnings);
        }
        if (!seen.SetEquals(slots))
            return SectionIssue.Missing.Describe(section, warnings);
        // A reference transformation to world X/Y has not been verified for this source version.
        return SectionIssue.Unsupported.Describe(section, warnings);
    }

    private static bool ValidAxis(JsonElement path, string axis, JsonElement samples, JsonElement block)
    {
        var min = path.Field(axis + "_min");
        var max = path.Field(axis + "_max");
        if (min.ValueKind != JsonValueKind.Number || max.ValueKind != JsonValueKind.Number ||
            !min.TryGetDouble(out var low) || !max.TryGetDouble(out var high) ||
            !double.IsFinite(low) || !double.IsFinite(high) || low > high)
            return false;
        var resolution = block.GetProperty(axis + "_resolution").GetInt32();
        return samples.EnumerateArray().All(s => s.NonNegativeInt() is int v && v <= resolution);
    }
}
