using System.Text.Json;
using FairfaxStat.Core.Execution;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;
using static FairfaxStat.Core.Tests.Execution.ToolRegistryTests;

namespace FairfaxStat.Core.Tests.Execution;

public sealed class DispatcherTests
{
    private readonly TestTool tool = new();
    private readonly ToolRegistry registry = new();
    private readonly TestClock clock = new();
    private readonly InMemoryJobStore store;
    private readonly Dispatcher dispatcher;

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public DispatcherTests()
    {
        store = new(clock);
        dispatcher = new(registry, store);
        registry.Register(new Operation("calculate", p => Result<JsonElement>.Ok(p),
            [new(tool.Key, value => ((JsonElement)value).GetInt32())],
            outputs => new JobArtifacts(JsonSerializer.SerializeToElement(new { input = 3 }),
                JsonSerializer.SerializeToElement(new { total = (int)outputs[0] }))));
    }

    private Guid Submit() => dispatcher.Submit("calculate", JsonSerializer.SerializeToElement(3)).Value.Id;

    [Fact]
    public async Task Newly_registered_tool_runs_through_the_same_dispatcher()
    {
        registry.Register(tool);
        var result = (await dispatcher.RunAsync(Submit())).Value;
        Assert.Equal(JobState.Succeeded, result.State);
        Assert.Equal(6, result.Artifacts!.Report.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Tool_exception_becomes_failed_job_with_public_error()
    {
        tool.Execute = _ => throw new InvalidOperationException("private exception text");
        registry.Register(tool);
        var result = (await dispatcher.RunAsync(Submit())).Value;
        Assert.Equal(JobState.Failed, result.State);
        Assert.Equal("INTERNAL_TOOL_ERROR", result.Error!.Code);
        Assert.DoesNotContain("private", result.Error.Message);
        Assert.Null(result.Artifacts);
    }

    [Fact]
    public async Task Transient_failure_can_run_again_only_when_due()
    {
        registry.Register(tool);
        var id = Submit();
        tool.Execute = _ => Result<int>.Fail(new("SOURCE_UNAVAILABLE", "Источник занят.", true));
        var wait = (await dispatcher.RunAsync(id)).Value;
        Assert.Equal(JobState.RetryWait, wait.State);
        Assert.False((await dispatcher.RunAsync(id)).IsSuccess);
        Assert.Equal(1, tool.Calls);
        clock.Now = wait.NextAttemptAt!.Value;
        Assert.True(store.Requeue(id).IsSuccess);
        tool.Execute = x => Result<int>.Ok(x);
        var done = (await dispatcher.RunAsync(id)).Value;
        Assert.Equal(2, done.Attempts);
        Assert.Equal(JobState.Succeeded, done.State);
    }

    [Fact]
    public async Task Third_transient_failure_has_no_fourth_attempt()
    {
        tool.Execute = _ => Result<int>.Fail(new("SOURCE_UNAVAILABLE", "Источник занят.", true));
        registry.Register(tool);
        var id = Submit();
        for (int i = 1; i <= 2; i++)
        {
            var waiting = (await dispatcher.RunAsync(id)).Value;
            Assert.Equal(clock.Now.AddSeconds(i == 1 ? 5 : 30), waiting.NextAttemptAt);
            clock.Now = waiting.NextAttemptAt!.Value;
            store.Requeue(id);
        }
        var failed = (await dispatcher.RunAsync(id)).Value;
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("SOURCE_UNAVAILABLE", failed.Error!.Code);
        Assert.Equal(3, tool.Calls);
        Assert.False((await dispatcher.RunAsync(id)).IsSuccess);
    }

    [Fact]
    public async Task Missing_version_and_invalid_output_fail_after_claim()
    {
        var missing = (await dispatcher.RunAsync(Submit())).Value;
        Assert.Equal("TOOL_VERSION_UNAVAILABLE", missing.Error!.Code);
        Assert.Equal(1, missing.Attempts);
        tool.AcceptOutput = false;
        registry.Register(tool);
        var invalid = (await dispatcher.RunAsync(Submit())).Value;
        Assert.Equal("TOOL_OUTPUT_INVALID", invalid.Error!.Code);
        Assert.Null(invalid.Artifacts);
    }

    [Fact]
    public async Task Tool_over_its_local_deadline_cannot_publish()
    {
        tool.Execute = value => { clock.Now = clock.Now.AddSeconds(10); return Result<int>.Ok(value); };
        registry.Register(tool);
        var failed = (await dispatcher.RunAsync(Submit())).Value;
        Assert.Equal("PROCESSING_TIMEOUT", failed.Error!.Code);
        Assert.Null(failed.Artifacts);
    }
}
