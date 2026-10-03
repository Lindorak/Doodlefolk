using System.Numerics;
using System.Text.Json;
using Doodlefolk;

CardCropChecks.Run();
var output = args.Length > 0 ? args[0] : throw new ArgumentException("Output JSON path required");
var before = new PoseFixture(true);
var after = new PoseFixture(false);
var frames = new List<object>();
bool distinctFrames = false, distinctSides = false;
Vector2[]? firstAfter = null;
const float dt = 1f / 120;
// Same fixed timing and source-owned pose constants on both sides. Reset between loops in the GIF.
for (int tick = -120; tick < 336; tick++)
{
    float t = tick * dt;
    bool active = t >= 0.4f && t < 1.8f;
    before.Step(dt, t, active); after.Step(dt, t, active);
    if (tick >= 0 && tick % 4 == 0)
    {
        firstAfter ??= (Vector2[])after.Jt.Clone();
        distinctFrames |= after.Jt.Where((p, j) => Vector2.Distance(p, firstAfter[j]) > 0.1f).Any();
        distinctSides |= after.Jt.Where((p, j) => Vector2.Distance(p, before.Jt[j]) > 0.1f).Any();
        frames.Add(new { t, active, before = before.Jt.Select(p => new[] { p.X, p.Y }).ToArray(), after = after.Jt.Select(p => new[] { p.X, p.Y }).ToArray() });
    }
    for (int i = 0; i < before.Jt.Length; i++)
        if (!float.IsFinite(before.Jt[i].X) || !float.IsFinite(before.Jt[i].Y) || !float.IsFinite(after.Jt[i].X) || !float.IsFinite(after.Jt[i].Y)) throw new Exception("Nonfinite joint");
}
// Check the exact extracted target block (not a transcription) at both seams and in the held pose.
static (Vector2 hand, Vector2 elbow, float tilt, Vector2 rest) Target(bool before, float t, float scale = 1, int facing = 1)
    => new PoseFixture(before, scale, facing).Target(t);
float beforeStartJump = Vector2.Distance(Target(true, 0).hand, Target(true, 0).rest);
float beforeEndJump = Vector2.Distance(Target(true, 1.4f).hand, Target(true, 1.4f).rest);
for (int facing = -1; facing <= 1; facing += 2)
foreach (float scale in new[] { 0.5f, 1f, 2f, 4f })
{
    foreach (float t in new[] { 0f, 1.4f })
    {
        var end = Target(false, t, scale, facing);
        if (Vector2.Distance(end.hand, end.rest) > 1e-5 || end.tilt != 0) throw new Exception("Fidget seam is not neutral");
    }
    foreach (float t in new[] { .3f, .7f, 1.1f })
    {
        var b = Target(true, t, scale, facing); var a = Target(false, t, scale, facing);
        if (Vector2.Distance(a.hand, b.hand) > 1e-5 || MathF.Abs(a.tilt-b.tilt) > 1e-6) throw new Exception("Held scratch pose changed");
    }
}
float afterStartJump = Vector2.Distance(Target(false,0).hand, Target(false,0).rest);
float afterEndJump = Vector2.Distance(Target(false,1.4f).hand, Target(false,1.4f).rest);
// The real animated fixture must change, and both sides must differ during easing.
if (!distinctFrames || !distinctSides) throw new Exception("Preview is static or before/after are identical");
// Mirrored/scale replay checks include the new elbow path, not only target equations.
foreach (float scale in new[] { .5f, 1f, 2f, 4f })
foreach (int facing in new[] {-1,1})
{
    var rig = new PoseFixture(false,scale,facing); var mirror = new PoseFixture(false,scale,-facing);
    for (int tick=-120;tick<336;tick++)
    {
        float t=tick*dt; bool active=t>=.4f && t<1.8f;
        rig.Step(dt,t,active); mirror.Step(dt,t,active);
        for(int j=0;j<11;j++)
            if(Vector2.Distance(rig.Jt[j],new(-mirror.Jt[j].X,mirror.Jt[j].Y))>1e-3f) throw new Exception("Mirrored fixture diverged");
    }
}
// Deterministic pose calculation allocation check. Does not measure renderer, GPU, or app FPS.
var measure = new PoseFixture(false);
for (int i=0;i<1000;i++) measure.Step(dt, (i % 336) * dt, (i % 336) >= 48 && (i % 336) < 216);
long allocated = GC.GetAllocatedBytesForCurrentThread();
for (int i=0;i<10000;i++) measure.Step(dt, (i % 336) * dt, (i % 336) >= 48 && (i % 336) < 216);
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
if (allocated != 0) throw new Exception($"Pose fixture allocated {allocated} bytes");
File.WriteAllText(output, JsonSerializer.Serialize(new { label = "Isolated cloud pose preview, software rasterization; Windows renderer not run", fps = 30, seconds = 2.8, dt, source = "Exact source slices extracted by tools/visual-preview.py", frames, metrics = new { beforeStartTargetJump = beforeStartJump, beforeEndTargetJump = beforeEndJump, afterStartTargetJump = afterStartJump, afterEndTargetJump = afterEndJump, allocationsFor10000PoseSteps = allocated } }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS: exact-source scratch-head seam/held-pose checks at 4 scales x 2 facings; finite joints and mirrored temporal replay; {allocated} bytes allocated by 10,000 warmed-up isolated pose steps");
Console.WriteLine($"Before target jumps: {beforeStartJump:0.000} at start, {beforeEndJump:0.000} at end. After: {afterStartJump:0.000} / {afterEndJump:0.000} (world units at scale 1)");
