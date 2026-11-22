using FairfaxStat.Core.Jobs;

namespace FairfaxStat.Core.Tests.Jobs;

public sealed class JobStoreTests
{
    [Fact]
    public async Task Only_one_worker_acquires_a_job_when_two_arrive_together()
    {
        var store = new InMemoryJobStore(TimeProvider.System);
        var job = store.Add(JobTransitionTests.NewJob()).Value;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable.Range(0, 2).Select(async _ =>
        {
            await ready.Task;
            return store.Start(job.Id);
        }).ToArray();
        ready.SetResult();
        var attempts = await Task.WhenAll(workers);
        Assert.Single(attempts, r => r.IsSuccess);
        Assert.Single(attempts, r => !r.IsSuccess && r.Error.Code == "INVALID_JOB_TRANSITION");
        Assert.Equal(1, store.Get(job.Id).Value.Attempts);
    }

    [Fact]
    public void Duplicate_completion_cannot_replace_the_first_report()
    {
        var store = new InMemoryJobStore(TimeProvider.System);
        var id = store.Add(JobTransitionTests.NewJob()).Value.Id;
        var token = store.Start(id).Value.RunToken!.Value;
        Assert.True(store.Complete(id, token, JobTransitionTests.Output("first")).IsSuccess);
        Assert.False(store.Complete(id, token, JobTransitionTests.Output("second")).IsSuccess);
        Assert.Equal("first", store.Get(id).Value.Artifacts!.Report.GetProperty("label").GetString());
    }
}
