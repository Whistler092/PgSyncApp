namespace PgSyncApp.Models;

public class AzureSettings
{
    public string? Host { get; set; }
    public string? User { get; set; }
    public string? DbName { get; set; }
    public string? Password { get; set; } // Will be populated from Secrets/Env Vars
    public StorageAccountSettings? StorageAccount { get; set; }
}

public class StorageAccountSettings
{
    public string? ConnectionString { get; set; }
    public string? ContainerName { get; set; }
    public string? BlobName { get; set; }
}
