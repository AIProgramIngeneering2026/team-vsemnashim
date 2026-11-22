using System.Collections.Immutable;
using System.Text.Json;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Execution;

public sealed class Dispatcher(ToolRegistry registry, IJobStore store)
{
    public Result<Job> Submit(string? operationName, JsonElement parameters)
    {
        if (operationName is null)
            return Result<Job>.Fail(Error.Field("VALIDATION_ERROR", "body", "/operation", "missing"));
        var operation = registry.FindOperation(operationName);
        if (!operation.IsSuccess)
            return Result<Job>.Fail(operation.Error);
        var input = operation.Value.Normalize(parameters);
        if (!input.IsSuccess)
            return Result<Job>.Fail(input.Error);
        var versions = operation.Value.Steps.Select(step => step.Key).ToImmutableArray();
        return store.Add(Job.Create(operationName, input.Value, versions, store.Clock.GetUtcNow()));
    }

    public async Task<Result<Job>> RunAsync(Guid id)
    {
        var start = store.Start(id);
        if (!start.IsSuccess)
            return start;
        var job = start.Value;
        var token = job.RunToken!.Value;
        var operation = registry.FindOperation(job.Operation);
        if (!operation.IsSuccess || operation.Value.Steps.Length != job.Versions.Length)
            return store.Reject(id, token, new("TOOL_VERSION_UNAVAILABLE", "Описание операции недоступно."));

        var context = new ToolContext(id, job.Attempts, job.Versions, job.DeadlineAt!.Value, store.Clock);
        var outputs = new List<object>();
        object previous = job.Parameters;
        try
        {
            foreach (var step in operation.Value.Steps)
            {
                var registered = registry.FindTool(job.Versions[outputs.Count]);
                if (!registered.IsSuccess)
                    return store.Reject(id, token, registered.Error);
                var result = await InvokeAsync(registered.Value, step.BindInput(previous), context);
                if (!result.IsSuccess)
                    return store.Reject(id, token, result.Error);
                previous = result.Value;
                outputs.Add(previous);
            }

            var artifacts = operation.Value.Publish(outputs);
            if (!artifacts.IsComplete)
                return store.Reject(id, token, new("TOOL_OUTPUT_INVALID", "Снимок или отчёт не получен."));
            var saved = store.Complete(id, token, artifacts);
            if (!saved.IsSuccess && saved.Error.Code == "INVALID_JOB_TRANSITION" &&
                store.Clock.GetUtcNow() >= context.DeadlineAt)
                return store.Reject(id, token, AttemptTimeout());
            return saved;
        }
        catch (Exception)
        {
            return store.Reject(id, token, new("INTERNAL_TOOL_ERROR", "Шаг обработки завершился исключением."));
        }
    }

    private static async Task<Result<object>> InvokeAsync(RegisteredTool tool, object input, ToolContext context)
    {
        var clock = context.Clock;
        var start = clock.GetUtcNow();
        var remaining = context.DeadlineAt - start;
        if (remaining <= TimeSpan.Zero)
            return Result<object>.Fail(AttemptTimeout());
        var budget = remaining < tool.Descriptor.Timeout ? remaining : tool.Descriptor.Timeout;
        using var timeout = new CancellationTokenSource(budget, clock);
        try
        {
            var result = await tool.Invoke(input, context, timeout.Token).AsTask().WaitAsync(timeout.Token);
            if (clock.GetUtcNow() >= context.DeadlineAt)
                return Result<object>.Fail(AttemptTimeout());
            return clock.GetUtcNow() - start >= budget || timeout.IsCancellationRequested
                ? Result<object>.Fail(tool.Descriptor.TimeoutError)
                : result;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Result<object>.Fail(clock.GetUtcNow() >= context.DeadlineAt ? AttemptTimeout() : tool.Descriptor.TimeoutError);
        }
    }

    private static Error AttemptTimeout() => new("PROCESSING_TIMEOUT", "Истёк срок попытки.");
}
