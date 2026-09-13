# Broken Bones

2 potions get referenced throughout below:

- **Analgesic** — the painkiller. Halves (regular) or clears (Concentrated) a walkSpeed penalty.
  Never touches the underlying injury or its healing.
- **Mind Tonic** — the headache/confusion reliever. Halves (regular) or clears (Concentrated) the
  headache symptom only. Never touches the underlying injury.

## The 3 locations and their tier ladders

| Location | Tiers |
|---|---|
| Chest/Arms | Strain → Fracture → Break → Crush |
| Legs | Strain → Fracture → Break → Crush |
| Skull | Strain → Fracture → **(3rd escalation = death)** |

Skull has no Break/Crush equivalent — a 3rd escalation past Fracture is fatal outright (brain
injury), not a named injury tier.

## Engine support

The game already supports everything this system needs, with no new engine code required:

- Hits already carry a location, so bucketing a hit into Skull/Chest-Arms/Legs is possible today.
- Armor is already equipped per body location, so location-scoped protection just works.
- A keyed, stackable max-health modifier already exists, ready to use for Skull Fracture's -10
  max health.

## Tier progression

- A limb's tier advances through repeated injury, not a single severity roll per hit.
- 2 things can trigger an escalation attempt:
  - Taking a new qualifying hit to that limb.
  - Using an unsplinted injured limb at all (swinging a tool, walking on it).
- Splinting removes the use-triggered risk. A fresh direct hit can still escalate a splinted
  limb.
- Leaving pain untreated does **not** advance the tier by itself — Analgesic manages pain only,
  it's not the on-ramp to a worse injury.

### Hit-triggered escalation

1 magnitude formula for every damage source — no damage-type gating for now. Melee ends up
reading as mostly sequential in practice simply because most hits are small; a fall or a
genuinely massive hit can skip tiers entirely, straight to Crush.

| Damage taken (post-armor) | Chance to escalate | Jump size on success |
|---|---|---|
| Minor, <4 | 5% | +1 tier |
| Moderate, 4-8 | 15% | +1 tier |
| Severe, 8-12 | 30% | +1-2 tiers |
| Catastrophic, 12+ | 50% | +2-3 tiers |

- Catastrophic's 12+ threshold matches a corrupt drifter's hit (~80% of base 15 max HP in 1
  hit) — a real, grounded reference point, not an arbitrary number.
- This is a percentage chance, not a guaranteed jump. Armor's job is reducing how much damage
  reaches this formula in the first place.
- Not split by damage type yet, even though most enemies only deal slashing damage today —
  worth revisiting once vanilla's 1.23 combat update lands multiple damage types per attack.

### Use-triggered escalation

- Flat 5% chance per qualifying use of an unsplinted injured limb.
- Always +1 tier on success — never a multi-tier jump.

## What each tier does

### Chest/Arms

- **Strain** — -5% work speed (pain). Fully usable. Analgesic halves the penalty; doesn't heal
  the injury. Needs splinting to heal.
- **Fracture** — -10% work speed (same pain, dialed up). Fully usable, just more painful than
  Strain. Same treatment rules as Strain.
- **Break**, unsplinted — work speed severely slowed, offhand locked entirely.
- **Break**, splinted — capped at -40% work speed. Offhand can hold light/passive items (a
  lantern, tools) but not a shield.
- **Crush** — same functional lockout as Break, splinted or not. The *only* difference from
  Break is a much longer healing time. Splinting is the only way to ease Crush's lockout;
  nothing else touches it.
- **Bow use (Break/Crush only)** — unsplinted, blocked entirely (a bow needs 2 working arms).
  Splinted, allowed but penalized: draw time up and damage down, both by the same config value
  (default 40%). A separate config toggle can re-allow bow use unsplinted too — the penalty
  still applies either way. Doesn't apply to spears (thrown one-handed).

### Legs

- **Strain** — -5% movement speed (pain). Fully usable. Analgesic halves the penalty; doesn't
  heal the injury. Needs splinting to heal.
- **Fracture** — -10% movement speed (same pain, dialed up). Fully usable, just more painful
  than Strain. Same treatment rules as Strain.
- **Break**, unsplinted — movement prevented entirely.
- **Break**, splinted — capped at -40% movement speed.
- **Crush** — same functional lockout as Break, splinted or not. The *only* difference from
  Break is a much longer healing time. Splinting is the only way to ease Crush's lockout;
  nothing else touches it.

### Skull

- **Strain** — headache/confusion + pain. Mind Tonic and Analgesic only manage those symptoms —
  neither heals the underlying injury.
  - A ringing Bell enemy nearby causes this same headache on its own, unrelated to actual
    injury — self-resolves once out of range, no treatment needed.
  - If the Temporal Symphony mod is installed, this also extends to bellhead shivers.
- **Fracture** — everything Strain has, plus a flat -10 max health. 1 more escalation is
  death outright. Skull never reaches Break or Crush.

## Injury chance

- Armor, damage source, damage location, and damage type all affect escalation odds.
- Armor only protects the location it actually covers — head armor doesn't help legs, etc.

## Splinting

- **Apply**: hold 4 sticks (offhand) + a cloth bandage *or* 3 ropes (main hand), use on the
  injured limb. Consumes both.
- **1 application per injury** — a splint can't be applied twice.
- **Required for healing** — an unsplinted injury never heals on its own, at any tier.
- **Blocks the use-triggered escalation risk** — a fresh direct hit can still escalate it
  regardless.
- **Restores a floor of function, Break/Crush only** — Strain and Fracture are already usable
  unsplinted, nothing to restore there. Break/Crush splinted: -40% movement/work speed, offhand
  can hold light items but not a shield.
- **Does not touch pain at all** — Analgesic is separate, and the 2 are complementary.

## Healing duration

Sample numbers — not yet reviewed.

- Based on ~1.5 months to heal a broken bone in reality.
- Expressed in the game's own month length (scales automatically with server settings, not a
  fixed day count).

| Tier | Duration (splinted) |
|---|---|
| Strain | 0.75 months (half baseline) |
| Fracture / Break | 1.5 months (baseline) |
| Crush | 2.25 months (baseline +50%) |
| Unsplinted (any tier) | Never heals |

**Recovery**:
- Flat countdown at full severity; clears entirely at zero. No stepping back down through tiers
  on the way.
- A fresh hit mid-heal rolls against the escalation table: success resets the countdown to the
  new tier's duration; a failed roll still docks 10% of the current tier's duration.
