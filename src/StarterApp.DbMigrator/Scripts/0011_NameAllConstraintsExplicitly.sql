-- Name all anonymous constraints explicitly.
-- Convention: PK_Table, DF_Table_Column, CK_Table_Description
-- Uses sp_rename to rename system-generated names to predictable ones,
-- making future migrations deterministic (no dynamic SQL lookups).

DECLARE @oldName NVARCHAR(256);

-- === Primary Keys ===
-- Pattern: find the PK constraint on each table's identity column

-- Products.Id
SELECT @oldName = kc.name
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID('Products') AND kc.type = 'PK';
IF @oldName IS NOT NULL AND @oldName <> 'PK_Products'
    EXEC sp_rename @oldName, 'PK_Products', 'OBJECT';
SET @oldName = NULL;

-- Customers.Id
SELECT @oldName = kc.name
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID('Customers') AND kc.type = 'PK';
IF @oldName IS NOT NULL AND @oldName <> 'PK_Customers'
    EXEC sp_rename @oldName, 'PK_Customers', 'OBJECT';
SET @oldName = NULL;

-- Orders.Id
SELECT @oldName = kc.name
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID('Orders') AND kc.type = 'PK';
IF @oldName IS NOT NULL AND @oldName <> 'PK_Orders'
    EXEC sp_rename @oldName, 'PK_Orders', 'OBJECT';
SET @oldName = NULL;

-- OrderItems.Id
SELECT @oldName = kc.name
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID('OrderItems') AND kc.type = 'PK';
IF @oldName IS NOT NULL AND @oldName <> 'PK_OrderItems'
    EXEC sp_rename @oldName, 'PK_OrderItems', 'OBJECT';
SET @oldName = NULL;

-- OutboxMessages.Id
SELECT @oldName = kc.name
FROM sys.key_constraints kc
WHERE kc.parent_object_id = OBJECT_ID('OutboxMessages') AND kc.type = 'PK';
IF @oldName IS NOT NULL AND @oldName <> 'PK_OutboxMessages'
    EXEC sp_rename @oldName, 'PK_OutboxMessages', 'OBJECT';
SET @oldName = NULL;

-- === Default Constraints ===
-- Pattern: find the default constraint on Table.Column via sys.default_constraints

-- Products.PriceCurrency
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Products') AND c.name = 'PriceCurrency';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Products_PriceCurrency'
    EXEC sp_rename @oldName, 'DF_Products_PriceCurrency', 'OBJECT';
SET @oldName = NULL;

-- Products.Stock
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Products') AND c.name = 'Stock';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Products_Stock'
    EXEC sp_rename @oldName, 'DF_Products_Stock', 'OBJECT';
SET @oldName = NULL;

-- Products.LastUpdated
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Products') AND c.name = 'LastUpdated';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Products_LastUpdated'
    EXEC sp_rename @oldName, 'DF_Products_LastUpdated', 'OBJECT';
SET @oldName = NULL;

-- Customers.DateCreated
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Customers') AND c.name = 'DateCreated';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Customers_DateCreated'
    EXEC sp_rename @oldName, 'DF_Customers_DateCreated', 'OBJECT';
SET @oldName = NULL;

-- Customers.IsActive
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Customers') AND c.name = 'IsActive';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Customers_IsActive'
    EXEC sp_rename @oldName, 'DF_Customers_IsActive', 'OBJECT';
SET @oldName = NULL;

-- Orders.OrderDate
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Orders') AND c.name = 'OrderDate';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Orders_OrderDate'
    EXEC sp_rename @oldName, 'DF_Orders_OrderDate', 'OBJECT';
SET @oldName = NULL;

-- Orders.Status
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Orders') AND c.name = 'Status';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Orders_Status'
    EXEC sp_rename @oldName, 'DF_Orders_Status', 'OBJECT';
SET @oldName = NULL;

-- Orders.LastUpdated
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('Orders') AND c.name = 'LastUpdated';
IF @oldName IS NOT NULL AND @oldName <> 'DF_Orders_LastUpdated'
    EXEC sp_rename @oldName, 'DF_Orders_LastUpdated', 'OBJECT';
SET @oldName = NULL;

-- OrderItems.Currency
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('OrderItems') AND c.name = 'Currency';
IF @oldName IS NOT NULL AND @oldName <> 'DF_OrderItems_Currency'
    EXEC sp_rename @oldName, 'DF_OrderItems_Currency', 'OBJECT';
SET @oldName = NULL;

-- OrderItems.GstRate
SELECT @oldName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('OrderItems') AND c.name = 'GstRate';
IF @oldName IS NOT NULL AND @oldName <> 'DF_OrderItems_GstRate'
    EXEC sp_rename @oldName, 'DF_OrderItems_GstRate', 'OBJECT';
SET @oldName = NULL;

-- === Check Constraints ===
-- Pattern: find unnamed check constraints via sys.check_constraints

-- OrderItems.Quantity (CHECK Quantity > 0)
SELECT @oldName = cc.name
FROM sys.check_constraints cc
JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
WHERE cc.parent_object_id = OBJECT_ID('OrderItems') AND c.name = 'Quantity';
IF @oldName IS NOT NULL AND @oldName <> 'CK_OrderItems_Quantity_Positive'
    EXEC sp_rename @oldName, 'CK_OrderItems_Quantity_Positive', 'OBJECT';
SET @oldName = NULL;

-- OrderItems.UnitPriceExcludingGst (CHECK >= 0)
SELECT @oldName = cc.name
FROM sys.check_constraints cc
JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
WHERE cc.parent_object_id = OBJECT_ID('OrderItems') AND c.name = 'UnitPriceExcludingGst';
IF @oldName IS NOT NULL AND @oldName <> 'CK_OrderItems_UnitPrice_NonNegative'
    EXEC sp_rename @oldName, 'CK_OrderItems_UnitPrice_NonNegative', 'OBJECT';
SET @oldName = NULL;

-- OrderItems.GstRate (CHECK >= 0)
SELECT @oldName = cc.name
FROM sys.check_constraints cc
JOIN sys.columns c ON cc.parent_object_id = c.object_id AND cc.parent_column_id = c.column_id
WHERE cc.parent_object_id = OBJECT_ID('OrderItems') AND c.name = 'GstRate';
IF @oldName IS NOT NULL AND @oldName <> 'CK_OrderItems_GstRate_NonNegative'
    EXEC sp_rename @oldName, 'CK_OrderItems_GstRate_NonNegative', 'OBJECT';
SET @oldName = NULL;
