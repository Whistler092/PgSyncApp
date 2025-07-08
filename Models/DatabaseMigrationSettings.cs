namespace PgSyncApp.Models;

public class DatabaseMigrationSettings
{
    public DatabaseConnectionSettings? SourceDatabase { get; set; }
    public DatabaseConnectionSettings? DestinationDatabase { get; set; }
    public string? TempLocalPath { get; set; } = "temp_db_migration";
    public bool KeepLocalFiles { get; set; } = false;
    public bool DropDestinationIfExists { get; set; } = false;
}

public class DatabaseConnectionSettings
{
    public string? Host { get; set; }
    public string? Port { get; set; } = "5432";
    public string? Database { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? SslMode { get; set; } = "Prefer";
} 