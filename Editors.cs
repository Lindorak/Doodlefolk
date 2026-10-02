using Vortice.Mathematics;
using WinColor = System.Drawing.Color;

namespace StickFight;

/// <summary>Small helpers for building the settings windows.</summary>
static class Ui
{
    public static readonly Font Body = new("Segoe UI", 9f);
    public static readonly Font Heading = new("Segoe UI Semibold", 10f);

    public static WinColor ToGdi(Color4 c) => WinColor.FromArgb((int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
    public static Color4 ToColor4(WinColor c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1);

    public static Form NewWindow(string title, int width)
    {
        return new Form
        {
            Text = title,
            Font = Body,
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ShowInTaskbar = true,
            TopMost = true,
            Width = width,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
    }

    public static TableLayoutPanel Grid(Control parent)
    {
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        parent.Controls.Add(t);
        return t;
    }

    public static Label Label(string text, bool bold = false) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Font = bold ? Heading : Body, Margin = new Padding(3, 8, 3, 3) };

    public static void Section(TableLayoutPanel t, string title)
    {
        var l = Label(title, true);
        l.Margin = new Padding(3, 14, 3, 2);
        t.Controls.Add(l);
        t.SetColumnSpan(l, 3);
    }

    /// <summary>A labelled 0..max slider whose value text updates live.</summary>
    public static TrackBar Slider(TableLayoutPanel t, string name, int min, int max, int value, Func<int, string> fmt, Action<int> changed, string? tip = null, ToolTip? tips = null)
    {
        var label = Label(name);
        var bar = new TrackBar { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickStyle = TickStyle.None, Width = 250, AutoSize = false, Height = 30 };
        var val = Label(fmt(bar.Value));
        bar.ValueChanged += (_, _) => { val.Text = fmt(bar.Value); changed(bar.Value); };
        if (tip != null && tips != null) { tips.SetToolTip(label, tip); tips.SetToolTip(bar, tip); }
        t.Controls.Add(label);
        t.Controls.Add(bar);
        t.Controls.Add(val);
        return bar;
    }

    public static ProgressBar Meter(TableLayoutPanel t, string name, out Label value)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Width = 250, Height = 14, Margin = new Padding(3, 9, 3, 3) };
        value = Label("");
        t.Controls.Add(Label(name));
        t.Controls.Add(bar);
        t.Controls.Add(value);
        return bar;
    }
}

/// <summary>Edit one figure: name, colour, size, personality (live), plus a live view of its mood.</summary>
sealed class FigureEditor
{
    readonly Form _form;
    Figure _f;

    public FigureEditor(Figure figure, World world, Func<Figure, float, Figure> resize, Func<Figure, string, string> rename,
                        Action<Figure> saveToLibrary, IReadOnlyList<SavedFigure> library)
    {
        _f = figure;
        _form = Ui.NewWindow($"{figure.Name}", 470);
        var tips = new ToolTip();
        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
        _form.Controls.Add(root);
        var t = Ui.Grid(root);

        // ---- identity ----
        Ui.Section(t, "Look");
        var name = new TextBox { Text = _f.Name, Width = 250 };
        name.Leave += (_, _) => { name.Text = rename(_f, name.Text.Trim().Length > 0 ? name.Text.Trim() : _f.Name); _form.Text = name.Text; };
        t.Controls.Add(Ui.Label("Name"));
        t.Controls.Add(name);
        t.Controls.Add(new Label());

        var swatches = new FlowLayoutPanel { AutoSize = true, Width = 250, WrapContents = true, Margin = new Padding(0) };
        foreach (var (pname, c) in Palette.All)
        {
            var b = new Button { Width = 22, Height = 22, BackColor = Ui.ToGdi(c), FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
            b.FlatAppearance.BorderColor = WinColor.FromArgb(80, 80, 80);
            tips.SetToolTip(b, pname);
            b.Click += (_, _) => _f.Color = c;
            swatches.Controls.Add(b);
        }
        var custom = new Button { Text = "Custom…", AutoSize = true, Margin = new Padding(2) };
        custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = Ui.ToGdi(_f.Color), FullOpen = true };
            if (dlg.ShowDialog(_form) == DialogResult.OK) _f.Color = Ui.ToColor4(dlg.Color);
        };
        swatches.Controls.Add(custom);
        t.Controls.Add(Ui.Label("Colour"));
        t.Controls.Add(swatches);
        t.Controls.Add(new Label());

        var size = Ui.Slider(t, "Size", 40, 300, (int)MathF.Round(_f.SizeMul * 100), v => $"{v}%", _ => { }, "How big this figure is.", tips);
        size.MouseUp += (_, _) => _f = resize(_f, size.Value / 100f);
        size.KeyUp += (_, _) => _f = resize(_f, size.Value / 100f);

        var gear = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        foreach (Gear g in Enum.GetValues<Gear>()) gear.Items.Add(GearInfo.Describe(g));
        gear.SelectedIndex = (int)_f.Gear;
        gear.SelectedIndexChanged += (_, _) => _f.Gear = (Gear)gear.SelectedIndex;
        t.Controls.Add(Ui.Label("Fists"));
        t.Controls.Add(gear);
        t.Controls.Add(new Label());

        // ---- personality ----
        Ui.Section(t, "Personality");
        var preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        preset.Items.Add("Custom");
        foreach (var p in Personality.Presets) preset.Items.Add($"{p.Name} — {p.Blurb}");
        foreach (var s in library) preset.Items.Add($"★ {s.Name} (saved)");
        preset.SelectedIndex = 0;
        t.Controls.Add(Ui.Label("Preset"));
        t.Controls.Add(preset);
        var random = new Button { Text = "Dice", AutoSize = true };
        tips.SetToolTip(random, "Randomize personality");
        t.Controls.Add(random);

        var traits = new (string Name, string Tip, Func<float> Get, Action<float> Set)[]
        {
            ("Energy", "Lazy ↔ hyper. Speed, stamina, how much it rests.", () => _f.Traits.Energy, v => _f.Traits.Energy = v),
            ("Curiosity", "How much it explores other windows and watches your cursor.", () => _f.Traits.Curiosity, v => _f.Traits.Curiosity = v),
            ("Bravery", "Timid figures startle and flee; brave ones stand their ground.", () => _f.Traits.Bravery, v => _f.Traits.Bravery = v),
            ("Playfulness", "Tricks, flips and playing with balls.", () => _f.Traits.Playfulness, v => _f.Traits.Playfulness = v),
            ("Aggression", "Picking fights, swatting (or punching) your cursor, laughing at others' misfortune.", () => _f.Traits.Aggression, v => _f.Traits.Aggression = v),
            ("Sociability", "How much it seeks out other figures to chat, high-five and hang out.", () => _f.Traits.Sociability, v => _f.Traits.Sociability = v),
        };
        var bars = new List<TrackBar>();
        bool syncing = false;
        foreach (var tr in traits)
        {
            var set = tr.Set;
            bars.Add(Ui.Slider(t, tr.Name, 0, 100, (int)(tr.Get() * 100), v => v.ToString(), v => { set(v / 100f); if (!syncing) preset.SelectedIndex = 0; }, tr.Tip, tips));
        }
        void Sync()
        {
            syncing = true;
            for (int i = 0; i < traits.Length; i++) bars[i].Value = (int)MathF.Round(traits[i].Get() * 100);
            syncing = false;
        }
        var libraryTraits = library.Select(s => s.Traits.Clone()).ToList();
        preset.SelectedIndexChanged += (_, _) =>
        {
            int i = preset.SelectedIndex - 1;
            if (i < 0) return;
            _f.Traits.CopyFrom(i < Personality.Presets.Length ? Personality.Presets[i].Traits : libraryTraits[i - Personality.Presets.Length]);
            Sync();
        };
        random.Click += (_, _) => { _f.Traits.CopyFrom(Personality.Random(world.Rng)); Sync(); preset.SelectedIndex = 0; };

        // ---- live mood ----
        Ui.Section(t, "Right now");
        var state = Ui.Label("");
        t.Controls.Add(Ui.Label("Doing"));
        t.Controls.Add(state);
        t.Controls.Add(new Label());
        var meters = new (string, Func<Brain, float>)[]
        {
            ("Stamina", b => b.Stamina), ("Boredom", b => b.Boredom), ("Loneliness", b => b.Loneliness),
            ("Annoyance", b => b.Annoyance), ("Trusts you", b => b.CursorTrust),
        };
        var meterBars = meters.Select(m => (Ui.Meter(t, m.Item1, out var lbl), lbl, m.Item2)).ToList();
        var friends = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(260, 0), Margin = new Padding(3, 8, 3, 3) };
        t.Controls.Add(Ui.Label("Feels about"));
        t.Controls.Add(friends);
        t.Controls.Add(new Label());

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 12, 3, 3) };
        var saveLib = new Button { Text = "Save to library", AutoSize = true };
        tips.SetToolTip(saveLib, "Remember this figure (name, colour, size, fists, personality) so you can spawn it again from the tray.");
        saveLib.Click += (_, _) =>
        {
            saveToLibrary(_f);
            saveLib.Text = "Saved ✓";
        };
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => _form.Close();
        buttons.Controls.Add(saveLib);
        buttons.Controls.Add(close);
        root.Controls.Add(buttons);
        _form.AcceptButton = close;

        var timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) =>
        {
            if (!world.Figures.Contains(_f)) { _form.Close(); return; }
            state.Text = _f.Mode == Mode.Control ? _f.Brain.State : _f.Mode.ToString();
            foreach (var (bar, lbl, get) in meterBars)
            {
                int v = (int)MathF.Round(get(_f.Brain) * 100);
                bar.Value = Math.Clamp(v, 0, 100);
                lbl.Text = v.ToString();
            }
            var rel = world.Figures.Where(o => o != _f).Select(o =>
            {
                float a = _f.Brain.AffinityWith(o);
                string word = a > 0.6f ? "best friends" : a > 0.3f ? "likes" : a > -0.1f ? "neutral" : a > -0.5f ? "dislikes" : "can't stand";
                return $"{o.Name}: {word}";
            });
            friends.Text = string.Join("\n", rel.DefaultIfEmpty("(nobody else around)"));
        };
        timer.Start();
        _form.FormClosed += (_, _) => { timer.Dispose(); tips.Dispose(); };
    }

    public void Show() => _form.Show();
}

/// <summary>Colour relationships (same colour / different colours / specific pairs) and fight options.</summary>
sealed class FightEditor
{
    readonly Form _form;

    public FightEditor(World world, Action save)
    {
        var rules = world.Fight;
        _form = Ui.NewWindow("Colours & fights", 520);
        var tips = new ToolTip();
        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
        _form.Controls.Add(root);
        var t = Ui.Grid(root);

        Ui.Section(t, "Fights");
        var enabled = new CheckBox { Text = "Allow fights", Checked = rules.Enabled, AutoSize = true };
        enabled.CheckedChanged += (_, _) => { rules.Enabled = enabled.Checked; save(); };
        t.Controls.Add(enabled);
        t.SetColumnSpan(enabled, 3);
        var cursor = new CheckBox { Text = "Feisty figures can punch your cursor (it gets shoved)", Checked = rules.PunchCursor, AutoSize = true };
        tips.SetToolTip(cursor, "Only aggressive figures that don't trust you, when you hover on or poke them.");
        cursor.CheckedChanged += (_, _) => { rules.PunchCursor = cursor.Checked; save(); };
        t.Controls.Add(cursor);
        t.SetColumnSpan(cursor, 3);
        Ui.Slider(t, "How often", 0, 200, (int)MathF.Round(rules.Frequency * 100), v => $"{v}%", v => { rules.Frequency = v / 100f; save(); },
                  "How readily fights break out, and how fast fighters attack.", tips);
        Ui.Slider(t, "Hit strength", 25, 300, (int)MathF.Round(rules.Strength * 100), v => $"{v}%", v => { rules.Strength = v / 100f; save(); },
                  "Damage and knockback. Crank it up to send figures flying across the screen.", tips);

        var death = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        death.Items.AddRange(new object[] { "Just get knocked down", "Knocked out (revive later)", "Die permanently" });
        death.SelectedIndex = (int)rules.OnZeroHealth;
        death.SelectedIndexChanged += (_, _) => { rules.OnZeroHealth = (DeathRule)death.SelectedIndex; save(); };
        tips.SetToolTip(death, "What happens when a figure's health runs out in a fight. Friends can revive knocked-out figures. " +
                               "Dead figures fade away for good (they're removed from your saved cast, but stay in your library).");
        t.Controls.Add(Ui.Label("At zero health"));
        t.Controls.Add(death);
        t.Controls.Add(new Label());
        Ui.Slider(t, "Out cold for", 5, 120, (int)rules.ReviveSeconds, v => $"{v}s", v => { rules.ReviveSeconds = v; save(); },
                  "How long a knocked-out figure stays down before getting up on its own.", tips);
        var bars = new CheckBox { Text = "Show health bars during fights", Checked = rules.HealthBars, AutoSize = true };
        bars.CheckedChanged += (_, _) => { rules.HealthBars = bars.Checked; save(); };
        t.Controls.Add(bars);
        t.SetColumnSpan(bars, 3);

        Ui.Section(t, "How colours get along");
        ComboBox RelationBox(Relation value, bool allowDefault, Action<Relation> set)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
            var values = Enum.GetValues<Relation>().Where(r => allowDefault || r != Relation.Default).ToList();
            foreach (var r in values) box.Items.Add(FightSettings.Describe(r));
            box.SelectedIndex = Math.Max(0, values.IndexOf(value));
            box.SelectedIndexChanged += (_, _) => { set(values[box.SelectedIndex]); save(); };
            return box;
        }
        t.Controls.Add(Ui.Label("Same colour"));
        t.Controls.Add(RelationBox(rules.SameColour, false, r => rules.SameColour = r));
        t.Controls.Add(new Label());
        t.Controls.Add(Ui.Label("Different colours"));
        t.Controls.Add(RelationBox(rules.DifferentColour, false, r => rules.DifferentColour = r));
        t.Controls.Add(new Label());

        // Specific pairs among the colours currently on screen (plus same-colour pairs).
        var teams = world.Figures.Select(f => FightSettings.Team(f.Color)).Distinct().OrderBy(n => Array.FindIndex(Palette.All, p => p.Name == n)).ToList();
        Ui.Section(t, teams.Count > 0 ? "Specific colours (on screen now)" : "Specific colours (spawn some figures first)");
        for (int i = 0; i < teams.Count; i++)
            for (int j = i; j < teams.Count; j++)
            {
                string a = teams[i], b = teams[j], key = FightSettings.PairKey(a, b);
                var pair = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0), WrapContents = false };
                pair.Controls.Add(Swatch(a));
                pair.Controls.Add(Swatch(b));
                pair.Controls.Add(new Label { Text = a == b ? $"{a} & {a}" : $"{a} & {b}", AutoSize = true, UseMnemonic = false, Margin = new Padding(4, 4, 0, 0) });
                t.Controls.Add(pair);
                t.Controls.Add(RelationBox(rules.Pairs.TryGetValue(key, out var r) ? r : Relation.Default, true, rel =>
                {
                    if (rel == Relation.Default) rules.Pairs.Remove(key); else rules.Pairs[key] = rel;
                }));
                t.Controls.Add(new Label());
            }

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(450, 0),
            ForeColor = WinColor.DimGray,
            Margin = new Padding(3, 12, 3, 3),
            Text = "Friends never fight and like hanging out. Rivals challenge each other to friendly sparring matches " +
                   "(first knockdown wins). Enemies pick real fights, hold grudges and send each other flying. " +
                   "Neutral figures only fight if they've been wronged. Personality still matters: hotheads start more fights, " +
                   "timid figures run away. Custom colours count as the nearest palette colour.",
        };
        root.Controls.Add(help);
        var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 12, 3, 3) };
        close.Click += (_, _) => _form.Close();
        root.Controls.Add(close);
        _form.AcceptButton = close;
        _form.FormClosed += (_, _) => tips.Dispose();
    }

    static Control Swatch(string team)
    {
        var c = Palette.All.First(p => p.Name == team).Color;
        return new Panel { Width = 14, Height = 14, BackColor = Ui.ToGdi(c), Margin = new Padding(2, 5, 0, 0), BorderStyle = BorderStyle.FixedSingle };
    }

    public void Show() => _form.Show();
}

/// <summary>Edit a ball: type, size, bounciness, colour.</summary>
sealed class PropEditor
{
    readonly Form _form;

    public PropEditor(Prop prop, World world)
    {
        _form = Ui.NewWindow(Prop.KindName(prop.Kind), 470);
        var tips = new ToolTip();
        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
        _form.Controls.Add(root);
        var t = Ui.Grid(root);

        var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        foreach (PropKind k in Enum.GetValues<PropKind>()) kind.Items.Add(Prop.KindName(k));
        kind.SelectedIndex = (int)prop.Kind;
        kind.SelectedIndexChanged += (_, _) => { prop.Kind = (PropKind)kind.SelectedIndex; _form.Text = Prop.KindName(prop.Kind); };
        t.Controls.Add(Ui.Label("Type"));
        t.Controls.Add(kind);
        t.Controls.Add(new Label());

        Ui.Slider(t, "Size", 30, 500, (int)MathF.Round(prop.SizeMul * 100), v => $"{v}%", v => prop.SizeMul = v / 100f,
                  "Bigger balls are heavier: harder to kick far, and they knock figures over.", tips);
        Ui.Slider(t, "Bounciness", 0, 95, (int)MathF.Round(prop.Bounce * 100), v => $"{v}%", v => prop.Bounce = v / 100f,
                  "How much energy it keeps on each bounce.", tips);

        var colour = new Button { Text = "Choose…", AutoSize = true, BackColor = Ui.ToGdi(prop.Color) };
        colour.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = Ui.ToGdi(prop.Color), FullOpen = true };
            if (dlg.ShowDialog(_form) == DialogResult.OK) { prop.Color = Ui.ToColor4(dlg.Color); colour.BackColor = dlg.Color; }
        };
        tips.SetToolTip(colour, "Used by the plain ball and the soccer ball.");
        t.Controls.Add(Ui.Label("Colour"));
        t.Controls.Add(colour);
        t.Controls.Add(new Label());

        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 12, 3, 3) };
        var remove = new Button { Text = "Remove", AutoSize = true };
        remove.Click += (_, _) => { world.RemoveProp(prop); _form.Close(); };
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => _form.Close();
        buttons.Controls.Add(remove);
        buttons.Controls.Add(close);
        root.Controls.Add(buttons);
        _form.AcceptButton = close;

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => { if (!world.Props.Contains(prop)) _form.Close(); };
        timer.Start();
        _form.FormClosed += (_, _) => { timer.Dispose(); tips.Dispose(); };
    }

    public void Show() => _form.Show();
}
