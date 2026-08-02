namespace Accesser.Configuration;

public class ServerConfig
{
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7654;
    public string? PacHost { get; set; } = "127.0.0.1";
}

public class DnsConfig
{
    public List<string> Nameserver { get; set; } = new();
}

public class LogConfig
{
    public string Loglevel { get; set; } = "INFO";
    public string Logfile { get; set; } = string.Empty;
}

public class AppConfig
{
    public ServerConfig Server { get; set; } = new();
    public DnsConfig DNS { get; set; } = new();
    public Dictionary<string, string> Hosts { get; set; } = new();
    public Dictionary<string, string> AlterHostname { get; set; } = new();
    public Dictionary<string, object> CertVerify { get; set; } = new();
    public Dictionary<string, string> HttpRedirect { get; set; } = new();
    public LogConfig Log { get; set; } = new();
    public bool CheckHostname { get; set; } = true;
    public bool Ipv6 { get; set; } = false;
    public bool SetProxy { get; set; } = true;
    public bool ImportCa { get; set; } = true;
    public string? StateDir { get; set; }
}

public class CommandLineOptions
{
    public bool NotSetProxy { get; set; }
    public bool NotImportCa { get; set; }
    public string? StateDir { get; set; }
}
