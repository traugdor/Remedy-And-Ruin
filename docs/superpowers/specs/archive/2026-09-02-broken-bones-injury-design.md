# Broken Bones — Design Spec

> **Archived — superseded.** Folded into `02-design-overview.md` (Part 4). This file is kept for
> history only; edit the master document instead.

Status: **approved design, not yet implemented**. This is the last named-but-undesigned
condition flagged as a TODO during the illness/poison effect catalog work
(`docs/superpowers/specs/2026-09-02-illness-poison-effects-design.md`). It builds directly on
that catalog and on the brewing pipeline spec — Pain relief, the Hallucination/Temporal Fog
split, and `EntityStats`/DoT/max-health-modifier machinery are all reused here rather than
reinvented. Splinting (physical first aid — cloth bandage + two sticks) is explicitly out of
scope for this mod, per the earlier scoping note in the illness/poison effects spec; a Pain
relief remedy easing symptoms is in scope.

**Note the reverse borrowing too**: worsening, untreated Wound Infection can independently apply
this same work-speed/offhand-lock (Chest/Arms) or movement-speed/immobilization (Legs) debuff
shape on the affected limb, without actually pushing that limb into this document's tier system
— see the illness/poison effects spec's Wound Infection section. The two mechanics share a
debuff shape on purpose but stay separate; an infected limb reading as symptom-similar to a
broken one doesn't mean it's tracked as broken.

## Engine grounding checked before designing this

- **Hit location data already exists on incoming damage.** `DamageSource` (the actual object
  passed to `Entity.ReceiveDamage`, not just outgoing-attack targeting) carries a `HitPosition`
  field — "the relative hit position of where the damage occurred"
  (`VintagestoryAPI/Vintagestory.API.Common/DamageSource.cs:22`). Its `DamageOverTimeType`
  field's own doc comment even says *"This is used to look for specific types of DoT effects.
  For example it can be used to stop bleeding with bandages"* — vanilla's own source comments
  describe this mod's Bleeding mechanic almost exactly. Bucketing a hit into
  Skull/Chest-Arms/Legs by `HitPosition`'s Y-offset relative to the target's own hitbox height is
  therefore buildable without new engine plumbing for the raw data — but **which attack sources
  actually populate `HitPosition` reliably is not yet verified per-source** and needs checking
  during implementation, not assumed universal from the field merely existing.
- **Per-body-slot armor already exists.** `itemtypes/wearable/seraph/armor.json` defines a
  `bodypart` variant group with states `head`/`body`/`legs` — armor is already equipped
  per-location, not as one flat "armor level." This directly supports the requirement that head
  armor (and only head armor) affects skull-injury odds, chest/leg armor affects
  Chest-Arms/Legs-injury odds, and none of them cross-protect each other.
- **`SetMaxHealthModifiers(string key, float value)`** (`EntityBehaviorHealth.cs`, already found
  during the Bleeding/Infection design work) is a keyed, stackable max-health modifier — exactly
  what Fracture's -10 max health needs, keyed per-limb so multiple simultaneous fractures don't
  stomp each other and each clears independently on healing.

## The three locations and their tier ladders

| Location | Tiers |
|---|---|
| Chest/Arms | Strain → Fracture → Break → Crush |
| Legs | Strain → Fracture → Break → Crush |
| Skull | Strain → Fracture → **(3rd escalation = death)** |

Skull has no Break/Crush equivalent — a third escalation past Fracture is fatal outright (brain
injury), not a named injury tier with its own symptom set.

## Tier progression: escalation, not independent rolls

**A limb's tier advances through repeated qualifying hits to that same limb, not a fresh
independent severity roll per hit.** An already-Strained limb is more vulnerable to becoming
Fractured; a Fractured limb is more vulnerable to Breaking; a Broken one, to Crushing. This gives
armor/damage-source/damage-location/damage-type a consistent job across the whole system: they
affect the *odds of the next escalation* on a given hit, not a one-shot outcome computed fresh
each time. Exact odds/formula are not decided here (balance value), but the shape is: each
qualifying hit to an injured limb rolls against those factors for a chance to push that limb's
tier up by one.

## What each tier does

Chest/Arms and Legs are one track (4 tiers); Skull is a separate, shorter track (2 tiers) with
its own distinct symptoms — **headache/brain-fog/confusion is Skull-only**, not a symptom
Chest/Arms or Legs injuries cause at all, and **the -10 max health penalty applies only at
Skull's Fracture tier** (its severity 2, the last non-fatal stage). The two tracks share the
Strain/Fracture tier *names* only — what each tier actually does differs by location, not just
by degree.

### Chest/Arms and Legs (4-tier track)

**Strain**

- **Pain**, treatable with a Pain relief remedy (the existing modifier system, now covering
  Bleeding/Wound Infection/Skin Irritation/Hallucination/Temporal Fog/Flu's fever — broken-bone
  pain is a natural addition to that same list, not a new mechanic).
- Treatable with remedies generally (clears with treatment, same self-resolving-but-not-safe
  shape used throughout this catalog — left untreated it's the on-ramp to Fracture).
- No headache/confusion symptom here — that's Skull-specific, see below.

**Fracture**

- Everything Strain has (pain, remedy-treatable). No max-health loss at this tier — that penalty
  is specific to Skull's Fracture only.
- What, if anything, distinguishes Fracture from Strain for Chest/Arms and Legs beyond
  remaining on the escalation path toward Break is **not decided** — flagged as an open item
  rather than assumed to be "the same as Strain" by default.

**Break** (Skull doesn't reach this tier)

- **Chest/Arms**: slows work speed with tools; if severe enough, can prevent holding anything in
  the offhand at all; causes pain.
- **Legs**: slows movement speed; if severe enough, can prevent movement entirely; causes pain.
- "If severe enough" implies Break itself may have internal graduation (a weaker vs. stronger
  Break), not decided here — see open items.

**Crush** (Skull doesn't reach this tier)

- **Break's "if severe enough" clauses become guaranteed, not conditional** — total offhand lock
  for Chest/Arms, total immobilization for Legs, always, not situationally.
- **Adds a real ongoing cost on top** — an HP/DoT-shaped penalty (same DoT machinery used
  throughout this catalog) rather than just a stronger version of Break's existing debuffs. This
  reflects genuinely severe tissue damage, distinguishing Crush from being merely "Break but
  worse" in degree — it's worse in kind.

### Skull (2-tier track, then death)

**Strain**

- Same headache/confusion + pain shape as Chest/Arms and Legs' Strain, remedy/painkiller
  treatable.
- **Also where the Bell/Temporal-Symphony-bellhead-shiver headache trigger lives** — a second,
  unrelated, self-resolving cause of this same symptom, independent of any actual skull injury:
  prolonged proximity to an active (ringing) Bell enemy — `entities/lore/bell.json`, class
  `EntityBell`, tagged `mechanical` (same tag as Locust, **not** `rust-creature` — distinct from
  the drifter/bowtorn/shiver family already used for Temporal Fog's apparition spawns). Its
  "ringing" is a real mechanic, not flavor: a looping alarm sound with a 48-block range, toggled
  server-side (`EntityBell.cs:33-62`). Being within range for long enough while it's active
  triggers the headache; unlike the injury-driven version, this one **self-resolves on its own
  after a time with no treatment needed** — a lighter-weight environmental hazard, not an
  escalating injury.
  - **Settled: this is its own standalone effect, not Hallucination or Temporal Fog.** The Bell
    is mechanical, not rust-creature or stability-linked, so its disorientation is purely
    sonic/mechanical — a third, distinct symptom-presentation from the other two, which is also
    why it's lighter-weight (self-resolving, untreated) rather than sharing either's fuller
    mechanic. This is separate from the still-open question of what the *bone-injury-caused*
    Skull-Strain headache above maps to — that one remains undecided.
  - **Conditional extension to bellhead shivers, gated on a specific mod being installed**: the
    "Temporal Symphony" mod (https://mods.vintagestory.at/temporalsymphony, mod ID
    `temporalsymphony`) adds "bellhead shivers with custom bell-ringing behavior and spawning
    mechanics" as one of its own features — vanilla bellhead shivers don't actually ring, so this
    mechanic only extends to them when that specific mod gives them ringing behavior. Detected
    via `IModLoader.IsModEnabled("temporalsymphony")` (`IModLoader.cs:26`, a real, simple
    soft-dependency check), not a hard dependency — this mod works fully without Temporal
    Symphony installed, just without the bellhead-shiver extension.

**Fracture** — severity 2, the last non-fatal tier

- Everything Strain has (headache/confusion, pain, remedy-treatable), **plus a flat -10 max
  health** via `SetMaxHealthModifiers`, keyed per-limb. **This penalty is unique to Skull's
  Fracture tier** — Chest/Arms and Legs Fracture does not carry it.
- A third escalation from here is death outright (brain injury), not a named injury tier — Skull
  never reaches Break or Crush.

## Injury chance: four factors, location-scoped armor

**Armor type, damage source, damage location, and damage type all factor into escalation
odds.** The location-scoping matters specifically: armor only affects the odds for the location
it actually covers. Wearing chest and leg armor does not reduce skull-injury odds — only head
armor does, and vice versa. This falls directly out of the already-existing per-`bodypart` armor
slot system (head/body/legs are separate equipped items, per `armor.json`), not something new to
build for the location-scoping itself — the new work is reading the correct slot's armor value
against the correct location's roll, not inventing per-slot armor.

## Deferred / not decided here

- Whether Skull-Strain's bone-injury-caused confusion symptom is Hallucination, Temporal Fog, or
  a choice between them (the Bell/Temporal-Symphony trigger is settled as its own standalone
  effect — this item is only about the injury-caused version).
- What, if anything, distinguishes Chest/Arms and Legs' Fracture from their Strain beyond
  progress along the escalation path.
- Exact escalation-chance formula: how armor/damage-source/damage-location/damage-type combine
  into a probability, and how much an already-injured limb's own current tier weights the next
  roll.
- Whether Break has internal graduation ("severe enough" implies a spectrum within the tier, not
  stated as binary here).
- Exact DoT/HP-cost values for Crush.
- Treatment specifics: which remedy cluster(s) treat Strain/Fracture, and whether Break/Crush are
  remedy-treatable at all or require time/rest (splinting is explicitly out of scope for this
  mod, per the earlier scoping note — but if Break/Crush need *some* mitigation path and it isn't
  splinting, what that is remains open).
- Recovery: how a limb heals back down the tier ladder (natural regression over time vs.
  treatment-driven only), symmetric to how it escalates.
- Whether Crush's guaranteed offhand-lock/immobilization can be temporarily lifted by anything
  (e.g. a strong enough Pain relief dose), or is absolute regardless of treatment until the
  underlying injury itself heals.
