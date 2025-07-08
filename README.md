# How to config 

NOTE: if no connection to database, copy db_backup.dump file from bk/ folder to bin\Debug\net9.0 folder.

```bash

## Download Postgres Tools

https://www.enterprisedb.com/download-postgresql-binaries
Extract and add into PATH 

## Initialize User Secrets for Passwords:

Since passwords shouldn't be in appsettings.json, use User Secrets for local development. Run these commands in the PgSyncApp directory:

```bash
# Initialize (only need to run once per project)
dotnet user-secrets init

# Set the secrets (use your actual passwords)
dotnet user-secrets set "Azure:Password" "YOUR_SECURE_AZURE_PASSWORD"
dotnet user-secrets set "LocalDocker:PgPassword" "YOUR_SECURE_LOCAL_DOCKER_PASSWORD"
```

`
```json
{
  "Azure": {
    "Host": "force-eleven-db.postgres.database.azure.com", 
    "User": "forceelevensa", 
    "DbName": "postgres" 
  },
  "LocalDocker": {
    "ContainerName": "my_local_pg",
    "LocalPort": "5432",
    "PostgresImage": "postgres:17"
  },
  "Backup": {
    "FileName": "db_backup.dump"
  }
}
```


Expected output, naveigate to the folder where the exe is located and run the command as a administrator:

```bash
> .\\PgSyncApp.exe
--- Starting Azure PostgreSQL Sync to Local Docker ---

[Step 1] Backing up Azure database 'postgres' from 'force-eleven-db.postgres.database.azure.com'...

[Step 1] Backup file 'C:\workspace\Force-Eleven\Force-ElevenApi\PgSyncApp\bin\Debug\net9.0\db_backup.dump' already exists.
[Step 1] Backup completed: C:\workspace\Force-Eleven\Force-ElevenApi\PgSyncApp\bin\Debug\net9.0\db_backup.dump

[Step 2] Setting up local Docker container 'my_local_pg'...
   - Stopping and removing existing container 'my_local_pg' (if any)...
   Warnings/Notices (stderr):
Error response from daemon: No such container: my_local_pg
   - Starting new container 'my_local_pg'...
run -d --name my_local_pg -e POSTGRES_PASSWORD=SuperSecretDevPassword123! -e POSTGRES_DB=postgres -p 5432:5432 -v pg_local_data_postgres:/var/lib/postgresql/data postgres:17
   Output:
141215af87939060cd29f77b46bc113285d5f6b9c849947fae6a8c2e95d862ac
   - Waiting a few seconds for PostgreSQL to initialize...
[Step 2] Docker container 'my_local_pg' is running.

[Step 3] Generating Table of Contents (TOC) from backup...
   Output redirected to: C:\workspace\Force-Eleven\Force-ElevenApi\PgSyncApp\bin\Debug\net9.0\toc.list
[Step 3] TOC file generated: C:\workspace\Force-Eleven\Force-ElevenApi\PgSyncApp\bin\Debug\net9.0\toc.list

[Step 4] Editing TOC file to exclude Azure extensions...
   - Reading TOC and commenting out Azure extensions...
   - Successfully wrote edited TOC to C:\workspace\Force-Eleven\Force-ElevenApi\PgSyncApp\bin\Debug\net9.0\toc_edited.list
[Step 4] Edited TOC file created: C:\workspace\Force-Eleven\Force-ElevenApi\PgSyncApp\bin\Debug\net9.0\toc_edited.list

[Step 5] Copying backup file and edited TOC to container...
   - Copying 'db_backup.dump' to 'my_local_pg:/tmp/db_backup.dump'
   - Copying 'toc_edited.list' to 'my_local_pg:/tmp/toc_edited.list'
[Step 5] Files copied.

[Step 6] Restoring backup into database 'postgres' inside container using edited TOC...
RestoreBackupInContainerFromListAsync
   Warnings/Notices (stderr):
pg_restore: connecting to database for restore
pg_restore: creating SCHEMA "LoggingSchema"
pg_restore: creating SCHEMA "public"
pg_restore: creating TABLE "LoggingSchema.logs"
pg_restore: creating TABLE "public.Users"
pg_restore: creating TABLE "public.__EFMigrationsHistory"
pg_restore: creating TABLE "public.category"
pg_restore: creating SEQUENCE "public.category_Id_seq"
pg_restore: creating TABLE "public.company"
pg_restore: creating SEQUENCE "public.company_Id_seq"
pg_restore: creating TABLE "public.inventory"
pg_restore: creating SEQUENCE "public.inventory_Id_seq"
pg_restore: creating TABLE "public.product"
pg_restore: creating SEQUENCE "public.product_Id_seq"
pg_restore: creating TABLE "public.product_inventory"
pg_restore: creating SEQUENCE "public.product_inventory_Id_seq"
pg_restore: processing data for table "LoggingSchema.logs"
pg_restore: processing data for table "public.Users"
pg_restore: processing data for table "public.__EFMigrationsHistory"
pg_restore: processing data for table "public.category"
pg_restore: processing data for table "public.company"
pg_restore: processing data for table "public.inventory"
pg_restore: processing data for table "public.product"
pg_restore: processing data for table "public.product_inventory"
pg_restore: executing SEQUENCE SET category_Id_seq
pg_restore: executing SEQUENCE SET company_Id_seq
pg_restore: executing SEQUENCE SET inventory_Id_seq
pg_restore: executing SEQUENCE SET product_Id_seq
pg_restore: executing SEQUENCE SET product_inventory_Id_seq
pg_restore: creating CONSTRAINT "public.Users PK_Users"
pg_restore: creating CONSTRAINT "public.__EFMigrationsHistory PK___EFMigrationsHistory"
pg_restore: creating CONSTRAINT "public.category PK_category"
pg_restore: creating CONSTRAINT "public.company PK_company"
pg_restore: creating CONSTRAINT "public.inventory PK_inventory"
pg_restore: creating CONSTRAINT "public.product PK_product"
pg_restore: creating CONSTRAINT "public.product_inventory PK_product_inventory"
pg_restore: creating INDEX "public.IX_category_CompanyId"
pg_restore: creating INDEX "public.IX_inventory_CompanyId"
pg_restore: creating INDEX "public.IX_product_CompanyId"
pg_restore: creating INDEX "public.IX_product_inventory_InventoryId"
pg_restore: creating INDEX "public.IX_product_inventory_ProductId"
pg_restore: creating FK CONSTRAINT "public.category FK_category_company_CompanyId"
pg_restore: creating FK CONSTRAINT "public.inventory FK_inventory_company_CompanyId"
pg_restore: creating FK CONSTRAINT "public.product FK_product_company_CompanyId"
pg_restore: creating FK CONSTRAINT "public.product_inventory FK_product_inventory_inventory_InventoryId"
pg_restore: creating FK CONSTRAINT "public.product_inventory FK_product_inventory_product_ProductId"
[Step 6] Restore completed successfully.

--- Sync Process Finished ---
Connect locally using: Host=localhost, Port=5432, DB=postgres, User=postgres, Password=******
```