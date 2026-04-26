namespace TenantJobScheduler.Shared;

public sealed class JobRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TenantId { get; init; } = "";
    public string EncryptedPayload { get; set; } = "";
    public JobPriority Priority { get; init; } = JobPriority.Normal;
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; init; } = 3;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AvailableAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
