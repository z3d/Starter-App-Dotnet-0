# Step 2: SQL Server Setup with DbUp

## Overview

This project uses SQL Server for data persistence with DbUp for database migrations. DbUp is a .NET library that helps deploy database changes in a controlled, versioned manner. It tracks which SQL scripts have been executed and only runs new migrations.

## Current Status

✅ **Already Configured!** - The database setup is complete with:

- **SQL Server**: Containerized SQL Server 2022
- **Migration System**: DbUp with embedded SQL scripts
- **Connection Management**: Environment-specific connection strings
- **Schema Versioning**: Automatic tracking of applied migrations

## Project Structure

```
DockerLearning.DbMigrator/
├── Program.cs                    # Migration runner console app
├── DatabaseMigrationEngine.cs    # DbUp configuration
├── appsettings.json             # Connection strings
└── Scripts/                     # SQL migration files
    ├── 0001_CreateProductsTable.sql
    └── 0002_AddLastUpdatedColumn.sql
```

## Database Schema

### Products Table (Created by Migration 0001)

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `INT IDENTITY(1,1)` | Primary key |
| `Name` | `NVARCHAR(100)` | Product name |
| `Description` | `NVARCHAR(500)` | Product description |
| `PriceAmount` | `DECIMAL(18,2)` | Price amount |
| `PriceCurrency` | `NVARCHAR(3)` | Currency code (default: USD) |
| `Stock` | `INT` | Stock quantity |
| `LastUpdated` | `DATETIME2` | Last update timestamp (added in Migration 0002) |

### Sample Data
The initial migration includes 3 sample products for testing.

## Connection Strings by Environment

### Local Development (.NET Aspire)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=127.0.0.1,61430;User ID=sa;Password=Your_password123;TrustServerCertificate=true;Initial Catalog=DockerLearning"
  }
}
```

### Docker Compose
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=db;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
  }
}
```

### Azure (Production)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=yourserver.database.windows.net;Database=ProductsDb;User Id=yourusername;Password=yourpassword;TrustServerCertificate=True;"
  }
}
```

## Running Migrations

### Option 1: Automatic (with .NET Aspire)
Migrations run automatically when starting the AppHost:
```powershell
cd src\DockerLearning.AppHost
dotnet run
```

### Option 2: Manual Migration Runner
```powershell
# Navigate to the migrator project
cd src\DockerLearning.DbMigrator

# Run migrations manually
dotnet run
```

### Option 3: PowerShell Script
```powershell
# From solution root
.\scripts\Create-Migrations.ps1
```

## Testing Database Connection

Use the provided PowerShell script to test connectivity:
```powershell
# Test connection with current settings
.\test-connection.ps1
```

This script will:
- Test the connection to SQL Server
- Verify database existence
- Run a simple query to confirm functionality

## Migration Script Examples

### Current Scripts

**0001_CreateProductsTable.sql**
```sql
CREATE TABLE Products (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    PriceAmount DECIMAL(18, 2) NOT NULL,
    PriceCurrency NVARCHAR(3) NOT NULL DEFAULT 'USD',
    Stock INT NOT NULL DEFAULT 0
);

INSERT INTO Products (Name, Description, PriceAmount, PriceCurrency, Stock)
VALUES 
    ('Product 1', 'Description for product 1', 10.99, 'USD', 100),
    ('Product 2', 'Description for product 2', 24.99, 'USD', 50),
    ('Product 3', 'Description for product 3', 5.99, 'USD', 200);
```

**0002_AddLastUpdatedColumn.sql**
```sql
ALTER TABLE Products ADD 
    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE();
```

### Adding New Migrations

To add a new migration:

1. **Create a new SQL file** in `src/DockerLearning.DbMigrator/Scripts/`:
   ```
   0003_AddCategoryColumn.sql
   0004_CreateIndexes.sql
   ```

2. **Write the migration SQL**:
   ```sql
   -- 0003_AddCategoryColumn.sql
   ALTER TABLE Products ADD 
       Category NVARCHAR(50) NOT NULL DEFAULT 'General';
   
   UPDATE Products SET Category = 'Electronics' WHERE Id IN (1, 2);
   UPDATE Products SET Category = 'Books' WHERE Id = 3;
   ```

3. **Set the build action** to `EmbeddedResource` in the `.csproj` file (already configured)

4. **Run migrations** using any of the methods above

## DbUp Features

### ✅ Benefits
- **Version Control**: All schema changes are in source control
- **Team Collaboration**: Everyone gets the same database schema
- **Environment Consistency**: Same scripts run everywhere
- **Docker Friendly**: Works perfectly in containers
- **Transactional Safety**: Each script runs in its own transaction
- **Idempotent**: Safe to run multiple times

### 🔍 How It Works
1. **Tracking Table**: DbUp creates `__SchemaVersions` to track executed scripts
2. **Execution Order**: Scripts run in alphabetical order (hence the numbering)
3. **One-Time Execution**: Each script runs only once per database
4. **Rollback**: Manual rollback scripts can be created if needed

## Troubleshooting

### Common Issues

**Connection Timeouts**
- Ensure SQL Server container is fully started
- Check port mappings (1433 for Docker, 61430 for Aspire)
- Verify firewall settings

**Migration Failures**
- Check SQL syntax in migration files
- Ensure scripts are set to `EmbeddedResource`
- Review `__SchemaVersions` table for partial executions

**Database Not Found**
- Verify connection string format
- Check database creation in Aspire configuration
- Ensure SQL Server is running and accessible

### Useful Queries

```sql
-- Check applied migrations
SELECT * FROM __SchemaVersions ORDER BY Applied;

-- Verify table structure
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Products';

-- Check sample data
SELECT * FROM Products;
```

## Next Step

Continue to **[Step 3: Docker Setup](../03-docker-setup/README.md)** to containerize the application and database using Docker Compose.
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