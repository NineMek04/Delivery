using VaultSharp;
using VaultSharp.V1.AuthMethods.AppRole;

namespace BackendApi.Setup.Configuration;

public class VaultConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new VaultConfigurationProvider();
    }
}

public class VaultConfigurationProvider : ConfigurationProvider
{
    public override void Load()
    {
        LoadAsync().GetAwaiter().GetResult();
    }

    private async Task LoadAsync()
    {
        var vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR");
        var roleIdFile = Environment.GetEnvironmentVariable("VAULT_ROLE_ID_FILE");
        var secretIdFile = Environment.GetEnvironmentVariable("VAULT_SECRET_ID_FILE");
        var isRequired = Environment.GetEnvironmentVariable("VAULT_REQUIRED")?.ToLower() == "true";

        if (string.IsNullOrEmpty(vaultAddr) || string.IsNullOrEmpty(roleIdFile) || string.IsNullOrEmpty(secretIdFile))
        {
            if (isRequired)
                throw new Exception("VAULT_REQUIRED is true but VAULT_ADDR, VAULT_ROLE_ID_FILE, or VAULT_SECRET_ID_FILE is missing.");
            return;
        }

        try
        {
            if (!File.Exists(roleIdFile) || !File.Exists(secretIdFile))
            {
                throw new FileNotFoundException("AppRole credential files not found.");
            }

            var roleId = (await File.ReadAllTextAsync(roleIdFile)).Trim();
            var secretId = (await File.ReadAllTextAsync(secretIdFile)).Trim();

            var authMethod = new AppRoleAuthMethodInfo(roleId, secretId);
            var vaultClientSettings = new VaultClientSettings(vaultAddr, authMethod);
            var vaultClient = new VaultClient(vaultClientSettings);

            // Fetch secrets
            var secret = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync("delivery/backend", mountPoint: "secret");

            if (secret?.Data?.Data != null)
            {
                foreach (var kvp in secret.Data.Data)
                {
                    // Map keys like Jwt__Keys__v1 to Jwt:Keys:v1 and connection strings
                    var key = kvp.Key.Replace("__", ":");
                    
                    // Special case for PostgresPassword inside ConnectionString
                    if (key == "PostgresPassword")
                    {
                        var defaultConn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                            ?? "Host=db;Database=delivery_db;Username=postgres;Maximum Pool Size=1024;";
                        
                        if (defaultConn.Contains("${POSTGRES_PASSWORD}"))
                        {
                            Data["ConnectionStrings:DefaultConnection"] = defaultConn.Replace("${POSTGRES_PASSWORD}", kvp.Value?.ToString());
                        }
                        else
                        {
                            Data["ConnectionStrings:DefaultConnection"] = defaultConn + $"Password={kvp.Value?.ToString()};";
                        }
                    }
                    else
                    {
                        Data[key] = kvp.Value?.ToString() ?? "";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (isRequired)
            {
                throw new Exception("Failed to load configuration from Vault and VAULT_REQUIRED is true.", ex);
            }
            Console.WriteLine($"[Vault] Warning: Failed to load secrets from Vault. Fallback to Env. Error: {ex.Message}");
        }
    }
}

public static class VaultConfigurationExtensions
{
    public static IConfigurationBuilder AddVaultConfiguration(this IConfigurationBuilder builder)
    {
        return builder.Add(new VaultConfigurationSource());
    }
}

