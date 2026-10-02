using System.Numerics;

namespace Doodlefolk;

/// <summary>What a figure does for a living.</summary>
enum Job { None, Shopkeeper, Chef, Builder, Entertainer, Teacher }

/// <summary>Town life. Figures have jobs (picked from their nature, changeable in the Studio): shopkeepers mind the
/// stall, chefs cook at the food cart, entertainers perform on the stage, teachers take the class at the chalkboard,
/// and builders put up forts and treehouses. Working earns coins; coins buy what they wish for at the shop, a meal
/// at the food cart, or go in the hat when someone's performing. Figures also age (at a pace you choose): the young
/// go to school, and elders go grey, slow down, lean on a cane and retire. And moods are catching.</summary>
sealed partial class Brain
{
    public Job Job = (Job)(-1);
    public int Coins = 10;
    float _workCd = 20, _earnT, _chefT, _schoolCd = 30, _buyCd = 30, _tipCd = 30, _moodT;
    int _earnedToday;
    Item? _workplace;

    public static string JobName(Job j) => j switch
    {
        Job.Shopkeeper => "shopkeeper", Job.Chef => "chef", Job.Builder => "builder", Job.Entertainer => "entertainer", Job.Teacher => "teacher", _ => "",
    };

    static string? WorkplaceKey(Job j) => j switch { Job.Shopkeeper => "shopstall", Job.Chef => "foodcart", Job.Entertainer => "stage", Job.Teacher => "schoolboard", _ => null };

    /// <summary>A job to suit its nature (the first time it's asked).</summary>
    public void EnsureJob()
    {
        if ((int)Job >= 0) return;
        var scores = new (Job j, float s)[]
        {
            (Job.Shopkeeper, P.Sociability * 1.2f + (1 - P.Aggression) * 0.3f),
            (Job.Chef, P.Curiosity * 0.6f + (1 - P.Energy) * 0.5f),
            (Job.Builder, P.Energy * 0.7f + P.Bravery * 0.5f),
            (Job.Entertainer, P.Playfulness * 1.1f + P.Sociability * 0.3f),
            (Job.Teacher, P.Curiosity * 0.9f + (1 - P.Playfulness) * 0.3f),
        };
        Job = scores.OrderByDescending(x => x.s + (float)rng.NextDouble() * 0.4f).First().j;
    }

    /// <summary>You changed its job in the Studio.</summary>
    public void JobChanged()
    {
        if (_g == G.Work) EndWork();
        _workCd = _t0 + 5;
        if (Job != Job.None) Write("job", V($"Became a {JobName(Job)}.", $"I'M A {JobName(Job).ToUpperInvariant()} NOW!!", $"New job: {JobName(Job)}. Fine.", $"I'm going to be a {JobName(Job)}… I hope I'm good at it.", $"I take up the work of a {JobName(Job)}."), "★", 0);
    }

    /// <summary>You gave it some coins.</summary>
    public void GotCoins(int n)
    {
        Cheered(0.2f);
        f.Emote(V("coins! thank you!", "COINS!!! I'M RICH!", "oh. coins. thanks.", "f-for me? thank you…", "a generous gift"), 1.5f);
        Write("coins", V($"You gave me {n} coins! I have {Coins} now.", $"YOU GAVE ME {n} COINS!!!", $"Got {n} coins from you. Not bad.", $"You gave me {n} coins… I'll save them.", $"A gift of {n} coins."), "♥", 120);
    }

    // ---------------- ageing ----------------

    /// <summary>Years of age: figures born here count up from childhood, everyone else from twenty, at the life pace.</summary>
    public float AgeYears => ParentIds.Count > 0 && Baby ? 3 + Grown * 15 : (ParentIds.Count > 0 ? 18 : 20) + AgeBank;
    /// <summary>Years lived since it grew up (or since ageing was switched on): counted while Doodlefolk runs.</summary>
    public float AgeBank;
    DateTime _ageTick = DateTime.Now;

    /// <summary>After loading: elders have already been announced.</summary>
    public void AgeLoaded() => _toldOld = IsElder;
    public bool IsElder => World.LifePace > 0 && AgeYears >= 62;
    public string LifeStage => Baby ? (Grown < 0.4f ? "Little one" : "Kid") : IsElder ? "Elder" : AgeYears < 26 ? "Young adult" : "Adult";
    bool _toldOld;

    void UpdateAge(World w)
    {
        var now = DateTime.Now;
        float days = (float)Math.Clamp((now - _ageTick).TotalDays, 0, 1);
        _ageTick = now;
        if (!Baby) AgeBank += days * World.LifePace;
        f.Elder = IsElder;
        if (IsElder && !_toldOld)
        {
            _toldOld = true;
            if (Job != Job.None) { Write("retired", V($"Retired from being a {JobName(Job)}. Time to rest.", $"RETIRED! No more work, ever!", "Retired. Finally.", $"I've retired. I'll miss being a {JobName(Job)}.", "I have put down my work."), "★", 1e9f); Job = Job.None; }
            w.News("town", $"{f.Name} is now one of the town's elders", 2, f);
        }
    }

    // ---------------- work ----------------

    void TownOptions(World w, OptionList opts)
    {
        UpdateAge(w);
        MoodContagion(w);
        if (!World.Jobs || f.Hunter) return;
        EnsureJob();
        // School for the young (when there is one).
        if (Baby && Grown > 0.25f && _schoolCd < _t0 && w.Night < 0.5f && w.Items.FirstOrDefault(i => i.Def.Key == "schoolboard") is { } board)
            opts.Add(0.9f + P.Curiosity * 0.5f, () => GoToSchool(board, w), "Go to school");
        if (Baby || IsElder) { ShoppingOptions(w, opts); return; }
        // Going to work (mornings and afternoons especially; more so when broke).
        if (_workCd < _t0 && Job is not (Job.None or Job.Builder) && WorkplaceKey(Job) is { } key && w.Items.FirstOrDefault(i => i.Def.Key == key && i.Free) is { } place)
        {
            float want = (0.35f + P.Energy * 0.3f + (Coins < 5 ? 0.9f : 0) - w.Night * 0.4f) * (Job == Job.Teacher && !w.Figures.Any(o => o.Brain.Baby) ? 0.1f : 1);
            opts.Add(want, () => GoToWork(place, w), $"Go to work ({JobName(Job)})");
        }
        if (Job == Job.Builder) BuildOptions(w, opts);
        ShoppingOptions(w, opts);
    }

    void GoToWork(Item place, World w)
    {
        _workCd = _t0 + rng.Range(150, 300);
        _workplace = place;
        // Shopkeepers stand behind the counter, chefs at the side of the cart, teachers beside the board.
        float off = Job switch { Job.Shopkeeper => 0, Job.Chef => -place.Def.W * place.Sc * 0.62f, Job.Teacher => place.Def.W * place.Sc * 0.62f, _ => 0 };
        Navigate(() => w.Items.Contains(place) ? (Job == Job.Entertainer ? StageSpot(place, w) : new Vector2(place.Pos.X + off, place.Pos.Y)) : null, 8 * S, false, () =>
        {
            Go(G.Work, rng.Range(40, 75));
            _earnT = 0; _earnedToday = 0;
            f.Emote(Job switch { Job.Shopkeeper => V("shop's open!", "SHOP'S OPEN!!", "open. buy something.", "um… we're open!", "the shop awakens"), Job.Chef => V("kitchen's open!", "WHO'S HUNGRY?!", "cooking. don't watch.", "I'll cook something nice…", "the stove is lit"), Job.Entertainer => V("showtime!", "SHOWTIME!!!", "fine. a show.", "h-hello everyone…", "let the show begin"), _ => V("class time!", "CLASS TIME!!", "sit down, class.", "um, class is starting…", "let us learn") }, 1.6f);
        }, WalkPurpose.Other);
    }

    Vector2? StageSpot(Item stage, World w)
    {
        foreach (var p in w.Env.Platforms) if (p.Item == stage) return new Vector2((p.X1 + p.X2) / 2, p.Y);
        return stage.Pos;
    }

    void DoWork(World w)
    {
        var place = _workplace;
        if (place == null || !w.Items.Contains(place) || place.Holder != null || _t > _dur) { EndWork(); return; }
        f.DesiredVX = 0;
        _earnT += World.Dt;
        if (_earnT > 15) { _earnT = 0; Coins++; _earnedToday++; }
        switch (Job)
        {
            case Job.Shopkeeper:
                FaceTo(place.Pos.X + f.Facing * 100);
                f.SetAction(_t % 6 < 1.2f ? Act.Talk : Act.Stand);
                if (rng.NextDouble() < World.Dt * 0.05) f.Emote(V("come buy!", "BARGAINS!!", "buy something.", "w-would you like anything?", "wares of wonder"), 1.3f);
                break;
            case Job.Chef:
                FaceTo(place.Pos.X);
                f.SetAction(Act.Tap);
                _chefT += World.Dt;
                if (_chefT > 14 && w.Items.Count(i => i.Def.Verbs.Contains(Verb.Eat) && Vector2.Distance(i.Pos, place.Pos) < 120 * S) < 2 && w.Items.Count < 55)
                {
                    _chefT = 0;
                    if (w.MakeItem?.Invoke(new[] { "pizza", "burger", "cookie", "apple", "cake" }[rng.Next(5)]) is { } dish)
                    {
                        dish.Pos = place.Local(rng.Range(-14, 14), 28); dish.Vel = Vector2.Zero; dish.OnGround = false;
                        f.Emote(V("order up!", "ORDER UP!!", "food's ready.", "here, it's ready…", "a dish is born"), 1.2f);
                        World.Play(Sfx.Munch, dish.Pos, 0.2f, 0.7f);
                    }
                }
                break;
            case Job.Entertainer:
                if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(rng.NextDouble() < 0.6 ? Fidget.Groove : Fidget.Stretch);
                if (rng.NextDouble() < World.Dt * 0.3) f.Emote(rng.NextDouble() < 0.5 ? "♪" : "♫", 0.9f);
                if (rng.NextDouble() < World.Dt * 0.08 && f.Grounded) { f.RequestFlip(55 * S); }
                break;
            case Job.Teacher:
                FaceTo(_t % 8 < 2 ? place.Pos.X : place.Pos.X + 400);
                f.SetAction(_t % 5 < 2 ? Act.Talk : Act.Stand);
                if (rng.NextDouble() < World.Dt * 0.12) f.Emote(new[] { "2 + 2 = 4", "A, B, C…", "the moon goes round", "plants need water!", "be kind ♥", "rabbits love carrots", "what's 3 × 3?" }[rng.Next(7)], 2);
                break;
        }
    }

    void EndWork()
    {
        if (_earnedToday > 0)
            Write("work", V($"Worked as a {JobName(Job)}. Earned {_earnedToday} coins ({Coins} now).", $"WORK DONE! {_earnedToday} coins! I'm RICH ({Coins})!", $"Worked. {_earnedToday} coins. {Coins} total. Fine.", $"I worked hard today… {_earnedToday} coins.", $"A day of honest work: {_earnedToday} coins."), "★", 600);
        _workplace = null;
        Go(G.Idle, 1);
    }

    /// <summary>Is someone working at this place right now?</summary>
    static Figure? WorkingAt(World w, Item place) => w.Figures.FirstOrDefault(o => o.Brain._g == G.Work && o.Brain._workplace == place);

    // ---------------- spending ----------------

    void ShoppingOptions(World w, OptionList opts)
    {
        // A meal at the food cart.
        if (Hunger > 0.45f && Coins >= 2 && _buyCd < _t0 && w.Items.FirstOrDefault(i => i.Def.Key == "foodcart" && WorkingAt(w, i) != null) is { } cart)
            opts.Add(Hunger * 2.5f, () => GoBuy(cart, null, w), "Buy a meal at the food cart");
        // Something it wants, from the shop.
        if (Coins >= 4 && _buyCd < _t0 && Wanting(w) is { Item: { } want } && w.Items.FirstOrDefault(i => i.Def.Key == "shopstall" && WorkingAt(w, i) != null) is { } shop)
            opts.Add(0.6f + Boredom * 0.6f, () => GoBuy(shop, want, w), $"Buy {want.Article} {want.Name.ToLowerInvariant()} at the shop");
        // A show on: go and watch, and tip.
        if (_tipCd < _t0 && w.Figures.FirstOrDefault(o => o != f && o.Brain._g == G.Work && o.Brain.Job == Job.Entertainer && Vector2.Distance(o.Base, f.Base) < 1500 * S) is { } act)
            opts.Add(0.5f + P.Sociability * 0.5f + Boredom * 0.6f, () => WatchShow(act, w), $"Watch {act.Name}'s show");
    }

    static int Price(ItemDef d) => Math.Clamp((int)MathF.Round((d.W * d.H) / 400f) + 2, 2, 9);

    void GoBuy(Item stall, ItemDef? thing, World w)
    {
        _buyCd = _t0 + 120;
        float side = MathF.Sign(f.Base.X - stall.Pos.X); if (side == 0) side = 1;
        Navigate(() => w.Items.Contains(stall) ? new Vector2(stall.Pos.X + side * (stall.Def.W * stall.Sc * 0.5f + 14 * S), stall.Pos.Y) : null, 10 * S, false, () =>
        {
            var seller = WorkingAt(w, stall);
            if (seller == null) { f.Emote("closed?", 1.2f); Go(G.Idle, 1); return; }
            FaceTo(stall.Pos.X);
            int price = thing != null ? Price(thing) : 2;
            if (Coins < price) { f.Emote(V("can't afford it…", "NOT ENOUGH COINS!", "too pricey.", "oh… I'm short…", "my purse is light"), 1.4f); Go(G.Idle, 1); return; }
            Coins -= price; seller.Brain.Coins += price;
            w.Sticker("shopping");
            seller.Emote(seller.Brain.V("thank you!", "THANKS!!", "pleasure.", "th-thank you!", "may it serve you well"), 1.2f);
            World.Play(Sfx.Pip, stall.Pos, 0.3f, 1.6f);
            var got = w.MakeItem?.Invoke(thing?.Key ?? new[] { "pizza", "burger", "cookie" }[rng.Next(3)]);
            if (got != null) { got.Pos = f.Base + new Vector2(f.Facing * 20 * S, -40 * S); got.Vel = Vector2.Zero; got.OnGround = false; }
            string what = got?.Def.Name.ToLowerInvariant() ?? "something";
            Write("bought:" + what, V($"Bought {got?.Def.Article ?? "a"} {what} with my own coins ({price}).", $"BOUGHT {got?.Def.Article?.ToUpperInvariant() ?? "A"} {what.ToUpperInvariant()}!!! {price} coins!", $"Paid {price} coins for {got?.Def.Article ?? "a"} {what}.", $"I saved up and bought {got?.Def.Article ?? "a"} {what}…", $"Traded {price} coins for {got?.Def.Article ?? "a"} {what}."), "★", 300);
            if (got != null && got.Def.Verbs.FirstOrDefault() is var v && v != default) UseItem(got, got.Def.Verbs.Contains(Verb.Eat) ? Verb.Eat : v, w);
            else Go(G.Cheer, 1);
        }, WalkPurpose.Other);
    }

    void WatchShow(Figure act, World w)
    {
        _tipCd = _t0 + 200;
        float side = MathF.Sign(f.Base.X - act.Base.X); if (side == 0) side = 1;
        float off = rng.Range(90, 180) * S;
        Navigate(() => w.Figures.Contains(act) ? new Vector2(act.Base.X + side * off, act.Base.Y) : null, 30 * S, false, () =>
        {
            FaceTo(act.Base.X);
            Go(G.SitFloor, rng.Range(8, 14));
            Cheered(0.15f); Boredom = MathF.Max(0, Boredom - 0.3f);
            if (Coins > 0 && act.Brain._g == G.Work)
            {
                Coins--; act.Brain.Coins++;
                f.Emote(V("bravo!", "BRAVO!!! *clap clap*", "not bad. here.", "that was lovely…", "a coin for the artist"), 1.4f);
                Write("show:" + act.Name, V($"Watched {act.Name}'s show and tipped a coin.", $"{act.Name}'s SHOW!!! AMAZING!", $"{act.Name} performed. I tipped. Once.", $"I watched {act.Name} perform. It made me smile.", $"{act.Name} performed under the lights."), "♪", 600);
            }
        }, WalkPurpose.Watch);
    }

    // ---------------- school ----------------

    void GoToSchool(Item board, World w)
    {
        _schoolCd = _t0 + rng.Range(240, 480);
        float off = rng.Range(0, 70) * S;
        Navigate(() => w.Items.Contains(board) ? new Vector2(board.Pos.X + off + board.Def.W * board.Sc * 1.4f, board.Pos.Y) : null, 10 * S, false, () =>
        {
            FaceTo(board.Pos.X);
            Go(G.SitFloor, rng.Range(20, 35));
            bool teacher = WorkingAt(w, board) != null;
            var skill = Enum.GetValues<SkillKind>()[rng.Next(Enum.GetValues<SkillKind>().Length)];
            Practice(skill, teacher ? 0.12f : 0.05f);
            string topic = new[] { "counting", "the alphabet", "the planets", "plants", "being kind", "drawing", "animals" }[rng.Next(7)];
            Write("school", V($"Learned about {topic} at school.", $"SCHOOL!!! We learned about {topic}!", $"School. {topic}. Okay, it was interesting.", $"At school we learned about {topic}… I put my hand up once!", $"Today's lesson: {topic}."), "★", 600);
        }, WalkPurpose.Other);
    }

    // ---------------- moods are catching ----------------

    void MoodContagion(World w)
    {
        _moodT -= 1;
        if (_moodT > 0) return;
        _moodT = 3;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || Vector2.Distance(o.Base, f.Base) > 320 * S) continue;
            float a = AffinityWith(o);
            if (a <= 0) continue;
            float k = 0.04f * a * (0.5f + P.Sociability);
            Joy += (o.Brain.Joy - Joy) * k;
            Sadness += (o.Brain.Sadness - Sadness) * k;
            Annoyance += (o.Brain.Annoyance - Annoyance) * k * 0.5f;
            // Laughter is catching.
            if (o.CurrentEmote is "ha" or "haha" or "hehe" && f.CurrentEmote == null && rng.NextDouble() < 0.25 + P.Playfulness * 0.3)
            {
                f.Emote("ha", 1);
                Cheered(0.05f);
            }
        }
    }

    public string DebugTown(World w, string what, string arg)
    {
        EnsureJob();
        switch (what)
        {
            case "job": if (Enum.TryParse<Job>(arg, true, out var j)) { Job = j; JobChanged(); } break;
            case "work":
                if (WorkplaceKey(Job) is { } key && w.Items.FirstOrDefault(i => i.Def.Key == key) is { } place) { _workCd = 0; GoToWork(place, w); }
                else return $"no workplace for {Job}";
                break;
            case "build":
            {
                var o = new OptionList(); _buildCd = 0;
                BuildOptions(w, o);
                if (o.Items.Count == 0) return "can't build here";
                o.Items[0].act();
                break;
            }
            case "grow": if (w.Items.FirstOrDefault(i => i.Def.Key == "buildsite") is { } site) site.Growth = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
            case "school": if (w.Items.FirstOrDefault(i => i.Def.Key == "schoolboard") is { } b) GoToSchool(b, w); break;
            case "buy":
                if (w.Items.FirstOrDefault(i => i.Def.Key == (arg == "food" ? "foodcart" : "shopstall")) is { } st) GoBuy(st, arg == "food" ? null : ItemCatalog.Find("book") ?? ItemCatalog.All[0], w);
                break;
            case "watch": if (w.Figures.FirstOrDefault(o => o.Name == arg) is { } act) WatchShow(act, w); break;
            case "dream": Go(G.Sleep, 60); _dreamAt = 0; break;
            case "showdream": f.Dream = arg.Replace('_', ' '); f.DreamUntil = _t0 + 30; f.Nightmare = arg.EndsWith('!'); break;
            case "coins": Coins = int.Parse(arg); break;
            case "laugh": f.Emote("ha", 2); break;
            case "ride": if (w.Items.FirstOrDefault(i => i.IsVehicle && i.Rider == null && (arg == "" || i.Def.Key == arg)) is { } veh) GoRide(veh, w); else return "no free vehicle"; break;
            case "swim": if (w.Items.FirstOrDefault(i => i.IsWater && (arg == "" || i.Def.Key == arg)) is { } pool) GoSwim(pool, w); break;
            case "fish": if (w.Items.FirstOrDefault(i => i.Def.Verbs.Contains(Verb.Fish)) is { } pond) GoFish(pond, w); break;
            case "bite": _biteAt = 0; break;
            case "befriend": if (w.Figures.FirstOrDefault(o => o.Name == arg) is { } bf) { AddAffinity(bf, 0.5f); bf.Brain.AddAffinity(f, 0.5f); } break;
            case "boost":
            {
                var o2 = new OptionList(); _boostCd = 0;
                BoostOptions(w, o2);
                if (o2.Items.Count == 0)
                {
                    var here = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
                    var ledges = w.Env.Platforms.Where(p => here != null && here.Y - p.Y is > 200 and < 1100 && p.X2 > f.Base.X - 600 && p.X1 < f.Base.X + 600)
                                    .Select(p => $"[{p.X1:0}-{p.X2:0}@{p.Y:0} item={p.Item?.Def.Key}]");
                    var mates = w.Figures.Where(o => o != f).Select(o => $"{o.Name}:{o.Brain._g}/{AffinityWith(o):0.00}/{(w.Env.SupportAt(o.Base.X, o.Base.Y, o.GroundHwnd) is { } t ? $"{t.X1:0}-{t.X2:0}@{t.Y:0}" : "air")}");
                    return $"no leg-up: grounded={f.Grounded} here={(here == null ? "air" : $"{here.X1:0}-{here.X2:0}@{here.Y:0}")} S={S:0.00} ledges {string.Join(" ", ledges)} | {string.Join(" ", mates)}";
                }
                o2.Items[0].act();
                break;
            }
        }
        return $"{f.Name}: {JobName(Job)} coins={Coins} age={AgeYears:0} stage={LifeStage} goal={_g} dream={f.Dream}";
    }
}
