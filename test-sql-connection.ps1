# Test SQL Server connection with the exact connection string from Aspire
$connectionString = "Server=127.0.0.1,62878;User ID=sa;Password=Your_password123;TrustServerCertificate=true;Initial Catalog=DockerLearning"

Write-Host "Testing SQL Server connection..." -ForegroundColor Yellow
Write-Host "Connection string: $connectionString" -ForegroundColor Cyan

try {
    # Load SQL Client
    Add-Type -AssemblyName "System.Data.SqlClient"
    
    # Create and open connection
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connection successful!" -ForegroundColor Green
    
    # Test a simple query
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT @@VERSION"
    $result = $command.ExecuteScalar()
    
    Write-Host "SQL Server Version: $result" -ForegroundColor Green
    
    $connection.Close()
}
catch {
    Write-Host "❌ Connection failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Error details: $($_.Exception.InnerException.Message)" -ForegroundColor Red
}
