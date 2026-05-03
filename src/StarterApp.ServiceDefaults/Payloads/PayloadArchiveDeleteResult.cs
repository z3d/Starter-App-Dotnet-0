namespace StarterApp.ServiceDefaults.Payloads;

public sealed record PayloadArchiveDeleteResult(int ArchiveDeleted, int AuditDeleted, int EntityIndexDeleted = 0)
{
    public int TotalDeleted => ArchiveDeleted + AuditDeleted + EntityIndexDeleted;
}
