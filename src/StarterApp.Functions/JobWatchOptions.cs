namespace StarterApp.Functions;

public sealed class JobWatchOptions
{
    public const string SectionName = "JobWatch";

    public int OverdueGraceMinutes { get; set; } = 15;

    public int UnfinishedAfterMinutes { get; set; } = 120;
}
