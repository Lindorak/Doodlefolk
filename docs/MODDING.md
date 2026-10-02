# Modding Doodlefolk

Mods are plain JSON files in `%APPDATA%\Doodlefolk\mods` (Studio → Settings → Mods → **Open the mods folder**).
Every `.json` file there is loaded when Doodlefolk starts. A file with a mistake is skipped, and the Studio shows
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

## Characters

Whole figures, ready to spawn from **Studio → Library → From your mods**. A character has the same shape as a
figure saved to the library (colour, size, personality, tastes, look), so the easiest way to make one is to make
the figure in the Studio, save it to the library, and copy its entry out of `settings.json`. They always arrive
fresh: no diary, feelings or history come with them.

```json
"characters": [
  { "name": "Captain Jo", "color": "#1E88E5", "size": 1.1,
    "traits": { "Energy": 0.8, "Curiosity": 0.9, "Bravery": 0.7, "Playfulness": 0.6, "Aggression": 0.2, "Sociability": 0.7 },
    "look": { "hat": "pirate" } }
]
```

## Storytellers

Town moods of your own, next to Cozy, Classic and Chaos. `drama` scales fights, break-ups and storms (Cozy is 0.35,
Chaos is 2); `events` and `visitors` scale how often festivals and visitors come (0 turns them off).

```json
"storytellers": [ { "key": "seaside", "name": "Seaside", "blurb": "Calm, with lots of visitors", "drama": 0.5, "events": 1, "visitors": 2 } ]
```

## Festivals

Your own town events. They run like the festival (everyone gathers, dances and chats) with your title, your
decorations and food (any object keys, built-in or from mods; up to twelve), and fireworks after dark if you like.
They come up on their own among the usual events, and there's a button for each in Settings → Town.

```json
"events": [ { "key": "regatta", "title": "The regatta", "news": "Boats on the pond!", "decor": ["pennant", "lantern"], "food": ["fish"], "fireworks": true } ]
```

## Scenarios

A town to start from: who's in it (character names from your mod; anyone missing is replaced by a random figure),
what's out (`x` is 0 for the left of the screen to 1 for the right), which animals, and the mood. Starting one makes
a new cast, so the player's own town is kept as it is. They're on the Cast page.

```json
"scenarios": [ { "key": "harbour", "name": "Harbour town", "blurb": "A sleepy port", "characters": ["Captain Jo"],
                 "items": [ { "key": "pond", "x": 0.3 } ], "pets": ["cat"], "mood": "seaside" } ]
```

## Songs

Lyrics anyone may sing at the talent show (one line per line).

```json
"songs": [ { "title": "Sea shanty", "lyrics": "yo ho\nheave ho" } ]
```

## Checking a mod before you share it

**Studio → Settings → Mods → Check a mod file…** reads a file without loading it and tells you what it adds, what's
broken (it won't load until that's fixed) and what looks odd (a misspelt object key, a scenario character that isn't
there, a section Doodlefolk doesn't know). Then share it on the Steam Workshop from the same place.
