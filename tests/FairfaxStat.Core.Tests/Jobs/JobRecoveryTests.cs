using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Tests.Jobs;

public sealed class JobRecoveryTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }

    [Fact]
    public void Z2_recovery_releases_lost_worker_and_rejects_its_late_report()
    {
        var clock = new ManualClock();
        var store = new InMemoryJobStore(clock);
        var id = store.Add(JobTransitionTests.NewJob()).Value.Id;
        var token = store.Start(id).Value.RunToken!.Value;
        clock.Advance(89);
        Assert.False(store.Recover(id).IsSuccess);
        clock.Advance(1);
        var waiting = store.Recover(id).Value;
        Assert.Equal(JobState.RetryWait, waiting.State);
        Assert.Equal("WORKER_LOST", waiting.LastError!.Code);
        Assert.Equal(clock.Now.AddSeconds(5), waiting.NextAttemptAt);
        Assert.False(store.Requeue(id).IsSuccess);
        clock.Advance(5);
        store.Requeue(id);
        var next = store.Start(id).Value;
        Assert.Equal(2, next.Attempts);
        Assert.Equal("STALE_ATTEMPT", store.Complete(id, token, JobTransitionTests.Output("old")).Error.Code);
        Assert.Null(store.Get(id).Value.Artifacts);
        Assert.True(store.Complete(id, next.RunToken!.Value, JobTransitionTests.Output()).IsSuccess);
    }

    [Fact]
    public void Three_lost_leases_exhaust_the_budget()
    {
        var clock = new ManualClock();
        var store = new InMemoryJobStore(clock);
        var id = store.Add(JobTransitionTests.NewJob()).Value.Id;
        for (int i = 1; i <= 3; i++)
        {
            Assert.Equal(i, store.Start(id).Value.Attempts);
            clock.Advance(90);
            var job = store.Recover(id).Value;
            if (i < 3)
            {
                clock.Advance(5);
                Assert.True(store.Requeue(id).IsSuccess);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State);
                Assert.Equal("WORKER_LOST", job.Error!.Code);
                Assert.False(store.Start(id).IsSuccess);
            }
        }
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(120, 120)]
    [InlineData(121, -1)]
    public void Source_retry_after_respects_bounds(int requested, int expected)
    {
        var now = DateTimeOffset.UnixEpoch;
        var running = JobTransitionTests.NewJob().Start(now).Value;
        var reason = new Error("SOURCE_UNAVAILABLE", "Источник занят.", true, now.AddSeconds(requested));
        var changed = running.Reject(running.RunToken!.Value, reason, now).Value;
        if (expected < 0)
            Assert.Equal("SOURCE_RATE_LIMITED", changed.Error!.Code);
        else
            Assert.Equal(now.AddSeconds(expected), changed.NextAttemptAt);
    }

    [Fact]
    public void Deadline_expires_before_lease_and_owner_can_still_record_timeout()
    {
        var now = DateTimeOffset.UnixEpoch;
        var job = JobTransitionTests.NewJob().Start(now).Value;
        var token = job.RunToken!.Value;
        Assert.False(job.Complete(token, JobTransitionTests.Output(), now.AddSeconds(60)).IsSuccess);
        var failed = job.Reject(token, new Error("PROCESSING_TIMEOUT", "Истёк срок."), now.AddSeconds(60)).Value;
        Assert.Equal("PROCESSING_TIMEOUT", failed.Error!.Code);
        Assert.Equal(JobState.Failed, failed.State);
    }
}
