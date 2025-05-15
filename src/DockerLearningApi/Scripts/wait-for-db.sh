#!/bin/bash

set -e

echo "Waiting for SQL Server to start..."
sleep 10

echo "Running database migrations..."
dotnet ef database update --context ApplicationDbContext

echo "SQL Server is up and migrations are applied."

exec "$@"