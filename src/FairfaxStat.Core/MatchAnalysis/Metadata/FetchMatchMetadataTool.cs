using FairfaxStat.Core.Execution;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.MatchAnalysis.Metadata;

public sealed record FetchMatchInput(string MatchId);

public sealed class FetchMatchMetadataTool(IMatchMetadataSource source) : ITool<FetchMatchInput, ValidatedMatchSnapshot>
{
    public static readonly ToolKey Key = new("fetch_match_metadata", 1);
    public ToolDescriptor Descriptor { get; } = new(Key, nameof(FetchMatchInput), nameof(ValidatedMatchSnapshot),
        TimeSpan.FromSeconds(30), new("SOURCE_UNAVAILABLE", "Истёк срок получения данных матча.", true));

    public async ValueTask<Result<ValidatedMatchSnapshot>> RunAsync(FetchMatchInput input, ToolContext context,
        CancellationToken cancellation)
    {
        if (context.Validate(Key) is { } invalid)
            return Result<ValidatedMatchSnapshot>.Fail(invalid);
        if (input?.MatchId is not { } id || !MatchId.IsCanonical(id))
            return Result<ValidatedMatchSnapshot>.Fail(Error.Field("TOOL_INPUT_INVALID", "job", "/match_id", "invalid_id"));
        var response = await source.ReadAsync(id, cancellation);
        return response.IsSuccess
            ? ValidatedMatchSnapshot.Read(response.Value, id, context.Clock.GetUtcNow())
            : Result<ValidatedMatchSnapshot>.Fail(response.Error);
    }

    public bool AcceptsOutput(FetchMatchInput input, ValidatedMatchSnapshot output, ToolContext context) =>
        output.MatchId == input.MatchId;
}
