#!/bin/bash

# Script to create a new DbUp SQL migration script
if [ -z "$1" ]; then
  echo "Error: Migration name is required"
  echo "Usage: ./create-migrations.sh MigrationName"
  exit 1
fi

MIGRATION_NAME=$1
SQL_SCRIPTS_DIR="c:/dev/scratchpad/dockerlearning/src/DockerLearningApi/SqlScripts"

# Create the SqlScripts directory if it doesn't exist
mkdir -p "$SQL_SCRIPTS_DIR"

# Get the next migration number
NEXT_NUMBER=1
LAST_SCRIPT=$(ls -1 "$SQL_SCRIPTS_DIR"/*.sql 2>/dev/null | sort | tail -1)

if [ ! -z "$LAST_SCRIPT" ]; then
  # Extract the number from the filename
  BASENAME=$(basename "$LAST_SCRIPT")
  CURRENT_NUMBER=$(echo "$BASENAME" | sed -r 's/^([0-9]+)_.*/\1/')
  
  # Convert to integer and increment
  NEXT_NUMBER=$((10#$CURRENT_NUMBER + 1))
fi

# Format with leading zeros
PADDED_NUMBER=$(printf "%04d" $NEXT_NUMBER)
NEW_FILENAME="${PADDED_NUMBER}_${MIGRATION_NAME}.sql"
NEW_FILEPATH="$SQL_SCRIPTS_DIR/$NEW_FILENAME"

# Create the new SQL script file with a template
cat > "$NEW_FILEPATH" << EOF
-- Migration: $MIGRATION_NAME
-- Created: $(date +"%Y-%m-%d %H:%M:%S")

-- Your SQL migration script here

EOF

echo "Created new DbUp SQL migration script: $NEW_FILEPATH"
echo "Edit this file to add your database changes."