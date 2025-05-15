#!/bin/bash

# Create migrations directory if it doesn't exist
mkdir -p c:\dev\scratchpad\dockerlearning\src\DockerLearningApi\Migrations

# Navigate to the API project directory
cd c:\dev\scratchpad\dockerlearning\src\DockerLearningApi

# Create the initial migration
dotnet ef migrations add InitialCreate

echo "Migrations created successfully. You can apply them with 'dotnet ef database update' when SQL Server is running."