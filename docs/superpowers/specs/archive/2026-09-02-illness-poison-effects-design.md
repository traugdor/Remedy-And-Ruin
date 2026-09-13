# Illness/Poison Effect Catalog — Design Spec

> **Archived — superseded.** Folded into `02-design-overview.md` (Part 3). This file is kept for
> history only; edit the master document instead.

Status: **approved design, not yet implemented**. This fills in the `ApplyEffect` placeholder
left open by
`docs/superpowers/specs/2026-09-02-remedy-poison-brewing-pipeline-design.md` §9 — it defines
*what* each condition/poison effect actually is and does, grounded in specific engine hooks. It
does not assign specific flora ingredients to specific conditions (see `flora/conditions.md` for
the ingredient-cluster side of that pairing) and does not design multi-ingredient recipe combos.

## Design principle: raw-food penalties — superseded, now connected to the Antidote

**This reverses the original decision below.** It's kept, struck through in spirit, so the
reasoning that led to the reversal is visible rather than silently erased. Originally: a
mushroom's vanilla raw-eaten `health` penalty (e.g. death cap `-50`) was treated as a base-game
food mechanic, independent of what a *brewed* remedy or poison made from that same ingredient
does — nothing tied the two together, and eating a deadly mushroom raw was instant, un-antidoted,
un-reactable damage.

**Now**: every mushroom's `health` penalty — whether eaten raw directly, or eaten as part of a
cooked meal containing that mushroom as an ingredient (vanilla already aggregates a mushroom's
`healthByType` into whatever meal it's cooked into via `nutritionPropsWhenInMeal`, confirmed in
`BlockMeal.GetContentNutritionProperties`, `BlockMeal.cs:459-521` — no new aggregation logic
needed, this already happens) — no longer applies as instant damage. Instead it becomes a **timed
delay followed by a DoT that replicates the mushroom's total poison damage over time**, the same
`DamageSource.Duration`/`TicksPerDuration` delayed-DoT shape already established for Liver
Failure, just applied to a new trigger (raw/cooked mushroom consumption) rather than a new
mechanic. This is what makes the Antidote actually relevant to raw mushroom poisoning for the
first time — previously, instant damage gave nothing to counter.

Rationale, largely in your own words:

- **Gives the player a real reaction window** to recognize something's wrong and drink the
  Antidote before it's too late — how much time, plausibly, depends on the specific mushroom/dose
  rather than being one flat number for everything.
- **Opens the door to a "slip it into their meal" social mechanic** — since the effect isn't
  instantly obvious, a target might genuinely not notice what happened until the delayed effect
  starts settling in, rather than dropping the instant a poisoned bite is swallowed.
- **Removes the pressure to consult the handbook before experimenting** — a curious player can
  just try an unfamiliar mushroom without instantly regretting it, since punishment isn't
  immediate; the delay is itself the safety margin that makes ingredient experimentation
  reasonable instead of reckless.

### Cheese-tactic risks this opens up, and how to guard against them

You asked directly, so here's a genuine answer rather than a hand-wave:

1. **"Eat anything, then reflexively Antidote" — reframed, not actually free.** The first dose's
   vomiting genuinely expels stomach contents before digestion, so this isn't a hole to plug so
   much as an intended safety valve: eating something risky and immediately curing it with the
   Antidote means surviving, but getting **none** of that food's nutritional benefit either — it
   comes back up before it can be digested. Trading "did I just eat poison" anxiety for "I wasted
   a meal and both Antidote doses" is a real cost, not a free win.
   - **The actual lever is delay length, not randomization primarily.** The delay before a
     mushroom's DoT starts needs to be **short** — short enough that even a player who doesn't
     immediately vomit-cure it gets little to no benefit from the food's satiety before the delay
     expires and the poison starts hitting. A long delay (anywhere near the Antidote's full
     2-hour window) would let a player eat something dangerous, get the full satiety benefit
     during the safe window, *then* cure it right before it manifests — genuinely free value with
     no real cost. A short window closes that off: there isn't enough time between eating and the
     poison starting for the food's benefit to matter much either way.
   - Randomizing the delay within that short window still helps (a player can't optimize to the
     exact millisecond), but it's a secondary refinement on top of "keep it short," not the
     primary defense.
2. **Cooking a lethal mushroom into a big multi-serving batch to dilute the dose — checked and
   confirmed not a real risk.** `BlockMeal.GetContentNutritionProperties` tracks each ingredient's
   health contribution scaled by how much of it went in, not averaged down toward zero by batch
   size, and a real vanilla recipe (`recipes/cooking/meatystew.json`) confirms mushrooms slot into
   ingredient categories capped at `maxQuantity: 1` — one stack per slot, a small fixed number of
   slots total. There's no "huge pot" to dilute into: vanilla meal recipes bound batch size by
   slot count, not a scalable pool, so a poisoned stew carries essentially the same
   per-serving penalty as eating the mushroom alone. No further verification needed here.
3. **Settled: no diagnostic signal, deliberately.** No status icon or other tell distinguishes a
   pending delayed poison from having eaten nothing risky at all — poisoned food is just poisoned
   food. You could be fine, you could not be fine. The genuine uncertainty is the point, not a
   gap to close with a warning UI; giving the player a "something's wrong" indicator during the
   silent phase would undercut the entire "slipped into a meal" mechanic this was designed to
   enable in the first place.
4. **Settled: the Antidote is a genuine cure-all, reaching the silent delay phase too** — it
   clears any poison currently pending, not just ones that have already started their DoT. A
   player who takes it can be confident it worked, full stop, rather than wondering if some other
   still-pending poison is about to blindside them anyway. The tradeoff isn't in reliability, it's
   in cost: the existing vomit-first-dose, 2-hour restricted-diet, potion-voiding-risk package
   already makes this something you take because you're actually poisoned, not something to chug
   preemptively "just in case" — reliability and cost do the balancing separately, not
   reliability alone.

None of these are reasons not to do this — they're the concrete places balance work needs to
happen so the delay window functions as intended tension rather than either a non-threat (cheese)
or an unfair surprise.

## Design principle: every effect registers with PlayerStatusHUD

Every condition/effect in this catalog — every remedy-cured condition, every poison effect, and
every injury/illness state, not just a subset — must register a status icon with PlayerStatusHUD
(https://mods.vintagestory.at/playerstatushud), the library mod noted earlier as this mod's HUD
dependency. Concretely: implement `IStatusStripProvider` and append one `StatusDescriptor` per
active effect in `Collect()`, using stable IDs across frames per that API's contract. Affect kind
(Neutral/Positive/Negative) maps directly onto this catalog's own shape — Bleeding, Wound
Infection, Upset Stomach, the Chest Cold/Flu/Bronchitis/Pneumonia family, Skin Irritation,
Hallucination, Temporal Fog, Liver Disease, and all poison-only effects are Negative; the
Sedative sleep-bypass and the Immune/Tonic preventive buff are
Positive. This is a blanket requirement on the catalog as a whole, not something to revisit
per-effect later — any new condition/effect added to this catalog inherits it.

### Icons are keyed to effect type, not to ingredient or recipe

Decided now rather than deferred, because two different things are expected to grow, and they
grow independently: the **ingredient/recipe catalog** (more flora added to the base game, more
flora mods integrated) grows, and — separately, not assumed away — **the set of effect types
itself may also grow** as those integrations happen. A flora mod being integrated could bring a
gameplay concept that doesn't map onto any of this mod's current ~16 effect types (Bleeding,
Wound Infection, Upset Stomach, Sedative, the Chest Cold/Flu/Bronchitis/Pneumonia family, Skin
Irritation, Immune/Tonic, Hallucination, Temporal Fog, Liver Disease, and the poison-only
effects: Liver Failure, Food Poisoning, Cardiac Strain, Weakness/Paralysis). The icon system is
designed open-ended for this, not around an assumption that today's type list is final:

- Every new **recipe** still produces one of the *existing* effect types by default and
  automatically reuses that type's icon — this part is unaffected by whether the type list
  itself is open or closed. One generic base icon shape per effect *type* is authored, not one
  per ingredient or recipe.
- Adding a genuinely **new effect type** is treated as a normal, systematic content operation,
  not a one-off exception requiring new tooling: author one new base icon shape following the
  same template as the existing set, generate its tinted variant(s) the same asset-build way, and
  register it the same way as any other type. Nothing about the icon pipeline itself needs to
  change to accommodate a new type — only new assets need to be produced.

Checked `StatusDescriptor`'s actual fields (`PlayerStatusStrip/docs/PLAYER_STATUS_STRIP_API.md`,
via the PlayerStatusHUD source repo) before deciding how tinting works: `Icon` only accepts a
finished `AssetLocation`, no runtime color parameter — `AffectKind` (Neutral/Positive/Negative)
only drives animation profile (`PositiveAnim`/`NegativeAnim`: shake/slide/scale), not color. So
there's nothing to gain from *live* runtime pixel tinting against this API regardless of how many
types exist. Instead: each base icon shape gets its colored variant(s) generated once as an
asset-build step, not live at runtime — and since most types are one-directional anyway (Bleeding
is always Negative, Sedative always Positive), most shapes only ever need a single pre-tinted
variant, not a runtime-switchable one.

## Conditions (cured by remedy clusters)

### Bleeding

Immediate blood-loss condition from combat/injury.

- Uses `EnumDamageOverTimeEffectType.Bleeding` — already defined in the engine
  (`EnumDamageOverTimeEffectType.cs`) as a DoT category distinct from Poison, but nothing in
  vanilla currently triggers it. This mod would be the first thing to actually use it.
- **Severity scales with hit size**, using a tiered system (e.g. Minor/Moderate/Severe) rather
  than a raw continuous formula — easier to balance and to show in the HUD tooltip than a bare
  number.
- **Which attacks can cause a bleed is source-specific, not universal**, and is real new mod
  content (retagging + a new armor mechanic), not free reuse — checked against actual game data
  rather than assumed:
  - **Drifter** (corrupt/nightmare/double-headed tiers only — confirmed `SlashingAttack` in
    `drifter.json:254-256`, nightmare has a dedicated "knife" shape) causes Bleeding. Lower
    tiers (normal/deep/tainted) do not.
  - **Shiver** (all tiers, confirmed `SlashingAttack` uniformly, `shiver.json:290`) causes
    Bleeding; higher tiers cause heavier bleeds and a higher Wound Infection chance on top —
    this second part is a Shiver-specific rule layered on, not derived from the damage type
    itself (the game doesn't actually tag Shiver's attack as a distinct "bite" type).
  - **Bowtorn**: melee attack causes no Bleeding. Its ranged `arrow-bone` projectile
    (`bowtorn.json:271-287`, confirmed ranged, confirmed bone arrows) causes no Bleeding either,
    but raises Wound Infection chance.
  - **Bear, wolf, and pig** (retaliation attack, `pig-adult.json:260-289` — pigs are passive but
    do fight back when struck, `whenInEmotionState: "aggressiveondamage"`) cause Bleeding plus
    raised Wound Infection chance. None of these three are tagged `PiercingAttack` in the base
    game as of this check — bear and wolf are `SlashingAttack`, pig has no explicit
    `damageType` override at all. **Retagging these three to `PiercingAttack` is real new mod
    content**, applied via a JSON patch (`assets/survival/patches/*.json` is a real,
    already-used-by-vanilla mechanism — RFC 6902-style JSON Patch applied at load time, no
    vanilla file is directly edited), not a free reuse of existing data.
  - Arrows in general very likely already default to `PiercingAttack` in C# (no explicit
    override found on the base `arrow` entity, and the type exists specifically for this) — to
    be confirmed at implementation time rather than assumed here.
- **Armor mitigation requires a new mechanic, not reuse of the existing system.** Checked
  vanilla's actual armor protection model (`protectionModifiersByType` in
  `itemtypes/wearable/seraph/armor.json`): it's `relativeProtection` + `flatDamageReduction` +
  `protectionTier` (compared against the attacker's `damageTier`), applied **uniformly across
  all damage types** — there is currently no per-damage-type differentiation at all, so "chain
  resists slashing but not piercing" isn't something vanilla already does. Implementing it means
  a new mod-defined attribute (e.g. `perDamageTypeProtection: { SlashingAttack: x, PiercingAttack:
  y, BluntAttack: z }` per armor piece) plus a new hook — most likely the same `onDamaged`
  delegate already used for Bleeding's damage-shaving (§ above) — that reads this new attribute
  and applies an additional bleed-chance/severity modifier keyed to the incoming hit's damage
  type. Target shape, not yet balanced: Plate strong across the board; Chain strong vs.
  Slashing, weak vs. Piercing; Scale/Lamellar/Brigandine moderate against both; Gambeson and
  other soft armor, minor against both.
- **Does not stack.** Only the strongest active Bleeding DoT applies at a time. `ActiveDoTEffects`
  (`EntityBehaviorHealth.cs:128`) is a plain list with no built-in stacking rule, so this is
  enforced by mod logic: before calling `ApplyDoTEffect(..., EnumDamageOverTimeEffectType.Bleeding)`,
  compare the new bleed's severity against any existing Bleeding entry in `ActiveDoTEffects` and
  keep only the stronger one (replace or skip, don't add a second).
- **Self-resolving is not the same as safe.** Left alone, a bleed's `TicksLeft` naturally runs
  out via the existing DoT tick loop (`ProcessDoTEffects`, `EntityBehaviorHealth.cs:570`) — but
  nothing in that loop floors damage above zero HP (`OnEntityReceiveDamage`,
  `EntityBehaviorHealth.cs:243`, kills the entity the same way any other damage does). A mild
  bleed tapers off harmlessly; a severe bleed from a big hit, left completely untreated for its
  full duration, **can kill**. This is deliberate — it's what makes treatment matter.
- **Instant hit damage is reduced to compensate**, so a Bleeding-causing hit isn't simply "normal
  damage plus extra DoT on top" (which would make combat unplayably punishing, especially once
  severe bleeds can be lethal on their own). Vanilla's `DamageSource.Duration`/`TicksPerDuration`
  only supports converting a hit's *entire* damage into a DoT (`DamageSource.cs:57-70`), not a
  partial split — so achieving "reduced instant hit + separate bleed DoT" means using two hooks
  together: `EntityBehaviorHealth`'s public `onDamaged` delegate (`EntityBehaviorHealth.cs:146`,
  invoked via `ApplyOnDamageDelegates` before damage is applied) to shave down the instant damage
  on a qualifying hit, and a separate `ApplyDoTEffect(..., Bleeding)` call to apply the
  shaved-off portion as the bleed.
- **Staunching bleeding is universal to any healing item, not gated to a new tier.** Vanilla
  already has a real two-tier bandage system (`itemtypes/bandage.json`): `bandage-clean` (heals
  3 HP) and `bandage-alcoholed` (heals 7 HP), both built on `CollectibleBehaviorHealingItem` — a
  channeled hold-to-apply heal-over-time behavior (distinct from the older instant `ItemPoultice`)
  that converts its heal into a `DamageSource` with `Duration`/`TicksPerDuration`
  (`CollectibleBehaviorHealingItem.cs:163-171`, same DoT machinery as Bleeding/Infection, just
  `EnumDamageType.Heal`), respects armor's `healingeffectivness` stat, and can revive a downed
  player via `EntityBehaviorPlayerRevivable`. This mod hooks the general "healing item
  application completed" point (any bandage, any poultice, vanilla or modded) to also call
  `StopDoTEffect(Bleeding)` — bandaging a wound staunches the bleeding regardless of which
  healing item was used, matching how bandaging works in reality.
- **Health potions are the emergency valve when bandaging isn't feasible, not a substitute for
  it.** A drinkable Heal-type dose can out-heal a severe bleed's damage rate fast enough to avoid
  dying to it, but does **not** call `StopDoTEffect(Bleeding)` — it treats the symptom (low HP),
  not the cause (the ongoing DoT), which still needs an actual bandage (or the bleed's own
  natural expiry) to fully end. This distinction matters mechanically:
  `CollectibleBehaviorHealingItem.CancelApplication` already cancels a bandage application if the
  entity is airborne (and optionally while swimming) — meaning there are real moments (mid-fall,
  mid-fight, swimming) where bandaging is genuinely not an option, which is exactly when a
  potion's instant, uninterruptible heal is the only available response.

### Wound Infection

A separate, later-onset condition from Bleeding, not the same thing.

- **Not guaranteed on an unbandaged wound — a chance, not a rule.** A flat 100% infection rate
  would contradict Bleeding's own severity scaling (mild hits are supposed to taper off
  harmlessly); instead, infection *chance* scales with the same severity value driving the bleed
  (bigger hit → higher odds), likely also weighted by how long the wound goes unbandaged. This
  keeps minor wounds low-stakes while making antiseptic bandaging genuinely urgent for serious
  ones. Exact chance curve/thresholds are a balance value, not decided here.
- Uses a custom DoT effect type distinct from Poison/Bleeding. `ApplyDoTEffect`'s general
  overload takes a plain `int effectType`, not just the 3-value enum
  (`EntityBehaviorHealth.cs:451`), so a 4th mod-defined "Infection" category needs no engine
  changes.
- **Clears on its own over time**, same self-resolving-but-not-automatically-safe shape as
  Bleeding.
- **Carries a secondary debuff beyond the DoT tick**, hitting both `healingeffectivness` and
  `walkSpeed` via `EntityStats` (both existing categories) for the duration of the infection —
  you heal worse *and* move slower while infected, not just losing HP over time. Reinforces
  urgency to treat it beyond the raw damage.
- **Worsening, untreated Infection can additionally apply a broken-bones-*shaped* debuff on the
  affected limb — work-speed/offhand-lock for Chest/Arms, movement-speed/immobilization for
  Legs — without actually pushing that limb into the broken-bones tier system.** This is an
  independent mechanic that reuses the same debuff shape Break already has
  (`docs/superpowers/specs/2026-09-02-broken-bones-injury-design.md`), not a trigger that
  literally escalates the limb's injury tier — the two systems produce similar symptoms on
  purpose (severe infection reads as "your arm barely works" the same way a real break does) but
  stay mechanically separate, same distinction as Liver Disease being its own condition from
  Liver Failure at onset.
- **Only prevented or cured by alcohol- or antiseptic-soaked bandages specifically** — plain
  bandages/poultices staunch bleeding (universal, above) but do **not** touch infection.
  `bandage-alcoholed` already exists in vanilla, crafted by dipping `bandage-clean` in a
  water-tight container holding `alcoholportion` (`recipes/grid/bandage.json:1-25`) —
  `alcoholportion` itself comes from distilling `spiritportion`
  (`itemtypes/liquid/spirit.json:14-17`), the same barrel→boiler→condenser chain the brewing
  pipeline spec already reuses. This mod adds a parallel bandage variant (e.g.
  `bandage-antiseptic`) crafted the identical dip way, but using a Wound Care Potion/Concentrated
  Potion (old man's beard, sage, thyme, wild daisy, woad, per `flora/conditions.md` cluster 1) in
  place of `alcoholportion` — same recipe shape, different liquid, and this is the variant whose
  application clears/prevents Infection specifically, on top of the universal bleeding-staunch
  and whatever HP the bandage already heals. **`bandage-antiseptic` carries the same drying
  penalty as `bandage-alcoholed`** — vanilla's alcohol-soaked bandage dries out and reverts to
  `bandage-clean` after about an hour (`transitionablePropsByType`'s `"Dry"` transition,
  `bandage.json:58-67`, `transitionHours: {avg: 1}`), losing the alcohol's benefit until re-dipped;
  the antiseptic variant should match that same timing rather than being treated as permanent.
  This is the bandage's *intended* use for the Wound Care Potion — drinking it (Upset Stomach's
  fourth trigger, above) is a deliberate easter egg for players who try it anyway, not the
  primary use case the potion was designed around.

### Upset Stomach / Vomiting

- **Four independent triggers**: overdosing on any potion (an overdose symptom, ties into the
  toxicity counter from the brewing pipeline spec §5), eating partially spoiled food (vanilla
  already tracks food freshness as a continuous value — `TransitionState.TransitionLevel`,
  `TransitionState.cs` — not just a binary fresh/rotten flag, so "partially spoiled" maps onto a
  mid-range `TransitionLevel` rather than needing a new freshness state), contracting a GI tract
  illness (a contagious disease — deferred, ties into the Immune/Tonic contagion mechanic already
  flagged as its own undesigned piece), and **drinking the Wound Care Potion/Concentrated
  Potion** (the same drinkable remedy from the Wound Care/Antimicrobial cluster — cattails, old
  man's beard, sage, thyme, woad, etc. — already established as both a cure for Wound Infection
  and the liquid used to make bandage-antiseptic). This fourth trigger is the first concrete
  example of a recipe's own side-effect profile being decided (the brewing pipeline spec §6
  committed to severity scaling with tier and distinct-ingredient count, but left *what* each
  recipe's side effect actually is unauthored until now) — the Wound Care cluster's side effect
  is specifically GI upset, present even at normal (non-overdose) dosage, not gated behind the
  toxicity counter the way the first trigger is. **This is a deliberate easter egg, not the
  potion's intended use** — the Wound Care Potion is meant for crafting `bandage-antiseptic`
  (§ above), and drinking it straight is a discoverable "why would you do that" moment for
  curious players, not a use case the design is balanced around.
- A vomiting episode calls `EntityBehaviorHunger.ConsumeSaturation(amount)` (already used
  internally by health regen, so it's a public, already-exercised method) — either the entity's
  full current saturation (**complete void**) or a fraction of it (**partial void**).
- **Every episode starts as a partial void.** Escalating to a complete void only happens via a
  relapse — the original trigger's severity doesn't decide the first episode's outcome, only
  whether/how likely a relapse is.
- **Two independent relapse paths**, not just a timer: (1) a bare chance the partial void relapses
  on its own given enough time, same shape as other self-resolving conditions in this catalog;
  and (2) **eating too much, or too rich, food too soon after an episode re-triggers vomiting** —
  a "stomach still settling" mechanic, checked against how much/what was eaten within a recovery
  window following the previous episode, not just a blind timer roll.
- Interrupts whatever the player is currently doing — needs a small new behavior to cancel the
  active action/hand-use on trigger; the saturation-draining half is free, the interrupt half is
  new but small.
- **Clears on its own over time**, same self-resolving shape as Wound Infection.

### Sedative / sleep bypass

Not really a "cure" — a direct utility effect.

- `BlockBed.cs:67` gates sleeping on `EntityBehaviorTiredness.Tiredness <= 8f`, throwing the
  `"not-tired-enough"` in-game error otherwise. `Tiredness` is a plain public settable float.
- A sedative dose sets `Tiredness` above that threshold directly — no workaround needed, the
  field is already open for exactly this use.
- Framed as "drink this to be able to sleep now, for up to 9 hours" rather than as curing an
  "Insomnia" illness.
- **The sedative sets a ceiling, it doesn't replace bed quality.** Its actual job is bypassing
  the `Tiredness` gate so sleep can be attempted at all — how many hours are actually granted
  still comes from the bed's own `sleepEfficiency` (`BlockBed.cs:152-163`, already vanilla,
  already scales hours by bed tier), just capped at 9 rather than whatever the bed alone would
  give. A better bed still matters even with the sedative in hand.

### Chest Cold (with escalation)

- **Two triggers**: environmental exposure (prolonged low body temperature/wetness — reuses
  vanilla's existing `EntityBehaviorBodyTemperature`, no new tracking needed) and contagion from
  another player (same deferred transmission mechanic flagged for Immune/Tonic and the GI tract
  illness).
- A debuff (stamina/breath penalty, coughing interrupts actions), cleared over time by
  respiratory remedies (catmint, edelweiss, thyme, wild daisy, hart's-tongue fern, cinnamon fern,
  bamboo, woad, per `flora/conditions.md` cluster 4).
- **Left untreated, it escalates through a branching/reconverging graph of named stages, not a
  flat chain** — this is the real answer to `01-engine-hooks.md`'s open "discrete stages"
  architecture question, not just an illustrative example:
  - **Chest Cold → Bronchitis** is the default untreated path.
  - **Chest Cold → Flu** happens instead if the player gets cold *and* wet while the Chest Cold
    is active (the same environmental trigger that can cause Chest Cold in the first place,
    checked again during an active infection) — reuses `EntityBehaviorBodyTemperature` again,
    same as the initial trigger.
  - **Bronchitis can also redirect into Flu mid-course** if that same cold-and-wet condition
    occurs while the player already has Bronchitis — the branches aren't fully separate tracks,
    they can cross over.
  - **Both Flu and Bronchitis converge on Pneumonia** as the shared, most severe terminal stage
    if left untreated further.
  - Fever is Flu's signature symptom: a `walkSpeed` penalty that compounds with (not replaces)
    the `healingeffectivness` penalty already present at the more severe stages — consistent
    with Wound Infection's own two-stat debuff shape decided earlier.

### Pain relief

Not a standalone condition — a symptom-severity modifier layered onto Bleeding, Wound Infection,
and Skin Irritation (reduces their debuff magnitude) rather than its own illness with its own
cure/cause. Also extends to **Hallucination and Temporal Fog** (offsets/lessens their intensity)
and can **eliminate Flu's fever symptom for a time** — same masking role, not a cure, applied to
a wider set of conditions.

**Genuinely buys time, not just a symptom mask.** While a painkiller is active on
Hallucination/Temporal Fog/Flu's fever, it also pauses or slows the underlying illness's actual
progression — a painkiller suppressing Flu's fever doesn't just hide how bad it feels while
Flu→Pneumonia keeps advancing underneath; taking it genuinely holds the escalation back for as
long as it's active. This is a real stopgap while a proper cure is being brewed, not a trap that
quietly punishes the player for feeling better.

### Skin Irritation

A minor topical debuff (frostbite, sunburn, contact with an irritant plant), cleared by topical
remedies (cornflower, horsetail, chamomile, edelweiss, per `flora/conditions.md` cluster 6).
Mechanically, a minor `healingeffectivness` penalty via `EntityStats` — the same stat category
already used by Wound Infection and the Bronchitis/Pneumonia stages, just at a much smaller
magnitude, consistent with this being the least severe condition in the catalog.

### Immune / Tonic (preventive, not curative)

- Not a cure for an active condition — a temporary buff that reduces the chance of *catching*
  Chest Cold or Upset Stomach from another player.
- **This explicitly implies a player-to-player contagion mechanic**, now settled rather than
  fully deferred: **room-based, not raw proximity.** Reuses `RoomRegistry.GetRoomForPosition`
  (`VSEssentials/Vintagestory.GameContent/RoomRegistry.cs:286`, already used for cellar/greenhouse
  detection — no new room-detection code needed) to check whether two players are in the same
  logical enclosed room, and if so, whether they're within roughly 2-3 blocks of each other (a
  "social distancing" range) — both conditions must hold for a transmission chance to roll at
  all. This is deliberate: it makes quarantining a sick player in a separate room (or keeping
  distance within a shared large room) a real, mechanically effective countermeasure, not just
  flavor. Applies to Chest Cold, the GI tract illness (Upset Stomach's third trigger), and
  whatever else ends up contagious. Exact transmission chance/tick-rate is still a balance value,
  not decided here; whether NPCs/animals can carry it (vs. only players) is also still open.

### Confusion / Brain Fog — split into two conditions with a shared remedy

What started as one condition turned out to be two different things that happen to share a cure
(the Cognitive/Nerve Support cluster — lion's mane, sage, per `flora/conditions.md` cluster 8):

#### Hallucination

- **Triggers**: overdose (toxicity counter crossing a threshold, per the brewing spec's §5),
  lack of sleep, *and* directly from drinking a Psychoactive-cluster poison. This unifies what
  was originally listed only as a standalone poison effect (below, in the poison table) with two
  additional non-poison triggers — it's the same effect either way, not two separate mechanics
  that happen to look alike.
- Uses `PsychedelicPerceptionEffect` (`VintagestoryAPI/Vintagestory.API.Client`, registered as
  `"psychedelic"` in `PerceptionEffects.cs:24`) — already confirmed reusable, see the poison
  effects section below.

#### Temporal Fog

- **The Temporal Stability HUD misreport idea is dropped entirely** — replaced with a real visual
  effect instead of a cosmetic-lie display trick.
- **Triggers**: *prolonged* exposure to low Temporal Stability (not merely being low at a given
  instant — duration matters, not just the instantaneous value), and using a Temporal Gear
  (`itemtypes/resource/gear.json`, confirmed a real vanilla item) to restore stability, which
  itself carries a Temporal Fog risk as a side effect of the cure.
- **Visual: screen wobble + a gently pulsing vignette**, and this is a near-exact reuse rather
  than new rendering work. `FreezingPerceptionEffect.cs` (registered as `"freezing"` in
  `PerceptionEffects.cs:22`) already implements precisely this shape: `ApplyFrostVignette` writes
  a `FrostVignetting` shader uniform, and `ApplyMotionEffects` perturbs `capi.Input.MouseYaw` via
  simplex noise for the camera-shake/wobble, both driven off a single watched attribute
  (`freezingEffectStrength`, read every frame in `HandleFreezingEffects`). Temporal Fog reuses
  this exact pattern — same shape of perception effect class, driven by its own mod-defined
  watched attribute (not `freezingEffectStrength` itself, so actual cold and Temporal Fog don't
  bleed into each other), likely with a different vignette tint to read as distinct from being
  cold.
- **Harmless apparition spawns kept** (this is the "leave the ghost spawns, those are funny"
  piece): spawns an entity variant that looks like a hostile threat but deals no damage and has
  no real attack AI (appear-and-vanish script instead), drawn from any tier of any entity
  carrying the `rust-creature` tag — confirmed to be exactly three families in this game
  version: `drifter` (normal/deep/tainted/corrupt/nightmare/double-headed), `bowtorn`
  (surface/deep/tainted/corrupt/nightmare/gearfoot), `shiver`
  (surface/deep/tainted/corrupt/nightmare/stilt/bellhead/deepsplit) — 18 variants total across
  the three. Locust is thematically similar but tagged `mechanical`, not `rust-creature`, so it's
  excluded by the game's own taxonomy. Filtering the spawn pool by the tag rather than a
  hardcoded entity-code list means it automatically picks up any rust-creature the base game adds
  later, with no mod update needed.

### Liver Disease (chronic overconsumption — distinct from Liver Failure)

- **Trigger 1: eating too much cooked bushmeat**, specifically — not protein/meat generally.
  Vanilla already tracks per-food-category overconsumption (`EntityBehaviorHunger`'s
  `SaturationLossDelayProtein` and siblings, `EntityBehaviorHunger.cs:55` — the real mechanism
  behind vanilla's "eat a varied diet" nudge), but that value is shared across every
  Protein-category food (bushmeat, redmeat, poultry, fish, …), so it can't be reused directly
  without also catching redmeat/poultry/fish overconsumption, which isn't what was asked for.
  This needs its own dedicated counter, tracking cooked-bushmeat consumption specifically
  (`bushmeat-cooked`, confirmed the correct item code — `bushmeat.json` has no per-animal
  sub-variant, it's one singular meat type, distinct from `redmeat`/poultry/fish as separate
  items entirely), not a hook into the shared vanilla value.
- **Trigger 2: excessive potion consumption generally, especially overdosing, and especially
  Concentrated Potions specifically** — "due to the toxicity of the ingredients and natural side
  effects," per your framing. Unlike Trigger 1, this reuses the *existing* toxicity/overdose
  counter from the brewing pipeline spec (§5) directly rather than needing a new one — that
  counter already rises faster per Concentrated Potion dose than per regular Potion dose, so
  weighting Concentrated Potions more heavily here falls out of the existing mechanic rather than
  needing separate tuning. Any potion can contribute at high enough volume; Concentrated Potions
  contribute more per dose, consistent with how the toxicity counter already treats them.
- **Deliberately not the same effect as Liver Failure at onset** — Liver Failure stays reserved
  for the deadly amatoxin mushrooms (death cap/funeral bell/fool's conecap) as its primary
  trigger. This starts as a separate, milder, chronic-overconsumption condition, not a new
  trigger for the existing severe one.
- **But left untreated, with whichever trigger caused it still ongoing (more bushmeat, or
  continued potion overuse), Liver Disease progresses into Liver Failure itself** — a second
  on-ramp into that same severe poison effect, gated on both conditions at once (untreated *and*
  continued cause), not on time passing alone. This is the same "escalation via continued cause
  while untreated" shape already used for Chest Cold (→ Flu/Bronchitis → Pneumonia) and Wound
  Infection — Liver Disease is now this catalog's overconsumption-based on-ramp to Liver Failure,
  the same way a severe Bleed is the on-ramp to Wound Infection.
- Raw bushmeat cannot be eaten at all — confirmed via `bushmeat.json`'s `nutritionPropsByType`,
  which only defines `*-cooked` and `*-cured` entries, no `*-raw` and no wildcard fallback, so the
  eat interaction has nothing to act on for the raw item. This condition can only ever be
  triggered by the cooked form, never raw, which isn't a design choice so much as a fact about
  what's even possible to eat.
- Exact threshold, severity, and cure are not decided here.

## Poison effects (caused by poison clusters)

Kept as originally proposed, with two additional confirmed engine hooks:

- `EnumDamageType.Poison` is already the damage type vanilla itself uses for a harmful
  poultice-shaped item (`ItemPoultice` with a negative `health` attribute applies
  `EnumDamageType.Poison`, `ItemPoultice.cs:63`) — poison effects delivered as items/doses are
  already aligned with an existing engine convention, not inventing a new one.
- **Hallucination** can reuse the engine's existing `PsychedelicPerceptionEffect` client shader
  class directly — vanilla already built this specifically for the `psychedelic` food attribute
  found on several mushrooms (`mushroom.json`), so no new rendering work is needed for this
  effect.

| Cluster | Effect | Shape |
|---|---|---|
| Deadly Hepatotoxic | Liver Failure | Delayed onset with a mild early warning (small `healingeffectivness` dip during the delay window, giving an attentive player a sliver of a chance to notice before the real damage starts), then escalates to a heavy DoT once the delay expires — not a fully silent timer. **Two triggers now**: the deadly mushrooms directly (primary), or an untreated Liver Disease (condition, above) that keeps getting fed more bushmeat |
| GI-toxic / Emetic | Food Poisoning | Nausea + saturation penalty + moderate DoT, same `ConsumeSaturation` shape as Upset Stomach |
| Cardiac Glycoside | Cardiac Strain | Not triggered immediately on drinking — the poison applies a vulnerability window, and the actual burst/spike damage (or temporary max-health reduction) only fires if the player exerts themselves while it's active. "Exertion" checks `EntityControls.Sprint` (already a real, synced field, `EntityControls.cs:244`), likely also combat activity — no new state-tracking needed for the sprint case at least |
| Neurotoxic / Alkaloid | Weakness / Paralysis | A 3-stage lethal progression, not a flat debuff: Weakness (`walkSpeed` penalty via `EntityStats`) → **Total Paralysis** → **Cardiac Arrest (death)**. Escalation is driven by repeated dosing via the same toxicity counter already used for potion overdose (brewing spec §5) — each additional dose of this poison pushes that counter further, and crossing progressively higher thresholds advances the stage. Reuses existing plumbing rather than a parallel per-dose escalation timer. This is the catalog's first genuinely lethal-by-design poison progression (Liver Failure and Cardiac Strain are dangerous but not framed as a guaranteed kill-chain the way this is) |
| Psychoactive | Hallucination | `PsychedelicPerceptionEffect` reuse, little/no damage — same effect as the Hallucination condition above (overdose/sleep-deprivation), not a separate mechanic |
| Situational/Conditional | *(reuses Food Poisoning at lower severity, no dedicated effect)* | — |

## Poison mitigation: Universal Antidote

Until now nothing in this design let a player counter a poison once it's already in their
system — every poison effect above ends via its own natural duration/thresholds, never by
treatment. **One universal Antidote**, not per-poison-type specific ones: a single recipe/cluster
that mitigates any poison regardless of type, rather than mirroring the remedy side's
per-condition cluster structure. Simpler to build and to remember as a player ("carry this for
emergencies") than a matching antidote per poison cluster.

### Recipe — finalized, and a deliberate exception to the usual substitution rule

**Field Mushroom + Red Wine Cap + Bitter Bolete**, exactly one 6-unit stack of each — **not** the
usual "any mix of the cluster satisfies the 3 stacks" rule from the brewing spec §1. This is a
deliberate, named exception: the three mushrooms are doing conceptually different things
(absorbing different poison mechanisms), not standing in for each other, so the recipe requires
all three specifically rather than treating them as interchangeable cluster members. All three
are on the rarer end of the unused-flora rarity pass above (bitter bolete and field mushroom are
both in the "Rare," `chance: 0.08`, tier; red wine cap is "Uncommon"), which fits an emergency
item you have to specifically seek out and stockpile rather than one you stumble into constantly.

Follows the normal tier rules otherwise (per your call) — the Potion Base can be eaten directly
for a short-duration version of the effect, same as any other remedy, but Diluted Potion stays a
non-functional intermediate; a real dose requires reaching at least the Potion tier through the
full Barrel → Distillery chain, same as everything else.

### Effect — a deliberate two-dose sequence

Not a single-drink cure. The Antidote must be consumed twice in sequence to work:

1. **First dose induces vomiting** — a real, intentional side effect, not a bug to balance away.
   This is a deliberate acknowledgment that Bitter Bolete itself carries genuine toxicity (it's
   listed as one of the mild-toxin mushrooms in this catalog's own poison research) — the cure
   makes you sick before it can work, thematically honest about what's actually in it.
2. **Second dose is when it actually takes effect.** Once consumed as the second dose in
   sequence, it:
   - **Cures any currently active poison outright** — stops an in-progress Weakness →
     Paralysis → Cardiac Arrest ladder, ends an active Liver Failure DoT, clears whatever poison
     state is currently running, not just prevents new poison going forward. A full reset, not
     just future-proofing.
   - **Imposes a 2-in-game-hour restricted diet, not a total lockout** — extended from an
     original 30-minute total food/drink ban, which was too short. During the window: solid food
     is still off-limits, but **drinks are allowed, restricted to broths and fruit juice — no
     alcohol.** Eating any solid food, or drinking alcohol, during the window **re-triggers
     vomiting** (a real penalty for breaking the restriction, not just a wasted action).
   - **Any potion drunk during the window carries a high risk of triggering vomiting and voiding
     that potion's effect entirely** — a strong, explicit disincentive against chaining potions
     right after taking the Antidote, on top of the food/drink restriction.
   - **Grants poison immunity for the same 2-hour window against external sources too** — e.g. a
     poisoned arrow hitting the player during this window does not apply its poison effect.
     Covers both ingested and externally-applied poison uniformly, matching the "works against
     all poisons" design (not per-poison-type).

Not yet worked out: exactly how "second dose in sequence" is tracked (most likely a short-window
dose-count check similar in shape to the toxicity counter, rather than requiring the two doses be
back-to-back with zero gap — Upset Stomach's own vomiting mechanic may naturally introduce a
short gap between doses anyway) — a balance/implementation detail, not a design gap.

## Deferred / explicitly out of scope

- Contagion mechanism (room-based proximity) is now settled — only exact chance/tick-rate and
  whether NPCs/animals can carry it remain open.
- The full "discrete named stages" architecture question for the illness system generally — the
  Chest Cold/Flu/Bronchitis/Pneumonia branching graph and the Weakness/Paralysis/Cardiac Arrest
  ladder are now two concrete examples, not yet a system-wide decision.
- Exact balance numbers: bleed/infection severity thresholds, escalation timing for Chest Cold,
  relapse chance for partial vomiting, contagion rate.
- Antidote recipe and effect are now finalized (Field Mushroom + Red Wine Cap + Bitter Bolete,
  two-dose vomit-then-cure sequence) — only the exact dose-sequence tracking mechanism
  (how "second dose within a window" is implemented) remains an implementation detail.
- Which specific flora recipe produces which specific condition/poison effect at what tier —
  still the next pass once ready to author actual Cookpot recipe JSON, per `flora/conditions.md`.
- **Handbook integration — deliberately sequenced, not forgotten.** Recipes integrate into the
  handbook automatically (standard vanilla behavior for `CookingRecipe`/`BarrelRecipe`-based
  content). A dedicated handbook page explaining this mod's mechanics themselves is wanted, but
  intentionally held until the mechanics are actually settled — writing that page now would mean
  documenting decisions that are still moving.
- PlayerStatusHUD provider implementation (`IStatusStripProvider`) — the icon *system* is
  designed, the provider itself isn't built.
- **Broken bones**, now designed as its own document —
  `docs/superpowers/specs/2026-09-02-broken-bones-injury-design.md`. Splinting (bandage + two
  sticks) remains a captured-but-not-committed idea there, pending outside input, and stays
  explicitly out of this mod's scope (physical first aid, not a remedy) — Pain relief covering a
  break's symptoms extends into that document the same way it now covers everything else.
- Exact per-damage-type armor resistance values (Plate/Chain/Scale/Lamellar/Brigandine/Gambeson
  vs. Slashing/Piercing/Blunt) — shape agreed, numbers not.
