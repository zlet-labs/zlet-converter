using System.Reflection;
namespace Zlet.FolderConverter.Headless;
internal static class HeadlessProductIdentity
{
    private static readonly Assembly Assembly = typeof(HeadlessProductIdentity).Assembly;
    public static string Name => Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Zlet Converter";
    public static string Version => (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
