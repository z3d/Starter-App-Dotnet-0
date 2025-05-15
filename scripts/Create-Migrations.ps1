# Create migrations directory if it doesn't exist
$migrationsDir = "c:\dev\scratchpad\dockerlearning\src\DockerLearningApi\Migrations"
if (-not (Test-Path $migrationsDir)) {
    New-Item -ItemType Directory -Path $migrationsDir | Out-Null
}

# Navigate to the API project directory
Set-Location -Path "c:\dev\scratchpad\dockerlearning\src\DockerLearningApi"

# Create the initial migration
dotnet ef migrations add InitialCreate

Write-Output "Migrations created successfully. You can apply them with 'dotnet ef database update' when SQL Server is running."