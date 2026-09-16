namespace Zlet.FolderConverter.App;

public sealed record HeadlessBatchCommand(
    string SourcePath,
    string DestinationPath,
    string ReportJsonPath,
    bool Recursive)
{
    public const int ExitSuccess = 0;
    public const int ExitCompletedWithIssues = 2;
    public const int ExitUsage = 64;
    public const int ExitFatal = 70;
    public const int ExitCancelled = 130;

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], "batch", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out HeadlessBatchCommand? command,
        out string error)
    {
        command = null;
        error = string.Empty;

        if (!IsRequested(args))
        {
            error = "The first argument must be 'batch'.";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument: {token}";
                return false;
            }

            string key;
            string value;
            var separator = token.IndexOf('=');
            if (separator >= 0)
            {
                key = token[..separator];
                value = token[(separator + 1)..];
            }
            else
            {
                key = token;
                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"Missing value for {key}.";
                    return false;
                }
                value = args[++index];
            }

            if (key is not ("--source" or "--destination" or "--target" or "--report-json" or "--recursive"))
            {
                error = $"Unknown option: {key}";
                return false;
            }

            if (!values.TryAdd(key, value))
            {
                error = $"Duplicate option: {key}";
                return false;
            }
        }

        if (!Require(values, "--source", out var source, out error)
            || !Require(values, "--destination", out var destination, out error)
            || !Require(values, "--target", out var target, out error)
            || !Require(values, "--report-json", out var report, out error))
        {
            return false;
        }

        if (!string.Equals(target, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only --target markdown is supported in the first headless batch contract.";
            return false;
        }

        var recursive = true;
        if (values.TryGetValue("--recursive", out var recursiveValue)
            && !bool.TryParse(recursiveValue, out recursive))
        {
            error = "--recursive must be true or false.";
            return false;
        }

        command = new HeadlessBatchCommand(source, destination, report, recursive);
        return true;
    }

    private static bool Require(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string value,
        out string error)
    {
        if (values.TryGetValue(key, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate;
            error = string.Empty;
            return true;
        }

        value = string.Empty;
        error = $"Missing required option: {key}";
        return false;
    }
}
