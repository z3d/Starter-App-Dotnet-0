namespace StarterApp.ServiceDefaults.Payloads;

public sealed record PayloadArchiveDeleteResult(int ArchiveDeleted, int AuditDeleted)
{
    public int TotalDeleted => ArchiveDeleted + AuditDeleted;
}
