-- Drop dead Currency column from Orders table.
-- This column was never mapped by EF Core (the Order entity has no Currency property).
-- Dapper read queries derive currency from OrderItems via OUTER APPLY.
-- Must drop default constraint first (SQL Server requires this).
DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql += N'ALTER TABLE Orders DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';' + CHAR(13)
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Orders')
  AND c.name = 'Currency';

EXEC sp_executesql @sql;

ALTER TABLE Orders DROP COLUMN Currency;
