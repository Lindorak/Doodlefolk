# Doodlefolk on Steam

Everything Steam-related in the code is ready; this is what's left on the Steamworks side, plus how to build and upload.

## 1. Get an app

1. Sign up at [partner.steamgames.com](https://partner.steamgames.com) (tax and bank details), and pay the Steam Direct
   fee ($100 per app, paid back once the game makes $1,000).
2. Create the app. You'll get an **App ID** (say `1234560`) and a **depot** ID (usually App ID + 1).

## 2. Steamworks settings

- **Installation → General**: launch option `Doodlefolk.exe`, OS Windows.
- **Steam Cloud** (Application → Steam Cloud): turn on **Auto-Cloud**, with a byte quota of 50 MB and 2,000 files, and
  one root: `WinAppDataRoaming`, subdirectory `Doodlefolk`, pattern `*`, recursive. That syncs everyone (settings.json),
  saved casts and mods between PCs.
- **Workshop** (Application → Workshop): enable it, visibility public. Doodlefolk publishes items with the tag `Mod`;
  add that tag. Players subscribe on the Workshop page and the mods load on the next start.
- **Achievements** (Stats & Achievements): add the ones below (API name, display name, description). Each sticker in the
  sticker book unlocks its achievement, and stickers earned before Steam are caught up on the first run.
- **Steam Overlay**: Doodlefolk is a see-through window over your whole desktop, so the overlay's "press Shift+Tab"
  pop-up would show over everything. Under **Installation → General → Overlay**, consider turning the overlay off for
  this app (players can still use it from the Steam client).
- **Rich presence**: Doodlefolk sets a `status` line ("5 doodlefolk and 3 pets on the desktop"); nothing to set up.

## 3. Build and upload

```
powershell -ExecutionPolicy Bypass -File tools\steam-build.ps1 -AppId 1234560 -DepotId 1234561
```

That publishes the Steam build (with `steam_api64.dll` beside the exe; the GitHub download leaves it out), writes the
`app_build.vdf`/`depot_build.vdf` scripts into `steam\`, and, if `steamcmd` is installed, offers to upload it (you
sign in with your Steamworks account; set the build live on the partner site afterwards).

The Steam build updates through Steam, so its own GitHub update check and the Install button switch themselves off.

## Testing without an app

Put a file called `steam_appid.txt` containing `480` (Valve's test app, "Spacewar") next to `Doodlefolk.exe` and run it
with Steam open: it connects, loads subscribed Workshop items and tries achievements (Spacewar doesn't have ours, so
those quietly do nothing). Don't publish to the Workshop under 480: it would go public on Spacewar's page.

## Achievements

| API name | Display name | Description |
|---|---|---|
| `STICKER_HELLO` | ✏️ Hello, world | Draw your first figure |
| `STICKER_FULLHOUSE` | 🏠 Full house | Have eight figures at once |
| `STICKER_YEET` | 🚀 Yeet | Throw a figure really hard |
| `STICKER_TALK` | 💬 Small talk | Say something to a figure |
| `STICKER_WISH` | 💭 Wish granted | Give a figure what it asked for |
| `STICKER_WISHES10` | 🎁 Fairy godparent | Grant ten wishes |
| `STICKER_PENCIL` | 🖍️ Creator | A figure draws something with the Creator's Pencil |
| `STICKER_HIDESEEK` | 🙈 Found you! | Find everyone in hide-and-seek |
| `STICKER_TAG` | 🏃 Tag, you're it | Tag a figure in a game of tag |
| `STICKER_CATCH10` | ⚾ Butterfingers no more | Ten catches in a row |
| `STICKER_PHOTO` | 📷 Say cheese | Take a photo |
| `STICKER_CHAMPION` | 🏆 Champion | See a tournament through to the end |
| `STICKER_RIVALS` | ⚔️ Arch-rivals | Two figures become rivals |
| `STICKER_PARTY` | 🎂 Party time | Celebrate a birthday |
| `STICKER_LOVEBIRDS` | 💞 Lovebirds | Two figures start dating |
| `STICKER_BABY` | 🍼 Bundle of joy | A baby is born |
| `STICKER_HOME` | ⛺ Home sweet home | A figure makes somewhere its home |
| `STICKER_CLUB` | 🎌 Join the club | Friends start a club |
| `STICKER_STORY` | 🔥 Around the campfire | Hear a campfire story |
| `STICKER_SLEEPOVER` | 🌙 Sleepover | Friends sleep over together |
| `STICKER_COLLECTOR` | 💎 Magpie | A figure collects five trinkets |
| `STICKER_BLOOM` | 🌻 Green thumb | A plant grows all the way into bloom |
| `STICKER_STORM` | ⛈️ Stormy weather | Live through a thunderstorm |
| `STICKER_SNOWMAN` | ⛄ Do you want to build a snowman? | A snowman gets built |
| `STICKER_PET` | 🐾 Best friends | Adopt a pet |
| `STICKER_ADORED` | 💛 Adored | A figure adores you |
| `STICKER_NEMESIS` | 😠 Nemesis | A figure can't stand you |
| `STICKER_WELCOME` | 👋 Welcome back | Get welcomed back after time away |
| `STICKER_PAPER` | 📰 Hot off the press | Read the weekly paper |
| `STICKER_SEASONS` | 🍂 Turn of the season | See leaves, petals or fireflies |
| `STICKER_WALKIES` | 🦮 Walkies! | Take a dog for a good long walk |
| `STICKER_GROWNUP` | 🐾 All grown up | A kitten, puppy or chick grows up |
| `STICKER_CLEANUP` | 🧹 On poop patrol | Clean up after a pet |
| `STICKER_TRAINED` | 🎓 Good boy! | Train a pet out of a bad habit (75%) |
| `STICKER_PETTALK` | 🦜 Pretty bird | Teach a parrot a new word |
| `STICKER_PETMODE` | 🏡 Pet parent | Try pet-only mode |
| `STICKER_LITTER` | 🐣 Growing family | A litter is born |
| `STICKER_TRICK` | 🎪 Show-off | Teach a pet a trick |
| `STICKER_DRESSUP` | 🎀 Dress-up | Put clothes on a pet |
| `STICKER_BUILT` | 🔨 Master builder | A fort or treehouse gets built |
| `STICKER_SHOPPING` | 🪙 Retail therapy | A figure buys something with coins it earned |
| `STICKER_DREAM` | 💭 Sweet dreams | Catch a figure dreaming |
| `STICKER_RIDE` | 🚲 On a roll | A figure goes for a ride |
| `STICKER_SWIM` | 🏊 Making a splash | A figure goes for a swim |
| `STICKER_FISH` | 🎣 Gone fishing | A figure catches a fish |
| `STICKER_FESTIVAL` | 🎪 Festival! | Hold a town festival |
| `STICKER_TALENT` | 🎤 Star of the show | Win a talent show |
| `STICKER_RACE` | 🏁 Photo finish | See a race day through |
| `STICKER_REALWEATHER` | 🌦️ Same sky | Turn on your real weather |
## Stats for community goals

Each week has one goal everyone works on together (it rotates: fish, town events, focus minutes, requests, songs).
Create these stats in Steamworks → Stats & Achievements, each an **INT**, **Increment only**, with **Aggregated**
ticked (that's what lets the game read the community total and its daily history):

| API name | What counts | Daily cap per player |
| --- | --- | --- |
| `FISH_CAUGHT` | anything caught in a pond | 40 |
| `FESTIVALS_HELD` | town events started | 6 |
| `FOCUS_MINUTES` | finished focus minutes | 300 |
| `REQUESTS_DONE` | requests granted | 10 |
| `SONGS_SUNG` | songs sung on request | 10 |

The caps are applied in the game before anything reaches Steam, so nobody can run the week's goal up on their own.
When the week's total passes the target, every player who added something gets the **laurel wreath** hat.

## Friends' weekly boards

Opt-in (the player ticks "Share my week with Steam friends"). The game makes its own leaderboards with
`FindOrCreateLeaderboard` (allowed for clients by default): `W<yyyyww>_BIGGEST_FISH` (tenths of a cm),
`W<yyyyww>_FOCUS` (minutes) and `W<yyyyww>_DEX` (Doodledex entries), sorted descending, and only ever downloads
friends' entries. There is deliberately no global ranking.

## Trading cards, badges, emoticons and backgrounds

`Doodlefolk.exe --cardart <folder>` draws the whole set with the game's own renderer (the current set is in
`steam-art/`): ten 1920×1080 cards under 350 KB and their 206×184 versions, three profile backgrounds faded to black at
the sides and bottom, five level badges and a foil one (80×80), five emoticons (18×18 and 54×54), the 206×44 logo, and
`steam-community-items.txt` with every title and description to paste in. Steam turns trading cards on for a game once
it has enough players; the assets can be uploaded any time before that (Steamworks → Community → Trading Cards).
