using AOSharp.Bootstrap.Contexts;
using AOSharp.Common.GameData;
using AOSharp.Common.SharedEventArgs;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace AOSharp.Bootstrap
{
    public class CoreDelegates
    {
        public delegate void InitDelegate();
        public InitDelegate Init;
        public delegate void TeardownDelegate();
        public TeardownDelegate Teardown;
        public delegate void DynelSpawnedDelegate(IntPtr pDynel);
        public DynelSpawnedDelegate DynelSpawned;
        public delegate void DataBlockToMessageDelegate(byte[] datablock);
        public DataBlockToMessageDelegate DataBlockToMessage;
        public delegate void ChatRecvDelegate(byte[] packet);
        public ChatRecvDelegate ChatRecv;
        public delegate void SentPacketDelegate(byte[] datablock);
        public SentPacketDelegate SentPacket;
        public delegate void UpdateDelegate(float deltaTime);
        public UpdateDelegate Update;
        public delegate void EarlyUpdateDelegate(float deltaTime);
        public EarlyUpdateDelegate EarlyUpdate;
        public delegate void TeleportStartedDelegate();
        public TeleportStartedDelegate TeleportStarted;
        public delegate void TeleportEndedDelegate();
        public TeleportEndedDelegate TeleportEnded;
        public delegate void TeleportFailedDelegate();
        public TeleportFailedDelegate TeleportFailed;
        public delegate void JoinTeamRequestDelegate(Identity pIdentity, IntPtr pName);
        public JoinTeamRequestDelegate JoinTeamRequest;
        public delegate void ClientPerformedSpecialActionDelegate(Identity pIdentity);
        public ClientPerformedSpecialActionDelegate ClientPerformedSpecialAction;
        public delegate void PlayfieldInitDelegate(uint id);
        public PlayfieldInitDelegate PlayfieldInit;
        public delegate void OptionPanelActivatedDelegate(IntPtr pOptionPanelModule, bool unk);
        public OptionPanelActivatedDelegate OptionPanelActivated;
        public delegate void ViewDeletedDelegate(IntPtr pView);
        public ViewDeletedDelegate ViewDeleted;
        public delegate void WindowDeletedDelegate(IntPtr pWindow);
        public WindowDeletedDelegate WindowDeleted;
        public delegate void AttemptingSpellCastDelegate(AttemptingSpellCastEventArgs args);
        public AttemptingSpellCastDelegate AttemptingSpellCast;
        public delegate void UnknownCommandDelegate(IntPtr pWindow, string command);
        public UnknownCommandDelegate UnknownChatCommand;
        public delegate void HandleGroupMessageDelegate(GroupMessageEventArgs args);
        public HandleGroupMessageDelegate HandleGroupMessage;
        public delegate void ContainerOpenedDelegate(Identity identity);
        public ContainerOpenedDelegate ContainerOpened;
        public delegate void ButtonPressedDelegate(IntPtr pButton);
        public ButtonPressedDelegate ButtonPressed;
        public delegate void CheckBoxToggledDelegate(IntPtr pCheckBox, bool enabled);
        public CheckBoxToggledDelegate CheckBoxToggled;
        public delegate void MultiListViewItemSelectionChangedDelegate(IntPtr pItem, bool selected);
        public MultiListViewItemSelectionChangedDelegate MultiListViewItemSelectionChanged;
        public delegate int GetDynamicIDOverrideDelegate(string name);
        public GetDynamicIDOverrideDelegate GetDynamicIDOverride;
    }

    // Note: No longer inherits from MarshalByRefObject as AssemblyLoadContext doesn't require marshaling
    public class PluginProxy
    {
        private static CoreDelegates _coreDelegates;
        private List<Plugin> _plugins = new List<Plugin>();
        private PluginLoadContext _pluginContext;
        private Assembly _coreAssembly;
        private MethodInfo _joinTeamRequestMethod;
        private MethodInfo _clientPerformedSpecialActionMethod;
        private MethodInfo _attemptingSpellCastMethod;
        private MethodInfo _handleGroupMessageMethod;

        public int GetDynamicIDOverride(string name) => (_coreDelegates?.GetDynamicIDOverride?.Invoke(name)).GetValueOrDefault(0);

        public void UnknownChatCommand(IntPtr pWindow, string command) => _coreDelegates?.UnknownChatCommand?.Invoke(pWindow, command);

        public void DataBlockToMessage(byte[] datablock) => _coreDelegates?.DataBlockToMessage?.Invoke(datablock);

        public void ChatRecv(byte[] packet) => _coreDelegates?.ChatRecv?.Invoke(packet);

        public void SentPacket(byte[] datablock) => _coreDelegates?.SentPacket?.Invoke(datablock);

        public unsafe void JoinTeamRequest(int type, int id, IntPtr pName)
        {
            if (_coreDelegates?.JoinTeamRequest != null)
            {
                _coreDelegates.JoinTeamRequest.Invoke(new Identity((IdentityType)type, id), pName);
            }
            else if (_joinTeamRequestMethod != null)
            {
                // Fallback: Use reflection if delegate creation failed
                var identity = new Identity((IdentityType)type, id);
                _joinTeamRequestMethod.Invoke(null, new object[] { identity, pName });
            }
        }

        public unsafe void ClientPerformedSpecialAction(int type, int id)
        {
            var identity = new Identity((IdentityType)type, id);
            if (_coreDelegates?.ClientPerformedSpecialAction != null)
                _coreDelegates.ClientPerformedSpecialAction.Invoke(identity);
            else if (_clientPerformedSpecialActionMethod != null)
                _clientPerformedSpecialActionMethod.Invoke(null, new object[] { identity });
        }

        public void DynelSpawned(IntPtr pDynel) => _coreDelegates?.DynelSpawned?.Invoke(pDynel);

        public void Update(float deltaTime) => _coreDelegates?.Update?.Invoke(deltaTime);

        public void EarlyUpdate(float deltaTime) => _coreDelegates?.EarlyUpdate?.Invoke(deltaTime);

        public void TeleportStarted() => _coreDelegates?.TeleportStarted?.Invoke();

        public void TeleportEnded() => _coreDelegates?.TeleportEnded?.Invoke();

        public void TeleportFailed() => _coreDelegates?.TeleportFailed?.Invoke();

        public void PlayfieldInit(uint id) => _coreDelegates?.PlayfieldInit?.Invoke(id);

        public void OptionPanelActivated(IntPtr pOptionPanelModule, bool unk) => _coreDelegates?.OptionPanelActivated?.Invoke(pOptionPanelModule, unk);

        public void ViewDeleted(IntPtr pView) => _coreDelegates?.ViewDeleted?.Invoke(pView);

        public void WindowDeleted(IntPtr pWindow) => _coreDelegates?.WindowDeleted?.Invoke(pWindow);

        public void ContainerOpened(int type, int id) => _coreDelegates?.ContainerOpened?.Invoke(new Identity((IdentityType)type, id));

        public void ButtonPressed(IntPtr pButton) => _coreDelegates?.ButtonPressed?.Invoke(pButton);

        public void CheckBoxToggled(IntPtr pCheckBox, bool enabled) => _coreDelegates?.CheckBoxToggled?.Invoke(pCheckBox, enabled);

        public void MultiListViewItemSelectionChanged(IntPtr pItem, bool selected) => _coreDelegates?.MultiListViewItemSelectionChanged?.Invoke(pItem, selected);

        public unsafe bool AttemptingSpellCast(int targetType, int targetId, int spellType, int spellId)
        {
            AttemptingSpellCastEventArgs eventArgs = new AttemptingSpellCastEventArgs(new Identity((IdentityType)targetType, targetId), new Identity((IdentityType)spellType, spellId));
            if (_coreDelegates?.AttemptingSpellCast != null)
            {
                _coreDelegates.AttemptingSpellCast.Invoke(eventArgs);
            }
            else if (_attemptingSpellCastMethod != null)
            {
                _attemptingSpellCastMethod.Invoke(null, new object[] { eventArgs });
            }
            return eventArgs.Blocked;
        }

        public bool HandleGroupMessage(IntPtr pGroupMessage)
        {
            GroupMessageEventArgs eventArgs = new GroupMessageEventArgs(new GroupMessage(pGroupMessage));
            if (_coreDelegates?.HandleGroupMessage != null)
                _coreDelegates.HandleGroupMessage.Invoke(eventArgs);
            else if (_handleGroupMessageMethod != null)
                _handleGroupMessageMethod.Invoke(null, new object[] { eventArgs });
            return eventArgs.Cancel;
        }

        private T TryCreateDelegateWithFallback<T>(Assembly assembly, string className, string methodName, ref MethodInfo fallbackMethod) where T : class
        {
            try
            {
                Type t = assembly.GetType(className);
                if (t == null)
                    return default(T);

                MethodInfo m = t.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
                if (m == null)
                    return default(T);

                // Store the method for fallback
                fallbackMethod = m;

                // Try to create the delegate
                return Delegate.CreateDelegate(typeof(T), m) as T;
            }
            catch (ArgumentException ex)
            {
                Log.Warning($"[Bootstrap] Failed to create delegate for {className}.{methodName}, will use reflection fallback: {ex.Message}");
                return default(T);
            }
        }

        private T CreateDelegate<T>(Assembly assembly, string className, string methodName) where T : class
        {
            Log.Information($"[Bootstrap] CreateDelegate :: {className}.{methodName}");
            Type t = assembly.GetType(className);

            if (t == null)
                return default(T);

            MethodInfo m = t.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);

            if (m == null)
                return default(T);

            try
            {
                return Delegate.CreateDelegate(typeof(T), m) as T;
            }
            catch (ArgumentException ex)
            {
                // Debug: Log detailed type information
                Log.Error($"Failed to create delegate for {className}.{methodName}");
                Log.Error($"Exception: {ex.Message}");
                
                // Get parameter info for debugging
                var methodParams = m.GetParameters();
                var delegateMethod = typeof(T).GetMethod("Invoke");
                var delegateParams = delegateMethod?.GetParameters();
                
                if (methodParams != null && delegateParams != null)
                {
                    for (int i = 0; i < Math.Max(methodParams.Length, delegateParams.Length); i++)
                    {
                        if (i < methodParams.Length && i < delegateParams.Length)
                        {
                            var methodParam = methodParams[i];
                            var delegateParam = delegateParams[i];
                            Log.Error($"Parameter {i}: Method='{methodParam.ParameterType.AssemblyQualifiedName}' vs Delegate='{delegateParam.ParameterType.AssemblyQualifiedName}'");
                            
                            // Check if it's the Identity type causing issues
                            if (methodParam.ParameterType.Name == "Identity" || delegateParam.ParameterType.Name == "Identity")
                            {
                                Log.Error($"Identity type mismatch detected:");
                                Log.Error($"  Method Identity Assembly: {methodParam.ParameterType.Assembly.FullName}");
                                Log.Error($"  Method Identity Location: {methodParam.ParameterType.Assembly.Location}");
                                Log.Error($"  Delegate Identity Assembly: {delegateParam.ParameterType.Assembly.FullName}");
                                Log.Error($"  Delegate Identity Location: {delegateParam.ParameterType.Assembly.Location}");
                            }
                        }
                    }
                }
                
                throw;
            }
        }

        public void LoadCore(string assemblyPath)
        {
            Log.Information("[Bootstrap] LoadCore: path={Path}, cwd={Cwd}", assemblyPath, Directory.GetCurrentDirectory());
            
            // Collect all assemblies already loaded in the Default context and pin them in the plugin
            // context so shared types (Identity, GroupMessageEventArgs, Serilog loggers, etc.) are
            // the same instance in both contexts. CreateDelegate requires exact type identity.
            // Touch known shared assemblies first to ensure they are loaded in Default.
            var sharedAssemblies = new List<Assembly>
            {
                typeof(Identity).Assembly,       // AOSharp.Common
                typeof(Log).Assembly,            // Serilog
            };
            // Also include everything already in Default
            foreach (var a in AssemblyLoadContext.Default.Assemblies)
            {
                if (!sharedAssemblies.Any(s => s.GetName().Name == a.GetName().Name))
                    sharedAssemblies.Add(a);
            }

            // Create a new context for the core and plugin assemblies.
            Log.Debug("[Bootstrap] Creating PluginLoadContext for AOSharpPlugins");
            _pluginContext = new PluginLoadContext(assemblyPath, "AOSharpPlugins", sharedAssemblies);

            // Load main assembly in the plugin context
            Log.Debug("[Bootstrap] Loading core assembly from path");
            _coreAssembly = _pluginContext.LoadFromAssemblyPath(assemblyPath);
            Log.Information("[Bootstrap] Core assembly loaded: {Name}", _coreAssembly.FullName);

            Log.Debug("[Bootstrap] Creating core delegates");
            _coreDelegates = new CoreDelegates()
            {
                Init = CreateDelegate<CoreDelegates.InitDelegate>(_coreAssembly, "AOSharp.Core.Game", "Init"),
                Teardown = CreateDelegate<CoreDelegates.TeardownDelegate>(_coreAssembly, "AOSharp.Core.Game", "Teardown"),
                Update = CreateDelegate<CoreDelegates.UpdateDelegate>(_coreAssembly, "AOSharp.Core.Game", "OnUpdateInternal"),
                EarlyUpdate = CreateDelegate<CoreDelegates.EarlyUpdateDelegate>(_coreAssembly, "AOSharp.Core.Game", "OnEarlyUpdateInternal"),
                DynelSpawned = CreateDelegate<CoreDelegates.DynelSpawnedDelegate>(_coreAssembly, "AOSharp.Core.DynelManager", "OnDynelSpawned"),
                TeleportStarted = CreateDelegate<CoreDelegates.TeleportStartedDelegate>(_coreAssembly, "AOSharp.Core.Game", "OnTeleportStarted"),
                TeleportEnded = CreateDelegate<CoreDelegates.TeleportEndedDelegate>(_coreAssembly, "AOSharp.Core.Game", "OnTeleportEnded"),
                TeleportFailed = CreateDelegate<CoreDelegates.TeleportFailedDelegate>(_coreAssembly, "AOSharp.Core.Game", "OnTeleportFailed"),
                PlayfieldInit = CreateDelegate<CoreDelegates.PlayfieldInitDelegate>(_coreAssembly, "AOSharp.Core.Game", "OnPlayfieldInit"),
                OptionPanelActivated = CreateDelegate<CoreDelegates.OptionPanelActivatedDelegate>(_coreAssembly, "AOSharp.Core.UI.Options.OptionPanel", "OnOptionPanelActivated"),
                ViewDeleted = CreateDelegate<CoreDelegates.ViewDeletedDelegate>(_coreAssembly, "AOSharp.Core.UI.UIController", "OnViewDeleted"),
                WindowDeleted = CreateDelegate<CoreDelegates.WindowDeletedDelegate>(_coreAssembly, "AOSharp.Core.UI.UIController", "OnWindowDeleted"),
                DataBlockToMessage = CreateDelegate<CoreDelegates.DataBlockToMessageDelegate>(_coreAssembly, "AOSharp.Core.Network", "OnInboundMessage"),
                ChatRecv = CreateDelegate<CoreDelegates.ChatRecvDelegate>(_coreAssembly, "AOSharp.Core.Network", "OnChatMessage"),
                SentPacket = CreateDelegate<CoreDelegates.SentPacketDelegate>(_coreAssembly, "AOSharp.Core.Network", "OnOutboundMessage"),
                JoinTeamRequest = TryCreateDelegateWithFallback<CoreDelegates.JoinTeamRequestDelegate>(_coreAssembly, "AOSharp.Core.Team", "OnJoinTeamRequest", ref _joinTeamRequestMethod),
                ClientPerformedSpecialAction = TryCreateDelegateWithFallback<CoreDelegates.ClientPerformedSpecialActionDelegate>(_coreAssembly, "AOSharp.Core.PerkAction", "OnClientPerformedSpecialAction", ref _clientPerformedSpecialActionMethod),
                AttemptingSpellCast = TryCreateDelegateWithFallback<CoreDelegates.AttemptingSpellCastDelegate>(_coreAssembly, "AOSharp.Core.MiscClientEvents", "OnAttemptingSpellCast", ref _attemptingSpellCastMethod),
                UnknownChatCommand = CreateDelegate<CoreDelegates.UnknownCommandDelegate>(_coreAssembly, "AOSharp.Core.UI.Chat", "OnUnknownCommand"),
                HandleGroupMessage = TryCreateDelegateWithFallback<CoreDelegates.HandleGroupMessageDelegate>(_coreAssembly, "AOSharp.Core.UI.Chat", "OnGroupMessage", ref _handleGroupMessageMethod),
                ContainerOpened = CreateDelegate<CoreDelegates.ContainerOpenedDelegate>(_coreAssembly, "AOSharp.Core.Inventory.Inventory", "OnContainerOpened"),
                ButtonPressed = CreateDelegate<CoreDelegates.ButtonPressedDelegate>(_coreAssembly, "AOSharp.Core.UI.UIController", "OnButtonPressed"),
                CheckBoxToggled = CreateDelegate<CoreDelegates.CheckBoxToggledDelegate>(_coreAssembly, "AOSharp.Core.UI.UIController", "OnCheckBoxToggled"),
                MultiListViewItemSelectionChanged = CreateDelegate<CoreDelegates.MultiListViewItemSelectionChangedDelegate>(_coreAssembly, "AOSharp.Core.UI.UIController", "OnMultiListViewItemStateChanged"),
                GetDynamicIDOverride = CreateDelegate<CoreDelegates.GetDynamicIDOverrideDelegate>(_coreAssembly, "AOSharp.Core.UI.UIController", "OnDynamicIDResolve")
            };

            _coreDelegates.Init();
            Log.Information("[Bootstrap] Core init completed");
        }

        public void LoadPlugin(string assemblyPath)
        {
            try
            {
                Log.Debug("[Bootstrap] LoadPlugin: loading from {Path}", assemblyPath);
                // Load plugin assembly in the same context as core
                Assembly assembly = _pluginContext.LoadFromAssemblyPath(assemblyPath);
                Log.Information("[Bootstrap] Plugin assembly loaded: {Name} from {Path}", assembly.GetName().Name, assemblyPath);

                // Find the first AOSharp.Core.IAOPluginEntry
                Type[] exportedTypes = assembly.GetExportedTypes();
                int entryCount = 0;
                foreach (Type type in exportedTypes)
                {
                    if (type.GetInterface("AOSharp.Core.IAOPluginEntry") == null)
                        continue;

                    MethodInfo initMethod = type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance);

                    if (initMethod == null)
                    {
                        Log.Warning("[Bootstrap] Plugin type {Type} has no public Init(string) method, skipping", type.FullName);
                        continue;
                    }

                    MethodInfo teardownMethod = type.GetMethod("Teardown", BindingFlags.Public | BindingFlags.Instance);

                    if (teardownMethod == null)
                    {
                        Log.Warning("[Bootstrap] Plugin type {Type} has no public Teardown() method, skipping", type.FullName);
                        continue;
                    }

                    ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes);

                    if (constructor == null)
                    {
                        Log.Warning("[Bootstrap] Plugin type {Type} has no parameterless constructor, skipping", type.FullName);
                        continue;
                    }

                    object instance = constructor.Invoke(null);

                    if (instance == null)
                        continue;

                    _plugins.Add(new Plugin(instance, initMethod, teardownMethod, Path.GetDirectoryName(assemblyPath)));
                    entryCount++;
                    Log.Information("[Bootstrap] Registered plugin entry: {Type}", type.FullName);
                }
                if (entryCount == 0)
                    Log.Warning("[Bootstrap] No IAOPluginEntry implementation found in {Path}", assemblyPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] LoadPlugin failed for {Path}: {Message}", assemblyPath, ex.Message);
            }
        }

        public void RunPluginInitializations()
        {
            foreach (Plugin plugin in _plugins)
            {
                if (plugin.Initialized)
                    continue;

                Log.Information("[Bootstrap] Initializating plugin: {Name}", plugin.GetType().FullName);
                plugin.Initialize();
            }
        }

        public void Teardown()
        {
            Log.Information("[Bootstrap] Teardown: core and {Count} plugin(s)", _plugins?.Count ?? 0);
            try
            {
                _coreDelegates?.Teardown?.Invoke();
                Log.Debug("[Bootstrap] Teardown: core delegates ok");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] Teardown (core): {Message}", ex.Message);
            }

            if (_plugins != null)
            {
                int i = 0;
                foreach (Plugin plugin in _plugins)
                {
                    try
                    {
                        plugin.Teardown();
                        Log.Debug("[Bootstrap] Teardown: plugin {Index} ok", i);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Bootstrap] Teardown (plugin {Index}): {Message}", i, ex.Message);
                    }
                    i++;
                }
            }
        }

        public void Unload()
        {
            Log.Information("[Bootstrap] Unload: clearing plugin proxy and requesting ALC unload");
            try
            {
                _plugins?.Clear();
                Log.Debug("[Bootstrap] Unload: _plugins cleared");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] Unload (_plugins.Clear): {Message}", ex.Message);
            }
            try
            {
                _coreDelegates = null;
                _coreAssembly = null;
                Log.Debug("[Bootstrap] Unload: refs nulled");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] Unload (null refs): {Message}", ex.Message);
            }
            try
            {
                _pluginContext?.Unload();
                _pluginContext = null;
                Log.Debug("[Bootstrap] Unload: ALC unload requested");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] Unload (ALC): {Message}", ex.Message);
            }
        }
    }

    public class Plugin
    {
        public bool Initialized;

        private object _instance;
        private MethodInfo _initMethod;
        private MethodInfo _teardownMethod;
        private string _assemblyDir;

        public Plugin(object instance, MethodInfo initMethod, MethodInfo teardownMethod, string assemblyDir)
        {
            Initialized = false;
            _instance = instance;
            _initMethod = initMethod;
            _teardownMethod = teardownMethod;
            _assemblyDir = assemblyDir;
        }

        public void Initialize()
        {
            try
            {
                Log.Debug("[Bootstrap] Plugin.Initialize: {Type}, dir={Dir}", _instance.GetType().FullName, _assemblyDir);
                _initMethod.Invoke(_instance, new object[] { _assemblyDir });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] Plugin.Initialize failed for {Type}: {Message}", _instance.GetType().FullName, ex.Message);
            }

            Initialized = true;
        }

        public void Teardown()
        {
            try
            {
                Log.Debug("[Bootstrap] Plugin.Teardown: {Type}", _instance?.GetType()?.FullName ?? "?");
                _teardownMethod.Invoke(_instance, null);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Bootstrap] Plugin.Teardown failed for {Type}: {Message}", _instance?.GetType()?.FullName ?? "?", ex.Message);
            }
        }
    }
}
