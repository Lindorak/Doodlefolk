using System.Numerics;
using System.Text.Json;

namespace StickFight;

/// <summary>Pets in the Studio, the right-click menu and the quick panel: their needs, mood, weight and training, and
/// the things you can do for (and with) them.</summary>
sealed partial class App
{
    void PetFrame(double now)
    {
        (Pet.CarePace, Pet.Accidents) = _settings.PetCare switch { "relaxed" => (0.5f, false), "realistic" => (1.6f, true), _ => (1f, true) };
        Pet.StaminaOn = World.StaminaOn = _settings.StaminaOn;
        Pet.WeightOn = World.WeightOn = _settings.WeightOn;
        World.PetHelp = _settings.PetHelp;
        if (_sprayTool && now > _sprayIdleUntil) PickUpSpray(false);
    }

    static float R2(float v) => MathF.Round(v, 2);

    object PetJson(Pet p) => new
    {
        id = p.Id, kind = p.Kind.ToString(), species = p.Species(), name = p.Name, hex = Settings.Hex(p.Color), accent = Settings.Hex(p.Accent), size = p.SizeMul,
        owner = p.Owner?.Name, activity = p.Activity, mood = p.Mood, happiness = R2(p.Happiness), age = R2(p.Age), young = p.Young,
        born = p.Born.ToString("d MMM"), weight = R2(p.Weight), weightWord = p.WeightWord, stamina = R2(p.Stamina), bond = R2(p.UserBond),
        leashed = p.Leashed, onCursor = p.OnCursor, want = p.Want.ToString(),
        needs = new { food = R2(1 - p.Hunger), water = R2(1 - p.Thirst), bathroom = R2(1 - MathF.Max(p.Bladder, p.Bowel)), energy = R2(p.Energy), love = R2(1 - p.Attention), fun = R2(1 - p.Boredom), calm = R2(1 - p.Stress), comfort = R2(1 - p.Frustration) },
        habits = p.Habits.Select(h => new { key = h.ToString(), name = Pet.HabitGood(h), v = R2(p.R(h)) }),
        skills = p.SkillList.Select(s => new { key = s.ToString(), name = Pet.SkillName(s), v = R2(p.Sk(s)) }),
        friends = _w.Pets.Where(o => o != p).Select(o => new { name = o.Name, v = R2(p.PetBond(o)) }),
        words = p.Kind == PetKind.Parrot ? p.Vocabulary : null,
        log = p.CareLog.AsEnumerable().Reverse().Take(12).Select(l => new { ago = (int)(DateTime.Now - l.When).TotalSeconds, text = l.Text }),
        sprays = p.Sprays, treats = p.TreatsGiven,
    };

    void PetCareEdit(Pet p, string op, JsonElement m)
    {
        switch (op)
        {
            case "treat": PostAll(new { t = "toast", text = p.GiveTreat(_w) }); break;
            case "sit": PostAll(new { t = "toast", text = p.Command("sit", _w) }); break;
            case "come": PostAll(new { t = "toast", text = p.Command("come", _w) }); break;
            case "leash":
                if (p.Kind == PetKind.Parrot) { PostAll(new { t = "toast", text = "Parrots don't do leashes. Try \"Step up\"." }); break; }
                p.Leashed = !p.Leashed;
                p.Log(p.Leashed ? "Leash on: walk time!" : "Leash off");
                break;
            case "stepup":
                if (p.OnCursor) { p.OnCursor = false; p.Log("Stepped down"); break; }
                if (p.UserBond < 0.15f && _w.Rng.NextDouble() < 0.6) { PostAll(new { t = "toast", text = $"{p.Name} isn't sure about you yet (spend some time together)" }); break; }
                p.OnCursor = true; p.Held = false; p.Flying = false; p.Log("Stepped up onto your hand");
                break;
            case "talk": { var text = Str(m, "v"); p.Hear(text, true, _w); PostAll(new { t = "toast", text = p.Kind == PetKind.Parrot ? $"{p.Name} listens…" : $"{p.Name} tilts their head." }); break; }
            case "spray": PickUpSpray(true); _pop?.Hide(); break;
            case "kit": StarterKit(p); break;
            case "spraynow": World.Log("spray: " + p.Sprayed(_w)); break;   // debug: a squirt without the tool
        }
    }
}
