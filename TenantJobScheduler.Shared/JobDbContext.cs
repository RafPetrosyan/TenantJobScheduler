using Microsoft.EntityFrameworkCore;

namespace TenantJobScheduler.Shared;

public sealed class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    public DbSet<JobRecord> Jobs => Set<JobRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<JobRecord>();

        job.ToTable("Jobs");
        job.HasKey(item => item.Id);
        job.Property(item => item.TenantId).HasMaxLength(128).IsRequired();
        job.Property(item => item.EncryptedPayload).IsRequired();
        job.Property(item => item.Priority).HasConversion<string>().HasMaxLength(32);
        job.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        job.Property(item => item.Result).HasMaxLength(4000);
        job.Property(item => item.Error).HasMaxLength(4000);
        job.HasIndex(item => new { item.Status, item.AvailableAt, item.CreatedAt });
        job.HasIndex(item => item.TenantId);
    }
}
