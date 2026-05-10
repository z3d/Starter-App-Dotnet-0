-- Add owner-scoping columns for resource-level authorization.
-- The API authenticates callers at the gateway boundary; owned resources persist the
-- verified subject + tenant so handlers and queries can enforce owner-only access.

ALTER TABLE Customers
    ADD OwnerSubject NVARCHAR(200) NOT NULL CONSTRAINT DF_Customers_OwnerSubject DEFAULT 'legacy-owner',
        TenantId NVARCHAR(100) NOT NULL CONSTRAINT DF_Customers_TenantId DEFAULT 'legacy-tenant';

ALTER TABLE Products
    ADD OwnerSubject NVARCHAR(200) NOT NULL CONSTRAINT DF_Products_OwnerSubject DEFAULT 'legacy-owner',
        TenantId NVARCHAR(100) NOT NULL CONSTRAINT DF_Products_TenantId DEFAULT 'legacy-tenant';

ALTER TABLE Orders
    ADD OwnerSubject NVARCHAR(200) NOT NULL CONSTRAINT DF_Orders_OwnerSubject DEFAULT 'legacy-owner',
        TenantId NVARCHAR(100) NOT NULL CONSTRAINT DF_Orders_TenantId DEFAULT 'legacy-tenant';

DROP INDEX IF EXISTS IX_Customers_Email ON Customers;

CREATE UNIQUE INDEX IX_Customers_TenantId_OwnerSubject_Email
    ON Customers (TenantId, OwnerSubject, Email);

CREATE INDEX IX_Customers_TenantId_OwnerSubject
    ON Customers (TenantId, OwnerSubject);

CREATE INDEX IX_Products_TenantId_OwnerSubject
    ON Products (TenantId, OwnerSubject);

CREATE INDEX IX_Orders_TenantId_OwnerSubject
    ON Orders (TenantId, OwnerSubject);

CREATE INDEX IX_Orders_TenantId_OwnerSubject_CustomerId
    ON Orders (TenantId, OwnerSubject, CustomerId);

CREATE INDEX IX_Orders_TenantId_OwnerSubject_Status
    ON Orders (TenantId, OwnerSubject, Status);
