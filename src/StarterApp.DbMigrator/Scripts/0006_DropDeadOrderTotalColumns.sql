-- Drop dead total columns from Orders table.
-- These were never written by EF Core (the Order entity doesn't map them).
-- Dapper read queries now compute totals via OUTER APPLY against OrderItems.
ALTER TABLE Orders DROP COLUMN TotalExcludingGst;
ALTER TABLE Orders DROP COLUMN TotalIncludingGst;
ALTER TABLE Orders DROP COLUMN TotalGstAmount;
