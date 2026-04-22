using PeNet;
using Reloaded.Injector;
using Reloaded.Injector.Interop.Structures;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AOSharp.Injection
{
    public class ReloadedInjector
    {
        public static bool Inject(Process targetProcess)
        {
            try
            {
                // Create injector instance
                using var injector = new Injector(targetProcess);
                
                // Get the bootstrap DLL path
                var bootstrapDllPath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "NativeHost.dll");
                
                if (!File.Exists(bootstrapDllPath))
                {
                    Log.Error($"Bootstrap DLL not found at: {bootstrapDllPath}");
                    return false;
                }
                
                // Inject the DLL
                var injectionResult = injector.Inject(bootstrapDllPath);
                
                if (injectionResult == 0)
                {
                    Log.Error("Failed to inject Bootstrap DLL");
                    return false;
                }
                
                // The DLL will initialize itself via ModuleInitializer
                // No need to call Initialize function
                Log.Information("Bootstrap DLL injected successfully");
                //injector.CallFunction("DotNet8InjectorStub.dll", "Start", targetProcess.Id);
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Injection failed: {ex.Message}");
                return false;
            }
        }
    }
}
