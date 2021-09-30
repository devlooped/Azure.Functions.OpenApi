using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

static class Extensions
{
    public static bool TryGetBuildProperty(this GeneratorExecutionContext context, string key, out string? value)
        => context.AnalyzerConfigOptions.GlobalOptions.TryGetBuildProperty(key, out value);

    public static bool TryGetBuildProperty(this AnalyzerConfigOptionsProvider options, string key, out string? value)
        => options.GlobalOptions.TryGetBuildProperty(key, out value);

    public static bool TryGetBuildProperty(this AnalyzerConfigOptions options, string key, out string? value)
    {
        if (options.TryGetValue("build_property." + key, out var raw) &&
            !string.IsNullOrEmpty(raw))
        {
            value = raw;
            return true;
        }

        value = null;
        return false;
    }
}