using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace AOSharp.Bootstrap.Contexts
{
    /// <summary>
    /// Custom AssemblyLoadContext for loading plugins in isolation
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginPath;
        private readonly Dictionary<string, Assembly> _sharedAssemblies;

        public PluginLoadContext(string pluginPath, string name, IEnumerable<Assembly> sharedAssemblies = null) 
            : base(name, isCollectible: true)
        {
            _pluginPath = Path.GetDirectoryName(pluginPath);
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _sharedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

            // Index the explicitly-shared assemblies by simple name so Load() and Resolving can return them directly
            if (sharedAssemblies != null)
            {
                foreach (var asm in sharedAssemblies)
                {
                    var n = asm.GetName().Name;
                    if (n != null)
                        _sharedAssemblies[n] = asm;
                }
            }


            Resolving += (context, name) =>
            {
                try
                {
                    Log.Debug("Resolving: {AssemblyName}", name.Name);
                    // For explicitly-shared assemblies return the pinned instance directly
                    if (name.Name != null && _sharedAssemblies.TryGetValue(name.Name, out var pinned))
                    {
                        Log.Information("Resolving {AssemblyName} from shared (pinned)", name.Name);
                        return pinned;
                    }

                    if (IsSharedAssembly(name))
                    {
                        var existing = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
                        if (existing != null)
                        {
                            Log.Information("Resolving {AssemblyName} from Default.Assemblies (shared)", name.Name);
                            return existing;
                        }
                        // Not pinned and not in Default — try to find it on disk and load into Default
                        // so both contexts share the same instance
                        foreach (var path in GetProbingPaths())
                        {
                            var dllPath = Path.Combine(path, $"{name.Name}.dll");
                            if (File.Exists(dllPath))
                            {
                                Log.Information("Resolving {AssemblyName} into Default from {Path} (shared fallback)", name.Name, dllPath);
                                return Default.LoadFromAssemblyPath(dllPath);
                            }
                        }
                        Log.Warning("[PluginLoadContext] Could not resolve shared assembly {AssemblyName} from Default", name.Name);
                        return null;
                    }

                    foreach (var path in GetProbingPaths())
                    {
                        var dllPath = Path.Combine(path, $"{name.Name}.dll");
                        var exePath = Path.Combine(path, $"{name.Name}.exe");
                        if (File.Exists(dllPath))
                        {
                            Log.Information("Resolving {AssemblyName} from {Path}", name.Name, dllPath);
                            return context.LoadFromAssemblyPath(dllPath);
                        }
                        if (File.Exists(exePath))
                        {
                            Log.Information("Resolving {AssemblyName} from {Path}", name.Name, exePath);
                            return context.LoadFromAssemblyPath(exePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[PluginLoadContext] Resolving {AssemblyName}: {Message}", name?.Name ?? "?", ex.Message);
                }
                return null;
            };
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            try
            {
                // Return pinned shared assembly directly — no search, guaranteed same instance
                if (assemblyName.Name != null && _sharedAssemblies.TryGetValue(assemblyName.Name, out var pinned))
                    return pinned;

                if (IsSharedAssembly(assemblyName))
                {
                    try
                    {
                        var assembly = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                        if (assembly != null)
                            return assembly;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[PluginLoadContext] Load (shared {Name}): {Message}", assemblyName?.Name ?? "?", ex.Message);
                    }
                    return null;
                }

                string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
                if (assemblyPath != null)
                    return LoadFromAssemblyPath(assemblyPath);

                string localPath = Path.Combine(_pluginPath, $"{assemblyName.Name}.dll");
                if (File.Exists(localPath))
                    return LoadFromAssemblyPath(localPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PluginLoadContext] Load {Name}: {Message}", assemblyName?.Name ?? "?", ex.Message);
            }
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            try
            {
                string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                if (libraryPath != null)
                    return LoadUnmanagedDllFromPath(libraryPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PluginLoadContext] LoadUnmanagedDll {Name}: {Message}", unmanagedDllName ?? "?", ex.Message);
            }
            return IntPtr.Zero;
        }

        private bool IsSharedAssembly(AssemblyName assemblyName)
        {
            // Define which assemblies should be shared between contexts
            return assemblyName.Name == "AOSharp.Common" ||
                   assemblyName.Name == "AOSharp.Bootstrap" ||
                   assemblyName.Name.StartsWith("System.") ||
                   assemblyName.Name.StartsWith("Microsoft.") ||
                   assemblyName.Name == "Newtonsoft.Json" ||
                   assemblyName.Name == "Serilog";
        }

        private static IEnumerable<string> GetProbingPaths()
        {
            var paths = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? "") ?? ""
            };

            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!string.IsNullOrEmpty(runtimeDir))
                paths.Add(runtimeDir);

            return paths.Where(p => !string.IsNullOrEmpty(p)).Distinct();
        }
    }
}
