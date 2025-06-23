-- Add a new column to the Products table
ALTER TABLE Products ADD 
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE();

-- For existing records, we've already set them with the DEFAULT constraint
-- No need to run a separate UPDATE

-- If you need to remove the default constraint, you would need to:
-- 1. Find the actual constraint name:
-- SELECT name FROM sys.default_constraints
-- WHERE parent_object_id = OBJECT_ID('Products')
-- AND parent_column_id = (SELECT column_id FROM sys.columns
--                        WHERE object_id = OBJECT_ID('Products')
--                        AND name = 'LastUpdated')
-- 
-- 2. Then drop it with:
-- ALTER TABLE Products DROP CONSTRAINT [constraint_name_here]