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
- **Spread layout ("valence", default).** The room's containers are laid out from a
  stable reference (the room's door, else a fixed corner — the same whichever chest you
  close). Each item kind gets its own chest, spreading across as many chests as the room
  offers; when there are more kinds than chests, chests take 2, then 3… kinds (balanced,
  like electron shells). No empty chest is ever left between filled ones.
- **Dense compaction (optional).** Set `CompactRoom = true` to instead pack everything
  into the fewest chests.
- **Room-aware (one virtual storage).** The mod floods the room's open space from the
  chest you closed and treats every container bordering it as one virtual inventory. The
  flood stops at **walls, doors and stairs**, so a fridge/cellar behind its door stays
  separate from the outside, while a whole hall of any size is covered. An open or
  oversized area (the flood never closes off) sorts only the chest you closed — so a
  carried (Carry-On) container or a dungeon chest won't dump items into nearby containers.
- **Floor separation.** Solid floor/ceiling layers and stairs keep storeys apart, so a
  ladder or stairwell no longer merges two floors into one network.
- **Sorts once when you're done.** Closing several chests in a row triggers a single
  room sort after a short debounce, not one heavy sort per chest.
- **Container types stay separate.** Chests/crates and storage vessels (jars) are sorted
  among their own kind and never mixed.
- **Read-only containers are left alone.** Collapsed/ruined trunks and any retrieve-only
  loot container (where you can't place items) are never touched — items are never pushed
  into them. Extra codes can be added via `IgnoredContainerCodes`.
- **Backpack sorting (opt-in).** Enable `SortPlayerBackpack` to tidy a player's backpack
  content when they close their inventory — bag slots and the hotbar are left untouched.
  Off by default.
- **Multiplayer-safe.** When several players or chests in a room are open, only the last
  one closed triggers the sort, so items never shuffle while someone is editing.
- **`/sort` overlay.** A read-only HUD that shows the contents of the container you are
  looking at — without opening it. It mirrors the real GUI (title, columns, layout),
  follows changes in real time, hides while a container is open, and remembers your
  preference across reconnects.
- **In-game settings (ConfigLib).** If the [ConfigLib](https://mods.vintagestory.at/configlib)
  mod is installed, AutoSort adds a settings screen: a `/sort` overlay switch for every
  player, plus an admin section to tune the sorting and edit the sorted container list
  (with server-side auto-discovery of modded containers). Available in English and French.

## Commands

| Command | Description |
|---|---|
| `/autosort` (alias `/sort`) | Toggle the read-only container overlay |
| `/autosort show` (or `on`, `enable`) | Show the overlay |
| `/autosort hide` (or `off`, `disable`) | Hide the overlay |

The overlay is per-player and persists across reconnects and server restarts.

## Tips

- **Need a "scratch" container that won't be sorted into your shelves?** Use a single
  container of a *different type* than your storage (e.g. one jar in a chest room, or one
  chest in a jar room). Since each container type is sorted only among its own kind, a
  lone container of another type has no peers to send items to — so nothing leaves it.
- Sorting only happens **inside an enclosed room** by default, so a chest you open while
  exploring (or a Carry-On container in the open) won't push its items anywhere.

## Installation

Download `autosort_0.1.2.zip` and drop it in the Vintage Story `Mods/` folder.

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
| `CompactRoom` | `false` | `false` = spread "valence" layout (one item kind per chest). `true` = dense packing into the fewest chests. |
| `SeparateFloors` | `true` | Treat chests separated by a solid floor/ceiling layer as different storeys. |
| `MaxVerticalSpan` | `0` | Optional hard cap on the vertical distance a chest may be from the trigger (0 = off, rely on `SeparateFloors`). |
| `SortPlayerBackpack` | `false` | Sort a player's backpack content on inventory close (bag slots and hotbar untouched). |
| `SortDebounceMs` | `1500` | Delay after the last container closes before the room is sorted (one sort per session, not per chest). |
| `MaxRoomCells` | `10000` | Cap on the room-flood size; beyond it the area is treated as open and only the closed chest is sorted. |
| `SearchRadiusBlocks` | `10` | Radius used by the fallback cascade when the room flood can't seal a space. |
| `MaxNetworkChests` | `1024` | Safety cap on how many chests one sort may pull in. |
| `IgnoredContainerCodes` | `collapsed` | Block-code substrings of containers to never touch. Retrieve-only loot containers are ignored automatically on top of this. |
| `SupportedInventoryClasses` | `chest`, `largecrate`, `storagevessel` | Container block kinds that get sorted (editable in-game via ConfigLib). |
| `ContainerGroups` | `[chest, largecrate]`, `[storagevessel]` | Groups that never mix during distribution. |

## How it works

1. A player closes a container. (If several containers in the room are open, only the
   last one closed triggers the sort, so items never move while someone is still editing.)
2. The mod flood-fills the connected network of same-group containers in the room,
   skipping read-only/ignored containers and anything on another storey.
3. The whole network is pooled into one inventory, sorted, and laid back out from a stable
   anchor — spread across chests ("valence") or packed densely (`CompactRoom`). The layout
   is deterministic, so re-sorting an already-sorted room moves nothing.
4. The changed containers are pushed to nearby clients so the overlay updates live.

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
