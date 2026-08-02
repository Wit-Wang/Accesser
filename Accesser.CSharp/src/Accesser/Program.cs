using Accesser.Certificates;
using Accesser.Configuration;
using Accesser.Dns;
using Accesser.Proxy;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("Accesser");
logger.LogInformation("Accesser v1.0.0");

SystemProxyManager? proxyManager = null;

try
{
    var config = ConfigurationManager.LoadConfiguration(args);

    var certManager = new CertificateManager(config, loggerFactory.CreateLogger<CertificateManager>());
    await certManager.InitializeAsync();

    if (config.ImportCa)
    {
        CertificateImporter.ImportToSystem(certManager.RootCertificatePath, loggerFactory.CreateLogger("CertificateImporter"));
    }

    var dnsResolver = new DnsResolver(config, loggerFactory.CreateLogger<DnsResolver>());
    var proxyServer = new ProxyServer(
        config,
        certManager,
        dnsResolver,
        loggerFactory.CreateLogger<ProxyServer>());

    if (config.SetProxy)
    {
        proxyManager = new SystemProxyManager(config, loggerFactory.CreateLogger<SystemProxyManager>());
        proxyManager.SetPacProxy($"http://localhost:{config.Server.Port}/pac/?t={Random.Shared.Next(65536)}");
    }

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        proxyManager?.SetPacProxy(null);
        Environment.Exit(0);
    };

    await proxyServer.StartAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error occurred");
    proxyManager?.SetPacProxy(null);
    return 1;
}

return 0;
