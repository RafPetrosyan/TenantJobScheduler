using Microsoft.EntityFrameworkCore;
using TenantJobScheduler.Shared;
using Testcontainers.MsSql;
using Xunit;

namespace TenantJobScheduler.Tests;

public sealed class SqlServerJobStoreTests : IAsyncLifetime
{
    private MsSqlContainer? _sqlServer;

    public async Task InitializeAsync()
    {
        if (!ShouldRunSqlServerTests())
        {
            return;
        }

        _sqlServer = new MsSqlBuilder().Build();
        await _sqlServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (ShouldRunSqlServerTests())
        {
            await _sqlServer!.DisposeAsync().AsTask();
        }
    }

    [Fact]
    public async Task EfJobStore_WithSqlServerContainer_PersistsAndUpdatesJobs()
    {
        if (!ShouldRunSqlServerTests())
        {
            return;
        }

        var options = new DbContextOptionsBuilder<JobDbContext>()
            .UseSqlServer(_sqlServer!.GetConnectionString())
            .Options;
        var store = new EfJobStore(new TestDbContextFactory(options));
        await store.EnsureCreatedAsync(CancellationToken.None);

        var job = new JobRecord
        {
            TenantId = "tenant-sql",
            EncryptedPayload = "encrypted-payload"
        };

        await store.AddAsync(job, CancellationToken.None);
        var queued = await store.GetAsync(job.Id, CancellationToken.None);

        Assert.NotNull(queued);
        Assert.Equal(JobStatus.Queued, queued.Status);

        queued.Status = JobStatus.Completed;
        queued.Result = "ok";
        await store.UpdateAsync(queued, CancellationToken.None);

        var completed = await store.GetAsync(job.Id, CancellationToken.None);
        Assert.NotNull(completed);
        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.Equal("ok", completed.Result);
    }

    private static bool ShouldRunSqlServerTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_SQLSERVER_TESTCONTAINERS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestDbContextFactory(DbContextOptions<JobDbContext> options) : IDbContextFactory<JobDbContext>
    {
        public JobDbContext CreateDbContext()
        {
            return new JobDbContext(options);
        }
    }
}
