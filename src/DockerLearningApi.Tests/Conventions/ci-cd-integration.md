# CI/CD Integration Examples for Convention Tests

This folder contains example scripts and configurations for integrating convention tests into various CI/CD pipelines.

## Azure DevOps

```yaml
# azure-pipelines-conventions.yml
trigger:
  branches:
    include:
    - main
    - develop
    - feature/*

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'
  dotNetFramework: 'net9.0'
  dotNetVersion: '9.0.x'

stages:
- stage: ConventionTests
  displayName: 'Architectural Convention Tests'
  jobs:
  - job: RunConventionTests
    displayName: 'Run Convention Tests'
    steps:
    - task: UseDotNet@2
      displayName: 'Use .NET SDK'
      inputs:
        packageType: 'sdk'
        version: $(dotNetVersion)

    - task: DotNetCoreCLI@2
      displayName: 'Restore NuGet packages'
      inputs:
        command: 'restore'
        projects: '**/*.csproj'

    - task: DotNetCoreCLI@2
      displayName: 'Build solution'
      inputs:
        command: 'build'
        projects: '**/*.csproj'
        arguments: '--configuration $(buildConfiguration) --no-restore'

    - task: DotNetCoreCLI@2
      displayName: 'Run Convention Tests'
      inputs:
        command: 'test'
        projects: '**/DockerLearningApi.Tests.csproj'
        arguments: |
          --configuration $(buildConfiguration)
          --filter "FullyQualifiedName~ConventionTests"
          --logger trx
          --logger "console;verbosity=detailed"
          --collect "XPlat Code Coverage"
          --settings "src/DockerLearningApi.Tests/Conventions/convention-tests.runsettings"
        publishTestResults: true

    - task: PublishCodeCoverageResults@1
      displayName: 'Publish Code Coverage'
      inputs:
        codeCoverageTool: 'Cobertura'
        summaryFileLocation: '$(Agent.TempDirectory)/**/coverage.cobertura.xml'
      condition: succeededOrFailed()

    - task: PowerShell@2
      displayName: 'Convention Test Summary'
      inputs:
        targetType: 'inline'
        script: |
          Write-Host "Convention Tests completed!" -ForegroundColor Green
          Write-Host "These tests ensure architectural consistency across the solution." -ForegroundColor White
          Write-Host "For more information, see: src/DockerLearningApi.Tests/Conventions/README.md" -ForegroundColor Gray
      condition: always()
```

## GitHub Actions

```yaml
# .github/workflows/convention-tests.yml
name: Convention Tests

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

env:
  DOTNET_VERSION: '9.0.x'
  BUILD_CONFIGURATION: 'Release'

jobs:
  convention-tests:
    name: Run Architectural Convention Tests
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Cache NuGet packages
      uses: actions/cache@v3
      with:
        path: ~/.nuget/packages
        key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
        restore-keys: |
          ${{ runner.os }}-nuget-

    - name: Restore dependencies
      run: dotnet restore

    - name: Build solution
      run: dotnet build --configuration ${{ env.BUILD_CONFIGURATION }} --no-restore

    - name: Run Convention Tests
      run: |
        dotnet test \
          --configuration ${{ env.BUILD_CONFIGURATION }} \
          --filter "FullyQualifiedName~ConventionTests" \
          --logger "trx;LogFileName=convention-tests.trx" \
          --logger "console;verbosity=detailed" \
          --collect "XPlat Code Coverage" \
          --settings "src/DockerLearningApi.Tests/Conventions/convention-tests.runsettings"

    - name: Publish Test Results
      uses: dorny/test-reporter@v1
      if: always()
      with:
        name: Convention Test Results
        path: '**/convention-tests.trx'
        reporter: dotnet-trx

    - name: Upload Coverage Reports
      uses: codecov/codecov-action@v3
      if: always()
      with:
        file: '**/coverage.cobertura.xml'
        flags: convention-tests
        name: convention-tests-coverage

    - name: Convention Test Summary
      if: always()
      run: |
        echo "::notice title=Convention Tests::Architectural convention tests help maintain code quality and consistency"
        echo "::notice title=Documentation::See src/DockerLearningApi.Tests/Conventions/README.md for details"
```

## Jenkins

```groovy
// Jenkinsfile.conventions
pipeline {
    agent any
    
    parameters {
        choice(
            name: 'TEST_FILTER',
            choices: [
                'FullyQualifiedName~ConventionTests',
                'FullyQualifiedName~Controllers_ShouldFollowNamingConventions',
                'FullyQualifiedName~DomainEntities_ShouldHaveProperEncapsulation'
            ],
            description: 'Convention test filter'
        )
        booleanParam(
            name: 'COLLECT_COVERAGE',
            defaultValue: true,
            description: 'Collect code coverage'
        )
    }
    
    environment {
        DOTNET_VERSION = '9.0.x'
        BUILD_CONFIGURATION = 'Release'
    }
    
    stages {
        stage('Setup') {
            steps {
                script {
                    // Install .NET SDK
                    sh 'wget https://dot.net/v1/dotnet-install.sh'
                    sh 'chmod +x dotnet-install.sh'
                    sh "./dotnet-install.sh --version ${DOTNET_VERSION}"
                }
            }
        }
        
        stage('Restore & Build') {
            parallel {
                stage('Restore') {
                    steps {
                        sh 'dotnet restore'
                    }
                }
                stage('Build') {
                    steps {
                        sh "dotnet build --configuration ${BUILD_CONFIGURATION} --no-restore"
                    }
                }
            }
        }
        
        stage('Convention Tests') {
            steps {
                script {
                    def testArgs = [
                        "--configuration ${BUILD_CONFIGURATION}",
                        "--filter \"${params.TEST_FILTER}\"",
                        "--logger \"trx;LogFileName=convention-tests.trx\"",
                        "--logger \"console;verbosity=detailed\""
                    ]
                    
                    if (params.COLLECT_COVERAGE) {
                        testArgs.add("--collect \"XPlat Code Coverage\"")
                        testArgs.add("--settings \"src/DockerLearningApi.Tests/Conventions/convention-tests.runsettings\"")
                    }
                    
                    sh "dotnet test ${testArgs.join(' ')}"
                }
            }
            post {
                always {
                    // Publish test results
                    publishTestResults testResultsPattern: '**/convention-tests.trx'
                    
                    // Publish coverage if collected
                    script {
                        if (params.COLLECT_COVERAGE) {
                            publishCoverage adapters: [
                                coberturaAdapter('**/coverage.cobertura.xml')
                            ], sourceFileResolver: sourceFiles('STORE_LAST_BUILD')
                        }
                    }
                }
                success {
                    echo 'Convention tests passed! Architecture integrity maintained.'
                }
                failure {
                    echo 'Convention tests failed! Please check architectural violations.'
                    echo 'See documentation: src/DockerLearningApi.Tests/Conventions/README.md'
                }
            }
        }
    }
    
    post {
        always {
            cleanWs()
        }
    }
}
```

## GitLab CI

```yaml
# .gitlab-ci.yml (convention tests section)
stages:
  - build
  - test
  - convention-tests

variables:
  DOTNET_VERSION: "9.0"
  BUILD_CONFIGURATION: "Release"

convention-tests:
  stage: convention-tests
  image: mcr.microsoft.com/dotnet/sdk:9.0
  
  before_script:
    - dotnet --version
    - dotnet restore
    
  script:
    - dotnet build --configuration $BUILD_CONFIGURATION --no-restore
    - |
      dotnet test \
        --configuration $BUILD_CONFIGURATION \
        --filter "FullyQualifiedName~ConventionTests" \
        --logger "junit;LogFilePath=convention-tests.xml" \
        --logger "console;verbosity=detailed" \
        --collect "XPlat Code Coverage" \
        --settings "src/DockerLearningApi.Tests/Conventions/convention-tests.runsettings"
        
  artifacts:
    when: always
    reports:
      junit: "**/convention-tests.xml"
      coverage_report:
        coverage_format: cobertura
        path: "**/coverage.cobertura.xml"
    paths:
      - "**/TestResults/"
    expire_in: 1 week
    
  rules:
    - if: $CI_PIPELINE_SOURCE == "merge_request_event"
    - if: $CI_COMMIT_BRANCH == "main"
    - if: $CI_COMMIT_BRANCH == "develop"
    
  after_script:
    - echo "Convention tests completed"
    - echo "For troubleshooting, see src/DockerLearningApi.Tests/Conventions/README.md"
```

## Pre-commit Hook

```bash
#!/bin/sh
# .git/hooks/pre-commit
# Run convention tests before allowing commits

echo "Running convention tests..."

# Navigate to repository root
cd "$(git rev-parse --show-toplevel)"

# Run convention tests
dotnet test \
  --configuration Debug \
  --filter "FullyQualifiedName~ConventionTests" \
  --logger "console;verbosity=minimal" \
  --no-build

exit_code=$?

if [ $exit_code -ne 0 ]; then
    echo ""
    echo "❌ Convention tests failed!"
    echo "Commit aborted. Please fix architectural violations before committing."
    echo "Run 'src/DockerLearningApi.Tests/Conventions/run-convention-tests.ps1 -Verbose' for details."
    echo ""
    exit 1
fi

echo "✅ Convention tests passed!"
exit 0
```

## Docker Integration

```dockerfile
# Dockerfile.convention-tests
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS test

WORKDIR /src

# Copy project files
COPY ["src/DockerLearningApi.Tests/DockerLearningApi.Tests.csproj", "src/DockerLearningApi.Tests/"]
COPY ["src/DockerLearningApi/DockerLearningApi.csproj", "src/DockerLearningApi/"]
COPY ["src/DockerLearning.Domain/DockerLearning.Domain.csproj", "src/DockerLearning.Domain/"]

# Restore dependencies
RUN dotnet restore "src/DockerLearningApi.Tests/DockerLearningApi.Tests.csproj"

# Copy source code
COPY . .

# Build the solution
RUN dotnet build --configuration Release --no-restore

# Run convention tests
RUN dotnet test \
    --configuration Release \
    --filter "FullyQualifiedName~ConventionTests" \
    --logger "trx;LogFileName=/test-results/convention-tests.trx" \
    --logger "console;verbosity=detailed" \
    --collect "XPlat Code Coverage" \
    --settings "src/DockerLearningApi.Tests/Conventions/convention-tests.runsettings" \
    --results-directory /test-results

# Export test results
FROM scratch AS export-results
COPY --from=test /test-results /
```

## Usage Examples

### Local Development
```bash
# Quick convention check
./src/DockerLearningApi.Tests/Conventions/run-convention-tests.ps1

# With coverage
./src/DockerLearningApi.Tests/Conventions/run-convention-tests.ps1 -Coverage

# Watch mode for TDD
./src/DockerLearningApi.Tests/Conventions/run-convention-tests.ps1 -Watch
```

### CI/CD Integration
```bash
# Azure DevOps
az pipelines run --name "Convention Tests" --branch feature/new-feature

# GitHub Actions
gh workflow run convention-tests.yml --ref feature/new-feature

# GitLab
gitlab-ci-multi-runner exec docker convention-tests
```
