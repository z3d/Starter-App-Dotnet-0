namespace StarterApp.Functions;

public sealed record WatchedJob(string Name, string Schedule)
{
    public string? ScheduleSetting => Schedule is ['%', .., '%'] ? Schedule.Trim('%') : null;
}
