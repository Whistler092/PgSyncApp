# Storage Account Migration Guide

This guide explains how to use the PgSyncApp to migrate data between Azure Storage accounts.

## Overview

The PgSyncApp now supports migrating blobs from one Azure Storage account to another. This is useful when you need to:
- Move data between different storage accounts
- Migrate from one region to another
- Copy data for backup purposes
- Transfer data between different subscriptions

## Configuration

### 1. Update appsettings.json

Configure your source and destination storage accounts in `appsettings.json`:

```json
{
  "StorageMigration": {
    "SourceStorage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=sourceaccount;AccountKey=your-source-key;EndpointSuffix=core.windows.net",
      "ContainerName": "source-container",
      "BlobName": "source-file.blob"
    },
    "DestinationStorage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=destaccount;AccountKey=your-dest-key;EndpointSuffix=core.windows.net",
      "ContainerName": "destination-container",
      "BlobName": "destination-file.blob"
    },
    "TempLocalPath": "temp_storage_migration",
    "KeepLocalFiles": false
  }
}
```

### 2. Security Best Practices

**DO NOT** store connection strings directly in `appsettings.json`. Instead, use one of these methods:

#### Option A: Environment Variables
```bash
# Set environment variables
export StorageMigration__SourceStorage__ConnectionString="your-source-connection-string"
export StorageMigration__DestinationStorage__ConnectionString="your-destination-connection-string"
```

#### Option B: User Secrets (Development)
```bash
dotnet user-secrets set "StorageMigration:SourceStorage:ConnectionString" "your-source-connection-string"
dotnet user-secrets set "StorageMigration:DestinationStorage:ConnectionString" "your-destination-connection-string"
```

#### Option C: Azure Key Vault (Production)
For production environments, consider using Azure Key Vault to store connection strings securely.

### 3. KeepLocalFiles Setting

The `KeepLocalFiles` setting controls whether temporary files are preserved locally after migration:

- **`false`** (default): Temporary files are automatically deleted after successful migration
- **`true`**: Temporary files are preserved in the `TempLocalPath` directory for backup or inspection

This is useful when you want to:
- Keep a local backup of migrated files
- Inspect files before they're uploaded to the destination
- Use the files for additional processing

## Usage

### Running the Application

1. **Configure your settings** as described above
2. **Run the application**:
   ```bash
   dotnet run
   ```

3. **Select an operation** from the menu:
   ```
   === Force Eleven Data Management Tool ===
   Please select an operation:
   1. Database Sync (Azure PostgreSQL to Local Docker)
   2. Storage Account Migration
   3. Both Operations
   4. Exit
   
   Enter your choice (1-4):
   ```

### Available Operations

- **Option 1**: Database Sync - Syncs Azure PostgreSQL database to local Docker container
- **Option 2**: Storage Account Migration - Migrates data between Azure Storage accounts
- **Option 3**: Both Operations - Performs both database sync and storage migration
- **Option 4**: Exit - Exits the application

### Storage Migration Types

The application supports two types of storage migration:

1. **Specific Blob Migration**: When you specify a blob name in the configuration
2. **Container Migration**: When you leave the blob name empty or provide a URL (migrates all blobs in the container)

### Storage Migration Process

The storage migration follows these steps:

1. **Download from Source**: Downloads the specified blob from the source storage account to a temporary local directory
2. **Upload to Destination**: Uploads the downloaded file to the destination storage account
3. **Verification**: Compares the file sizes to ensure successful migration
4. **Cleanup**: Removes temporary files

### Example Output

#### Menu Selection
```
=== Force Eleven Data Management Tool ===
Please select an operation:
1. Database Sync (Azure PostgreSQL to Local Docker)
2. Storage Account Migration
3. Both Operations
4. Exit

Enter your choice (1-4): 2
```

#### Storage Migration (Specific Blob)
```
--- Starting Storage Account Migration ---

[Storage Step 1] Downloading blob 'source-file.blob' from source container 'source-container'...
   - Downloading to: temp_storage_migration/source-file.blob
[Storage Step 1] Downloaded to: temp_storage_migration/source-file.blob

[Storage Step 2] Uploading to destination container 'destination-container' as 'destination-file.blob'...
   - Uploading from: temp_storage_migration/source-file.blob
[Storage Step 2] Upload completed successfully.

[Storage Step 3] Verifying migration...
   Verification successful: Both blobs have size 1048576 bytes
[Storage Step 3] Migration verified successfully.

--- Storage Migration Process Finished ---
```

#### Storage Migration (Container)
```
--- Starting Storage Account Migration ---

[Storage Step 1] Migrating entire container 'source-container' to 'destination-container'...
   - Listing blobs in source container 'source-container'...
   - Found 5 blobs to migrate.
   - Migrating blob: image1.jpg
     ✓ Successfully migrated: image1.jpg
   - Migrating blob: image2.png
     ✓ Successfully migrated: image2.png
   - Migration completed: 5/5 blobs migrated successfully.
[Storage Step 1] Container migration completed successfully.

--- Storage Migration Process Finished ---
```

#### Final Summary
```
=== Operation Summary ===
✓ Storage Migration: Completed

All selected operations have been completed.
```

#### KeepLocalFiles Example
When `KeepLocalFiles` is set to `true`:
```
--- Storage Migration Process Finished ---

Local files preserved in: C:\workspace\ForceEleven\BlobTemp
```

When `KeepLocalFiles` is set to `false` (default):
```
--- Storage Migration Process Finished ---

Cleaned up temporary directory: temp_storage_migration
```

## Error Handling

The application includes comprehensive error handling:

- **Missing Configuration**: Validates all required settings before starting
- **Blob Not Found**: Checks if source blob exists before attempting download
- **Network Issues**: Handles connection timeouts and retries
- **Permission Errors**: Validates access to both source and destination storage accounts
- **Verification Failures**: Warns if file sizes don't match after migration

## Troubleshooting

### Common Issues

1. **"Storage settings are incomplete"**
   - Check that all required fields are populated in your configuration
   - Verify connection strings are properly set via environment variables or user secrets

2. **"Blob does not exist"**
   - Verify the source blob name and container name are correct
   - Check that the source storage account is accessible

3. **"Access denied"**
   - Verify your connection strings have the necessary permissions
   - Check that the storage accounts allow access from your network

4. **"Container does not exist"**
   - The destination container will be created automatically if it doesn't exist
   - Ensure your connection string has permission to create containers

### Logging

The application provides detailed logging for troubleshooting:
- All operations are logged with step numbers
- Error messages include specific details about what failed
- File paths and sizes are logged for verification

## Performance Considerations

- **Large Files**: For very large files, consider using Azure Storage's built-in copy operations instead
- **Network**: Ensure good network connectivity between your machine and Azure Storage
- **Temporary Storage**: Ensure sufficient local disk space for temporary files
- **KeepLocalFiles**: When enabled, ensure you have enough disk space to store all migrated files locally

## Security Notes

- Connection strings contain sensitive information - never commit them to source control
- Use managed identities when possible for production environments
- Consider using SAS tokens with limited permissions instead of account keys
- Regularly rotate storage account keys 