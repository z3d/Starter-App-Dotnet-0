-- Add a new column to the Products table
ALTER TABLE Products ADD 
    LastUpdated DATETIME2 NULL;

-- Update existing records with a default value
UPDATE Products
SET LastUpdated = GETUTCDATE();

-- Add a constraint to ensure future records have this field populated
ALTER TABLE Products
ALTER COLUMN LastUpdated DATETIME2 NOT NULL;