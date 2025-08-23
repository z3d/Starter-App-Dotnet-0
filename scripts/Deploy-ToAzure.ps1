# Azure deployment script for StarterApp API
# This PowerShell script automates the deployment of the StarterApp.Api to Azure

# Define variables
$resourceGroup = "StarterAppRG"
$location = "eastus"
$uniqueId = Get-Random -Minimum 1000 -Maximum 9999
$acrName = "starterappacrr$uniqueId"  # Must be globally unique
$sqlServerName = "starterapp-sql-$uniqueId"
$sqlDatabaseName = "StarterApp"
$sqlAdminLogin = "sqladmin"
$sqlAdminPassword = "StarterApp_Pass123!"  # In production, use Azure Key Vault
$containerAppName = "starterapp-api"
$containerAppEnvName = "starterapp-env"

# Login to Azure (comment out if already logged in)
# az login

# Create a resource group
Write-Host "Creating resource group..." -ForegroundColor Green
az group create --name $resourceGroup --location $location

# Create Azure Container Registry
Write-Host "Creating Azure Container Registry..." -ForegroundColor Green
az acr create --resource-group $resourceGroup --name $acrName --sku Basic

# Enable admin access for registry
Write-Host "Enabling ACR admin access..." -ForegroundColor Green
az acr update --name $acrName --admin-enabled true

# Log in to ACR
Write-Host "Logging in to ACR..." -ForegroundColor Green
az acr login --name $acrName

# Get ACR credentials
$acrLoginServer = az acr show --name $acrName --query loginServer --output tsv
$acrUsername = az acr credential show --name $acrName --query username --output tsv
$acrPassword = az acr credential show --name $acrName --query "passwords[0].value" --output tsv

# Navigate to solution directory
Set-Location -Path "c:\dev\scratchpad\dockerlearning"

# Build and push Docker image to ACR
Write-Host "Building and pushing Docker image to ACR..." -ForegroundColor Green

# Build and tag the API image
docker build -t ${acrLoginServer}/starterapp-api:latest -f ./src/StarterApp.Api/Dockerfile ./src
docker push ${acrLoginServer}/starterapp-api:latest

# Create Azure SQL Server
Write-Host "Creating Azure SQL Server..." -ForegroundColor Green
az sql server create --name $sqlServerName --resource-group $resourceGroup --location $location --admin-user $sqlAdminLogin --admin-password $sqlAdminPassword

# Configure firewall to allow Azure services
Write-Host "Configuring SQL Server firewall..." -ForegroundColor Green
az sql server firewall-rule create --resource-group $resourceGroup --server $sqlServerName --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Create a SQL database
Write-Host "Creating SQL Database..." -ForegroundColor Green
az sql db create --resource-group $resourceGroup --server $sqlServerName --name $sqlDatabaseName --edition Basic --capacity 5

# Create Container Apps Environment
Write-Host "Creating Container App Environment..." -ForegroundColor Green

# Install Container Apps extension if not already installed
az extension add --name containerapp --upgrade --yes

# Register required providers
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait

az containerapp env create --name $containerAppEnvName --resource-group $resourceGroup --location $location

# Create Container App
Write-Host "Creating Container App..." -ForegroundColor Green
az containerapp create --name $containerAppName `
    --resource-group $resourceGroup `
    --environment $containerAppEnvName `
    --image "${acrLoginServer}/starterapp-api:latest" `
    --registry-server $acrLoginServer `
    --registry-username $acrUsername `
    --registry-password $acrPassword `
    --target-port 8080 `
    --ingress external `
    --min-replicas 1 `
    --max-replicas 3 `
    --cpu 0.5 `
    --memory 1.0Gi `
    --env-vars "ASPNETCORE_ENVIRONMENT=Production" "ConnectionStrings__DefaultConnection=Server=${sqlServerName}.database.windows.net;Database=${sqlDatabaseName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};TrustServerCertificate=True;"

# Get the Application URL
$appUrl = az containerapp show --name $containerAppName --resource-group $resourceGroup --query properties.configuration.ingress.fqdn -o tsv

Write-Host "" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "       DEPLOYMENT COMPLETED!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Application URL: https://${appUrl}" -ForegroundColor Yellow
Write-Host "Health Check: https://${appUrl}/health" -ForegroundColor Yellow
Write-Host "Swagger UI: https://${appUrl}/swagger" -ForegroundColor Yellow
Write-Host "" -ForegroundColor Green
Write-Host "Database will be automatically migrated on first startup." -ForegroundColor Cyan
Write-Host "Resource Group: $resourceGroup" -ForegroundColor Cyan
Write-Host "SQL Server: ${sqlServerName}.database.windows.net" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Green