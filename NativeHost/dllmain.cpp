#define WIN32_LEAN_AND_MEAN
#define NETHOST_USE_AS_STATIC
#include <windows.h>
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>

// ── Pure Win32 helpers (no STL / CRT) ────────────────────────────────────────

static wchar_t g_base_dir[MAX_PATH];

static void Log(const wchar_t* msg)
{
    OutputDebugStringW(L"[NativeHost] ");
    OutputDebugStringW(msg);
    OutputDebugStringW(L"\n");

    if (g_base_dir[0])
    {
        wchar_t path[MAX_PATH + 32];
        lstrcpyW(path, g_base_dir);
        lstrcatW(path, L"\\NativeHost.log");

        HANDLE hf = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                                OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (hf != INVALID_HANDLE_VALUE)
        {
            SetFilePointer(hf, 0, nullptr, FILE_END);
            DWORD written;
            // Write as UTF-16 LE
            WriteFile(hf, msg, lstrlenW(msg) * sizeof(wchar_t), &written, nullptr);
            WriteFile(hf, L"\r\n", 2 * sizeof(wchar_t), &written, nullptr);
            CloseHandle(hf);
        }
    }
}

static void LogFmt(const wchar_t* fmt, ...)
{
    wchar_t buf[512];
    va_list args;
    va_start(args, fmt);
    wvsprintfW(buf, fmt, args);       // Win32 wvsprintf, no CRT needed
    va_end(args);
    Log(buf);
}

// Build a full path: base_dir + L"\\" + name → out (MAX_PATH)
static void MakePath(wchar_t* out, const wchar_t* name)
{
    lstrcpyW(out, g_base_dir);
    lstrcatW(out, L"\\");
    lstrcatW(out, name);
}

// Build a path inside the Plugins subfolder: base_dir + L"\\Plugins\\" + name → out (MAX_PATH)
static void MakePluginsPath(wchar_t* out, const wchar_t* name)
{
    lstrcpyW(out, g_base_dir);
    lstrcatW(out, L"\\Plugins\\");
    lstrcatW(out, name);
}

// Build a path inside the AOSharp.SDK plugin subfolder: base_dir + L"\\Plugins\\AOSharp.SDK\\" + name → out (MAX_PATH)
static void MakeSDKPath(wchar_t* out, const wchar_t* name)
{
    lstrcpyW(out, g_base_dir);
    lstrcatW(out, L"\\Plugins\\AOSharp.SDK\\");
    lstrcatW(out, name);
}

// ── hostfxr function pointers ────────────────────────────────────────────────
static hostfxr_initialize_for_runtime_config_fn g_hostfxr_init     = nullptr;
static hostfxr_get_runtime_delegate_fn          g_hostfxr_delegate  = nullptr;
static hostfxr_close_fn                         g_hostfxr_close     = nullptr;

static bool LoadHostfxr(const wchar_t* assembly_path)
{
    wchar_t hostfxr_path[MAX_PATH];
    size_t  hostfxr_path_size = MAX_PATH;

    get_hostfxr_parameters params{};
    params.size          = sizeof(params);
    params.assembly_path = assembly_path;

    int rc = get_hostfxr_path(hostfxr_path, &hostfxr_path_size, &params);
    if (rc != 0)
    {
        LogFmt(L"get_hostfxr_path failed: 0x%x", rc);
        return false;
    }

    LogFmt(L"hostfxr: %s", hostfxr_path);
    HMODULE hLib = LoadLibraryW(hostfxr_path);
    if (!hLib)
    {
        LogFmt(L"LoadLibraryW(hostfxr) failed: %u", GetLastError());
        return false;
    }

    g_hostfxr_init     = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
                             GetProcAddress(hLib, "hostfxr_initialize_for_runtime_config"));
    g_hostfxr_delegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
                             GetProcAddress(hLib, "hostfxr_get_runtime_delegate"));
    g_hostfxr_close    = reinterpret_cast<hostfxr_close_fn>(
                             GetProcAddress(hLib, "hostfxr_close"));

    return g_hostfxr_init && g_hostfxr_delegate && g_hostfxr_close;
}

static load_assembly_and_get_function_pointer_fn GetLoadAssemblyFn(const wchar_t* runtimeconfig)
{
    hostfxr_handle cxt = nullptr;
    int rc = g_hostfxr_init(runtimeconfig, nullptr, &cxt);
    if (rc != 0 || !cxt)
    {
        LogFmt(L"hostfxr_initialize_for_runtime_config failed: 0x%x", rc);
        return nullptr;
    }

    void* fn_ptr = nullptr;
    rc = g_hostfxr_delegate(cxt, hdt_load_assembly_and_get_function_pointer, &fn_ptr);
    g_hostfxr_close(cxt);

    if (rc != 0 || !fn_ptr)
    {
        LogFmt(L"hostfxr_get_runtime_delegate failed: 0x%x", rc);
        return nullptr;
    }

    return reinterpret_cast<load_assembly_and_get_function_pointer_fn>(fn_ptr);
}

// ── Host thread ───────────────────────────────────────────────────────────────
static DWORD WINAPI HostThread(LPVOID)
{
    // Resolve our own DLL path to determine the base directory
    wchar_t dll_path[MAX_PATH]{};
    HMODULE hSelf = nullptr;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&HostThread), &hSelf);
    GetModuleFileNameW(hSelf, dll_path, MAX_PATH);

    // Strip filename to get directory
    lstrcpyW(g_base_dir, dll_path);
    for (int i = lstrlenW(g_base_dir) - 1; i >= 0; --i)
    {
        if (g_base_dir[i] == L'\\' || g_base_dir[i] == L'/')
        {
            g_base_dir[i] = L'\0';
            break;
        }
    }

    Log(L"NativeHost starting");

    wchar_t assembly_path[MAX_PATH], runtimeconfig[MAX_PATH];
    MakeSDKPath(assembly_path, L"AOSharp.Bootstrap.dll");
    MakeSDKPath(runtimeconfig, L"AOSharp.Bootstrap.runtimeconfig.json");

    if (GetFileAttributesW(assembly_path) == INVALID_FILE_ATTRIBUTES)
    {
        LogFmt(L"Missing: %s", assembly_path);
        return 1;
    }
    if (GetFileAttributesW(runtimeconfig) == INVALID_FILE_ATTRIBUTES)
    {
        LogFmt(L"Missing: %s", runtimeconfig);
        return 1;
    }

    if (!LoadHostfxr(assembly_path))
    {
        Log(L"LoadHostfxr failed");
        return 1;
    }

    auto load_assembly = GetLoadAssemblyFn(runtimeconfig);
    if (!load_assembly)
    {
        Log(L"GetLoadAssemblyFn failed");
        return 1;
    }

    // Call AOSharp.Bootstrap.Main.Initialize() — marked [UnmanagedCallersOnly]
    typedef void (*initialize_fn)();
    initialize_fn initialize = nullptr;

    int rc = load_assembly(
        assembly_path,
        L"AOSharp.Bootstrap.Main, AOSharp.Bootstrap",
        L"Initialize",
        UNMANAGEDCALLERSONLY_METHOD,
        nullptr,
        reinterpret_cast<void**>(&initialize));

    if (rc != 0 || !initialize)
    {
        LogFmt(L"load_assembly_and_get_function_pointer failed: 0x%x", rc);
        return 1;
    }

    Log(L"Calling Initialize()");
    initialize();

    return 0;
}

// ── DllMain ───────────────────────────────────────────────────────────────────
BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        HANDLE h = CreateThread(nullptr, 0, HostThread, nullptr, 0, nullptr);
        if (h) CloseHandle(h);
    }
    return TRUE;
}
