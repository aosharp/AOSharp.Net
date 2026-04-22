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

## Getting Started

### Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Visual Studio 2022 (or the `dotnet` CLI)
- Anarchy Online client

### Build

```bat
build.bat
```

### Inject

1. Open the **AOSharp launcher** (`AOSharp.exe`).
2. Add your plugin DLLs and organize them into a profile.
3. Launch Anarchy Online and log in.
4. Select your profile and click **Inject**.

To unload, click **Eject** — plugins are cleanly removed without restarting the game.

---

## Writing a Plugin

Create a new class library project, reference `AOSharp.Core`, and implement `AOPluginEntry`:

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

See `TestPlugin\Main.cs` for a complete example covering commands, UI, inventory, networking, and more.

---

## Related Repos

- [aosharp.bots](https://gitlab.com/never-knows-best/aosharp.bots) — bot plugins maintained by the author
- [aosharp-automation](https://gitlab.com/never-knows-best/aosharp-automation) — automation plugins

> **Note:** The API is still evolving and syntax may change as the project expands.

---

## Community

Questions, requests, and discussion: [Discord](https://discord.gg/UyVD7C9)
