using System.Numerics;

namespace Doodlefolk;

/// <summary>Builders put up forts and treehouses: a building site goes down, the frame rises, the roof goes on, and
/// the finished thing becomes somebody's home (the builder's, or a friend's who hasn't got one). Friends lend a hand.</summary>
sealed partial class Brain
{
    float _buildCd = 60;
    Item? _site;

    void BuildOptions(World w, OptionList opts)
    {
        // Carry on with (or help at) a site in progress.
        if (w.Items.FirstOrDefault(i => i.Def.Key == "buildsite" && Vector2.Distance(i.Pos, f.Base) < 2500 * S) is { } site)
        {
            opts.Add(1.2f + P.Energy * 0.5f, () => WorkOnSite(site, w), $"Build the {SiteTarget(site)}");
            return;
        }
        if (_buildCd > _t0 || w.Items.Count > 50) return;
        int built = w.Items.Count(i => i.Def.Key is "fort" or "treehouse");
        if (built >= Math.Max(1, w.Figures.Count / 2)) return;
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null || seg.Item != null || seg.X2 - seg.X1 < 200 * S) return;
        opts.Add(0.5f + P.Energy * 0.4f, () =>
        {
            _buildCd = _t0 + rng.Range(400, 900);
            if (w.MakeItem?.Invoke("buildsite") is not { } s) return;
            float x = M.ClampIn(f.Base.X + f.Facing * 70 * S, seg.X1 + 40 * S, seg.X2 - 40 * S);
            s.Pos = new Vector2(x, seg.Y - 2); s.Vel = Vector2.Zero; s.OnGround = false;
            s.PlantKind = seg.Solid && rng.NextDouble() < 0.5 ? "treehouse" : "fort";
            s.PlanterId = f.Id;
            s.Growth = 0;
            Write("build:" + s.PlantKind, V($"Started building a {s.PlantKind}.", $"I'M BUILDING A {s.PlantKind.ToUpperInvariant()}!!!", $"Building a {s.PlantKind}. It'll be good.", $"I'm going to try to build a {s.PlantKind}…", $"A {s.PlantKind} begins, plank by plank."), "★", 600);
            w.News("town", $"{f.Name} breaks ground on a new {s.PlantKind}", 2, f);
            WorkOnSite(s, w);
        }, "Start building something");
    }

    static string SiteTarget(Item site) => site.PlantKind.Length > 0 ? site.PlantKind : "fort";

    /// <summary>Anyone can lend a hand at a site nearby (friends of the builder, mostly).</summary>
    void HelpBuildOptions(World w, OptionList opts)
    {
        if (Job == Job.Builder || Baby || IsElder) return;
        if (w.Items.FirstOrDefault(i => i.Def.Key == "buildsite" && Vector2.Distance(i.Pos, f.Base) < 1200 * S) is not { } site) return;
        var builder = w.Figures.FirstOrDefault(o => o.Id == site.PlanterId);
        if (builder != null && AffinityWith(builder) < 0.2f) return;
        opts.Add(0.3f + P.Sociability * 0.4f, () => WorkOnSite(site, w), $"Help build the {SiteTarget(site)}");
    }

    void WorkOnSite(Item site, World w)
    {
        _site = site;
        float side = rng.NextDouble() < 0.5 ? -1 : 1;
        Navigate(() => w.Items.Contains(site) ? new Vector2(site.Pos.X + side * site.Def.W * site.Sc * 0.35f, site.Pos.Y) : null, 10 * S, false, () => Go(G.Build, rng.Range(20, 35)), WalkPurpose.Other);
    }

    void DoBuild(World w)
    {
        var site = _site;
        if (site == null || !w.Items.Contains(site) || _t > _dur) { _site = null; Go(G.Idle, 1); return; }
        f.DesiredVX = 0;
        FaceTo(site.Pos.X);
        f.SetAction(Act.Tap);
        if ((int)(_t / 0.6f) != (int)((_t - World.Dt) / 0.6f)) World.Play(Sfx.Clank, f.Jt[J.HandN], 0.18f, 1.4f, 0.2);
        // Each builder adds; the more hands, the faster.
        site.Growth += World.Dt / 120f * (0.7f + Sk(SkillKind.Building) * 0.8f);
        Practice(SkillKind.Building, World.Dt * 0.002f, true);
        // Builders are paid for their time (and helpers chip in for free).
        if (Job == Job.Builder) { _earnT += World.Dt; if (_earnT > 15) { _earnT = 0; Coins++; } }
        Stamina = MathF.Max(0, Stamina - World.Dt * 0.004f);
        if (site.Growth < 1) return;
        // Done!
        string kind = SiteTarget(site);
        var pos = site.Pos;
        int planter = site.PlanterId;
        w.RemoveItem(site);
        if (w.MakeItem?.Invoke(kind) is { } built)
        {
            built.Pos = pos; built.Vel = Vector2.Zero; built.OnGround = false;
            var owner = w.Figures.FirstOrDefault(o => o.Id == planter);
            var newOwner = owner != null && owner.Brain.Home(w) == null ? owner : w.Figures.Where(o => o.Brain.Home(w) == null && !o.Brain.Baby).OrderByDescending(o => owner != null ? owner.Brain.AffinityWith(o) : 0).FirstOrDefault();
            newOwner?.Brain.ClaimHome(built, w);
            w.Sticker("built");
            w.News("town", $"A new {kind} is finished{(owner != null ? $", built by {owner.Name}" : "")}", 3);
        }
        f.Emote(V("finished!", "IT'S DONE!!!", "built. obviously.", "w-we did it!", "the work is complete"), 1.8f);
        Cheered(0.4f);
        if (Job == Job.Builder) Coins += 5;
        Write("built:" + kind, V($"Finished building the {kind}!", $"THE {kind.ToUpperInvariant()} IS DONE!!!", $"The {kind}'s done. Solid work.", $"We finished the {kind}. I'm so proud.", $"The {kind} stands."), "★", 0);
        _site = null;
        Go(G.Cheer, 1.5f);
    }
}
