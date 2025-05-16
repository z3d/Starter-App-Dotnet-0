-- Create the Products table
CREATE TABLE Products (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    Price DECIMAL(18, 2) NOT NULL,
    Stock INT NOT NULL DEFAULT 0
);

-- Insert some initial seed data
INSERT INTO Products (Name, Description, Price, Stock)
VALUES 
    ('Product 1', 'Description for product 1', 10.99, 100),
    ('Product 2', 'Description for product 2', 24.99, 50),
    ('Product 3', 'Description for product 3', 5.99, 200);