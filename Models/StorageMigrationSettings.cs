namespace PgSyncApp.Models;

public class StorageMigrationSettings
{
    public StorageAccountSettings? SourceStorage { get; set; }
    public StorageAccountSettings? DestinationStorage { get; set; }
    public string? TempLocalPath { get; set; } = "temp_storage_migration";
    public bool KeepLocalFiles { get; set; } = false;
} 