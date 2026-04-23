-- Convert Orders.Id and OrderItems.OrderId from INT IDENTITY to UNIQUEIDENTIFIER.
--
-- Rationale: Aggregates that raise creation events (like Order) must have client-assigned
-- Ids so the event payload can be built BEFORE SaveChanges. This keeps the outbox a single
-- SaveChanges, which is required for Azure SQL's EnableRetryOnFailure strategy to work safely.
-- See CLAUDE.md "Aggregate Id Convention".
--
-- Existing rows (if any) are preserved via a mapping table that assigns a fresh Guid per row.
-- Constraint names follow the 0011_NameAllConstraintsExplicitly convention.

-- 1. Drop FKs referencing Orders.Id and indexes that would block renames
ALTER TABLE OrderItems DROP CONSTRAINT FK_OrderItems_OrderId;

DROP INDEX IF EXISTS IX_OrderItems_OrderId ON OrderItems;
DROP INDEX IF EXISTS IX_Orders_CustomerId ON Orders;
DROP INDEX IF EXISTS IX_Orders_Status ON Orders;
DROP INDEX IF EXISTS IX_Orders_OrderDate ON Orders;

-- 2. Build Id mapping (old INT -> new UNIQUEIDENTIFIER)
CREATE TABLE OrderIdMap
(
    OldId INT NOT NULL CONSTRAINT PK_OrderIdMap PRIMARY KEY,
    NewId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_OrderIdMap_NewId DEFAULT NEWID()
);

INSERT INTO OrderIdMap (OldId)
SELECT Id FROM Orders;

-- 3. Rename old Orders table, create new Orders with UNIQUEIDENTIFIER PK
EXEC sp_rename 'Orders', 'Orders_old';

-- Rename constraints on Orders_old so the new table can reuse the canonical names.
-- Renaming the table doesn't rename its attached constraints — they keep their original names
-- and would collide when the new Orders is created with the same constraint names.
DECLARE @cname NVARCHAR(256);

SELECT @cname = kc.name FROM sys.key_constraints kc
 WHERE kc.parent_object_id = OBJECT_ID('Orders_old') AND kc.type = 'PK';
IF @cname IS NOT NULL AND @cname <> 'PK_Orders_old'
    EXEC sp_rename @cname, 'PK_Orders_old', 'OBJECT';

SELECT @cname = dc.name FROM sys.default_constraints dc
 INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
 WHERE dc.parent_object_id = OBJECT_ID('Orders_old') AND c.name = 'OrderDate';
IF @cname IS NOT NULL AND @cname <> 'DF_Orders_old_OrderDate'
    EXEC sp_rename @cname, 'DF_Orders_old_OrderDate', 'OBJECT';

SELECT @cname = dc.name FROM sys.default_constraints dc
 INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
 WHERE dc.parent_object_id = OBJECT_ID('Orders_old') AND c.name = 'Status';
IF @cname IS NOT NULL AND @cname <> 'DF_Orders_old_Status'
    EXEC sp_rename @cname, 'DF_Orders_old_Status', 'OBJECT';

SELECT @cname = dc.name FROM sys.default_constraints dc
 INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
 WHERE dc.parent_object_id = OBJECT_ID('Orders_old') AND c.name = 'LastUpdated';
IF @cname IS NOT NULL AND @cname <> 'DF_Orders_old_LastUpdated'
    EXEC sp_rename @cname, 'DF_Orders_old_LastUpdated', 'OBJECT';

SELECT @cname = fk.name FROM sys.foreign_keys fk
 WHERE fk.parent_object_id = OBJECT_ID('Orders_old');
IF @cname IS NOT NULL AND @cname <> 'FK_Orders_old_CustomerId'
    EXEC sp_rename @cname, 'FK_Orders_old_CustomerId', 'OBJECT';

CREATE TABLE Orders
(
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    CustomerId INT NOT NULL,
    OrderDate DATETIMEOFFSET NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT SYSUTCDATETIME(),
    Status NVARCHAR(50) NOT NULL CONSTRAINT DF_Orders_Status DEFAULT 'Pending',
    LastUpdated DATETIMEOFFSET NOT NULL CONSTRAINT DF_Orders_LastUpdated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Orders_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
);

INSERT INTO Orders (Id, CustomerId, OrderDate, Status, LastUpdated)
SELECT m.NewId, o.CustomerId, o.OrderDate, o.Status, o.LastUpdated
FROM Orders_old o
INNER JOIN OrderIdMap m ON m.OldId = o.Id;

-- 4. Update OrderItems.OrderId to UNIQUEIDENTIFIER via mapping
ALTER TABLE OrderItems ADD NewOrderId UNIQUEIDENTIFIER NULL;

UPDATE oi
SET NewOrderId = m.NewId
FROM OrderItems oi
INNER JOIN OrderIdMap m ON m.OldId = oi.OrderId;

ALTER TABLE OrderItems DROP COLUMN OrderId;
EXEC sp_rename 'OrderItems.NewOrderId', 'OrderId', 'COLUMN';
ALTER TABLE OrderItems ALTER COLUMN OrderId UNIQUEIDENTIFIER NOT NULL;

-- 5. Recreate FKs and indexes
ALTER TABLE OrderItems
    ADD CONSTRAINT FK_OrderItems_OrderId FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE;

CREATE INDEX IX_OrderItems_OrderId ON OrderItems (OrderId);
CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);
CREATE INDEX IX_Orders_Status ON Orders (Status);
CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate DESC);

-- 6. Drop scaffolding
DROP TABLE Orders_old;
DROP TABLE OrderIdMap;
