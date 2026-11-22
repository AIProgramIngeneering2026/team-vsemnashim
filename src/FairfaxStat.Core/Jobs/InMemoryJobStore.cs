using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Jobs;

public sealed class InMemoryJobStore(TimeProvider clock) : IJobStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Job> jobs = [];
    public TimeProvider Clock { get; } = clock;

    public Result<Job> Add(Job job)
    {
        lock (gate)
        {
            if (job.State != JobState.Queued || job.Attempts != 0 || jobs.ContainsKey(job.Id))
                return Result<Job>.Fail(new("INVALID_JOB_TRANSITION", "Задание уже принято или выполнялось."));
            if (jobs.Values.Count(j => j.State is JobState.Queued or JobState.Running or JobState.RetryWait) >= 100)
                return Result<Job>.Fail(new("QUEUE_FULL", "Очередь заполнена."));
            jobs.Add(job.Id, job);
            return Result<Job>.Ok(job);
        }
    }

    public Result<Job> Get(Guid id)
    {
        lock (gate)
            return jobs.TryGetValue(id, out var job) ? Result<Job>.Ok(job) : Missing();
    }

    public Result<Job> Start(Guid id) => Change(id, (job, now) =>
        job.State == JobState.Queued && jobs.Values.Any(j => j.State == JobState.Running)
            ? Result<Job>.Fail(new("WORKER_BUSY", "Обработчик занят."))
            : job.Start(now));
    public Result<Job> Complete(Guid id, Guid token, JobArtifacts artifacts) =>
        Change(id, (job, now) => job.Complete(token, artifacts, now));
    public Result<Job> Reject(Guid id, Guid token, Error reason) =>
        Change(id, (job, now) => job.Reject(token, reason, now));
    public Result<Job> Requeue(Guid id) => Change(id, (job, now) => job.Requeue(now));
    public Result<Job> Recover(Guid id) => Change(id, (job, now) => job.Recover(now));

    private Result<Job> Change(Guid id, Func<Job, DateTimeOffset, Result<Job>> transition)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(id, out var current))
                return Missing();
            var next = transition(current, Clock.GetUtcNow());
            if (next.IsSuccess)
                jobs[id] = next.Value;
            return next;
        }
    }

    private static Result<Job> Missing() => Result<Job>.Fail(new("JOB_NOT_FOUND", "Задание не найдено."));
}
