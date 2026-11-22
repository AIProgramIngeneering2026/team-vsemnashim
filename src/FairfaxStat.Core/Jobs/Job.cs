using System.Collections.Immutable;
using System.Text.Json;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Jobs;

public sealed record Job
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);

    private Job() { }
    public Guid Id { get; private init; }
    public string Operation { get; private init; } = "";
    public JsonElement Parameters { get; private init; }
    public ImmutableArray<ToolKey> Versions { get; private init; }
    public JobState State { get; private init; }
    public int Attempts { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private init; }
    public DateTimeOffset? FinishedAt { get; private init; }
    public DateTimeOffset? NextAttemptAt { get; private init; }
    public Guid? RunToken { get; private init; }
    public DateTimeOffset? DeadlineAt { get; private init; }
    public DateTimeOffset? LeaseUntil { get; private init; }
    public Error? Error { get; private init; }
    public Error? LastError { get; private init; }
    public JobArtifacts? Artifacts { get; private init; }
    public string? ResultUrl => State == JobState.Succeeded ? $"/api/v1/jobs/{Id:D}/result" : null;

    public static Job Create(string operation, JsonElement parameters, ImmutableArray<ToolKey> versions,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), Operation = operation, Parameters = parameters.Clone(), Versions = versions,
        CreatedAt = now, UpdatedAt = now, State = JobState.Queued
    };

    public Result<Job> Start(DateTimeOffset now)
    {
        if (State != JobState.Queued || Attempts >= MaxAttempts)
            return Invalid();
        return Result<Job>.Ok(this with
        {
            State = JobState.Running, Attempts = Attempts + 1, RunToken = Guid.NewGuid(),
            LeaseUntil = now + LeaseDuration, DeadlineAt = now + AttemptTimeout, UpdatedAt = now
        });
    }

    public Result<Job> Complete(Guid token, JobArtifacts artifacts, DateTimeOffset now)
    {
        if (CheckOwner(token, now) is { } error)
            return Result<Job>.Fail(error);
        if (now >= DeadlineAt || !artifacts.IsComplete)
            return Invalid();
        return Result<Job>.Ok(Release(now) with
        {
            State = JobState.Succeeded, Artifacts = artifacts, FinishedAt = now
        });
    }

    public Result<Job> Reject(Guid token, Error reason, DateTimeOffset now)
    {
        if (CheckOwner(token, now) is { } error)
            return Result<Job>.Fail(error);
        if (now >= DeadlineAt)
            return FinishFailure(new("PROCESSING_TIMEOUT", "Истёк срок попытки."), now);

        bool transient = reason.Retryable && reason.Code is "SOURCE_UNAVAILABLE" or "SOURCE_RATE_LIMITED";
        if (!transient)
            return FinishFailure(reason, now);
        var baseDelay = TimeSpan.FromSeconds(Attempts == 1 ? 5 : 30);
        var requested = reason.RetryAt is { } retryAt ? retryAt - now : TimeSpan.Zero;
        var delay = requested > baseDelay ? requested : baseDelay;
        if (delay > TimeSpan.FromSeconds(120))
            return FinishFailure(reason with { Code = "SOURCE_RATE_LIMITED" }, now);
        if (Attempts == MaxAttempts)
            return FinishFailure(reason, now);
        return Wait(reason, now, TimeSpan.FromSeconds(Math.Ceiling(delay.TotalSeconds)));
    }

    public Result<Job> Requeue(DateTimeOffset now)
    {
        if (State != JobState.RetryWait || now < NextAttemptAt)
            return Invalid();
        return Result<Job>.Ok(this with { State = JobState.Queued, NextAttemptAt = null, UpdatedAt = now });
    }

    public Result<Job> Recover(DateTimeOffset now)
    {
        if (State != JobState.Running || now < LeaseUntil)
            return Invalid();
        var lost = new Error("WORKER_LOST", "Обработчик утратил владение попыткой.", true);
        return Attempts == MaxAttempts ? FinishFailure(lost, now) : Wait(lost, now, TimeSpan.FromSeconds(5));
    }

    private Error? CheckOwner(Guid token, DateTimeOffset now)
    {
        if ((State is JobState.RetryWait or JobState.Queued) && Attempts > 0)
            return new("STALE_ATTEMPT", "Попытка больше не владеет заданием.");
        if (State != JobState.Running)
            return Invalid().Error;
        if (RunToken != token || now >= LeaseUntil)
            return new("STALE_ATTEMPT", "Попытка больше не владеет заданием.");
        return null;
    }

    private Job Release(DateTimeOffset now) => this with
    {
        RunToken = null, DeadlineAt = null, LeaseUntil = null, UpdatedAt = now
    };

    private Result<Job> Wait(Error reason, DateTimeOffset now, TimeSpan delay) =>
        Result<Job>.Ok(Release(now) with { State = JobState.RetryWait, LastError = reason, NextAttemptAt = now + delay });

    private Result<Job> FinishFailure(Error reason, DateTimeOffset now) =>
        Result<Job>.Ok(Release(now) with { State = JobState.Failed, Error = reason, FinishedAt = now });

    private Result<Job> Invalid() => Result<Job>.Fail(new("INVALID_JOB_TRANSITION",
        $"Команда недопустима для задания в состоянии {State} или её условия не выполнены."));
}
