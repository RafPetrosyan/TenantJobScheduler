namespace TenantJobScheduler.Shared;

public sealed class JobStoreOptions
{
    public string FilePath { get; init; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "jobs.json");
}
