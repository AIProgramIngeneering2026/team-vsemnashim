using System.Text.Json;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Tests.Jobs;

public sealed class JobTransitionTests
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    internal static Job NewJob() => Job.Create("analyze_match", JsonSerializer.SerializeToElement(new { match_id = "7" }),
        [new ToolKey("fetch_match_metadata", 1)], Now);
    internal static JobArtifacts Output(string label = "winner") => new(
        JsonSerializer.SerializeToElement(new { match_id = "7" }), JsonSerializer.SerializeToElement(new { label }));

    [Fact]
    public void Successful_job_publishes_snapshot_and_report_together()
    {
        var queued = NewJob();
        var running = queued.Start(Now).Value;
        var finished = running.Complete(running.RunToken!.Value, Output(), Now.AddSeconds(1)).Value;

        Assert.Equal(JobState.Queued, queued.State);
        Assert.Null(queued.Artifacts);
        Assert.Equal(1, finished.Attempts);
        Assert.Equal(JobState.Succeeded, finished.State);
        Assert.Equal("winner", finished.Artifacts!.Report.GetProperty("label").GetString());
        Assert.Equal("7", finished.Artifacts.Snapshot.GetProperty("match_id").GetString());
        Assert.Null(finished.RunToken);
    }

    [Fact]
    public void Z1_double_start_is_rejected_by_the_job_itself()
    {
        var running = NewJob().Start(Now).Value;
        Assert.Equal("INVALID_JOB_TRANSITION", running.Start(Now).Error.Code);
        Assert.Equal(1, running.Attempts);
    }

    [Fact]
    public void Z4_failure_cannot_be_replaced_with_a_late_success()
    {
        var running = NewJob().Start(Now).Value;
        var token = running.RunToken!.Value;
        var failed = running.Reject(token, new Error("SOURCE_DATA_INVALID", "Ошибка данных."), Now).Value;
        Assert.Equal("INVALID_JOB_TRANSITION", failed.Complete(token, Output(), Now).Error.Code);
        Assert.Equal("SOURCE_DATA_INVALID", failed.Error!.Code);
        Assert.Null(failed.Artifacts);
    }

    [Fact]
    public void Z5_queued_job_cannot_publish_a_result()
    {
        var queued = NewJob();
        Assert.Equal("INVALID_JOB_TRANSITION", queued.Complete(Guid.NewGuid(), Output(), Now).Error.Code);
        Assert.Null(queued.Artifacts);
        Assert.Equal(0, queued.Attempts);
    }

    [Fact]
    public void Z6_successful_job_cannot_run_again()
    {
        var running = NewJob().Start(Now).Value;
        var done = running.Complete(running.RunToken!.Value, Output(), Now).Value;
        Assert.Equal("INVALID_JOB_TRANSITION", done.Start(Now).Error.Code);
        Assert.Equal("INVALID_JOB_TRANSITION", done.Requeue(Now).Error.Code);
        Assert.Equal(1, done.Attempts);
    }
}
