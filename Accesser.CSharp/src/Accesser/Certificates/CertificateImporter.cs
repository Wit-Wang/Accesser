using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Accesser.Certificates;

public static class CertificateImporter
{
    public static void ImportToSystem(string certPath, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            ImportToWindowsStore(certPath, logger);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ImportToMacOsKeychain(certPath, logger);
        }
        else if (OperatingSystem.IsLinux())
        {
            logger.LogWarning("Please manually import certificate {Path} to system trust store.", certPath);
        }
    }

    private static void ImportToWindowsStore(string certPath, ILogger logger)
    {
        try
        {
            var cert = X509Certificate2.CreateFromPemFile(certPath);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            logger.LogInformation("Certificate imported to Windows certificate store.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import certificate to Windows store.");
        }
    }

    private static void ImportToMacOsKeychain(string certPath, ILogger logger)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain {certPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            process?.WaitForExit();
            logger.LogInformation("Certificate import command executed for macOS.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import certificate to macOS keychain.");
        }
    }
}
