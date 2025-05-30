# Convention Tests Runner
# This script provides an easy way to run convention tests locally with various options

param(
    [string]$TestFilter = "FullyQualifiedName~ConventionTests",
    [string]$Configuration = "Debug",
    [switch]$Coverage,
    [switch]$Verbose,
    [switch]$Watch,
    [string]$Logger = "console;verbosity=normal"
)

# Set script location as working directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Navigate to solution root (assuming script is in Tests/Conventions folder)
$SolutionRoot = Resolve-Path "../../.."
Set-Location $SolutionRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "     Convention Tests Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Solution Root: $SolutionRoot" -ForegroundColor Gray
Write-Host "Test Filter: $TestFilter" -ForegroundColor Gray
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host ""

# Build the test command
$testCommand = "dotnet test"
$testArgs = @(
    "--configuration", $Configuration,
    "--filter", "`"$TestFilter`"",
    "--logger", "`"$Logger`""
)

# Add coverage collection if requested
if ($Coverage) {
    Write-Host "Code coverage collection enabled" -ForegroundColor Yellow
    $testArgs += "--collect", "`"XPlat Code Coverage`""
    $testArgs += "--settings", "`"src/DockerLearningApi.Tests/Conventions/convention-tests.runsettings`""
}

# Add verbose output if requested
if ($Verbose) {
    Write-Host "Verbose output enabled" -ForegroundColor Yellow
    $testArgs += "--verbosity", "detailed"
}

# Function to run tests
function Invoke-ConventionTests {
    Write-Host "Running convention tests..." -ForegroundColor Green
    
    $fullCommand = "$testCommand $($testArgs -join ' ')"
    Write-Host "Command: $fullCommand" -ForegroundColor Gray
    Write-Host ""
    
    # Execute the test command
    & dotnet test @testArgs
    
    $exitCode = $LASTEXITCODE
    
    Write-Host ""
    if ($exitCode -eq 0) {
        Write-Host "✅ All convention tests passed!" -ForegroundColor Green
    } else {
        Write-Host "❌ Convention tests failed!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Common Solutions:" -ForegroundColor Yellow
        Write-Host "- Check class naming conventions (Controller, Service, Repository, etc.)" -ForegroundColor White
        Write-Host "- Ensure domain entities have private setters" -ForegroundColor White
        Write-Host "- Verify async methods have 'Async' suffix" -ForegroundColor White
        Write-Host "- Confirm DTOs have public getters" -ForegroundColor White
        Write-Host ""
        Write-Host "For detailed documentation, see:" -ForegroundColor Yellow
        Write-Host "src/DockerLearningApi.Tests/Conventions/README.md" -ForegroundColor White
    }
    
    return $exitCode
}

# Watch mode for continuous testing during development
if ($Watch) {
    Write-Host "Watch mode enabled - tests will re-run on code changes" -ForegroundColor Yellow
    Write-Host "Press Ctrl+C to stop watching" -ForegroundColor Gray
    Write-Host ""
    
    try {
        while ($true) {
            $result = Invoke-ConventionTests
            
            Write-Host ""
            Write-Host "Watching for changes... (Press Ctrl+C to exit)" -ForegroundColor Gray
            
            # Watch for file changes in source directories
            $watcher = New-Object System.IO.FileSystemWatcher
            $watcher.Path = "src"
            $watcher.IncludeSubdirectories = $true
            $watcher.Filter = "*.cs"
            $watcher.EnableRaisingEvents = $true
            
            # Wait for a file change
            $changed = $false
            Register-ObjectEvent -InputObject $watcher -EventName "Changed" -Action {
                $Global:changed = $true
            } | Out-Null
            
            while (-not $changed) {
                Start-Sleep -Milliseconds 500
            }
            
            $watcher.Dispose()
            Write-Host "Change detected, re-running tests..." -ForegroundColor Yellow
            Write-Host ""
        }
    }
    catch {
        Write-Host "Watch mode interrupted" -ForegroundColor Yellow
    }
} else {
    # Single test run
    $exitCode = Invoke-ConventionTests
    exit $exitCode
}

# Examples of usage:
<#
# Run all convention tests
.\run-convention-tests.ps1

# Run specific convention test
.\run-convention-tests.ps1 -TestFilter "FullyQualifiedName~Controllers_ShouldFollowNamingConventions"

# Run with code coverage
.\run-convention-tests.ps1 -Coverage

# Run in watch mode for development
.\run-convention-tests.ps1 -Watch

# Run with verbose output
.\run-convention-tests.ps1 -Verbose

# Combine options
.\run-convention-tests.ps1 -Coverage -Verbose -TestFilter "FullyQualifiedName~ConventionTests"
#>
