# C# Rewrite Instructions for Accesser

## Overview

This document provides comprehensive instructions for rewriting the Accesser Python application in C#. Accesser is a local HTTP proxy tool that solves SNI RST (Server Name Indication Reset) issues, enabling access to sites like Wikipedia and Pixiv in regions where they are blocked.

## Project Architecture

### Current Python Implementation

The project consists of approximately 841 lines of Python code with the following structure:

```
accesser/
├── __init__.py          # Main proxy server logic (~262 lines)
├── __main__.py          # Entry point
└── utils/
    ├── certmanager.py   # Certificate generation and management (~187 lines)
    ├── cert_verify.py   # Certificate verification
    ├── importca.py      # Certificate import to system
    ├── log.py           # Logging configuration
    ├── setting.py       # Configuration management (~131 lines)
    └── sysproxy.py      # System proxy settings (~23 lines)
```

### Core Functionality

1. **HTTP/HTTPS Proxy Server**: Local proxy that intercepts CONNECT requests
2. **SNI Manipulation**: Removes or alters SNI (Server Name Indication) in TLS handshakes
3. **Certificate Management**: Generates self-signed CA and domain certificates on-the-fly
4. **DNS Resolution**: Supports multiple DNS protocols (DNS, DoH, DoT, DoQ)
5. **PAC File Serving**: Provides Proxy Auto-Configuration files
6. **Certificate Verification**: Custom hostname verification with pattern matching
7. **System Integration**: Automatic proxy configuration and CA certificate installation

## Technology Stack for C# Implementation

### .NET Version
- **Target**: .NET 8.0 or later (LTS)
- **Reason**: Best async/await support, cross-platform, modern C# features

### Key NuGet Packages

```xml
<PackageReference Include="BouncyCastle.Cryptography" Version="2.3.0" />
<PackageReference Include="DnsClient" Version="1.7.0" />
<PackageReference Include="Tomlyn" Version="0.17.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
<PackageReference Include="Nager.PublicSuffix" Version="3.0.0" />
```

### Additional Platform-Specific Dependencies

**For Windows:**
- System.Management
- Windows Registry APIs (built-in)

**For Linux:**
- System.CommandLine (for better CLI argument parsing)

## Project Structure for C# Implementation

```
Accesser.CSharp/
├── Accesser.sln
├── src/
│   └── Accesser/
│       ├── Accesser.csproj
│       ├── Program.cs                    # Entry point
│       ├── ProxyServer.cs                # Main proxy server
│       ├── RequestHandler.cs             # HTTP/HTTPS request handling
│       ├── Certificates/
│       │   ├── CertificateManager.cs     # Certificate generation
│       │   ├── CertificateVerifier.cs    # Custom cert verification
│       │   └── CertificateImporter.cs    # System CA import
│       ├── Dns/
│       │   ├── DnsResolver.cs            # DNS resolution wrapper
│       │   └── DnsProtocolHandler.cs     # DoH, DoT, DoQ support
│       ├── Configuration/
│       │   ├── ConfigurationManager.cs   # TOML config loader
│       │   ├── ConfigModel.cs            # Configuration classes
│       │   └── RulesManager.cs           # Rules management
│       ├── Proxy/
│       │   ├── PacFileHandler.cs         # PAC file serving
│       │   └── SystemProxyManager.cs     # OS proxy settings
│       ├── Utils/
│       │   ├── Logger.cs                 # Logging wrapper
│       │   └── StreamForwarder.cs        # Async stream forwarding
│       └── Resources/
│           ├── pac                       # PAC file template
│           ├── config.toml              # Default config
│           └── rules.toml               # Default rules
└── tests/
    └── Accesser.Tests/
        └── Accesser.Tests.csproj
```

## Step-by-Step Implementation Guide

### Phase 1: Project Setup

#### 1.1 Create .NET Project

```bash
# Create solution
dotnet new sln -n Accesser

# Create console application
dotnet new console -n Accesser -f net8.0

# Add project to solution
dotnet sln add Accesser/Accesser.csproj

# Create test project
dotnet new xunit -n Accesser.Tests
dotnet sln add Accesser.Tests/Accesser.Tests.csproj
```

#### 1.2 Configure Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>accesser</AssemblyName>
    <RootNamespace>Accesser</RootNamespace>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>A tool for solving SNI RST issues</Description>
    <Copyright>Copyright (C) 2024</Copyright>
    <PackageLicenseExpression>GPL-3.0-or-later</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BouncyCastle.Cryptography" Version="2.3.0" />
    <PackageReference Include="DnsClient" Version="1.7.0" />
    <PackageReference Include="Tomlyn" Version="0.17.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Resources\**\*" />
  </ItemGroup>
</Project>
```

### Phase 2: Core Components Implementation

#### 2.1 Program.cs - Entry Point

```csharp
using Accesser;
using Accesser.Configuration;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

logger.LogInformation("Accesser v1.0.0 Copyright (C) 2024");

try
{
    // Parse command line arguments
    var config = ConfigurationManager.LoadConfiguration(args);
    
    // Initialize certificate manager
    var certManager = new CertificateManager(config, logger);
    await certManager.InitializeAsync();
    
    // Setup DNS resolver
    var dnsResolver = new DnsResolver(config, logger);
    
    // Create and start proxy server
    var proxyServer = new ProxyServer(config, certManager, dnsResolver, logger);
    
    // Setup system proxy if needed
    if (config.SetProxy)
    {
        var proxyManager = new SystemProxyManager(config, logger);
        proxyManager.SetPacProxy($"http://localhost:{config.ServerPort}/pac/?t={Random.Shared.Next(65536)}");
    }
    
    // Start server
    await proxyServer.StartAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error occurred");
    return 1;
}

return 0;
```

#### 2.2 Configuration Management

**ConfigModel.cs:**

```csharp
namespace Accesser.Configuration;

public class ServerConfig
{
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7654;
    public string? PacHost { get; set; }
}

public class DnsConfig
{
    public List<string> Nameserver { get; set; } = new();
}

public class AppConfig
{
    public ServerConfig Server { get; set; } = new();
    public DnsConfig DNS { get; set; } = new();
    public Dictionary<string, string> Hosts { get; set; } = new();
    public Dictionary<string, string> AlterHostname { get; set; } = new();
    public Dictionary<string, object> CertVerify { get; set; } = new();
    public Dictionary<string, string> HttpRedirect { get; set; } = new();
    public bool CheckHostname { get; set; } = true;
    public bool Ipv6 { get; set; } = false;
    public bool SetProxy { get; set; } = true;
    public bool ImportCa { get; set; } = true;
    public string? StateDir { get; set; }
}
```

**ConfigurationManager.cs:**

```csharp
using System.IO;
using Tomlyn;

namespace Accesser.Configuration;

public class ConfigurationManager
{
    public static AppConfig LoadConfiguration(string[] args)
    {
        // Parse command line arguments
        var parser = new ArgumentParser();
        var options = parser.Parse(args);
        
        // Load base configuration
        var config = LoadConfigFile("config.toml");
        
        // Load rules
        var rules = LoadRulesConfiguration();
        
        // Merge configurations (rules.toml + custom rules + config.toml)
        var mergedConfig = MergeConfigurations(rules, config);
        
        // Apply command line overrides
        ApplyCommandLineOptions(mergedConfig, options);
        
        return mergedConfig;
    }
    
    private static AppConfig LoadConfigFile(string path)
    {
        if (!File.Exists(path))
        {
            // Copy default config from embedded resources
            CopyDefaultConfig(path);
        }
        
        var tomlContent = File.ReadAllText(path);
        var model = Toml.ToModel<AppConfig>(tomlContent);
        return model;
    }
    
    private static AppConfig LoadRulesConfiguration()
    {
        var baseRules = LoadConfigFile("rules.toml");
        
        // Load custom rules from rules directory
        if (Directory.Exists("rules"))
        {
            foreach (var ruleFile in Directory.GetFiles("rules", "*.toml").OrderBy(f => f))
            {
                var customRules = LoadConfigFile(ruleFile);
                baseRules = MergeConfigurations(baseRules, customRules);
            }
        }
        
        return baseRules;
    }
    
    private static AppConfig MergeConfigurations(AppConfig baseConfig, AppConfig overlay)
    {
        // Deep merge logic - overlay takes precedence
        // This should recursively merge dictionaries and combine lists without duplicates
        //
        // RECOMMENDED APPROACH: Use a JSON-based merge with System.Text.Json
        // 
        // Example implementation:
        /*
        using System.Text.Json;
        using System.Text.Json.Nodes;
        
        var baseJson = JsonSerializer.Serialize(baseConfig);
        var overlayJson = JsonSerializer.Serialize(overlay);
        
        var baseNode = JsonNode.Parse(baseJson)!.AsObject();
        var overlayNode = JsonNode.Parse(overlayJson)!.AsObject();
        
        DeepMerge(baseNode, overlayNode);
        
        return JsonSerializer.Deserialize<AppConfig>(baseNode.ToJsonString())!;
        
        void DeepMerge(JsonObject target, JsonObject source)
        {
            foreach (var prop in source)
            {
                if (prop.Value is JsonObject sourceObj && 
                    target[prop.Key] is JsonObject targetObj)
                {
                    DeepMerge(targetObj, sourceObj);
                }
                else if (prop.Value is JsonArray sourceArr && 
                         target[prop.Key] is JsonArray targetArr)
                {
                    // Combine arrays without duplicates
                    var combined = new JsonArray();
                    var seen = new HashSet<string>();
                    foreach (var item in targetArr.Concat(sourceArr))
                    {
                        var itemStr = item?.ToJsonString();
                        if (itemStr != null && seen.Add(itemStr))
                        {
                            combined.Add(item?.DeepClone());
                        }
                    }
                    target[prop.Key] = combined;
                }
                else
                {
                    target[prop.Key] = prop.Value?.DeepClone();
                }
            }
        }
        */
        
        // PLACEHOLDER: For this example, we return overlay
        // Replace with the implementation above in production code
        return overlay;
    }
}
```

#### 2.3 Certificate Management

**CertificateManager.cs:**

```csharp
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Accesser.Certificates;

public class CertificateManager
{
    private readonly string _certPath;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, (X509Certificate2 Cert, DateTime Expiry)> _certCache;
    private readonly SemaphoreSlim _certLock;
    private X509Certificate2? _rootCert;
    private AsymmetricKeyParameter? _rootKey;
    private Org.BouncyCastle.X509.X509Certificate? _rootCertBC;
    private AsymmetricCipherKeyPair? _domainKeyPair;
    
    public CertificateManager(AppConfig config, ILogger logger)
    {
        _certPath = DetermineCertPath(config);
        _logger = logger;
        _certCache = new ConcurrentDictionary<string, (X509Certificate2, DateTime)>();
        _certLock = new SemaphoreSlim(1, 1);
    }
    
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_certPath);
        
        // Load or create root CA
        var rootCertPath = Path.Combine(_certPath, "root.crt");
        var rootKeyPath = Path.Combine(_certPath, "root.key");
        
        if (!File.Exists(rootCertPath) || !File.Exists(rootKeyPath))
        {
            await CreateRootCertificateAsync();
        }
        
        _rootCert = new X509Certificate2(rootCertPath);
        
        // Load root cert in BouncyCastle format for issuer name
        var rootCertBytes = File.ReadAllBytes(rootCertPath);
        _rootCertBC = new X509CertificateParser().ReadCertificate(rootCertBytes);
        
        // Load root private key
        var rootKeyBytes = File.ReadAllBytes(rootKeyPath);
        _rootKey = PrivateKeyFactory.CreateKey(rootKeyBytes);
        
        // Generate domain signing key (ECC P-256) - single key reused for all certificates
        var keyGen = new ECKeyPairGenerator("EC");
        var curve = ECNamedCurveTable.GetByName("secp256r1");
        var domainParameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
        keyGen.Init(new ECKeyGenerationParameters(domainParameters, new SecureRandom()));
        _domainKeyPair = keyGen.GenerateKeyPair();
        
        _logger.LogInformation("Certificate manager initialized");
    }
    
    private async Task CreateRootCertificateAsync()
    {
        _logger.LogInformation("Creating root CA certificate");
        
        // Generate RSA 4096-bit key for root CA
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 4096));
        var keyPair = keyGen.GenerateKeyPair();
        
        // Create certificate
        var certGen = new X509V3CertificateGenerator();
        var subject = new X509Name("O=Accesser, CN=Accesser");
        
        certGen.SetSubjectDN(subject);
        certGen.SetIssuerDN(subject);
        certGen.SetSerialNumber(BigInteger.ProbablePrime(120, new Random()));
        certGen.SetNotBefore(DateTime.UtcNow);
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(25));
        certGen.SetPublicKey(keyPair.Public);
        
        // Add extensions
        certGen.AddExtension(
            X509Extensions.BasicConstraints,
            true,
            new BasicConstraints(true));
        
        certGen.AddExtension(
            X509Extensions.KeyUsage,
            true,
            new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CRLSign));
        
        var signatureFactory = new Asn1SignatureFactory("SHA256WithRSA", keyPair.Private);
        var cert = certGen.Generate(signatureFactory);
        
        // Save certificate and key
        File.WriteAllBytes(
            Path.Combine(_certPath, "root.crt"),
            cert.GetEncoded());
        
        var keyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private);
        File.WriteAllBytes(
            Path.Combine(_certPath, "root.key"),
            keyInfo.GetEncoded());
        
        _logger.LogInformation("Root CA certificate created");
    }
    
    public async Task<X509Certificate2> GetOrCreateCertificateAsync(string serverName)
    {
        // Normalize server name (keep one subdomain level)
        serverName = NormalizeServerName(serverName);
        
        await _certLock.WaitAsync();
        try
        {
            if (_certCache.TryGetValue(serverName, out var cached))
            {
                if (DateTime.UtcNow.AddDays(1) < cached.Expiry)
                {
                    return cached.Cert;
                }
            }
            
            var cert = await CreateDomainCertificateAsync(serverName);
            var expiry = DateTime.Parse(cert.GetExpirationDateString());
            _certCache[serverName] = (cert, expiry);
            
            _logger.LogDebug($"Certificate for {serverName} refreshed (expiry: {expiry})");
            
            return cert;
        }
        finally
        {
            _certLock.Release();
        }
    }
    
    private async Task<X509Certificate2> CreateDomainCertificateAsync(string serverName)
    {
        var certGen = new X509V3CertificateGenerator();
        var subject = new X509Name("O=Accesser, CN=Accesser_Proxy");
        
        certGen.SetSubjectDN(subject);
        certGen.SetIssuerDN(_rootCertBC!.SubjectDN);
        certGen.SetSerialNumber(BigInteger.ProbablePrime(120, new Random()));
        certGen.SetNotBefore(DateTime.UtcNow.AddMinutes(-10));
        certGen.SetNotAfter(DateTime.UtcNow.AddDays(30));
        certGen.SetPublicKey(_domainKeyPair!.Public);
        
        // Add Subject Alternative Name
        var altNames = new GeneralNames(new[]
        {
            new GeneralName(GeneralName.DnsName, serverName),
            new GeneralName(GeneralName.DnsName, $"*.{serverName}")
        });
        
        certGen.AddExtension(
            X509Extensions.SubjectAlternativeName,
            false,
            altNames);
        
        certGen.AddExtension(
            X509Extensions.KeyUsage,
            true,
            new KeyUsage(KeyUsage.DigitalSignature));
        
        certGen.AddExtension(
            X509Extensions.ExtendedKeyUsage,
            true,
            new ExtendedKeyUsage(
                KeyPurposeID.IdKPServerAuth,
                KeyPurposeID.IdKPClientAuth));
        
        var signatureFactory = new Asn1SignatureFactory("SHA256WithRSA", _rootKey);
        var cert = certGen.Generate(signatureFactory);
        
        // Save to file
        var certBytes = cert.GetEncoded();
        var keyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(_domainKeyPair!.Private);
        var combined = certBytes.Concat(keyInfo.GetEncoded()).ToArray();
        
        File.WriteAllBytes(
            Path.Combine(_certPath, $"{serverName}.crt"),
            combined);
        
        return new X509Certificate2(certBytes);
    }
    
    private string NormalizeServerName(string serverName)
    {
        // Normalize server name to keep only one subdomain level
        // This matches the Python implementation using tld library
        //
        // RECOMMENDED APPROACH: Use Nager.PublicSuffix NuGet package
        // Add to project: <PackageReference Include="Nager.PublicSuffix" Version="3.0.0" />
        //
        // Example with Nager.PublicSuffix:
        // var domainParser = new DomainParser(new WebTldRuleProvider());
        // var domainInfo = domainParser.Parse(serverName);
        // if (domainInfo.SubDomain != null && domainInfo.SubDomain.Contains('.'))
        // {
        //     var subParts = domainInfo.SubDomain.Split('.');
        //     return $"{subParts[^1]}.{domainInfo.Domain}.{domainInfo.TLD}";
        // }
        // return $"{domainInfo.Domain}.{domainInfo.TLD}";
        //
        // SIMPLE FALLBACK (not recommended for production):
        // This simplified logic doesn't handle multi-level TLDs like .co.uk correctly
        var parts = serverName.Split('.');
        if (parts.Length <= 2)
        {
            return serverName; // Already normalized (domain.tld)
        }
        
        // Keep last 3 parts (subdomain.domain.tld)
        // WARNING: This fails for domains like "www.example.co.uk"
        return string.Join(".", parts.Skip(parts.Length - 3));
    }
}
```

#### 2.4 Proxy Server

**ProxyServer.cs:**

```csharp
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;

namespace Accesser;

public class ProxyServer
{
    private readonly AppConfig _config;
    private readonly CertificateManager _certManager;
    private readonly DnsResolver _dnsResolver;
    private readonly ILogger _logger;
    private TcpListener? _listener;
    
    public ProxyServer(
        AppConfig config,
        CertificateManager certManager,
        DnsResolver dnsResolver,
        ILogger logger)
    {
        _config = config;
        _certManager = certManager;
        _dnsResolver = dnsResolver;
        _logger = logger;
    }
    
    public async Task StartAsync()
    {
        var address = IPAddress.Parse(_config.Server.Address);
        _listener = new TcpListener(address, _config.Server.Port);
        _listener.Start();
        
        _logger.LogInformation($"Serving on {address}:{_config.Server.Port}");
        
        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }
    
    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        TcpClient? remoteClient = null;
        
        try
        {
            var stream = client.GetStream();
            var endpoint = (IPEndPoint)client.Client.RemoteEndPoint!;
            
            // Read HTTP request line
            var requestLine = await ReadLineAsync(stream);
            _logger.LogDebug($"{endpoint.Address}:{endpoint.Port} say: {requestLine}");
            
            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return;
            
            var command = parts[0];
            var path = parts[1];
            
            switch (command)
            {
                case "CONNECT":
                    await HandleConnectAsync(stream, path, endpoint.Port);
                    break;
                case "GET":
                    await HandleGetAsync(stream, path);
                    break;
                default:
                    await HandleHttpRedirectAsync(stream, path);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
        finally
        {
            remoteClient?.Close();
        }
    }
    
    private async Task HandleConnectAsync(NetworkStream clientStream, string path, int clientPort)
    {
        var parts = path.Split(':');
        var host = parts[0];
        var port = int.Parse(parts[1]);
        
        // DNS resolution
        var remoteIp = await _dnsResolver.ResolveAsync(host);
        _logger.LogDebug($"[{clientPort,5}] DNS: {host} -> {remoteIp}");
        
        // Send connection established
        await WriteAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n");
        
        // Start TLS on client side
        var serverCert = await _certManager.GetOrCreateCertificateAsync(host);
        var sslStream = new SslStream(clientStream, false);
        await sslStream.AuthenticateAsServerAsync(
            serverCert,
            false,
            SslProtocols.Tls12 | SslProtocols.Tls13,
            false);
        
        // Determine server hostname for SNI
        var serverHostname = DetermineServerHostname(host);
        _logger.LogDebug($"[{clientPort,5}] server_hostname: {serverHostname}");
        
        // Connect to remote server
        var remoteClient = new TcpClient();
        await remoteClient.ConnectAsync(remoteIp, port);
        var remoteStream = remoteClient.GetStream();
        
        // Start TLS to remote with altered/empty SNI
        var remoteSslStream = new SslStream(remoteStream, false, ValidateRemoteCertificate);
        await remoteSslStream.AuthenticateAsClientAsync(
            serverHostname,
            null,
            SslProtocols.Tls12 | SslProtocols.Tls13,
            false);
        
        // Verify certificate
        var remoteCert = remoteSslStream.RemoteCertificate as X509Certificate2;
        if (remoteCert != null)
        {
            VerifyCertificate(remoteCert, host, clientPort);
        }
        
        // Forward streams bidirectionally
        var tasks = new[]
        {
            ForwardStreamAsync(sslStream, remoteSslStream),
            ForwardStreamAsync(remoteSslStream, sslStream)
        };
        
        await Task.WhenAny(tasks);
    }
    
    private async Task HandleGetAsync(NetworkStream stream, string path)
    {
        if (path.StartsWith("/pac/"))
        {
            await SendPacFileAsync(stream);
        }
        else if (path.StartsWith("/CERT/root."))
        {
            await SendCertificateAsync(stream, path);
        }
        else if (path == "/shutdown")
        {
            Environment.Exit(0);
        }
        else
        {
            await HandleHttpRedirectAsync(stream, path);
        }
    }
    
    private async Task SendPacFileAsync(NetworkStream stream)
    {
        // Load PAC file from file or embedded resource
        var pacContent = LoadPacFile();
        
        // Replace placeholders
        pacContent = pacContent
            .Replace("{{port}}", _config.Server.Port.ToString())
            .Replace("{{host}}", _config.Server.PacHost ?? "127.0.0.1");
        
        var response = $"HTTP/1.1 200 OK\r\n" +
                      $"Content-Type: application/x-ns-proxy-autoconfig\r\n" +
                      $"Content-Length: {pacContent.Length}\r\n\r\n" +
                      pacContent;
        
        await WriteAsync(stream, response);
    }
    
    private async Task ForwardStreamAsync(Stream source, Stream destination)
    {
        var buffer = new byte[32768];
        int bytesRead;
        
        while ((bytesRead = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
            await destination.FlushAsync();
        }
    }
    
    private string DetermineServerHostname(string host)
    {
        // Check alter_hostname configuration
        foreach (var pattern in _config.AlterHostname)
        {
            if (MatchPattern(host, pattern.Key))
            {
                return pattern.Value;
            }
        }
        return "";
    }
    
    private void VerifyCertificate(X509Certificate2 cert, string host, int port)
    {
        // Custom certificate verification matching Python's cert_verify.py
        //
        // Algorithm:
        // 1. Check if host matches any pattern in cert_verify configuration
        // 2. If matched, get the verification list and policy
        // 3. If policy is false, skip verification
        // 4. Otherwise, verify the cert matches one of the allowed hostnames
        //
        // Pattern matching should support wildcards (using fnmatch logic)
        // Hostname matching should check:
        // - SubjectAlternativeName DNS entries
        // - Subject CommonName (CN)
        // - Support for wildcard certificates (*.example.com)
        
        // Get cert verify configuration for this host
        string? certVerifyKey = null;
        foreach (var pattern in _config.CertVerify.Keys)
        {
            if (MatchPattern(host, pattern))
            {
                certVerifyKey = pattern;
                break;
            }
        }
        
        List<string> allowedHosts;
        bool shouldVerify;
        
        if (certVerifyKey != null)
        {
            var verifyConfig = _config.CertVerify[certVerifyKey];
            if (verifyConfig is bool boolValue)
            {
                shouldVerify = boolValue;
                allowedHosts = new List<string> { host };
            }
            else
            {
                shouldVerify = true;
                allowedHosts = (List<string>)verifyConfig;
            }
        }
        else
        {
            // Check if host has altered hostname
            var serverHostname = DetermineServerHostname(host);
            if (!string.IsNullOrEmpty(serverHostname))
            {
                allowedHosts = new List<string> { serverHostname };
                shouldVerify = _config.CheckHostname;
            }
            else
            {
                allowedHosts = new List<string> { host };
                shouldVerify = _config.CheckHostname;
            }
        }
        
        if (!shouldVerify)
        {
            return; // Skip verification
        }
        
        // Check if cert matches any allowed hostname
        bool matched = false;
        foreach (var allowedHost in allowedHosts)
        {
            if (CertificateMatchesHostname(cert, allowedHost))
            {
                matched = true;
                break;
            }
        }
        
        if (!matched)
        {
            _logger.LogWarning($"[{port,5}] Certificate doesn't match any of: {string.Join(", ", allowedHosts)}");
            throw new Exception("Certificate verification failed");
        }
    }
    
    private bool CertificateMatchesHostname(X509Certificate2 cert, string hostname)
    {
        // Check SubjectAlternativeName for DNS entries
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value == "2.5.29.17") // SubjectAlternativeName OID
            {
                var asnData = new System.Security.Cryptography.AsnEncodedData(ext.Oid, ext.RawData);
                var sanString = asnData.Format(false);
                
                // Parse SAN entries (format: "DNS Name=*.example.com")
                var lines = sanString.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("DNS Name="))
                    {
                        var dnsName = line.Substring(9);
                        if (MatchHostnameWithWildcard(hostname, dnsName))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        
        // Check Subject CN as fallback
        var subjectParts = cert.Subject.Split(',');
        foreach (var part in subjectParts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN="))
            {
                var cn = trimmed.Substring(3);
                if (MatchHostnameWithWildcard(hostname, cn))
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    private bool MatchHostnameWithWildcard(string hostname, string pattern)
    {
        // Support wildcard matching: *.example.com matches www.example.com
        // But NOT sub.www.example.com (wildcard matches only one level)
        
        if (pattern == hostname)
        {
            return true;
        }
        
        if (pattern.StartsWith("*."))
        {
            var domain = pattern.Substring(2);
            // Match if hostname ends with domain and has exactly one more component
            if (hostname.EndsWith("." + domain))
            {
                var prefix = hostname.Substring(0, hostname.Length - domain.Length - 1);
                // Ensure prefix doesn't contain dots (only one level)
                return !prefix.Contains('.');
            }
        }
        
        return false;
    }
}
```

#### 2.5 DNS Resolution

**DnsResolver.cs:**

```csharp
using DnsClient;
using DnsClient.Protocol;

namespace Accesser.Dns;

public class DnsResolver
{
    private readonly AppConfig _config;
    private readonly ILogger _logger;
    private readonly LookupClient _client;
    private readonly Dictionary<string, string> _cache;
    
    public DnsResolver(AppConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
        _cache = new Dictionary<string, string>();
        
        // Configure DNS client with custom nameservers
        var options = new LookupClientOptions();
        
        foreach (var nameserver in config.DNS.Nameserver)
        {
            // Parse DNS server URLs (support dns://, https://, tls://, quic://)
            AddNameserver(options, nameserver);
        }
        
        _client = new LookupClient(options);
    }
    
    public async Task<string> ResolveAsync(string domain)
    {
        // Check hosts file configuration first
        if (_config.Hosts.TryGetValue(domain, out var hostEntry))
        {
            return hostEntry;
        }
        
        // Check for wildcard hosts entries
        foreach (var (pattern, ip) in _config.Hosts)
        {
            if (pattern.StartsWith(".") && domain.EndsWith(pattern))
            {
                return ip;
            }
        }
        
        // Check cache
        if (_cache.TryGetValue(domain, out var cached))
        {
            return cached;
        }
        
        // Query DNS
        try
        {
            IDnsQueryResponse response;
            
            if (_config.Ipv6)
            {
                try
                {
                    response = await _client.QueryAsync(domain, QueryType.AAAA);
                }
                catch
                {
                    response = await _client.QueryAsync(domain, QueryType.A);
                }
            }
            else
            {
                response = await _client.QueryAsync(domain, QueryType.A);
            }
            
            var answer = response.Answers.FirstOrDefault();
            if (answer != null)
            {
                string result;
                if (answer is ARecord aRecord)
                {
                    result = aRecord.Address.ToString();
                }
                else if (answer is AaaaRecord aaaaRecord)
                {
                    result = aaaaRecord.Address.ToString();
                }
                else
                {
                    throw new Exception($"Unexpected DNS record type: {answer.GetType()}");
                }
                
                _cache[domain] = result;
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"DNS resolution failed for {domain}");
        }
        
        throw new Exception($"Failed to resolve {domain}");
    }
    
    private void AddNameserver(LookupClientOptions options, string nameserver)
    {
        // Parse URL and configure appropriate DNS protocol
        var uri = new Uri(nameserver.Contains("://") ? nameserver : $"dns://{nameserver}");
        
        switch (uri.Scheme)
        {
            case "dns":
                options.NameServers.Add(new IPEndPoint(
                    IPAddress.Parse(uri.Host),
                    uri.Port > 0 ? uri.Port : 53));
                break;
            case "https":
                // Configure DoH
                options.UseHttps = true;
                // Additional DoH configuration
                break;
            case "tls":
                // Configure DoT
                options.UseTcpOnly = true;
                // Additional DoT configuration
                break;
            case "quic":
                // Configure DoQ (may require additional libraries)
                _logger.LogWarning("DoQ support requires additional implementation");
                break;
        }
    }
}
```

#### 2.6 System Proxy Management

**SystemProxyManager.cs (Windows):**

```csharp
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Accesser.Proxy;

public class SystemProxyManager
{
    private readonly AppConfig _config;
    private readonly ILogger _logger;
    
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    
    private const int INTERNET_OPTION_REFRESH = 37;
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    
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
                _logger.LogError("Cannot open registry key for proxy settings");
                return;
            }
            
            if (pacUrl == null)
            {
                key.DeleteValue("AutoConfigURL", false);
            }
            else
            {
                key.SetValue("AutoConfigURL", pacUrl);
            }
            
            // Notify Windows of changes
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            
            _logger.LogInformation($"System PAC proxy set to: {pacUrl ?? "disabled"}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set system proxy");
        }
    }
}
```

**For Linux:** System proxy management on Linux varies by desktop environment (GNOME, KDE, etc.) and typically requires gsettings or similar tools.

### Phase 3: Additional Features

#### 3.1 Certificate Import

**CertificateImporter.cs:**

```csharp
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Accesser.Certificates;

public class CertificateImporter
{
    public static void ImportToSystem(string certPath, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            ImportToWindowsStore(certPath, logger);
        }
        else if (OperatingSystem.IsLinux())
        {
            ImportToLinuxStore(certPath, logger);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ImportToMacOSKeychain(certPath, logger);
        }
    }
    
    private static void ImportToWindowsStore(string certPath, ILogger logger)
    {
        try
        {
            var cert = new X509Certificate2(certPath);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            store.Close();
            logger.LogInformation("Certificate imported to Windows certificate store");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import certificate to Windows store");
        }
    }
    
    private static void ImportToLinuxStore(string certPath, ILogger logger)
    {
        // Linux certificate import varies by distribution
        // Common locations: /usr/local/share/ca-certificates/
        logger.LogWarning("Linux certificate import requires manual steps");
        logger.LogInformation($"Please copy {certPath} to /usr/local/share/ca-certificates/ and run update-ca-certificates");
    }
    
    private static void ImportToMacOSKeychain(string certPath, ILogger logger)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "security",
                Arguments = $"add-trusted-cert -k ~/Library/Keychains/login.keychain {certPath}",
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            process?.WaitForExit();
            logger.LogInformation("Certificate imported to macOS keychain");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to import certificate to macOS keychain");
        }
    }
}
```

#### 3.2 Update Checker

```csharp
public class UpdateChecker
{
    private const string CURRENT_VERSION = "1.0.0";
    
    public static async Task CheckForUpdatesAsync(ILogger logger)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Accesser-CSharp");
            
            // Check GitHub releases
            var response = await client.GetAsync("https://api.github.com/repos/YOUR_USERNAME/Accesser-CSharp/releases/latest");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                // Parse version from JSON
                // Compare with CURRENT_VERSION
                // Log warning if newer version available
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Update check failed");
        }
    }
}
```

### Phase 4: Testing

#### 4.1 Unit Tests

```csharp
using Xunit;

namespace Accesser.Tests;

public class ConfigurationTests
{
    [Fact]
    public void LoadConfiguration_ValidFile_ReturnsConfig()
    {
        // Test configuration loading
    }
    
    [Fact]
    public void MergeConfiguration_OverlayTakesPrecedence()
    {
        // Test configuration merging
    }
}

public class CertificateTests
{
    [Fact]
    public async Task CreateCertificate_ValidDomain_ReturnsCert()
    {
        // Test certificate generation
    }
}

public class DnsTests
{
    [Fact]
    public async Task Resolve_KnownDomain_ReturnsIp()
    {
        // Test DNS resolution
    }
}
```

### Phase 5: Building and Distribution

#### 5.1 Build Commands

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish single-file executable
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Publish for multiple platforms
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r osx-x64 --self-contained
```

#### 5.2 NuGet Package

To distribute via NuGet:

```bash
dotnet pack -c Release
dotnet nuget push bin/Release/Accesser.1.0.0.nupkg --source https://api.nuget.org/v3/index.json
```

#### 5.3 Docker Support

Create `Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Accesser/Accesser.csproj", "Accesser/"]
RUN dotnet restore "Accesser/Accesser.csproj"
COPY . .
WORKDIR "/src/Accesser"
RUN dotnet build "Accesser.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Accesser.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Accesser.dll"]
```

## Key Considerations

### 1. Async/Await Pattern

C# has excellent async/await support. Use `async Task` consistently throughout:
- All I/O operations should be async
- Use `ConfigureAwait(false)` where appropriate for library code
- Avoid blocking calls like `.Result` or `.Wait()`

### 2. Memory Management

- Use `using` statements or `using` declarations for IDisposable resources
- Consider `ArrayPool<byte>` for buffer allocation in high-throughput scenarios
- Implement proper cancellation token support

### 3. Error Handling

- Use try-catch blocks appropriately
- Log exceptions with context
- Don't swallow exceptions silently
- Consider custom exception types for specific errors

### 4. Performance Optimization

- Use `Span<T>` and `Memory<T>` for zero-allocation scenarios
- Consider connection pooling for remote connections
- Implement LRU cache for certificates
- Use concurrent collections where appropriate

### 5. Security Considerations

- Certificate validation is critical - implement carefully
- Secure credential storage (if needed)
- Input validation for all external inputs
- Follow OWASP guidelines for proxy implementations

### 6. Cross-Platform Compatibility

- Use `System.Runtime.InteropServices.RuntimeInformation` to detect OS
- Conditional compilation for platform-specific code
- Test on Windows, Linux, and macOS
- Handle path separators correctly

### 7. Configuration Management

- TOML parsing with Tomlyn library
- Support for embedded default configurations
- Environment variable overrides
- Command-line argument parsing

## Migration Path from Python

### Direct Equivalents

| Python | C# |
|--------|-----|
| `asyncio.StreamReader` | `NetworkStream` / `SslStream` |
| `ssl.SSLContext` | `SslStream` with `SslServerAuthenticationOptions` |
| `cryptography` | `System.Security.Cryptography` + `BouncyCastle` |
| `dnspython` | `DnsClient` NuGet package |
| `tomllib` | `Tomlyn` NuGet package |
| `logging` | `Microsoft.Extensions.Logging` |
| `argparse` | `System.CommandLine` or manual parsing |

### Python Asyncio to C# Tasks

```python
# Python
async def handle(reader, writer):
    data = await reader.read(1024)
    writer.write(response)
    await writer.drain()
```

```csharp
// C#
async Task HandleAsync(NetworkStream stream)
{
    var buffer = new byte[1024];
    var bytesRead = await stream.ReadAsync(buffer);
    await stream.WriteAsync(response);
    await stream.FlushAsync();
}
```

## Testing Strategy

### Unit Tests
- Configuration loading and merging
- Certificate generation
- DNS resolution
- Pattern matching for hostnames

### Integration Tests
- End-to-end proxy functionality
- Certificate validation
- PAC file serving
- System proxy setting (if testable)

### Manual Testing
- Test with real browsers (Chrome, Firefox, Edge)
- Test with various blocked sites
- Test certificate import process
- Test on different operating systems

## Documentation Requirements

### User Documentation
1. Installation guide
2. Configuration guide
3. Troubleshooting guide
4. FAQ

### Developer Documentation
1. Architecture overview
2. API documentation (XML comments)
3. Contributing guidelines
4. Build and deployment guide

## Timeline Estimate

| Phase | Estimated Time |
|-------|---------------|
| Project Setup | 2-4 hours |
| Configuration Management | 8-12 hours |
| Certificate Management | 16-24 hours |
| Proxy Server Core | 24-32 hours |
| DNS Resolution | 8-12 hours |
| System Integration | 12-16 hours |
| Testing | 16-24 hours |
| Documentation | 8-12 hours |
| **Total** | **~94-136 hours** |

## Resources and References

### Documentation
- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [System.Net.Security](https://docs.microsoft.com/en-us/dotnet/api/system.net.security)
- [BouncyCastle Documentation](https://www.bouncycastle.org/csharp/)
- [DnsClient.NET](https://dnsclient.michaco.net/)

### Sample Projects
- [Titanium Web Proxy](https://github.com/justcoding121/titanium-web-proxy) - Reference implementation
- [HTTP Proxy Server](https://github.com/adams85/aspnetskeleton) - ASP.NET proxy example

### RFCs
- RFC 5246 - TLS 1.2
- RFC 8446 - TLS 1.3
- RFC 6066 - TLS Extensions (SNI)

## Conclusion

This document provides a comprehensive guide for rewriting Accesser from Python to C#. The C# implementation will leverage .NET's strong typing, excellent async support, and cross-platform capabilities to create a robust and performant proxy tool.

The key challenges will be:
1. Proper certificate generation and management
2. Correct TLS/SSL handling with SNI manipulation
3. Cross-platform system integration
4. Maintaining compatibility with the original Python configuration format

Follow the phased approach, test thoroughly, and maintain compatibility with the original configuration files to ensure a smooth transition for existing users.
