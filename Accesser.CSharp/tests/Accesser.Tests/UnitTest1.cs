using Accesser.Configuration;
using Accesser.Utils;

namespace Accesser.Tests;

public class ConfigurationTests
{
    [Fact]
    public void MergeConfiguration_OverlayTakesPrecedence()
    {
        var baseConfig = new AppConfig
        {
            SetProxy = true,
            Server = new ServerConfig { Port = 7654 },
            DNS = new DnsConfig { Nameserver = ["1.1.1.1"] }
        };

        var overlay = new AppConfig
        {
            SetProxy = false,
            Server = new ServerConfig { Port = 8080 },
            DNS = new DnsConfig { Nameserver = ["8.8.8.8"] }
        };

        var merged = ConfigurationManager.MergeConfigurations(baseConfig, overlay);

        Assert.False(merged.SetProxy);
        Assert.Equal(8080, merged.Server.Port);
        Assert.Contains("1.1.1.1", merged.DNS.Nameserver);
        Assert.Contains("8.8.8.8", merged.DNS.Nameserver);
    }
}

public class PatternMatcherTests
{
    [Theory]
    [InlineData("www.github.com", "*.github.com", true)]
    [InlineData("sub.www.github.com", "*.github.com", false)]
    [InlineData("github.com", "github.com", true)]
    public void MatchHostnameWithWildcard_WorksAsExpected(string host, string pattern, bool expected)
    {
        Assert.Equal(expected, PatternMatcher.MatchHostnameWithWildcard(host, pattern));
    }
}
