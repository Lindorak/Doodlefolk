# Modding StickFight

Mods are plain JSON files in `%APPDATA%\StickFight\mods` (Studio → Settings → Mods → **Open the mods folder**).
Every `.json` file there is loaded when StickFight starts. A file with a mistake is skipped, and the Studio shows
what went wrong. Mods are data only: they can't run code.

There's a working example in the folder, `example-mod.json.txt`. Rename it to end in `.json` and restart to try it.

A mod file can have any of these sections:

```json
{
  "items": [ ... ],
  "hats": [ ... ],
  "names": { "figures": ["Doodle"], "cats": ["Sir Pounce"], "dogs": [], "parrots": [], "rabbits": [], "hamsters": [] },
  "jokes": ["what do you call a stick figure with no stick? a figure of speech!"]
}
```

Comments (`// …`) and trailing commas are allowed.

## Objects

```json
{
  "key": "lavalamp",              // unique; lowercase letters, digits, - and _
  "name": "Lava lamp",
  "words": ["lava lamp", "lamp"], // what you can type to summon it
  "colour": "#E040FB",            // its main colour (you can recolour it in the Studio)
  "w": 12, "h": 28,               // size in object units (about a pixel at 100% scale)
  "verbs": ["Dance"],             // what figures can do with it (below)
  "likes": ["Dancing"],           // which tastes it appeals to
  "svg": "<svg viewBox='0 0 12 28'>…</svg>"   // or "svg": "lavalamp.svg" (a file in the mods folder), or "shapes": […]
}
```

Optional: `surface` (height of a top they can stand on), `seats` (x positions of seats) and `seatY`,
`comfort` (0–1), `bounce`, `mass`, `carry` (small enough to carry), `bites` (food), `reach` and `damage` (weapons).

**Verbs:** `Sit`, `Lie`, `Hammock`, `Bounce`, `Stand`, `Eat`, `Hide`, `Dance`, `Read`, `Warm`, `Wield`, `Shoot`,
`Play`, `Shelter`, `Collect`, `Tend`, `Ride`, `Swim`, `Fish`.

**Things they can like:** `PlayingBall`, `Juggling`, `Climbing`, `Exploring`, `Chatting`, `HighFives`, `Fighting`,
`Sparring`, `Napping`, `Tricks`, `Dancing`, `Sitting`, `Eating`, `Reading`, `HighPlaces`, …

### Drawing with SVG

The simple parts of SVG work: `rect` (with `rx` for rounded corners), `circle`, `ellipse`, `line`, `polyline`,
`polygon` and `path` (straight lines; curves are joined by their end points), with `fill`, `stroke` and
`stroke-width` (as attributes or in `style`). Transforms and gradients are ignored.

One SVG unit is one object unit, and the **bottom centre of the viewBox sits on the floor**. Use `fill="main"` (or
the object's own colour) for the parts that change when someone recolours it, `dark` and `light` for shades of it.

### Drawing with shapes

Each shape is `{ "k": kind, "p": [numbers], "c": colour, "w": line width, "over": false }`, in object units with
`(0, 0)` at the bottom centre and **y pointing up**:

| kind | numbers |
|---|---|
| `r` rectangle | x0, y0, x1, y1 |
| `o` rounded rectangle | x0, y0, x1, y1, radius |
| `e` ellipse | cx, cy, rx, ry |
| `p` polygon | x, y, x, y, … |
| `l` line | x0, y0, x1, y1 |
| `c` open line | x, y, x, y, … |

`c` is `0` (main colour), `1` (darker), `2` (lighter) or a `"#RRGGBB"` colour. `over: true` draws the shape in
front of anyone using the object (a blanket, a chair back).

## Hats

```json
{ "key": "tiara", "name": "Tiara", "svg": "<svg viewBox='-12 -16 24 8'>…</svg>" }
```

For hats, **10 SVG units are one head radius**, with `(0, 0)` at the centre of the head (so the top of the head is
at y = −10). Red (`#E53935`) or `main` parts take the hat colour you pick in the Studio. Or use `"front"` and
`"back"` shape lists in head units (1 = head radius, y up).

Mod hats show up in the Studio's **Look** tab with the others.

## Names and jokes

`names.figures` are used for newly drawn figures; the pet lists are mixed in with the built-in names. `jokes` are
added to what figures say when you ask for a joke.
