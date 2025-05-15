# Step 2: SQL Server Setup

## Overview
We'll use SQL Server (Developer Edition, which is free for non-production use) with Entity Framework Core to store and retrieve product data.

## Instructions

### 1. Add required NuGet packages:

```bash
# Navigate to the API project directory
cd c:\dev\scratchpad\dockerlearning\src\DockerLearningApi

# Add Entity Framework Core packages
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Design
```

### 2. Create a database context:

```csharp
// Data/ApplicationDbContext.cs
using DockerLearningApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DockerLearningApi.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Seed some sample data
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Product 1", Description = "Description for product 1", Price = 10.99m, Stock = 100 },
            new Product { Id = 2, Name = "Product 2", Description = "Description for product 2", Price = 24.99m, Stock = 50 },
            new Product { Id = 3, Name = "Product 3", Description = "Description for product 3", Price = 5.99m, Stock = 200 }
        );
    }
}
```

### 3. Configure the database connection in `Program.cs`:

```csharp
// Add the following to Program.cs before app.Build()
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### 4. Update your `appsettings.json` file to include the connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=ProductsDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"
  },
  // ... other settings ...
}
```

### 5. Create migrations:

```bash
# Create the initial migration
dotnet ef migrations add InitialCreate

# Apply the migration (when SQL Server is available)
dotnet ef database update
```

### Next Step
Proceed to [Step 3: Docker Setup](../03-docker-setup/README.md) to containerize our application and database.