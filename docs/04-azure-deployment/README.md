# Step 4: Azure Deployment with Container Apps

## Overview

This step demonstrates deploying the containerized .NET Web API to Azure using Azure Container Apps, Azure Container Registry (ACR), and Azure SQL Database. This provides a scalable, serverless container hosting solution.

## Architecture

```
Azure Environment
├── Azure Container Registry (ACR)
│   └── dockerlearning-api:latest
├── Azure Container Apps Environment
│   ├── Container App (API)
│   └── Log Analytics Workspace
├── Azure SQL Database
│   └── ProductsDb
└── Resource Group
    └── All resources
```

## Prerequisites

- Azure account with active subscription
- Azure CLI installed and configured
- Docker Desktop running locally
- PowerShell (for running deployment scripts)

### Install Azure CLI
```powershell
# Install Azure CLI (if not already installed)
winget install Microsoft.AzureCLI

# Login to Azure
az login

# Set subscription (if you have multiple)
az account set --subscription "Your-Subscription-Name"
```

## Deployment Options

### Option 1: Automated Script (Recommended)

Use the provided PowerShell script for easy deployment:

```powershell
# From solution root
.\scripts\Deploy-ToAzure.ps1
```

This script will:
- ✅ Create resource group
- ✅ Create Azure Container Registry
- ✅ Build and push Docker images
- ✅ Create Azure SQL Database
- ✅ Create Container Apps Environment
- ✅ Deploy the API as a Container App
- ✅ Configure environment variables

### Option 2: Manual Step-by-Step

Follow these steps for manual deployment:

## Manual Deployment Steps

### 1. Set Variables and Create Resource Group

```powershell
# Configuration variables
$resourceGroup = "DockerLearningGroup"
$location = "eastus"
$acrName = "dockerlearningacr$(Get-Random)"  # Must be globally unique
$containerAppName = "dockerlearning-api"
$environmentName = "dockerlearning-env"
$sqlServerName = "dockerlearning-sql-$(Get-Random)"
$sqlDatabaseName = "ProductsDb"
$sqlAdminLogin = "sqladmin"
$sqlAdminPassword = "YourStrong@Passw0rd123"  # Use secure password

# Create resource group
az group create --name $resourceGroup --location $location
```

### 2. Create Azure Container Registry

```powershell
# Create ACR
az acr create --resource-group $resourceGroup --name $acrName --sku Basic

# Enable admin access (for simplicity - use service principal in production)
az acr update --name $acrName --admin-enabled true

# Get login credentials
$acrLoginServer = $(az acr show --name $acrName --query loginServer --output tsv)
$acrPassword = $(az acr credential show --name $acrName --query "passwords[0].value" --output tsv)

# Login to ACR
az acr login --name $acrName
```

### 3. Build and Push Docker Images

```powershell
# Build the Docker image locally
docker build -t $acrLoginServer/dockerlearning-api:latest -f src/DockerLearningApi/Dockerfile src/

# Push to ACR
docker push $acrLoginServer/dockerlearning-api:latest

# Verify image was pushed
az acr repository list --name $acrName --output table
```

### 4. Create Azure SQL Database

```powershell
# Create SQL Server
az sql server create `
    --name $sqlServerName `
    --resource-group $resourceGroup `
    --location $location `
    --admin-user $sqlAdminLogin `
    --admin-password $sqlAdminPassword

# Configure firewall to allow Azure services
az sql server firewall-rule create `
    --resource-group $resourceGroup `
    --server $sqlServerName `
    --name AllowAzureServices `
    --start-ip-address 0.0.0.0 `
    --end-ip-address 0.0.0.0

# Create the database
az sql db create `
    --resource-group $resourceGroup `
    --server $sqlServerName `
    --name $sqlDatabaseName `
    --edition Basic `
    --capacity 5
```

### 5. Create Container Apps Environment

```powershell
# Install Container Apps extension
az extension add --name containerapp --upgrade

# Register providers
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights

# Create Container Apps environment
az containerapp env create `
    --name $environmentName `
    --resource-group $resourceGroup `
    --location $location
```

### 6. Deploy Container App

```powershell
# Create the container app
az containerapp create `
    --name $containerAppName `
    --resource-group $resourceGroup `
    --environment $environmentName `
    --image "$acrLoginServer/dockerlearning-api:latest" `
    --registry-server $acrLoginServer `
    --registry-username $acrName `
    --registry-password $acrPassword `
    --target-port 8080 `
    --ingress external `
    --min-replicas 1 `
    --max-replicas 5 `
    --cpu 0.5 `
    --memory 1Gi `
    --env-vars "ASPNETCORE_ENVIRONMENT=Production" "ConnectionStrings__DefaultConnection=Server=$sqlServerName.database.windows.net;Database=$sqlDatabaseName;User Id=$sqlAdminLogin;Password=$sqlAdminPassword;TrustServerCertificate=True;"

# Get the application URL
$appUrl = $(az containerapp show --name $containerAppName --resource-group $resourceGroup --query "properties.configuration.ingress.fqdn" --output tsv)
Write-Host "Application URL: https://$appUrl"
```

### 7. Initialize Database

The application will automatically run database migrations on startup, but you can also run them manually:

```powershell
# Option 1: Automatic (migrations run on app startup)
# Just access the application URL

# Option 2: Manual migration from local machine
$connectionString = "Server=$sqlServerName.database.windows.net;Database=$sqlDatabaseName;User Id=$sqlAdminLogin;Password=$sqlAdminPassword;TrustServerCertificate=True;"

# Run from local machine (requires .NET SDK)
cd src/DockerLearning.DbMigrator
dotnet run --ConnectionStrings:DefaultConnection="$connectionString"
```

## Testing the Deployment

### Access the Application

Once deployed, you can access:

| Endpoint | URL | Description |
|----------|-----|-------------|
| **API Root** | `https://your-app.region.azurecontainerapps.io` | API base URL |
| **Swagger UI** | `https://your-app.region.azurecontainerapps.io/swagger` | API documentation |
| **Health Check** | `https://your-app.region.azurecontainerapps.io/health` | Health status |
| **Products API** | `https://your-app.region.azurecontainerapps.io/api/products` | Products endpoint |

### Test the API

```powershell
# Test health endpoint
$healthUrl = "https://$appUrl/health"
Invoke-RestMethod -Uri $healthUrl

# Test products endpoint
$productsUrl = "https://$appUrl/api/products"
Invoke-RestMethod -Uri $productsUrl

# Create a new product
$newProduct = @{
    name = "Test Product"
    description = "Created via API"
    priceAmount = 29.99
    priceCurrency = "USD"
    stock = 10
} | ConvertTo-Json

Invoke-RestMethod -Uri $productsUrl -Method POST -Body $newProduct -ContentType "application/json"
```

## Configuration Management

### Environment Variables

The Container App is configured with:

```yaml
Environment Variables:
- ASPNETCORE_ENVIRONMENT: Production
- ConnectionStrings__DefaultConnection: [Azure SQL connection string]
```

### Security Best Practices

For production deployments:

1. **Use Key Vault for secrets:**
   ```powershell
   # Create Key Vault
   az keyvault create --name "dockerlearning-kv" --resource-group $resourceGroup --location $location
   
   # Store connection string
   az keyvault secret set --vault-name "dockerlearning-kv" --name "ConnectionString" --value $connectionString
   ```

2. **Use Managed Identity:**
   ```powershell
   # Enable system-assigned identity
   az containerapp identity assign --name $containerAppName --resource-group $resourceGroup --system-assigned
   ```

3. **Configure proper RBAC permissions**

## Monitoring and Observability

### Application Insights

```powershell
# Create Application Insights
az monitor app-insights component create `
    --app dockerlearning-insights `
    --location $location `
    --resource-group $resourceGroup

# Get instrumentation key
$instrumentationKey = $(az monitor app-insights component show --app dockerlearning-insights --resource-group $resourceGroup --query "instrumentationKey" --output tsv)

# Update container app with Application Insights
az containerapp update `
    --name $containerAppName `
    --resource-group $resourceGroup `
    --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=$instrumentationKey"
```

### Log Analytics

Container Apps automatically integrate with Log Analytics for centralized logging:

```powershell
# Query container logs
az containerapp logs show --name $containerAppName --resource-group $resourceGroup

# Stream live logs
az containerapp logs show --name $containerAppName --resource-group $resourceGroup --follow
```

## Scaling Configuration

### Auto-scaling Rules

```powershell
# Configure HTTP-based scaling
az containerapp update `
    --name $containerAppName `
    --resource-group $resourceGroup `
    --min-replicas 1 `
    --max-replicas 10 `
    --scale-rule-name "http-scale" `
    --scale-rule-type "http" `
    --scale-rule-http-concurrency 50
```

### Manual Scaling

```powershell
# Scale to specific number of replicas
az containerapp replica set --name $containerAppName --resource-group $resourceGroup --replica-count 3
```

## Cost Optimization

### Resource Sizing
- **Development**: 0.25 CPU, 0.5Gi memory
- **Production**: 0.5+ CPU, 1Gi+ memory

### SQL Database Tiers
- **Development**: Basic (5 DTU)
- **Production**: Standard S1+ or Premium

### Container Apps Pricing
- Pay per vCPU-second and GiB-second
- No charges when scaled to zero
- Free allowance included

## Troubleshooting

### Common Issues

**Container App Won't Start:**
```powershell
# Check container app status
az containerapp show --name $containerAppName --resource-group $resourceGroup --query "properties.runningStatus"

# View application logs
az containerapp logs show --name $containerAppName --resource-group $resourceGroup
```

**Database Connection Issues:**
```powershell
# Test connection from local machine
Test-NetConnection -ComputerName "$sqlServerName.database.windows.net" -Port 1433

# Check firewall rules
az sql server firewall-rule list --resource-group $resourceGroup --server $sqlServerName
```

**Image Pull Errors:**
```powershell
# Check ACR credentials
az acr credential show --name $acrName

# Verify image exists
az acr repository show --name $acrName --repository dockerlearning-api
```

## Cleanup Resources

To avoid ongoing charges, clean up Azure resources when done:

```powershell
# Delete entire resource group (removes all resources)
az group delete --name $resourceGroup --yes --no-wait

# Or delete individual resources
az containerapp delete --name $containerAppName --resource-group $resourceGroup
az sql server delete --name $sqlServerName --resource-group $resourceGroup
az acr delete --name $acrName --resource-group $resourceGroup
```

## CI/CD Integration

For continuous deployment, consider:

- **GitHub Actions** with Azure Container Apps deployment
- **Azure DevOps** pipelines
- **Automated image builds** on code changes

Example GitHub Actions workflow available in `.github/workflows/` (if implemented).

## Next Step

Continue to **[Step 5: Aspire Setup](../05-aspire-setup/README.md)** to learn about .NET Aspire as an alternative local development orchestration tool.