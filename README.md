# AutoSort — Vintage Story Mod

![AutoSort](logo.png)

Automatic chest sorting for Vintage Story. Drop everything into one chest, close it, and
AutoSort tidies each container internally **and** routes every item to the right chest
across your whole storage room.

**By [Los Albatros](https://github.com/Los-Albatros) — MIT License**

---

## Features

- **Sort on close.** Whenever a player closes a chest whose contents changed, the
  network is re-sorted. The trigger is idempotent: closing an already-sorted chest
  moves nothing, so nothing ever shuffles around for no reason.
- **Semantic grouping.** Items are classified into 17 categories (Weapon, Tool, Armor,
  Clothing, Food, Drink, Ingredient, Seed, Material, Pottery, Building, Fuel, Lighting,
  Mechanical, Medicine, Valuable, Other) and laid out top-to-bottom, left-to-right.
- **Variant clustering.** Every variant of an item is kept together (all gears next to
  each other, all planks, all fruit…), grouped by its base name regardless of tier or
  colour.
- **Cross-chest consolidation.** Items flow to the chest already holding the most of
  their kind, then overflow to the next when it is full — so one item type ends up in
  one chest instead of being scattered.
- **Room-aware (one virtual storage).** Starting from the chest you closed, the mod
  flood-fills the connected network of nearby containers and treats them as a single
  virtual inventory. With `RestrictToSameRoom` enabled (default), a **closed door**
  keeps two storage rooms separate.
- **Container types stay separate.** Chests/crates and storage vessels (jars) are sorted
  among their own kind and never mixed.
- **`/sort` overlay.** A read-only HUD that shows the contents of the container you are
  looking at — without opening it. It mirrors the real GUI (title, columns, layout),
  follows changes in real time, hides while a container is open, and remembers your
  preference across reconnects.

## Commands

| Command | Description |
|---|---|
| `/autosort` (alias `/sort`) | Toggle the read-only container overlay |
| `/autosort show` (or `on`, `enable`) | Show the overlay |
| `/autosort hide` (or `off`, `disable`) | Hide the overlay |

The overlay is per-player and persists across reconnects and server restarts.

## Installation

Download `autosort_0.1.0.zip` and drop it in the Vintage Story `Mods/` folder.

- **Server: required.** Installing the mod on the server enables the sorting for
  **everyone**, including players who don't have the mod. A config file `autosort.json`
  is generated under `ModConfig/` on first launch.
- **Clients: optional, needed for the overlay.** The `/sort` HUD is client-side, so
  **each player who wants it must also install the mod on their own client.** Players
  without it still benefit from the server-side sorting — they just won't see the overlay.

This is why the mod is **not** server-side only: the sorting is, but the overlay needs the
client.

## Configuration (`ModConfig/autosort.json`)

| Option | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `SearchRadiusBlocks` | `10` | Neighbourhood radius scanned around each chest. The network grows by cascade, so it can reach further than this from the origin. |
| `MaxNetworkChests` | `256` | Safety cap on how many chests one cascade may pull in. |
| `SpecialisationThreshold` | `0.70` | Fraction of a chest's items that must share a type for it to be considered a specialist for that type. |
| `MaxCascadeIterations` | `10` | Maximum number of global re-sort passes per trigger. |
| `RestrictToSameRoom` | `true` | Keep sorting within the origin chest's enclosed room (closed door separates rooms). Falls back to the full radius when the area is not an enclosed room. |
| `SupportedInventoryClasses` | `chest`, `largecrate`, `storagevessel` | Container inventory classes that get sorted. |
| `ContainerGroups` | `[chest, largecrate]`, `[storagevessel]` | Groups that never mix during distribution. |

## How it works

1. A player closes a container whose contents changed.
2. The mod flood-fills the connected network of same-group containers around it
   (optionally constrained to the enclosed room).
3. The whole network is treated as one inventory: items are consolidated to the chest
   already holding the most of their kind (capacity-aware, with overflow), and each
   touched chest is sorted internally.
4. Passes repeat until the layout is stable, then the changed containers are pushed to
   nearby clients so the overlay updates live.

## Building from source

```
dotnet build autoSortVintageStoryMod -c Release
dotnet test  autoSortVintageStoryMod.Tests
```

Requires the `VINTAGE_STORY` environment variable pointing at your game install (used to
resolve `VintagestoryAPI.dll` and `VSSurvivalMod.dll`). The classifier and distribution
logic are pure and covered by unit tests.

## License

MIT — see [LICENSE](LICENSE).
