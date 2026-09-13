# Existing Engine Hooks Relevant to Injury/Illness + Remedy Brewing

Factual inventory only — what the base game already has, with file:line citations into the
decompiled source (`VSDecompile/`). This is not a design for the new systems; it's the toolbox
a future design session should evaluate before inventing anything from scratch.

## Buff/debuff primitive: `EntityStats`

A named, source-keyed float-stat system already used for temporary and permanent modifiers
(e.g. from food, gear, effects).

- `VintagestoryAPI/Vintagestory.API.Common/EntityStats.cs:126` — `Set(category, code, value, persistent)`
- `VintagestoryAPI/Vintagestory.API.Common/EntityStats.cs:146` — `Remove(category, code)`
- `VintagestoryAPI/Vintagestory.API.Common/EntityStats.cs:164` — `GetBlended(category)`, blended by `EnumStatBlendType`
- `VintagestoryAPI/Vintagestory.API.Common/StatModifiers.cs` — existing stat categories: `walkSpeed`, `healingeffectivness`, `hungerrate`, `rangedWeaponsSpeed`, `rangedWeaponsAcc`, `canEat`

Each stat source is keyed by a string `code`, so multiple simultaneous effects (e.g. two
different remedies, or a remedy plus an affliction) can stack/blend without stomping each other,
and each can be independently removed when its own duration ends.

## Damage-over-time primitive: `EntityBehaviorHealth`

- `VSEssentials/Vintagestory.GameContent/EntityBehaviorHealth.cs:34-37` — `timeSinceLastDoTTickSec`, `timeBetweenDoTTicksSec`

A DoT tick loop already exists on the base health behavior — bleeding/poison/infection-style
damage over time is not something that needs to be built from zero.

## Hunger-adjacent "detox" precedent: `EntityBehaviorHunger`

- `VSEssentials/Vintagestory.GameContent/EntityBehaviorHunger.cs:27` — `detoxCounter` field, alongside `hungerCounter`, `SaturationLossDelayFruit`, `SaturationLossDelayVegetable`

Shows the base game already models a poison/toxin-adjacent counter tied into the hunger
behavior — evidence this general shape (a hidden counter influencing gameplay state) is an
established pattern in this codebase, not a novel approach.

## Full "affliction system" precedent: Temporal Stability

The closest existing analogue to a full illness/affliction system already shipped by the game:
a meter that drains, causes escalating debuffs and screen/visual effects, and is restored by
player action/items.

- `VSSurvivalMod/Vintagestory.GameContent/SystemTemporalStability.cs`
- `VSSurvivalMod/Vintagestory.GameContent/TemporalStabilityEffects.cs` — client rendering of effects (fog/rain overlay, slow-mo mode, glitch/warp effects), server tick loop, networked toggle packets
- `VSSurvivalMod/Vintagestory.GameContent/EntityBehaviorTemporalStabilityAffected.cs`
- `VSSurvivalMod/Vintagestory.GameContent/GetTemporalStabilityDelegate.cs`

Worth reading in full during the design session — it demonstrates the client/server sync
pattern, the tiered-effect pattern, and the renderer-hook pattern in one coherent, working
system already proven in this exact game.

## Data-driven "brewing" primitive: `BarrelRecipe`

- `VSSurvivalMod/Vintagestory.GameContent/BarrelRecipe.cs:11-56` — JSON-defined recipe: `Output`, `SealHours` (steep/seal time), `Ingredients[]`, `Matches(...)`, `TryCraftNow(...)`
- `VSSurvivalMod/Vintagestory.GameContent/BlockEntityBarrel.cs` — the container block entity that hosts sealed-recipe crafting over real time
- `VSSurvivalMod/Vintagestory.GameContent/BlockLiquidContainerBase.cs`, `BlockCrock.cs`, `BlockCookingContainer.cs`, `BlockEntityCookedContainer.cs` — sibling container/recipe patterns (cooking, cheese, pie) for comparison

This is already exactly "combine ingredients in a container, wait real time, get an output
item" — the core loop a brewing/apothecary system needs. Whether the new system reuses barrels
directly, subclasses the pattern, or is a new block entity modeled on it is a design-session
decision.

## Content is JSON-driven, not hardcoded

Block/item definitions, variant lists, and recipes all live in versioned JSON under
`assets/survival/` and `assets/game/` (see `flora/` files for the existing plant/fungus
inventory). Adding new ingredient items, or new recipes referencing existing behaviors, is
largely a content task; new C# is only needed for genuinely new mechanics (the illness meter
itself, any new block/item classes with novel behavior).

## Not investigated yet (flag for design session)

- Handbook/documentation integration (`[DocumentAsJson]` attributes appear throughout —
  relevant if the new items/effects should show up in the in-game handbook like vanilla ones).
- Trait/character-creation system, if illness susceptibility should hook into player traits.
- Whether any existing negative-status icon/HUD element could be reused for illness display,
  or whether that needs a new client-side UI element entirely.
