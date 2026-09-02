namespace StarterApp.ServiceDefaults.Payloads;

// Drives a bounded delete page repeatedly until a page comes back short (the prefix is caught up)
// or the caller's time budget is spent. A single capped pass could only ever delete
// CleanupBatchSize blobs per run, so ingestion above that rate accumulated expired PII forever;
// draining pages makes RetentionDays an enforceable property, and BudgetExhausted tells the
// job-runs trail when a run could not catch up.
public static class PayloadArchiveCleanupDrain
{
    public static async Task<(int Deleted, bool BudgetExhausted)> DrainAsync(
        Func<CancellationToken, Task<int>> deletePage,
        int pageSize,
        Func<bool> budgetExhausted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deletePage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentNullException.ThrowIfNull(budgetExhausted);

        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deleted = await deletePage(cancellationToken).ConfigureAwait(false);
            total += deleted;

            if (deleted < pageSize)
                return (total, false);

            if (budgetExhausted())
                return (total, true);
        }
    }
}
