# Script to create a new DbUp SQL migration script
param(
    [Parameter(Mandatory=$true)]
    [string]$MigrationName
)

# Define directories
$sqlScriptsDir = "c:\dev\scratchpad\dockerlearning\src\DockerLearningApi\SqlScripts"
if (-not (Test-Path $sqlScriptsDir)) {
    New-Item -ItemType Directory -Path $sqlScriptsDir | Out-Null
}

# Get current scripts to determine next number
$existingScripts = Get-ChildItem -Path $sqlScriptsDir -Filter "*.sql" | Sort-Object Name
$nextNumber = 1

if ($existingScripts.Count -gt 0) {
    # Extract highest number from existing scripts
    $lastScript = $existingScripts | Select-Object -Last 1
    if ($lastScript.Name -match '^(\d+)_') {
        $nextNumber = [int]$matches[1] + 1
    }
}

# Format the new file name with padded zeros
$newFileName = "{0:D4}_{1}.sql" -f $nextNumber, $MigrationName
$newFilePath = Join-Path -Path $sqlScriptsDir -ChildPath $newFileName

# Create the new SQL script file with a template
@"
-- Migration: $MigrationName
-- Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

-- Your SQL migration script here

"@ | Out-File -FilePath $newFilePath -Encoding utf8

Write-Output "Created new DbUp SQL migration script: $newFilePath"
Write-Output "Edit this file to add your database changes."