-- Create the OrderItems table
CREATE TABLE OrderItems (
    Id INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT NOT NULL,
    ProductId INT NOT NULL,
    ProductName NVARCHAR(100) NOT NULL,
    Quantity INT NOT NULL CHECK (Quantity > 0),
    UnitPriceExcludingGst DECIMAL(18,2) NOT NULL CHECK (UnitPriceExcludingGst >= 0),
    Currency NVARCHAR(3) NOT NULL DEFAULT 'USD',
    GstRate DECIMAL(5,4) NOT NULL DEFAULT 0.1000 CHECK (GstRate >= 0),
    
    CONSTRAINT FK_OrderItems_OrderId FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
    CONSTRAINT FK_OrderItems_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

-- Create index on OrderId for faster lookups
CREATE INDEX IX_OrderItems_OrderId ON OrderItems (OrderId);

-- Create index on ProductId for product analysis
CREATE INDEX IX_OrderItems_ProductId ON OrderItems (ProductId);