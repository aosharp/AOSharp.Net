using AOSharp.Bootstrap.Contexts;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace AOSharp.Bootstrap
{
    /// <summary>
    /// Example plugin manager that demonstrates how to use the new AssemblyLoadContext approach
    /// </summary>
    public class PluginManager : IDisposable
    {
        private readonly Dictionary<string, PluginInfo> _loadedPlugins = new();
        private readonly Type _pluginInterfaceType;

        public PluginManager(Type pluginInterfaceType)
        {
            _pluginInterfaceType = pluginInterfaceType;
        }

        public IEnumerable<string> LoadedPlugins => _loadedPlugins.Keys;

        /// <summary>
        /// Load a plugin from the specified path
        /// </summary>
        public object LoadPlugin(string pluginPath, string pluginName = null)
        {
            pluginName ??= Path.GetFileNameWithoutExtension(pluginPath);
            Log.Information("[PluginManager] LoadPlugin: name={Name}, path={Path}", pluginName, pluginPath);

            // Unload existing plugin with same name
            if (_loadedPlugins.ContainsKey(pluginName))
            {
                Log.Debug("[PluginManager] Unloading existing plugin with name {Name}", pluginName);
                UnloadPlugin(pluginName);
            }

            // Create new load context for this plugin
            var context = new PluginLoadContext(pluginPath, pluginName);
            
            try
            {
                // Load the assembly
                var assembly = context.LoadFromAssemblyPath(pluginPath);
                Log.Debug("[PluginManager] Assembly loaded: {AssemblyName}", assembly.GetName().Name);
                
                // Find plugin implementation
                var pluginType = assembly.GetExportedTypes()
                    .FirstOrDefault(t => _pluginInterfaceType.IsAssignableFrom(t) && !t.IsAbstract);

                if (pluginType == null)
                {
                    Log.Error("[PluginManager] No implementation of {Interface} found in {Path}", _pluginInterfaceType.Name, pluginPath);
                    throw new InvalidOperationException(
                        $"No implementation of {_pluginInterfaceType.Name} found in {pluginPath}");
                }

                // Create instance
                var plugin = Activator.CreateInstance(pluginType);
                Log.Information("[PluginManager] Plugin instance created: {Type}", pluginType.FullName);

                // Store plugin info
                _loadedPlugins[pluginName] = new PluginInfo
                {
                    Context = context,
                    Plugin = plugin,
                    Assembly = assembly,
                    PluginType = pluginType
                };
                Log.Debug("[PluginManager] Plugin registered: {Name}", pluginName);

                return plugin;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PluginManager] LoadPlugin failed for {Name}: {Message}", pluginName, ex.Message);
                // Clean up on failure
                context.Unload();
                throw;
            }
        }

        /// <summary>
        /// Unload a specific plugin
        /// </summary>
        public void UnloadPlugin(string pluginName)
        {
            if (!_loadedPlugins.TryGetValue(pluginName, out var info))
            {
                Log.Debug("[PluginManager] UnloadPlugin: {Name} not loaded", pluginName);
                return;
            }

            Log.Information("[PluginManager] UnloadPlugin: {Name} ({Type})", pluginName, info.PluginType?.FullName ?? "?");

            // Call Teardown if available
            var teardownMethod = info.PluginType.GetMethod("Teardown");
            if (teardownMethod != null)
            {
                try
                {
                    teardownMethod.Invoke(info.Plugin, null);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[PluginManager] Teardown failed for {Name}: {Message}", pluginName, ex.Message);
                }
            }

            // Remove from collection
            _loadedPlugins.Remove(pluginName);

            // Clear references
            info.Plugin = null;
            info.Assembly = null;
            info.PluginType = null;

            // Request unload
            info.Context.Unload();

            // Force collection to complete unload
            ForceGarbageCollection();
            Log.Debug("[PluginManager] Plugin unloaded: {Name}", pluginName);
        }

        /// <summary>
        /// Unload all plugins
        /// </summary>
        public void UnloadAll()
        {
            var pluginNames = _loadedPlugins.Keys.ToList();
            Log.Information("[PluginManager] UnloadAll: {Count} plugin(s)", pluginNames.Count);
            foreach (var name in pluginNames)
            {
                UnloadPlugin(name);
            }
        }

        /// <summary>
        /// Get a loaded plugin by name
        /// </summary>
        public T GetPlugin<T>(string pluginName) where T : class
        {
            return _loadedPlugins.TryGetValue(pluginName, out var info) 
                ? info.Plugin as T 
                : null;
        }

        /// <summary>
        /// Execute an action on all loaded plugins
        /// </summary>
        public void ExecuteOnAll<T>(Action<T> action) where T : class
        {
            foreach (var info in _loadedPlugins.Values)
            {
                if (info.Plugin is T plugin)
                {
                    action(plugin);
                }
            }
        }

        public void Dispose()
        {
            UnloadAll();
        }

        /// <summary>
        /// Force garbage collection to complete assembly unloading
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ForceGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private class PluginInfo
        {
            public PluginLoadContext Context { get; set; }
            public object Plugin { get; set; }
            public Assembly Assembly { get; set; }
            public Type PluginType { get; set; }
        }
    }
}
