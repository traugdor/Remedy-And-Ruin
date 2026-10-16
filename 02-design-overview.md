# Remedy & Ruin — Master Design Document

Status: **approved design, not yet implemented.** This is the single merged document folding in
every design spec produced so far — the flora cluster catalog, the brewing pipeline, the
illness/poison effect catalog, broken bones, and renewable cultivation. The five source documents
are archived under `docs/superpowers/specs/archive/` for history; this file is the current,
authoritative one. When something here changes, this is the only file that needs editing.

Supersedes `00-mod-overview.md`, which is now historical (an explicitly-labeled pre-design
research dump) rather than a live picture of the design. `01-engine-hooks.md` and `flora/*.md`
(the five per-category raw catalogs: `flowers.md`, `herbs.md`, `mushrooms.md`,
`ferns-lichens-vines.md`, `cacti-reeds-grasses.md`) remain live reference material, cited
throughout this document rather than superseded or merged in — they're primary research, not
design decisions.

## Pipeline, end to end

```txt
Flora catalog (flora/*.md)
        │
        ▼
Part 1: Clusters — 9 remedy clusters + 6 poison clusters, each cluster IS a Cookpot
        recipe's ingredient list. Plus rarity data and the Universal Antidote's fixed recipe.
        │
        ▼
Part 2: Brewing Pipeline — Cookpot (1L water + 3 stacks, any mix of one cluster) → Potion
        Base (edible directly, ~2hr fresh) → Barrel (10L water, ~1wk steep) → Diluted
        Potion (non-functional) → Distillery (Boiler+Condenser) → Potion → Concentrated
        Potion. Dosing via Vial (precise) or any liquid container (risky, 4x dose). Toxicity/
        overdose counter. Poison track reuses the same pipeline. Arrow poisoning (dip + craft).
        │
        ▼
Part 3: Conditions & Poison Effects — what everything actually does: Bleeding, Wound
        Infection, Upset Stomach, Sedative, Chest Cold/Flu/Bronchitis/Pneumonia, Pain relief,
        Skin Irritation, Immune/Tonic (contagion), Hallucination, Temporal Fog, Liver Disease,
        Resurrection Sickness, Toxic/Noxious/Cardiac/Neurotoxic/Mind Poison. Also: delayed-onset
        mushroom poisoning, and the Universal Antidote's two-dose cure.
        │
        ├──▶ Part 4: Broken Bones — Chest/Arms & Legs (Strain→Fracture→Break→Crush) and
        │     Skull (Strain→Fracture→death), escalation via repeated hits, location-scoped armor.
        │
        └──▶ Part 5: Renewable Cultivation — extends vanilla's decorative flowerpot/planter
              blocks with a berry-bush-style growth cycle, covering all flora (mushrooms
              included) without an existing regrowth mechanism.
```

Poisons and remedies share one pipeline. Conditions and poison effects share one delivery
architecture (`EntityStats` buffs/debuffs, `EntityBehaviorHealth`'s DoT system). Broken bones and
Wound Infection share a debuff shape without sharing a tier system. See "Cross-cutting
decisions" near the end for the connections that span parts.

---

# Part 1: Flora Catalog & Clusters

Source: derived bottom-up from the real-world herbal-use notes recorded in `flora/herbs.md`,
`flora/flowers.md`, `flora/mushrooms.md`, `flora/ferns-lichens-vines.md`, and
`flora/cacti-reeds-grasses.md`. No illness/injury system existed when this catalog was first
built, so it still doesn't assign specific potency numbers — but per Part 2 §1, **each cluster
below doubles directly as one Cookpot recipe's ingredient category**: every ingredient listed
under a cluster is one entry in that recipe's `CookingRecipeIngredient.ValidStacks[]`, all
interchangeable within the recipe's 3 required stacks (any mix). This is not a step removed from
actual recipe data — it more or less *is* the recipe ingredient lists, modulo exact
effect/potency values still to be assigned.

A recipe only matches within its own cluster — plants from two different clusters mixed across
the 3 stacks satisfy no recipe and fall through to Sludge. Every ingredient listed under "No
known remedy or poison use" below is, by construction, always a Sludge ingredient: it belongs to
no cluster, so no recipe would ever be authored around it.

A recipe's brewed effect is authored on its own terms, not derived from an ingredient's raw-eaten
health value — Antiseptic's effect doesn't get computed from its cluster members' raw `health`
numbers. Separately, the raw-eaten `health` values themselves (e.g. death cap ` -50`, funeral
bell ` -40`) aren't instant damage — per Part 3's "raw-food penalties" section, they apply as a
timed delay followed by a DoT, specifically so the Universal Antidote matters for raw/cooked
mushroom poisoning too.

**Harvestability caveat:** being catalogued here doesn't guarantee an ingredient is actually
obtainable. Confirmed example: `tallgrass-*` drops `drygrass` on break, never a `tallgrass-*`
item itself (`assets/survival/blocktypes/plant/tallgrass.json:145-151`, tool `knife`/`scythe`) —
same non-harvestable pattern found for tree leaves during original research. Other borderline
entries below are marked low-confidence where the real-world species ID itself is uncertain, but
their harvestability as an item has not been individually re-verified.

## Remedy clusters

### 1. Wound Care / Antimicrobial → produces **Antiseptic**

Primary use: treating bandages (`bandage-antiseptic`) or crafting an antiseptic poultice from an
already-made poultice (both detailed in Part 3's Wound Infection section). Drinking it induces
GI tract irritation and vomiting (Upset Stomach's fourth trigger, Part 3) — the easter egg, not
the intended use.

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

### 2. Digestive / GI → produces **Antinausea**

Primary use: drinking calms GI tract irritation, reduces vomiting symptoms, and is what actually
lets the player eat again — the real cure for Upset Stomach/Vomiting (Part 3), not just something
that clears on its own.

| Ingredient | Block code | Note |
|---|---|---|
| Basil | `herb-basil-*` | Digestive aid |
| Cilantro | `herb-cilantro-*` | Digestive aid |
| Marjoram | `herb-marjoram-*` | Digestive and mild antispasmodic |
| Mint | `herb-mint-*` | Digestive aid, nausea relief |
| Chamomile | `herb-chamomile-*` | Mild digestive soother |
| Mugwort | `flower-mugwort-*` | Digestive bitter |
| Hart's-tongue fern | `fern-hartstongue` | Liver/spleen/digestive complaints |

### 3. Calming / Sedative / Sleep → produces **Sedative**

Primary use: drinking allows the player to sleep when blocked by the `Tiredness` gate or other
means (Part 3).

| Ingredient | Block code | Note |
|---|---|---|
| Catmint | `flower-catmint-*` | Sedative/antispasmodic for anxiety, insomnia |
| Chamomile | `herb-chamomile-*` | Classic calming/sleep-aid tea |
| Lavender | `herb-lavender-*` | Calming/sleep aid |
| Ghost pipe | `flower-ghostpipewhite/pink/red-*` | Nervine/sedative/antispasmodic |
| Water lily | `block-waterlily` | Mild sedative, sleep-related folk use |
| Mugwort | `flower-mugwort-*` | Dream work/moxibustion tradition |
| Golden poppy | `flower-goldenpoppy-*` | **Low confidence** species ID — mild non-narcotic sedative if correct |

### 4. Respiratory / Cough / Cold → produces **Antiviral**

Primary use: drinking reduces the severity of symptoms across the whole Chest Cold family —
Chest Cold, Flu, Bronchitis, and Pneumonia alike (Part 3), not a single-stage cure.

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

### 5. Pain relief / Analgesic → produces **Analgesic**

Primary use: drinking reduces the effects of fever and broken-bone/wound pain (Flu's fever,
Skull-Strain's pain, Part 3/4) — the same Pain relief modifier already layered onto
Bleeding/Wound Infection, now with a formal product name. Does **not** cover Skin Irritation (no
walkSpeed component) or Hallucination/Temporal Fog (both are Mind Tonic's, cluster 8 below, since
neither is actually pain).

| Ingredient | Block code | Note |
|---|---|---|
| Mint | `herb-mint-*` | Cooling/analgesic topical use |
| Ghost pipe | `flower-ghostpipewhite/pink/red-*` | Modern herbalist tincture use for pain |
| Cattails | `tallplant-coopersreed-*` | Analgesic use of stem juice |
| Golden poppy | `flower-goldenpoppy-*` | **Low confidence** species ID — mild analgesic if correct |

### 6. Skin / Topical Inflammation → produces **Topical Ointment**

Primary use: applied to a bandage to produce a new `treated-bandage` item (same dip-craft pattern
as `bandage-antiseptic`, Part 3, just with Topical Ointment as the liquid instead of Antiseptic),
or combined with a poultice to produce a treated poultice (same grid-craft pattern as the
antiseptic poultice, from Topical Ointment Potion Base). Used to fight skin irritations. **Flagged for a later pass**: worth revisiting to more fully
nail down the specific sources/causes of Skin Irritation beyond what's already listed
(frostbite, sunburn, contact with an irritant plant, Part 3) — not blocking, just noted as
incomplete.

| Ingredient | Block code | Note |
|---|---|---|
| Cornflower | `flower-cornflower-*` | Folk eyewash/astringent for eye and skin inflammation |
| Horsetail | `flower-horsetail-*` | High-silica remedy for skin, hair, nail, connective tissue |
| Chamomile | `herb-chamomile-*` | Mild skin soother |
| Edelweiss | `flower-edelweiss-*` | Modern antioxidant skincare use |

### 7. Immune / Tonic → produces **Tonic**

Primary use: drink *before* interacting with sick patients to avoid catching their contagion —
the existing preventive-buff mechanic (Part 3), now with a formal product name. Absorbs the
former Urinary cluster's members (below) as additional sources. **Secondary property: restores
HP directly** — the only one of the 8 remedy potions that does. Fills a real gap: Part 3's
Bleeding section already assumed a drinkable "health potion" existed as the emergency valve when
bandaging isn't feasible, without any cluster actually producing one — Tonic is that potion, "a
tonic that restores your health" being the most natural fit of the eight.

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
| Heather | `flower-heather-*` | Absorbed from the former Urinary cluster (below) — infusions for urinary complaints |
| Cattails | `tallplant-coopersreed-*` | Absorbed from the former Urinary cluster — diuretic use (TCM pollen); also already in clusters 1 and 5 |
| Cow parsley | `flower-cowparsley-*` | Absorbed from the former Urinary cluster. **Caution retained** — occasionally used for kidney/urinary stones, but closely resembles poisonous hemlock; misidentification risk, not a clean pick |

### 8. Cognitive / Nerve support → produces **Mind Tonic**

Primary use: drinking reduces and/or removes the effects of Confusion, Brain Fog, and
headache/migraine — **Hallucination (apparition spawns), Temporal Fog (screen wobble/vignette),
and Skull-Strain's headache (same wobble/vignette mechanism as Temporal Fog, at a lighter
strength), all three** (Part 3/4). Regular Mind Tonic halves the wobble/vignette strength on
Temporal Fog or Skull-Strain's headache, whichever is active; Concentrated zeroes it entirely
while active. Hallucination's own potency split is still deferred, pending its apparition-spawn
mechanics being numbered.

| Ingredient | Block code | Note |
|---|---|---|
| Lion's mane (bearded tooth) | `mushroom-beardedtooth-*` | Nerve growth factor research |
| Sage | `herb-sage-*` | Traditional memory/cognitive folk use |

### 9. Urinary — removed, not used in this mod

Not a standalone cluster anymore. Its three members (heather, cattails, cow parsley) are rolled
into Tonic (cluster 7, above) to give that remedy more ingredient sources, rather than supporting
a distinct urinary-complaints remedy this mod doesn't otherwise use.

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
| Witch's hat | `mushroom-witchhat-*` | Real-world GI distress, but **in-game this is not modeled** — falls to the default `health: 0` in `mushroom.json` (harmless to eat) |
| Devil's bolete | `mushroom-devilbolete-*` | GI upset. **In-game: `health: -10`** |
| Earthball | `mushroom-earthball-*` | Common puffball lookalike. **In-game: `health: -8`** |
| Elfin saddle | `mushroom-elfinsaddle-*` | Contains gyromitrin — toxic even cooked in some species, can be deadly, not purely "survivable". **In-game: `health: -7`, and it also has `satiety: 80`** — treats it as edible food that also damages you |
| Gold-drop milkcap | `mushroom-golddropmilkcap-*` | Mildly toxic/inedible, bitter. **In-game: `health: -2.5`** |
| Pink bonnet | `mushroom-pinkbonnet-*` | **Low confidence** real-world species ID (possibly *Mycena rosea*) — **in-game confirmed `health: -10`** (`mushroom-side.json:42,71`, tree-side), regardless of the uncertain real-world match |

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
| Blue meanie | `mushroom-bluemeanie-*` | Potent psilocybin-containing species. **In-game: no health penalty, `psychedelic: 2`** — strongest in the game |
| Laughing jim | `mushroom-laughingjim-*` | Toxic/psychoactive, species-dependent. **In-game: `health: -10`, `psychedelic: 0.6`** |
| Fly agaric | `mushroom-flyagaric-*` | Ibotenic acid/muscimol; generally poisonous raw. **In-game: `health: -6.5`, `psychedelic: 0.4`** — both this and Laughing Jim are damaging and psychedelic at once |

### 6. Situational / conditional toxin (safe if prepared correctly, toxic otherwise)

| Ingredient | Block code | Note |
|---|---|---|
| Common morel | `mushroom-commonmorel-*` | Toxic raw, safe cooked |
| Bamboo (shoots) | `bamboo-*` | Cyanogenic compounds raw, must be cooked |
| Honey mushroom | `mushroom-honeymushroom-*` | GI upset raw/undercooked |
| Eagle fern | `fern-eaglefern` | **Low confidence** species ID (possibly bracken) — chronic toxicity/carcinogenic risk in quantity if correct |

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
- Field mushroom, bitter bolete, black trumpet, chanterelle, green cracked russula, indigo milkcap, king bolete, lobster, orange oak bolete, paddy straw, puffball, red wine cap, saffron milkcap, violet webcap, dryad saddle, pink oyster — plain edible/culinary mushrooms, no distinct herbal-use note recorded (three of these — field mushroom, red wine cap, bitter bolete — are the Universal Antidote's fixed recipe, Part 3)
- Deer ear (`mushroom-deerear-*`) — low-confidence species ID, insufficient source data, `health: 0` in-game

## Rarity of the unused flora

Pulled from `assets/survival/worldgen/blockpatches/*.json` (`chance` = roughly how many
patch-placement attempts per eligible area, a relative signal, not a precise probability).
Excludes Tallgrass (not harvestable) and Hay (processed good, not foraged). Originally compiled
to inform the Universal Antidote's ingredient rarity; useful more broadly for any future
ingredient-selection pass.

**Very rare**: Barrel cactus (`chance: 0.015`/`0.0375`, hot/dry biome only) — the rarest entry on
the whole unused list by a wide margin.

**Rare**: Field mushroom, Red wine cap, Puffball (`chance: 0.08` each, an order of magnitude below
most other mushrooms — Field Mushroom and Red Wine Cap's rarity is why they became two of the
Antidote's three ingredients); Silver torch cactus (`chance: 0.3`, mountain-restricted); Saguaro
cactus (`chance: 0.1-0.4`, same arid biome as barrel cactus but less rare); Forget-me-not
(`chance: 0.4` but `quantity: {avg: 1, var: 0}` — never a real patch, always a single plant).

**Narrow-biome specialists** (not rare within their biome, but the biome itself is small):
Rafflesia (`chance: 1.25`, high, but `minTemp: 30, minForest: 0.9` — deep jungle only); Croton (5
color-tier variants, `chance: 0.15-0.5`, tropical jungle only).

**Uncommon**: Western gorse (`chance: 0.15` — identical profile to heather, which *is* already
clustered, so it isn't actually rarer than a currently-used ingredient, just unused); Black
trumpet (`chance: 0.4`); Indigo milkcap (`0.64`); King bolete, Green cracked russula (`0.8` each);
**Bitter bolete** (`1.2`) — genuinely uncommon, not rare; Violet webcap (`1.2`).

**Actually common, unused for lack of research rather than rarity**: Eagle fern, Deer fern, Tall
fern (`chance: 3`, explicitly commented "Common" in the source file, same tier as cinnamon
fern/hart's-tongue fern which are already clustered); Deer ear mushroom, Dryad saddle
(`chance: 8` each); Pink oyster (`4`); Chanterelle (`2.4`); Lobster mushroom (`2`); Paddy straw
(`1.6`); Papyrus, Tule, Brown sedge (`chance: 1`, reliable near-water spawns, same tier as
cattails).

**Different spawn mechanism, not blockpatch-based**: Wild vine generates as part of tree growth
(`worldgen/treegen/vineykapok.json` and similar), not an independent patch. Lace lichen's
natural-growth mechanism could not be confirmed — only one reference found, in an underground
story schematic, not a general blockpatch; likely grows attached to trees like old man's beard
does, but unresolved.

---

# Part 2: Brewing Pipeline

## Goals

- One data-driven crafting pipeline, shared by both remedies and poisons, built by reusing
  existing Vintage Story primitives (cookpot recipes, barrel sealing, boiler/condenser
  distillation, generic liquid-container drinking) rather than inventing new block types where
  an existing one already fits.
- A meaningful risk/reward curve: stronger tiers and riskier ingredient combos are more powerful
  but more dangerous, and the container you drink from is itself a choice between precision and
  risk.
- Content-authorable: adding a new remedy or poison is a matter of writing a recipe (ingredients
  + effect + side-effect profile), not writing new code.

## Pipeline overview

```txt
Cookpot                       Barrel                  Distillery (Boiler+Condenser)
1L water                                               Pass 1              Pass 2 (optional)
+ 3 stacks of 6, any    →     Potion Base    →         Diluted Potion  →   Potion  →  Concentrated
mix of one cluster's           + 10L water               (unusable,          Potion     Potion
plants (18 units total)        sealed ~1 week            near-zero effect)
                                → Diluted Potion
```

Poisons run through the exact same three stages — a poison recipe is simply a recipe whose output
is tagged harmful instead of beneficial. There is no separate poison pipeline.

## §1. Cookpot — Potion Base

The Cookpot recipe machinery is `CookingRecipe` / `CookingRecipeIngredient`
(`VSSurvivalMod/Vintagestory.GameContent/CookingRecipe.cs`, `CookingRecipeIngredient.cs`), the
same class vanilla uses for soup/stew recipes ("any vegetable" style categories). This matters a
lot, because it natively supports the ingredient-substitution model below with zero new engine
code.

- Slot 1 is always exactly 1L water.
- **One recipe = one cluster** (Part 1), not one recipe per specific ingredient combination. Each
  remedy/poison recipe defines a single `CookingRecipeIngredient` category — e.g. "Wound Care
  plant" — whose `ValidStacks[]` list is exactly that cluster's ingredient list, each entry at
  `StackSize: 6`.
- **All 3 plant-stack slots must always be filled.** The category's `MinQuantity`/`MaxQuantity`
  are both set to 3 (a count of *qualifying stacks*, not raw units — `CookingRecipe.Matches()`
  tallies one match per input stack, `CookingRecipe.cs:65-144`), so exactly 3 stacks of 6 units
  each are required, 18 units total.
- **Any mix of that cluster's members satisfies the 3 stacks**, independently per stack — 3
  stacks of the same plant (18× cattails), or a mix (2×cattails + 1×heather), match the same
  recipe identically, because `CookingRecipe.Matches()` only checks that each input stack
  satisfies *some* valid entry in the category, not which specific one. This is exactly how
  vanilla already lets "any vegetable" satisfy a soup's vegetable slot.
- **Ingredient choice within a cluster doesn't affect anything mechanically.** Every Potion Base
  from a given cluster is identical regardless of which valid combination produced it — risk
  lives entirely in §5/§6's overdose system, not in ingredient choice.
- Recipes only match within a single cluster — mixing plants from two different clusters across
  the 3 slots (e.g. cattails + basil) doesn't satisfy any one recipe's category and falls through
  to Sludge.
- **Multi-cluster ingredients don't get partial credit — all 3 stacks must land on one common
  cluster, unanimously, not just a majority.** Several ingredients belong to more than one
  cluster at once (e.g. cattails is in Antiseptic/1, Analgesic/5, *and* Tonic/7). When such an
  ingredient is combined with others, there has to be a single cluster that all 3 chosen
  ingredients belong to simultaneously — if 2 of 3 happen to share a cluster but the 3rd doesn't,
  that's still Sludge, not a "close enough" match rounded up to the majority cluster. Worked
  example: **Cattails + Shiitake + Sage.** Cattails could support Antiseptic, Analgesic, or
  Tonic; Sage could support Antiseptic or Mind Tonic; Shiitake only supports Tonic. Tonic has
  Cattails and Shiitake, but not Sage. Antiseptic has Cattails and Sage, but not Shiitake. No
  cluster contains all three — every candidate cluster is still missing its 3rd ingredient — so
  the combination produces Sludge, not Antiseptic, not Tonic, and not some hybrid of the two.
  This falls straight out of the existing mechanic (`CookingRecipe.Matches()` checks each
  recipe's own fixed `ValidStacks[]` independently; there's no cross-recipe partial-match
  fallback), not a new rule — just worth stating explicitly since multi-cluster membership could
  otherwise read as more forgiving than it actually is.
- Any ingredient combination that does **not** match an authored recipe produces **Sludge** — a
  junk/filler item, not an error state. Ingredients with no medicinal (or poisonous) properties
  can still safely go in the pot without a special-cased rejection. **Sludge is pure waste**: not
  edible, and not a valid input anywhere in the Barrel stage — mixing clusters doesn't produce a
  weaker or off-brand remedy, it's a dead end with no further use.
- **Sludge is its own catch-all Cookpot recipe, numbered last (`15-sludge.json`) so every one of
  the 14 real recipes gets first shot at matching.** Its ingredient category lists every plant
  from the "No known remedy or poison use" catalog above **and every ingredient used by any of the
  13 clusters** — the full flora catalog, not just the leftovers. This is what makes the
  Cattails + Shiitake + Sage worked example (above) actually produce Sludge instead of failing to
  craft anything: none of those three plants is itself unclaimed, but no single cluster contains
  all three, so no cluster recipe matches and only Sludge's broader category does. Safe to be this
  inclusive precisely because Sludge loads last — any combination that genuinely satisfies one
  cluster's specific recipe is already claimed by that recipe before Sludge is ever tried. Sludge
  is its own item, not literal vanilla `rot` — it reuses `rot`'s shape and texture wholesale (the
  same "no new art" principle as Potion Base) but has its own code, lang entry, and identity, so
  the player actually sees "Sludge," not "Rot," when they waste ingredients on a bad combination.
  Like Potion Base, it isn't edible and decays into real `rot` on its own short fuse. Lace lichen
  is the one catalogued entry deliberately left out: it shares `BlockHangingLichen`'s class with
  Old Man's Beard, which was already confirmed to drop nothing at all when harvested,
  so it's unobtainable the same way Tallgrass is.
- **Potion Base (and Sludge) start spoiling immediately, not on a delay from crafting.** Roughly
  2 in-game hours fresh before spoilage begins, same `TransitionableProperties`-style "Perish"
  shape used throughout vanilla (freshHours, then a transition window toward `rot`) — a short
  fuse relative to the Barrel's ~1-week dilution steep, so a Potion Base can't be crafted and
  stockpiled for later; it has to go into the Barrel promptly.
- Output: **Potion Base**, a discrete item (not a liquid) consumed by the barrel stage.
- **Texture and mechanical template: reuses vanilla's `rot` item wholesale.** `rot.json` already
  defines exactly the shape Potion Base needs — a solid item with `waterTightContainerProps`
  (`itemsPerLitre: 1`, matching the 1-unit-per-liter convention `§2`'s ratio math already
  depends on), a distinct `inContainerTexture` for how it looks once inside a barrel, and its own
  held/dropped item texture (`block/creature/rot/rot`) — a genuine fit, not just a placeholder:
  Potion Base *is* a boiled plant mash being scooped out of a pot, visually and mechanically the
  same category of thing as rot. No new art, no new `waterTightContainerProps` shape to invent.
- **The Potion Base itself is edible**, not only a brewing intermediate — eating it directly
  applies the same effect as drinking the fully processed Potion, just for a much shorter
  duration. Processing it through the Barrel and Distillery is what makes the effect last;
  skipping straight to eating the boiled mash is the "I need this right now and don't care that
  it won't last" option, not a strictly worse alternative to be balanced away.

## §2. Barrel — Dilution

- 1x Potion Base + 10L water, sealed in a barrel, at a fixed 1:10 ratio.
- Game-calendar steep duration, default ~1 week of in-game
  time, defined per-recipe (same `SealHours` field as
  `BarrelRecipe`, so different remedies/poisons can have different steep times).
- Output: 10L of **Diluted Potion** liquid.
- Batches scale: barrels hold up to 50L, and `BarrelRecipe`'s existing output-scaling logic
  (`GetOutputSize()`, `BarrelRecipe.cs:239-272`) already multiplies the recipe by however many
  ingredient-stack multiples are present — so 5x Potion Base + 50L water in one barrel yields 50L
  of Diluted Potion in a single steep, at the same 1:10 ratio, with no new scaling logic needed.
- Diluted Potion is intentionally not a usable end product — its effect (and side effect) is
  negligible. It exists purely as the distillery's input; a player who drinks it straight is
  wasting the batch, not exploiting a shortcut.

## §3. Distillery — Boiler + Condenser

Reuses the vanilla spirits-distillation apparatus almost unmodified:
`VSSurvivalMod/Vintagestory.GameContent/BlockEntityBoiler.cs` (heats a liquid container, and once
its temperature crosses 75°C, evaporates it at a rate driven by a `distillationProps` item
attribute — `DistillationProps.Ratio`/`DistillationProps.DistilledStack`) feeding an adjacent
`VSSurvivalMod/Vintagestory.GameContent/BlockEntityCondenser.cs`, which condenses the vapor into a
bucket-like container.

- **Pass 1**: Diluted Potion boiled and condensed → **Potion**. Volume drops; first usable tier —
  real primary effect, real (but modest) side-effect risk.
- **Pass 2 (optional)**: re-boil the Potion output through the same boiler+condenser pair →
  **Concentrated Potion**. Volume drops further; strongest primary effect, strongest side
  effects, and raises the toxicity counter (§5) faster per dose than Potion does.
- Each recipe defines its own `DistillationProps`-style ratio per pass, so different
  remedies/poisons can concentrate at different rates — same mechanism vanilla already uses to
  differentiate spirit types.
- There is no separate "advanced distillery" block gating Concentrated Potion — it's the same
  apparatus, run twice.

## §4. Dosing & consumption

No dedicated "drink this specific item" action beyond what `BlockLiquidContainerBase` already
provides. Drinking is the existing generic "drink from any compatible liquid container"
interaction (`CanDrinkFrom` + `DrinkPortionSize`), which already works uniformly across bowls,
jugs, buckets, waterskins, and any third-party mod's container that subclasses
`BlockLiquidContainerBase` — no new drinking mechanic needs to be written.

`DrinkPortionSize` is a **per-container** attribute (default 1L if unset), not a property of the
liquid. This mod adds one new container to exploit that:

- **Vial** (new item/block added by this mod): `capacityLitres: 0.25`, `drinkPortionSize: 0.25`.
  Holds exactly one dose, dispenses exactly one dose per drink — the precise way to take a
  potion. Unlike the balance values elsewhere, the Vial isn't JSON-only — it's a genuinely new
  block with no vanilla shape to reuse, so it needs its own model authored in VSMC (Vintage Story
  Model Creator) and referenced from its shape JSON.
- **Vial charges (9 per fill), only relevant once §8 Arrow poisoning exists.** A freshly-filled
  Vial starts at a full 9/9. Dipping an arrow or crafting a poisoned arrow from a Vial (§8) each
  consume exactly 1 charge, regardless of which of the two methods was used — a poisoned arrow's
  own potency is always fixed at full strength and is never affected by how many charges the Vial
  had remaining at the moment of dipping/crafting; only the Vial's own *remaining liquid* is
  affected. Drinking a Vial always consumes everything still in it in one dose, whatever fraction
  of the 9 charges that happens to be — an untouched 9/9 Vial drunk directly is a full-strength
  dose, but a Vial already drawn down to, say, 3/9 by prior dipping/crafting delivers only 3/9 of
  that dose's normal potency when drunk. This is a deliberate, safer tolerance-building path,
  alongside deliberately eating/drinking small enough discounted doses (Tolerance below) — drawing
  a Vial most of the way down via arrows first, then drinking the diminished remainder, is a way to
  intentionally take a much weaker dose than a fresh Vial would deliver. A Vial cannot be refilled
  until fully emptied (0/9, reached either by using the last charge or by drinking whatever
  remained) — refilling then costs 0.25L from a source container and resets the Vial back to a
  full 9/9.
- **Generic containers** (bowl, jug, bucket, other mods' containers): use their own existing
  `drinkPortionSize`, vanilla default 1L. Drinking a potion straight from one of these consumes a
  full 1L per gulp — **4 doses at once** — spiking the toxicity counter accordingly.

One dose = 0.25L, fixed regardless of tier — the tier changes *potency per dose*, not dose size.

## §5. Overdose / toxicity

- A decaying per-player toxicity counter, structurally modeled on
  `EntityBehaviorHunger.detoxCounter` — a hidden float that rises on consumption and bleeds off
  over time rather than resetting on a hard cooldown.
- Each dose consumed raises the counter by an amount defined per recipe/tier: Potion dose =
  baseline increase; Concentrated Potion dose = larger increase (exact multiplier a tuning
  value).
- Drinking from a generic container (1L = 4 doses at once) raises the counter by 4x a single
  dose's worth in one action, independent of decay — this is what makes the Vial the safer choice
  for anything with meaningful toxicity per dose.
- **Crossing the threshold triggers an overdose effect specific to which potion was overdosed
  on** — see §6.

## §6. Overdose effects

Every potion carries overdose risk via §5's toxicity counter, and **which overdose effect
triggers depends on which potion type was overdosed on**, not a single universal symptom. This
is a genuinely new piece of content per potion (not free reuse), and several of these
deliberately reuse the effect their own potion normally treats or causes, as an ironic backfire.
Draft mapping, explicitly adjustable later, not final balance:

| Potion | Overdose effect | Why |
|---|---|---|
| Antiseptic | Severe/guaranteed-complete Upset Stomach (skips the partial-void-first rule) | Already causes GI upset at normal dose (the easter egg, Part 3); overdose removes the "starts mild" mercy |
| Antinausea | Drowsiness/lethargy — a temporary `walkSpeed`/stamina penalty | Real anti-nausea medication commonly causes drowsiness at high doses |
| Sedative | Forces an extended, uncontrollable sleep — can't voluntarily wake early for a while | Over-sedation, the utility effect turning against the player |
| Antiviral | Temporarily *weakens* Immune/Tonic's contagion resistance — the opposite of what Antiviral does at normal dose | Ironic immune-overcorrection backfire |
| Analgesic | Numbs perception of your own condition — temporarily hides/dulls the Bleeding severity readout or low-health warning | Mirrors real analgesic-overdose risk: masking symptoms you need to notice, not just more pain relief |
| Topical Ointment | Internal skin-irritation-shaped backfire (a rash) if drunk rather than applied | Mirrors Antiseptic's own "drink the topical-use one and get punished" pattern |
| Tonic | Triggers Liver Disease directly | Consistent with Liver Disease's existing "excessive potion consumption" trigger (Part 3) — this is a concrete instance of it |
| Mind Tonic | Triggers Temporal Fog or Hallucination — the very things it cures | Overdosing on the cognitive-support cure causing overstimulation, a tight ironic fit |

Poison overdose is unaffected by this — poisons already have their own distinct per-cluster
effects and escalation ladders (Part 3), this table is specific to the 8 remedy potions.

## §7. Poison track

- Identical pipeline to remedies (Cookpot → Barrel → Distillery), no separate blocks or steps.
- A poison recipe is a Cookpot recipe like any other, just tagged as producing a harmful output
  instead of a beneficial one, carrying a harmful "primary effect" instead of a curative one.
- Concentrated Poison is the strongest/most dangerous tier, mirroring Concentrated Potion.
- Poison doses interact with the toxicity/overdose system when drunk, same as remedy potions.

## §8. Arrow poisoning

Both application methods are supported, mirroring the dual interaction pattern already used by
fishing rods/bait in the base game:

- **Dip**: right-click one arrow against a poison-filled Vial (§4's 9-charge Vial specifically,
  not a generic container) to coat it directly, producing a poisoned arrow. Dipping only ever
  treats a single arrow per interaction, regardless of how large the held stack is — poisoning a
  whole stack means repeating the interaction once per arrow.
- **Craft**: a conventional grid recipe consuming 1 arrow + 1 Vial of poison to output 1 poisoned
  arrow, leaving the same Vial behind with one less charge (it isn't consumed outright — see §4).

**Dose and coating rules:**

- Both dipping and crafting consume exactly **1 of the Vial's 9 charges** per arrow — never a
  flat liquid volume, and never from any container other than a Vial (§4). A poisoned arrow's own
  potency is always fixed at full strength regardless of how many charges the Vial had remaining
  at the moment of dipping/crafting; only the Vial's own remaining liquid (and what it delivers if
  later drunk) is affected by charge depletion.
- **Picking up a poisoned arrow strips it** — it's no longer poisoned once retrieved, and must
  be re-treated before it counts as poisoned again.
- **Re-dipping an already-poisoned arrow in the same poison wastes the poison** — no stacking,
  no refresh benefit, just burns a charge for nothing.
- **Dipping an already-poisoned arrow in a different poison replaces the effect, doesn't combine
  it** — an arrow carries one poison type at a time; switching overwrites, never stacks two
  effects at once.

Poisoned arrows apply the poison's effect on hit, via the same delivery architecture as §9. See
Part 3's poison effects table for what each poison type actually does on a hit vs. when drunk —
they're not identical.

## §9. Effect delivery

The hook point is a generic call — conceptually `ApplyEffect(entity, recipeEffectData)` — invoked
on dose consumption or on-hit (arrows), backed by two existing engine primitives:

- `EntityStats` — named, source-keyed float-stat buffs/debuffs (`Set`/`Remove`/`GetBlended`), so
  multiple simultaneous potion/poison effects can stack or blend without stomping each other and
  can be independently removed when their duration ends.
- `EntityBehaviorHealth`'s DoT tick loop — for damage-over-time poisons/toxins. DoT ticks run on
  real (engine) elapsed time, not the game calendar - `EntityBehaviorHealth`'s tick loop is not
  scaled by `Calendar.SpeedOfTime`/`CalendarSpeedMul`; any poison-duration math in this document
  that assumes DoT ticks scale with calendar speed does not hold, and the tick interval itself
  should be expressed in real seconds.

The actual catalog of effects (Part 3) is what fills this hook in.

## Data model sketch

Not a final schema — illustrates the shape implied by the decisions above:

```jsonc
// Cookpot recipe (potion base) — CookingRecipe shape
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
        // full list = Part 1's Wound Care / Antimicrobial cluster
      ]
    }
  ],
  "output": { "code": "potionbase-woundcare" }
  // no per-combination side-effect data — ingredient choice within the cluster doesn't matter
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
    "distilledStack": { "code": "potion-woundcare" }
  }
}

// Effect + overdose profile (attached to the finished Potion / Concentrated Potion item)
{
  "primaryEffect": { /* Part 3 hook data */ },
  "overdoseEffect": { "code": "antiseptic-overdose" }, // keyed per potion TYPE, see §6's table
  "toxicityPerDose": { "potion": 1.0, "concentrated": 2.5 } // illustrative
}
```

## Engine hooks this pipeline reuses (no new mechanics required for these)

- Cluster/category recipe matching ("any of a list satisfies this slot") — `CookingRecipe.cs`,
  `CookingRecipeIngredient.cs`.
- Sealed game-calendar-time steeping — `BlockEntityBarrel.cs`/`BarrelRecipe.cs` (§2).
- Boiler/condenser distillation — `BlockEntityBoiler.cs`, `BlockEntityCondenser.cs`,
  `DistillationProps.cs`.
- Generic "drink from any container" — `BlockLiquidContainerBase.cs`.
- Buff/debuff stacking — `EntityStats.cs`.
- Damage-over-time — `EntityBehaviorHealth.cs`.
- Decaying counter precedent — `EntityBehaviorHunger.cs` (`detoxCounter`).

New C# is only needed for: the toxicity/overdose counter itself, the `ApplyEffect` hook point,
the per-potion-type overdose effect dispatch (§6), and the Vial item/block (likely no new class
at all beyond JSON, per `01-engine-hooks.md`'s note that content is JSON-driven).

---

# Part 3: Conditions & Poison Effects

This fills in Part 2 §9's `ApplyEffect` placeholder — it defines *what* each condition/poison
effect actually is and does, grounded in specific engine hooks. It does not assign specific flora
ingredients to specific conditions beyond what Part 1 already establishes.

## Design principle: raw/cooked poisonous ingredients trigger their cluster's effect directly

Eating a poisonous mushroom — raw, or cooked into any meal — does not apply vanilla's own instant
`health` penalty. That mechanic is fully suppressed for every mushroom belonging to one of the
five poison clusters, on both the raw item and inside any meal containing it (vanilla's own
meal-eating code, `BlockMeal.Consume`, sums every ingredient's health into one lump instant hit
via a single `ReceiveDamage` call — this pipeline is bypassed entirely for poison-cluster
ingredients rather than fed into it). In its place, the ingredient triggers its cluster's poison
effect (Part 3's Poison effects table below) directly, scaled by two per-mushroom numbers:

- **Effect ×** — magnitude, direct scale (1.0 = full cluster-baseline strength).
- **Onset ×** — speed, inverse scale (higher = faster onset).

Some mushrooms carry a **dual identity**: a primary cluster effect, plus a secondary, incidental
**Toxic Effect ×** / **Toxic Onset ×** pair — a residual hepatotoxic component riding along
underneath their main effect. This is scaled against Death Cap's vanilla `-50` health value as
the universal anchor for the Toxic axis specifically, regardless of which cluster the mushroom's
primary effect belongs to. Grounded in real toxicology, not an arbitrary cross-wire: classic
amatoxin poisoning (Death Cap, Funeral Bell, Fool's Conecap) genuinely does present with severe
GI symptoms hours before the liver-failure phase sets in days later, so giving GI-toxic mushrooms
a residual toxic sliver — and vice versa — is more accurate than treating every poison cluster as
hermetically sealed from the others. The GI-cluster mushrooms picking up a toxic sliver is a
deliberate gameplay unification more than strict realism (most of them aren't meaningfully
hepatotoxic in reality); the amatoxin mushrooms' own GI-first presentation is the well-documented
half of this.

**Toxic Poison cluster** (primary effect only, no secondary):

| Mushroom | Effect × | Onset × |
|---|---|---|
| Death Cap | 1.0 | 1.0 |
| Funeral Bell | 0.8 | 1.0 |
| Fool's Conecap | 0.4 | 0.8 |

**Liver Failure only actually triggers once the tolerance-discounted effect exceeds 15% of that
exposure's own undiscounted magnitude** — below that, the exposure produces only the harmless
early-warning symptom (a small `healingeffectivness` dip) and fades on its own, no Antidote
needed. Tolerance discounts in fixed 1/9 steps per tier reached (see Tolerance below), so this
threshold is crossed the moment discount reaches 8/9 (leaving 11.1% of the original magnitude,
under the 15% line) — the 22nd survived exposure to that cluster, one tier short of full (9/9)
immunity. This is a percentage of each exposure's own magnitude, not an absolute number, so Death
Cap, Funeral Bell, Fool's Conecap, and any incidental Toxic sliver riding in from another cluster
all stop being able to cross into the real DoT at the exact same tolerance progress, regardless of
how strong the source mushroom's raw dose was.

**Noxious Poison cluster** (GI is primary; all eight also carry a secondary Toxic sliver):

| Mushroom | GI Effect × | Toxic Effect × | Toxic Onset × |
|---|---|---|---|
| Devil's Bolete | 1.0 | 0.2 | 0.5 |
| Pink Bonnet | 1.0 | 0.2 | 0.5 |
| Earthball | 0.8 | 0.16 | 0.4 |
| Sickener | 0.7 | 0.14 | 0.4 |
| Elfin Saddle | 0.7 | 0.14 | 0.4 |
| Jack o'lantern | 0.6 | 0.12 | 0.3 |
| Gold-drop Milkcap | 0.25 | 0.05 | 0.2 |
| Witch's Hat | 0.05 | 0 | — |

**Mind Poison cluster** (psychedelic tripping is primary; two of six also carry a secondary Toxic
sliver):

| Mushroom | Effect × | Onset × | Toxic Effect × | Toxic Onset × |
|---|---|---|---|---|
| Blue Meanie | 1.0 | 1.0 | — | — |
| Liberty Cap | 0.7 | 0.7 | — | — |
| Gold Cap | 0.5 | 0.7 | — | — |
| Wavy Cap | 0.4 | 0.7 | — | — |
| Laughing Jim | 0.3 | 0.9 | 0.2 | 0.5 |
| Fly Agaric | 0.2 | 1.1 | 0.13 | 0.4 |

Cardiac Poison and Neurotoxic Poison have no mushroom members — both clusters are flowers only —
so no per-ingredient table applies to them.

Rationale for scaled-onset delivery generally:

- **Gives the player a real reaction window** to recognize something's wrong and drink the
  Antidote before it's too late — how much time plausibly depends on the specific
  mushroom/cluster rather than being one flat number for everything.
- **Opens the door to a "slip it into their meal" social mechanic** — since the effect isn't
  instantly obvious, a target might genuinely not notice what happened until it starts settling
  in.
- **No diagnostic signal, deliberately.** No status icon or other tell distinguishes a pending
  poison from having eaten nothing risky at all — poisoned food is just poisoned food. Giving the
  player a "something's wrong" indicator during the silent phase would undercut the entire
  "slipped into a meal" mechanic this is designed to enable.
- **The Antidote is a genuine cure-all, reaching the silent onset phase too** — see Universal
  Antidote below.

### Stacking

Every simultaneous poison exposure — from raw bites, a multi-ingredient stew, drinking, or a
poisoned arrow — is tracked as its own fully independent instance: its own timer, its own effect
magnitude, its own tolerance mitigation (below). Exposures never combine into a shared instance or
timeline, regardless of cluster or onset value — not even repeated bites of the exact same
mushroom. Eating three Fly Agarics produces three separate, independently-timed Mind Poison
instances, not one instance at 3× effect; a stew containing Fly Agaric and Devil's Bolete produces
two separate Toxic Poison tracks for the same reason any two exposures of that cluster would,
regardless of whether their onset values happen to match. Onset value is only ever a per-instance
speed setting (how quickly that one instance's own effect ramps up) — it has no bearing on whether
two exposures share or don't share a track.

This is one uniform rule regardless of source: nothing about *how* a poison was delivered changes
this — eating, drinking, or a poisoned arrow all produce their own independent instance every time.

### Stews and cooked meals

Vanilla's meal-eating code sums every ingredient's health value into one blended lump-sum hit
before applying it. That summing is fully bypassed for poison-cluster ingredients. Instead, the
mod inspects the meal's actual stored ingredient list directly (meals retain their full ingredient
stacks as data on the item itself — `BlockMeal.SetContents`/`GetNonEmptyContents`, also how
vanilla's own recipe-naming logic identifies ingredients like rot for the "Rotten Food" name
override) and applies the Effect×/Onset× rules and Stacking above per distinct ingredient present.
A poisoned stew is mechanically equivalent to eating its poisonous ingredients as separate bites,
not a diluted average — there is no "cook it into a big batch to dilute the dose" exploit.

## Tolerance

One tolerance counter exists per poison cluster (Toxic, Noxious, Cardiac, Neurotoxic, Mind) — not
per specific mushroom, and not per delivery method. It discounts all three axes a cluster can
express: lethal/toxic, GI, and psychoactive.

Tolerance builds in nine fixed tiers. Each tier requires exactly 3 survived exposures to that
cluster to advance; the discount itself moves in fixed 1/9 steps per tier reached (not a smooth
per-exposure ramp), so all three exposures within a tier apply the same discount as the tier
before it advances. Full progression is 9 tiers × 3 exposures = **27 total exposures for complete
(9/9) immunity** to that cluster.

- **Below** the crossing point for a given dose size: only duration scales down as tolerance
  rises; magnitude and any flat/instant component (e.g. Cardiac Poison's flat HP hit) stay at
  full strength.
- **At or above** the crossing point for that exact dose size: the entire effect is voided,
  including flat/instant components — a true all-or-nothing cutoff, not a fade. Cardiac Poison's
  flat -5 current/max HP hit is the clearest example: it stays fixed through partial tolerance
  (representing real, lasting cardiac damage) and only the recovery window shortens, until full
  crossing, at which point the HP hit itself is waived too.
- **Poisoned arrows** always apply the full, tolerance-discounted effect on a hit (no exemption
  short of 9/9 tolerance for that cluster), but never contribute toward building tolerance —
  bloodstream-direct delivery doesn't train resistance.
- **Eating something for its primary effect still builds tolerance toward that primary cluster**
  even when an incidental secondary Toxic sliver rides along uninvited (e.g. eating Fly Agaric
  for the trip) — the secondary risk is real and unmanaged unless separately trained.
- **No interaction with the potion toxicity/overdose counter** (§5/§6 above) — that system stays
  scoped to brewed potions only. The only consequences of eating poison are the stacked effects
  themselves, resolved by surviving, curing via Antidote, or dying.
- **A poison's effect must fade completely, on its own, before another exposure counts toward
  tolerance.** Stacking (above) still applies for danger — a second exposure taken while the
  first is still active still stacks its effect on top, same as ever — but it does not advance
  the tolerance ladder while any instance of that cluster remains active. Only an exposure taken
  after every active instance has fully resolved registers as one of the 27. This rules out
  rapid-fire chain-dosing as a fast track to immunity; deliberate tolerance-building means one
  exposure, full recovery, then the next.
- **Curing a poison with the Antidote negates that exposure's tolerance credit entirely.** The
  poison must run its natural course uninterrupted to count — reaching for the Antidote means you
  survive, but that attempt doesn't advance the ladder. This is exactly why a small enough
  discounted dose not crossing into Toxic Poison's actual DoT at all (above) matters beyond just
  being safer: it's what makes the late stages of tolerance-building survivable *without* an
  Antidote in the first place. Early tiers are genuinely dangerous and have to be survived
  outright; as accumulated tolerance shrinks the effective dose toward the point where it no
  longer crosses into real danger, "letting it run its course" naturally stops requiring a cure
  at all. The risk tapers off as a consequence of the existing threshold mechanic, not a separate
  exception.
- **Decays after a month of no exposure.** Each cluster tracks the in-game date of its last
  survived exposure (a watched attribute on the player). Tolerance holds at its current tier with
  no decay for one full in-game month (per the world calendar) after that date. Once a month
  passes with no further exposure to that cluster, tolerance begins decaying at 5% per in-game
  day — falling back through tiers as the percentage drops below their thresholds, same as it was
  built up. Any new survived exposure to that cluster resets the last-exposure date and the grace
  period along with it.

## Design principle: every effect surfaces through the "R&R Ongoing Effects" tab

Every condition/effect in this catalog — every remedy-cured condition, every injury/illness
state, every poison effect — is surfaced through a dedicated **"R&R Ongoing Effects"** tab in the
character info screen, listing every active instance as plain text (name, remaining duration,
current magnitude where relevant). No PlayerStatusHUD dependency, no status-strip icons, no icon
artwork at all. The player has to open the tab to check status rather than seeing an
always-visible on-screen indicator — a deliberate tradeoff for the poison side specifically
(Ingredient Effect Scaling and Stacking above mean a player can have several independent
instances of the same poison cluster running at once with different onsets and magnitudes, which
a fixed set of status-strip icons couldn't represent cleanly anyway), and applied uniformly to
every other condition too rather than splitting the display mechanism by effect type. Blanket
requirement, not revisited per-effect — any new condition/effect inherits it.

Positive/Negative categorization (Bleeding, Wound Infection, Upset Stomach, the Chest Cold
family, Skin Irritation, Hallucination, Temporal Fog, Liver Disease, Resurrection Sickness, and
all poison effects are Negative; the Sedative sleep-bypass and the Immune/Tonic preventive buff
are Positive) is still useful for how the tab displays entries — grouping or color-coding by
whether an effect is helping or hurting the player — but is purely a display concern now, not
tied to any external mod's API.

### Growth is a plain content operation

Two different things are expected to grow, independently: the **ingredient/recipe catalog** (more
flora, more flora mods integrated), and — separately — **the set of effect types itself**, which
could also grow as integrations happen. Since the tab is plain text with no per-effect icon
asset, adding either is simpler than an icon-based system would require:

- Every new **recipe** produces one of the *existing* effect types by default and just adds
  another entry under that type's existing name in the tab — nothing new to author.
- Adding a genuinely **new effect type** is a normal content operation: give it a name and a
  category (Positive/Negative), no new art asset-build step required.

## Conditions (cured by remedy clusters)

### Bleeding

Immediate blood-loss condition from combat/injury.

- Uses `EnumDamageOverTimeEffectType.Bleeding` — already defined in the engine as a DoT category
  distinct from Poison, but nothing in vanilla currently triggers it. This mod would be the first
  to actually use it.
- **Severity scales with hit size**, using a tiered system rather than a raw continuous formula:
  **Minor <4 damage taken, Moderate 4-8, Severe >8** — calibrated against damage actually taken
  *after* armor mitigation, not raw attacker damage (corrupt drifter's unmitigated 12, nightmare's
  20, double-headed's 24, `drifter.json:237-244`, would nearly always land Severe pre-armor, but
  armor's `flatDamageReduction`/`relativeProtection` brings actual damage taken well below that
  in practice — and by the time a hit clears 8 taken damage even after armor, the player is
  already in "bandage immediately" territory regardless of which tier it lands in).
- **Per-tier drain**: Minor 0.1 HP/sec for **60s** (~6 HP total, tapers off harmlessly — extended
  from an initial 30s specifically so its infection roll count matches Moderate's, see Wound
  Infection below); Moderate 0.25 HP/sec for 60s (~15 HP total — a full health bar's worth on a
  fresh player if completely ignored); Severe 0.5 HP/sec for 120s (~60 HP total if never treated
  — several times over lethal, matching "can kill if ignored" for the worst tier, with roughly 30
  seconds before death from full health if genuinely nothing is done about it).
- **Which attacks can cause a bleed is source-specific, not universal**, real new mod content
  (retagging + a new armor mechanic), checked against actual game data:
  - **Drifter** (corrupt/nightmare/double-headed tiers only — confirmed `SlashingAttack`,
    nightmare has a dedicated "knife" shape) causes Bleeding. Lower tiers do not.
  - **Shiver** (all tiers, confirmed `SlashingAttack` uniformly) causes Bleeding; higher tiers
    cause heavier bleeds and higher Wound Infection chance — a Shiver-specific rule layered on,
    not derived from a distinct "bite" damage type (the game doesn't tag it that way).
  - **Bowtorn**: melee causes no Bleeding. Its ranged `arrow-bone` projectile (confirmed ranged,
    confirmed bone arrows) causes no Bleeding either, but raises Wound Infection chance.
  - **Bear, wolf, and pig** (retaliation attack — pigs are passive but fight back when struck,
    `whenInEmotionState: "aggressiveondamage"`) cause Bleeding plus raised Wound Infection
    chance. None of these three are tagged `PiercingAttack` in the base game — bear and wolf are
    `SlashingAttack`, pig has no explicit override. **Retagging these three to `PiercingAttack`
    is real new mod content**, applied via a JSON patch (`assets/survival/patches/*.json`,
    RFC 6902-style, applied at load time, no vanilla file directly edited).
  - Arrows in general very likely already default to `PiercingAttack` in C# — to be confirmed at
    implementation time rather than assumed.
- **Armor mitigation requires a new mechanic, not reuse of the existing system.** Vanilla's armor
  protection (`relativeProtection` + `flatDamageReduction` + `protectionTier`) applies uniformly
  across all damage types — no per-damage-type differentiation exists. Implementing "chain
  resists slashing but not piercing" means a new mod-defined attribute (e.g.
  `perDamageTypeProtection`) plus a new hook (likely the same `onDamaged` delegate used below)
  that applies an additional bleed-chance/severity modifier keyed to the incoming hit's damage
  type. Target shape, not balanced: Plate strong across the board; Chain strong vs. Slashing,
  weak vs. Piercing; Scale/Lamellar/Brigandine moderate against both; Gambeson and other soft
  armor, minor against both.
- **Does not stack.** Only the strongest active Bleeding DoT applies at a time — enforced by mod
  logic (compare a new bleed's severity against any existing Bleeding entry in
  `ActiveDoTEffects` and keep only the stronger one).
- **Self-resolving is not the same as safe.** Left alone, a bleed's tick loop naturally runs out
  — but nothing floors damage above zero HP. A mild bleed tapers off harmlessly; a severe bleed
  from a big hit, left completely untreated for its full duration, **can kill**. Deliberate — it's
  what makes treatment matter.
- **Instant hit damage is reduced to compensate**, so a Bleeding-causing hit isn't simply "normal
  damage plus extra DoT on top." Vanilla's `DamageSource.Duration`/`TicksPerDuration` only
  supports converting a hit's *entire* damage into a DoT, not a partial split — so "reduced
  instant hit + separate bleed DoT" means using two hooks together: `EntityBehaviorHealth`'s
  public `onDamaged` delegate to shave down the instant damage on a qualifying hit, and a
  separate `ApplyDoTEffect(..., Bleeding)` call to apply the shaved-off portion as the bleed.
- **Staunching bleeding is universal to any healing item, not gated to a new tier.** Vanilla
  already has a real two-tier bandage system: `bandage-clean` (heals 3 HP) and `bandage-alcoholed`
  (heals 7 HP), both built on `CollectibleBehaviorHealingItem` — a channeled hold-to-apply
  heal-over-time behavior (distinct from the older instant `ItemPoultice`) that converts its heal
  into a `DamageSource` with `Duration`/`TicksPerDuration`, respects armor's `healingeffectivness`
  stat, and can revive a downed player. This mod hooks the general "healing item application
  completed" point (any bandage, any poultice) to also call `StopDoTEffect(Bleeding)`.
- **Tonic (Part 1 cluster 7) is the emergency valve when bandaging isn't feasible, not a
  substitute for it.** Its HP-restoring secondary property can out-heal a severe bleed's damage
  rate fast enough to avoid dying, but drinking it doesn't call `StopDoTEffect(Bleeding)` — it
  treats the symptom, not the cause, which still needs an actual bandage (or natural expiry).
  This is specifically why it has to be something *drinkable*: `CollectibleBehaviorHealingItem`'s
  `CancelApplication` already cancels a bandage application if the entity is airborne (and
  optionally while swimming), while drinking (`BlockLiquidContainerBase`'s instant drink
  interaction, Part 2 §4) isn't a channeled action and can't be interrupted the same way — real
  moments where bandaging genuinely isn't an option but drinking still is.

### Wound Infection

A separate, later-onset condition from Bleeding, not the same thing.

- **Not guaranteed on an unbandaged wound — a chance, not a rule, now fully numbered.** Base
  chance by the Bleeding tier that caused it: **Minor 5%, Moderate 15%, Severe 35%.** The chance
  is **rolled every 30 seconds** the wound stays unbandaged, and **each roll adds +5% to the next
  one, capped at +50% total** — so a Severe wound (120s duration, 4 rolls at the 30/60/90/120s
  marks) climbs from 35% toward its cap across those rolls, never guaranteed no matter how long
  it's ignored. This is also why Minor's duration was extended to 60s above — at 30s it would
  only ever get one roll, while Moderate (also 60s) gets two; matching roll counts across tiers
  was worth a small Bleeding-duration change to keep the system consistent.
- Uses a custom DoT effect type distinct from Poison/Bleeding — `ApplyDoTEffect`'s general
  overload takes a plain `int effectType`, not just the 3-value enum, so a 4th mod-defined
  "Infection" category needs no engine changes.
- **Does not clear on its own.** Once infection takes hold, it drains **0.05 HP every 3 seconds
  (~1 HP/min)** until either treated (antiseptic bandage/poultice) or the player dies. Slow
  enough that it isn't an immediate threat to a new player still getting established — real
  infections are dangerous and eventually fatal if genuinely ignored forever, not an instant
  death sentence — but it never goes away on its own, so eventually it has to be dealt with.
  Sepsis-style rapid deterioration is deliberately not modeled; the goal is teaching that
  infection is dangerous and demands action, not punishing a struggling new player.
- **Carries a secondary debuff beyond the DoT tick — -15% healingeffectivness, -10% walkSpeed**,
  via `EntityStats`, for as long as the infection persists (no fixed duration, since it doesn't
  self-resolve). A deliberately light penalty, consistent with the DoT rate above — noticeable,
  not game-breaking for a struggling new player.
- **Worsening, untreated Infection can additionally apply a broken-bones-*shaped* debuff on the
  affected limb** — work-speed/offhand-lock for Chest/Arms, movement-speed/immobilization for
  Legs — without actually pushing that limb into Part 4's tier system. Independent mechanic that
  reuses the same debuff shape Break already has, not a trigger that escalates the limb's injury
  tier — the two systems produce similar symptoms on purpose but stay mechanically separate.
- **Only prevented or cured by alcohol- or antiseptic-soaked bandages, or an antiseptic
  poultice, specifically** — plain bandages/poultices alone staunch bleeding (universal) but do
  **not** touch infection on their own. Two application paths, both using Antiseptic (Part 1
  cluster 1):
  - **Bandage**: `bandage-alcoholed` already exists, crafted by dipping `bandage-clean` in a
    container holding `alcoholportion` (itself distilled from `spiritportion`, the same
    barrel→boiler→condenser chain Part 2 reuses). This mod adds a parallel `bandage-antiseptic`
    variant crafted the identical dip way using Antiseptic in place of `alcoholportion` — same
    recipe shape, different liquid. **Carries the same drying penalty as `bandage-alcoholed`** —
    dries out and reverts to `bandage-clean` after about an hour, losing the benefit until
    re-dipped.
  - **Poultice**: vanilla's `poultice.json` is already a multi-variant item (`material`:
    linen/reed × `agent`: honey-sulfur/horsetail, each combination configured via
    `CollectibleBehaviorHealingItem`'s `propertiesByType`) — the same variant-group shape
    `bandage.json` uses. A JSON patch adds a third `agent` state ("antiseptic"), producing
    `poultice-linen-antiseptic`/`poultice-reed-antiseptic`, with its texture entry pointed at an
    already-existing agent's texture (no new art needed — visually identical to a normal
    poultice, as intended). Crafted via a **grid recipe**, not a dip — poultice (crafted first)
    + Antiseptic Potion Base (a solid item, not a liquid, so dipping doesn't apply here) →
    antiseptic poultice, consuming both. This is Potion Base's third use, beyond being processed
    into a full Potion or eaten directly. Curing/preventing Infection itself needs one small new
    attribute the vanilla `HealingItem` schema has no concept of (a plain custom marker, e.g.
    `curesInfectionByType`), read by the same general "any healing item's application just
    completed" hook that already makes bleeding-staunching universal — additive to a hook already
    planned, not a second new system.
  - **Only Antiseptic and Topical Ointment potion bases have any bandage/poultice-crafting path
    at all** — the other 6 remedy potions are drink-only, no physical-application use case.
    Topical Ointment's `treated-bandage` and treated poultice (Skin Irritation, below) are the
    bandage and poultice sides of that pair, symmetrical to Antiseptic's. Drinking Topical
    Ointment instead of applying it has its own backfire (Part 2 §6).
  - This is Antiseptic's *intended* use — drinking it straight (Upset Stomach's fourth trigger,
    below) is a deliberate easter egg for curious players, not the primary use case.
  - **Potency, now numbered**: applying a bandage/poultice made with regular Antiseptic clears
    the infection itself (stops the DoT, clears the infection flag) immediately on application,
    but the **-15% healingeffectivness / -10% walkSpeed debuff lingers, tapering off over 3
    in-game hours** rather than vanishing with it. A bandage made with **Concentrated** Antiseptic
    clears the infection *and* the debuff instantly, with nothing left to taper off — the tangible
    reason to bother distilling twice. **The poultice has no Concentrated tier** — it's crafted
    from Antiseptic Potion Base directly (a solid item, one step before Diluted Potion/Potion/
    Concentrated Potion even exist in the distillation chain), so there's no Concentrated Potion
    Base to craft a Concentrated poultice from; only the bandage gets the instant-cure path.

### Upset Stomach / Vomiting

- **Four independent triggers**: **overdosing on Antiseptic specifically** — its overdose effect
  (Part 2 §6) is a severe/guaranteed-complete version of this same condition, not a generic
  "any potion overdose" trigger (other potions have their own distinct overdose effects, not
  Upset Stomach); eating partially spoiled food (vanilla tracks food freshness as a continuous
  `TransitionState.TransitionLevel`, not a binary fresh/rotten flag, so "partially spoiled" maps
  onto a mid-range value); food poisoning from eating Discolored Meat (Part 3), which is the GI
  tract illness and spreads to nearby players when the sick player vomits (Tonic protects
  against catching it, below); and **drinking Antiseptic at a normal, non-overdose dose** —
  Antiseptic's baseline side effect is GI upset regardless of quantity, layered independently of
  its own overdose effect above. **This is a deliberate easter egg, not the potion's intended
  use** — Antiseptic is meant for crafting `bandage-antiseptic` or combining with a poultice, and
  drinking it straight is a discoverable "why would you do that" moment.
- **Actually cured by Antinausea** (Part 1 cluster 2) — drinking it calms the GI irritation,
  reduces vomiting symptoms, and is what genuinely lets the player eat again, rather than the
  condition only ever clearing passively on its own. The self-resolving shape below still applies
  if untreated; Antinausea is the active cure on top of that.
- **Potency, now numbered**: regular Antinausea **zeros the 25% relapse chance outright** — the
  player is cured, but still has to sit out the 5-minute recovery window before eating normally
  again ("stomach's still touchy" even though the sickness itself is gone). **Concentrated
  additionally skips the recovery window entirely**, letting the player eat again immediately.
- A vomiting episode calls `EntityBehaviorHunger.ConsumeSaturation(amount)` (already used
  internally by health regen) — either the entity's full current saturation (**complete void**)
  or a fraction of it (**partial void**).
- **Every episode starts as a partial void.** Escalating to a complete void only happens via a
  relapse — the original trigger's severity doesn't decide the first episode's outcome, only how
  likely a relapse is.
- **Two independent relapse paths, now numbered**: (1) a **25% chance** the partial void relapses
  on its own, and (2) a **5-minute "stomach still settling" recovery window** where *any* eating
  re-triggers vomiting, regardless of quantity or richness. **The window resets on every
  relapse**, whether from the passive chance or from eating during it — the player isn't clear
  until they go a full 5 minutes without a relapse.
- Interrupts whatever the player is currently doing — needs a small new behavior to cancel the
  active action/hand-use on trigger.
- **Clears on its own over time**, same self-resolving shape as Bleeding (not Wound Infection,
  which doesn't self-resolve, above).

### Sedative / sleep bypass

Not really a "cure" — a direct utility effect.

- `BlockBed.cs:67` gates sleeping on `EntityBehaviorTiredness.Tiredness <= 8f`, throwing the
  `"not-tired-enough"` error otherwise. `Tiredness` is a plain public settable float.
- A sedative dose sets `Tiredness` above that threshold directly — no workaround needed.
- Framed as "drink this to be able to sleep now, for up to 9 hours" rather than curing an
  "Insomnia" illness.
- **The sedative sets a ceiling, it doesn't replace bed quality.** Its job is bypassing the
  `Tiredness` gate so sleep can be attempted at all — how many hours are actually granted still
  comes from the bed's own `sleepEfficiency` (already vanilla, already scales hours by bed tier),
  just capped at 9. A better bed still matters even with the sedative in hand.
- **Potency, now numbered**: regular Sedative works, but the player wakes up dizzy — a minor
  `DrunkPerceptionEffect` (Part 2 §9's existing perception-effect reuse) lasting **5-10 in-game
  minutes** after waking. **Concentrated has no wake-up side effect** — the clean version, worth
  the extra distillation pass if the player expects to need to act right after getting up.

### Chest Cold (with escalation)

- **Two triggers**: environmental exposure (prolonged low body temperature/wetness — reuses
  vanilla's existing `EntityBehaviorBodyTemperature`) and contagion from another player (same
  room-based transmission mechanic described under Immune/Tonic, below).
- A debuff (stamina/breath penalty, coughing interrupts actions). **Antiviral** (Part 1 cluster 4)
  treats all four stages of this family — Chest Cold, Bronchitis, Flu, and Pneumonia alike.
- **Potency, now numbered**: regular Antiviral **reverses the illness by one stage** rather than
  curing it outright — Pneumonia drops to Bronchitis (always Bronchitis regardless of whether Flu
  or Bronchitis led there, the deterministic reversal target), Bronchitis or Flu drops to Chest
  Cold, and Chest Cold itself is simply cured. Treating Pneumonia down to nothing this way takes
  **3 regular doses** in sequence. **Concentrated Antiviral cures the current stage outright** in
  one dose, no matter how severe.
- **Chest Cold itself is fully recoverable without any medicine at all** — it's viral, and simply
  runs its course in **3 in-game days** if the player stays warm and dry. Escalation is driven
  entirely by continued cold/wet exposure while sick, not by time or neglect alone — the design
  goal is that Chest Cold on its own is never dangerous, but repeatedly going back out into bad
  weather while sick is genuinely risky:
  - Escalation is decided once, **the next morning (6:00)**, from the total cold-and-wet exposure
    accumulated since the illness began (or since the previous morning's check). Not a chance roll
    — a guarantee once the exposure is met. Each morning check closes that window and opens the next.
  - **2-4 in-game hours of cold-and-wet exposure while Chest Cold is active** progresses to
    **Bronchitis** by the next morning.
  - **4 or more in-game hours of the same exposure** progresses to **Flu** instead — a worse
    outcome than Bronchitis for the same underlying cause, just more of it. Under 2 hours, the
    Chest Cold simply continues.
  - **Bronchitis self-resolves after about a week** if exposure stops; **Flu self-resolves after
    about 3 days**. Both are recoverable without Antiviral, just slower and worse than a plain
    Chest Cold.
  - **From either Bronchitis or Flu, just 1 more hour of cold-and-wet exposure by the next morning
    progresses to Pneumonia** — the shared, most severe terminal stage of this family, and the
    one point where "just stay warm and it'll pass" stops being good enough advice.
  - Fever is Flu's signature symptom: a `walkSpeed` penalty that compounds with (not replaces)
    the `healingeffectivness` penalty already present at the more severe stages.

### Pain relief → product: **Analgesic**

Not a standalone condition — a symptom-severity modifier layered onto Bleeding, Wound Infection,
and fever/broken-bone pain specifically — Flu's fever and Skull-Strain's pain (not its
headache/confusion, that's Mind Tonic's — Part 1 cluster 5's stated primary use) — and
broken-bone pain generally (Part 4, Strain/Fracture on all locations, and pain from infection
generally) — rather than its own illness with its own cure/cause. **Hallucination and Temporal
Fog are not part of Analgesic's coverage** — both moved to Mind Tonic below, since neither is
actually pain.

**Potency, now numbered (walkSpeed component only, so far)**: regular Analgesic **halves the
walkSpeed penalty** on any condition that carries one — Wound Infection's -10% becomes -5%, and
the same applies at whatever stage Chest Cold's fever or broken-bone pain layer one in.
**Concentrated Analgesic clears the walkSpeed penalty entirely** while active. **Does not touch
Skin Irritation** — its debuff is healingeffectivness-only, with no walkSpeed component for
Analgesic to act on, so it's simply unaffected. The healingeffectivness side of any condition
(Wound Infection's -15%, Skin Irritation's -5%, etc.) is likewise untouched by Analgesic — that's
a separate axis this remedy doesn't reach. Headache/fever/broken-bone-pain magnitude numbers are
deferred until Part 4 gets its own numbering pass.

**Genuinely buys time, not just a symptom mask.** While a painkiller is active on
Hallucination/Temporal Fog/Flu's fever, it also pauses or slows the underlying illness's actual
progression — a painkiller suppressing Flu's fever doesn't just hide how bad it feels while
Flu→Pneumonia keeps advancing underneath; taking it genuinely holds the escalation back for as
long as it's active. A real stopgap while a proper cure is being brewed, not a trap that quietly
punishes the player for feeling better.

### Skin Irritation

A minor topical debuff (frostbite, sunburn, contact with an irritant plant — flagged in Part 1
cluster 6 as worth a deeper pass later to more fully nail down causes). Cleared by applying
`treated-bandage` — Topical Ointment (Part 1 cluster 6) dipped onto a bandage, same craft pattern
as `bandage-antiseptic`, or a treated poultice crafted from a poultice and Topical Ointment
Potion Base, same craft pattern as the antiseptic poultice. Mechanically, a **-5% `healingeffectivness` penalty** via `EntityStats` —
the same stat category used by Wound Infection and the Bronchitis/Pneumonia stages, just at a
much smaller magnitude, consistent with this being the least severe condition in the catalog.
**Self-resolves in 2-3 in-game days** if left untreated; Topical Ointment just clears it early.
**Potency, now numbered**: applying a `treated-bandage` or a treated poultice clears the debuff
instantly regardless of tier — regular and Concentrated Topical Ointment behave identically here,
no distinction worth inventing for the mildest condition in the catalog.

### Immune / Tonic (preventive, not curative) → product: **Tonic**

- Not a cure for an active condition — a temporary buff that reduces the chance of *catching*
  Chest Cold or Upset Stomach from another player.
- **Contagion mechanism: room-based, not raw proximity.** Reuses `RoomRegistry.GetRoomForPosition`
  (already used for cellar/greenhouse detection — no new room-detection code needed) to check
  whether two players are in the same logical enclosed room, and if so, whether they're within
  roughly 2-3 blocks of each other (a "social distancing" range) — both conditions must hold for
  a transmission chance to roll at all. Deliberate: makes quarantining a sick player in a
  separate room (or keeping distance within a shared large room) a real, mechanically effective
  countermeasure, not just flavor. Applies to Chest Cold and whatever else ends up spreading this
  way; food poisoning spreads by vomiting instead (Discolored Meat, Part 3), and Tonic's buff
  protects against that too. **Players-only** — NPCs and animals don't carry or transmit contagion (Chest
  Cold is still independently catchable from cold/wet exposure regardless, per its own trigger
  above, that's just not this mechanism).
- **Now fully numbered.** While both conditions hold, a transmission roll happens **every 30
  in-game seconds at a 5% chance per roll**. **Tonic's buff halves that chance (-50%) for 1
  in-game hour per dose** — worth drinking before a caretaking session, not a standing buff to
  keep topped off indefinitely.
- **Secondary HP-restore potency, now numbered**: checked against vanilla's own healing items for
  scale — bandages heal 3-7 HP and poultices 2-7 HP depending on variant, topping out at 7 HP for
  the best of either. **Regular Tonic restores 4 HP**, sitting mid-pack with vanilla's options;
  **Concentrated Tonic restores 8 HP**, exceeding the best vanilla item outright — the tangible
  payoff for running the full brewing pipeline instead of just carrying bandages.

### Confusion / Brain Fog — split into two conditions with a shared remedy

Confusion and Brain Fog are actually two different things that happen to share a cure: **Mind
Tonic** (Part 1 cluster 8), which reduces/removes Hallucination, Temporal Fog, *and*
Skull-Strain's headache/migraine (Part 4) all three. Skull-Strain's bone-injury headache shares
the same base vignette shader as Temporal Fog — edge gradient, rust-brown tint, chromatic
aberration, blur, continuous 0-100% throb — but each carries its own distinct periodic sub-effect
layered on top. Temporal Fog's is vein tendrils creeping in from the screen edges; Skull-Strain's
headache is a subtle horizontal shear of the scene itself instead, no veins — a concussion reads
as the world briefly swimming sideways, not as tendrils, so the two symptoms have visibly
different presentations despite sharing the same underlying gradient/throb.

**Potency, now numbered**: regular Mind Tonic **halves the base vignette strength** on whichever
condition is active — Temporal Fog (0.5→0.25) or Skull-Strain's headache (0.25→0.125);
**Concentrated Mind Tonic zeroes it entirely while active**, which also stops each one's own
sub-effect (veins or shear) since both scale off the same base strength/pulse envelope. Against
Hallucination's apparitions
(below), regular Mind Tonic **halves the max visible count (5→2-3) and doubles the roll interval
(20-30s→40-60s)**; **Concentrated stops spawns entirely while active** — the full, clean cure,
consistent with the halve/zero pattern used everywhere else Mind Tonic reaches.

**Hallucination**

- **Triggers**: **overdosing on Mind Tonic specifically** (its defined overdose effect, Part 2
  §6 — not a generic "any potion overdose" trigger), lack of sleep, *and* directly from Mind
  Poison (drunk or via a poisoned arrow, Part 3's poison effects table) — the same effect
  regardless of which trigger caused it, not separate mechanics that happen to look alike.
- **Effect: real wander/pathfind/attack AI, but no actual combat impact.** Drifter and Shiver
  apparitions wander a few points near their spawn (pausing between each), then path toward the
  player using real obstacle-avoiding pathfinding, close to melee range, and play their
  attack animation once — no damage is ever dealt, and the apparition despawns right after that
  animation finishes. Bowtorn spawns too, but stays invisible for its entire lifetime (see its own
  entry below). Candidates are drawn from any tier of any entity carrying the `rust-creature` tag —
  confirmed to be exactly three families: `drifter` (normal/deep/tainted/corrupt/nightmare/
  double-headed), `bowtorn` (surface/deep/tainted/corrupt/nightmare/gearfoot), `shiver` (surface/
  deep/tainted/corrupt/nightmare/stilt/bellhead/deepsplit) — 20 variants total (6 + 6 + 8). Asset
  domain is `game:`, not `survival:` — the physical folder these
  assets ship under (`assets/survival/...`) doesn't determine the runtime domain; verified against
  the mod's own already-working patches that target `game:` paths physically stored under
  `assets/survival/`. Locust is thematically similar but tagged `mechanical`, not `rust-creature`,
  excluded by the game's own taxonomy. Filtering by tag rather than a hardcoded list means it
  automatically picks up any rust-creature the base game adds later.
- **Spawn conditions, now numbered**: weighted toward vanilla's normal mob-spawn conditions
  (light level, time of day, etc.) but **deliberately ignores the real rift-activity/Temporal
  Stability gating** `ModSystemRifts`/`ModSystemRiftWeather` normally require — apparitions can
  spawn on an otherwise calm, clear night, since they aren't real rift spawns at all.
- **Distance (Drifter/Shiver)**: enclosed/underground spaces spawn apparitions **5-10 blocks**
  away; outdoors, **15-50 blocks**, scaled by line of sight — closer in wooded/brushy terrain,
  farther when the ground ahead is clear. Always placed within the player's current field of view.
- **Bowtorn: spawns invisibly, and deliberately never in view.** It spawns a real entity, kept
  invisible for its entire lifetime, at a point **13-17 blocks away, randomized within the
  half of the world behind the player's current facing** — the opposite placement rule from
  Drifter/Shiver, on purpose. From there it plays its own real windup sound (the sound the real
  creature makes drawing its bow before firing), waits 5 seconds, then despawns — never
  revealed at any point, so this one is never something you catch sight of, only something you
  hear and can't immediately place.
- **Client-side-only, visible to no one but the affected player.** Genuinely private to that
  player — never added to any server-side chunk entity list, never sent over the network to any
  other client, not a real multiplayer-visible entity with visibility hacked around it. (The
  normal `IWorldAccessor.SpawnEntity` call does not actually work for this — it's an empty method
  body on the client. See the implementation notes for the real mechanism.)
- **Roll every 20-30 in-game seconds, up to 5 apparitions visible at once** — deliberately not
  a single stray spectre; five drifter-shaped figures at once would be a real threat if they
  were genuine, which is exactly the moment of alarm this effect is going for. Each roll picks one
  of the three families (subject to Mind Poison's own severity scaling, below) - Bowtorn's invisible
  entity still counts against the 5-concurrent cap like the other two, even though nothing renders.

**Temporal Fog**

- The Temporal Stability HUD misreport idea was dropped entirely — replaced with a real visual
  effect instead of a cosmetic-lie display trick.
- **Triggers**: *prolonged* exposure to low Temporal Stability (duration matters, not just the
  instantaneous value), using a Temporal Gear to restore stability (which itself carries a
  Temporal Fog risk as a side effect of the cure), **overdosing on Mind Tonic** (Part 2 §6 lists
  this as an either/or with Hallucination for Mind Tonic's overdose — which of the two triggers
  is a balance/randomization detail, not decided here), and **Mind Poison** (drunk or via a
  poisoned arrow) — Mind Poison's confusion/mind-fog drives both Temporal Fog's screen
  wobble/vignette and Hallucination's apparition spawns together, not a choice between them the
  way Mind Tonic overdose is.
- Only the client/server sync channel and a per-position stability read have been verified against
  source so far - the specific tiered-threshold structure this document wants to mirror has not
  been confirmed and needs its own read of `TemporalStabilityEffects.cs` (past its sync-channel
  setup) and `SystemTemporalStability.cs` before any implementation plan treats its exact
  thresholds as precedent.
- **Effect: screen wobble + a genuinely new custom vignette — not a `FrostVignetting` reuse.**
  Checked the actual shader (`assets/game/shaders/final.fsh`): `frostVignetting` isn't a plain
  darkened edge at all — it runs gradient noise (`gnoise`) to paint an icy, crystalline frost
  *texture* at the screen edges, tinted pale blue-white. Vanilla's other vignette,
  `damageVignetting` (blood-red, same noise-texture approach), has the same issue. Neither
  reads as "temporal distortion" under a simple re-tint, so Temporal Fog gets its own effect
  instead: a **smooth gradient vignette from the screen edge inward** (no noise texture, unlike
  either vanilla vignette), **tinted to match temporal rifts' own rust-brown screen-distortion
  color** (not violet/magenta — checked `rift.fsh`'s actual color math: no fixed rift color
  exists in vanilla, just a dark navy-blue particle color and a double-sepia channel-remix that
  shifts the screen rust-brown; the vignette uses the latter, `RGB(0.82, 0.73, 0.57)`, derived by
  running that same matrix against neutral gray), layered with a **chromatic aberration** pass
  and a **mild blur**. This is genuine new shader work, not a reuse — implemented as a
  custom fragment shader registered through the mod's own `IShaderAPI`/render-stage hook (the
  same modding surface other client-side visual mods already use for full-screen post-process
  effects), not a patch onto `final.fsh` itself. Camera wobble still reuses
  `FreezingPerceptionEffect`'s `ApplyMotionEffects` simplex-noise approach for
  `capi.Input.MouseYaw`, driven by Temporal Fog's own mod-defined watched attribute — that part
  remains a genuine near-exact reuse, unlike the vignette.
- **Numbered: one unified strength value (0-1) drives vignette opacity, chromatic aberration
  offset, and blur radius together** — a single dial rather than three separate magnitudes to
  balance. Temporal Fog sits at **strength 0.5**, lasting **10-15 in-game minutes per trigger** —
  clearly noticeable without being disorienting to the point of unplayable.

### Liver Disease (chronic overconsumption — distinct from Liver Failure)

- **Trigger 1: eating too much cooked bushmeat**, specifically — not protein/meat generally.
  Vanilla already tracks per-food-category overconsumption
  (`EntityBehaviorHunger.SaturationLossDelayProtein` and siblings — the real mechanism behind
  vanilla's "eat a varied diet" nudge), but that value is shared across every Protein-category
  food (bushmeat, redmeat, poultry, fish), so it can't be reused directly without also catching
  those. This needs its own dedicated counter, tracking cooked-bushmeat consumption specifically
  (`bushmeat-cooked` — `bushmeat.json` has no per-animal sub-variant, one singular meat type,
  distinct from redmeat/poultry/fish as separate items entirely). **Numbered: a liver toxicity
  meter** — each cooked bushmeat eaten adds **0.25**, and the meter drains **1 per in-game day**
  (decayed against the game calendar, so it follows calendar speed and keeps draining across
  saves and relogs). Liver Disease starts once the meter reaches **3**; Liver Failure starts once
  it reaches **6**.
- **Trigger 2: overdosing on Tonic specifically** — Tonic's defined overdose effect (Part 2 §6)
  *is* Liver Disease directly, not a generic "any potion overdose" outcome; overdosing on other
  potions produces their own distinct effect instead (§6), not Liver Disease. Reuses the
  *existing* toxicity/overdose counter (Part 2 §5) directly, and Concentrated Tonic doses push
  that counter faster than regular Tonic doses, same as every other potion.
- **Deliberately not the same effect as Liver Failure at onset** — Liver Failure stays reserved
  for the deadly amatoxin mushrooms as its primary trigger. This starts as a separate, milder,
  chronic-overconsumption condition.
- **Numbered debuff**: **-10% `healingeffectivness`, -10% saturation gain from food, and -2.5
  max health** (via `EntityStats`' max-health-modifier path, same mechanism as Cardiac Poison's
  exertion stacking) — the max-health hit specifically reflecting toxin buildup rather than being
  just another flat penalty layered on top.
- **Fades on its own, no medicine needed**: both Liver Disease and Liver Failure last until the
  toxicity meter has drained back to **0** (about 3 days from the Liver Disease threshold, 6 from
  the Liver Failure one) or the player dies, whichever comes first. Eating more bushmeat while
  affected pushes the meter back up and extends it.
- **Liver Failure from this path** is a second on-ramp into Liver Failure, but a much gentler one
  than the amatoxin mushrooms': it starts immediately at the threshold with no onset delay,
  drains **0.1 HP/sec** (about 2.5 minutes from full health), and its damage is silent, like Wound
  Infection's. It ends when the meter reaches 0, on death, or on an Antidote's second dose, which
  stops the drain and drops the meter just under the Liver Failure threshold; Liver Disease is
  left to fade with the meter.
- Raw bushmeat cannot be eaten at all — `bushmeat.json`'s `nutritionPropsByType` only defines
  `*-cooked` and `*-cured` entries, no `*-raw` and no wildcard fallback, so the eat interaction
  has nothing to act on for the raw item. This condition can only ever be triggered by the cooked
  form.

### Resurrection Sickness

Triggers on any player death, unconditionally — no attempt to distinguish an intentional
self-inflicted death from a genuine accident. `EnumDamageSource` does have a real `Suicide`
value (set only by the `/kill` command, which regular survival players have access to by
default), which would be a 100% reliable signal for that one specific case — but every other
self-inflicted death (falls, lava, drowning) is indistinguishable from a genuine accident, and a
badly afflicted player (broken legs, impaired vision from Temporal Fog/Hallucination) is
*already* more accident-prone than a healthy one. Trying to detect intent would end up punishing
exactly the "genuinely struggling" players this is meant to protect, so death itself is the only
trigger — no remedy, no cure, it simply runs its course.

- **Resurrect at exactly 50% Temporal Stability**, regardless of what it was before death.
- **Lasts 1 in-game day.**
- **Temporal Stability regenerates 75% slower while active** — modeled as a multiplier on the
  existing regen math (`EntityBehaviorTemporalStabilityAffected.OnGameTick`'s velocity-based
  `gain` calculation, Part 3's engine-hooks convention of layering a modifier onto existing
  systems rather than replacing them), not a hand-rolled replacement formula.
- **Drain rate is untouched** — storms, rifts, and low-stability zones drain Temporal Stability
  at the normal rate throughout. Only recovery is dampened, encouraging a resurrected player to
  stay somewhere stable and let it recover naturally rather than making the outside world
  actively more dangerous on top of an already-bad respawn.

## Poison effects (caused by poison clusters)

`EnumDamageType.Poison` is already the damage type vanilla itself uses for a harmful
poultice-shaped item (`ItemPoultice` with a negative `health` attribute applies
`EnumDamageType.Poison`) — poison effects delivered as items/doses are already aligned with an
existing engine convention. Hallucination is the harmless apparition-spawn effect (Part 3
Confusion/Brain Fog above, not a perception-effect reuse); Temporal Fog's camera wobble reuses
`FreezingPerceptionEffect`'s `ApplyMotionEffects` approach but its vignette is a genuinely new
custom shader (smooth gradient rust-brown-tinted to match temporal rifts' own screen-distortion
color, chromatic aberration + blur — not a `FrostVignetting` reuse, see Part 3 Confusion/Brain
Fog above for why); and dizziness reuses `TemporalVignetteRenderer`'s own
`DrunkWobbleStrength`/`ApplyWobble` sway, a custom mechanism that only borrows vanilla
`DrunkPerceptionEffect`'s multi-sine yaw/pitch curve shape — not a hook into vanilla's own
`intoxication`-attribute system, which isn't independently parameterizable per-poison — so it
needs no new shader work, just a new strength input.

| Cluster | Product | Coated-arrow effect | Drinking effect |
|---|---|---|---|
| Deadly Hepatotoxic | **Toxic Poison** (= Liver Failure) | +1.5 HP flat bonus damage per hit, applied immediately, plus a **chance** to also inject a smaller version of the delayed DoT below. **Hunting risk**: if the smaller DoT is what finishes off a killed animal, it drops Discolored Meat instead of its normal meat (Discolored Meat, below) — the poison taints it, deliberately making poisoned-arrow hunting a real risk (faster potential kill vs. a dangerous, tainted carcass) rather than a free efficiency upgrade | Delayed onset with a mild early warning (small `healingeffectivness` dip during the delay window), then **1.5 HP/sec continuous drain — no natural end, only stops via Antidote or death**. Two triggers: the deadly mushrooms directly, or a liver toxicity meter built up by eating cooked bushmeat (see Liver Disease, which uses a gentler, meter-bound version of this drain) |
| GI-toxic / Emetic | **Noxious Poison** (= Food Poisoning) | High chance of the same as drinking | Fever + genuine psychedelic tripping (vanilla's own `psychedelic`-attribute mushroom effect, not the mod's Hallucination condition) + vomiting (via the same `ConsumeSaturation` mechanic as Upset Stomach), lasting 24h, ends via Antidote or death from other causes. **No DoT** — deliberately not lethal on its own; not every poison needs to be a death spiral, some should be annoying and disruptive but survivable on purpose |
| Cardiac Glycoside | **Cardiac Poison** (= Cardiac Strain) | **25-35% chance** of cardiac arrest: -5 current *and* max HP (instant death if current HP is already below 5), heavy movement-speed debuff, considerably slowed tool use, 6h duration, may lead to death. Exertion stacking below applies the same way | **100% guaranteed** cardiac arrest, same effect. Trigger model changed from the original design: no longer gated behind exertion — it fires directly on drink/hit, with exertion now a *worsening* factor (both duration and severity) rather than the sole trigger, see below |
| Neurotoxic / Alkaloid | **Neurotoxic Poison** (= Weakness/Paralysis) | **25-35% chance** per hit of contributing toxicity toward the escalation ladder — each successful proc adds **half a stage's worth** (two arrow procs ≈ one drunk dose) | The 3-stage escalation ladder (numbered below): Weakness → Paralysis → Cardiac Arrest. **Each drunk dose advances the ladder by exactly one full stage**, expressed as toxicity-per-dose on the same counter used for potion overdose (Part 2 §5), which is already weighted rather than a raw dose count, so this needs no new mechanic. **Also causes headache and dizziness** (mouse-sway via `TemporalVignetteRenderer.DrunkWobbleStrength`) throughout, at every stage. Confusion and mind-fog are *not* part of this poison — moved to Mind Poison below, since those specifically drive Hallucination/Temporal Fog rather than this ladder's physical debuffs |
| Psychoactive | **Mind Poison** (= Hallucination) | **40-55% chance** of the same as drinking — higher than Cardiac/Neurotoxic's 25-35% since Mind Poison is non-lethal by design, less reason to gate it as tightly | Confusion and mind-fog — the actual driver of both Hallucination's apparition spawns *and* Temporal Fog's screen wobble/pulsating vignette together, not just one or the other — plus genuine psychedelic tripping and dizziness. **Attempting to move during the effect has a 15-25% chance per attempt of inducing vomiting.** Doubles hunger rate. Lasts 24h, ends via Antidote or death from other causes. **No DoT, deliberately non-lethal by design** — same as Noxious, and per the same rule (below) |
| Situational/Conditional | *(reuses Noxious Poison at lower severity, no dedicated effect)* | — | — |

**Standing rule**: a poison is only lethal-capable if its own description explicitly says so
(death, cardiac arrest, "no natural end," or similar). Toxic, Cardiac, and Neurotoxic all state
this explicitly and stay lethal-capable; Noxious and Mind Poison don't, and are deliberately
non-lethal by design — annoying and disruptive, not a death spiral. Applies to any future poison
added to this roster too, not just the current five.

**Cardiac Poison's exertion-stacking mechanic, numbered**: exertion — sprinting or using any
tool — during an active episode adds a stack roughly every 10 seconds of exertion (a per-attempt
cooldown between stacks, not a free-form tick); ordinary walking is exempt and never stacks.
Each stack adds **+1 hour to the duration and -5 HP to both current and max health**, with
**no cap on stack count** — it's always eventually lethal no matter how much health the player
has, just a matter of how long they keep exerting. On the base game's default 15 max HP,
avoiding exertion isn't optional: the initial hit already costs -5, one stack brings a fresh
player to 5/5 (right at the instant-death threshold), and a second stack ends it — roughly 15-20
seconds of continued exertion from full health. Players with more max health (from nutrition's
`maxhealthExtraPoints` bonus, or from other mods) get proportionally longer before the same
uncapped stacking catches up with them — the mechanic scales with the player's health pool
without needing separate tuning for modded/high-health setups.

**Neurotoxic Poison's 3-stage ladder, numbered**: dose #1 applies **Weakness** — a `walkSpeed`
penalty lasting a baseline 6h, scaled by the dose's own effect multiplier and discounted by
Neurotoxic tolerance (per Tolerance above). Dose #2 (**Paralysis**) layers a second,
independently-timed Weakness-strength instance on top, at double the base duration (12h baseline)
— while both instances overlap the combined penalty reads as full paralysis; the original instance
still expires on its own schedule underneath. Dose #3 (**Cardiac Arrest**) adds Cardiac Poison's
own effect package on top of whatever's still active — the same -5 current/max HP hit,
movement-speed debuff, and slowed tool use, but at quadruple the base duration (24h baseline, same
discounted multiplier) instead of Cardiac Poison's normal 6h — and **inherits Cardiac Poison's
exertion-stacking rule**: sprinting or using any tool during this stage adds stacks the same way,
worsening duration and HP loss, uncapped; ordinary walking is exempt. A player who avoids exertion
can survive a full 3-dose ladder; one who sprints or works through it risks the same uncapped
death spiral Cardiac Poison itself has.

**Mind Poison's Hallucination severity scaling, numbered**: the same tolerance-discounted effect
multiplier (0-1+) that already scales Temporal Fog's screen wobble/vignette strength, the doubled
hunger rate, and the vomiting-attempt chance also gates which Hallucination apparition families can
spawn, rounded into three windows: **0-0.33** — Drifter only. **0.33-0.66** — Drifter and Shiver,
chosen at random per spawn. **0.66 and above** — Drifter, Shiver, and Bowtorn all become possible.
Each window strictly adds a family on top of the previous one rather than replacing it, so rising
severity only ever adds variety, never removes Drifter from the pool. Bowtorn's own spawn behavior
is defined independently of this scaling (see its own section) and applies the same way regardless
of which window unlocked it. Hallucination's other two triggers (Mind Tonic overdose, sleep
deprivation, both above) have no equivalent continuous effect value to scale from, since neither
is a dose with its own tolerance-discounted magnitude — those two instead **roll one of the three
windows at random** each time they trigger, rather than defaulting to a fixed tier.

## Discolored Meat — the poisoned-kill hunting risk

Meat from an animal finished off by Toxic Poison's arrow-delivered smaller DoT (Toxic's hunting
risk, above) is tainted by the poison rather than lost.

- **What drops.** Raw red meat drops as **Discolored Red Meat**; every other drop (hide, fat,
  antlers, bones) is normal. Raw bush meat drops as **Discolored Bush Meat** when the
  `discoloredBushMeatEnabled` server setting is on (default off), and is removed otherwise, since
  bush meat already carries its own eating risk. Other meat drops are removed. There is no
  discolored poultry or fish — chickens die to the arrow itself, and fish do not drop meat — but
  a chicken that the poison does finish off pays out **double** normal poultry, an easter egg.
- **The poison can't be cooked or cured out.** Discolored meat has three tiers — raw, cooked
  (grilled in a fire) and cured (salt in a barrel, 480 in-game hours) — and each keeps the mark.
  It is accepted wherever the vanilla equivalent is, and nowhere that would turn it into food
  that can't carry the mark: cooking-pot soups and stews (raw and cured red meat, like vanilla),
  stinkbait, the recurve bow recipe (bush meat), and the barrel cure. It is not accepted in
  pies, and pemmican, which is disabled in the base game, would not accept it either.
- **Spoilage.** The raw and cooked tiers last twice as long as vanilla's raw and cooked meat
  (raw: 72 in-game hours fresh, then 48 to rot; cooked: 240, then 72); the cured tier matches
  vanilla's.
- **Eating it risks food poisoning.** Every eating action that involves discolored meat — one
  plain piece, or one meal containing it, whatever its servings — adds a random amount between 0
  and 1 to a toxin total stored on the player. When the total reaches 1 or more, the player gets
  **food poisoning** and the total resets to 0, so the first meal or two may do nothing while the
  toxins build up. The total is cleared on death. Food poisoning is the GI tract illness: its
  symptom is Upset Stomach (Part 3), and it spreads to nearby players when the sick player
  vomits. The range, the chance per vomit, how long the illness lasts and how it is cured are not
  settled yet.
- **Other interactions.** Cooked discolored bush meat counts toward Liver Disease's toxicity
  meter like any cooked bush meat. A soup containing discolored red meat counts as containing
  protein for the Antidote's restricted-diet window, like soup with raw red meat.

## Poison mitigation: Universal Antidote

Nothing else in this design lets a player counter a poison once it's already in their system.
Some poisons (Toxic, Cardiac) have **no natural end at all** — only the Antidote or death stops
them; others (Noxious, Neurotoxic, Mind) run a fixed duration but are severe enough that waiting
them out is rarely the better option. **One universal Antidote**, not per-poison-type specific
ones — simpler to build and to remember as a player than a matching antidote per poison cluster.

### Recipe — a deliberate exception to the usual substitution rule

**Field Mushroom + Red Wine Cap + Bitter Bolete**, exactly one 6-unit stack of each — **not** the
usual "any mix of the cluster satisfies the 3 stacks" rule from Part 2 §1. The three mushrooms
are doing conceptually different things (absorbing different poison mechanisms), not standing in
for each other, so the recipe requires all three specifically. Field Mushroom and Red Wine Cap are
both genuinely Rare (`chance: 0.08`, Part 1); Bitter Bolete is meaningfully more common
(`chance: 1.2`, Uncommon tier) — not the equally-rare trio the recipe was originally picked around,
so the "hard to stockpile" framing rests more on Field Mushroom and Red Wine Cap than on all three
equally.

Follows the normal tier rules otherwise — the Potion Base can be eaten directly for a
short-duration version of the effect, but Diluted Potion stays a non-functional intermediate; a
real dose requires reaching at least the Potion tier through the full Barrel → Distillery chain.

### Effect — a deliberate two-dose sequence

Not a single-drink cure. The Antidote must be consumed twice in sequence to work:

1. **First dose induces vomiting** — a real, intentional side effect, acknowledging that Bitter
   Bolete itself carries genuine toxicity (one of the mild-toxin mushrooms in Part 1's own poison
   research) — the cure makes you sick before it can work.
2. **Second dose is when it actually takes effect.** The two doses must land within roughly one
   real-world minute of each other, or the sequence resets and both doses are wasted. **Consuming
   anything else at all between the first and second dose — a new poisoning, any potion, or any
   ordinary food or drink — also resets the sequence.** Only the Antidote's own two doses ever
   advance the cycle; literally anything else consumed in between means starting over from a fresh
   first dose. Once the second dose successfully lands as the second in sequence, it:
   - **Cures every currently active poison outright, all at once**, including any still in a
     silent pre-onset delay — stops an in-progress Weakness → Paralysis → Cardiac Arrest ladder,
     ends every active Toxic Poison drain, clears whatever poison state is currently running or
     pending, regardless of how many separate stacked instances are active (Stacking above). A
     full reset.
   - **Imposes a 2-in-game-hour restricted diet, not a total lockout.** Solid food is still
     off-limits, but **drinks are allowed, restricted to broths and fruit juice — no alcohol.**
     Eating solid food, or drinking alcohol, during the window **re-triggers vomiting**.
   - **Any potion drunk during the window has a 50% chance of triggering vomiting and voiding
     that potion's effect entirely** — a strong disincentive against chaining potions right after
     the Antidote.
   - **Grants poison immunity for the same 2-hour window against external sources too** — e.g. a
     poisoned arrow hitting the player during this window does not apply its poison effect.
   - **Also applies a 15% penalty to melee damage dealt and healing effectiveness, for the same
     2-hour window** — the purged poisons are gone, but their lingering aftereffects aren't instant
     to shake off. Runs on the same single timer as the restricted diet and poison immunity above,
     not a separate duration. Distinct from Neurotoxic Poison's own named Weakness stage (a
     `walkSpeed` penalty, above) — this is a different debuff with the same plain-English name,
     not a reuse of that mechanic.

---

# Part 4: Broken Bones

Fully numbered — Pain relief, the Hallucination/Temporal Fog split, and `EntityStats`/DoT/
max-health-modifier machinery are all reused here rather than reinvented. **Splinting** (physical
first aid — 4 sticks + a clean linen bandage or 3 ropes, below) is a genuine remedy for broken
bones, layered on top of Pain relief rather than replacing it — not tied to any potion, but still
a treatment path in its own right.

**Reverse borrowing**: worsening, untreated Wound Infection (Part 3) can independently apply this
same work-speed/offhand-lock (Chest/Arms) or movement-speed/immobilization (Legs) debuff shape on
the affected limb, without actually pushing that limb into this part's tier system. The 2
mechanics share a debuff shape on purpose but stay separate; an infected limb reading as
symptom-similar to a broken one doesn't mean it's tracked as broken.

## Engine grounding

- **Hit height exists only for some attacks.** `DamageSource` (the object passed to
  `Entity.ReceiveDamage`) carries a `HitPosition` field — the hit's height above the victim's
  feet. Vanilla sets it only for player melee (and one cleaver item). It is not set for creature
  attacks, projectiles and thrown items, falls, explosions, fire, and so on; lightning sets a zero
  vector that carries no information. A player is 1.85 blocks tall (eye height 1.7). Attacks
  without height data are located by the fallback rules under "Hit location", below.
- **Vanilla armor picks its slot at random.** When a player is hit, vanilla armor protects with
  *one* randomly chosen slot (20% head, 50% body, 30% legs), and only for Blunt, Piercing and
  Slashing damage whose source is not Internal/Suicide — never for falls, fire or poison.
  `itemtypes/wearable/seraph/armor.json` defines a `bodypart` variant group with states
  `head`/`body`/`legs`, so armor is already equipped per location; this mod replaces the random
  slot choice with the slot of the hit's location (below).
- **`SetMaxHealthModifiers(string key, float value)`** (`EntityBehaviorHealth.cs`) is a keyed,
  stackable max-health modifier — exactly what Skull Fracture's -10 max health needs. Vanilla does
  not persist these modifiers, so the Skull Fracture modifier is re-applied at login and current
  health is limited to the new maximum. When the modifier is removed while a player is at full
  health, vanilla fills them to the new maximum.
- **`DamageOverTimeType`** on `DamageSource` has a doc comment saying it is used to look for
  specific types of DoT effects, for example to stop bleeding with bandages — the same shape this
  mod's Bleeding mechanic uses.

## Hit location

Every hit is classified once into one of the 3 locations below, and that single classification is
shared by armor and by bone injury.

- **Height cutoffs** are fractions of the victim's *current* height: Legs below 0.9/1.85 of it,
  Skull above 1.5/1.85, Chest/Arms in between — so a sneaking or sitting player still has a head.
  A height within a hair of a cutoff picks randomly between the 2 neighbouring locations.
- **Hits with no height data.** Creature melee uses an estimate of 0.6 × the attacker's
  selection-box height above its feet (plus its vertical offset to the victim), clamped to the
  victim's body. Projectiles and thrown objects use the point where their flight path enters the
  victim's collision box (a ray from the projectile along its motion), falling back to the
  projectile's own height.
- **Fixed rules.** Falls, the Eidolon's ground slam, touch damage from sharp blocks and a mount's
  saddle-break throw land on Legs. A falling block lands on Skull. Explosions land on a uniformly
  random location and can injure. Hail is treated as hitting the head (for armor) but never
  injures bones. Polar bears land randomly on Chest/Arms or Skull. Bowtorns (melee and arrows) and
  Erel (swoop, dive and feathers) always land on Skull. Bees land randomly on Legs or Chest/Arms.
  The Eidolon's 360-degree sweep lands on Chest/Arms. A hit with nothing usable lands on
  Chest/Arms.
- **Never injures a bone**: fire, poison, hunger, cold, lightning, drowning/suffocation, void,
  bleeding and other damage-over-time ticks, and self-inflicted injury never roll a bone injury.

## The 3 locations and their tier ladders

| Location | Tiers |
|---|---|
| Chest/Arms | Strain → Fracture → Break → Crush |
| Legs | Strain → Fracture → Break → Crush |
| Skull | Strain → Fracture → **(any escalation past Fracture = death)** |

Skull has no Break/Crush equivalent — any escalation that would end above Fracture is fatal
outright (brain injury), not a named injury tier. A healthy skull dies from a 3-tier jump, a
Strained skull from a jump of 2 or more, and a Fractured skull from any successful escalation roll.

## Tier progression: escalation, not independent rolls

**A limb's tier advances through repeated qualifying injury to that same limb, not a fresh
independent severity roll per hit — and "injury" isn't only new hits.** 2 distinct sources can
trigger an escalation event: **taking a new qualifying hit to that limb**, or **using an
unsplinted injured limb** — swinging a tool with a Strained arm, walking on a Fractured leg, etc.
**Splinting exists specifically to remove most of that second source**: a splinted limb takes no
use risk apart from jumping and hard landings on an injured leg (below), though a fresh direct hit
to it can still push it further regardless of splinting. **Leaving pain untreated does not, by
itself, advance the tier** — Analgesic only manages the pain symptom, it was never the on-ramp.

**Hit-triggered escalation: 1 unified magnitude formula, no damage-type gating.** A qualifying
hit's damage (after armor mitigation, same convention Bleeding's severity tiers use) maps directly
to how many tiers it can push a limb, applied against the limb's *current* tier — not gated behind
needing to already be 1 tier below. A small hit can only ever manage +1 tier; a genuinely massive
one can jump a healthy limb straight to Crush, skipping Fracture and Break entirely, the same way
a nearly-fatal fall can. This is deliberately *not* split by damage type (`SlashingAttack` vs
`BluntAttack` vs `Gravity`, etc.) even though vanilla's rust creatures only ever deal
`SlashingAttack` — **vanilla's 1.23 combat update is expected to let enemies deal multiple damage
types depending on how they attack, which is the natural point to revisit splitting this by
type.** Until then, magnitude alone decides the jump size for every damage source uniformly —
melee still reads as mostly-sequential in practice simply because most hits are small relative to
what a fall or a high-tier monster's claw can deal, not because of a hard rule forcing it.

**The formula reuses Bleeding's own damage-taken bands (Part 3) for consistency, plus 1 band
above Severe for genuinely catastrophic hits:**

| Damage taken (post-armor) | Chance to escalate | Jump size on success |
|---|---|---|
| Minor, <4 | 5% | +1 tier |
| Moderate, 4-8 | 15% | +1 tier |
| Severe, 8-12 | 30% | +1-2 tiers |
| Catastrophic, 12+ | 50% | +2-3 tiers |

Catastrophic's 12+ threshold is grounded, not arbitrary — it matches the corrupt drifter's own
hit value (`drifter.json`, the same reference point Bleeding's tiers and Cardiac Poison's numbers
use), roughly 80% of base 15 max HP in 1 hit. This is a percentage-chance roll,
not a guaranteed jump — armor's role is reducing how much damage reaches this formula in the
first place (moving a hit down a band or out of qualifying range entirely), not a separate
success roll layered on top. A fully absorbed hit rolls nothing.

**Post-armor damage is shared with Bleeding.** Bleeding and bone injury both read the damage
*after* armor. Bone injury is evaluated before Bleeding converts half of the hit into a bleed, so
it sees the full post-armor damage. Bleed ticks use a damage type that armor ignores, so armor and
clothing are not worn down by every tick.

**Splint and healing interaction.** A hit-triggered tier rise **breaks the splint** of a
Chest/Arms or Legs limb — it must be splinted again, and its healing countdown starts over at the
new tier's duration once it is. A Skull has no splint. A qualifying hit on a limb that does not
escalate it (roll failed, or already at Crush) while it is healing sets its healing back by 10% of
that tier's duration (Healing duration, below).

**Use-triggered escalation: a flat 5% chance per qualifying event** (swinging a tool with a
Strained arm, walking on a Fractured leg, etc.), unaffected by the hit-magnitude formula above —
mirrors Minor's own hit-triggered chance for consistency. Always **+1 tier on success**, never a
multi-tier jump — unlike a hit, "use" carries no magnitude to scale a bigger jump off of. It only
applies while the limb is injured; creative and spectator players never roll, and neither do
mounted players for leg use.

- **Arms**: hitting a creature, breaking a block, or placing a block — only while the arm is
  unsplinted.
- **Legs, footsteps**: only while the leg is unsplinted, counted per N steps, where N is 10 for
  Strain, 5 for Fracture, 3 for Break and 2 for Crush (halved while sprinting, doubled while
  sneaking, never below 1).
- **Legs, jumps and hard landings**: a jump (taken from the ground) and a hard landing (a fall
  that deals fall damage — an extra roll on top of the fall's own hit roll) roll for any injured
  leg, splinted or not.

## What each tier does

3 tracks: Chest/Arms (4 tiers), Legs (4 tiers), Skull (2 tiers, then death). Chest/Arms and Legs
share the Strain/Fracture/Break/Crush tier *names* but each has its own penalty type below. Skull
carries its own distinct symptoms — headache/brain-fog/confusion and the -10 max health
penalty (at Fracture) are Skull-only, not something Chest/Arms or Legs injuries ever cause.

A tier raised to Fracture or higher plays the game's tool-break sound, and every tier rise shows
a message (an on-screen warning plus an optional chat line, controlled by a server setting).

### Chest/Arms (4-tier track)

**Strain**: Pain — digging speed and melee damage drop to **95%** (Analgesic halves the pain
penalty). Otherwise fully usable. **Analgesic manages the pain symptom only — it does not heal the
injury.** Splinting is what actually lets a Strain heal; left unsplinted, using the arm risks the
use-triggered escalation roll toward Fracture (above).

**Fracture**: The same pain symptom, dialed up — digging speed and melee damage drop to **90%**
(Analgesic halves the pain penalty), not a new penalty type. Still fully usable, just more painful
than Strain — "a fractured limb is still a working limb." Same deal as Strain: Analgesic only
manages the pain, splinting is what heals it and what blocks the use-triggered escalation roll
toward Break.

**Break**: a broken arm means everything is done one-handed. Digging speed and melee damage drop
to **50% unsplinted** and **60% splinted**. Digging speed is computed on the client and scaled
there; melee damage uses the player's melee damage stat. The offhand depends on the splint:

- **Unsplinted**: the offhand is locked completely — nothing may enter it, and an item already
  there is dropped. Sticks are the one exception and are always allowed, because they are the
  splint material.
- **Splinted**: the offhand is unrestricted. But each time a shield blocks a hit while the arm is
  splinted, there is a **25% chance the splint breaks** and the shield is forced out of the hand
  and dropped (the block does not raise the tier). A broken arm can carry something; it can't
  safely brace or block with it.

Splinting a Break is still required for it to actually heal. **No separate internal Break
graduation** — the hit-magnitude formula above already decides Break vs. Crush directly, so
there's nothing left for a "mild vs. severe Break" grade to represent.

**Crush**: **a much longer healing duration is the entire distinction from Break — no worse
functional lockout.** Unsplinted, Crush has the same 50% rate and total offhand lock as Break;
splinted, the same 60% rate and unrestricted offhand with the same shield-block splint risk.
**Settled: splinting is the only lever that eases this lockout** — nothing else (time alone,
Analgesic, any other remedy) touches it, consistent with Analgesic only ever managing pain and
splinting being the sole functional-restoration mechanic in this part.

**Bow use, Break/Crush only.** Unsplinted, a Break or Crush blocks drawing a bow entirely — a bow
takes 2 working arms to draw. Splinted, bow use is allowed but penalized: **draw time increased
and arrow damage reduced by the same percentage, default 40%** (a server setting). A separate
server setting lets players draw a bow even with an unsplinted Break or Crush (an
easier-difficulty option) — the same damage/draw-time penalty still applies either way, splinted
or not, once bow use is allowed at all. **Doesn't apply to spears** — thrown one-handed,
unaffected by an arm injury.

### Legs (4-tier track)

**Strain**: Pain — a **-5% walk speed penalty** (Analgesic halves the pain penalty; the same
pain-linked walkSpeed component Part 3's Pain relief section already assumed existed). Otherwise
fully usable. **Analgesic manages the pain symptom only — it does not heal the injury.**
Splinting is what actually lets a Strain heal; left unsplinted, using the leg risks the
use-triggered escalation roll toward Fracture (above).

**Fracture**: The same pain symptom, dialed up — a **-10% walk speed penalty** (Analgesic halves
the pain penalty), not a new penalty type. Still fully usable, just more painful than Strain —
"a fractured limb is still a working limb." Same deal as Strain: Analgesic only manages the pain,
splinting is what heals it and what blocks the use-triggered escalation roll toward Break.

**Break**: unsplinted, the player's own movement is prevented entirely — walking, sprinting,
jumping and swimming. A locked player in deep water sinks and drowns; in water shallower than head
height they cannot swim out. Sneaking is not blocked. Mounted players, and creative and spectator
players, are never locked. Splinting restores a **floor of function, not full use**: **-40% walk
speed** even splinted (the slowest the game's own animations still read as intentional rather
than broken). Splinting a Break is still required for it to
actually heal. **No separate internal Break graduation** — the hit-magnitude formula above already
decides Break vs. Crush directly, so there's nothing left for a "mild vs. severe Break" grade to
represent.

**Crush**: **a much longer healing duration is the entire distinction from Break — no worse
functional lockout.** Unsplinted, Crush guarantees the same total immobilization as Break's
worst case; splinted, a Crush caps at the exact same -40% walk speed floor as a splinted
Break. **Settled: splinting is the only lever that eases this lockout** — nothing else (time
alone, Analgesic, any other remedy) touches it, consistent with Analgesic only ever managing
pain and splinting being the sole functional-restoration mechanic in this part.

### Skull (2-tier track, then death)

**Strain**: Same headache/confusion + pain shape, with no stat effect of its own, treatable with
Mind Tonic (headache/confusion) and Analgesic (pain). A Skull has no splint and heals on its own
(Healing duration, below). **Also where the Bell headache trigger lives** — a 2nd, unrelated,
self-resolving cause of this same symptom, independent of any actual skull injury: prolonged
proximity to a ringing Bell enemy (`entities/lore/bell.json`, class `EntityBell`, tagged
`mechanical` — same tag as Locust, **not** `rust-creature`, distinct from the
drifter/bowtorn/shiver family used for Hallucination's apparition spawns). The Bell's ringing is
real: a ringing Bell is an `EntityBell` whose alarm task is active. Being within 15 blocks (the
bell task's own seeking range; the 48-block figure is only how far the alarm is audible) of a
ringing Bell for 5 continuous in-game minutes (about 10 real seconds at the default calendar speed
of 24 hours = 48 real minutes) starts the headache. Interrupted exposure resets the count. The
headache lasts until 1 in-game hour (about 2 real minutes at the default speed) after the player
was last exposed, with **no treatment needed**. It is not a Skull injury tier and changes no tier
or health. Creative and spectator players are not affected.

- **Settled: this is its own standalone effect, not Hallucination or Temporal Fog.** The Bell is
  mechanical, not rust-creature or stability-linked, so its disorientation is purely sonic/
  mechanical — a 3rd, distinct symptom-presentation, also why it's lighter-weight. This is
  separate from the *bone-injury-caused* Skull-Strain headache, which shares Temporal Fog's base
  vignette shader at strength 0.25 but gets its own subtle horizontal-shear sub-effect instead of
  Temporal Fog's vein tendrils (Part 3 Confusion/Brain Fog, above) — settled, not the same
  mechanic as the Bell's sonic disorientation either.
- **Conditional extension to bellhead shivers, gated on a specific mod being installed**: the
  "Temporal Symphony" mod (https://mods.vintagestory.at/temporalsymphony, mod ID
  `temporalsymphony`) adds "bellhead shivers with custom bell-ringing behavior and spawning
  mechanics" — vanilla bellhead shivers don't actually ring, so this only extends to them when
  that mod is installed. Detected via `IModLoader.IsModEnabled("temporalsymphony")` — a soft
  dependency, not a hard one; this mod works fully without Temporal Symphony, just without the
  bellhead-shiver extension.

**Fracture** (severity 2, last non-fatal tier): Everything Strain has, **plus a flat -10 max
health** via `SetMaxHealthModifiers` — unique to Skull's Fracture. Any further escalation is
death outright, not a named injury tier — Skull never reaches Break or Crush.

## Injury chance: location-scoped armor

**Armor, damage location and damage amount all factor into escalation odds.** Vanilla armor
protects with one randomly chosen slot per hit; this mod replaces that random choice with the slot
of the hit's location (Hit location, above): Skull uses the head piece, Chest/Arms the body piece,
Legs the legs piece. That applies to all damage vanilla armor already handles, so head armor
protects only against head hits, and so on, and the clothing-wear fallback follows the same
location. Falls remain unmitigated by armor. Armor reduces the post-armor damage that feeds both
Bleeding and the escalation table above; it is not a separate success roll.

## Splinting — the physical remedy

- **Application**: hold at least 4 sticks in the offhand and either a clean linen bandage (not the
  alcohol or antiseptic bandage variants) or at least 3 ropes in the main hand, then use. It
  consumes 4 sticks and the bandage or 3 ropes, and splints the player's worst unsplinted injured
  Chest/Arms or Legs limb (higher tier first; ties go to Legs, then Chest/Arms). A Skull cannot be
  splinted. A limb already splinted is not a candidate, and the item use is not intercepted when
  nothing can be splinted. Grid-craft-free, a direct use-interaction like bandaging a wound.
  **1 application per injury — a splint can't be applied twice.** The splint breaks when the limb
  escalates or when a shield block breaks it (Chest/Arms, above).
- **Not a symptom mask — it's what actually lets the limb heal, and it ends most use-triggered
  escalation risk (Tier progression, above).** An unsplinted Chest/Arms or Legs injury doesn't
  heal on its own; using it risks pushing it up a tier. Splinting removes that risk (for legs,
  jumps and hard landings still roll) and is the precondition for healing to happen — but a fresh
  direct hit to the limb can still escalate it regardless of splinting.
- **Restores a floor of function, not full use, and only at Break/Crush** — Strain and Fracture
  are already fully usable unsplinted (just painful, above), so splinting there is purely about
  enabling healing and blocking use-triggered escalation, nothing to functionally restore.
  Splinted Break or Crush works at **-40% walk speed for Legs**, or **60% digging speed and melee
  damage for Chest/Arms with the offhand unrestricted** — a real improvement over the unsplinted
  lockout, but still obviously a broken limb, not a cured one.
- **Does not touch pain at all.** The pain symptom (and its walkSpeed component, above) stays
  exactly as it is regardless of splinting. Analgesic is the only thing that dulls pain —
  splinting and Analgesic are complementary, not substitutes for each other.

## Healing duration — calendar-driven, not a fixed day count

Grounded in a real reference point: a broken bone typically takes about a month and a half to
heal. Rather than hardcoding that as a fixed number of days, it's expressed in the game's own
calendar unit and read live off `IGameCalendar.DaysPerMonth` (already a real, queryable,
server-configurable property; default 12) — a server running shorter months heals bones faster in
wall-clock terms, a server running longer months heals them slower, automatically, with no
separate config of our own needed.

A limb heals while it is injured and either splinted or the Skull.

- **Fracture and Break: 1.5 months** (`DaysPerMonth * 1.5`) — the baseline "a broken
  bone" duration, same for both since they're both genuinely broken, just at different
  functional severity (above).
- **Crush: 2.25 months** (`DaysPerMonth * 2.25`, the baseline + 50%) — the actual number behind
  "Crush's real distinguishing trait is a much longer healing duration" (above).
- **Strain: 0.75 months** (`DaysPerMonth * 0.75`, half the baseline) — Strain isn't really a
  break, just the on-ramp to one, so it heals faster than a genuine break does.
- **Skull**: heals at its tier's duration above without needing a splint.
- **Unsplinted Chest/Arms or Legs**: never heals, regardless of tier (Splinting, above) — these
  durations only apply once splinted.

**Recovery mechanics.** A healing limb runs 1 flat countdown at the tier's own duration above — it
stays at full injury severity (and the splinted rates, if Break/Crush) the whole time, then clears
entirely the moment the timer ends, rather than stepping back down through the named tiers on the
way. The countdown is stored as calendar end days, so it follows calendar speed and sleep and
survives relogs. **A fresh qualifying hit to a healing limb still rolls against the same magnitude
table (above)**: a successful roll escalates the tier as normal, breaking the splint of a
Chest/Arms or Legs limb so that the countdown starts over at the new, worse tier's own full
duration once re-splinted; a roll that fails to escalate (or a limb already at Crush) still
**docks a flat 10% of that tier's total healing duration** — reinjuring a healing bone sets it
back even when it doesn't get objectively worse. Death clears every injury.

---

# Part 5: Renewable Cultivation

Resolves the scarcity/renewability gap in Part 1 — wild-only ingredients like heather can be
over-harvested to local depletion, with no vanilla mechanic to regenerate them besides new chunk
generation. Grounded in this game's own existing mechanics rather than built from scratch.

## What's actually in vanilla today

- **Vanilla's flowerpot/planter block entity (`BlockEntityPlantContainer`) is purely
  decorative.** `OnTick` is a literal empty method body — no growth logic exists at all. It also
  explicitly freezes spoilage/transition on its contents (`slotTransitionSpeed` returns `0f`) —
  there's no vanilla growth mechanic on the pot itself to build on.
- **A real, already-tuned, already-shipped regrowth mechanic exists elsewhere**:
  `BlockEntityBerryBush` runs a periodic tick (`RegisterGameTickListener(CheckGrow, 8000)`)
  cycling through named growth stages (`empty` → `flowering` → `ripe`), harvestable once ripe,
  then resets. The pattern to crib the actual tick/state-machine shape from — proven and balanced
  in this exact game.
- **Room-based environment classification already exists and is reused across multiple vanilla
  systems**: `RoomRegistry.GetRoomForPosition(BlockPos)` returns a `Room` with
  `SkylightCount`/`NonSkylightCount`/`ExitCount`. `BlockEntityBerryBush` uses this for a
  greenhouse temperature bonus (`greenhousetempbonus`); `ItemCheese` uses the same registry for
  cellar-aging detection. No "humidity"/"dampness" stat exists anywhere in the engine — every
  existing "damp/cool" mechanic in this game is approximated via enclosure + light level.

## Decisions

### Extends vanilla's existing pots, not a new dedicated block

Growth logic is added to the flowerpot/planter blocks players already have, rather than a
separate "Cultivation Pot" block. Mechanically: swap in a mod-provided `BlockEntity` subclass of
`BlockEntityPlantContainer` (overriding the empty `OnTick` with real growth logic) via a JSON
patch changing the existing pot blocks' `class` attribute — real new C#, and a point of potential
conflict if another mod also patches the same block's class.

### Growth model: timed stages, berry-bush style

Deterministic stages (Planted → Growing → Ready), each a fixed real/game-time duration,
harvestable once Ready, then resets — same shape as `BlockEntityBerryBush`'s empty/flowering/ripe
cycle, chosen because it reuses a pattern already proven and balanced in this game rather than
inventing new probability math with no in-game precedent.

### Growable scope: all flora without an existing growth mechanism, mushrooms included

Any flora currently a one-time static worldgen spawn (everything in Part 1 — flowers, herbs,
ferns, lichens, cacti, reeds, mushrooms) qualifies, specifically because it has no existing
regrowth mechanism — crops and berry bushes already regrow and aren't in scope. Mushrooms are
explicitly included, not treated as out of scope, despite growing differently in reality.

### Environment gating: three growing conditions, mushrooms restricted to one

No new detection code, just a new consumer of `RoomRegistry`:

- **Outdoor** (unenclosed / no room) — general flora only.
- **Indoor / cellar-like** (enclosed, `NonSkylightCount` dominant — dark) — general flora, and
  the *only* environment mushrooms can grow in, the best available proxy for "damp and out of
  direct light."
- **Greenhouse** (enclosed, `SkylightCount` dominant — bright) — general flora only, not
  mushrooms (too bright/dry by the same proxy logic).

## Settled decisions

- **Harvest yield scales by pot size.** Flowerpot (`smallContainer`) doubles the plant — harvest
  gives 2 once fully grown. Planter (`largeContainer`) gives 4 — a genuine upgrade, not just a
  bigger version of the same yield.
- **Mod-compatibility risk is handled at the listing level, not in code.** If another mod also
  patches `BlockEntityPlantContainer`'s block class, the resolution is to mark that mod as
  incompatible or risky on this mod's ModDB page — a documentation/support answer, not an in-game
  detection-and-fallback mechanism.
- **Greenhouse gives a real growth-speed bonus**, the same way it already does for berry bushes
  (the `greenhousetempbonus` mechanism) — not just a pass/fail gate, greenhouse specifically
  speeds things up further.
- **Growth stage durations are twice as fast as vanilla berry bushes.** Berry bush stage duration
  comes from `GetHoursForNextStage()` — a per-species `nextStageMonths` range converted through
  the calendar, divided by a `growthRateMul` modifier, not a hardcoded constant. "Double speed"
  means this mod's cultivable flora use the same formula shape with either half the
  `nextStageMonths` value a comparable berry bush would use, or an equivalent ~2x
  `growthRateMul` — falls out of the existing formula, just different per-species tuning values.

---

# Cross-cutting decisions

Connections that span multiple parts, not obvious from reading any single part in isolation:

- **Pain relief (Analgesic)** covers: Bleeding, Wound Infection, Flu's fever, and broken-bone
  pain (Strain/Fracture, all locations) — **not** Skin Irritation (no walkSpeed component) and
  **not** Hallucination or Temporal Fog, both of which moved to Mind Tonic since neither is pain.
  Painkillers genuinely pause/slow the underlying progression while active for the escalating
  conditions, not just mask the symptom.
- **The toxicity/overdose counter** (Part 2 §5) is reused by: potion overdose itself (now
  dispatching to a distinct effect per potion type, §6 — Mind Tonic overdose → Hallucination/
  Temporal Fog, Tonic overdose → Liver Disease, etc.), and the Weakness→Paralysis→Cardiac Arrest
  escalation ladder. One counter, many consumers.
- **Escalation-via-continued-cause-while-untreated** is the catalog's recurring shape: Chest Cold
  (→ Flu or Bronchitis → Pneumonia, branching/reconverging), Liver Disease (→ Liver Failure), and
  broken bones' tier ladders. Not a named, system-wide architecture — each is its own concrete
  instance.
- **`healingeffectivness` and `walkspeed`** (`EntityStats` — note the category string is all
  lowercase, `"walkspeed"`, confirmed against `EntityPlayer.cs`'s own `Stats.Register` calls; the
  camelCase `walkSpeed` used elsewhere in this doc is descriptive prose, not the literal string to
  pass to `Stats.Set`/`GetBlended`) are the two workhorse debuff stats,
  reused across Wound Infection, Skin Irritation, Liver Failure's early warning, Flu's fever, and
  Weakness/Paralysis.
- **Room-based classification** (`RoomRegistry.GetRoomForPosition`) does three unrelated jobs:
  Immune/Tonic contagion, mushroom cultivation's indoor/damp gate, and cultivation's greenhouse
  speed bonus.
- **Wound Infection and broken bones share a debuff shape, not a tier system** — worsening
  untreated infection can apply a Break-equivalent penalty without the limb being tracked as
  broken.
- **Every condition/poison effect surfaces through the "R&R Ongoing Effects" character-screen
  tab**, keyed to effect type, not ingredient/recipe.
- **Antidote reliability vs. cost are separate levers**: a genuine cure-all reaching the silent
  pre-onset phase of a poison, but its real cost (vomit-first-dose, 2-hour restricted diet with
  relapse/potion-voiding risk) is what stops it from being spammable.

---

# Consolidated open items

**Part 1 (Flora) and Part 2 (Brewing Pipeline)**: every remedy cluster has an assigned product
name, primary use, and numbered regular-vs-Concentrated potency (Antiseptic, Antinausea,
Sedative, Antiviral, Analgesic, Topical Ointment, Tonic, Mind Tonic). Cookpot recipes and Potion
Base/Diluted Potion/Potion/Concentrated Potion item JSON are implemented for all 14 clusters
(remedy and poison alike, including the Universal Antidote), plus the Sludge catch-all recipe
covering every ingredient with no cluster membership. The Vial's full casting chain —
clay-forming a mold, firing it, melting quartz-derived glass in a Crucible, and pouring/cooling it
into 4 Vials per mold-fill — is implemented and confirmed working in-game, so the physical item is
obtainable. Its liquid-container mechanics are now wired up, following the real `bowl.json`
pattern (`class: BlockLiquidContainerTopOpened`, `liquidContainerProps` with
`capacityLitres: 0.25`/`drinkPortionSize: 0.25` per §4, a separate `vial-liquidcontents` shape
holding the visible liquid cuboid that the engine tints/levels per content, matching bowl/bucket's
convention rather than jug's opt-out). This moved the Vial from an Item registration to a Block
registration (same "kept as a block for backwards compatibility" pattern bowl/jug/crucible all
use), since `BlockLiquidContainerTopOpened` only exists on `Block` — every reference to
`remedyandruin:vial` elsewhere had to move from `"type": "item"` to `"type": "block"` accordingly
(the Vial Mold's `drop` attribute, the lang key). Not yet tested in-game. The per-potion overdose
effect table (§6) is an explicitly adjustable draft, not balanced or finalized. Effect delivery
itself (Part 2 §9's hook) is not yet implemented — potions and poisons exist as real, craftable
items but don't yet apply their effects on consumption.

**Part 3 (Conditions & Poison Effects)**: all 5 poison effects and all non-poison conditions are
fully numbered, including the raw/cooked ingredient Effect×/Onset× scaling tables, Stacking, and
Tolerance (including its decay rule, now settled — see Tolerance above). Still open: the
"discrete named stages" architecture generalized beyond its two current concrete examples (Chest
Cold family, Weakness/Paralysis/Cardiac Arrest); the "R&R Ongoing Effects" character-screen tab
isn't implemented yet (the system is designed).

**Part 4 (Broken Bones)**: fully numbered — no open items remain. Temporal Fog and Skull-Strain's
concussion screen effects are implemented and confirmed working in-game
(`TemporalVignetteRenderer.cs`, `temporalvignette.vsh`/`.fsh`): Temporal Fog is a muted
sepia/desaturation color grade, Concussion applies a horizontal shear plus a contrast/brightness
boost and a drunken camera sway (toggleable for accessibility via config). Both are driven by two
debug chat commands (`.rrTempFog`, `.rrConcussion`) — not yet wired to any real gameplay trigger.

**Part 5 (Renewable Cultivation)**: fully settled — no open items remain.

**Balance numbers, everywhere, structurally**: toxicity thresholds, distillation ratios, steep
durations, bleed/infection severity curves, escalation timing, per-damage-type armor resistance
values, per-species cultivation growth durations. None of this is designable further without
playtesting a real build.

**Effect duration persistence across logout/login, decided and numbered.** An absolute-date
model — store an in-game start date, compute remaining time as `currentDate - startDate` whenever
checked, the same pattern already used for Tolerance decay/broken-bone healing/cultivation growth
above — has a real exploit if applied to poison/illness effect durations specifically: since the
actual DoT/debuff tick only ever runs while a player's entity is loaded, a player could dodge an
affliction's real symptoms entirely just by staying logged out until the calendar-computed elapsed
time exceeds the duration, without ever experiencing it.

Each active effect instance instead stores **two independent fields**, both always kept correct
regardless of which one currently governs gameplay:
- **`TimeStarted`** — an absolute in-game date, written once at effect creation and never touched
  again. Free to maintain; everything else about it is calendar arithmetic done on read, same as
  Tolerance/healing/cultivation's own pattern.
- **`TimeLeft`** — remaining duration, decremented only while the player is actually online
  (a live, in-memory countdown during normal play — **not** written to WatchedAttributes every
  tick, which would flood the attribute-sync bookkeeping). Persisted with a **single write on
  `PlayerDisconnect`**, capturing whatever the live tracker's current value is at that instant.
  Untouched the rest of the session.

A **config setting** (default: `TimeLeft`) decides which field is authoritative for resuming an
effect on login (`PlayerNowPlaying`, already hooked for `EntityBehaviorRemedyEffects`): the default
pauses effects while offline, closing the logout exploit above; an opt-out flips to `TimeStarted`,
letting effects run their course in real calendar time even while the player is away, for server
admins who'd rather match the same "time simply passes" behavior every other calendar-based system
in this document already has.

Because both fields are always kept live rather than one being derived from the other at the
moment of a config flip, switching the setting mid-playthrough is an instant, safe swap, not a
conversion. For a player who stays online continuously the two tracks never diverge in the first
place (no offline pause ever separated them), so a flip is invisible to them. For a player who was
offline while the setting changed, the two tracks did diverge during that gap, so the flip
surfaces as a real, one-time jump (more or less time remaining) the next time they log in.

**Known, accepted gap**: an ungraceful disconnect (crash, force-quit, server kill) skips the
`PlayerDisconnect` write, so a stale `TimeLeft` can lag behind by up to the periodic world-save
interval. Bounded and not an exploit vector (worst case: a few extra minutes of frozen time no
different in kind from a normal logout), not worth solving further.

**Sleep-driven calendar jumps affect the same `TimeStarted`/`TimeLeft` bookkeeping above.** There
is no engine event fired when sleep ends or by how many hours it advanced the calendar - confirmed
against `ModSleeping.cs` (a continuous time-speed-modifier ramp, not a discrete jump) and
`EntityBehaviorTiredness.cs` (which detects elapsed sleep purely by polling `Calendar.TotalHours`
before and after its own tick - the same approach this mod must use).

The real-time-to-game-time ratio (default 48 real minutes per game day) is server-configurable at
runtime via `CalendarSpeedMul` and `SpeedOfTime` - any code converting between real time and game
time must read `Calendar.SpeedOfTime * Calendar.CalendarSpeedMul` live rather than hardcoding the
default ratio, matching how vanilla's own `EntityBehaviorHealth.ApplyRegenAndHunger` does this
conversion.

**Handbook page explaining the mod's mechanics** — deliberately held until mechanics are settled.
Recipe integration itself is automatic (standard vanilla behavior) and doesn't wait on this.
