using System.Text.Json.Nodes;

namespace TenantJobScheduler.Shared;

public sealed record SubmitJobRequest(string TenantId, JsonNode Payload, JobPriority Priority = JobPriority.Normal);

public sealed record SubmitJobResponse(Guid JobId, JobStatus Status);

public sealed record JobStatusResponse(
    Guid JobId,
    string TenantId,
    JobStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Result,
    string? Error);

public sealed record WorkerJobRequest(Guid JobId, string TenantId, string Payload, int Attempt);

public sealed record WorkerCallbackRequest(JobStatus Status, string? Result, string? Error);
