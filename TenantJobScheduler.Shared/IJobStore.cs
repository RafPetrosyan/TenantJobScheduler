namespace TenantJobScheduler.Shared;

public interface IJobStore
{
    Task<IReadOnlyList<JobRecord>> ListAsync(CancellationToken cancellationToken);
    Task<JobRecord?> GetAsync(Guid jobId, CancellationToken cancellationToken);
    Task AddAsync(JobRecord job, CancellationToken cancellationToken);
    Task UpdateAsync(JobRecord job, CancellationToken cancellationToken);
    Task<bool> TryUpdateAsync(JobRecord job, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
