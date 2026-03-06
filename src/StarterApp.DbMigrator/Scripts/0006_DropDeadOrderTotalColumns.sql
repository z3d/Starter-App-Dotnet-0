-- Drop dead total columns from Orders table.
-- These were never written by EF Core (the Order entity doesn't map them).
-- Dapper read queries now compute totals via OUTER APPLY against OrderItems.
-- Must drop default constraints first (SQL Server requires this).
DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql += N'ALTER TABLE Orders DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';' + CHAR(13)
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Orders')
  AND c.name IN ('TotalExcludingGst', 'TotalIncludingGst', 'TotalGstAmount');

EXEC sp_executesql @sql;

ALTER TABLE Orders DROP COLUMN TotalExcludingGst;
ALTER TABLE Orders DROP COLUMN TotalIncludingGst;
ALTER TABLE Orders DROP COLUMN TotalGstAmount;
