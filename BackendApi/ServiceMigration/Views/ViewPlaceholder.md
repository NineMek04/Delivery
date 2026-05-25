# SQL Views Placeholder Directory

This folder is designed to house any future raw SQL or database views setup scripts. 

## How to add a new View in the future:
1. Write your custom View script here as a SQL or Markdown file.
2. Register and execute the SQL script in `PostgresAdvancedConfigurator.cs` under the `SetupDatabaseViewsAsync` method.

Example:
```csharp
await ExecuteSqlRawAsync(context, @"
    CREATE OR REPLACE VIEW ""v_RiderPerformance"" AS
    SELECT r.""Id"", r.""FullName"", count(o.""Id"") as ""TotalDeliveries""
    FROM ""Riders"" r
    LEFT JOIN ""Orders"" o ON o.""AssignedRiderId"" = r.""Id"" AND o.""State"" = 5
    GROUP BY r.""Id"", r.""FullName"";
");
```
