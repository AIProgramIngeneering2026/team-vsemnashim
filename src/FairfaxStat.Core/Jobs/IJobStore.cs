using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Jobs;

public interface IJobStore
{
    TimeProvider Clock { get; }
    Result<Job> Add(Job job);
    Result<Job> Get(Guid id);
    Result<Job> Start(Guid id);
    Result<Job> Complete(Guid id, Guid token, JobArtifacts artifacts);
    Result<Job> Reject(Guid id, Guid token, Error reason);
    Result<Job> Requeue(Guid id);
    Result<Job> Recover(Guid id);
}
