-- Create the Products table
CREATE TABLE Products (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    PriceAmount DECIMAL(18, 2) NOT NULL,
    PriceCurrency NVARCHAR(3) NOT NULL DEFAULT 'USD',
    Stock INT NOT NULL DEFAULT 0
);

-- Insert some initial seed data
INSERT INTO Products (Name, Description, PriceAmount, PriceCurrency, Stock)
VALUES 
    ('Product 1', 'Description for product 1', 10.99, 'USD', 100),
    ('Product 2', 'Description for product 2', 24.99, 'USD', 50),
    ('Product 3', 'Description for product 3', 5.99, 'USD', 200);