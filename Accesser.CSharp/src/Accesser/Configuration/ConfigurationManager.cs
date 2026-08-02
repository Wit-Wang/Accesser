using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;

namespace Accesser.Configuration;

public static class ConfigurationManager
{
    public static AppConfig LoadConfiguration(string[] args)
    {
        var options = ParseCommandLine(args);

        EnsureDefaultFile("config.toml", "config.toml");
        EnsureDefaultFile("rules.toml", "rules.toml");
        EnsureRulesDirectory();

        var config = LoadConfigFile("config.toml");
        var rules = LoadRulesConfiguration();

        var mergedConfig = MergeConfigurations(rules, config);
        ApplyCommandLineOptions(mergedConfig, options);

        return mergedConfig;
    }

    public static AppConfig MergeConfigurations(AppConfig baseConfig, AppConfig overlay)
    {
        var baseJson = JsonSerializer.Serialize(baseConfig);
        var overlayJson = JsonSerializer.Serialize(overlay);

        var baseNode = JsonNode.Parse(baseJson)?.AsObject() ?? new JsonObject();
        var overlayNode = JsonNode.Parse(overlayJson)?.AsObject() ?? new JsonObject();

        DeepMerge(baseNode, overlayNode);

        return JsonSerializer.Deserialize<AppConfig>(baseNode.ToJsonString()) ?? new AppConfig();
    }

    private static AppConfig LoadConfigFile(string path)
    {
        var tomlContent = File.ReadAllText(path);
        return Toml.ToModel<AppConfig>(tomlContent);
    }

    private static AppConfig LoadRulesConfiguration()
    {
        var baseRules = LoadConfigFile("rules.toml");

        foreach (var ruleFile in Directory.GetFiles("rules", "*.toml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var customRules = LoadConfigFile(ruleFile);
            baseRules = MergeConfigurations(baseRules, customRules);
        }

        return baseRules;
    }

    private static void EnsureDefaultFile(string fileName, string resourceName)
    {
        if (File.Exists(fileName))
        {
            return;
        }

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Resources", resourceName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Default resource not found: {sourcePath}");
        }

        File.Copy(sourcePath, fileName);
    }

    private static void EnsureRulesDirectory()
    {
        if (!Directory.Exists("rules"))
        {
            Directory.CreateDirectory("rules");
        }
    }

    private static CommandLineOptions ParseCommandLine(string[] args)
    {
        var options = new CommandLineOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--notsetproxy":
                    options.NotSetProxy = true;
                    break;
                case "--notimportca":
                    options.NotImportCa = true;
                    break;
                case "--state-dir" when i + 1 < args.Length:
                    options.StateDir = args[++i];
                    break;
            }
        }

        return options;
    }

    private static void ApplyCommandLineOptions(AppConfig config, CommandLineOptions options)
    {
        if (options.NotSetProxy)
        {
            config.SetProxy = false;
        }

        if (options.NotImportCa)
        {
            config.ImportCa = false;
        }

        if (!string.IsNullOrWhiteSpace(options.StateDir))
        {
            config.StateDir = options.StateDir;
        }
    }

    private static void DeepMerge(JsonObject target, JsonObject source)
    {
        foreach (var prop in source)
        {
            if (prop.Value is JsonObject sourceObj && target[prop.Key] is JsonObject targetObj)
            {
                DeepMerge(targetObj, sourceObj);
                continue;
            }

            if (prop.Value is JsonArray sourceArr && target[prop.Key] is JsonArray targetArr)
            {
                var combined = new JsonArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in targetArr.Concat(sourceArr))
                {
                    var itemString = item?.ToJsonString();
                    if (itemString != null && seen.Add(itemString))
                    {
                        combined.Add(item is null ? null : item.DeepClone());
                    }
                }

                target[prop.Key] = combined;
                continue;
            }

            target[prop.Key] = prop.Value?.DeepClone();
        }
    }
}
