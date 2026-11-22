using System.Text;
using System.Text.Json.Nodes;
using FairfaxStat.Core.Execution;
using FairfaxStat.Core.MatchAnalysis.Metadata;
using FairfaxStat.Core.MatchAnalysis.Reports;

namespace FairfaxStat.Core.Tests.MatchAnalysis;

public sealed class BuildMatchReportTests
{
    private const string Sample = """
        {"match_info":{"match_id":"7","duration_s":120,"players":[{"player_slot":2,"hero_id":76,"account_id":0,"team":0,
          "death_details":[],"items":[{"item_id":381961617,"game_time_s":10,"sold_time_s":0,"flags":0,"upgrade_id":1}],
          "stats":[{"time_stamp_s":30,"net_worth":10,"hero_damage_total":25}]}],"objectives":[]}}
        """;

    private static async Task<MatchReport> Build(Action<JsonObject>? edit = null)
    {
        var root = JsonNode.Parse(Sample)!.AsObject();
        edit?.Invoke(root["match_info"]!.AsObject());
        var snapshot = ValidatedMatchSnapshot.Read(Encoding.UTF8.GetBytes(root.ToJsonString()), "7", DateTimeOffset.UnixEpoch).Value;
        var context = new ToolContext(Guid.NewGuid(), 1, [BuildMatchReportTool.Key],
            DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System);
        return (await new BuildMatchReportTool().RunAsync(new(snapshot, null), context, TestContext.Current.CancellationToken)).Value;
    }

    [Fact]
    public async Task Missing_labels_keep_ids_and_zero_sale_time_never_creates_a_sale()
    {
        var report = await Build();
        Assert.Null(Assert.Single(report.Players).HeroName);
        Assert.Equal(76, report.Players[0].HeroId);
        var item = Assert.Single(report.Sections["items"].Data!.Value.EnumerateArray());
        Assert.Equal("381961617", item.GetProperty("item_id").GetString());
        Assert.Equal("purchase", item.GetProperty("action").GetString());
        Assert.Contains(report.Warnings, w => w.Code == "LABEL_UNAVAILABLE");
        Assert.Empty(report.Sections["deaths"].Data!.Value.EnumerateArray());
        Assert.Equal("unavailable", report.Sections["trajectories"].Availability);
        Assert.Null(report.Players[0].Kills);
    }

    [Fact]
    public async Task Uncertain_item_flags_do_not_become_purchases()
    {
        var report = await Build(i => i["players"]![0]!["items"]![0]!["flags"] = 128);
        Assert.Equal("unsupported", report.Sections["items"].Availability);
        Assert.Null(report.Sections["items"].Data);
        Assert.Equal("available", report.Sections["economy"].Availability);
    }

    [Fact]
    public async Task Unequal_path_axes_only_invalidate_trajectories()
    {
        var report = await Build(i => i["match_paths"] = JsonNode.Parse("""
            {"version":1,"interval_s":1,"x_resolution":16,"y_resolution":16,
             "paths":[{"player_slot":2,"x_pos":[1,2],"y_pos":[1]}]}
            """));
        Assert.Equal("invalid", report.Sections["trajectories"].Availability);
        Assert.Equal("available", report.Sections["items"].Availability);
        Assert.Contains(report.Warnings, w => w.Field == "/sections/trajectories");
    }

    [Fact]
    public async Task Damage_matrix_without_an_identified_time_series_is_unsupported()
    {
        var report = await Build(info =>
        {
            info["players"]![0]!["stats"]![0]!.AsObject().Remove("hero_damage_total");
            info["damage_matrix"] = new JsonObject();
        });
        Assert.Equal("unsupported", report.Sections["damage"].Availability);
        Assert.Null(report.Sections["damage"].Data);
    }

    [Fact]
    public async Task Oversized_section_is_reported_without_truncating_or_losing_the_summary()
    {
        var report = await Build(info =>
        {
            var tooMany = new JsonArray();
            for (var i = 0; i <= 20_000; i++)
                tooMany.Add(new JsonObject { ["destroyed_time_s"] = 15, ["team_objective_id"] = i });
            info["objectives"] = tooMany;
        });
        Assert.Equal("invalid", report.Sections["objectives"].Availability);
        Assert.Null(report.Sections["objectives"].Data);
        Assert.Contains(report.Warnings, w => w.Code == "OPTIONAL_DATA_TOO_LARGE" && w.Field == "/sections/objectives");
        Assert.Single(report.Players);
    }

    [Fact]
    public async Task Conflicting_samples_and_missing_blocks_keep_distinct_statuses()
    {
        var report = await Build(info =>
        {
            var player = info["players"]![0]!.AsObject();
            player.Remove("death_details");
            player["stats"]!.AsArray().Add(JsonNode.Parse("""{"time_stamp_s":30,"net_worth":12,"hero_damage_total":25}"""));
        });
        Assert.Equal("invalid", report.Sections["economy"].Availability);
        Assert.Equal("unavailable", report.Sections["deaths"].Availability);
        var damage = Assert.Single(report.Sections["damage"].Data!.Value.EnumerateArray());
        Assert.Equal(25, damage.GetProperty("hero_damage_total").GetDouble());
    }
}
