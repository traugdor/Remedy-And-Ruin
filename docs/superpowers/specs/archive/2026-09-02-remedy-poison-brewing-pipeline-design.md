# Remedy & Poison Brewing Pipeline — Design Spec

> **Archived — superseded.** Folded into `02-design-overview.md` (Part 2). This file is kept for
> history only; edit the master document instead.

Status: **approved design, not yet implemented**. This spec covers the crafting/brewing
mechanics only. It deliberately does not define the illness/injury catalog (what a given
recipe's effect actually cures, or which flora treats which ailment) — see "Deferred / out of
scope" below. That catalog and the injury/illness status-effect system are separate future
design sessions, per `00-mod-overview.md`.

## Goals

- One data-driven crafting pipeline, shared by both remedies and poisons, built by reusing
  existing Vintage Story primitives (cookpot recipes, barrel sealing, boiler/condenser
  distillation, generic liquid-container drinking) rather than inventing new block types where
  an existing one already fits.
- A meaningful risk/reward curve: stronger tiers and riskier ingredient combos are more
  powerful but more dangerous, and the container you drink from is itself a choice between
  precision and risk.
- Content-authorable: adding a new remedy or poison is a matter of writing a recipe (ingredients
  + effect + side-effect profile), not writing new code.

## Non-goals (out of scope for this spec)

- The illness/injury status-effect system itself (what conditions exist, how they progress).
- The catalog of which specific flora cures/causes which specific condition.
- Balance numbers (exact potency values, exact toxicity thresholds, exact recipe ingredient
  lists). This spec fixes the *shape* of the data and mechanics; tuning happens during
  implementation/playtesting.

## Pipeline overview

```
Cookpot                       Barrel                  Distillery (Boiler+Condenser)
1L water                                               Pass 1              Pass 2 (optional)
+ 3 stacks of 6, any    →     Potion Base    →         Diluted Potion  →   Potion  →  Concentrated
mix of one cluster's           + 10L water               (unusable,          Potion     Potion
plants (18 units total)        sealed ~1 week            near-zero effect)
                                → Diluted Potion
```

Poisons run through the exact same three stages — a poison recipe is simply a recipe whose
output is tagged harmful instead of beneficial. There is no separate poison pipeline.

## 1. Cookpot — Potion Base

**Correction from an earlier draft of this spec:** the Cookpot is not modeled on `BarrelRecipe`
— that was a mistaken citation. The actual Cookpot recipe machinery is `CookingRecipe` /
`CookingRecipeIngredient` (`VSSurvivalMod/Vintagestory.GameContent/CookingRecipe.cs`,
`CookingRecipeIngredient.cs`), the same class vanilla uses for soup/stew recipes ("any
vegetable" style categories). This turns out to matter a lot, because it already natively
supports the ingredient-substitution model below with zero new engine code.

- Slot 1 is always exactly 1L water.
- **One recipe = one cluster** (see `flora/conditions.md`), not one recipe per specific
  ingredient combination. Each remedy/poison recipe defines a single `CookingRecipeIngredient`
  category — e.g. "Wound Care plant" — whose `ValidStacks[]` list is exactly that cluster's
  ingredient list (cattails, old man's beard, wild daisy, woad, sage, thyme, heather,
  hart's-tongue fern, devil's tooth, …), each entry at `StackSize: 6`.
- **All 3 plant-stack slots must always be filled** — no longer the earlier "0–3 slots, some
  recipes need only 1" rule from a prior draft; that's superseded. The category's
  `MinQuantity`/`MaxQuantity` are both set to 3 (a count of *qualifying stacks*, not raw units —
  `CookingRecipe.Matches()` tallies one match per input stack, `CookingRecipe.cs:65-144`), so
  exactly 3 stacks of 6 units each are required, 18 units total.
- **Any mix of that cluster's members satisfies the 3 stacks**, independently per stack — 3
  stacks of the same plant (18× cattails), or a mix (2×cattails + 1×heather), match the same
  recipe identically, because `CookingRecipe.Matches()` only checks that each input stack
  satisfies *some* valid entry in the category, not which specific one. This is exactly how
  vanilla already lets "any vegetable" satisfy a soup's vegetable slot — nothing new to build for
  this part.
- **Side-effect severity scales with how many distinct plant species were used**, matching
  §6's interaction-driven design: 18× cattails alone carries less side-effect risk than
  2×cattails + 1×heather, even though both produce the same Wound Care Potion Base. This *is*
  new mod logic layered on top of the native match — `CookingRecipe.Matches()` itself doesn't
  track distinct-ingredient-count, only aggregate stack counts per category, so after a match
  succeeds the mod counts the distinct item codes among the qualifying input stacks and scales
  the resulting Potion Base's stored side-effect severity attribute accordingly.
- Recipes only match within a single cluster — mixing plants from two different clusters across
  the 3 slots (e.g. cattails + basil) doesn't satisfy any one recipe's category and falls through
  to Sludge, same as before.
- Any ingredient combination that does **not** match an authored recipe produces **Sludge** — a
  junk/filler item, not an error or a wasted-ingredients failure state. This also means
  ingredients with no medicinal (or poisonous) properties can still safely go in the pot without
  a special-cased rejection. **Sludge is pure waste, confirmed**: not edible, and not a valid
  input anywhere in the Barrel stage (§2) — mixing clusters doesn't produce a weaker or
  off-brand remedy, it's a dead end with no further use.
- **Potion Base (and Sludge) start spoiling immediately, not on a delay from crafting.** Roughly
  2 in-game hours fresh before spoilage begins, same `TransitionableProperties`-style "Perish"
  shape used throughout vanilla (freshHours, then a transition window toward `rot`) — this is a
  short fuse relative to the Barrel's ~1-week dilution steep, so a Potion Base can't be crafted
  and stockpiled for later; it has to go into the Barrel promptly.
- Output: **Potion Base**, a discrete item (not a liquid) consumed by the barrel stage.
- **The Potion Base itself is edible**, not only a brewing intermediate — eating it directly
  applies the same effect as drinking the fully processed Potion, just for a much shorter
  duration. Processing it through the Barrel and Distillery (§2, §3) is what makes the effect
  last; skipping straight to eating the boiled mash is the "I need this right now and don't care
  that it won't last" option, not a strictly worse alternative to be balanced away.

## 2. Barrel — Dilution

- 1x Potion Base + 10L water, sealed in a barrel, at a fixed 1:10 ratio.
- Real-time steep duration, default ~1 week, defined per-recipe (same `SealHours` field as
  `BarrelRecipe`, so different remedies/poisons can have different steep times).
- Output: 10L of **Diluted Potion** liquid.
- Batches scale: barrels hold up to 50L, and `BarrelRecipe`'s existing output-scaling logic
  (`GetOutputSize()`, `BarrelRecipe.cs:239-272`) already multiplies the recipe by however many
  ingredient-stack multiples are present — so 5x Potion Base + 50L water in one barrel yields
  50L of Diluted Potion in a single steep, at the same 1:10 ratio, with no new scaling logic
  needed. A full barrel is simply the efficient way to brew a large batch in one week instead of
  five separate 10L batches.
- Diluted Potion is intentionally not a usable end product — its effect (and side effect) is
  negligible. It exists purely as the distillery's input; a player who drinks it straight is
  wasting the batch, not exploiting a shortcut.

## 3. Distillery — Boiler + Condenser

Reuses the vanilla spirits-distillation apparatus almost unmodified:
`VSSurvivalMod/Vintagestory.GameContent/BlockEntityBoiler.cs` (heats a liquid container, and
once its temperature crosses 75°C, evaporates it at a rate driven by a `distillationProps`
item attribute — `DistillationProps.Ratio` / `DistillationProps.DistilledStack`, see
`DistillationProps.cs`) feeding an adjacent
`VSSurvivalMod/Vintagestory.GameContent/BlockEntityCondenser.cs`, which condenses the vapor into
a bucket-like container.

- **Pass 1**: Diluted Potion boiled and condensed → **Potion**. Volume drops; this is the first
  usable tier — real primary effect, real (but modest) side-effect risk.
- **Pass 2 (optional)**: re-boil the Potion output through the same boiler+condenser pair →
  **Concentrated Potion**. Volume drops further; strongest primary effect, strongest side
  effects, and raises the toxicity counter (§5) faster per dose than Potion does.
- Each recipe defines its own `DistillationProps`-style ratio per pass, so different
  remedies/poisons can concentrate at different rates — same mechanism vanilla already uses to
  differentiate spirit types.
- There is no separate "advanced distillery" block gating Concentrated Potion — it's the same
  apparatus, run twice.

## 4. Dosing & consumption

No dedicated "drink this specific item" action beyond what `BlockLiquidContainerBase` already
provides (`VSSurvivalMod/Vintagestory.GameContent/BlockLiquidContainerBase.cs`). Drinking is the
existing generic "drink from any compatible liquid container" interaction
(`CanDrinkFrom` + `DrinkPortionSize`, line 37/742), which already works uniformly across bowls,
jugs, buckets, waterskins, and any third-party mod's container that subclasses
`BlockLiquidContainerBase` — no new drinking mechanic needs to be written.

`DrinkPortionSize` is a **per-container** attribute (`Attributes["drinkPortionSize"]`, default
1L if unset, `BlockLiquidContainerBase.cs:137-148`), not a property of the liquid. This mod adds
one new container to exploit that:

- **Vial** (new item/block added by this mod): `capacityLitres: 0.25`, `drinkPortionSize: 0.25`.
  Holds exactly one dose, dispenses exactly one dose per drink. The "precise" way to take a
  potion. Unlike the balance values above, the vial isn't JSON-only — it's a genuinely new block
  with no vanilla shape to reuse, so it needs its own model authored in VSMC (Vintage Story
  Model Creator) and referenced from its shape JSON, the same as any other new block added by
  this mod.
- **Generic containers** (bowl, jug, bucket, other mods' containers): use their own existing
  `drinkPortionSize`, vanilla default 1L. Drinking a potion straight from one of these consumes a
  full 1L per gulp — **4 doses at once** — spiking the toxicity counter accordingly.

One dose = 0.25L, fixed regardless of tier (Potion vs. Concentrated Potion) — the tier changes
*potency per dose*, not dose size.

## 5. Overdose / toxicity

- A decaying per-player toxicity counter, structurally modeled on
  `EntityBehaviorHunger.detoxCounter`
  (`VSEssentials/Vintagestory.GameContent/EntityBehaviorHunger.cs:27`) — a hidden float that
  rises on consumption and bleeds off over time rather than resetting on a hard cooldown.
- Each dose consumed raises the counter by an amount defined per recipe/tier:
  - Potion dose: baseline increase.
  - Concentrated Potion dose: larger increase than a Potion dose (exact multiplier is a tuning
    value, not fixed here).
- Crossing a threshold triggers overdose symptoms (symptom definition itself is out of scope —
  see Deferred, below; this spec only commits to the counter/threshold mechanism).
- Drinking from a generic container (1L = 4 doses at once) raises the counter by 4x a single
  dose's worth in one action, independent of decay — this is what makes the vial the safer
  choice for anything with meaningful toxicity per dose.

## 6. Side effects & ingredient interactions

**Superseded by §1's cluster-based matching:** the original design here (below, struck through
in spirit) assumed one recipe = one specific, curated ingredient combination, which made
authoring a bespoke side-effect profile per combination affordable. §1 replaced that with one
recipe = one *cluster*, matchable by any mix of that cluster's members across the 3 required
stacks — which means the same recipe can now be satisfied by many different specific
combinations (any selection of 3 stacks, with repeats allowed, from however many members a
cluster has). Authoring a bespoke interaction profile per exact combination is no longer bounded
the same way, so it's replaced by a simpler rule:

- **Side-effect severity scales with the number of *distinct* plant species used**, not with
  which specific species. 3 stacks of the same plant (e.g. 18× cattails) is the safest way to
  brew a given remedy; using 2 or 3 different cluster members for the same recipe raises the
  side-effect severity, regardless of which specific members were chosen. This is the "distinct
  ingredient count" logic introduced in §1.
- This still isn't purely additive in the naive sense — severity scales with *variety*, not with
  literally summing N independent per-ingredient side-effect entries — but it's no longer a
  bespoke authored table per combination either. A cluster doesn't need per-pair or per-triple
  authored interaction data; the severity curve (e.g. 1 species → baseline, 2 species → +X%, 3
  species → +Y%) is authored once per recipe (or even once globally, with per-recipe
  overrides where a specific cluster warrants a steeper or gentler curve).
- Side-effect magnitude also scales with tier the same way the primary effect does: Potion has a
  modest side-effect profile, Concentrated Potion has a stronger one, independent of and additive
  with the variety-based scaling above.

## 7. Poison track

- Identical pipeline to remedies (Cookpot → Barrel → Distillery), no separate blocks or steps.
- A poison recipe is a Cookpot recipe like any other, just tagged as producing a harmful output
  instead of a beneficial one, and carrying a harmful "primary effect" instead of a curative one.
- Concentrated Poison is the strongest/most dangerous tier, mirroring Concentrated Potion.
- Poison doses presumably also interact with the toxicity/overdose system when drunk (accidental
  or deliberate ingestion), though the poison-specific effect itself (damage type, DoT shape) is
  deferred along with the rest of the effect catalog.

## 8. Arrow poisoning

Both application methods are supported, mirroring the dual interaction pattern already used by
fishing rods/bait in the base game:

- **Dip**: right-click a stack of arrows against a poison-filled container to coat them
  directly, producing a poisoned-arrow item variant.
- **Craft**: a conventional grid recipe consuming arrows + a portioned poison (e.g. a vial of
  poison) to output poisoned arrows.

Poisoned arrows apply the poison's effect on hit. The effect delivery mechanism itself is the
same deferred `ApplyEffect` hook as everything else (§9).

## 9. Effect delivery — interface only, deferred content

This spec commits to *how* an effect gets hooked into the game, not to *what* any specific
effect does. The hook point is a generic call — conceptually `ApplyEffect(entity,
recipeEffectData)` — invoked on dose consumption or on-hit (arrows), backed by two existing
engine primitives:

- `EntityStats` (`VintagestoryAPI/Vintagestory.API.Common/EntityStats.cs`) — named, source-keyed
  float-stat buffs/debuffs (`Set`/`Remove`/`GetBlended`), so multiple simultaneous potion/poison
  effects can stack or blend without stomping each other and can be independently removed when
  their duration ends.
- `EntityBehaviorHealth`'s DoT tick loop
  (`VSEssentials/Vintagestory.GameContent/EntityBehaviorHealth.cs:34-37`) — for damage-over-time
  poisons/toxins.

The actual catalog of effects (what a "wound care" remedy does numerically, what a given poison's
DoT looks like, what overdose symptoms are) is explicitly deferred to the future illness/injury
design session referenced in `00-mod-overview.md`.

## Data model sketch

Not a final schema — illustrates the shape implied by the decisions above, for the
implementation-planning pass to refine:

```jsonc
// Cookpot recipe (potion base) — CookingRecipe shape, per §1's correction
{
  "code": "remedy-wound-care-base",
  "ingredients": [
    {
      "code": "water",
      "minQuantity": 1,
      "maxQuantity": 1,
      "validStacks": [ { "code": "water", "stackSize": 1, "portionSizeLitres": 1 } ]
    },
    {
      "code": "woundcareplant",
      "minQuantity": 3,
      "maxQuantity": 3, // exactly 3 qualifying stacks required, any mix of the cluster below
      "validStacks": [
        { "code": "tallplant-coopersreed-*", "stackSize": 6 }, // cattails
        { "code": "hanginglichen-oldmans-*", "stackSize": 6 }, // old man's beard
        { "code": "flower-wilddaisy-*", "stackSize": 6 },
        { "code": "flower-woad-*", "stackSize": 6 },
        { "code": "herb-sage-*", "stackSize": 6 },
        { "code": "herb-thyme-*", "stackSize": 6 },
        { "code": "flower-heather-*", "stackSize": 6 },
        { "code": "fern-hartstongue", "stackSize": 6 },
        { "code": "mushroom-devilstooth-*", "stackSize": 6 }
        // full list = the Wound Care / Antimicrobial cluster, flora/conditions.md §1
      ]
    }
  ],
  "output": { "code": "potionbase-woundcare" },
  "sideEffectSeverityByDistinctIngredientCount": { "1": 1.0, "2": 1.3, "3": 1.6 } // illustrative, not balanced
}

// Barrel recipe (dilution)
{
  "code": "remedy-wound-care-dilute",
  "ingredients": [
    { "code": "potionbase-woundcare", "quantity": 1 },
    { "code": "water", "litres": 10 }
  ],
  "sealHours": 168, // ~1 week, tunable per recipe
  "output": { "code": "potion-dilutedwoundcare", "litres": 10 }
}

// Distillation props (per-liquid item attribute, both passes use this shape)
{
  "distillationProps": {
    "ratio": 0.5,
    "distilledStack": { "code": "potion-woundcare" } // pass 2 input becomes pass 2 output "potion-woundcare-concentrated"
  }
}

// Effect + side-effect profile (attached to the finished Potion / Concentrated Potion item)
{
  "primaryEffect": { /* deferred — illness-system hook data */ },
  "sideEffectProfile": {
    "potion": [ /* deferred — authored per-recipe interaction entries */ ],
    "concentrated": [ /* stronger version of the same entries */ ]
  },
  "toxicityPerDose": { "potion": 1.0, "concentrated": 2.5 } // illustrative, not balanced
}
```

## Engine hooks this design reuses (no new mechanics required for these)

- Cluster/category recipe matching ("any of a list satisfies this slot") — `CookingRecipe.cs`,
  `CookingRecipeIngredient.cs` (corrected from an earlier, mistaken `BarrelRecipe.cs` citation).
- Sealed real-time steeping — `BlockEntityBarrel.cs` (barrel dilution stage still uses
  `BarrelRecipe`, that citation was correct for §2, just not for §1's Cookpot stage).
- Boiler/condenser distillation — `BlockEntityBoiler.cs`, `BlockEntityCondenser.cs`,
  `DistillationProps.cs`.
- Generic "drink from any container" — `BlockLiquidContainerBase.cs`.
- Buff/debuff stacking — `EntityStats.cs`.
- Damage-over-time — `EntityBehaviorHealth.cs`.
- Decaying counter precedent — `EntityBehaviorHunger.cs` (`detoxCounter`).

New C# is only needed for: the toxicity/overdose counter itself, the `ApplyEffect` hook point,
the distinct-ingredient-count side-effect severity scaling (§6, layered on top of a native
`CookingRecipe` match), and the Vial item/block (a straightforward `BlockLiquidContainerBase`
subclass/config, likely requiring no new class at all — see `01-engine-hooks.md`'s note that
content is JSON-driven).

## Deferred / explicitly out of scope

**This list was stale — three items below were resolved by later docs and are corrected here
rather than left contradicting them.**

- ~~The illness/injury status-effect system~~ — **resolved**: see
  `docs/superpowers/specs/2026-09-02-illness-poison-effects-design.md` and
  `docs/superpowers/specs/2026-09-02-broken-bones-injury-design.md`.
- ~~The full effect catalog~~ — **resolved**: see the illness/poison effects spec above.
- ~~Misidentification-risk mechanic~~ — **resolved: considered and explicitly rejected**, see
  `flora/mushrooms.md`'s notes (the game shows exact item names on hover, no real foraging
  ambiguity to hook a mechanic into).
- Which specific flora (from `flora/*.md`) map to which specific recipes — ingredient catalog
  exists, recipe-to-effect assignment does not. Still open — this is the project's single
  biggest remaining structural gap, see `02-design-overview.md`.
- Exact balance numbers: toxicity thresholds, per-recipe toxicity-per-dose values, distillation
  ratios, steep durations, side-effect magnitudes. Still open.
- Handbook/documentation integration for the new recipes/items — deliberately sequenced after
  mechanics are settled, not forgotten; see the illness/poison effects spec's deferred list.
- Multiplayer/save-compatibility specifics for the new toxicity counter and any new block
  entities. Still open, unrevisited since first flagged.

See `02-design-overview.md` for the consolidated, currently-accurate view across all specs —
check there first rather than this list alone.
