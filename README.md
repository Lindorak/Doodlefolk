# StickFight

Stick figures that live on your Windows desktop. They walk along your taskbar, jump between windows,
climb window edges, nap, chat and high-five each other, play with balls, react to your cursor, and
fight, with knockouts, flying kicks and figures sent flying across your screen. Drag them around,
throw them at each other, or set them against each other by colour.

> Early preview (v0.3). Windows 10/11, 64-bit.

## Why this exists

I grew up on stick-figure animations, especially *Animator vs. Animation*, where a little stick figure
breaks loose on someone's desktop and starts fighting back. I always wanted one of those guys living on
my own screen. So I set out to see whether Claude could help turn that childhood memory into real,
working software: I described what I remembered and what I wanted it to feel like, and Claude
designed and built it with me, from the physics and animation to the personalities and fights, while I
played with every build and kept pushing for more life in it. StickFight is the result: a little world of
stick figures that treats your real desktop as their playground, climbing your windows, playing with
your cursor, and settling their differences in fights.

*Inspired by Alan Becker's* Animator vs. Animation. *This is a fan-made project and isn't affiliated with
or endorsed by him.*

## Download and run

1. Grab `StickFight-v0.3.0-win-x64.zip` from the [latest release](https://github.com/Lindorak/StickFight/releases/latest).
2. Unzip it anywhere and run `StickFight.exe`. Nothing to install.
3. Windows may show "Windows protected your PC" because the app isn't code-signed yet:
   click **More info → Run anyway**.

A figure appears and sketches itself onto your screen. Everything else is in the **tray icon**
(bottom-right, near the clock). To quit: tray icon → **Exit**.

## What you can do

- **Spawn figures** (tray → Spawn figure): random, by colour, from a personality preset, or from
  **your saved figures**.
- **Drag** a figure to pick it up by that limb. Let go while moving to **throw** it (into other
  figures, if you like). **Click** to poke. **Hover** on one and it reacts. Feisty figures that don't
  trust you may put up their fists and punch your cursor (it gets shoved; you can turn that off).
- **Right-click a figure**: see its personality and mood, change colour, **Edit…**, or remove it.
- **Edit a figure**: name, colour (any colour), size, fists (bare, boxing gloves, brass knuckles), and
  personality via presets or six sliders. The editor also shows its live mood and who it likes. Save
  it to your library to spawn it again any time.
- **Balls** (tray → Add ball): ball, soccer ball / football, basketball, beach ball. Figures kick,
  dribble, juggle, carry, throw, catch and pass them. You can drag and throw them too, and right-click
  to change size and bounciness.
- **Colours & fights** (tray): decide how colours get along: **Friends**, **Neutral**, **Rivals**
  (friendly sparring), **Enemies** (real fights) or **Ignore**, for same-colour figures, different
  colours, or specific pairs. Also: how often fights break out, hit strength, health bars, and what
  happens at zero health (knocked down / knocked out until revived / **permanent death**).
- **Frame rate** (tray): match your monitor, cap it (30/60/90/…), or unlimited.
- Figures hide automatically while a fullscreen app (game, video) is in front, and the app remembers
  your figures between runs (toggle in the tray).

## How they behave

Each figure has six traits (energy, curiosity, bravery, playfulness, aggression, sociability) and
needs that change over time: stamina (tired figures sit and sleep), boredom (bored figures explore
and play), loneliness (lonely figures seek company) and annoyance. They remember how you treat them
(throw them around and they stop trusting your cursor) and how much they like each other.

Fights aren't scripted. Every frame each fighter reads distance and its opponent's wind-ups,
chooses moves (jab, cross, uppercut, front kick, roundhouse, sweep, flying kick), blocks, dodges and
hops over sweeps, and only lands a hit if the fist or foot actually connects. Hurt figures flee,
friends jump in to help or revive a knocked-out friend, and winners celebrate.

## Build from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```
dotnet run -c Release
```

Command-line options: `--spawn N` (start with N random figures), `--scale X` (size multiplier),
`--platforms` (show the surfaces they can stand on), `--debug` (see below).

Self-contained release build:

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

### Layout

| File | What |
|---|---|
| `App.cs` | Frame loop, input, tray and context menus, dirty-region rendering, save/restore |
| `Overlay.cs` | Transparent, click-through, topmost window |
| `Renderer.cs` | Direct2D/DirectWrite on a DirectComposition swap chain |
| `Env.cs` | Reads windows/monitors; builds platforms (window tops, taskbar) and climbable walls |
| `Figure*.cs` | The body: movement, procedural animation, climbing, moves, combat, drawing |
| `Ragdoll.cs` | Verlet ragdoll physics |
| `Brain*.cs` | Needs, decisions, navigation, social behaviour, ball play, fighting |
| `Fight.cs` | Colour relationships, gear, and the move list |
| `Prop.cs` | Ball physics and drawing |
| `Editors.cs` | Figure, ball and colours/fights windows |
| `Settings.cs` | Persisted preferences, cast and library (`%APPDATA%\StickFight\settings.json`) |

### Debug tools

With `--debug`, the app writes `%TEMP%\stickfight_state.json`, logs events to
`%TEMP%\stickfight_events.log`, and runs commands written to `%TEMP%\stickfight_cmd.txt` (one per
line), e.g. `spawn blue hothead`, `ball SoccerBall`, `rel Red Blue Enemies`, `Red fight Blue`,
`Red spar Blue`, `Red juggle`, `Red chat Blue`, `Red flip`, `Red climb`, `place Red 1500 2000`,
`fling Red 1800 -2000`, `ko Red`, `deathrule Permanent`, `edit Red`, `clear`, `exit`.
`tools\burst.ps1` captures a contact sheet of frames around a figure; `tools\winshot.ps1` captures
one of the app's windows.

## Licence

MIT. See [LICENSE](LICENSE).
