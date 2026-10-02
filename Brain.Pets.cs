using System.Numerics;

namespace StickFight;

/// <summary>Figures and pets: petting them (animal lovers go over on their own), being adopted as a pet's favourite,
/// and throwing the ball again when the dog brings it back.</summary>
sealed partial class Brain
{
    Pet? _petting;

    void PetOptions(World w, OptionList opts)
    {
        if (w.Pets.Count == 0 || Stamina < 0.2f) return;
        float like = f.Tastes.Of(Thing.Pets);
        if (like < -0.3f) return;
        var pet = w.Pets.Where(p => !p.Held && p.Grounded && Vector2.Distance(p.Pos, f.Base) < 900 * S).OrderBy(p => Vector2.Distance(p.Pos, f.Base)).FirstOrDefault();
        if (pet == null) return;
        float mine = pet.Owner == f ? 0.6f : 0;
        opts.Add((0.25f + MathF.Max(0, like) * 1.3f + Loneliness * 0.5f + mine) * (pet.Asleep ? 0.4f : 1), () => GoPet(pet, w), $"Pet {pet.Name}");
    }

    void GoPet(Pet pet, World w)
    {
        _petting = pet;
        Navigate(() => w.Pets.Contains(pet) && !pet.Held ? pet.Pos + new Vector2((f.Base.X < pet.Pos.X ? -1 : 1) * (pet.Length * 0.6f + 6 * S), 0) : null,
                 8 * S, false, () => Go(G.PetAnimal, rng.Range(3, 5)), WalkPurpose.Social);
        _navAbout = pet;
    }

    void DoPetAnimal(World w)
    {
        var pet = _petting;
        if (pet == null || !w.Pets.Contains(pet) || pet.Held || !f.Grounded) { Go(G.Idle, 1); return; }
        f.DesiredVX = 0;
        FaceTo(pet.Pos.X);
        f.DuckT = 0.2f;
        // Hand on its head, stroking.
        f.HoldN = pet.Centre + new Vector2(MathF.Sin(_t * 6) * 4 * S, -pet.Height * 0.4f);
        if (_t > 0.4f && _t - World.Dt <= 0.4f)
        {
            pet.PettedBy(f);
            f.Emote(rng.NextDouble() < 0.5 ? "♥" : pet.Kind == PetKind.Cat ? "kitty!" : "good dog!", 1.3f);
            Cheered(0.15f);
            Loneliness = MathF.Max(0, Loneliness - 0.2f);
            Write("pet:" + pet.Name, V($"Petted {pet.Name}. So soft.", $"Petted {pet.Name}!! Best {(pet.Kind == PetKind.Cat ? "cat" : "dog")} ever!", $"Petted {pet.Name}. Fine. It was nice.",
                                       $"{pet.Name} let me pet them. My heart.", $"Petted {pet.Name}. They know things, I'm sure of it."), "♥", 1800);
        }
        if (_t > _dur) { _petting = null; Go(G.Idle, rng.Range(1, 2)); }
    }

    /// <summary>A pet has picked this figure as its favourite.</summary>
    public void OnAdoptedBy(Pet pet)
    {
        f.Emote("♥", 1.2f);
        Write("adopted:" + pet.Name, V($"{pet.Name} follows me everywhere now.", $"{pet.Name} picked ME! I have a {(pet.Kind == PetKind.Cat ? "cat" : "dog")}!!", $"{pet.Name} won't leave me alone. I don't mind.",
                                        $"{pet.Name} chose me. I didn't know I needed that.", $"{pet.Name} and I understand each other."), "♥", 1e9f);
    }

    /// <summary>The dog dropped the ball at our feet: throw it again (if we're up for it).</summary>
    public void OnPetBroughtBall(Pet pet, Prop ball, World w)
    {
        if (f.Mode != Mode.Control || !f.Grounded || Stamina < 0.2f || _g is G.Sleep or G.Fight or G.Sport) return;
        FaceTo(pet.Pos.X);
        float dir = rng.NextDouble() < 0.5 ? -1 : 1;
        ball.Vel = new Vector2(dir * rng.Range(450, 900), -rng.Range(450, 850)) * S;
        ball.OnGround = false;
        World.Play(Sfx.BallKick, ball.Pos, 0.4f);
        f.SetAction(Act.Throw);
        f.Emote("fetch!", 1);
        Write("fetch:" + pet.Name, V($"Played fetch with {pet.Name}.", $"Fetch with {pet.Name}! Again! Again!", $"{pet.Name} kept bringing the ball back. Fine. I threw it."), "★", 1800);
    }
}
