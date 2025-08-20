-- Create the Orders table
CREATE TABLE Orders (
    Id INT PRIMARY KEY IDENTITY(1,1),
    CustomerId INT NOT NULL,
    OrderDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
    TotalExcludingGst DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    TotalIncludingGst DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    TotalGstAmount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    Currency NVARCHAR(3) NOT NULL DEFAULT 'USD',
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT FK_Orders_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
);

-- Create index on CustomerId for faster lookups
CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);

-- Create index on Status for faster filtering
CREATE INDEX IX_Orders_Status ON Orders (Status);

-- Create index on OrderDate for sorting
CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate DESC);