using TenantJobScheduler.Shared;
using Xunit;

namespace TenantJobScheduler.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void SelectDispatchBatch_WhenTwentyTenantsAreActive_GivesEachTenantOneSlot()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = Enumerable.Range(1, 20)
            .Select(index => NewJob($"tenant-{index}", now))
            .ToList();

        var selected = new TenantScheduler().SelectDispatchBatch(jobs, totalSlots: 20, now);

        Assert.Equal(20, selected.Count);
        Assert.Equal(20, selected.Select(job => job.TenantId).Distinct().Count());
    }

    [Fact]
    public void SelectDispatchBatch_WhenTwoTenantsAreActive_UsesAllAvailableSlots()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = Enumerable.Range(1, 20)
            .Select(index => NewJob(index <= 10 ? "tenant-a" : "tenant-b", now.AddMilliseconds(index)))
            .ToList();

        var selected = new TenantScheduler().SelectDispatchBatch(jobs, totalSlots: 20, now);

        Assert.Equal(20, selected.Count);
        Assert.Equal(10, selected.Count(job => job.TenantId == "tenant-a"));
        Assert.Equal(10, selected.Count(job => job.TenantId == "tenant-b"));
    }

    [Fact]
    public void SelectDispatchBatch_WithReservedHeadroom_PreservesOneSlotForNewTenant()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = Enumerable.Range(1, 20)
            .Select(index => NewJob(index <= 10 ? "tenant-a" : "tenant-b", now.AddMilliseconds(index)))
            .ToList();

        var selected = new TenantScheduler().SelectDispatchBatch(
            jobs,
            totalSlots: 20,
            now,
            reservedHeadroomSlots: 1);

        Assert.Equal(19, selected.Count);
        Assert.True(selected.Count(job => job.TenantId == "tenant-a") is 9 or 10);
        Assert.True(selected.Count(job => job.TenantId == "tenant-b") is 9 or 10);
    }

    [Fact]
    public void SelectDispatchBatch_WithReservedHeadroom_UsesHeadroomForUnderservedTenant()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = Enumerable.Range(1, 19)
            .Select(index => RunningJob(index <= 10 ? "tenant-a" : "tenant-b", now))
            .Append(NewJob("tenant-new", now.AddMilliseconds(20)))
            .ToList();

        var selected = new TenantScheduler().SelectDispatchBatch(
            jobs,
            totalSlots: 20,
            now,
            reservedHeadroomSlots: 1);

        Assert.Single(selected);
        Assert.Equal("tenant-new", selected.Single().TenantId);
    }

    [Fact]
    public void SelectDispatchBatch_WhenTenantIsInBackoff_DoesNotDispatchIt()
    {
        var now = DateTimeOffset.UtcNow;
        var jobs = new[]
        {
            NewJob("tenant-a", now, now.AddSeconds(30)),
            NewJob("tenant-b", now)
        };

        var selected = new TenantScheduler().SelectDispatchBatch(jobs, totalSlots: 2, now);

        Assert.Single(selected);
        Assert.Equal("tenant-b", selected.Single().TenantId);
    }

    [Fact]
    public void PayloadProtector_RoundTripsEncryptedContent()
    {
        var protector = new AesPayloadProtector("test-key");
        const string payload = """{"durationMs":25,"kind":"report"}""";

        var encrypted = protector.Protect(payload);
        var decrypted = protector.Unprotect(encrypted);

        Assert.NotEqual(payload, encrypted);
        Assert.Equal(payload, decrypted);
    }

    private static JobRecord NewJob(string tenantId, DateTimeOffset createdAt, DateTimeOffset? availableAt = null)
    {
        return new JobRecord
        {
            TenantId = tenantId,
            EncryptedPayload = "encrypted",
            CreatedAt = createdAt,
            AvailableAt = availableAt
        };
    }

    private static JobRecord RunningJob(string tenantId, DateTimeOffset createdAt)
    {
        return new JobRecord
        {
            TenantId = tenantId,
            EncryptedPayload = "encrypted",
            CreatedAt = createdAt,
            Status = JobStatus.Running,
            LockedUntil = createdAt.AddMinutes(5)
        };
    }
}
