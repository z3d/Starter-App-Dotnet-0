# Step 2: SQL Server Setup with DbUp

## Overview
We'll use SQL Server with DbUp for database migrations. DbUp is a .NET library that helps you deploy changes to SQL Server databases. It tracks which SQL scripts have been run, and runs the change scripts that are needed to get your database up to date.

## Project Structure
- `DockerLearning.DbMigrator` - Console application that runs database migrations
- `Scripts/` folder - Contains numbered SQL migration scripts
- Scripts are embedded as resources in the assembly

## Migration Scripts

### 1. Create Products Table (`0001_CreateProductsTable.sql`):
This script creates the main Products table with seed data:

```sql
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
```

### 2. Add LastUpdated Column (`0002_AddLastUpdatedColumn.sql`):
This script adds a timestamp column for tracking updates:

```sql
-- Add a new column to the Products table
ALTER TABLE Products ADD 
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE();
```

## Configuration

### 1. Database Connection
Update the connection string in `DockerLearning.DbMigrator/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
  }
}
```

### 2. API Configuration
Also update `DockerLearningApi/appsettings.json` with the same connection string for the API to connect to the database.

## Running Migrations

### Option 1: Using the DbMigrator Console App
```bash
# Navigate to the migrator project
cd c:\dev\scratchpad\dockerlearning\src\DockerLearning.DbMigrator

# Run the migrator (SQL Server must be running)
dotnet run
```

### Option 2: Using the PowerShell Script
```bash
# From the solution root
.\scripts\Create-Migrations.ps1
```

### Option 3: Integration with API Startup
The API can automatically run migrations on startup (useful for Docker containers):

```csharp
// In Program.cs - migrations run automatically when the API starts
```

## How DbUp Works

1. **Script Execution Order**: Scripts are executed in filename order (0001, 0002, etc.)
2. **Change Tracking**: DbUp creates a `__SchemaVersions` table to track executed scripts
3. **Idempotent**: Only runs scripts that haven't been executed before
4. **Transactional**: Each script runs in its own transaction for safety

## Adding New Migration Scripts

1. Create a new SQL file in `Scripts/` folder with incremental numbering:
   - `0003_AddNewFeature.sql`
   - `0004_UpdatePricing.sql`
   - etc.

2. The script will be automatically embedded as a resource and executed on next run

3. Example new migration:
```sql
-- 0003_AddCategoryColumn.sql
ALTER TABLE Products ADD 
    Category NVARCHAR(50) NOT NULL DEFAULT 'General';
```

## Benefits of DbUp

- ✅ **Version Control**: Migration scripts are stored in source control
- ✅ **Team Collaboration**: Everyone gets the same database schema
- ✅ **Environment Consistency**: Same scripts run in dev, test, and production
- ✅ **Docker Friendly**: Works great in containerized environments
- ✅ **Rollback Support**: Can create down migration scripts if needed

### Next Step
Proceed to [Step 3: Docker Setup](../03-docker-setup/README.md) to containerize our application and database.