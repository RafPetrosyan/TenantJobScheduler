namespace TenantJobScheduler.Shared;

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    DeadLetter
}
