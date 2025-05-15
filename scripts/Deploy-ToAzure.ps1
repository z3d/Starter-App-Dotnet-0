# Azure deployment script for Docker Learning API
# This PowerShell script automates the deployment of the DockerLearningApi to Azure

# Define variables
$resourceGroup = "DockerLearningRG"
$location = "eastus"
$acrName = "dockerlearningacr"  # Must be globally unique
$sqlServerName = "dockerlearningsql"
$sqlDatabaseName = "ProductsDb"
$sqlAdminLogin = "sqladmin"
$sqlAdminPassword = "YourStrongPassword123!"  # In production, use a secure method to handle passwords
$containerAppName = "dockerlearningapi"
$containerAppEnvName = "dockerlearningenv"

# Login to Azure (comment out if already logged in)
# az login

# Create a resource group
Write-Host "Creating resource group..." -ForegroundColor Green
az group create --name $resourceGroup --location $location

# Create Azure Container Registry
Write-Host "Creating Azure Container Registry..." -ForegroundColor Green
az acr create --resource-group $resourceGroup --name $acrName --sku Basic

# Log in to ACR
Write-Host "Logging in to ACR..." -ForegroundColor Green
az acr login --name $acrName

# Build and push Docker image to ACR
Write-Host "Building and pushing Docker image to ACR..." -ForegroundColor Green
$acrLoginServer = az acr show --name $acrName --query loginServer --output tsv

# Navigate to solution directory
Set-Location -Path "c:\dev\scratchpad\dockerlearning"

# Build and tag the API image
docker build -t ${acrLoginServer}/dockerlearningapi:latest -f ./src/DockerLearningApi/Dockerfile ./src/DockerLearningApi
docker push ${acrLoginServer}/dockerlearningapi:latest

# Create Azure SQL Server
Write-Host "Creating Azure SQL Server..." -ForegroundColor Green
az sql server create --name $sqlServerName --resource-group $resourceGroup --location $location --admin-user $sqlAdminLogin --admin-password $sqlAdminPassword

# Configure firewall to allow Azure services
Write-Host "Configuring SQL Server firewall..." -ForegroundColor Green
az sql server firewall-rule create --resource-group $resourceGroup --server $sqlServerName --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Create a SQL database
Write-Host "Creating SQL Database..." -ForegroundColor Green
az sql db create --resource-group $resourceGroup --server $sqlServerName --name $sqlDatabaseName --edition Basic --capacity 5

# Create Azure Container App Environment
Write-Host "Creating Container App Environment..." -ForegroundColor Green
az containerapp env create --name $containerAppEnvName --resource-group $resourceGroup --location $location

# Create Container App
Write-Host "Creating Container App..." -ForegroundColor Green
az containerapp create --name $containerAppName `
    --resource-group $resourceGroup `
    --environment $containerAppEnvName `
    --image "${acrLoginServer}/dockerlearningapi:latest" `
    --registry-server $acrLoginServer `
    --target-port 8080 `
    --ingress external `
    --env-vars "ASPNETCORE_ENVIRONMENT=Production" "ConnectionStrings__DefaultConnection=Server=${sqlServerName}.database.windows.net;Database=${sqlDatabaseName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};TrustServerCertificate=True;"

# Get the Application URL
$appUrl = az containerapp show --name $containerAppName --resource-group $resourceGroup --query properties.configuration.ingress.fqdn -o tsv
Write-Host "Application deployed successfully to: https://${appUrl}" -ForegroundColor Green