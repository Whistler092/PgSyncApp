# Database Migration Guide

This guide explains how to use the PgSyncApp to migrate PostgreSQL databases between different connections.

## Overview

The PgSyncApp now supports migrating PostgreSQL databases from one connection to another. This is useful when you need to:
- Move databases between different PostgreSQL servers
- Migrate from one region to another
- Copy databases for backup purposes
- Transfer databases between different environments (dev, staging, prod)

## Configuration

### 1. Update appsettings.json

Configure your source and destination database connections in `appsettings.json`:

```json
{
  "DatabaseMigration": {
    "SourceDatabase": {
      "Host": "source-db.postgres.database.azure.com",
      "Port": "5432",
      "Database": "postgres",
      "Username": "sourceuser",
      "Password": "",
      "SslMode": "Require"
    },
    "DestinationDatabase": {
      "Host": "dest-db.postgres.database.azure.com",
      "Port": "5432",
      "Database": "postgres",
      "Username": "destuser",
      "Password": "",
      "SslMode": "Require"
    },
    "TempLocalPath": "C:\\workspace\\ForceEleven\\DbTemp",
    "KeepLocalFiles": false,
    "DropDestinationIfExists": false
  }
}
```

### 2. Security Best Practices

**DO NOT** store passwords directly in `appsettings.json`. Instead, use one of these methods:

#### Option A: Environment Variables
```bash
# Set environment variables
export DatabaseMigration__SourceDatabase__Password="your-source-password"
export DatabaseMigration__DestinationDatabase__Password="your-destination-password"
```

#### Option B: User Secrets (Development)
```bash
dotnet user-secrets set "DatabaseMigration:SourceDatabase:Password" "your-source-password"
dotnet user-secrets set "DatabaseMigration:DestinationDatabase:Password" "your-destination-password"
```

#### Option C: Azure Key Vault (Production)
For production environments, consider using Azure Key Vault to store database passwords securely.

### 3. Configuration Options

#### Database Connection Settings
- **Host**: PostgreSQL server hostname or IP address
- **Port**: PostgreSQL port (default: 5432)
- **Database**: Database name to migrate
- **Username**: Database username
- **Password**: Database password (set via environment variables or secrets)
- **SslMode**: SSL mode for connection (default: "Prefer")

#### Migration Settings
- **TempLocalPath**: Local directory for temporary backup files
- **KeepLocalFiles**: Whether to preserve backup files locally after migration
- **DropDestinationIfExists**: Whether to drop the destination database if it exists

## Usage

### Running Database Migration

1. **Configure your settings** as described above
2. **Run the application**:
   ```bash
   dotnet run
   ```

3. **Select database migration** from the menu:
   ```
   === Force Eleven Data Management Tool ===
   Please select an operation:
   1. Database Sync (Azure PostgreSQL to Local Docker)
   2. Storage Account Migration
   3. Database Migration (PostgreSQL to PostgreSQL)
   4. All Operations
   5. Exit
   
   Enter your choice (1-5): 3
   ```

### Database Migration Process

The database migration follows these steps:

1. **Backup Source Database**: Creates a custom format backup of the source database
2. **Prepare Destination**: Drops existing database (if requested) and creates new one
3. **Restore to Destination**: Restores the backup to the destination database
4. **Verification**: Compares table counts to ensure successful migration
5. **Cleanup**: Removes temporary files (unless KeepLocalFiles is true)

### Example Output

#### Menu Selection
```
=== Force Eleven Data Management Tool ===
Please select an operation:
1. Database Sync (Azure PostgreSQL to Local Docker)
2. Storage Account Migration
3. Database Migration (PostgreSQL to PostgreSQL)
4. All Operations
5. Exit

Enter your choice (1-5): 3
```

#### Database Migration Process
```
--- Starting Database Migration ---

[DB Migration Step 1] Creating backup of source database 'postgres'...
   - Creating backup: postgres_20241201_143022.dump
[DB Migration Step 1] Backup created: C:\workspace\ForceEleven\DbTemp\postgres_20241201_143022.dump

[DB Migration Step 2] Restoring to destination database 'postgres'...
   - Ensuring database 'postgres' exists...
   - Restoring database from: postgres_20241201_143022.dump
[DB Migration Step 2] Database restored successfully.

[DB Migration Step 3] Verifying migration...
   Verification successful: Both databases have 15 tables
[DB Migration Step 3] Migration verified successfully.

--- Database Migration Process Finished ---
```

#### Final Summary
```
=== Operation Summary ===
✓ Database Migration: Completed

All selected operations have been completed.
```

## Error Handling

The application includes comprehensive error handling:

- **Missing Configuration**: Validates all required database settings before starting
- **Connection Issues**: Handles connection timeouts and authentication errors
- **Permission Errors**: Validates access to both source and destination databases
- **Backup/Restore Failures**: Provides detailed error messages for pg_dump/pg_restore issues
- **Verification Failures**: Warns if table counts don't match after migration

## Troubleshooting

### Common Issues

1. **"Database settings are incomplete"**
   - Check that all required fields are populated in your configuration
   - Verify passwords are properly set via environment variables or user secrets

2. **"Connection refused"**
   - Verify the database host and port are correct
   - Check that the database server is accessible from your network
   - Ensure firewall rules allow connections

3. **"Authentication failed"**
   - Verify username and password are correct
   - Check that the user has necessary permissions on both databases

4. **"Permission denied"**
   - Ensure the user has CREATE DATABASE permission on the destination server
   - Check that the user has access to the source database

5. **"Database already exists"**
   - Set `DropDestinationIfExists` to `true` if you want to overwrite existing databases
   - Or manually drop the destination database before migration

### Logging

The application provides detailed logging for troubleshooting:
- All operations are logged with step numbers
- Error messages include specific details about what failed
- File paths and database names are logged for verification

## Performance Considerations

- **Large Databases**: For very large databases, ensure sufficient disk space for backup files
- **Network**: Ensure good network connectivity between your machine and both database servers
- **Temporary Storage**: Ensure sufficient local disk space for backup files
- **KeepLocalFiles**: When enabled, ensure you have enough disk space to store backup files locally

## Security Notes

- Database passwords contain sensitive information - never commit them to source control
- Use managed identities when possible for production environments
- Consider using connection pooling for better performance
- Regularly rotate database passwords
- Use SSL connections (SslMode: "Require") for production environments

## Advanced Features

### Drop Destination If Exists

When `DropDestinationIfExists` is set to `true`, the application will:
1. Connect to the destination server
2. Drop the destination database if it exists
3. Create a new database with the same name
4. Restore the backup to the new database

This is useful when you want to ensure a clean migration without any existing data conflicts.

### Keep Local Files

When `KeepLocalFiles` is set to `true`:
- Backup files are preserved in the `TempLocalPath` directory
- You can use these files for additional backup purposes
- Files are not automatically cleaned up

### SSL Configuration

The `SslMode` setting controls SSL connection behavior:
- **"Disable"**: No SSL
- **"Prefer"**: Use SSL if available (default)
- **"Require"**: Require SSL connection
- **"Verify-CA"**: Verify SSL certificate authority
- **"Verify-Full"**: Verify SSL certificate and hostname 