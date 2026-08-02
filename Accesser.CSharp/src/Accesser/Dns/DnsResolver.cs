using Accesser.Configuration;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Accesser.Dns;

public class DnsResolver
{
    private readonly AppConfig _config;
    private readonly ILogger _logger;
    private readonly LookupClient _client;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DnsResolver(AppConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;

        var endpoints = new List<IPEndPoint>();
        foreach (var nameserver in config.DNS.Nameserver)
        {
            if (TryParseNameserver(nameserver, out var endpoint))
            {
                endpoints.Add(endpoint);
            }
        }

        _client = endpoints.Count > 0
            ? new LookupClient(endpoints.ToArray())
            : new LookupClient();
    }

    public async Task<string> ResolveAsync(string domain)
    {
        if (_config.Hosts.TryGetValue(domain, out var exact))
        {
            return exact;
        }

        foreach (var (pattern, mapped) in _config.Hosts)
        {
            if (pattern.StartsWith('.') && domain.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return mapped;
            }
        }

        if (_cache.TryGetValue(domain, out var cached))
        {
            return cached;
        }

        IDnsQueryResponse response;
        if (_config.Ipv6)
        {
            response = await _client.QueryAsync(domain, QueryType.AAAA);
            if (!response.Answers.AaaaRecords().Any())
            {
                response = await _client.QueryAsync(domain, QueryType.A);
            }
        }
        else
        {
            response = await _client.QueryAsync(domain, QueryType.A);
        }

        var answer = response.Answers.AaaaRecords().FirstOrDefault()?.Address.ToString()
                     ?? response.Answers.ARecords().FirstOrDefault()?.Address.ToString();

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException($"Failed to resolve domain: {domain}");
        }

        _cache[domain] = answer;
        return answer;
    }

    private bool TryParseNameserver(string value, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, 53);

        var uriString = value.Contains("://", StringComparison.Ordinal) ? value : $"dns://{value}";
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is "https" or "tls" or "quic")
        {
            _logger.LogWarning("DNS protocol {Scheme} in {Nameserver} is configured but will use system DNS in this build.", uri.Scheme, value);
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            endpoint = new IPEndPoint(ip, uri.Port > 0 ? uri.Port : 53);
            return true;
        }

        return false;
    }
}
