using System.Text;

namespace FluentCourse.App;

internal static class LaunchArguments
{
    public static string? GetCoursePath(string? launchArguments)
    {
        var args = GetArguments(launchArguments);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--course=", StringComparison.OrdinalIgnoreCase))
                return CleanPath(arg["--course=".Length..]);

            if (string.Equals(arg, "--course", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return CleanPath(args[i + 1]);
        }

        return args.Select(CleanPath).FirstOrDefault(IsLikelyCoursePath);
    }

    public static bool HasSwitch(string? launchArguments, string name)
    {
        var switchName = name.StartsWith("--", StringComparison.Ordinal) ? name : $"--{name}";
        return GetArguments(launchArguments)
            .Any(arg => string.Equals(arg, switchName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetArguments(string? launchArguments)
    {
        var args = new List<string>();
        args.AddRange(Environment.GetCommandLineArgs().Skip(1));

        if (!string.IsNullOrWhiteSpace(launchArguments))
            args.AddRange(Split(launchArguments));

        return args;
    }

    private static IEnumerable<string> Split(string arguments)
    {
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string CleanPath(string path) =>
        Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    private static bool IsLikelyCoursePath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".fcourse", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.OrdinalIgnoreCase);
    }
}
