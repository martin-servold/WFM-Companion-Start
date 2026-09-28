# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Wildfrost mod ("Companion Start", GUID `fuko.wildfrost.companionstart`). It adds six new clans whose leader choices are the game's companions or its monsters and bosses, instead of the vanilla randomized human leaders. It's a C# class library (.NET Framework 4.7.2, old-style non-SDK csproj). It compiles against the game's own assemblies (`Assembly-CSharp`, `0Harmony`, `DeadExtensions`, Unity DLLs), which are referenced by absolute HintPaths into the local Steam install at `~/.local/share/Steam/steamapps/common/Wildfrost/Modded/Wildfrost_Data/Managed/`.

## Build

```
dotnet build "Companion Start/Companion Start.csproj"
```

- On Linux the csproj sets `FrameworkPathOverride` to `/usr/lib/mono/4.7.1-api`, so the mono reference assemblies must be installed.
- The post-build step runs `PostBuild.ps1` through `pwsh`. It copies `CompanionStart.dll` and `icon.png` into the game's mod folder (`.../Wildfrost_Data/StreamingAssets/Mods/companionstart`). It also mirrors the whole solution, minus bin/obj/.vs/.git, into that folder's `Source/` subfolder. So every build deploys straight into the local game install. The DLL is swapped in by renaming a temp copy. Overwriting it in place while the game runs breaks the loaded mod (`BadImageFormatException: Method has zero rva` from a Harmony `DMD<...>` frame), so if that error shows up, first check whether the DLL was rebuilt mid-session. Restart the game to load a new build.
- There are no tests and no linter. You verify changes by launching the modded game and playing. Use `Debug.Log` lines prefixed with `CompanionStart:` for diagnostics.
- New `.cs` files must be added as `<Compile Include=...>` in the csproj by hand, because non-SDK projects don't glob.
- Commits bump `AssemblyVersion`/`AssemblyFileVersion` in `Companion Start/Properties/AssemblyInfo.cs` (see git log: "..., bump version").

## Architecture

**Entry point: `CompanionStart.cs`** (`WildfrostMod` subclass). `Load()` does the setup and `Unload()` must undo all of it, since mods can be toggled at runtime. When you add a registration, event subscription or data mutation in `Load()`, add the matching reversal in `Unload()`.

- **Leader CardTypes**: `Friendly` and `Clunker` are cloned into `CompanionLeader`/`ClunkerLeader` CardTypes with `miniboss = true`. The miniboss flag is what the game actually uses to identify the leader (`References.LeaderData`, drain, etc.). Both clones also set `canReserve`/`canRecall` to false. `CardManager` builds its render-prefab pools before mods load, so the new types are aliased onto the source type's pools through reflection (`AliasCardRenderPool`).
- **Crown**: a stat-less `CardUpgradeData` of type `Crown` is assigned to every leader so it gets drawn and played first like vanilla leaders.
- **Clans**: `BuildLeaderClass` clones each of `Basic`/`Clunk`/`Magic` twice:
  - A `...Companions` clan, whose roster comes from that clan's Units reward pool, plus Clunker items, plus `SituationalCompanionNames`.
  - A `...Monsters` clan, whose roster comes from the hand-maintained `EnemyLeaderNames` and `BossLeaderNames` lists.

  The new clans are appended to `GameModeNormal.classes`.
- **`CloneAsLeader`**: clones each roster card and swaps in the leader CardType and crown. It also:
  - caps boss HP and strips phase-change effects
  - sets customData markers (`ClunkerLeaderMarker`, `TallLeaderMarker`)
  - always stamps `OverrideCardType`, because otherwise save/load reverts the card to its vanilla CardType.
- All added assets are named with `NamePrefix` (`fuko.wildfrost.companionstart.`). Other code identifies mod clans and cards by that prefix, by the customData markers, or by `cardType.miniboss`.

**Harmony patches (the other `*.cs` files)**: these are `[HarmonyPatch]` static classes. They are picked up automatically by the `WildfrostMod` base (there is no explicit `PatchAll` in this repo). Each one fixes a vanilla assumption that breaks once the leader is a companion or monster card. Examples: leaders fleeing (`LeaderCannotFleePatch`), eat-abilities rejecting miniboss self-targets (`EatSelfTriggerPatch`), tall two-slot bosses as leaders (`TallLeaderPatch`, which also hooks `Events.OnEntityMove`), and duplicate leader rewards. Some patches also mutate game data at load and have to restore it on unload.

**UI**:
- `LeaderSelectionPatch` + `GroupedLeaderGrid` replace the leader-select screen with a scrollable grid grouped by category, since rosters are far larger than the vanilla three.
- `TribeFlagGridPatch` lays out the tribe-select flags as a grid. It also suppresses the optional Extended UI mod's grid through a `Prepare()`-gated patch, so there's no hard dependency on that mod.
- `DarkModeFlagPatch` tints the new clans' flags using `CompanionStart.FlagTints`.

Comments in this codebase explain *why* a game-internal quirk forces each workaround. Keep that style when adding patches.
