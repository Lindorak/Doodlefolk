# Headless numeric-helper proof

A small, package-free .NET 9 console harness that runs on Linux and Windows. It compiles the **actual production** `Ballistics.cs`, `Coverage.cs`, `Util.cs` numeric helpers, and `PropKind.cs`. It does not reference the Windows application, Vortice, WinForms, audio, Steam, or a graphics device.

The Windows application still targets `net9.0-windows`. The only production changes are moving the unchanged colour helpers to `Util.Color.cs` (a partial `M` class) and the unchanged `PropKind` enum to its own file. No simulation or rendering algorithm is replaced.

## Run

From the repository root, with the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0):

```sh
dotnet run --project tests/Doodlefolk.Headless -c Release
```

This runs 30 check groups and exits 0 for success or 1 for failure. This is a console check runner, not an xUnit project: use `dotnet run`, not `dotnet test`.

The separate `Headless helpers` workflow runs it on Linux and Windows. The existing Windows unit, town-simulation, and self-test workflows remain unchanged and are still necessary.

## Replay one numeric experiment

```sh
dotnet run --project tests/Doodlefolk.Headless -c Release -- --replay tests/Doodlefolk.Headless/replays/bounce.json
```

Optionally save the result to a **new** file:

```sh
dotnet run --project tests/Doodlefolk.Headless -c Release -- --replay tests/Doodlefolk.Headless/replays/bounce.json --out bounce-result.json
```

Existing output files are never overwritten. Parent directories must already exist. Input files are limited to 64 KiB; unknown/missing fields, non-finite values, unsupported versions, and out-of-range settings are rejected. `ticks` is 1–72,000, up to ten simulated minutes at 120 Hz. Invalid commands exit 2; malformed input or failed invariants exit 1.

Version 1's JSON fields are:

- `version`: 1
- `seed`: integer used only for the spring/IK target stream
- `ticks`: number of spring/IK steps and maximum flight steps
- `gravity`, `drag`, `radius`, `floorY`, `bounce`, `position`, `velocity`: flat-floor flight inputs, in the same screen-space units as `Ballistics.Fly` (positive Y points down)

Every tick, the replay checks finite results and limb lengths; the flight also checks floor penetration. The ball stops when the production helper transitions to rolling, so `flightTicks` may be less than `ticks`. Springs/IK continue for all requested ticks. The spring uses frequency 12 and damping 0.8; its seeded target changes once every 120 ticks. The IK sweep uses positive 10–50-unit bone lengths, noncoincident target offsets, and a nonzero bend preference.

The JSON result includes the complete input, runtime/architecture, final state, maximum IK length error, and a SHA-256 digest of the numeric trace. Repeating a spec on the **same code, runtime, and architecture** should produce the same result; cross-runtime/cross-architecture bit identity is not promised. Keep the commit and SDK/runtime version alongside any failure. The digest is a replay comparison aid, not a universal golden hash. Failed invariant messages are written to stderr; the original input file is the replay artifact.

## Coverage

- Analytic shot endpoints for four drag profiles, fixed-step agreement, integration order, bounce response, rolling termination, repeated trace determinism, net clearance, impossible shots, and enum/drag mapping
- Scalar spring finiteness/convergence at 120 Hz for nine frequency/damping combinations, and scalar/vector agreement
- 10,000 seeded IK samples checking both bone lengths, finite outputs, and maximum reach; equal-bone coincident-target and bend-direction cases; a few numeric clamp/segment boundaries
- Pair coverage for 3, 4, and 20-factor test fixtures; triple coverage for 6 and 26-factor fixtures; an independent exhaustive combination-count oracle; seed repeatability; missing-coverage detection
- Replay JSON round-trip, deterministic trace/result, seed effects, rejected inputs, and one-tick/72,000-tick limits
- CLI exit codes, missing/oversized files, JSON output, and input/existing-output preservation

The factor fixtures also appear in the existing Windows planner unit tests. These checks validate **planner rows**, not town outcomes, and do not enumerate `App.SimFactors` or run the 31 Windows town scenarios. Springs are only tested within the listed stable parameter range; IK tests do not establish behaviour for zero bend preferences, zero/negative bone lengths, or unequal-bone coincident targets.

## What this deliberately does not establish

This is not a Linux port of the game or the full `--simtest` world simulation. It does not execute `Prop.Step`, the `App.cs` world tick, figure/pet/AI lifecycle, saves, wall-clock branches, window collision/layout, drawing, animation appearance, sound, or Steam integration. `Ballistics.Fly` is the production shot-prediction helper, with a flat floor; it is not all live prop physics. Passing these checks does not replace existing Windows checks.

A future full headless host would need a separate, reviewed extraction of the fixed-step world loop and scenario setup from `App.cs`/`App.SimTest.cs`, plus injectable desktop/environment and output services. Deterministic RNG alone is insufficient: season, birthday, cooldown, and other calendar branches need an injected clock/calendar. Keep the Windows host as an adapter around that shared simulation rather than adding fake Windows/rendering classes to the test project.

## Windows compile check from Linux

With NuGet access and Windows targeting packs available:

```sh
dotnet build Doodlefolk.csproj -c Release -p:EnableWindowsTargeting=true
dotnet build tests/Doodlefolk.Tests -c Release -p:EnableWindowsTargeting=true
```

These are compile checks only. Run `tools/ci.ps1` on Windows for the existing unit/town/self-test runtime coverage.

## SDK support

As of October 3, 2026, Microsoft's release metadata lists SDK 9.0.318 / runtime 9.0.20. [.NET 9 support ends November 10, 2026](https://dotnet.microsoft.com/platform/support/policy). This proof matches the app's current target; a framework upgrade belongs in a separate compatibility change before that deadline.
