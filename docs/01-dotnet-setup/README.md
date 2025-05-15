# Step 1: Setting Up a .NET 8 Web API Project

## Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed
- Visual Studio Code or Visual Studio 2022

## Instructions

### 1. Create a new .NET Web API project:

```bash
# Navigate to the src directory
cd c:\dev\scratchpad\dockerlearning\src

# Create a new Web API project
dotnet new webapi -n DockerLearningApi
```

### 2. Add entity models for our demo:

Create a `Models` folder in your project and add a simple `Product` class:

```csharp
// DockerLearningApi/Models/Product.cs
namespace DockerLearningApi.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}
```

### 3. Test the API locally:

```bash
# Navigate to the API project directory
cd DockerLearningApi

# Run the project
dotnet run
```

Visit https://localhost:7001/swagger (your port may vary) to see the Swagger UI.

### Next Step
Proceed to [Step 2: SQL Server Setup](../02-sql-server-setup/README.md) to configure our database.