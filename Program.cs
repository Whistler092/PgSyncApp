using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PgSyncApp.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;


class Program
{
	// No more static config variables here!

	static async Task Main(string[] args)
	{
		// --- Build Configuration ---
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(AppContext.BaseDirectory) // Use deployed app's directory
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
			.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true) // Environment-specific settings
			.AddEnvironmentVariables() // Allow overriding with environment variables (important!)
			.AddUserSecrets<Program>() // Load secrets (for development)
			.Build();

		// --- Bind Configuration to Objects ---
		var azureSettings = configuration.GetSection("Azure").Get<AzureSettings>()
							?? throw new InvalidOperationException("Azure settings are missing.");
		var localDockerSettings = configuration.GetSection("LocalDocker").Get<LocalDockerSettings>()
								  ?? throw new InvalidOperationException("LocalDocker settings are missing.");
		var backupSettings = configuration.GetSection("Backup").Get<BackupSettings>()
							 ?? throw new InvalidOperationException("Backup settings are missing.");
		        var storageMigrationSettings = configuration.GetSection("StorageMigration").Get<StorageMigrationSettings>()
                                       ?? throw new InvalidOperationException("StorageMigration settings are missing.");
        var databaseMigrationSettings = configuration.GetSection("DatabaseMigration").Get<DatabaseMigrationSettings>()
                                        ?? throw new InvalidOperationException("DatabaseMigration settings are missing.");

		// --- Get Sensitive Data (Prioritize Env Vars over Secrets) ---
		// Note: The .Get<T>() above automatically reads matching keys from all providers (JSON, Env Vars, Secrets)
		// Ensure your secrets keys match the C# property names (e.g., "Azure:Password" maps to AzureSettings.Password)
		// You might still want explicit checks or fallbacks if needed.
		

		

		        // --- Show Selection Menu ---
        Console.WriteLine("=== Force Eleven Data Management Tool ===");
        Console.WriteLine("Please select an operation:");
        Console.WriteLine("1. Database Sync (Azure PostgreSQL to Local Docker)");
        Console.WriteLine("2. Storage Account Migration");
        Console.WriteLine("3. Database Migration (PostgreSQL to PostgreSQL)");
        Console.WriteLine("4. All Operations");
        Console.WriteLine("5. Backup Only (SourceDatabase to TempLocalPath)");
        Console.WriteLine("6. Restore Only (DestinationDatabase, specify dump path)");
        Console.WriteLine("7. Exit");
        Console.Write("\nEnter your choice (1-7): ");

		string? choice = Console.ReadLine()?.Trim();

        bool performDbSync = false;
        bool performStorageMigration = false;
        bool performDatabaseMigration = false;
        bool performBackupOnly = false;
        bool performRestoreOnly = false;

        switch (choice)
        {
            case "1":
                performDbSync = true;
                break;
            case "2":
                performStorageMigration = true;
                break;
            case "3":
                performDatabaseMigration = true;
                break;
            case "4":
                performDbSync = true;
                performStorageMigration = true;
                performDatabaseMigration = true;
                break;
            case "5":
                performBackupOnly = true;
                break;
            case "6":
                performRestoreOnly = true;
                break;
            case "7":
                Console.WriteLine("Exiting...");
                return;
            default:
                Console.WriteLine("Invalid choice. Exiting...");
                return;
        }

        // --- Backup Only Workflow ---
        if (performBackupOnly)
        {
            if (databaseMigrationSettings.SourceDatabase != null)
            {
                Console.WriteLine("\n--- Starting Backup Only (SourceDatabase to TempLocalPath) ---");
                try
                {
                    string backupFilePath = await CreateDatabaseBackupAsync(databaseMigrationSettings.SourceDatabase, databaseMigrationSettings.TempLocalPath);
                    if (!string.IsNullOrEmpty(backupFilePath))
                    {
                        Console.WriteLine($"Backup created at: {backupFilePath}");
                    }
                    else
                    {
                        Console.WriteLine("Backup failed.");
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"\nBackup failed: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine("\n--- Backup Skipped ---");
                Console.WriteLine("Source database settings are not configured. Please check your appsettings.json file.");
            }
            return;
        }

        // --- Restore Only Workflow ---
        if (performRestoreOnly)
        {
            if (databaseMigrationSettings.DestinationDatabase != null)
            {
                Console.WriteLine("\n--- Starting Restore Only (DestinationDatabase, specify dump path) ---");
                Console.Write("Enter the full path to the dump file: ");
                string? dumpPath = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(dumpPath))
                {
                    Console.WriteLine("No dump file path provided. Aborting restore.");
                    return;
                }
                try
                {
                    bool restoreSuccess = await RestoreDatabaseAsync(databaseMigrationSettings.DestinationDatabase, dumpPath, databaseMigrationSettings.DropDestinationIfExists);
                    if (restoreSuccess)
                    {
                        Console.WriteLine("Restore completed successfully.");
                    }
                    else
                    {
                        Console.WriteLine("Restore failed.");
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"\nRestore failed: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine("\n--- Restore Skipped ---");
                Console.WriteLine("Destination database settings are not configured. Please check your appsettings.json file.");
            }
            return;
        }

		// --- Database Sync Workflow ---
		if (performDbSync)
		{
			if (string.IsNullOrWhiteSpace(azureSettings.Password))
			{
				// Fallback to prompting if not found in Env Vars or Secrets
				Console.Write($"Password for Azure user '{azureSettings.User}' not found in config. Enter password: ");
				azureSettings.Password = ReadPassword();
				Console.WriteLine("\nPassword received.");
				if (string.IsNullOrWhiteSpace(azureSettings.Password))
				{
					Console.Error.WriteLine("Azure password is required.");
					return;
				}
			}
			if (string.IsNullOrWhiteSpace(localDockerSettings.PgPassword))
			{
				// Fallback for local password if needed (or just throw error)
				Console.Write($"Password for local Docker PostgreSQL user 'postgres' not found in config. Enter password: ");
				localDockerSettings.PgPassword = ReadPassword();
				Console.WriteLine("\nPassword received.");
				if (string.IsNullOrWhiteSpace(localDockerSettings.PgPassword))
				{
					Console.Error.WriteLine("Local PostgreSQL password is required.");
					return;
				}
			}


			// --- Validate Required Settings ---
			// Add more checks as needed
			if (string.IsNullOrWhiteSpace(azureSettings.Host) ||
				string.IsNullOrWhiteSpace(azureSettings.User) ||
				string.IsNullOrWhiteSpace(azureSettings.DbName) ||
				string.IsNullOrWhiteSpace(localDockerSettings.ContainerName) ||
				string.IsNullOrWhiteSpace(localDockerSettings.LocalPort) ||
				string.IsNullOrWhiteSpace(localDockerSettings.PostgresImage) ||
				string.IsNullOrWhiteSpace(backupSettings.FileName))
			{
				Console.Error.WriteLine("One or more required settings are missing in appsettings.json or configuration providers.");
				return;
			}
			Console.WriteLine("\n--- Starting Azure PostgreSQL Sync to Local Docker ---");
			// Get full path for backup file relative to application base directory
			string backupFilePath = Path.Combine(AppContext.BaseDirectory, backupSettings.FileName);
			string tocFilePath = Path.Combine(AppContext.BaseDirectory, "toc.list"); // Path for original TOC
			string editedTocFilePath = Path.Combine(AppContext.BaseDirectory, "toc_edited.list"); // Path for edited TOC


			try
			{
				// 1. Backup Azure Database
				Console.WriteLine($"\n[Step 1] Backing up Azure database '{azureSettings.DbName}' from '{azureSettings.Host}'...");
				// Pass settings objects or individual properties to helper methods
				bool backupSuccess = await RunPgDumpAsync(azureSettings, backupFilePath);
				if (!backupSuccess) return;
				Console.WriteLine($"[Step 1] Backup completed: {backupFilePath}");

				// 2. Setup Local Docker Container
				Console.WriteLine($"\n[Step 2] Setting up local Docker container '{localDockerSettings.ContainerName}'...");
				bool dockerSetupSuccess = await SetupDockerContainerAsync(localDockerSettings, azureSettings.DbName); // Pass local DB name too
				if (!dockerSetupSuccess) return;
				Console.WriteLine($"[Step 2] Docker container '{localDockerSettings.ContainerName}' is running.");

				// --- NEW Steps for TOC Handling ---

				// 3. Generate TOC File Locally
				Console.WriteLine($"\n[Step 3] Generating Table of Contents (TOC) from backup...");
				bool generateTocSuccess = await GenerateTocFileAsync(backupFilePath, tocFilePath);
				if (!generateTocSuccess) return;
				Console.WriteLine($"[Step 3] TOC file generated: {tocFilePath}");

				// 4. Edit TOC File (Remove Azure Extensions)
				Console.WriteLine($"\n[Step 4] Editing TOC file to exclude Azure extensions...");
				bool editTocSuccess = EditTocFile(tocFilePath, editedTocFilePath);
				if (!editTocSuccess) return;
				Console.WriteLine($"[Step 4] Edited TOC file created: {editedTocFilePath}");

				// 5. Copy Backup and Edited TOC to Container
				Console.WriteLine($"\n[Step 5] Copying backup file and edited TOC to container...");
				bool copyBackupSuccess = await CopyFileToContainerAsync(localDockerSettings.ContainerName, backupSettings.FileName, backupFilePath, $"/tmp/{backupSettings.FileName}");
				bool copyTocSuccess = await CopyFileToContainerAsync(localDockerSettings.ContainerName, Path.GetFileName(editedTocFilePath), editedTocFilePath, $"/tmp/{Path.GetFileName(editedTocFilePath)}");
				if (!copyBackupSuccess || !copyTocSuccess) return;
				Console.WriteLine($"[Step 5] Files copied.");

				// 6. Restore Backup in Container using Edited TOC
				Console.WriteLine($"\n[Step 6] Restoring backup into database '{azureSettings.DbName}' inside container using edited TOC...");
				bool restoreSuccess = await RestoreBackupInContainerFromListAsync(localDockerSettings, azureSettings.DbName, Path.GetFileName(editedTocFilePath), $"/tmp/{backupSettings.FileName}"); // Pass dump file path too
				if (!restoreSuccess) return; // Stop if restore failed
				Console.WriteLine($"[Step 6] Restore completed successfully.");

				// --- End NEW Steps ---

				Console.WriteLine("\n--- Database Sync Process Finished ---");
				Console.WriteLine($"Connect locally using: Host=localhost, Port={localDockerSettings.LocalPort}, DB={azureSettings.DbName}, User=postgres, Password=******");
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Error.WriteLine($"\nAn unexpected error occurred: {ex.Message}");
				Console.Error.WriteLine(ex.StackTrace);
				Console.ResetColor();
			}
			finally
			{
				// Optional: Clean up local backup file
				// if (File.Exists(backupFilePath))
				// {
				//     Console.WriteLine($"\nCleaning up local backup file: {backupFilePath}");
				//     File.Delete(backupFilePath);
				// }
			}
		}

		// --- Storage Migration Workflow ---
		if (performStorageMigration)
		{
			// Validate storage migration settings if provided
			if (storageMigrationSettings.SourceStorage != null && storageMigrationSettings.DestinationStorage != null)
			{
				if (string.IsNullOrWhiteSpace(storageMigrationSettings.SourceStorage.ConnectionString) ||
					string.IsNullOrWhiteSpace(storageMigrationSettings.SourceStorage.ContainerName) ||
					string.IsNullOrWhiteSpace(storageMigrationSettings.SourceStorage.BlobName) ||
					string.IsNullOrWhiteSpace(storageMigrationSettings.DestinationStorage.ConnectionString) ||
					string.IsNullOrWhiteSpace(storageMigrationSettings.DestinationStorage.ContainerName) ||
					string.IsNullOrWhiteSpace(storageMigrationSettings.DestinationStorage.BlobName))
				{
					Console.Error.WriteLine("Storage migration settings are incomplete. Please check source and destination storage configurations.");
					return;
				}
			}

			if (storageMigrationSettings.SourceStorage != null && storageMigrationSettings.DestinationStorage != null)
			{
				Console.WriteLine("\n--- Starting Storage Account Migration ---");

				try
				{
					// Check if we're migrating a specific blob or entire container
					bool isContainerMigration = string.IsNullOrWhiteSpace(storageMigrationSettings.SourceStorage.BlobName) ||
											   storageMigrationSettings.SourceStorage.BlobName.StartsWith("http");

					if (isContainerMigration)
					{
						                        // Migrate entire container
                        Console.WriteLine($"\n[Storage Step 1] Migrating entire container '{storageMigrationSettings.SourceStorage.ContainerName}' to '{storageMigrationSettings.DestinationStorage.ContainerName}'...");
                        bool containerMigrationSuccess = await MigrateContainerAsync(storageMigrationSettings.SourceStorage, storageMigrationSettings.DestinationStorage, storageMigrationSettings.TempLocalPath, storageMigrationSettings.KeepLocalFiles);
						if (!containerMigrationSuccess) return;
						Console.WriteLine($"[Storage Step 1] Container migration completed successfully.");
					}
					else
					{
						// Migrate specific blob
						Console.WriteLine($"\n[Storage Step 1] Downloading blob '{storageMigrationSettings.SourceStorage.BlobName}' from source container '{storageMigrationSettings.SourceStorage.ContainerName}'...");
						string localFilePath = await DownloadBlobFromStorageAsync(storageMigrationSettings.SourceStorage, storageMigrationSettings.TempLocalPath);
						if (string.IsNullOrEmpty(localFilePath)) return;
						Console.WriteLine($"[Storage Step 1] Downloaded to: {localFilePath}");

						// 2. Upload to destination storage
						Console.WriteLine($"\n[Storage Step 2] Uploading to destination container '{storageMigrationSettings.DestinationStorage.ContainerName}' as '{storageMigrationSettings.DestinationStorage.BlobName}'...");
						bool uploadSuccess = await UploadBlobToStorageAsync(storageMigrationSettings.DestinationStorage, localFilePath);
						if (!uploadSuccess) return;
						Console.WriteLine($"[Storage Step 2] Upload completed successfully.");

						// 3. Verify migration (optional)
						Console.WriteLine($"\n[Storage Step 3] Verifying migration...");
						bool verificationSuccess = await VerifyStorageMigrationAsync(storageMigrationSettings.SourceStorage, storageMigrationSettings.DestinationStorage);
						if (verificationSuccess)
						{
							Console.WriteLine($"[Storage Step 3] Migration verified successfully.");
						}
						else
						{
							Console.WriteLine($"[Storage Step 3] Warning: Migration verification failed. Please check manually.");
						}
					}

					Console.WriteLine("\n--- Storage Migration Process Finished ---");
				}
				catch (Exception ex)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.Error.WriteLine($"\nStorage migration failed: {ex.Message}");
					Console.Error.WriteLine(ex.StackTrace);
					Console.ResetColor();
				}
				                finally
                {
                    // Clean up temporary files based on KeepLocalFiles setting
                    if (!storageMigrationSettings.KeepLocalFiles && !string.IsNullOrEmpty(storageMigrationSettings.TempLocalPath) && Directory.Exists(storageMigrationSettings.TempLocalPath))
                    {
                        try
                        {
                            Directory.Delete(storageMigrationSettings.TempLocalPath, true);
                            Console.WriteLine($"\nCleaned up temporary directory: {storageMigrationSettings.TempLocalPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\nWarning: Could not clean up temporary directory: {ex.Message}");
                        }
                    }
                    else if (storageMigrationSettings.KeepLocalFiles && !string.IsNullOrEmpty(storageMigrationSettings.TempLocalPath))
                    {
                        Console.WriteLine($"\nLocal files preserved in: {storageMigrationSettings.TempLocalPath}");
                    }
                }
			}
			else
			{
				Console.WriteLine("\n--- Storage Migration Skipped ---");
				Console.WriteLine("Storage migration settings are not configured. Please check your appsettings.json file.");
			            }
        }

        // --- Database Migration Workflow ---
        if (performDatabaseMigration)
        {
            if (databaseMigrationSettings.SourceDatabase != null && databaseMigrationSettings.DestinationDatabase != null)
            {
                Console.WriteLine("\n--- Starting Database Migration ---");
                
                try
                {
                    // 1. Backup source database
                    Console.WriteLine($"\n[DB Migration Step 1] Creating backup of source database '{databaseMigrationSettings.SourceDatabase.Database}'...");
                    string backupFilePath = await CreateDatabaseBackupAsync(databaseMigrationSettings.SourceDatabase, databaseMigrationSettings.TempLocalPath);
                    if (string.IsNullOrEmpty(backupFilePath)) return;
                    Console.WriteLine($"[DB Migration Step 1] Backup created: {backupFilePath}");

                    // 2. Restore to destination database
                    Console.WriteLine($"\n[DB Migration Step 2] Restoring to destination database '{databaseMigrationSettings.DestinationDatabase.Database}'...");
                    bool restoreSuccess = await RestoreDatabaseAsync(databaseMigrationSettings.DestinationDatabase, backupFilePath, databaseMigrationSettings.DropDestinationIfExists);
                    if (!restoreSuccess) return;
                    Console.WriteLine($"[DB Migration Step 2] Database restored successfully.");

                    // 3. Verify migration
                    Console.WriteLine($"\n[DB Migration Step 3] Verifying migration...");
                    bool verificationSuccess = await VerifyDatabaseMigrationAsync(databaseMigrationSettings.SourceDatabase, databaseMigrationSettings.DestinationDatabase);
                    if (verificationSuccess)
                    {
                        Console.WriteLine($"[DB Migration Step 3] Migration verified successfully.");
                    }
                    else
                    {
                        Console.WriteLine($"[DB Migration Step 3] Warning: Migration verification failed. Please check manually.");
                    }

                    Console.WriteLine("\n--- Database Migration Process Finished ---");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"\nDatabase migration failed: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Console.ResetColor();
                }
                finally
                {
                    // Clean up temporary files based on KeepLocalFiles setting
                    if (!databaseMigrationSettings.KeepLocalFiles && !string.IsNullOrEmpty(databaseMigrationSettings.TempLocalPath) && Directory.Exists(databaseMigrationSettings.TempLocalPath))
                    {
                        try
                        {
                            Directory.Delete(databaseMigrationSettings.TempLocalPath, true);
                            Console.WriteLine($"\nCleaned up temporary directory: {databaseMigrationSettings.TempLocalPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\nWarning: Could not clean up temporary directory: {ex.Message}");
                        }
                    }
                    else if (databaseMigrationSettings.KeepLocalFiles && !string.IsNullOrEmpty(databaseMigrationSettings.TempLocalPath))
                    {
                        Console.WriteLine($"\nLocal files preserved in: {databaseMigrationSettings.TempLocalPath}");
                    }
                }
            }
            else
            {
                Console.WriteLine("\n--- Database Migration Skipped ---");
                Console.WriteLine("Database migration settings are not configured. Please check your appsettings.json file.");
            }
        }

        // --- Final Summary ---
        if (performDbSync || performStorageMigration || performDatabaseMigration)
        {
            Console.WriteLine("\n=== Operation Summary ===");
            if (performDbSync)
            {
                Console.WriteLine("✓ Database Sync: Completed");
            }
            if (performStorageMigration)
            {
                Console.WriteLine("✓ Storage Migration: Completed");
            }
            if (performDatabaseMigration)
            {
                Console.WriteLine("✓ Database Migration: Completed");
            }
            Console.WriteLine("\nAll selected operations have been completed.");
        }
        else
        {
            Console.WriteLine("\nNo operations were selected.");
        }

		/*// 3. Copy Backup to Container
			Console.WriteLine($"\n[Step 3] Copying backup file '{backupSettings.FileName}' to container...");
			bool copySuccess = await CopyBackupToContainerAsync(localDockerSettings.ContainerName, backupSettings.FileName, backupFilePath);
			if (!copySuccess) return;
			Console.WriteLine($"[Step 3] Backup file copied.");

			// 4. Restore Backup in Container
			Console.WriteLine($"\n[Step 4] Restoring backup into database '{azureSettings.DbName}' inside container...");
			 // Use azureSettings.DbName assuming local DB name is same, or add a LocalDbName setting if needed
			bool restoreSuccess = await RestoreBackupInContainerAsync(localDockerSettings, azureSettings.DbName, backupSettings.FileName);
			if (!restoreSuccess) return;
			Console.WriteLine($"[Step 4] Restore completed successfully.");

			Console.WriteLine("\n--- Sync Process Finished ---");
			Console.WriteLine($"Connect locally using: Host=localhost, Port={localDockerSettings.LocalPort}, DB={azureSettings.DbName}, User=postgres, Password=******"); // Mask password
			*/

	}

	// --- Update Helper Methods Signatures ---

	static async Task<bool> RunPgDumpAsync(AzureSettings azureSettings, string backupFilePath)
	{
		if (string.IsNullOrWhiteSpace(azureSettings.Password))
		{
			Console.Error.WriteLine("Azure password is missing for pg_dump.");
			return false;
		}

		if (File.Exists(backupFilePath))
		{
			Console.WriteLine($"\n[Step 1] Backup file '{backupFilePath}' already exists.");
			return true;
		}

		//string arguments = $"-h {azureSettings.Host} -U {azureSettings.User} -d {azureSettings.DbName} -Fc --no-owner --no-privileges -f \"{backupFilePath}\"";
		string arguments = $"-h {azureSettings.Host} -U {azureSettings.User} -d {azureSettings.DbName} -Fc --no-owner --no-acl -f \"{backupFilePath}\"";

		var environmentVariables = new Dictionary<string, string> { { "PGPASSWORD", azureSettings.Password } };
		return await RunProcessAsync("pg_dump", arguments, environmentVariables);
	}

	static async Task<bool> SetupDockerContainerAsync(LocalDockerSettings dockerSettings, string localDbName)
	{
		if (string.IsNullOrWhiteSpace(dockerSettings.PgPassword))
		{
			Console.Error.WriteLine("Local Docker PostgreSQL password is missing.");
			return false;
		}
		Console.WriteLine($"   - Stopping and removing existing container '{dockerSettings.ContainerName}' (if any)...");
		await RunProcessAsync("docker", $"rm -f {dockerSettings.ContainerName}"); // Ignore errors

		Console.WriteLine($"   - Starting new container '{dockerSettings.ContainerName}'...");
		string dockerRunArgs = $"run -d --name {dockerSettings.ContainerName} " +
							   $"-e POSTGRES_PASSWORD={dockerSettings.PgPassword} " +
							   $"-e POSTGRES_DB={localDbName} " + // Use passed-in local DB name
							   $"-p {dockerSettings.LocalPort}:5432 " +
							   $"-v pg_local_data_{SanitizeForVolumeName(localDbName)}:/var/lib/postgresql/data " +
							   $"{dockerSettings.PostgresImage}";
		Console.WriteLine(dockerRunArgs);
		bool success = await RunProcessAsync("docker", dockerRunArgs);
		if (success)
		{
			Console.WriteLine("   - Waiting a few seconds for PostgreSQL to initialize...");
			await Task.Delay(5000);
		}
		return success;
	}

	static async Task<bool> CopyBackupToContainerAsync(string containerName, string backupFileName, string sourceBackupPath)
	{
		string destinationPath = $"{containerName}:/tmp/{backupFileName}";
		return await RunProcessAsync("docker", $"cp \"{sourceBackupPath}\" \"{destinationPath}\"");
	}

	static async Task<bool> RestoreBackupInContainerAsync(LocalDockerSettings dockerSettings, string localDbName, string backupFileName)
	{
		if (string.IsNullOrWhiteSpace(dockerSettings.PgPassword))
		{
			Console.Error.WriteLine("Local Docker PostgreSQL password is missing for pg_restore.");
			return false;
		}
		string pgRestoreArgs = $"exec -i -e PGPASSWORD={dockerSettings.PgPassword} {dockerSettings.ContainerName} " +
							   $"pg_restore -U postgres -d {localDbName} --no-owner --no-privileges --clean --if-exists -v /tmp/{backupFileName}";

		return await RunProcessAsync("docker", pgRestoreArgs);
	}

	// Updated Copy method to be more generic
	static async Task<bool> CopyFileToContainerAsync(string containerName, string localFileName, string sourceFilePath, string destinationContainerPath)
	{
		Console.WriteLine($"   - Copying '{localFileName}' to '{containerName}:{destinationContainerPath}'");
		return await RunProcessAsync("docker", $"cp \"{sourceFilePath}\" \"{containerName}:{destinationContainerPath}\"");
	}


	// NEW Helper to generate TOC
	static async Task<bool> GenerateTocFileAsync(string backupFilePath, string tocFilePath)
	{
		// Use local pg_restore to generate the list
		string arguments = $"-l \"{backupFilePath}\""; // Generate list from backup
													   // Redirect standard output to the tocFilePath
		return await RunProcessAsync("pg_restore", arguments, redirectOutputToFile: tocFilePath);
	}

	// NEW Helper to edit TOC
	static bool EditTocFile(string inputTocPath, string outputTocPath)
	{
		Console.WriteLine("   - Reading TOC and commenting out Azure extensions...");
		try
		{
			var originalLines = File.ReadAllLines(inputTocPath);
			var modifiedLines = new List<string>();

			foreach (string line in originalLines)
			{
				string trimmedLine = line.Trim();
				bool shouldComment = false;

				// Skip already commented lines or empty lines
				if (trimmedLine.StartsWith(";") || string.IsNullOrWhiteSpace(trimmedLine))
				{
					modifiedLines.Add(line);
					continue;
				}

				// 1. Comment out EXTENSION creation and their comments
				if (trimmedLine.Contains(" EXTENSION - azure") || trimmedLine.Contains(" COMMENT - EXTENSION azure") ||
					trimmedLine.Contains(" EXTENSION - pgaadauth") || trimmedLine.Contains(" COMMENT - EXTENSION pgaadauth"))
				{
					shouldComment = true;
				}
				// 2. Comment out explicit ALTER ... OWNER TO commands for problematic roles
				//    (These are less likely if pg_dump --no-owner was effective, but good to have)
				else if (trimmedLine.StartsWith("ALTER ") && (trimmedLine.Contains(" OWNER TO forceelevensa") || trimmedLine.Contains(" OWNER TO azure_pg_admin")))
				{
					shouldComment = true;
				}
				// Add other specific known problematic lines here if needed.
				// For example, if there's a specific "SET SESSION AUTHORIZATION" that causes issues.

				// DO NOT comment out SCHEMA, TABLE, SEQUENCE, INDEX, CONSTRAINT definitions
				// just because 'forceelevensa' or 'azure_pg_admin' appears at the end of the line.
				// Rely on pg_restore --no-owner to handle the ownership during creation.

				if (shouldComment)
				{
					modifiedLines.Add(";" + line);
				}
				else
				{
					modifiedLines.Add(line);
				}
			}

			File.WriteAllLines(outputTocPath, modifiedLines.ToArray());
			Console.WriteLine($"   - Successfully wrote edited TOC to {outputTocPath}");
			return true;
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"   Error editing TOC file: {ex.Message}");
			Console.ResetColor();
			return false;
		}
	}

	// Renamed and modified Restore method
	static async Task<bool> RestoreBackupInContainerFromListAsync(LocalDockerSettings dockerSettings, string localDbName, string editedTocFileName, string backupFilePathInContainer)
	{
		if (string.IsNullOrWhiteSpace(dockerSettings.PgPassword))
		{
			Console.Error.WriteLine("Local Docker PostgreSQL password is missing for pg_restore.");
			return false;
		}
		Console.WriteLine("RestoreBackupInContainerFromListAsync");
		// Use -L with the edited TOC file.
		// Note: --clean, --if-exists are generally NOT used with -L as the list itself defines what to do.
		// -d is still required to specify the target database. -v is optional for verbosity.
		string editedTocPathInContainer = $"/tmp/{editedTocFileName}";
		string pgRestoreArgs = $"exec -i -e PGPASSWORD={dockerSettings.PgPassword} {dockerSettings.ContainerName} " +
							   $"pg_restore -U postgres -d {localDbName} -v --no-owner --no-acl -L \"{editedTocPathInContainer}\" \"{backupFilePathInContainer}\"";
		return await RunProcessAsync("docker", pgRestoreArgs);
	}


	// --- Generic Process Runner (Needs modification for file redirection) ---
	static async Task<bool> RunProcessAsync(string fileName, string arguments, Dictionary<string, string>? environmentVariables = null, string? redirectOutputToFile = null) // Added redirect parameter
	{


		var processStartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			Arguments = arguments,
			RedirectStandardOutput = true, // Always redirect stdout
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			Environment = { }
		};
		// ... (Keep environment variable handling) ...
		string? pgPasswordValue = null;
		if (environmentVariables != null)
		{ /* ... keep env var setup ... */
			Console.WriteLine($"   > Running: {fileName} {arguments.Replace(environmentVariables?.GetValueOrDefault("PGPASSWORD") ?? "", "***")}"); // Mask password if present

			foreach (var kvp in environmentVariables)
			{
				processStartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
				if (kvp.Key == "PGPASSWORD")
				{
					processStartInfo.Environment["PGPASSWORD"] = kvp.Value;
					pgPasswordValue = kvp.Value;
				}
			}
		}
		if (environmentVariables == null || !environmentVariables.ContainsKey("PGPASSWORD"))
		{
			if (processStartInfo.EnvironmentVariables.ContainsKey("PGPASSWORD")) processStartInfo.EnvironmentVariables.Remove("PGPASSWORD");
			if (processStartInfo.Environment.ContainsKey("PGPASSWORD")) processStartInfo.Environment.Remove("PGPASSWORD");
		}


		using var process = new Process { StartInfo = processStartInfo };
		var outputBuilder = new StringBuilder();
		var errorBuilder = new StringBuilder();
		StreamWriter? outputFileStream = null;

		// Setup redirection based on parameter
		if (!string.IsNullOrEmpty(redirectOutputToFile))
		{
			try
			{
				// Ensure directory exists
				Directory.CreateDirectory(Path.GetDirectoryName(redirectOutputToFile)!);
				outputFileStream = new StreamWriter(redirectOutputToFile, false, Encoding.UTF8);
				process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputFileStream.WriteLine(e.Data); };
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Error.WriteLine($"   Failed to open file for redirection '{redirectOutputToFile}': {ex.Message}");
				Console.ResetColor();
				return false; // Cannot proceed if redirection fails
			}

		}
		else
		{
			// Capture to StringBuilder if not redirecting to file
			process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
		}

		// Always capture stderr
		process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };


		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		await process.WaitForExitAsync();

		outputFileStream?.Close(); // Ensure file stream is closed

		// --- Process results ---
		bool success = process.ExitCode == 0;
		string output = outputBuilder.ToString().Trim(); // Output captured in memory (if not redirected)
		string errors = errorBuilder.ToString().Trim(); // Errors always captured in memory

		// Mask password
		if (!string.IsNullOrWhiteSpace(pgPasswordValue))
		{
			output = output.Replace(pgPasswordValue, "****");
			errors = errors.Replace(pgPasswordValue, "****");
		}

		// Log output (if not redirected)
		if (!string.IsNullOrEmpty(redirectOutputToFile))
		{
			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.WriteLine($"   Output redirected to: {redirectOutputToFile}");
			Console.ResetColor();
		}
		else if (!string.IsNullOrEmpty(output))
		{
			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.WriteLine("   Output:\n" + output);
			Console.ResetColor();
		}

		// Log errors/warnings
		if (!success)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"   Error (Exit Code: {process.ExitCode}):");
			if (!string.IsNullOrEmpty(errors)) Console.Error.WriteLine(errors);
			else if (!string.IsNullOrEmpty(output) && string.IsNullOrEmpty(redirectOutputToFile)) Console.Error.WriteLine(output); // Show stdout if stderr is empty and not redirected
			else Console.Error.WriteLine("   No error details captured.");
			Console.ResetColor();
		}
		else if (!string.IsNullOrEmpty(errors))
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("   Warnings/Notices (stderr):\n" + errors);
			Console.ResetColor();
		}

		return success;
	}
	// --- ReadPassword (Keep as before) ---
	public static string ReadPassword()
	{
		// ... (ReadPassword implementation remains the same) ...
		var pass = new StringBuilder();
		while (true)
		{
			ConsoleKeyInfo key = Console.ReadKey(true);
			if (key.Key == ConsoleKey.Enter) break;
			if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
			{
				pass.Remove(pass.Length - 1, 1); Console.Write("\b \b");
			}
			else if (!char.IsControl(key.KeyChar))
			{
				pass.Append(key.KeyChar); Console.Write("*");
			}
		}
		return pass.ToString();
	}

	// --- SanitizeForVolumeName (Keep as before) ---
	private static string SanitizeForVolumeName(string input)
	{
		// ... (SanitizeForVolumeName implementation remains the same) ...
		var sanitized = System.Text.RegularExpressions.Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9_.-]+", "-");
		sanitized = sanitized.Trim('-', '.', '_');
		if (string.IsNullOrEmpty(sanitized)) return "default";
		return sanitized;
	}

	// --- Storage Migration Helper Methods ---

	static async Task<string?> DownloadBlobFromStorageAsync(StorageAccountSettings storageSettings, string? tempPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(storageSettings.ConnectionString) ||
				string.IsNullOrWhiteSpace(storageSettings.ContainerName) ||
				string.IsNullOrWhiteSpace(storageSettings.BlobName))
			{
				Console.Error.WriteLine("Storage settings are incomplete for download.");
				return null;
			}

			var blobServiceClient = new BlobServiceClient(storageSettings.ConnectionString);
			var containerClient = blobServiceClient.GetBlobContainerClient(storageSettings.ContainerName);
			var blobClient = containerClient.GetBlobClient(storageSettings.BlobName);

			// Check if blob exists
			if (!await blobClient.ExistsAsync())
			{
				Console.Error.WriteLine($"Blob '{storageSettings.BlobName}' does not exist in container '{storageSettings.ContainerName}'.");
				return null;
			}

			// Create temp directory if it doesn't exist
			string tempDirectory = tempPath ?? "temp_storage_migration";
			Directory.CreateDirectory(tempDirectory);

			// Download to local file
			string localFilePath = Path.Combine(tempDirectory, Path.GetFileName(storageSettings.BlobName));
			Console.WriteLine($"   - Downloading to: {localFilePath}");

			await blobClient.DownloadToAsync(localFilePath);

			return localFilePath;
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"   Error downloading blob: {ex.Message}");
			Console.ResetColor();
			return null;
		}
	}

	static async Task<bool> UploadBlobToStorageAsync(StorageAccountSettings storageSettings, string localFilePath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(storageSettings.ConnectionString) ||
				string.IsNullOrWhiteSpace(storageSettings.ContainerName) ||
				string.IsNullOrWhiteSpace(storageSettings.BlobName))
			{
				Console.Error.WriteLine("Storage settings are incomplete for upload.");
				return false;
			}

			if (!File.Exists(localFilePath))
			{
				Console.Error.WriteLine($"Local file does not exist: {localFilePath}");
				return false;
			}

			var blobServiceClient = new BlobServiceClient(storageSettings.ConnectionString);
			var containerClient = blobServiceClient.GetBlobContainerClient(storageSettings.ContainerName);

			// Create container if it doesn't exist
			await containerClient.CreateIfNotExistsAsync();

			var blobClient = containerClient.GetBlobClient(storageSettings.BlobName);

			Console.WriteLine($"   - Uploading from: {localFilePath}");
			await blobClient.UploadAsync(localFilePath, overwrite: true);

			return true;
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"   Error uploading blob: {ex.Message}");
			Console.ResetColor();
			return false;
		}
	}

	    static async Task<bool> MigrateContainerAsync(StorageAccountSettings sourceSettings, StorageAccountSettings destinationSettings, string? tempPath, bool keepLocalFiles = false)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(sourceSettings.ConnectionString) ||
				string.IsNullOrWhiteSpace(sourceSettings.ContainerName) ||
				string.IsNullOrWhiteSpace(destinationSettings.ConnectionString) ||
				string.IsNullOrWhiteSpace(destinationSettings.ContainerName))
			{
				Console.Error.WriteLine("Storage settings are incomplete for container migration.");
				return false;
			}

			var sourceBlobServiceClient = new BlobServiceClient(sourceSettings.ConnectionString);
			var sourceContainerClient = sourceBlobServiceClient.GetBlobContainerClient(sourceSettings.ContainerName);

			var destBlobServiceClient = new BlobServiceClient(destinationSettings.ConnectionString);
			var destContainerClient = destBlobServiceClient.GetBlobContainerClient(destinationSettings.ContainerName);

			// Check if source container exists
			if (!await sourceContainerClient.ExistsAsync())
			{
				Console.Error.WriteLine($"Source container '{sourceSettings.ContainerName}' does not exist.");
				return false;
			}

			// Create destination container if it doesn't exist
			await destContainerClient.CreateIfNotExistsAsync();

			// List all blobs in source container
			Console.WriteLine($"   - Listing blobs in source container '{sourceSettings.ContainerName}'...");
			var blobs = sourceContainerClient.GetBlobsAsync();
			var blobList = new List<string>();

			await foreach (var blob in blobs)
			{
				blobList.Add(blob.Name);
			}

			if (blobList.Count == 0)
			{
				Console.WriteLine("   - Source container is empty. Nothing to migrate.");
				return true;
			}

			Console.WriteLine($"   - Found {blobList.Count} blobs to migrate.");

			// Create temp directory
			string tempDirectory = tempPath ?? "temp_storage_migration";
			Directory.CreateDirectory(tempDirectory);

			int successCount = 0;
			int totalCount = blobList.Count;

			foreach (var blobName in blobList)
			{
				try
				{
					Console.WriteLine($"   - Migrating blob: {blobName}");

					// Download from source
					var sourceBlobClient = sourceContainerClient.GetBlobClient(blobName);
					string localFilePath = Path.Combine(tempDirectory, Path.GetFileName(blobName));

					await sourceBlobClient.DownloadToAsync(localFilePath);

					                    // Upload to destination
                    var destBlobClient = destContainerClient.GetBlobClient(blobName);
                    await destBlobClient.UploadAsync(localFilePath, overwrite: true);
                    
                    // Clean up local file if keepLocalFiles is false
                    if (!keepLocalFiles)
                    {
                        File.Delete(localFilePath);
                    }

					successCount++;
					Console.WriteLine($"     ✓ Successfully migrated: {blobName}");
				}
				catch (Exception ex)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"     ⚠ Failed to migrate blob '{blobName}': {ex.Message}");
					Console.ResetColor();
				}
			}

			Console.WriteLine($"   - Migration completed: {successCount}/{totalCount} blobs migrated successfully.");
			return successCount > 0; // Return true if at least one blob was migrated
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"   Error during container migration: {ex.Message}");
			Console.ResetColor();
			return false;
		}
	}

	static async Task<bool> VerifyStorageMigrationAsync(StorageAccountSettings sourceSettings, StorageAccountSettings destinationSettings)
	{
		try
		{
			// Get source blob properties
			var sourceBlobServiceClient = new BlobServiceClient(sourceSettings.ConnectionString);
			var sourceContainerClient = sourceBlobServiceClient.GetBlobContainerClient(sourceSettings.ContainerName);
			var sourceBlobClient = sourceContainerClient.GetBlobClient(sourceSettings.BlobName);

			// Get destination blob properties
			var destBlobServiceClient = new BlobServiceClient(destinationSettings.ConnectionString);
			var destContainerClient = destBlobServiceClient.GetBlobContainerClient(destinationSettings.ContainerName);
			var destBlobClient = destContainerClient.GetBlobClient(destinationSettings.BlobName);

			// Check if both blobs exist
			if (!await sourceBlobClient.ExistsAsync())
			{
				Console.Error.WriteLine("   Source blob does not exist for verification.");
				return false;
			}

			if (!await destBlobClient.ExistsAsync())
			{
				Console.Error.WriteLine("   Destination blob does not exist for verification.");
				return false;
			}

			// Get properties for comparison
			var sourceProperties = await sourceBlobClient.GetPropertiesAsync();
			var destProperties = await destBlobClient.GetPropertiesAsync();

			// Compare content length
			if (sourceProperties.Value.ContentLength != destProperties.Value.ContentLength)
			{
				Console.Error.WriteLine($"   Content length mismatch: Source={sourceProperties.Value.ContentLength}, Destination={destProperties.Value.ContentLength}");
				return false;
			}

			Console.WriteLine($"   Verification successful: Both blobs have size {sourceProperties.Value.ContentLength} bytes");
			return true;
		}
		catch (Exception ex)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"   Error during verification: {ex.Message}");
			Console.ResetColor();
			            return false;
        }
    }

    // --- Database Migration Helper Methods ---

    static async Task<string?> CreateDatabaseBackupAsync(DatabaseConnectionSettings dbSettings, string? tempPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dbSettings.Host) ||
                string.IsNullOrWhiteSpace(dbSettings.Database) ||
                string.IsNullOrWhiteSpace(dbSettings.Username) ||
                string.IsNullOrWhiteSpace(dbSettings.Password))
            {
                Console.Error.WriteLine("Database settings are incomplete for backup.");
                return null;
            }

            // Create temp directory if it doesn't exist
            string tempDirectory = tempPath ?? "temp_db_migration";
            Directory.CreateDirectory(tempDirectory);

            // Generate backup filename
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = $"{dbSettings.Database}_{timestamp}.dump";
            string backupFilePath = Path.Combine(tempDirectory, backupFileName);

            // Build pg_dump command
            string arguments = $"-h {dbSettings.Host} -p {dbSettings.Port} -U {dbSettings.Username} -d {dbSettings.Database} " +
                              $"--format=custom --verbose --no-owner --no-acl " +
                              $"--file=\"{backupFilePath}\"";

            // Set environment variables
            var environmentVariables = new Dictionary<string, string>
            {
                { "PGPASSWORD", dbSettings.Password },
                { "PGSSLMODE", dbSettings.SslMode ?? "Prefer" }
            };

            Console.WriteLine($"   - Creating backup: {backupFileName}");
            bool success = await RunProcessAsync("pg_dump", arguments, environmentVariables);
            
            if (success && File.Exists(backupFilePath))
            {
                return backupFilePath;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"   Error creating database backup: {ex.Message}");
            Console.ResetColor();
            return null;
        }
    }

    static async Task<bool> RestoreDatabaseAsync(DatabaseConnectionSettings dbSettings, string backupFilePath, bool dropIfExists)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dbSettings.Host) ||
                string.IsNullOrWhiteSpace(dbSettings.Database) ||
                string.IsNullOrWhiteSpace(dbSettings.Username) ||
                string.IsNullOrWhiteSpace(dbSettings.Password))
            {
                Console.Error.WriteLine("Database settings are incomplete for restore.");
                return false;
            }

            if (!File.Exists(backupFilePath))
            {
                Console.Error.WriteLine($"Backup file does not exist: {backupFilePath}");
                return false;
            }

            // Drop database if requested
            if (dropIfExists)
            {
                Console.WriteLine($"   - Dropping existing database '{dbSettings.Database}' if it exists...");
                bool dropSuccess = await DropDatabaseAsync(dbSettings);
                if (!dropSuccess)
                {
                    Console.WriteLine($"   - Warning: Could not drop database. Continuing with restore...");
                }
            }

            // Create database if it doesn't exist
            Console.WriteLine($"   - Ensuring database '{dbSettings.Database}' exists...");
            bool createSuccess = await CreateDatabaseAsync(dbSettings);
            if (!createSuccess)
            {
                Console.WriteLine($"   - Warning: Could not create database. Continuing with restore...");
            }

            // Build pg_restore command
            string arguments = $"-h {dbSettings.Host} -p {dbSettings.Port} -U {dbSettings.Username} -d {dbSettings.Database} " +
                              $"--verbose --no-owner --no-acl --clean --if-exists " +
                              $"\"{backupFilePath}\"";

            // Set environment variables
            var environmentVariables = new Dictionary<string, string>
            {
                { "PGPASSWORD", dbSettings.Password },
                { "PGSSLMODE", dbSettings.SslMode ?? "Prefer" }
            };

            Console.WriteLine($"   - Restoring database from: {Path.GetFileName(backupFilePath)}");
            return await RunProcessAsync("pg_restore", arguments, environmentVariables);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"   Error restoring database: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    static async Task<bool> DropDatabaseAsync(DatabaseConnectionSettings dbSettings)
    {
        try
        {
            // Connect to postgres database to drop the target database
            string arguments = $"-h {dbSettings.Host} -p {dbSettings.Port} -U {dbSettings.Username} -d postgres " +
                              $"-c \"DROP DATABASE IF EXISTS \\\"{dbSettings.Database}\\\";\"";

            var environmentVariables = new Dictionary<string, string>
            {
                { "PGPASSWORD", dbSettings.Password },
                { "PGSSLMODE", dbSettings.SslMode ?? "Prefer" }
            };

            return await RunProcessAsync("psql", arguments, environmentVariables);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   Warning: Could not drop database: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    static async Task<bool> CreateDatabaseAsync(DatabaseConnectionSettings dbSettings)
    {
        try
        {
            // Connect to postgres database to create the target database
            string arguments = $"-h {dbSettings.Host} -p {dbSettings.Port} -U {dbSettings.Username} -d postgres " +
                              $"-c \"CREATE DATABASE \\\"{dbSettings.Database}\\\";\"";

            var environmentVariables = new Dictionary<string, string>
            {
                { "PGPASSWORD", dbSettings.Password },
                { "PGSSLMODE", dbSettings.SslMode ?? "Prefer" }
            };

            return await RunProcessAsync("psql", arguments, environmentVariables);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   Warning: Could not create database: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    static async Task<bool> VerifyDatabaseMigrationAsync(DatabaseConnectionSettings sourceSettings, DatabaseConnectionSettings destSettings)
    {
        try
        {
            // Get table counts from both databases
            var sourceTableCount = await GetTableCountAsync(sourceSettings);
            var destTableCount = await GetTableCountAsync(destSettings);

            if (sourceTableCount == -1 || destTableCount == -1)
            {
                Console.Error.WriteLine("   Could not verify table counts.");
                return false;
            }

            if (sourceTableCount != destTableCount)
            {
                Console.Error.WriteLine($"   Table count mismatch: Source={sourceTableCount}, Destination={destTableCount}");
                return false;
            }

            Console.WriteLine($"   Verification successful: Both databases have {sourceTableCount} tables");
            return true;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"   Error during verification: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    static async Task<int> GetTableCountAsync(DatabaseConnectionSettings dbSettings)
    {
        try
        {
            string arguments = $"-h {dbSettings.Host} -p {dbSettings.Port} -U {dbSettings.Username} -d {dbSettings.Database} " +
                              $"-t -c \"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';\"";

            var environmentVariables = new Dictionary<string, string>
            {
                { "PGPASSWORD", dbSettings.Password },
                { "PGSSLMODE", dbSettings.SslMode ?? "Prefer" }
            };

            var outputBuilder = new StringBuilder();
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "psql",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var kvp in environmentVariables)
            {
                processStartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }

            using var process = new Process { StartInfo = processStartInfo };
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                string output = outputBuilder.ToString().Trim();
                // Parse the count from psql output (remove headers and footers)
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (int.TryParse(line.Trim(), out int count))
                    {
                        return count;
                    }
                }
            }

            return -1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   Warning: Could not get table count: {ex.Message}");
            Console.ResetColor();
            return -1;
        }
    }
}