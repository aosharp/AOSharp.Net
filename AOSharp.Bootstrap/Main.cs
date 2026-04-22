using AOSharp.Bootstrap.IPC;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Imports;
using Reloaded.Hooks;
using Reloaded.Hooks.Definitions;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using AOSharp.Bootstrap.Contexts;
namespace AOSharp.Bootstrap
{
    public class Main
    {
        private static IPCServer _ipcPipe;
        private static ManualResetEvent _connectEvent;
        private static ManualResetEvent _unloadEvent;
        private static ManualResetEvent _disconnectEvent;
        private static PluginProxy _pluginProxy;
        private static ChatSocketListener _chatSocketListener;
        private static bool _exiting = false;
        private static IReloadedHooks _reloadedHooks;
        private static bool _initialized = false;
        /// <summary>Set by IPC thread on disconnect; game thread performs teardown and UnhookAll on next RunEngine tick.</summary>
        private static volatile bool _disconnecting = false;
        private static bool _disconnectTeardownDone = false;

        private static string _lastChatInput;
        private static IntPtr _lastChatInputWindowPtr;

        // Hook instances
        private static IHook<Ws2_32.RecvDelegate> _wsRecvHook;
        private static IHook<DynamicID_t.DGetID> _dynamicIdGetIdHook;
        private static IHook<MultiListViewItem_c.DSelect> _multiListViewItemSelectHook;
        private static IHook<CheckBox_c.DSlotButtonToggled> _checkBoxSlotButtonToggledHook;
        private static IHook<ButtonBase_c.DSetValue> _buttonBaseSetValueHook;
        private static IHook<InventoryGUIModule_c.DContainerOpened> _containerOpenedHook;
        private static IHook<CommandInterpreter_c.DProcessChatInput> _processChatInputHook;
        private static IHook<CommandInterpreter_c.DGetCommand> _getCommandHook;
        private static IHook<ChatGUIModule_t.DHandleGroupAction> _handleGroupMessageHook;
        private static IHook<Connection_t.DSend> _sendHook;
        private static IHook<MessageProtocol.DDataBlockToMessage> _dataBlockToMessageHook;
        private static IHook<WindowController_c.DViewDeleted> _viewDeletedHook;
        private static IHook<WindowController_c.DRemoveWindow> _removeWindowHook;
        private static IHook<TeamViewModule_c.DSlotJoinTeamRequest> _joinTeamRequestHook;
        private static IHook<TeamViewModule_c.DSlotJoinTeamRequestFailed> _joinTeamRequestFailedLowHook;
        private static IHook<TeamViewModule_c.DSlotJoinTeamRequestFailed> _joinTeamRequestFailedHighHook;
        private static IHook<N3EngineClientAnarchy_t.DPerformSpecialAction> _performSpecialActionHook;
        private static IHook<N3EngineClientAnarchy_t.DCastNanoSpell> _castNanoSpellHook;
        private static IHook<OptionPanelModule_c.DModuleActivated> _optionPanelModuleActivatedHook;
        private static IHook<FlowControlModule_t.DTeleportStartedMessage> _teleportStartedHook;
        private static IHook<TeleportTrier_t.DTeleportFailed> _teleportFailedHook;
        private static IHook<N3EngineClientAnarchy_t.DSendInPlayMessage> _sendInPlayMessageHook;
        private static IHook<N3EngineClientAnarchy_t.DPlayfieldInit> _playfieldInitHook;
        private static IHook<N3EngineClientAnarchy_t.DRunEngine> _runEngineHook;
        private static IHook<N3Playfield_t.DAddChildDynel> _addChildDynelHook;

        /// <summary>Exclusive ACL: only these thread IDs run hook logic; others call original and return. Null = no filter (all threads).</summary>
        private static HashSet<int> _exclusiveAclThreadIds;

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        /// <summary>Like EasyHook's SetExclusiveACL: only the given thread IDs will execute hook code; other threads call original only.</summary>
        public static void SetExclusiveACL(int[] threadIds)
        {
            _exclusiveAclThreadIds = threadIds != null && threadIds.Length > 0
                ? new HashSet<int>(threadIds)
                : null;
        }

        /// <summary>Returns true if the current thread is allowed to run hook logic (no ACL, or current thread in exclusive list).</summary>
        private static bool IsCurrentThreadAllowedByAcl()
        {
            if (_exclusiveAclThreadIds == null)
                return true;
            return _exclusiveAclThreadIds.Contains(GetCurrentThreadId());
        }

        private static readonly object _hookCreateLock = new object();

        /// <summary>Resolves export address and creates/activates a hook. Returns the hook or null on failure. Optionally restricts execution to given thread IDs (SetExclusiveACL).</summary>
        private static IHook<T> CreateAndActivateHook<T>(string module, string exportName, T hookCallback, int[] exclusiveThreadIds = null) where T : Delegate
        {
            IntPtr address = GetProcAddress(module, exportName);
            if (address == IntPtr.Zero)
                return null;
            lock (_hookCreateLock)
            {
                try
                {
                    var hook = _reloadedHooks.CreateHook<T>(hookCallback, (long)address);
                    if (exclusiveThreadIds != null && exclusiveThreadIds.Length > 0)
                        SetExclusiveACL(exclusiveThreadIds);
                    hook.Activate();
                    return hook;
                }
                catch (Exception ex)
                {
                    Log.Error($"[Bootstrap] Hook failed ({exportName}): {ex.Message}");
                    return null;
                }
            }
        }

        // Module initializer - runs when the assembly is loaded
        [ModuleInitializer]
        public static void ModuleInit()
        {
            Log.Information("[Bootstrap] ModuleInit");
            try
            {
                // Get the process ID from the current process
                var processId = Process.GetCurrentProcess().Id;
                //Initialize(processId);
            }
            catch (Exception ex)
            {
                // Don't let exceptions crash the process
                try
                {
                    File.WriteAllText("AOSharp.Bootstrap.Error.txt", $"ModuleInit failed: {ex}");
                }
                catch { }
            }
        }

        // Static constructor as a fallback
        static Main()
        {
            Log.Information("[Bootstrap] Main");
            if (!_initialized)
            {
                var processId = Process.GetCurrentProcess().Id;
                //Initialize(processId);
            }
        }

        [UnmanagedCallersOnly]
        public static void Initialize()
        {
            Log.Information("[Bootstrap] Initialize");

            if (_initialized)
                return;

            _initialized = true;

            try
            {
                int processId = Process.GetCurrentProcess().Id;

                Log.Logger = new LoggerConfiguration()
                    .WriteTo.File("AOSharp.Bootstrapper.txt", restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug)
                    .WriteTo.Debug(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug)
                    .CreateLogger();

                Log.Information($"Initializing Bootstrap for process {processId}");

                _connectEvent = new ManualResetEvent(false);
                _unloadEvent = new ManualResetEvent(false);
                _disconnectEvent = new ManualResetEvent(false);
                _chatSocketListener = new ChatSocketListener();
                _reloadedHooks = new ReloadedHooks();

                // Loop: wait for connection, then wait for disconnect; on disconnect tear down and wait for next connection (re-inject = reconnect, no need to reload DLL)
                while (!_exiting)
                {
                    try
                    {
                        Log.Information($"[Bootstrap] Starting IPCServer with name {processId.ToString()}");
                        _ipcPipe = new IPCServer(processId.ToString());
                        _ipcPipe.OnConnected = OnIPCClientConnected;
                        _ipcPipe.OnDisconnected = OnIPCClientDisconnected;
                        _ipcPipe.RegisterCallback((byte)HookOpCode.LoadAssembly, typeof(LoadAssemblyMessage), OnAssembliesChanged);
                        _ipcPipe.Start();
                        Log.Debug("[Bootstrap] IPCServer started, waiting for connection (10s timeout)");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Bootstrap] IPCServer create/start: {Message}", ex.Message);
                        continue;
                    }

                    _connectEvent.Reset();
                    Log.Debug("[Bootstrap] Waiting for client connection (no timeout).");
                    _connectEvent.WaitOne(); // wait until client connects; pipe is already listening via BeginWaitForConnection

                    Log.Debug("[Bootstrap] Client connected, waiting for disconnect.");
                    _disconnectEvent.Reset();
                    _disconnectEvent.WaitOne(); // wait until client disconnects (eject)
                    Log.Debug("[Bootstrap] Disconnect signalled, looping to start new server.");
                    _connectEvent.Reset();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in Initialize: {ex}");
            }
        }

        public static void Run(string channelName)
        {
            Log.Information("[Bootstrap] Run");
            //If the GameController doesn't connect within 10 seconds we will unload the dll.
            if (!_connectEvent.WaitOne(10000))
                return;

            //Wait for the signal to unload the dll.
            _unloadEvent.WaitOne();
        }

        private static void OnIPCClientConnected(IPCServer pipe)
        {
            Log.Information($"[Bootstrap] Got IPC Connection");

            _disconnecting = false;
            _disconnectTeardownDone = false;

            //Notify the main thread we recieved a connection from the GameController.
            _connectEvent.Set();

            SetupHooks();
        }

        private static void OnIPCClientDisconnected(IPCServer pipe)
        {
            try
            {
                Log.Debug("[Bootstrap] OnIPCClientDisconnected: setting _disconnecting");
                // Do NOT teardown or UnhookAll here - we may be on the IPC thread while the game thread
                // is inside the RunEngine hook (e.g. plugin Update). Teardown and UnhookAll run on the
                // game thread on the next RunEngine tick to avoid use-after-free and unloaded ALC crashes.
                _disconnecting = true;
                _ipcPipe?.Close();
                _disconnectEvent?.Set();
                Log.Debug("[Bootstrap] OnIPCClientDisconnected: done");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] OnIPCClientDisconnected: {Message}", ex.Message);
                throw;
            }
        }

        private static void OnAssembliesChanged(object pipe, IPCMessage message)
        {
            try
            {
                LoadAssemblyMessage msg = message as LoadAssemblyMessage;
                int assemblyCount = msg?.Assemblies?.Count() ?? 0;
                Log.Information("[Bootstrap] OnAssembliesChanged: received {Count} assembly path(s)", assemblyCount);

                if (_pluginProxy != null)
                {
                    Log.Information("[Bootstrap] Unloading existing plugin proxy and plugins");
                    // Unload existing plugins
                    _pluginProxy.Teardown();
                    _pluginProxy.Unload();
                    _pluginProxy = null;
                    
                    // Force garbage collection to complete the unload
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Log.Debug("[Bootstrap] Previous plugins unloaded and GC completed");
                }

                if (!msg.Assemblies.Any())
                {
                    Log.Information("[Bootstrap] No assemblies to load, skipping");
                    return;
                }

                // Create new plugin proxy (no AppDomain needed)
                Log.Debug("[Bootstrap] Creating new PluginProxy");
                _pluginProxy = new PluginProxy();

                // Load core assembly
                string coreAssemblyPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "AOSharp.Core.dll");
                Log.Information("[Bootstrap] Loading core assembly: {Path}", coreAssemblyPath);
                _pluginProxy.LoadCore(coreAssemblyPath);

                // Load each plugin
                foreach (string assembly in msg.Assemblies)
                {
                    Log.Information("[Bootstrap] Loading plugin assembly: {Path}", assembly);
                    _pluginProxy.LoadPlugin(assembly);
                }
                Log.Information("[Bootstrap] Plugin loading complete");
            }
            catch (Exception e)
            {
                //TODO: Send IPC message back to loader on error
                Log.Error(e, "[Bootstrap] Plugin load failed: {Message}", e.Message);
            }
        }

        private static unsafe void SetupHooks()
        {
            _addChildDynelHook = CreateAndActivateHook<N3Playfield_t.DAddChildDynel>(
                "N3.dll", "?AddChildDynel@n3Playfield_t@@QAEXPAVn3Dynel_t@@ABVVector3_t@@ABVQuaternion_t@@@Z",
                N3Playfield_t__AddChildDynel_Hook);

            _runEngineHook = CreateAndActivateHook<N3EngineClientAnarchy_t.DRunEngine>(
                "Gamecode.dll", "?RunEngine@n3EngineClientAnarchy_t@@UAEXM@Z",
                N3EngineClientAnarchy_RunEngine_Hook, exclusiveThreadIds: null);

            _sendInPlayMessageHook = CreateAndActivateHook<N3EngineClientAnarchy_t.DSendInPlayMessage>(
                "Gamecode.dll", "?N3Msg_SendInPlayMessage@n3EngineClientAnarchy_t@@QBE_NXZ",
                N3EngineClientAnarchy_SendInPlayMessage_Hook);

            _teleportStartedHook = CreateAndActivateHook<FlowControlModule_t.DTeleportStartedMessage>(
                "GUI.dll", "?TeleportStartedMessage@FlowControlModule_t@@CAXXZ",
                FlowControlModule_t_TeleportStarted_Hook);

            _teleportFailedHook = CreateAndActivateHook<TeleportTrier_t.DTeleportFailed>(
                "Gamecode.dll", "?TeleportFailed@TeleportTrier_t@@QAEXXZ",
                TeleportTrier_t_TeleportFailed_Hook);

            _optionPanelModuleActivatedHook = CreateAndActivateHook<OptionPanelModule_c.DModuleActivated>(
                "GUI.dll", "?ModuleActivated@OptionPanelModule_c@@UAEX_N@Z",
                OptionPanelModule_ModuleActivated_Hook);

            _viewDeletedHook = CreateAndActivateHook<WindowController_c.DViewDeleted>(
                "GUI.dll", "?ViewDeleted@WindowController_c@@QAEXPAVView@@@Z",
                WindowController_ViewDeleted_Hook);

            _removeWindowHook = CreateAndActivateHook<WindowController_c.DRemoveWindow>(
                "GUI.dll", "?RemoveWindow@WindowController_c@@QAEXPAVWindow@@@Z",
                WindowController_RemoveWindow_Hook);

            _dataBlockToMessageHook = CreateAndActivateHook<MessageProtocol.DDataBlockToMessage>(
                "MessageProtocol.dll", "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z",
                DataBlockToMessage_Hook);

            _playfieldInitHook = CreateAndActivateHook<N3EngineClientAnarchy_t.DPlayfieldInit>(
                "Gamecode.dll", "?PlayfieldInit@n3EngineClientAnarchy_t@@UAEXI@Z",
                N3EngineClientAnarchy_PlayfieldInit_Hook);

            _joinTeamRequestHook = CreateAndActivateHook<TeamViewModule_c.DSlotJoinTeamRequest>(
                "GUI.dll", "?SlotJoinTeamRequest@TeamViewModule_c@@AAEXABVIdentity_t@@ABV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@@Z",
                TeamViewModule_SlotJoinTeamRequest_Hook);

            _joinTeamRequestFailedLowHook = CreateAndActivateHook<TeamViewModule_c.DSlotJoinTeamRequestFailed>(
                "GUI.dll", "?SlotJoinTeamRequestFailedTooLow@TeamViewModule_c@@AAEXABVIdentity_t@@@Z",
                TeamViewModule_SlotJoinTeamRequestFailed_Hook);

            _joinTeamRequestFailedHighHook = CreateAndActivateHook<TeamViewModule_c.DSlotJoinTeamRequestFailed>(
                "GUI.dll", "?SlotJoinTeamRequestFailedTooHigh@TeamViewModule_c@@AAEXABVIdentity_t@@@Z",
                TeamViewModule_SlotJoinTeamRequestFailed_Hook);

            _performSpecialActionHook = CreateAndActivateHook<N3EngineClientAnarchy_t.DPerformSpecialAction>(
                "Gamecode.dll", "?N3Msg_PerformSpecialAction@n3EngineClientAnarchy_t@@QAE_NABVIdentity_t@@@Z",
                N3EngineClientAnarchy_PerformSpecialAction_Hook);

            _handleGroupMessageHook = CreateAndActivateHook<ChatGUIModule_t.DHandleGroupAction>(
                "GUI.dll", "?HandleGroupMessage@ChatGUIModule_c@@AAEXPBUGroupMessage_t@Client_c@ppj@@@Z",
                HandleGroupMessage_Hook);

            _castNanoSpellHook = CreateAndActivateHook<N3EngineClientAnarchy_t.DCastNanoSpell>(
                "Gamecode.dll", "?N3Msg_CastNanoSpell@n3EngineClientAnarchy_t@@QAEXABVIdentity_t@@0@Z",
                N3EngineClientAnarchy_CastNanoSpell_Hook);

            _containerOpenedHook = CreateAndActivateHook<InventoryGUIModule_c.DContainerOpened>(
                "GUI.dll", "?SlotContainerOpened@InventoryGUIModule_c@@AAEXABVIdentity_t@@_N1@Z",
                ContainerOpened_Hook);

            _buttonBaseSetValueHook = CreateAndActivateHook<ButtonBase_c.DSetValue>(
                "GUI.dll", "?SetValue@ButtonBase_c@@UAEXABVVariant@@_N@Z",
                ButtonBase_SetValue_Hook);

            _checkBoxSlotButtonToggledHook = CreateAndActivateHook<CheckBox_c.DSlotButtonToggled>(
                "GUI.dll", "?SlotButtonToggled@CheckBox_c@@AAEX_N@Z",
                CheckBox_SlotButtonToggled_Hook);

            _dynamicIdGetIdHook = CreateAndActivateHook<DynamicID_t.DGetID>(
                "AFCM.dll", "?GetID@DynamicID_t@@QAEHPBD_N@Z",
                DynamicID_GetID_Hook);

            _multiListViewItemSelectHook = CreateAndActivateHook<MultiListViewItem_c.DSelect>(
                "GUI.dll", "?Select@MultiListViewItem_c@@QAEX_N0@Z",
                MultiListViewItem_Select_Hook);

            _sendHook = CreateAndActivateHook<Connection_t.DSend>(
                "Connection.dll", "?Send@Connection_t@@QAEHIIPBX@Z",
                Send_Hook);

            _wsRecvHook = CreateAndActivateHook<Ws2_32.RecvDelegate>(
                "ws2_32.dll", "recv",
                WsRecv_Hook);

            if (ProcessChatInputPatcher.Patch(out IntPtr pProcessCommand, out IntPtr pGetCommand))
            {
                CommandInterpreter_c.ProcessChatInput = Marshal.GetDelegateForFunctionPointer<CommandInterpreter_c.DProcessChatInput>(pProcessCommand);
                CommandInterpreter_c.GetCommand = Marshal.GetDelegateForFunctionPointer<CommandInterpreter_c.DGetCommand>(pGetCommand);

                _processChatInputHook = _reloadedHooks.CreateHook<CommandInterpreter_c.DProcessChatInput>(ProcessChatInput_Hook, (long)pProcessCommand);
                _processChatInputHook.Activate();

                _getCommandHook = _reloadedHooks.CreateHook<CommandInterpreter_c.DGetCommand>(GetCommand_Hook, (long)pGetCommand);
                _getCommandHook.Activate();
            }

            Log.Information($"[Bootstrap] SetupHooks :: Complete!");
        }

        private static IntPtr GetProcAddress(string module, string funcName)
        {
            IntPtr hModule = LoadLibrary(module);
            if (hModule == IntPtr.Zero)
                return IntPtr.Zero;
            return GetProcAddress(hModule, funcName);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        private static void UnhookAll()
        {
            try
            {
                _wsRecvHook?.Disable();
                _dynamicIdGetIdHook?.Disable();
                _multiListViewItemSelectHook?.Disable();
                _checkBoxSlotButtonToggledHook?.Disable();
                _buttonBaseSetValueHook?.Disable();
                _containerOpenedHook?.Disable();
                _processChatInputHook?.Disable();
                _getCommandHook?.Disable();
                _handleGroupMessageHook?.Disable();
                _sendHook?.Disable();
                _dataBlockToMessageHook?.Disable();
                _viewDeletedHook?.Disable();
                _removeWindowHook?.Disable();
                _joinTeamRequestHook?.Disable();
                _joinTeamRequestFailedLowHook?.Disable();
                _joinTeamRequestFailedHighHook?.Disable();
                _performSpecialActionHook?.Disable();
                _castNanoSpellHook?.Disable();
                _optionPanelModuleActivatedHook?.Disable();
                _teleportStartedHook?.Disable();
                _teleportFailedHook?.Disable();
                _sendInPlayMessageHook?.Disable();
                _playfieldInitHook?.Disable();
                _runEngineHook?.Disable();
                _addChildDynelHook?.Disable();
                Log.Debug("[Bootstrap] UnhookAll: done");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] UnhookAll: {Message}", ex.Message);
            }
        }

        // Hook implementations

        public static unsafe int WsRecv_Hook(int socket, IntPtr buffer, int len, int flags)
        {
            int bytesRead = _wsRecvHook.OriginalFunction(socket, buffer, len, flags);

            if (bytesRead == -1)
                return bytesRead;

            try
            {
                if (_pluginProxy != null && socket == ChatSocketListener.Socket)
                {
                    byte[] trimmedBuffer = new byte[bytesRead];
                    Marshal.Copy(buffer, trimmedBuffer, 0, bytesRead);
                    List<byte[]> packets = _chatSocketListener.ProcessBuffer(trimmedBuffer);
                    foreach (byte[] packet in packets)
                        _pluginProxy.ChatRecv(packet);
                }
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] WsRecv_Hook: {Message}", ex.Message); }

            return bytesRead;
        }

        public static int DynamicID_GetID_Hook(IntPtr pThis, string name, bool unk)
        {
            int customId = 0;
            try { customId = (_pluginProxy?.GetDynamicIDOverride(name)).GetValueOrDefault(0); }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] DynamicID_GetID_Hook: {Message}", ex.Message); }
            return customId > 0 ? customId : _dynamicIdGetIdHook.OriginalFunction(pThis, name, unk);
        }

        public static void MultiListViewItem_Select_Hook(IntPtr pThis, bool selected, bool unk)
        {
            _multiListViewItemSelectHook.OriginalFunction(pThis, selected, unk);
            try { _pluginProxy?.MultiListViewItemSelectionChanged(pThis, selected); }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] MultiListViewItem_Select_Hook: {Message}", ex.Message); }
        }

        public static void CheckBox_SlotButtonToggled_Hook(IntPtr pThis, bool enabled)
        {
            try { _pluginProxy?.CheckBoxToggled(pThis, enabled); }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] CheckBox_SlotButtonToggled_Hook: {Message}", ex.Message); }
            _checkBoxSlotButtonToggledHook.OriginalFunction(pThis, enabled);
        }

        public static void ButtonBase_SetValue_Hook(IntPtr pThis, IntPtr pVariant, bool unk)
        {
            try
            {
                if (!Variant_c.AsBool(pVariant))
                    _pluginProxy?.ButtonPressed(pThis);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] ButtonBase_SetValue_Hook: {Message}", ex.Message); }
            _buttonBaseSetValueHook.OriginalFunction(pThis, pVariant, unk);
        }

        public static void ContainerOpened_Hook(IntPtr pThis, ref Identity identity, bool unk, bool unk2)
        {
            try { _pluginProxy?.ContainerOpened((int)identity.Type, identity.Instance); }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] ContainerOpened_Hook: {Message}", ex.Message); }
            _containerOpenedHook.OriginalFunction(pThis, ref identity, unk, unk2);
        }

        public static byte ProcessChatInput_Hook(IntPtr pThis, IntPtr pWindow, IntPtr pCmdText)
        {
            StdString tokenized = StdString.Create();
            ChatGUIModule_t.ExpandChatTextArgs(tokenized.Pointer, pCmdText);
            _lastChatInput = tokenized.ToString();
            _lastChatInputWindowPtr = pWindow;

            return _processChatInputHook.OriginalFunction(pThis, pWindow, pCmdText);
        }

        public static IntPtr GetCommand_Hook(IntPtr pThis, IntPtr pCmdText, bool unk)
        {
            IntPtr result = _getCommandHook.OriginalFunction(pThis, pCmdText, unk);
            
            try
            {
                if (result == IntPtr.Zero && unk && _pluginProxy != null)
                    _pluginProxy?.UnknownChatCommand(_lastChatInputWindowPtr, _lastChatInput);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] GetCommand_Hook: {Message}", ex.Message); }
            return result;
        }

        public static void HandleGroupMessage_Hook(IntPtr pThis, IntPtr pGroupMessage)
        {
            bool cancel = false;
            try
            {
                if (_pluginProxy != null)
                    cancel = _pluginProxy.HandleGroupMessage(pGroupMessage);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] HandleGroupMessage_Hook: {Message}", ex.Message); }
            if (!cancel)
                _handleGroupMessageHook.OriginalFunction(pThis, pGroupMessage);
        }

        private static int Send_Hook(IntPtr pConnection, uint unk, int len, byte[] buf)
        {
            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.SentPacket(buf);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] Send_Hook: {Message}", ex.Message); }

            return _sendHook.OriginalFunction(pConnection, unk, len, buf);
        }

        private static IntPtr DataBlockToMessage_Hook(uint size, byte[] dataBlock)
        {
            //Let the client process the packet before we inspect it.
            IntPtr pMsg = _dataBlockToMessageHook.OriginalFunction(size, dataBlock);

            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.DataBlockToMessage(dataBlock);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] DataBlockToMessage_Hook: {Message}", ex.Message); }

            return pMsg;
        }

        private static void WindowController_ViewDeleted_Hook(IntPtr pThis, IntPtr pView)
        {
            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.ViewDeleted(pView);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] ViewDeleted_Hook: {Message}", ex.Message); }

            _viewDeletedHook.OriginalFunction(pThis, pView);
        }

        private static void WindowController_RemoveWindow_Hook(IntPtr pThis, IntPtr pWindow)
        {
            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.WindowDeleted(pWindow);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] RemoveWindow_Hook: {Message}", ex.Message); }

            _removeWindowHook.OriginalFunction(pThis, pWindow);
        }

        private static void TeamViewModule_SlotJoinTeamRequest_Hook(IntPtr pThis, ref Identity identity, IntPtr pName)
        {
            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.JoinTeamRequest((int)identity.Type, identity.Instance, pName);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] JoinTeamRequest_Hook: {Message}", ex.Message); }
        }

        private static void TeamViewModule_SlotJoinTeamRequestFailed_Hook(IntPtr pThis, ref Identity identity)
        {
            try
            {
                IntPtr pEngine = N3Engine_t.GetInstance();
                if (pEngine == IntPtr.Zero)
                    return;
                N3EngineClientAnarchy_t.TeamJoinRequest(pEngine, ref identity, true);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] JoinTeamRequestFailed_Hook: {Message}", ex.Message); }
        }

        private static bool N3EngineClientAnarchy_PerformSpecialAction_Hook(IntPtr pThis, ref Identity identity)
        {
            bool specialActionResult = _performSpecialActionHook.OriginalFunction(pThis, ref identity);

            try
            {
                if (_pluginProxy != null && specialActionResult)
                    _pluginProxy.ClientPerformedSpecialAction((int)identity.Type, identity.Instance);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] PerformSpecialAction_Hook: {Message}", ex.Message); }

            return specialActionResult;
        }

        private static unsafe bool N3EngineClientAnarchy_CastNanoSpell_Hook(IntPtr pThis, ref Identity target, ref Identity spell)
        {
            try
            {
                if (_pluginProxy != null && _pluginProxy.AttemptingSpellCast((int)target.Type, target.Instance, (int)spell.Type, spell.Instance))
                    return false;
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] CastNanoSpell_Hook: {Message}", ex.Message); }

            return _castNanoSpellHook.OriginalFunction(pThis, ref target, ref spell);
        }

        public static void OptionPanelModule_ModuleActivated_Hook(IntPtr pThis, bool unk)
        {
            _optionPanelModuleActivatedHook.OriginalFunction(pThis, unk);

            try
            {
                if (_pluginProxy != null && unk)
                    _pluginProxy.OptionPanelActivated(pThis, unk);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] OptionPanelActivated_Hook: {Message}", ex.Message); }
        }

        public static unsafe void FlowControlModule_t_TeleportStarted_Hook()
        {
            try
            {
                if (_pluginProxy != null && !*FlowControlModule_t.pIsTeleporting)
                    _pluginProxy.TeleportStarted();
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] TeleportStarted_Hook: {Message}", ex.Message); }

            _teleportStartedHook.OriginalFunction();
        }

        public static void TeleportTrier_t_TeleportFailed_Hook(IntPtr pThis)
        {
            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.TeleportFailed();
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] TeleportFailed_Hook: {Message}", ex.Message); }

            _teleportFailedHook.OriginalFunction(pThis);
        }

        public static bool N3EngineClientAnarchy_SendInPlayMessage_Hook(IntPtr pThis)
        {
            bool result = _sendInPlayMessageHook.OriginalFunction(pThis);
            
            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.TeleportEnded();
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] SendInPlayMessage_Hook: {Message}", ex.Message); }

            return result;
        }

        public static void N3EngineClientAnarchy_PlayfieldInit_Hook(IntPtr pThis, uint id)
        {
            _playfieldInitHook.OriginalFunction(pThis, id);

            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.PlayfieldInit(id);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] PlayfieldInit_Hook: {Message}", ex.Message); }
        }

        public static void N3EngineClientAnarchy_RunEngine_Hook(IntPtr pThis, float deltaTime)
        {
            // Eject path: IPC thread set _disconnecting; do all teardown on game thread to avoid
            // use-after-free and unloaded ALC crashes (no plugin code runs while we unload).
            if (_disconnecting || _exiting)
            {
                if (!_disconnectTeardownDone)
                {
                    _disconnectTeardownDone = true;
                    Log.Information("[Bootstrap] RunEngine (eject): starting teardown");
                    if (_pluginProxy != null)
                    {
                        try { _pluginProxy.Teardown(); Log.Debug("[Bootstrap] RunEngine (eject): Teardown ok"); } catch (Exception ex) { Log.Error(ex, "[Bootstrap] RunEngine (eject) Teardown: {Message}", ex.Message); }
                        try { _pluginProxy.Unload(); Log.Debug("[Bootstrap] RunEngine (eject): Unload ok"); } catch (Exception ex) { Log.Error(ex, "[Bootstrap] RunEngine (eject) Unload: {Message}", ex.Message); }
                        _pluginProxy = null;
                        try { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Log.Debug("[Bootstrap] RunEngine (eject): GC ok"); } catch (Exception ex) { Log.Error(ex, "[Bootstrap] RunEngine (eject) GC: {Message}", ex.Message); }
                    }
                    try { UnhookAll(); } catch (Exception ex) { Log.Error(ex, "[Bootstrap] RunEngine (eject) UnhookAll: {Message}", ex.Message); }
                    Log.Information("[Bootstrap] RunEngine (eject): teardown complete");
                }
                _exiting = false;
                try
                {
                    _runEngineHook.OriginalFunction(pThis, deltaTime);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Bootstrap] RunEngine (eject) OriginalFunction: {Message}", ex.Message);
                }
                return;
            }

            try
            {
                if (_pluginProxy != null)
                {
                    _pluginProxy.RunPluginInitializations();
                    _pluginProxy.EarlyUpdate(deltaTime);
                    _runEngineHook.OriginalFunction(pThis, deltaTime);
                    _pluginProxy.Update(deltaTime);
                }
                else
                {
                    _runEngineHook.OriginalFunction(pThis, deltaTime);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Bootstrap] RunEngine hook (normal): {Message}", ex.Message);
            }
        }

        public static void N3Playfield_t__AddChildDynel_Hook(IntPtr pThis, IntPtr pDynel, IntPtr pos, IntPtr rot)
        {
            //Let the client load the dynel before we notify the GameController of it's spawn.
            _addChildDynelHook.OriginalFunction(pThis, pDynel, pos, rot);

            try
            {
                if (_pluginProxy != null)
                    _pluginProxy.DynelSpawned(pDynel);
            }
            catch (Exception ex) { Log.Error(ex, "[Bootstrap] AddChildDynel_Hook: {Message}", ex.Message); }
        }
    }
}
