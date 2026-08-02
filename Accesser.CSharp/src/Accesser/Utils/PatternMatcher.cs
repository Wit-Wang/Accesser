using System.Text.RegularExpressions;

namespace Accesser.Utils;

public static class PatternMatcher
{
    public static bool Match(string input, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool MatchHostnameWithWildcard(string hostname, string pattern)
    {
        if (string.Equals(hostname, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var domain = pattern[2..];
            if (hostname.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase))
            {
                var prefix = hostname[..^(domain.Length + 1)];
                return !prefix.Contains('.');
            }
        }

        return false;
    }
}
