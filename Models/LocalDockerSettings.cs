namespace PgSyncApp.Models;

public class LocalDockerSettings
{
    public string? ContainerName { get; set; }
    public string? PgPassword { get; set; } // Will be populated from Secrets/Env Vars
    public string? LocalPort { get; set; }
    public string? PostgresImage { get; set; }
}