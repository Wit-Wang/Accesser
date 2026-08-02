using Accesser.Certificates;
using Accesser.Configuration;
using Accesser.Dns;
using Accesser.Utils;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Accesser.Proxy;

public class ProxyServer
{
    private readonly AppConfig _config;
    private readonly CertificateManager _certManager;
    private readonly DnsResolver _dnsResolver;
    private readonly ILogger _logger;
    private TcpListener? _listener;

    public ProxyServer(AppConfig config, CertificateManager certManager, DnsResolver dnsResolver, ILogger logger)
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
        _logger.LogInformation("Serving on {Address}:{Port}", address, _config.Server.Port);

        while (true)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var endpoint = (IPEndPoint)client.Client.RemoteEndPoint!;
            var requestLine = await ReadRequestLineAsync(stream);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            _logger.LogDebug("{Address}:{Port} say: {Line}", endpoint.Address, endpoint.Port, requestLine);

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return;
            }

            var command = parts[0];
            var path = parts[1];

            if (string.Equals(command, "CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                await HandleConnectAsync(stream, path, endpoint.Port);
                return;
            }

            if (string.Equals(command, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetAsync(stream, path);
                return;
            }

            await HandleHttpRedirectAsync(stream, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
    }

    private async Task HandleConnectAsync(NetworkStream clientStream, string path, int clientPort)
    {
        var split = path.Split(':', 2);
        if (split.Length != 2 || !int.TryParse(split[1], out var port))
        {
            return;
        }

        var host = split[0];
        var remoteIp = await _dnsResolver.ResolveAsync(host);
        _logger.LogDebug("[{Port,5}] DNS: {Host} -> {RemoteIp}", clientPort, host, remoteIp);

        await WriteAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n");

        var serverCert = await _certManager.GetOrCreateCertificateAsync(host);
        using var clientSsl = new SslStream(clientStream, leaveInnerStreamOpen: true);
        await clientSsl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCert,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        });

        var remoteTargetHost = DetermineServerHostname(host);
        if (string.IsNullOrEmpty(remoteTargetHost))
        {
            remoteTargetHost = host;
        }

        using var remoteClient = new TcpClient();
        await remoteClient.ConnectAsync(IPAddress.Parse(remoteIp), port);
        using var remoteSsl = new SslStream(remoteClient.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await remoteSsl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = remoteTargetHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        });

        if (remoteSsl.RemoteCertificate is X509Certificate2 remoteCert)
        {
            VerifyCertificate(remoteCert, host, clientPort);
        }

        var c2r = ForwardStreamAsync(clientSsl, remoteSsl);
        var r2c = ForwardStreamAsync(remoteSsl, clientSsl);
        await Task.WhenAny(c2r, r2c);
    }

    private async Task HandleGetAsync(NetworkStream stream, string path)
    {
        if (path.StartsWith("/pac/", StringComparison.OrdinalIgnoreCase))
        {
            await SendPacFileAsync(stream);
            return;
        }

        if (path.StartsWith("/CERT/root.", StringComparison.OrdinalIgnoreCase))
        {
            await SendCertificateAsync(stream);
            return;
        }

        if (string.Equals(path, "/shutdown", StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(0);
        }

        await HandleHttpRedirectAsync(stream, path);
    }

    private async Task SendPacFileAsync(NetworkStream stream)
    {
        var candidate = Path.Combine(Directory.GetCurrentDirectory(), "pac");
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "Resources", "pac");
        var content = await File.ReadAllTextAsync(File.Exists(candidate) ? candidate : defaultPath);
        content = content.Replace("{{port}}", _config.Server.Port.ToString(), StringComparison.Ordinal)
            .Replace("{{host}}", _config.Server.PacHost ?? "127.0.0.1", StringComparison.Ordinal);

        var body = Encoding.UTF8.GetBytes(content);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\nContent-Length: {body.Length}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private async Task SendCertificateAsync(NetworkStream stream)
    {
        var body = await File.ReadAllBytesAsync(_certManager.RootCertificatePath);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-x509-ca-cert\r\nContent-Length: {body.Length}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private async Task HandleHttpRedirectAsync(NetworkStream stream, string path)
    {
        var normalized = path.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);

        foreach (var (prefix, target) in _config.HttpRedirect)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = target + normalized[prefix.Length..];
                break;
            }
        }

        var response = $"HTTP/1.1 301 Moved Permanently\r\nLocation: https://{normalized}\r\n\r\n";
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
        foreach (var (pattern, name) in _config.AlterHostname)
        {
            if (PatternMatcher.Match(host, pattern))
            {
                return name;
            }
        }

        return string.Empty;
    }

    private void VerifyCertificate(X509Certificate2 cert, string host, int port)
    {
        var shouldVerify = _config.CheckHostname;
        var allowedHosts = new List<string> { host };

        foreach (var key in _config.CertVerify.Keys)
        {
            if (!PatternMatcher.Match(host, key))
            {
                continue;
            }

            if (_config.CertVerify[key] is bool value)
            {
                shouldVerify = value;
                break;
            }

            if (_config.CertVerify[key] is IEnumerable<object> objects)
            {
                allowedHosts = objects.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
                shouldVerify = true;
                break;
            }

            if (_config.CertVerify[key] is IEnumerable<string> strings)
            {
                allowedHosts = strings.ToList();
                shouldVerify = true;
                break;
            }
        }

        if (!shouldVerify)
        {
            return;
        }

        if (!allowedHosts.Any(allowedHost => CertificateMatchesHostname(cert, allowedHost)))
        {
            throw new AuthenticationException($"[{port,5}] Certificate doesn't match any of: {string.Join(", ", allowedHosts)}");
        }
    }

    private static bool CertificateMatchesHostname(X509Certificate2 cert, string hostname)
    {
        if (cert.GetNameInfo(X509NameType.DnsName, false) is { Length: > 0 } dnsName &&
            PatternMatcher.MatchHostnameWithWildcard(hostname, dnsName))
        {
            return true;
        }

        var sanExtension = cert.Extensions["2.5.29.17"];
        if (sanExtension is not null)
        {
            var formatted = sanExtension.Format(false);
            var items = formatted.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                var value = item.Trim();
                const string prefix = "DNS Name=";
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = value[prefix.Length..].Trim();
                    if (PatternMatcher.MatchHostnameWithWildcard(hostname, candidate))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream)
    {
        var buffer = new List<byte>(1024);
        var temp = new byte[1];

        while (buffer.Count < 8192)
        {
            var read = await stream.ReadAsync(temp);
            if (read == 0)
            {
                break;
            }

            buffer.Add(temp[0]);
            if (buffer.Count >= 2 && buffer[^2] == '\r' && buffer[^1] == '\n')
            {
                break;
            }
        }

        var line = Encoding.ASCII.GetString(buffer.ToArray());

        if (line.Contains("\r\n", StringComparison.Ordinal))
        {
            // Read and discard headers.
            await ReadUntilHeaderEndAsync(stream);
            return line[..line.IndexOf("\r\n", StringComparison.Ordinal)];
        }

        return string.Empty;
    }

    private static async Task ReadUntilHeaderEndAsync(NetworkStream stream)
    {
        var sequence = new Queue<byte>(4);
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return;
            }

            if (sequence.Count == 4)
            {
                sequence.Dequeue();
            }

            sequence.Enqueue(buffer[0]);
            if (sequence.Count == 4 && sequence.SequenceEqual(new byte[] { 13, 10, 13, 10 }))
            {
                return;
            }
        }
    }

    private static async Task WriteAsync(Stream stream, string content)
    {
        var data = Encoding.ASCII.GetBytes(content);
        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }
}
