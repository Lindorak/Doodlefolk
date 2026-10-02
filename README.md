# StickFight

Stick figures that live on your Windows desktop. They walk along your taskbar, jump between windows,
climb window edges (or throw a grappling hook up them), nap, chat, dance and high-five each other, play
with balls, and fight, with knockouts, flying kicks and figures sent flying across your screen. Each one
has its own personality, its own way of moving, its own likes and dislikes, friendships, and feelings
about **you**, built from how you treat it.

> Early preview (v0.4). Windows 10/11, 64-bit.

![The StickFight Studio](docs/studio-cast.png)

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

1. Grab `StickFight-v0.4.0-win-x64.zip` from the [latest release](https://github.com/Lindorak/StickFight/releases/latest).
2. Unzip it anywhere and run `StickFight.exe`. Nothing to install.
3. Windows may show "Windows protected your PC" because the app isn't code-signed yet:
   click **More info → Run anyway**.

A figure appears and sketches itself onto your screen. Everything else lives in the **tray icon**
(bottom-right, near the clock): **left-click** it for the **StickFight Studio**, **right-click** for a quick
panel (draw a figure, toss in a toy, hide them, quit).

## What you can do

- **Draw figures** in any colour, with a personality preset or a random one, or bring back **your saved
  figures** from the library.
- **Drag** a figure by a limb and let go while moving to **throw** it (into other figures, if you like).
  **Click** to poke. Move the cursor gently over one to **pet** it. Feisty figures who don't like you may
  put up their fists and box your cursor (it gets shoved; you can turn that off).
- **Right-click a figure** to open its page in the Studio:
  - **Personality**: drag the points of its personality chart, or start from a type.
  - **Likes & dislikes**: Sims-style opinions on things to do, places, toys and you, plus a favourite
    and least favourite colour. Figures who share likes hang out, chat about them and become friends.
  - **Friends**: a web of how it feels about everyone, including **you**. Edit any of it.
  - **Moves**: how it walks, runs, stands, climbs, jumps, fights, celebrates and uses its grappling hook.
  - **Mood**: energy, joy, sadness, fear, annoyance, boredom, loneliness and health, live.
  - **Look**: size and fists (bare, boxing gloves, brass knuckles); colour is next to its name.
  - **Call them over**: fans come running; figures who can't stand you turn their back.
- **Balls**: ball, soccer ball / football, basketball, beach ball. Figures kick, dribble, juggle, carry,
  throw, catch and pass them, and bring them to you if they like you. Throw them yourself, too.
- **Colours & fights**: decide how colours get along (**Friends**, **Neutral**, **Rivals** who spar for
  fun, **Enemies** who really fight, or **Ignore**), plus exceptions for any pair; how often fights
  happen, hit strength, health bars, and what happens at zero health (knocked down / knocked out until
  revived / **permanent death**).
- **Settings**: frame rate (match your monitor, any cap, or unlimited), paper or chalkboard look,
  remember everyone between runs.
- Figures hide automatically while a fullscreen app (game, video) is in front.

## How they behave

Each figure has six traits (energy, curiosity, bravery, playfulness, aggression, sociability), needs
that change over time (stamina, boredom, loneliness, annoyance, joy, sadness, fear), its own body
language (a swagger, a sneaky tiptoe, a flailing run...), and tastes. It decides what to do from all of
that: nap when tired, go exploring when bored, look for a kindred spirit when lonely, climb down if it's
scared of heights, dance if its friend loves dancing too.

**It has a relationship with you.** It remembers what you did: picking it up, throwing it around,
poking, petting, playing ball, hurting its friends. Fans come over to say hi and bring you balls.
Figures who can't stand you glare, turn their back, run from your cursor, or try to box it. Feelings fade
slowly; grudge-holders take longer. The Studio shows what each one remembers.

Fights aren't scripted. Every frame each fighter reads distance and its opponent's wind-ups, chooses moves
in its own fighting style (boxer, kicker, brawler, acrobat, turtle), blocks, ducks and dodges, and only
lands a hit if the fist or foot actually connects. Hurt figures flee, friends jump in to help or revive a
knocked-out friend, and winners celebrate.

![A figure's friends, including you](docs/studio-friends.png)
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
| `App.cs` | Frame loop, input, tray, dirty-region rendering, save/restore |
| `App.Studio.cs` | Bridge between the world and the Studio (state out, edits in) |
| `Studio/` | The Studio window (WebView2) and its hand-drawn web UI (`Studio/web`) |
| `Overlay.cs` | Transparent, click-through, topmost window |
| `Renderer.cs` | Direct2D/DirectWrite on a DirectComposition swap chain |
| `Env.cs` | Reads windows/monitors; builds platforms (window tops, taskbar) and climbable walls |
| `Figure*.cs` | The body: movement, procedural animation, climbing, grappling hook, moves, combat, drawing |
| `BodyStyle.cs` | Per-figure body language (walk, run, idle, climb, jump, fight, celebrate, rope styles) |
| `Tastes.cs` | Likes and dislikes |
| `Ragdoll.cs` | Verlet ragdoll physics |
| `Brain*.cs` | Needs, decisions, navigation, social behaviour, tastes, feelings about you, ball play, fighting |
| `Fight.cs` | Colour relationships, gear, and the move list |
| `Prop.cs` | Ball physics and drawing |
| `Settings.cs` | Persisted preferences, cast and library (`%APPDATA%\StickFight\settings.json`) |

### Debug tools

With `--debug`, the app writes `%TEMP%\stickfight_state.json`, logs events to
`%TEMP%\stickfight_events.log`, and runs commands written to `%TEMP%\stickfight_cmd.txt` (one per
line), e.g. `spawn blue hothead`, `ball SoccerBall`, `rel Red Blue Enemies`, `Red fight Blue`,
`Red spar Blue`, `Red juggle`, `Red chat Blue`, `Red flip`, `Red climb`, `place Red 1500 2000`,
`fling Red 1800 -2000`, `Red grapple`, `Red dance Blue`, `taste Red Dancing 1`, `fond Red -0.8`,
`ko Red`, `deathrule Permanent`, `studio figure Red`, `clear`, `exit`.
`tools\burst.ps1` captures a contact sheet of frames around a figure; `tools\winshot.ps1` captures
one of the app's windows; `tools\printwin.ps1` captures the Studio even when it's covered.

## Licence

MIT. See [LICENSE](LICENSE).
