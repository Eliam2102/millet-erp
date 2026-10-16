using System.Reflection;

namespace Millet.Api;

public sealed record VersionInfo(string? Commit, string? FechaCompilacion, string Ambiente)
{
    public static VersionInfo Actual(string ambiente)
    {
        var assembly = typeof(VersionInfo).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);
        var commit = metadata.GetValueOrDefault("SourceRevisionId");
        if (string.IsNullOrWhiteSpace(commit))
            commit = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+').ElementAtOrDefault(1);
        return new VersionInfo(commit, metadata.GetValueOrDefault("FechaCompilacion"), ambiente);
    }
}
