# Bounded first visual pass

This is a **cloud preview of one isolated animation fixture**, not a portable Doodlefolk build.
The app remains Windows-only. No Windows renderer, live town, brain, personality-driven idle,
foot planner, carried props, look-at override, turn blending, equipment, or audio runs here.

## Reproduce

Prerequisites: the existing .NET 9 SDK, Python 3, Git (with base commit available), and Pillow
for rasterized PNG/GIF output. No new NuGet dependency is added. From the repository root:

```sh
python tools/visual-preview.py --output /path/to/visual-results
python tools/render-visual-preview.py /path/to/visual-results
```

The first command disables tiered JIT for the allocation check and saves `scratchhead-poses.json`
and `source-manifest.json`. It tests the crop helper and animation equations before exporting.
`dotnet run --project tests/Doodlefolk.VisualPreview -c Release -- /path/output.json` also
regenerates source slices before compilation; set `DOTNET_TieredCompilation=0` for its allocation
check. Override `-p:PythonExecutable=python3` if required by your system.

## What is authentic

`tools/visual-preview.py` extracts the exact ScratchHead case from the original
`4e554f5ecc6e44701001ee56b047fd04ee82ec85` source and the current `Figure.Style.cs`.
It also extracts production body dimensions, neutral pose defaults, pose spring calls,
arm/leg IK calls, and joint assignment. Both versions use linked production `Util.cs`.
The source manifest records source hashes and the complete changed equation blocks.
The pose fixture uses fixed planted feet and the production reach limit with no step bob.
Both versions have the same scale, facing, neutral rig, 1.4 s fidget, 120 Hz timestep,
2.8 s timeline, camera, output framing, and 30 Hz sampling. GIF delays alternate
30/30/40 ms to preserve exactly 2.8 seconds. Both sides restart together at the loop seam.

The Pillow drawing is a **flat software approximation** of the slim stick figure:
production proportions, bone order, red palette color, far-side darkening, outline, and round
caps. No Direct2D anti-aliasing, shaded gradients, lighting, accessories, or world is claimed.
The regression coverage checks neutral hand/tilt target seams, unchanged middle targets,
finite joints, mirrored temporal IK traces at four scales, frame movement, side differences,
and zero warmed-up allocations for 10,000 isolated pose steps with tiered JIT disabled.
That allocation result is not a renderer/FPS benchmark.

## Production changes

- Sweet Dreams alone supplies a live head-position focal point to the thumbnail crop helper.
  Other card crops preserve their old centered behavior. Full-sized images are unchanged.
- ScratchHead reuses the existing one-shot envelope, adds a modest outward transition arc,
  and follows the smoothed hand for a consistent elbow bend. Its duration and held scratch
  rhythm/target are unchanged. No game state or action scheduling is changed.

The card comparison crops the same checked-in full JPG twice using the original center crop
and new helper rectangle. It is a **software crop preview**, not a fresh Windows export.
The real old `-small.png` remains available as a separately labeled original reference.

## Windows follow-through

Run `tools/ci.ps1` on Windows before runtime acceptance. For an isolated native reference:

```powershell
Doodlefolk.exe --animsheet C:\temp\doodlefolk-before ScratchHead
Doodlefolk.exe --cardart C:\temp\doodlefolk-cards-before
```

Run those same commands on the candidate with new empty `after` output folders. The native
`--animsheet` output is a contact sheet, not the cloud GIF. Keep saved user data untouched;
these existing exporters stage their own test world. Native capture may still differ from the
isolated fixture because it runs the actual world and selected figure's personality.
