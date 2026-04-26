namespace TenantJobScheduler.Shared;

public sealed class TenantScheduler
{
    private int _tenantCursor;

    public IReadOnlyList<JobRecord> SelectDispatchBatch(
        IReadOnlyList<JobRecord> jobs,
        int totalSlots,
        DateTimeOffset now)
    {
        if (totalSlots <= 0)
        {
            return [];
        }

        var running = jobs
            .Where(job => job.Status == JobStatus.Running && (!job.LockedUntil.HasValue || job.LockedUntil > now))
            .ToList();

        var queued = jobs
            .Where(job => job.Status == JobStatus.Queued && (!job.AvailableAt.HasValue || job.AvailableAt <= now))
            .OrderByDescending(job => job.Priority)
            .ThenBy(job => job.CreatedAt)
            .ToList();

        var activeTenantIds = queued.Select(job => job.TenantId)
            .Concat(running.Select(job => job.TenantId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (activeTenantIds.Count == 0)
        {
            return [];
        }

        var baseSlots = Math.Max(1, totalSlots / activeTenantIds.Count);
        var freeSlots = Math.Max(0, totalSlots - running.Count);
        var selected = new List<JobRecord>();
        var guard = activeTenantIds.Count * Math.Max(1, freeSlots + queued.Count);

        while (freeSlots > 0 && queued.Count > 0 && guard-- > 0)
        {
            var tenantId = activeTenantIds[_tenantCursor % activeTenantIds.Count];
            _tenantCursor = (_tenantCursor + 1) % activeTenantIds.Count;

            var tenantRunningCount = running.Count(job => SameTenant(job.TenantId, tenantId))
                + selected.Count(job => SameTenant(job.TenantId, tenantId));

            if (tenantRunningCount >= baseSlots && HasOtherEligibleTenant(queued, running, selected, activeTenantIds, baseSlots))
            {
                continue;
            }

            var nextJob = queued.FirstOrDefault(job => SameTenant(job.TenantId, tenantId));
            if (nextJob is null)
            {
                continue;
            }

            queued.Remove(nextJob);
            selected.Add(nextJob);
            freeSlots--;
        }

        return selected;
    }

    private static bool HasOtherEligibleTenant(
        IReadOnlyList<JobRecord> queued,
        IReadOnlyList<JobRecord> running,
        IReadOnlyList<JobRecord> selected,
        IReadOnlyList<string> activeTenantIds,
        int baseSlots)
    {
        return activeTenantIds.Any(tenantId =>
            queued.Any(job => SameTenant(job.TenantId, tenantId))
            && running.Count(job => SameTenant(job.TenantId, tenantId))
            + selected.Count(job => SameTenant(job.TenantId, tenantId)) < baseSlots);
    }

    private static bool SameTenant(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
