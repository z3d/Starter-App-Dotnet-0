-- Create the Orders table
CREATE TABLE Orders (
    Id INT PRIMARY KEY IDENTITY(1,1),
    CustomerId INT NOT NULL,
    OrderDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT FK_Orders_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
);

-- Create index on CustomerId for faster lookups
CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);

-- Create index on Status for faster filtering
CREATE INDEX IX_Orders_Status ON Orders (Status);

-- Create index on OrderDate for sorting
CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate DESC);