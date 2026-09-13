# Vintage Story Mod — Injury/Illness + Remedy Brewing (Ground-Breaking Notes)

Status: **pre-design research dump**. Nothing in this document commits to specific mechanics,
balance, data schemas, or class designs for the injury/illness system or the potion/remedy
crafting system — those are intentionally left generic, to be designed in a dedicated session
once the mod project scaffold exists. This file exists to hand that future session the context
it needs without re-deriving it.

Source of this research: the decompiled Vintage Story codebase at `VSDecompile/` (research-only,
read-only reference — see its `CLAUDE.md`) plus the installed game's `assets/` folder
(`C:\Users\<username>\AppData\Roaming\Vintagestory\assets`), cross-referenced with public
real-world herbal/ethnobotanical sources where noted.

## The two systems (scope, not design)

1. **Injury/illness system** — a status-effect layer distinct from raw HP loss: wounds,
   infections, poisonings, diseases, etc., each with its own state and progression, rather than
   everything collapsing into "health went down."
2. **Remedy/potion/poison crafting system** — a way to turn the game's existing (and possibly
   new) flora/fungi into consumable or appliable items that treat, mitigate, or (for poisons)
   cause the above conditions.

Neither system's internals are designed here. See `01-engine-hooks.md` for what already exists
in the base game that the future design session should evaluate reusing.

## Catalogued flora

See the `flora/` subfolder — one file per category, each pulled directly from the game's JSON
asset definitions (`assets/survival/blocktypes/plant/*.json` and
`assets/survival/worldproperties/block/*.json`) with real-world plant/fungus identification and
potential herbal relevance cross-referenced against the web where possible:

- `flora/flowers.md` — the `flower` and `flower-lupine` block groups, plus rafflesia, waterlily, croton
- `flora/herbs.md` — the `herb` block group (basil, chamomile, cilantro, lavender, marjoram, mint, saffron, sage, thyme)
- `flora/mushrooms.md` — the `mushroom` block group (43 named species) — **includes real-world toxicity flags, safety-critical**
- `flora/ferns-lichens-vines.md` — ferns, fern trees, lichens, hanging lichens, wild vines
- `flora/cacti-reeds-grasses.md` — cacti, reeds/sedges (cattails, tule, papyrus, brown sedge), bamboo, tallgrass, hay

These are the candidate ingredient pool for the remedy-brewing system. Not every entry needs a
gameplay effect — some are flagged toxic/no-known-use and might instead serve as poison
ingredients, junk/filler ingredients, or purely decorative with no system role.

### Tree leaves — investigated, not harvestable, not catalogued

Checked whether tree leaves (`assets/survival/blocktypes/plant/leaves/*.json`:
`normal.json`, `branchy.json`, `branchy-birch-static.json`, `narrow.json`, `fallen.json`,
`bamboo.json`) can be harvested as a distinct item, since that would make them a candidate
ingredient. They cannot:

- Breaking a leaf block's `drops` array only ever yields a small chance of `treeseed-{wood}`
  or `stick` — no leaf item is ever produced (`normal.json`, `branchy.json`,
  `narrow.json` all confirmed).
- No `item-leaf*` entry exists anywhere in `assets/game/lang/en.json`, and no leaf item type
  exists under `itemtypes/`.
- `BlockLeaves`/`BlockLeavesNarrow` in the decompiled source
  (`VSSurvivalMod/Vintagestory.GameContent/BlockLeaves.cs`,
  `BlockLeavesNarrow.cs`) add no drop/harvest override beyond that JSON `drops` array.
- The one alternate leaf block, `fallenleaves` (`leaves/fallen.json`), is explicitly
  `enabled: false` in this game version — decorative/unused content, not a fallback harvest path.

Conclusion: leaves are not a viable ingredient source as of game version 1.22.7 without adding
new game content (a new drop, or a new "gather leaves" interaction) — which would be a mod
change to the base game's blocks, not something this ingredient catalog can draw from as-is.

**Deferred idea, not current scope:** making trees drop a harvestable leaf item is possible
(new item + per-tree-type drop chance in the `leaves-*` block JSON, or a dedicated "gather
leaves" interaction), but it's an added item category on top of an already large ingredient
catalog (herbs, flowers, mushrooms, lichens, reeds). Treat as an optional later expansion, not
a prerequisite for the injury/illness or brewing systems — the existing catalog is enough to
start with.

## Reference: where this research came from

- Flora block definitions: `assets/survival/blocktypes/plant/`
- Flora variant name lists: `assets/survival/worldproperties/block/`
- Display names: `assets/game/lang/en.json`
- Game version at time of research: 1.22.7 (from `assets/version-1.22.7.txt`)
- Decompiled source cross-referenced: `VSDecompile/VintagestoryAPI`, `VSDecompile/VSEssentials`, `VSDecompile/VSSurvivalMod`

## Open questions for the future design session

(Not answered here on purpose — flagging so they aren't lost.)

- Does illness/injury live on a new `EntityBehavior`, or extend an existing one?
- Is severity continuous (a meter) or discrete (named stages), per `01-engine-hooks.md`'s
  precedents?
- Do poisons/toxins from the mushroom/flower list cause *illness-system* effects, or just
  vanilla damage/debuffs?
- Brewing vessel: reuse the barrel/crock system as-is, or introduce a new dedicated
  "apothecary" block?
- ~~Should misidentification (e.g. death cap vs. field mushroom) be a deliberate gameplay
  risk~~ — **decided: no.** Considered and rejected (see `flora/mushrooms.md`'s notes) because
  every item's exact name is visible on hover/in the handbook, so there's no real foraging
  ambiguity in this game for the mechanic to hook into.
- Multiplayer/save-compatibility plan for whatever new persistent state gets added.
