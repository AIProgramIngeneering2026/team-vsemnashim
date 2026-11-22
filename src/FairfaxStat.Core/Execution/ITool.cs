using System.Collections.Immutable;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Execution;

public sealed record ToolDescriptor(ToolKey Key, string InputSchema, string OutputSchema,
    TimeSpan Timeout, Error TimeoutError);

public sealed record ToolContext(Guid JobId, int Attempt, ImmutableArray<ToolKey> Versions,
    DateTimeOffset DeadlineAt, TimeProvider Clock)
{
    public Error? Validate(ToolKey key)
    {
        if (JobId == Guid.Empty)
            return Error.Field("TOOL_INPUT_INVALID", "job", "/job_id", "missing");
        if (Attempt is < 1 or > Job.MaxAttempts)
            return Error.Field("TOOL_INPUT_INVALID", "job", "/attempt", "out_of_range");
        if (Versions.IsDefaultOrEmpty || !Versions.Contains(key))
            return Error.Field("TOOL_INPUT_INVALID", "job", "/versions", "version_mismatch");
        return null;
    }
}

public interface ITool<TInput, TOutput> where TInput : notnull where TOutput : notnull
{
    ToolDescriptor Descriptor { get; }
    ValueTask<Result<TOutput>> RunAsync(TInput input, ToolContext context, CancellationToken cancellation);
    bool AcceptsOutput(TInput input, TOutput output, ToolContext context);
}
