// Isolated document importer dependency loading, 2026-10-19. AGPL-3.0-or-later.
using System.Reflection;
using System.Runtime.Loader;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Plugins;

/// <summary>Groups plugin dependencies; this is not a code security sandbox.</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly Assembly SharedContract = typeof(IDocumentImporter).Assembly;
    private static readonly AssemblyName ContractName = SharedContract.GetName();
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginId, string assemblyPath)
        : base($"DocumentImporter:{pluginId}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _pluginDirectory = Path.GetDirectoryName(assemblyPath)!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Returning the host instance is essential: a second contract DLL would
        // create different interface/DTO types even if its name were identical.
        if (string.Equals(assemblyName.Name, ContractName.Name, StringComparison.OrdinalIgnoreCase))
        {
            var requestedVersion = assemblyName.Version;
            var hostVersion = ContractName.Version!;
            if (requestedVersion is not null &&
                (requestedVersion.Major != hostVersion.Major || requestedVersion > hostVersion))
            {
                throw new FileLoadException(
                    $"插件需要接口程序集 {requestedVersion}，当前应用提供 {hostVersion}。请使用匹配的插件版本。");
            }

            return SharedContract;
        }

        // A plugin window must use the exact Avalonia assemblies already loaded by the host.
        // Loading a second Avalonia.Controls copy creates incompatible Window types.
        if (assemblyName.Name is { } name &&
            (name == "Avalonia" || name.StartsWith("Avalonia.", StringComparison.Ordinal)))
        {
            var sharedUi = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (sharedUi is not null) return sharedUi;
            // Let the default context locate optional Avalonia dependencies.
            return null;
        }

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolvedPath is not null)
        {
            return LoadFromAssemblyPath(resolvedPath);
        }

        // A simple private dependency copied beside the plugin can also work
        // without appearing in its deps.json. Never probe another plugin folder.
        if (IsSimpleFileName(assemblyName.Name))
        {
            var localPath = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
            if (File.Exists(localPath))
            {
                return LoadFromAssemblyPath(localPath);
            }
        }

        // The default context supplies runtime/framework assemblies.
        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (resolvedPath is not null)
        {
            return LoadUnmanagedDllFromPath(resolvedPath);
        }

        if (IsSimpleFileName(unmanagedDllName))
        {
            var localPath = Path.Combine(_pluginDirectory, unmanagedDllName);
            if (File.Exists(localPath))
            {
                return LoadUnmanagedDllFromPath(localPath);
            }

            if (OperatingSystem.IsWindows() &&
                !unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                localPath += ".dll";
                if (File.Exists(localPath))
                {
                    return LoadUnmanagedDllFromPath(localPath);
                }
            }
        }

        return 0;
    }

    internal static bool IsSimpleFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value is not "." and not ".." &&
        !value.EndsWith('.') && !value.EndsWith(' ') &&
        !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));
}
