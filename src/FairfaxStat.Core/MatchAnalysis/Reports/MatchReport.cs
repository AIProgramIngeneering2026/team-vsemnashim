using System.Collections.Immutable;
using System.Text.Json;
using FairfaxStat.Core.MatchAnalysis.Catalog;
using FairfaxStat.Core.MatchAnalysis.Metadata;

namespace FairfaxStat.Core.MatchAnalysis.Reports;

public sealed record BuildReportInput(ValidatedMatchSnapshot Snapshot, CatalogSnapshot? Catalog);
public sealed record ReportPlayer(int PlayerSlot, string AccountId, int HeroId, string? HeroName, int Team,
    int? Kills, int? Deaths, int? Assists, int? NetWorth);
public sealed record ReportSource(string Name, DateTime FetchedAt);
public sealed record ReportWarning(string Code, string Field, string Message);
public sealed record ReportSection(string Availability, string? Reason, JsonElement? Data);
public sealed record MatchReport(string SchemaVersion, Guid JobId, string MatchId, ReportSource Source,
    double DurationS, int? WinningTeam, ImmutableArray<ReportPlayer> Players,
    ImmutableSortedDictionary<string, ReportSection> Sections, ImmutableArray<ReportWarning> Warnings)
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, Json);
}

internal sealed class ReportWarnings
{
    private readonly Dictionary<(string Field, string Code), ReportWarning> warnings = [];
    internal void Add(string code, string field, string message)
    {
        if (warnings.Count < 200)
            warnings.TryAdd((field, code), new(code, field, message));
    }
    internal ImmutableArray<ReportWarning> ToArray() => [.. warnings.Values];
}

internal sealed record SectionIssue(string Status, string Code, string Message)
{
    internal static readonly SectionIssue Missing = new("unavailable", "OPTIONAL_DATA_MISSING", "Источник не предоставил раздел.");
    internal static readonly SectionIssue Invalid = new("invalid", "OPTIONAL_DATA_INVALID", "Данные раздела повреждены.");
    internal static readonly SectionIssue Unsupported = new("unsupported", "OPTIONAL_DATA_UNSUPPORTED", "Семантика данных не подтверждена.");
    internal static readonly SectionIssue TooLarge = new("invalid", "OPTIONAL_DATA_TOO_LARGE", "Превышен размер раздела.");

    internal ReportSection Describe(string name, ReportWarnings warnings)
    {
        warnings.Add(Code, "/sections/" + name, Message);
        return new(Status, Message, null);
    }
}
