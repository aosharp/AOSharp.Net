# AOSharp

AOSharp is a plugin framework for **Anarchy Online** that lets you write C# plugins to automate, extend, or enhance your gameplay experience. It handles all the low-level integration with the game so you can focus on building.

> **Note:** AOSharp is primarily built for the **classic game engine**. Some features may not work correctly on the new engine.

---

## What It Does

AOSharp gives you a clean API to interact with the game from C# code. With it you can:

- Respond to in-game events (entering a zone, receiving a message, joining a team, etc.)
- Read and interact with your character, inventory, and surroundings
- Register custom chat commands
- Build in-game UI windows using XML layouts
- Send and receive network packets
- Communicate between multiple plugins

---

## Usage

Use these steps when you already have a built launcher (from a **release package** or from **Build** below).

### What you need

- Windows 10 or later
- [.NET 8](https://dotnet.microsoft.com/download/dotnet/8) installed (runtime is enough to run the launcher; use the same major version the release targets)
- Anarchy Online client

### From a release

Download the latest Windows release archive from the project's releases page, extract it, and run `AOSharp.exe` from the extracted folder. The release notes list prerequisites and anything else you need for that build.

### Run the launcher and inject

1. Start **`AOSharp.exe`** (from the extracted release folder, or from `bin\Release\net8.0-windows\` after a Release build—see **Build**).
2. Add your plugin DLLs and organize them into a profile.
3. Launch Anarchy Online and log in.
4. Install plugins from Official or Community sources.
5. Select your profile and click **Inject**.


---

## Build

Use this when you are compiling the **launcher** from this repository instead of using a prebuilt release.

### What you need

- Windows 10 or later
- **Visual Studio 2022** (or Build Tools) with **MSBuild** and **Desktop development with C++**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- **Node.js** (LTS) and npm

### Commands

From the `Loader` directory:

```bat
build.bat
```

For a Debug configuration:

```bat
build.bat --debug
```

### Output

Artifacts go under `Loader\bin\<Configuration>\net8.0-windows\`, including `AOSharp.exe`, the native host, managed assemblies, and a `ui\` folder produced from the React build.

---

## Writing a Plugin

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) to compile plugins. Create a class library project, reference `AOSharp.Core`, and implement `AOPluginEntry`:

```csharp
using AOSharp.Core;

public class MyPlugin : AOPluginEntry
{
    public override void Run(string pluginDir)
    {
        Chat.RegisterCommand("hello", (args, quiet) =>
        {
            Chat.WriteLine("Hello from MyPlugin!");
        });

        Game.OnUpdate += OnUpdate;
    }

    private void OnUpdate(object sender, float deltaTime)
    {
        // Runs every game tick
    }
}
```

Your plugin's logs and settings are automatically stored at `%LocalAppData%\AOSharp\<PluginName>\`.

See `Example\HelloWorldPlugin\Main.cs` in this repo (sibling of `Loader`) for a small starter plugin. Larger examples also live in the related community repos below.

---

## Related Repos

- [aosharp.bots](https://gitlab.com/never-knows-best/aosharp.bots) — bot plugins maintained by the author
- [aosharp-automation](https://gitlab.com/never-knows-best/aosharp-automation) — automation plugins

---

## Community

Questions, requests, and discussion: [Discord](https://discord.gg/UyVD7C9)
