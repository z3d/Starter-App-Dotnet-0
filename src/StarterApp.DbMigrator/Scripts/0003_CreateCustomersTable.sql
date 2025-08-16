-- Create the Customers table
CREATE TABLE Customers (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL,
    Email NVARCHAR(320) NOT NULL,
    DateCreated DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    IsActive BIT NOT NULL DEFAULT 1
);

-- Create index on Email for faster lookups
CREATE UNIQUE INDEX IX_Customers_Email ON Customers (Email);

-- Insert some initial seed data
INSERT INTO Customers (Name, Email, DateCreated, IsActive)
VALUES 
    ('John Doe', 'john.doe@example.com', GETUTCDATE(), 1),
    ('Jane Smith', 'jane.smith@example.com', GETUTCDATE(), 1),
    ('Bob Johnson', 'bob.johnson@example.com', GETUTCDATE(), 0);