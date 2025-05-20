#!/bin/bash

set -e

echo "Waiting for SQL Server to start..."

# Set connection timeout variables
MAX_RETRIES=60
RETRY_INTERVAL=5
RETRY_COUNT=0

# Extract host and port from connection string if needed
DB_HOST="db"
DB_PORT="1433"

# Wait for database to be ready
until /opt/mssql-tools/bin/sqlcmd -S ${DB_HOST},${DB_PORT} -U sa -P "YourStrong@Passw0rd" -Q "SELECT 1" &> /dev/null || [ $RETRY_COUNT -eq $MAX_RETRIES ]; do
    echo "SQL Server is starting up. Attempt ${RETRY_COUNT}/${MAX_RETRIES}..."
    RETRY_COUNT=$((RETRY_COUNT+1))
    sleep $RETRY_INTERVAL
done

if [ $RETRY_COUNT -eq $MAX_RETRIES ]; then
    echo "Timed out waiting for SQL Server to start. The application will continue, but may fail if database is not available."
else
    echo "SQL Server is now accepting connections!"
    
    # Wait a bit more to ensure SQL Server is fully initialized
    echo "Giving SQL Server a moment to fully initialize..."
    sleep 5
    
    # Create the database if it doesn't exist
    echo "Ensuring ProductsDb database exists..."
    /opt/mssql-tools/bin/sqlcmd -S ${DB_HOST},${DB_PORT} -U sa -P "YourStrong@Passw0rd" -Q "IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ProductsDb') CREATE DATABASE ProductsDb"
fi

echo "Database migrations will be applied automatically on application startup via DbUp."
echo "Starting application..."

exec "$@"