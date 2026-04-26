using Microsoft.EntityFrameworkCore;

namespace TenantJobScheduler.Shared;

public static class JobStoreFactory
{
    public static async Task<IJobStore> CreateAsync(CancellationToken cancellationToken = default)
    {
        var provider = Environment.GetEnvironmentVariable("JOB_STORE_PROVIDER");
        var connectionString = Environment.GetEnvironmentVariable("JOB_STORE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Jobs");

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(connectionString))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("SQL Server job storage requires JOB_STORE_CONNECTION_STRING.");
            }

            var options = new DbContextOptionsBuilder<JobDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            var store = new EfJobStore(new SimpleDbContextFactory(options));
            await store.EnsureCreatedAsync(cancellationToken);
            return store;
        }

        return new FileJobStore(new JobStoreOptions
        {
            FilePath = Environment.GetEnvironmentVariable("JOB_STORE_PATH")
                ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "jobs.json")
        });
    }

    private sealed class SimpleDbContextFactory(DbContextOptions<JobDbContext> options) : IDbContextFactory<JobDbContext>
    {
        public JobDbContext CreateDbContext()
        {
            return new JobDbContext(options);
        }
    }
}
