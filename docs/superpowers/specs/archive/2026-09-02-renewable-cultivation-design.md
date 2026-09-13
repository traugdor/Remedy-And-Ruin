# Renewable Flora Cultivation — Design Spec

> **Archived — superseded.** Folded into `02-design-overview.md` (Part 5). This file is kept for
> history only; edit the master document instead.

Status: **approved design, not yet implemented**. Resolves the scarcity/renewability gap
flagged in `flora/conditions.md`'s Open follow-ups (wild-only ingredients like heather can be
over-harvested to local depletion, with no vanilla mechanic to regenerate them besides new chunk
generation) and in the original `00-mod-overview.md` research. Originally scoped against the
"Herbalist Pots" mod (https://mods.vintagestory.at/show/mod/41014) as prior art; this design
supersedes that reference with a shape grounded in this game's own existing mechanics instead.

## What's actually in vanilla today (checked, not assumed)

- **Vanilla's flowerpot/planter block entity (`BlockEntityPlantContainer`) is purely
  decorative.** `OnTick` is a literal empty method body — no growth logic exists at all. It also
  explicitly freezes spoilage/transition on its contents (`slotTransitionSpeed` returns `0f`).
  Confirms why a mod like Herbalist Pots needed to exist in the first place — there's no vanilla
  growth mechanic on the pot itself to build on.
- **A real, already-tuned, already-shipped regrowth mechanic exists elsewhere**:
  `BlockEntityBerryBush` (`VSSurvivalMod/Vintagestory.GameContent/BlockEntityBerryBush.cs`) runs
  a periodic tick (`RegisterGameTickListener(CheckGrow, 8000)`) cycling through named growth
  stages (flowering → unripe → ripe), harvestable once ripe, then resets. This is the pattern to
  crib the actual tick/state-machine shape from — proven and balanced in this exact game — not
  Herbalist Pots' code.
- **Room-based environment classification already exists and is reused across multiple vanilla
  systems**: `RoomRegistry.GetRoomForPosition(BlockPos)` (`VSEssentials/Vintagestory.GameContent/
  RoomRegistry.cs:286`) returns a `Room` with `SkylightCount`/`NonSkylightCount`/`ExitCount`.
  `BlockEntityBerryBush` itself uses this for a greenhouse temperature bonus
  (`greenhousetempbonus`); `ItemCheese` uses the same registry for cellar-aging detection
  (enclosed room + low light level). No "humidity"/"dampness" stat exists anywhere in the engine
  — every existing "damp/cool" mechanic in this game is actually approximated via
  enclosure + light level, not a literal moisture value.

## Decisions

### Extends vanilla's existing pots, not a new dedicated block

Growth logic is added to the flowerpot/planter blocks players already have and already know,
rather than introducing a separate "Cultivation Pot" block. Mechanically this means swapping in
a mod-provided `BlockEntity` subclass of `BlockEntityPlantContainer` (overriding the empty
`OnTick` with real growth logic) via a JSON patch changing the existing pot blocks' `class`
attribute — real new C#, and a point of potential conflict if another mod also tries to patch the
same block's class, worth flagging as an implementation-time compatibility risk rather than
deciding a mitigation here.

### Growth model: timed stages, berry-bush style

Not Herbalist Pots' escalating-weekly-chance model. Deterministic stages (e.g. Planted → Growing
→ Ready), each taking a fixed real/game-time duration, harvestable once Ready, then resets to
Growing (or back to Planted, TBD) — same shape as `BlockEntityBerryBush`'s
flowering/unripe/ripe cycle. Chosen specifically because it reuses a pattern already proven and
balanced in this game, rather than inventing new probability math with no in-game precedent.

### Growable scope: all flora without an existing growth mechanism, mushrooms included

Any flora currently a one-time static worldgen spawn (i.e., everything catalogued in
`flora/conditions.md`'s clusters and the "no known use" list — flowers, herbs, ferns, lichens,
cacti, reeds, mushrooms) qualifies, specifically *because* it has no existing regrowth mechanism
— crops and berry bushes already regrow on their own and aren't in scope here. Mushrooms are
explicitly included alongside the rest, not treated as out of scope, despite growing differently
in reality.

### Environment gating: three growing conditions, mushrooms restricted to one

Reuses the room classification already described above — no new detection code, just a new
consumer of `RoomRegistry`:

- **Outdoor** (unenclosed / no room) — general flora only.
- **Indoor / cellar-like** (enclosed, `NonSkylightCount` dominant — dark) — general flora, and
  the *only* environment mushrooms can grow in, as the best available proxy for "damp and out of
  direct light" given the engine has no literal humidity stat.
- **Greenhouse** (enclosed, `SkylightCount` dominant — bright) — general flora only, not
  mushrooms (too bright/dry by the same proxy logic).

General flora (non-mushroom) grows in any of the three conditions; mushrooms grow only in the
Indoor/cellar-like condition. This is an approximation of "mushrooms need damp, dark growing
conditions" using the closest thing the engine actually tracks, not a literal simulation of
humidity — worth stating plainly rather than implying a fidelity the engine can't back up.

## Settled since first draft

- **Harvest yield scales by pot size.** Flowerpot (`smallContainer`) doubles the plant — harvest
  gives 2 once fully grown. Planter (`largeContainer`) is more productive still — harvest gives
  4. The two sizes are not identical, resolving the earlier open question in favor of the
  planter being a genuine upgrade, not just a bigger version of the same yield.
- **Mod-compatibility risk is handled at the listing level, not in code.** If another mod is
  found to also patch `BlockEntityPlantContainer`'s block class, the resolution is to mark that
  mod as incompatible or risky on this mod's ModDB page — a documentation/support answer, not an
  in-game detection-and-fallback mechanism.
- **Greenhouse gives a real growth-speed bonus**, the same way it already does for berry bushes
  (the `greenhousetempbonus` mechanism `BlockEntityBerryBush` already uses, per the engine
  grounding above) — not just a pass/fail gate on top of Outdoor/Indoor/Greenhouse, greenhouse
  specifically speeds things up further.
- **Growth stage durations are twice as fast as vanilla berry bushes.** Checked the actual
  mechanism: `BlockEntityBerryBush.GetHoursForNextStage()` (`BlockEntityBerryBush.cs:215-221`)
  computes each stage's duration from a per-species `nextStageMonths` range converted through the
  calendar (`DaysPerMonth * HoursPerDay`), divided by a `growthRateMul` modifier — not a single
  hardcoded constant, config-driven per plant type. "Double speed" means this mod's cultivable
  flora use the same formula shape with either half the `nextStageMonths` value a comparable
  berry bush would use, or an equivalent ~2x `growthRateMul` — falls out of the existing formula
  rather than needing a new one, just different per-species tuning values.

This document's open items are now fully resolved — every question raised in the original draft
has a settled answer above.
