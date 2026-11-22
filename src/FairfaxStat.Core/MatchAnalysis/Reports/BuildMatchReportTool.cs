using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using FairfaxStat.Core.Execution;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.MatchAnalysis.Metadata;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.MatchAnalysis.Reports;

public sealed class BuildMatchReportTool : ITool<BuildReportInput, MatchReport>
{
    public static readonly ToolKey Key = new("build_match_report", 1);
    public ToolDescriptor Descriptor { get; } = new(Key, nameof(BuildReportInput), nameof(MatchReport),
        TimeSpan.FromSeconds(20), new("PROCESSING_TIMEOUT", "Истёк срок построения отчёта."));

    public ValueTask<Result<MatchReport>> RunAsync(BuildReportInput input, ToolContext context, CancellationToken cancellation)
    {
        if (context.Validate(Key) is { } invalid)
            return ValueTask.FromResult(Result<MatchReport>.Fail(invalid));
        if (input?.Snapshot is null)
            return ValueTask.FromResult(Result<MatchReport>.Fail(Error.Field("TOOL_INPUT_INVALID", "job", "/snapshot", "missing")));
        return ValueTask.FromResult(Build(input, context.JobId, cancellation));
    }

    private static Result<MatchReport> Build(BuildReportInput input, Guid jobId, CancellationToken cancellation)
    {
        var snapshot = input.Snapshot;
        var info = snapshot.Raw.GetProperty("match_info");
        var warnings = new ReportWarnings();
        var players = ImmutableArray.CreateBuilder<ReportPlayer>();
        var rawPlayers = info.GetProperty("players");
        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            var player = snapshot.Players[i];
            var raw = rawPlayers[i];
            var heroName = input.Catalog?.Heroes.GetValueOrDefault(player.HeroId.ToString(CultureInfo.InvariantCulture));
            if (heroName is null)
                warnings.Add("LABEL_UNAVAILABLE", "/players", "В справочнике нет подписи героя.");
            int? Stat(string field)
            {
                var number = raw.Field(field);
                int? value = number.NonNegativeInt();
                if (value is null)
                    warnings.Add(number.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        ? "OPTIONAL_DATA_MISSING" : "OPTIONAL_DATA_INVALID", "/players", "Часть показателей игроков недоступна.");
                return value;
            }
            players.Add(new(player.Slot, player.AccountId, player.HeroId, heroName, player.Team,
                Stat("kills"), Stat("deaths"), Stat("assists"), Stat("net_worth")));
        }
        var winning = info.Field("winning_team");
        var team = winning.NonNegativeInt();
        if (team is not (0 or 1))
        {
            team = null;
            warnings.Add(winning.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? "OPTIONAL_DATA_MISSING" : "OPTIONAL_DATA_INVALID", "/winning_team", "Победитель не определён.");
        }

        var sections = new MatchSections(input, warnings, cancellation).Read();
        var report = new MatchReport("1.0", jobId, snapshot.MatchId, new(snapshot.Source, snapshot.FetchedAt.UtcDateTime),
            snapshot.Duration, team, [.. players.OrderBy(p => p.PlayerSlot)], sections, warnings.ToArray());
        foreach (string name in new[] { "trajectories", "damage", "economy", "items", "deaths", "objectives" })
        {
            if (Fits(report))
                return Result<MatchReport>.Ok(report);
            if (report.Sections[name].Data is not null)
                report = report with { Sections = report.Sections.SetItem(name, SectionIssue.TooLarge.Describe(name, warnings)),
                    Warnings = warnings.ToArray() };
        }
        return Fits(report) ? Result<MatchReport>.Ok(report) :
            Result<MatchReport>.Fail(new("RESULT_TOO_LARGE", "Обязательные данные отчёта превышают лимит."));

        static bool Fits(MatchReport report) =>
            JsonSerializer.SerializeToUtf8Bytes(report, MatchReport.Json).Length <= 16 * 1024 * 1024;
    }

    public bool AcceptsOutput(BuildReportInput input, MatchReport output, ToolContext context) =>
        output.JobId == context.JobId && output.MatchId == input.Snapshot.MatchId &&
        output.DurationS == input.Snapshot.Duration && output.Players.Length == input.Snapshot.Players.Length &&
        output.Sections.Count == 6;
}
