# AutoCAD-CS

[Русский](README.md) | **[English](README.en.md)**

CS 1.6 in AutoCAD (using its vector graphics engine). Dust 2 map. Version **1.1.0**.

**[Download CS 1.6 v1.1.0 — ready-to-play ZIP for AutoCAD 2020–2027](https://github.com/mihonder/AutoCAD-CS/releases/download/v1.1.0/CS16-v1.1.0-AutoCAD2020-2027.zip)**

## Quick start

1. Download **`CS16-v1.1.0-AutoCAD2020-2027.zip`** using the link above.
2. Extract the archive to any folder. Spaces in the path are allowed.
3. Open a separate, blank drawing in AutoCAD and enter **`_APPLOAD`**.
4. Select **`CS16.lsp`** from the root of the extracted archive, click “Load”, then “Close”.
5. Enter **`CS16`** in the command line and press Enter.
6. The game opens paused. Press **F6** to enable game controls.

## Compatibility

Four modules are provided for the full version of AutoCAD on Windows x64.

| AutoCAD | Module runtime | API |
| --- | --- | --- |
| 2020 | .NET Framework 4.7 | 23.1 |
| 2021–2024 | .NET Framework 4.8 | 24.0 |
| 2025–2026 | .NET 8 | 25.0 |
| 2027 | .NET 10 | 26.0 |

All four modules compile against their respective Autodesk SDKs. Game logic is tested independently of AutoCAD on .NET Framework, .NET 8 and .NET 10.

AutoCAD LT, macOS and Windows x86 are outside the project's current scope.

## Controls

| Keys | Action |
| --- | --- |
| F6 | Capture or release keyboard and mouse input |
| Esc | Go back in a menu or pause |
| Hold Esc | Release input |
| WASD, Shift | Run, walk |
| Ctrl, Space | Crouch, jump |
| Left mouse button, right mouse button | Fire, use scope when available |
| 1 / 2 / 3, mouse wheel | Select a weapon |
| B / И, then number keys | Buy weapons |
| R | Reload |
| G, E | Drop, pick up a weapon |
| H / Р | Bot difficulty |
| M / Ь | Choose T or CT; bots balance the teams |
| L | Change language |
| Tab | Scoreboard |
| F5 | New round |
| After death: tap Space | Switch spectator mode |
| After death: hold Space for 1.2 seconds | Restart the round |

To close the game and restore the drawing view, release input with **F6**, then enter **`DUSTSTOP`**.

## Report a bug

[Open an Issue](https://github.com/mihonder/AutoCAD-CS/issues/new/choose): include your AutoCAD and Windows versions, describe the problem and how to reproduce it.
