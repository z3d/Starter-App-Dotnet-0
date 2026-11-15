namespace StarterApp.Functions;

public sealed record IdempotencyRecordPurgeResult(int Deleted, bool BudgetExhausted);
