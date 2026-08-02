using Accesser.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Accesser.Proxy;

public class SystemProxyManager
{
    private readonly AppConfig _config;
    private readonly ILogger _logger;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    public SystemProxyManager(AppConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public void SetPacProxy(string? pacUrl)
    {
        if (!_config.SetProxy || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
                writable: true);

            if (key == null)
            {
                _logger.LogError("Cannot open registry key for proxy settings.");
                return;
            }

            if (string.IsNullOrEmpty(pacUrl))
            {
                key.DeleteValue("AutoConfigURL", false);
            }
            else
            {
                key.SetValue("AutoConfigURL", pacUrl);
            }

            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set system proxy.");
        }
    }
}
