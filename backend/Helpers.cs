using System.Reflection;
using System.Runtime.Serialization;

namespace immensive;

public static class EnumExtensions
{
    public static string ToEnumString(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        if (field == null) return value.ToString();
        
        var attribute = field.GetCustomAttribute<EnumMemberAttribute>();
        return attribute?.Value ?? value.ToString();
    }
}

public class ErrorResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public required string Error { get; set; }
}

public class OkResponse(bool ok = true)
{
    [System.Text.Json.Serialization.JsonPropertyName("ok")]
    public bool Ok { get; set; } = ok;
}

public static class VersionHelper
{
    public static string GetVersion()
    {
        // Get version from assembly (from <Version> in .csproj)
        string fullVersion = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "N/A";
        
        // Remove Git metadata (everything after '+' if present)
        return fullVersion.Split('+')[0];
    }
}