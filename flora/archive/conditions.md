# Flora Cross-Reference: Remedy and Poison Clusters

> **Archived — superseded.** Folded into `02-design-overview.md` (Part 1). This file is kept for
> history only; edit the master document instead.

Source: derived bottom-up from the real-world herbal-use notes already recorded in
`flora/herbs.md`, `flora/flowers.md`, `flora/mushrooms.md`, `flora/ferns-lichens-vines.md`, and
`flora/cacti-reeds-grasses.md`. No illness/injury system design exists yet (see
`00-mod-overview.md`), so this file still doesn't assign specific effects/potency numbers — but
as of the brewing pipeline spec's §1 correction, **each cluster below now doubles directly as one
Cookpot recipe's ingredient category**: every ingredient listed under a cluster is one entry in
that recipe's `CookingRecipeIngredient.ValidStacks[]`, all interchangeable within the recipe's 3
required stacks (any mix, `docs/superpowers/specs/2026-09-02-remedy-poison-brewing-pipeline-design.md`
§1). This file is no longer a step removed from actual recipe data — it more or less *is* the
recipe ingredient lists, modulo the exact effect/potency values still to be assigned.

**How this ties into the brewing pipeline:** per that same §1, a recipe only matches within its
own cluster — plants from two different clusters mixed across the 3 stacks satisfy no recipe and
fall through to Sludge. Every ingredient listed under "No known remedy or poison use" below is,
by construction, always a Sludge ingredient: it belongs to no cluster, so no recipe would ever be
authored around it.

**A recipe's brewed effect is still authored on its own terms, not derived from an ingredient's
raw-eaten health value** — a Wound Care Potion's effect doesn't get computed from its cluster
members' raw `health` numbers, and that stays true. What *has* changed since this note was first
written: the raw-eaten `health` values themselves (see the in-game values noted throughout the
poison clusters below, e.g. death cap `-50`, funeral bell `-40`) are no longer instant damage —
per `docs/superpowers/specs/2026-09-02-illness-poison-effects-design.md`'s "raw-food penalties"
section, they now apply as a timed delay followed by a DoT, specifically so the Universal
Antidote can matter for raw/cooked mushroom poisoning too. See that doc for the full reasoning
and the cheese-tactic risks that decision was checked against.

**Harvestability caveat:** being catalogued here doesn't guarantee an ingredient is actually
obtainable. Confirmed example: `tallgrass-*` drops `drygrass` on break, never a `tallgrass-*`
item itself (`assets/survival/blocktypes/plant/tallgrass.json:145-151`, tool `knife`/`scythe`) —
same non-harvestable pattern `00-mod-overview.md` already found for tree leaves. Other
borderline entries below are marked low-confidence where the real-world species ID itself is
uncertain, but their harvestability as an item has not been individually re-verified here.

## Remedy clusters

### 1. Wound Care / Antimicrobial

| Ingredient | Block code | Note |
|---|---|---|
| Cattails | `tallplant-coopersreed-*` | TCM hemostatic use (pollen, "Pu Huang"); Native American antiseptic stem juice; wound-bandage fluff |
| Old man's beard | `hanginglichen-oldmans-*`, `lichen-oldmansbeard-up` | Usnic acid, well-documented traditional antimicrobial, wound cleaning |
| Wild daisy | `flower-wilddaisy-*` | Arnica-like, bruises/wounds, some modern wound-healing study support |
| Woad | `flower-woad-*` | Ancient antiseptic/wound remedy; root in European Pharmacopoeia today |
| Sage | `herb-sage-*` | Mild antiseptic, sore throat gargle |
| Thyme | `herb-thyme-*` | Antiseptic |
| Heather | `flower-heather-*` | Poultices for skin and wound healing |
| Hart's-tongue fern | `fern-hartstongue` | Medieval herbalist use for wounds |
| Devil's tooth | `mushroom-devilstooth-*` | Weak candidate — antimicrobial compound research (atromentin), not edible |

### 2. Digestive / GI

| Ingredient | Block code | Note |
|---|---|---|
| Basil | `herb-basil-*` | Digestive aid |
| Cilantro | `herb-cilantro-*` | Digestive aid |
| Marjoram | `herb-marjoram-*` | Digestive and mild antispasmodic |
| Mint | `herb-mint-*` | Digestive aid, nausea relief |
| Chamomile | `herb-chamomile-*` | Mild digestive soother |
| Mugwort | `flower-mugwort-*` | Digestive bitter |
| Hart's-tongue fern | `fern-hartstongue` | Liver/spleen/digestive complaints |

### 3. Calming / Sedative / Sleep

| Ingredient | Block code | Note |
|---|---|---|
| Catmint | `flower-catmint-*` | Sedative/antispasmodic for anxiety, insomnia |
| Chamomile | `herb-chamomile-*` | Classic calming/sleep-aid tea |
| Lavender | `herb-lavender-*` | Calming/sleep aid |
| Ghost pipe | `flower-ghostpipewhite/pink/red-*` | Nervine/sedative/antispasmodic |
| Water lily | `block-waterlily` | Mild sedative, sleep-related folk use |
| Mugwort | `flower-mugwort-*` | Dream work/moxibustion tradition |
| Golden poppy | `flower-goldenpoppy-*` | **Low confidence** species ID — mild non-narcotic sedative if correct |

### 4. Respiratory / Cough / Cold

| Ingredient | Block code | Note |
|---|---|---|
| Catmint | `flower-catmint-*` | Traditional cough remedy |
| Edelweiss | `flower-edelweiss-*` | Alpine folk remedy for respiratory complaints |
| Thyme | `herb-thyme-*` | Cough/respiratory remedy |
| Wild daisy | `flower-wilddaisy-*` | Traditional cough remedy |
| Hart's-tongue fern | `fern-hartstongue` | Coughs, fever |
| Cinnamon fern | `fern-cinnamonfern` | Colds, chills |
| Bamboo | `bamboo-*` | TCM respiratory/anti-inflammatory use (cooked) |
| Woad | `flower-woad-*` | Traditional fever remedy |
| Orange mallow | `flower-orangemallow-*` | **Low confidence** species ID — mallow-family demulcent for sore throat if correct |

### 5. Pain relief / Analgesic

| Ingredient | Block code | Note |
|---|---|---|
| Mint | `herb-mint-*` | Cooling/analgesic topical use |
| Ghost pipe | `flower-ghostpipewhite/pink/red-*` | Modern herbalist tincture use for pain |
| Cattails | `tallplant-coopersreed-*` | Analgesic use of stem juice |
| Golden poppy | `flower-goldenpoppy-*` | **Low confidence** species ID — mild analgesic if correct |

### 6. Skin / Topical Inflammation

| Ingredient | Block code | Note |
|---|---|---|
| Cornflower | `flower-cornflower-*` | Folk eyewash/astringent for eye and skin inflammation |
| Horsetail | `flower-horsetail-*` | High-silica remedy for skin, hair, nail, connective tissue |
| Chamomile | `herb-chamomile-*` | Mild skin soother |
| Edelweiss | `flower-edelweiss-*` | Modern antioxidant skincare use |

### 7. Immune / Tonic

| Ingredient | Block code | Note |
|---|---|---|
| Reishi | `mushroom-reishi-*` | Major TCM immune/sleep/stress/liver-protective mushroom |
| Shiitake | `mushroom-shiitake-*` | Immune-supporting (lentinan) |
| Lion's mane (bearded tooth) | `mushroom-beardedtooth-*` | Neuroprotective/cognitive research |
| Chicken of the woods | `mushroom-chickenofthewoods-*` | Antioxidant properties studied |
| Almond mushroom | `mushroom-almondmushroom-*` | Immune-modulating polysaccharides studied |
| Tinder hoof | `mushroom-tinderhoof-*` | Immune/antiviral research (also historic wound-cauterization use) |
| White oyster | `mushroom-whiteoyster-*` | Cholesterol-lowering studies — weaker general-health candidate |
| Saffron | `herb-saffron-*` | Mood-lifting, antioxidant |
| Tapir's liver | `mushroom-livermushroom-*` | Vitamin C-rich, traditional tonic |

### 8. Cognitive / Nerve support

| Ingredient | Block code | Note |
|---|---|---|
| Lion's mane (bearded tooth) | `mushroom-beardedtooth-*` | Nerve growth factor research |
| Sage | `herb-sage-*` | Traditional memory/cognitive folk use |

### 9. Urinary

| Ingredient | Block code | Note |
|---|---|---|
| Heather | `flower-heather-*` | Infusions for urinary complaints |
| Cattails | `tallplant-coopersreed-*` | Diuretic use (TCM pollen) |
| Cow parsley | `flower-cowparsley-*` | **Caution** — occasionally used for kidney/urinary stones, but closely resembles poisonous hemlock; misidentification risk, not a clean pick |

## Poison clusters

### 1. Deadly hepatotoxic (amatoxin)

| Ingredient | Block code | Note |
|---|---|---|
| Death cap | `mushroom-deathcap-*` | Most fatal mushroom species worldwide, severe liver damage. **In-game: `health: -50`** (`mushroom.json:91`) — the single most punishing food value in the game |
| Funeral bell | `mushroom-funeralbell-*` | Same amatoxins as death cap; classic misidentification risk. **In-game: `health: -40`** — grows as a tree-side mushroom, defined in the separate `mushroom-side.json:41,70` rather than the ground `mushroom.json` (easy to miss on a first pass), second only to death cap |
| Fool's conecap | `mushroom-foolsconecap-*` | Deadly in some species (e.g. *C. filaris*), amatoxin risk. **In-game: `health: -20`** (`mushroom.json:103`) |

### 2. GI-toxic / emetic (survivable)

| Ingredient | Block code | Note |
|---|---|---|
| Sickener | `mushroom-sickener-*` | Induces vomiting (name is literal). **In-game: `health: -7`** |
| Jack o'lantern | `mushroom-jackolantern-*` | Severe GI distress, often confused with chanterelles. **In-game: `health: -6`** |
| Witch's hat | `mushroom-witchhat-*` | Real-world GI distress, but **in-game this is not modeled** — falls to the default `health: 0` in `mushroom.json` (harmless to eat). Kept here for real-world accuracy; treat as neutral for any in-game balance decision |
| Devil's bolete | `mushroom-devilbolete-*` | GI upset. **In-game: `health: -10`** |
| Earthball | `mushroom-earthball-*` | Common puffball lookalike. **In-game: `health: -8`** |
| Elfin saddle | `mushroom-elfinsaddle-*` | Contains gyromitrin — **flag: toxic even cooked in some species, can be deadly, not purely "survivable"**. **In-game: `health: -7`, and it also has `satiety: 80` — the game treats it as edible food that also damages you, not exclusively one or the other** |
| Gold-drop milkcap | `mushroom-golddropmilkcap-*` | Mildly toxic/inedible, bitter. **In-game: `health: -2.5`** |
| Pink bonnet | `mushroom-pinkbonnet-*` | **Low confidence** real-world species ID (possibly *Mycena rosea*) — moved here from "no known use" because **in-game it has a confirmed `health: -10`** (`mushroom-side.json:42,71`, tree-side mushroom), regardless of the uncertain real-world match |

### 3. Cardiac glycoside

| Ingredient | Block code | Note |
|---|---|---|
| Lily of the valley | `flower-lilyofthevalley-*` | All parts highly toxic; historically used for cardiac conditions, unsupported by modern safety data |

### 4. Neurotoxic / alkaloid

| Ingredient | Block code | Note |
|---|---|---|
| Daffodil | `flower-daffodil-*` | Lycorine, galanthamine |
| Bluebell | `flower-bluebell-*` | Toxic, no established remedy use |
| Lupine | `flower-lupine-{color}-*` | Toxic alkaloids in many wild species (severity is species-dependent) |
| Cow parsley | `flower-cowparsley-*` | Risk is mostly misidentification — closely resembles poisonous hemlock |

### 5. Psychoactive / hallucinogenic

| Ingredient | Block code | Note |
|---|---|---|
| Liberty cap | `mushroom-libertycap-*` | Psilocybin-containing. **In-game: no health penalty, `psychedelic: 1.4`** |
| Gold cap | `mushroom-goldcap-*` | Psilocybin-containing. **In-game: no health penalty, `psychedelic: 1`** |
| Wavy cap | `mushroom-wavycap-*` | Psilocybin-containing. **In-game: no health penalty, `psychedelic: 0.8`** |
| Blue meanie | `mushroom-bluemeanie-*` | Potent psilocybin-containing species. **In-game: no health penalty, `psychedelic: 2`** — strongest psychedelic value in the game |
| Laughing jim | `mushroom-laughingjim-*` | Toxic/psychoactive, species-dependent. **In-game: `health: -10`, `psychedelic: 0.6`** — the only mushroom that's both damaging and psychedelic |
| Fly agaric | `mushroom-flyagaric-*` | Ibotenic acid/muscimol; generally poisonous raw. **In-game: `health: -6.5`, `psychedelic: 0.4`** |

### 6. Situational / conditional toxin (safe if prepared correctly, toxic otherwise)

| Ingredient | Block code | Note |
|---|---|---|
| Common morel | `mushroom-commonmorel-*` | Toxic raw, safe cooked |
| Bamboo (shoots) | `bamboo-*` | Cyanogenic compounds raw, must be cooked |
| Honey mushroom | `mushroom-honeymushroom-*` | GI upset raw/undercooked |
| Eagle fern | `fern-eaglefern` | **Low confidence** species ID (possibly bracken, *Pteridium aquilinum*) — chronic toxicity/carcinogenic risk in quantity if this ID is correct |

## No known remedy or poison use (Sludge ingredients)

Includes both confirmed-neutral entries and unresearched/too-low-confidence-to-assign entries;
either way, no recipe would currently be authored around these, so using them in a Cookpot
combination falls through to Sludge.

- Forget me not (`flower-forgetmenot-*`) — little documented medicinal history, mostly ornamental
- Dwarf furze / western gorse (`flower-westerngorse-*`) — edible flowers, no distinct medicinal tradition
- Redtop grass (`flower-redtopgrass-*`) — ornamental/pasture grass
- Deer fern (`fern-deerfern`) — unresearched
- Lace lichen (`hanginglichen-lace-*`) — unresearched, low-confidence species ID
- Wild vine, wild vine (tropical), wild vines (static) (`wildvine-*`, `wildvinestatic-*`) — no real-world species implied by the game
- Croton (`flower-croton-*`) — decorative ornamental foliage, not the medicinal *Croton* genus
- Rafflesia (brown/red) (`flower-rafflesia-{brown/red}`) — folk postpartum tonic claims exist but no pharmacological support; too unconfirmed to cluster
- Saguaro cactus (`saguarocactus-*`) — food/ceremonial use, no herbal remedy tradition
- Barrel cactus (`barrelcactus-normal`) — water/food source, no herbal remedy tradition
- Silver torch cactus (`silvertorchcactus`) — decorative; unlike related torch cacti, not established as medicinal/psychoactive
- Papyrus (`tallplant-papyrus-*`) — historically paper-making/food, no medicinal use
- Tule (`tallplant-tule-*`) — traditionally weaving/food, no medicinal use
- Brown sedge (`tallplant-brownsedge-*`) — low-confidence species ID, no use found
- Fern tree (`ferntree-normal-*`), tall fern (`tallfern`) — no real-world species implied, decorative
- **Tallgrass (`tallgrass-*`)** — no herbal use found, **and not harvestable as an ingredient item at all** (drops `drygrass`, confirmed in `tallgrass.json`)
- Hay (`hay-*`) — building/feed material, no herbal-remedy tradition
- Field mushroom, bitter bolete, black trumpet, chanterelle, green cracked russula, indigo milkcap, king bolete, lobster, orange oak bolete, paddy straw, puffball, red wine cap, saffron milkcap, violet webcap, dryad saddle, pink oyster — plain edible/culinary mushrooms, no distinct herbal-use note recorded
- Deer ear (`mushroom-deerear-*`) — low-confidence species ID, insufficient source data to assign a cluster, and not in either mushroom JSON's health-penalty tables, so `health: 0` in-game

## Rarity of the unused flora (for Antidote-cluster candidate selection)

Pulled from `assets/survival/worldgen/blockpatches/*.json` (`chance` = roughly how many
patch-placement attempts per eligible area, not a literal per-block odds — a real relative
signal, not a precise probability). Excludes Tallgrass (not harvestable) and Hay (processed
good, not foraged). Compiled to inform which unused ingredients would suit a rare/special
Antidote recipe vs. a common/always-available one — not a decision on cluster membership itself.

**Very rare**: Barrel cactus (`chance: 0.015`/`0.0375`, hot/dry biome only) — the rarest entry on
the whole unused list by a wide margin.

**Rare**: Field mushroom, Red wine cap, Bitter bolete (`chance: 0.08` each, an order of magnitude
below most other mushrooms); Silver torch cactus (`chance: 0.3`, mountain-restricted); Saguaro
cactus (`chance: 0.1-0.4`, same arid biome as barrel cactus but less rare); Forget-me-not
(`chance: 0.4` but `quantity: {avg: 1, var: 0}` — never a real patch, always a single plant).

**Narrow-biome specialists** (not rare within their biome, but the biome itself is small):
Rafflesia (`chance: 1.25`, high, but `minTemp: 30, minForest: 0.9` — deep jungle only); Croton
(5 color-tier variants, `chance: 0.15-0.5`, tropical jungle only).

**Uncommon**: Western gorse (`chance: 0.15` — identical profile to heather, which *is* already
clustered, so it isn't actually rarer than a currently-used ingredient, just unused); Indigo
milkcap, Black trumpet, Lobster mushroom (`chance: 0.8`); Violet webcap, Green cracked russula
(`chance: 1.6`).

**Actually common, unused for lack of research rather than rarity**: Eagle fern, Deer fern, Tall
fern (`chance: 3`, explicitly commented "Common" in the source file, same tier as cinnamon
fern/hart's-tongue fern which are already clustered); Deer ear mushroom (`chance: 8`); Pink
oyster (`6.4`), Dryad saddle (`4`), Paddy straw (`2.4`), King bolete/Puffball (`2`), Chanterelle
(`1.2`); Papyrus, Tule, Brown sedge (`chance: 1`, reliable near-water spawns, same tier as
cattails which are already clustered).

**Different spawn mechanism, not blockpatch-based**: Wild vine generates as part of tree growth
(`worldgen/treegen/vineykapok.json` and similar), not an independent patch — availability tracks
specific tree species density in jungle biomes rather than a standalone chance value. Lace
lichen's natural-growth mechanism could not be confirmed in this pass — only one reference found,
in an underground story schematic, not a general blockpatch; likely grows attached to trees like
old man's beard does, but unresolved rather than guessed at here.

## Open follow-ups

- Elfin saddle's cluster placement (GI-toxic) is real-world-provisional (gyromitrin toxicity
  varies sharply by species and can be fatal), but its **in-game severity is now confirmed**:
  `health: -7`, same tier as sickener, not in the death-cap/funeral-bell tier.
- Golden poppy, orange mallow, and eagle fern still carry low-confidence real-world species IDs
  with no in-game numeric data found to cross-check them against (they're flowers/ferns, not
  mushrooms, so no `nutritionPropsByType`-style table applies) — re-verify before any specific
  recipe is authored against them. Pink bonnet's real-world ID is still low-confidence, but its
  in-game toxicity (`health: -10`) is now confirmed regardless of the ID question.
- **Lesson from this pass:** several toxic mushrooms grow as tree-side variants defined in a
  separate file (`mushroom-side.json`) from the ground-mushroom file (`mushroom.json`) — funeral
  bell's real in-game severity (`health: -40`) was missed on the first data pull specifically
  because of this split. Any future numeric audit of the mushroom catalog should check both
  files, not just `mushroom.json`.
- No specific recipe combos are proposed here by design (see intro) — that's the next pass once
  ready to author actual Cookpot recipe JSON.
- **Scarcity/renewability — now designed**, see
  `docs/superpowers/specs/2026-09-02-renewable-cultivation-design.md`. Extends vanilla's existing
  flowerpot/planter blocks with a `BlockEntityBerryBush`-style timed growth cycle (vanilla's own
  pot block entity is otherwise purely decorative, confirmed), covering all flora without an
  existing growth mechanism — including mushrooms, gated to indoor/cellar-like rooms only via the
  same room classification vanilla already uses for cheese-aging and berry-bush greenhouse
  bonuses. Supersedes the original "Herbalist Pots" mod reference below as prior art only, not as
  a dependency.
