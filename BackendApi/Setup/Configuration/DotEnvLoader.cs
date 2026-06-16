using System.Text;

namespace BackendApi.Setup.Configuration;

public static class DotEnvLoader
{
    public static Dictionary<string, string?> Load(string rootPath)
    {
        var filePath = Path.Combine(rootPath, ".env");
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
        {
            return values;
        }

        foreach (var rawLine in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }
}

