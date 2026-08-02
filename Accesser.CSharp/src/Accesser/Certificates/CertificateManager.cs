using Accesser.Configuration;
using Microsoft.Extensions.Logging;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Accesser.Certificates;

public class CertificateManager
{
    private readonly string _certPath;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, (X509Certificate2 Cert, DateTimeOffset Expiry)> _certCache = new();
    private readonly SemaphoreSlim _certLock = new(1, 1);
    private X509Certificate2? _rootCert;
    private RSA? _rootRsa;
    private readonly DomainParser _domainParser = new(new WebTldRuleProvider());

    public string RootCertificatePath => Path.Combine(_certPath, "root.crt");

    public CertificateManager(AppConfig config, ILogger logger)
    {
        _certPath = DetermineCertPath(config);
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_certPath);

        var rootCertPath = Path.Combine(_certPath, "root.crt");
        var rootKeyPath = Path.Combine(_certPath, "root.key");

        if (!File.Exists(rootCertPath) || !File.Exists(rootKeyPath))
        {
            await CreateRootCertificateAsync(rootCertPath, rootKeyPath);
        }

        var rootPem = await File.ReadAllTextAsync(rootCertPath);
        var keyPem = await File.ReadAllTextAsync(rootKeyPath);

        _rootCert = X509Certificate2.CreateFromPem(rootPem, keyPem);
        _rootRsa = RSA.Create();
        _rootRsa.ImportFromPem(keyPem);

        _logger.LogInformation("Certificate manager initialized at {Path}", _certPath);
    }

    public async Task<X509Certificate2> GetOrCreateCertificateAsync(string serverName)
    {
        var normalized = NormalizeServerName(serverName);

        await _certLock.WaitAsync();
        try
        {
            if (_certCache.TryGetValue(normalized, out var cached) && DateTimeOffset.UtcNow.AddDays(1) < cached.Expiry)
            {
                return cached.Cert;
            }

            var cert = await CreateDomainCertificateAsync(normalized);
            _certCache[normalized] = (cert, cert.NotAfter);
            return cert;
        }
        finally
        {
            _certLock.Release();
        }
    }

    private async Task CreateRootCertificateAsync(string certPath, string keyPath)
    {
        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest(
            "CN=Accesser, O=Accesser",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(25));

        await File.WriteAllTextAsync(certPath, PemEncode("CERTIFICATE", cert.Export(X509ContentType.Cert)));
        await File.WriteAllTextAsync(keyPath, PemEncode("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()));

        using var pfxCert = cert.CopyWithPrivateKey(rsa);
        await File.WriteAllBytesAsync(Path.Combine(_certPath, "root.pfx"), pfxCert.Export(X509ContentType.Pkcs12));
    }

    private async Task<X509Certificate2> CreateDomainCertificateAsync(string serverName)
    {
        if (_rootCert is null || _rootRsa is null)
        {
            throw new InvalidOperationException("Certificate manager is not initialized.");
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Accesser_Proxy, O=Accesser",
            ecdsa,
            HashAlgorithmName.SHA256);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(serverName);
        sanBuilder.AddDnsName($"*.{serverName}");
        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [
                new Oid("1.3.6.1.5.5.7.3.1"),
                new Oid("1.3.6.1.5.5.7.3.2")
            ],
            true));
        request.CertificateExtensions.Add(new X509AuthorityKeyIdentifierExtension(_rootCert, false, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var serial = RandomNumberGenerator.GetBytes(16);

        using var issued = request.Create(
            _rootCert.SubjectName,
            X509SignatureGenerator.CreateForRSA(_rootRsa, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddDays(30),
            serial);

        using var issuedWithKey = issued.CopyWithPrivateKey(ecdsa);
        var pfxBytes = issuedWithKey.Export(X509ContentType.Pkcs12);
        var certPath = Path.Combine(_certPath, $"{serverName}.pfx");
        await File.WriteAllBytesAsync(certPath, pfxBytes);
        await File.WriteAllTextAsync(Path.Combine(_certPath, $"{serverName}.crt"), PemEncode("CERTIFICATE", issued.Export(X509ContentType.Cert)));

        return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);
    }

    private string NormalizeServerName(string serverName)
    {
        try
        {
            var parsed = _domainParser.Parse(serverName);
            if (!string.IsNullOrEmpty(parsed.SubDomain) && parsed.SubDomain.Contains('.'))
            {
                var pieces = parsed.SubDomain.Split('.', StringSplitOptions.RemoveEmptyEntries);
                return $"{pieces[^1]}.{parsed.Domain}.{parsed.TLD}";
            }

            return $"{parsed.Domain}.{parsed.TLD}";
        }
        catch
        {
            var parts = serverName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2)
            {
                return serverName;
            }

            return string.Join('.', parts.Skip(parts.Length - 3));
        }
    }

    private static string DetermineCertPath(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.StateDir))
        {
            return Path.Combine(config.StateDir, "CERT");
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            if (Environment.UserName == "root")
            {
                return Path.Combine("/var/lib", "accesser", "CERT");
            }

            var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrWhiteSpace(stateHome))
            {
                return Path.Combine(stateHome, "accesser", "CERT");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "accesser", "CERT");
        }

        return Path.Combine(AppContext.BaseDirectory, "CERT");
    }

    private static string PemEncode(string label, byte[] data)
    {
        var base64 = Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN {label}-----\n{base64}\n-----END {label}-----\n";
    }
}
