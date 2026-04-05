-- Migrate all DATETIME2 columns to DATETIMEOFFSET.
-- Existing DATETIME2 values are implicitly converted with +00:00 offset.
-- Default constraints must be dropped and recreated because ALTER COLUMN does not update them.

-- === Helper: drop a named or system-generated default constraint ===
DECLARE @sql NVARCHAR(MAX);

-- Products.LastUpdated
SELECT @sql = 'ALTER TABLE Products DROP CONSTRAINT ' + QUOTENAME(d.name)
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID('Products') AND c.name = 'LastUpdated';
IF @sql IS NOT NULL EXEC sp_executesql @sql;
SET @sql = NULL;

ALTER TABLE Products ALTER COLUMN LastUpdated DATETIMEOFFSET NOT NULL;
ALTER TABLE Products ADD DEFAULT SYSDATETIMEOFFSET() FOR LastUpdated;

-- Customers.DateCreated
SELECT @sql = 'ALTER TABLE Customers DROP CONSTRAINT ' + QUOTENAME(d.name)
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID('Customers') AND c.name = 'DateCreated';
IF @sql IS NOT NULL EXEC sp_executesql @sql;
SET @sql = NULL;

ALTER TABLE Customers ALTER COLUMN DateCreated DATETIMEOFFSET NOT NULL;
ALTER TABLE Customers ADD DEFAULT SYSDATETIMEOFFSET() FOR DateCreated;

-- Orders.OrderDate (must drop dependent index first)
DROP INDEX IF EXISTS IX_Orders_OrderDate ON Orders;

SELECT @sql = 'ALTER TABLE Orders DROP CONSTRAINT ' + QUOTENAME(d.name)
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID('Orders') AND c.name = 'OrderDate';
IF @sql IS NOT NULL EXEC sp_executesql @sql;
SET @sql = NULL;

ALTER TABLE Orders ALTER COLUMN OrderDate DATETIMEOFFSET NOT NULL;
ALTER TABLE Orders ADD DEFAULT SYSDATETIMEOFFSET() FOR OrderDate;

CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate DESC);

-- Orders.LastUpdated
SELECT @sql = 'ALTER TABLE Orders DROP CONSTRAINT ' + QUOTENAME(d.name)
FROM sys.default_constraints d
JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
WHERE d.parent_object_id = OBJECT_ID('Orders') AND c.name = 'LastUpdated';
IF @sql IS NOT NULL EXEC sp_executesql @sql;
SET @sql = NULL;

ALTER TABLE Orders ALTER COLUMN LastUpdated DATETIMEOFFSET NOT NULL;
ALTER TABLE Orders ADD DEFAULT SYSDATETIMEOFFSET() FOR LastUpdated;

-- OutboxMessages (must drop dependent index first)
DROP INDEX IF EXISTS IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc ON OutboxMessages;

ALTER TABLE OutboxMessages ALTER COLUMN OccurredOnUtc DATETIMEOFFSET NOT NULL;
ALTER TABLE OutboxMessages ALTER COLUMN ProcessedOnUtc DATETIMEOFFSET NULL;

CREATE INDEX IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc ON OutboxMessages (ProcessedOnUtc, OccurredOnUtc);
