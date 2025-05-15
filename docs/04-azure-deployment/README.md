# Step 4: Azure Deployment

## Overview
In this step, we'll deploy our containerized .NET Web API and SQL Server to Azure using Azure Container Registry (ACR) and Azure Container Apps.

## Prerequisites
- An Azure account with an active subscription
- Azure CLI installed and logged in
- Docker Desktop running

## Instructions

### 1. Create an Azure Resource Group:

```bash
# Set variables for resource names
$resourceGroup = "DockerLearningGroup"
$location = "eastus"
$acrName = "dockerlearningacr"  # Must be globally unique

# Create resource group
az group create --name $resourceGroup --location $location
```

### 2. Create an Azure Container Registry (ACR):

```bash
# Create ACR
az acr create --resource-group $resourceGroup --name $acrName --sku Basic

# Log in to ACR
az acr login --name $acrName
```

### 3. Tag and push your Docker images to ACR:

```bash
# Get ACR login server
$acrLoginServer = $(az acr show --name $acrName --query loginServer --output tsv)

# Tag the images
docker tag dockerlearning-api:latest $acrLoginServer/dockerlearning-api:latest
docker tag mcr.microsoft.com/mssql/server:2022-latest $acrLoginServer/mssql-server:latest

# Push images to ACR
docker push $acrLoginServer/dockerlearning-api:latest
docker push $acrLoginServer/mssql-server:latest
```

### 4. Create an Azure SQL Database:

```bash
# Set SQL Server variables
$sqlServerName = "dockerlearning-sql"
$sqlAdminLogin = "sqladmin"
$sqlAdminPassword = "YourStrong@Passw0rd"  # Use a secure password in production

# Create SQL Server
az sql server create --name $sqlServerName --resource-group $resourceGroup --location $location --admin-user $sqlAdminLogin --admin-password $sqlAdminPassword

# Create a firewall rule to allow Azure services
az sql server firewall-rule create --resource-group $resourceGroup --server $sqlServerName --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Create a database
az sql db create --resource-group $resourceGroup --server $sqlServerName --name ProductsDb --edition Basic --capacity 5
```

### 5. Create an Azure Container App:

```bash
# Create Container App Environment
az containerapp env create --name "dockerlearning-env" --resource-group $resourceGroup --location $location

# Create Container App
az containerapp create --name "dockerlearning-api" \
    --resource-group $resourceGroup \
    --environment "dockerlearning-env" \
    --image "$acrLoginServer/dockerlearning-api:latest" \
    --registry-server "$acrLoginServer" \
    --target-port 8080 \
    --ingress external \
    --query properties.configuration.ingress.fqdn

# Set environment variables for the Container App
az containerapp update --name "dockerlearning-api" \
    --resource-group $resourceGroup \
    --set-env-vars "ConnectionStrings__DefaultConnection=Server=$sqlServerName.database.windows.net;Database=ProductsDb;User Id=$sqlAdminLogin;Password=$sqlAdminPassword;TrustServerCertificate=True;"
```

### 6. Initialize the database:

Execute the Entity Framework migrations either by:
- Setting up a one-time initialization container
- Running migrations manually from your local machine against the Azure SQL Database
- Using a CI/CD pipeline to run migrations during deployment

```bash
# Example of running migrations from local machine
$connectionString = "Server=$sqlServerName.database.windows.net;Database=ProductsDb;User Id=$sqlAdminLogin;Password=$sqlAdminPassword;TrustServerCertificate=True;"
dotnet ef database update --connection "$connectionString" --project ./src/DockerLearningApi
```

### 7. Access your deployed API:

Use the FQDN (Fully Qualified Domain Name) provided by the Container App creation command to access your API:

```
https://dockerlearning-api.{region}.azurecontainer.io
```

### 8. Set up continuous deployment (optional):

Set up Azure DevOps or GitHub Actions to automatically build and deploy your container when code changes are pushed to your repository.

## Clean-up Resources

When you're done with the resources, clean them up to avoid incurring charges:

```bash
# Delete resource group (deletes all resources in the group)
az group delete --name $resourceGroup --yes
```