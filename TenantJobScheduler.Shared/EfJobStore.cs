using Microsoft.EntityFrameworkCore;

namespace TenantJobScheduler.Shared;

public sealed class EfJobStore(IDbContextFactory<JobDbContext> contextFactory) : IJobStore
{
    public async Task<IReadOnlyList<JobRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Jobs.AsNoTracking()
            .OrderBy(job => job.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<JobRecord?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Jobs.AsNoTracking()
            .FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
    }

    public async Task AddAsync(JobRecord job, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Jobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        if (!await TryUpdateAsync(job, cancellationToken))
        {
            throw new InvalidOperationException($"Job '{job.Id}' was not found.");
        }
    }

    public async Task<bool> TryUpdateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await context.Jobs.AnyAsync(current => current.Id == job.Id, cancellationToken);
        if (!exists)
        {
            return false;
        }

        context.Jobs.Update(job);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Jobs.RemoveRange(context.Jobs);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }
}
