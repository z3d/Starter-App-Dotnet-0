#!/bin/bash

set -e

echo "Waiting for SQL Server to start..."
sleep 10

echo "Database migrations will be applied automatically on application startup via DbUp."
echo "SQL Server is ready. Starting application..."

exec "$@"