using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Accesser.Utils;

public static class UpdateChecker
{
    private const string CurrentVersion = "1.0.0";

    public static async Task CheckForUpdatesAsync(ILogger logger)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Accesser-CSharp");
            var response = await client.GetAsync("https://api.github.com/repos/URenko/Accesser/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            using var jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!jsonDoc.RootElement.TryGetProperty("tag_name", out var tagElement))
            {
                return;
            }

            var latest = tagElement.GetString()?.TrimStart('v');
            if (Version.TryParse(latest, out var latestVersion) && Version.TryParse(CurrentVersion, out var currentVersion) && latestVersion > currentVersion)
            {
                logger.LogWarning("A new version is available: {Version}", latest);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Update check failed.");
        }
    }
}
