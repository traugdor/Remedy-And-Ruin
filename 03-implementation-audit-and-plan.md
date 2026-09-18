# Implementation Audit & Build-Out Plan

Produced from a full audit of `01-engine-hooks.md`, `02-design-overview.md`, every `.cs` file and
relevant asset under `Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/`, a survey of sibling mods
(ExpandedStomach, ExpandedFitness, BetterBearArmor, StacksCoolSlower, fseasonedfirewood,
ClientEntityAILib), and a fact-check of the design doc's base-game assumptions against the
decompiled source. This is Phase 0-3 of the requested process: audit, building-blocks plan, wiring
plan, and open questions. **No implementation should start from this document until the Open
Questions section below is answered.**

---

## Part A: Current State Audit

Status key: **NOT STARTED** (nothing exists) / **STUBBED** (a switch case, property, or method
exists but contains no real logic — often just a comment describing intent) / **PARTIAL** (real,
working logic exists but is incomplete or unwired) / **COMPLETE**.

### A1. Brewing pipeline (Part 2 §1-4) — COMPLETE

Cookpot → Barrel → Distillery (2 passes) → Vial casting, for all 14 clusters (8 remedy + 5 poison +
Antidote... the exact count per the doc's own clusters). Recipes, item JSON, transitionable
properties, and the Vial's full casting chain (mold → fired mold → clayforming → crucible melt →
`BlockLiquidContainerTopOpened`) all verified present and matching the design doc's numbers exactly
(spot-checked several clusters' Effect×/ratio values). Dosing via the Vial is complete — relies
entirely on vanilla `BlockLiquidContainerBase`, nothing further to build. No code changes needed
here.

### A2. Effect delivery scaffolding (Part 2 §9, cross-cutting persistence) — PARTIAL, further along than the doc admits

- `Patch_RawEating.cs` / `Patch_MealEating.cs` (Harmony prefixes) correctly intercept eating and
  call `EntityBehaviorRemedyEffects.OnItemConsumed`, which parses a `remedyandruinEffect` item
  attribute into an `EffectStruct` and calls `ApplyEffect`.
- `ApplyEffect` writes the effect into `rrpoisons`/`rrpotions` WatchedAttributes correctly and
  (via `parseEffectsAndApply`) spawns an `EffectThreadManager`-owned `EffectTimerThread` — a real,
  working background-thread calendar-timer with save/forced-end/natural-end callbacks, verified
  earlier this session.
- **The actual gameplay effect is missing**: `EffectThreadManager.ApplyEffect`
  (`EffectThreadManager.cs:174-183`) has the `entity.Stats`/DoT application as an explicit
  `PLACEHOLDER` comment. A potion or poison can be drunk, is tracked with a running timer, and
  expires on schedule — but produces **zero actual gameplay effect**.
- **Even once filled in, remedy potions have no magnitude to act on**: `remedyandruinEffect`
  attributes on the 8 remedy-potion items + Antidote carry only `cluster`/`tier`/`isPoison` — no
  `effectMultiplier`/`onsetMultiplier`. Only the poison-mushroom `remedyandruinEffectByType`
  patches carry real multiplier data.

### A3. Raw/cooked poison mushroom handling — PARTIAL, currently inert

`patches/mushroom-effects.json` and `patches/mushroom-side-effects.json` correctly encode every
Effect×/Onset×/Toxic× value from Part 3's tables. But `Patch_RawEating`/`Patch_MealEating` never
read `remedyandruinEffectByType` at all — both have an identical TODO. **Result: eating a raw or
cooked poison mushroom today does nothing through this mod's system; vanilla's own instant
`health` damage still fires unmodified**, and the design's "suppress vanilla instant damage,
replace with the mod's cluster effect" principle (`02-design-overview.md:692-716`) has no
corresponding code anywhere.

### A4. Poison effects (Toxic/Noxious/Cardiac/Neurotoxic/Mind Poison) — STUBBED

All 5 `switch` cases in `ApplyEffect` carry detailed comments transcribing the design doc's own
formulas (tolerance discount, DoT thresholds, etc.) but each is followed only by `poison = true;
break;` — no `EntityStats` call, no DoT call, no actual computation.

### A5. Remedy potions (Analgesic, Antinausea, Antiseptic, Antiviral, Mind Tonic, Sedative, Tonic, Topical Ointment) — NOT STARTED, except Antidote

All 8 non-Antidote cases are empty `break;` statements. No walkSpeed halving, no infection cure, no
Chest Cold suppression, no sleep-bypass, no contagion/HP-restore, no skin-irritation salve — none
of Part 3's per-condition mechanics exist for any of these.

### A6. Antidote — PARTIAL, with one likely bug

Two-dose sequencing works: first dose sets a flag + timestamp and triggers vomiting
(`VoidStomachContents`, real code); second dose within 60 real-world seconds (matches doc's "~1
real-world minute" spec) clears poison state. **Bug**: the second-dose clear reads
`RRPotionEffects.value` (`EntityBehaviorRemedyEffects.cs:514`) when the surrounding comment says
"erase all poison effects" — this looks like a copy-paste variable mix-up that clears potions
instead of poisons. Missing entirely: "second dose resets if a new poisoning happens between
doses," and the whole 2-hour restricted-diet window (broths/juice-only, re-triggered vomiting on
alcohol/solid food, poison immunity during the window).

### A7. Upset Stomach / Vomiting — STUBBED (partially)

`VoidStomachContents` itself is real, working code (drains `EntityBehaviorHunger.Saturation`,
correctly interops with a third-party "expandedStomach" behavior via reflection). But it's only
ever called from the Antidote's first dose; its `chance` parameter is accepted but never used; the
doc's "partial void by default, 25% relapse, 5-minute recovery window" model isn't implemented; and
there's an explicit `//TODO: wire in chance for additional void events if not triggered by
antidote`.

### A8. Tolerance system — STUBBED

5 clamped (0-27) tolerance properties exist, matching the 9-tier/27-exposure model, but nothing
increments, reads, or decays them. No "must fully resolve before counting," no "Antidote negates
tolerance credit," no month-of-no-exposure decay. `EffectThreadManager.OnNaturalEnd` has its own
`PLACEHOLDER` comment correctly noting tolerance should be awarded there (and never from
`OnForcedEnd`) — the design intent is recorded, nothing is built.

### A9. Toxicity/overdose counter (Part 2 §5-6) — NOT STARTED

No counter, no threshold, no per-remedy-potion overdose dispatch exists anywhere.

### A10. Stacking (Part 3) — RESOLVED, no build needed

The design doc's own Stacking section (§Stacking) is unambiguous: every exposure is always its own
fully independent instance, never merged with another regardless of cluster or onset value.
`EffectThreadManager` already gives every applied effect an independent GUID/thread
unconditionally, which is exactly this rule. The Stews/cooked-meals half (per-ingredient dispatch,
no dilution exploit) is also already built via `Patch_MealEating`'s per-ingredient
`OnItemConsumed` calls. No code changes required.

### A11. Arrow poisoning (Part 2 §8) — NOT STARTED

No dip-craft recipe, no poisoned-arrow item variant, no delivery code.

### A12. Hallucination / apparition system (Part 3 "Brain Fog") — PARTIAL, most complete non-pipeline system

Real, working pathfinding-driven wander/beeline/attack AI (Drifter/Shiver/Bowtorn), correct family
resolution, distance rules, concurrency cap, severity-window unlock logic. **Missing only the
trigger**: `getSeverity()` is fed by the `.rrbrainrot` debug command rather than the real
`MINDPOISON` poison case (which doesn't exist yet per A4) — the code's own comment says as much.
Minor doc/code drift: Bowtorn's actual spawn distance is 13-17 blocks; the doc says "roughly 20."

### A13. Temporal Fog / Skull-Strain Concussion visuals (Part 3/4) — PARTIAL, visuals complete, triggers absent

`TemporalVignetteRenderer` + custom shader is real, working, independently-tracked per-effect
strength, correctly composited. Matches the doc's own "implemented and confirmed working" claim.
**Driven only by debug chat commands** — no real gameplay trigger (Temporal Stability threshold,
Mind Tonic overdose, Skull-Strain bone injury, Bell proximity) exists.

### A14. Everything else in Part 3's condition catalog — NOT STARTED

Bleeding, Wound Infection, Chest Cold family (→Bronchitis/Flu→Pneumonia), Liver Disease,
Resurrection Sickness: zero code for any of them (verified via targeted grep — no matches for
`Bleeding`, `EnumDamageOverTimeEffectType`, `curesInfectionByType`, contagion/room-registry checks,
etc.).

### A15. Part 4: Broken Bones — NOT STARTED (entire part)

No location bucketing, tier state machine, splinting, `SetMaxHealthModifiers` usage, or
Bell-proximity headache trigger. The design itself is fully numbered/complete on paper; zero of it
exists in code.

### A16. Part 5: Renewable Cultivation — NOT STARTED (entire part)

No `BlockEntityPlantContainer` subclass, growth-stage machine, or room-registry-based gating.

### A17. "R&R Ongoing Effects" UI tab — NOT STARTED

No dialog/GUI code of any kind for showing active effects to the player.

### A18. Effect-duration persistence infrastructure — PARTIAL, and the scaffolding is genuinely good

`EffectTimerThread`/`EffectThreadManager` implement the `TimeStarted`/`TimeLeft` design reasonably
faithfully (absolute calendar-hour target, live polling, single-pass save write). **But
`HandleGameWorldSaving` and `HandleForcefulEnd` — both fully implemented — are never called from
anywhere in the codebase.** No `PlayerDisconnect` hook exists to trigger a save-flush, and nothing
calls `HandleForcefulEnd` from the Antidote's poison-clear or a death handler. This is real,
working code sitting completely disconnected from the rest of the mod.

### Known bugs (not just gaps)

1. **`EntityBehaviorRemedyEffects.cs:514`** — Antidote's second-dose clear reads
   `RRPotionEffects.value` instead of `RRPoisonEffects.value`.
2. **`EffectThreadManager.HandleGameWorldSaving`/`HandleForcefulEnd`** — fully built, never called.
3. **`ConfigServer.debugMode`** — declared, serialized, never read anywhere.

### Design-doc corrections needed (verified against decompiled source)

1. **`walkSpeed` → `walkspeed`.** The doc uses `walkSpeed` (camelCase) as an `EntityStats`
   category string in several places (Parts 3/4 and the cross-cutting section). Vanilla's real,
   registered category is `"walkspeed"` (all lowercase) — confirmed at `EntityPlayer.cs:368` and
   every real call site (`ItemSnowshovel.cs:12`, `ModSystemWearableStats.cs:357`). `walkSpeed`
   camelCase is only a field name on the unrelated `StatModifiers` class. Using the wrong casing
   would silently create a disconnected, inert stat category.
2. **DoT tick cadence is real (engine) time, not game-calendar time.** `EntityBehaviorHealth`'s DoT
   loop ticks against `entity.World.ElapsedMilliseconds`, not `Calendar.TotalHours`, and isn't
   scaled by calendar speed anywhere. Any DoT tuning math in the doc that assumes calendar-time
   ticking needs revisiting.
3. **Barrel `SealHours` is game-calendar hours, not real-world hours** (`BlockEntityBarrel.cs`
   confirms `SealedSinceTotalHours = Calendar.TotalHours`). The doc's "real-time steep duration"
   phrasing is ambiguous — at the default 48 real-min/game-day, a "~1 week" steep is roughly half
   an hour of real time, not 7 real days.
4. **No sleep-completion hook exists.** Sleep is a continuous time-speed-multiplier ramp
   (`ModSleeping.SetTimeSpeedModifier("sleeping", ...)`), not a discrete jump, and nothing fires an
   event when it ends. Vanilla's own `EntityBehaviorTiredness` detects elapsed sleep purely by
   polling `Calendar.TotalHours` before/after on its own tick — confirming that's the only
   available approach, not a fallback.
5. **"24 game hours = 48 real minutes" is the real vanilla default** (`SpeedOfTime` default 60 ×
   `CalendarSpeedMul` default 0.5), but both are independently server-configurable at runtime. Any
   code converting between real time and game time should read
   `Calendar.SpeedOfTime * Calendar.CalendarSpeedMul` live (exactly what vanilla's own
   `EntityBehaviorHealth.ApplyRegenAndHunger` does) rather than hardcoding 48 minutes/day.
6. **Temporal Stability's tiered-effect structure is not fully verified** — only the client/server
   sync channel and a per-position stability read were confirmed. If the plan wants to mirror its
   tier thresholds specifically, that needs a follow-up read of `TemporalStabilityEffects.cs`
   (past line 90) and `SystemTemporalStability.cs` before being relied on.

### Reusable patterns identified (for use in Part B)

- **Config**: `ExpandedStomach`'s `IModConfig`/`ModConfig.ReadConfig<T>`/`[JsonProperty(Order=N)]`
  + paired description-property + copy-constructor pattern is generic and directly reusable
  verbatim.
- **Per-player decaying counter, calendar-day-gated**: `ExpandedStomach.EntityBehaviorStomach`'s
  `days`/`dayCountOffset` + `if (today > days)` day-boundary check in a slow tick listener is the
  exact shape needed for Tolerance decay and the toxicity counter.
- **entity.Stats keying**: `ExpandedFitness` uses one source-key per mod+purpose and never calls
  `Remove` (always re-zeroes). R&R's use case is different — many independently-timed effect
  instances — so it should key `Set(category, $"rr_{effectGuid}", magnitude, persistent: false)`
  per active effect and call `Remove(category, code)` on expiry, not copy ExpandedFitness's
  never-remove pattern.
- **BetterBearArmor, StacksCoolSlower**: nothing usable — pure JSON-patch content mod and unrelated
  temperature-diffusion Harmony patches respectively.
- **fseasonedfirewood**: a working pattern for live-patching a numeric field (e.g.
  `transitionHours`) in JSON recipe/item definitions from config at load time — relevant if R&R
  wants config-tunable steep/distillation timings. Also confirms Harmony-postfixing
  `BlockEntityBoiler` is a known-working fallback technique in this codebase family if the
  distillery ever needs behavior beyond what `DistillationProps` supports (not expected to be
  needed, per A1/verification above).
- **ClientEntityAILib**: nothing beyond what Hallucination already uses.

---

## Part B: Building Blocks Needed

High-level components, not yet broken into TDD steps — sequenced roughly by dependency, not
necessarily by priority (priority is an open question, see Part D).

1. **Fix known bugs** (A: bugs 1-3 above) — small, standalone, no dependencies.
2. **Real-time↔game-time helper** — a single method reading
   `Calendar.SpeedOfTime * Calendar.CalendarSpeedMul` live, used anywhere the mod needs this
   conversion, replacing any hardcoded ratio.
3. **`entity.Stats` effect-application layer** — fills `EffectThreadManager.ApplyEffect`'s
   placeholder: apply/remove a stat modifier keyed per effect-instance GUID, using verified
   category names (`healingeffectivness`, `walkspeed`, etc.), for any effect whose magnitude is
   just a stat blend.
4. **DoT integration** — wire `EntityBehaviorHealth.ApplyDoTEffect`/`StopDoTEffect` for poison
   effects that need damage-over-time (Toxic Poison's liver failure DoT is the concrete case named
   in the doc's own comments).
5. **Toxicity/overdose counter** — new WatchedAttributes-backed counter (per §5's design),
   day-boundary-gated decay, threshold-crossing dispatch into per-remedy-potion overdose effects
   (§6).
6. **Tolerance tracking** — 5 per-cluster counters (already exist as properties), each needing:
   increment-on-survived-exposure logic (wired from `OnNaturalEnd`, per the existing placeholder),
   last-exposure-day tracking, and day-boundary-gated 5%/day decay after 1 month of no exposure.
7. **`PlayerDisconnect` hook + `EffectThreadManager.HandleGameWorldSaving`/`HandleForcefulEnd`
   wiring** — connect the already-built persistence methods to real trigger points.
8. **`remedyandruinEffectByType` wildcard lookup** — the missing helper both `Patch_RawEating` and
   `Patch_MealEating` need, plus the raw/cooked vanilla-instant-damage suppression mechanic for
   poison-cluster mushrooms.
9. **Remedy potion magnitude data** — `effectMultiplier`/`onsetMultiplier` values need adding to
   all 8 remedy-potion + Antidote item JSON (currently absent) — this is balance data, not code.
10. **Stacking** — resolved without code changes; see A10.
11. **Arrow poisoning** — dip-craft recipe + delivery hook into the same `OnItemConsumed`-adjacent
    pipeline (or a parallel damage-source-triggered path, since arrows don't go through eating).
12. **Per-condition mechanics** (each is its own block, largely independent of each other once (3)
    and (4) exist): Analgesic, Antinausea (finish Upset Stomach), Antiseptic (Wound Infection
    cure), Antiviral (Chest Cold family), Mind Tonic, Sedative, Tonic (Immune contagion + HP
    restore), Topical Ointment (Skin Irritation), plus the 5 poison clusters' actual effects
    (Toxic/Noxious/Cardiac/Neurotoxic/Mind Poison).
13. **Bleeding condition** — location bucketing, severity tiers, bandage interaction.
14. **Liver Disease, Resurrection Sickness** — standalone condition mechanics.
15. **Hallucination trigger wiring** — swap `.rrbrainrot` debug feed for the real `MINDPOISON`
    case's output once (12) exists.
16. **Temporal Fog/Concussion trigger wiring** — same idea, once the conditions that should drive
    them exist.
17. **"R&R Ongoing Effects" UI tab** — new GUI dialog listing active effects.
18. **Broken Bones system (Part 4)** — a large, largely self-contained subsystem: damage-location
    bucketing, tier state machine, splinting, armor-odds hook, healing-duration tracking.
19. **Renewable Cultivation (Part 5)** — a large, self-contained subsystem: custom
    `BlockEntityPlantContainer`, growth stages, indoor/outdoor/greenhouse gating.
20. **Handbook pages** — deferred per the doc's own note; last.

---

## Part C: Wiring Plan

How the building blocks connect once built:

- **Consumption**: `Patch_RawEating`/`Patch_MealEating` → (new) `remedyandruinEffectByType`
  resolution for raw/cooked mushrooms → `OnItemConsumed` → `ApplyEffect` → writes
  `rrpoisons`/`rrillness`/`rrpotions` → `EffectThreadManager.ApplyPoisonEffect`/etc. → spawns
  `EffectTimerThread`.
- **Effect running**: `EffectTimerThread` polls `Calendar.TotalHours` → on natural end, calls
  (new) `entity.Stats`/DoT layer to remove the modifier, and (new) Tolerance-award logic if it was
  a poison that ran its full course → on forced end (antidote/death), removes the modifier only,
  no Tolerance award, per the existing placeholder's own documented intent.
- **Session boundaries**: `PlayerNowPlaying` (existing) → config-change reconciliation (existing,
  working) + (new) reconstructing `EffectTimerThread`s from any `rrpoisons`/`rrillness`/`rrpotions`
  entries still present in WatchedAttributes on login (need to confirm this actually happens today
  — see Open Questions). (New) `PlayerDisconnect` → `HandleGameWorldSaving`.
- **Antidote**: fix the `RRPotionEffects`/`RRPoisonEffects` bug → second dose calls (new)
  `EffectThreadManager.HandleForcefulEnd` for the poison bucket, which now actually gets invoked.
- **Overdose**: toxicity counter crossing a threshold → dispatches into whichever remedy-potion's
  §6 overdose effect, applied through the same `entity.Stats`/DoT layer as everything else.
- **Hallucination/Temporal Fog/Concussion**: once `MINDPOISON` and Skull-Strain-adjacent conditions
  have real logic, their output strength feeds `HallucinationManager.getSeverity()` and
  `TemporalVignetteRenderer`'s strength properties directly, replacing the debug-command feed
  (those debug commands stay, just stop being the only input).

---

## Part D: Open Questions

*(Answer inline below each question, then hand the file back.)*

**D1. Scope for this build-out.** The full design doc includes Part 4 (Broken Bones) and Part 5
(Renewable Cultivation) as complete, standalone subsystems on top of finishing Part 3's condition
catalog. Given the size, should this build-out include all of it, or should Parts 4/5 (and/or
specific Part 3 conditions) be marked **"not included in this release"** and deferred? If deferred,
which ones exactly?

> Answer: Don't defer anything. I can't really think of a reason it should be deferred since all the systems outlined and developed in the documentation were explicitly created to be a part of this mod.

**D2. Doc corrections.** Should I update `02-design-overview.md` directly with the 6 corrections
listed above (walkspeed casing, DoT/barrel timing units, no-sleep-hook, calendar-ratio note,
Temporal Stability caveat), or leave the doc as-is and just make sure the *code* gets these right?

> Answer: Yes the design overview doc is intended to be the master document outlining anything and everything that this mod can do. It will be converted to wiki pages when the mod is finished.

**D3. Bug fixes.** Confirm the three known bugs (Antidote's `RRPotionEffects`/`RRPoisonEffects`
mix-up, `HandleGameWorldSaving`/`HandleForcefulEnd` never called, dead `ConfigServer.debugMode`
field) should just be fixed as part of this build-out rather than called out separately.

> Answer: Yes the bugs should be fixed as the code is built out.

**D4. Remedy potion balance data.** The 8 remedy-potion clusters + Antidote have no
`effectMultiplier`/`onsetMultiplier` values authored yet. Do you have these numbers already
(somewhere not yet shown to me), or do they need to be designed as part of this build-out? If the
latter, that's real game-design work, not just implementation — worth flagging as its own
deliverable rather than assuming I can invent balance numbers.

> Answer: Potions and the Antidote deliberately do NOT have onset and effect multipliers since the effect will always be maximum for the potion type and the onset will always be immediate.

**D5. `PlayerDisconnect` hook semantics.** Given this session's earlier design work on
online-only vs. calendar-anchored effect timing (and the still-open question of what "online-only"
should actually mean once a real disconnect hook exists), do you want me to also resolve that
design question as part of building block #7, or is there already a decision I'm missing?

> Answer: I'm not entirely sure what the open question of "online-only" means. Online means the player connected and playing on the server. With regards to the effect timing, if effects are allowed to expire when the player is offline, then that means that the effects are allowed to expire when the player is no longer connected to the server so that the next time they log in, the effects may be missing. This gets PAUSED when the game calendar also gets paused.

**D6. Reconstructing timers on login.** I couldn't confirm whether an existing effect
(`rrpoisons`/etc. entry already in WatchedAttributes from a prior session) actually gets a fresh
`EffectTimerThread` spawned for it on the next login today — `parseEffectsAndApply`'s
`effectsApplied` guard is in-memory only, so it *should* re-fire on reconstruction, but I haven't
traced this end-to-end against real save/load behavior. Should I verify this specifically before
building anything else on top of it?

> Answer: All existing effects that haven't expired yet should be reapplied the instant that the player rejoins. since the entity.Stats is not persistant, they will need to be rebuilt with their timers and be allowed to expire naturally.

**D7. "Hungry While Injured."** I couldn't locate this mod under any obvious name in
`C:\Users\joelt\source\repos\`. Does it exist somewhere else, under a different folder name, or
should I skip it?

> Answer: It's part of ExpandedStomach as a bugfix for some old base game code. I think it's called HungerPatcher and was renamed/repurposed once the bugfix was incorporated into the base game.

**D8. `LiquidTintTextureSystem`/`RemedyPoisonClusterTextures`.** This runtime texture-generation
system exists in code but isn't mentioned in either design doc. Confirm it's intentional (and I'll
add a line to the doc noting it), or if it should be replaced with hand-authored textures instead.

> Answer: It is intentional and I'm actually considering making it into it's own library mod that can generate live textures on the fly, but that is out of scope for this document.

**D9. Bowtorn spawn distance.** Code uses 13-17 blocks; the doc says "roughly 20." Which is
authoritative — update the doc to match the tuned code value, or retune the code to ~20?

> Answer: Update the doc. 20 was too far away.

**D10. Antidote's missing rules.** The "second dose resets if poisoned again between doses" rule
and the full 2-hour restricted-diet window (broths/juice-only, re-triggered vomiting, poison
immunity) aren't built. Include them in this build-out, or defer?

> Answer: Include.

**D11. Stacking/arrow poisoning priority.** Both are fully-specified in the doc but entirely
unbuilt. Same question as D1 but specifically for these two — in scope for this pass, or deferred?

> Answer: In scope. PLease build I just kind of ... forgot. 

**D12. Execution approach once this is resolved.** Once you've answered the above, do you want the
next document to be the full bite-sized TDD implementation plan (the standard `writing-plans` skill
format) covering everything now in scope, or do you want it broken into multiple smaller plans (one
per major subsystem — e.g. one for Tolerance/Toxicity, one for the 5 poison clusters, one for
Broken Bones, etc.) that can be executed and reviewed independently?

> Answer: Break it into smaller plans where appropriate so that the systems get built and fully debugged BEFORE they get wired together. Debugging can be done by simulating what the code will do in specific scenarios. This means that the broken bones system may actually get built before any wiring gets done so that broken bones can be wired into the other systems during the wiring phase.

---

### Follow-up questions from your answers

**D13. Online-only mode's actual pause trigger (follow-up to D5).** Your answer describes the
*calendar-anchored* mode correctly (pauses exactly when the calendar itself pauses, i.e. when
*every* player is offline). But the *online-only* mode (`allowEffectsToExpireWhenOffline = false`,
the `TimeSpan`/duration path) was originally meant to be scoped to **this specific player's own
connection**, not the calendar's aggregate state — meaning it should keep advancing for Player A
as long as the calendar is running, even if Player A personally disconnects while Player B stays
online and keeps the world ticking. Two different designs are possible here, and they need
different code:

- **(a) Both modes actually just track the calendar's pause state**, not any individual player's
  connection. In that case a `PlayerDisconnect` hook isn't needed for *pausing* anything at all —
  only for *persisting* state before the process might go away — and the "online-only vs.
  calendar-anchored" distinction collapses down to just "does the absolute target stay fixed
  forever (calendar-anchored) vs. does it get recomputed from a persisted remaining-hours value
  each session (online-only)," with both otherwise ticking whenever the calendar ticks.
- **(b) Online-only genuinely means paused specifically while *this player* is disconnected**,
  independent of whether other players keep the server (and calendar) running. This needs the
  `PlayerDisconnect` hook to actually stop/snapshot that player's own threads, and reconstruct them
  fresh on their own reconnect — a real behavioral difference from the calendar-anchored mode
  whenever other players are online.

Which one did you actually intend?

> Answer: If the config is set to true, then it relies on the game calendar advancing regardless of who is logged in. The effect doesn't keep ticking, it just understands that if the game calendar advances beyond the effect's expiration time, then the effect will not apply next time the player logs in. If the config is set to false, then it still relies on the game calendar advancing but ONLY when the player that has the effect is logged in. The effect will pick up where it left off regardless of how much time has passed since they last logged in and continue to tick until the player logs off again or it expires.

> **Clarification (supersedes the above where it conflicts):** The thread manager is per-player and
> only runs while that player is online — disconnecting always kills that player's threads,
> regardless of config, specifically so the server can shut down gracefully while players are
> offline. So in both modes, effects physically PAUSE on disconnect; the two modes differ only in
> what happens at reconnect:
> - **`false` (online-only):** the persisted remaining time is reused exactly as-is at reconnect,
>   with no adjustment for elapsed offline time — genuinely paused, picks up where it left off.
> - **`true` (calendar-anchored):** at reconnect, if the current calendar time has already passed
>   the effect's calculated absolute end time, the effect is treated as having **expired naturally**
>   (i.e. should count toward Tolerance/etc. the same way a live natural expiry does, not like a
>   forced end) even though nothing was actually running to observe it happen.
>
> Implementation consequence for Plan 7: the `false` mode must persist a plain remaining-hours
> value reused as-is; the `true` mode must persist the **original absolute target itself** (not a
> recomputed "current calendar + remaining" value — recomputing that at every reconnect would
> incorrectly grant a fresh, later deadline instead of preserving the real one, which is the exact
> mistake corrected in this mod's code two sessions ago and must not reappear here). Disconnect
> handling also needs to actually stop/dispose each thread after capturing its final state, not just
> report-and-keep-running like `HandleGameWorldSaving` currently does.

**D14. Testing approach for "build and debug in isolation via simulated scenarios."** This project
has no test project today (no xUnit/NUnit harness, nothing under a `tests/` folder) — everything's
been verified so far by reading code and reasoning through it together in chat, or by you running
the mod live. For each subsystem's "build and fully debug before wiring" phase, do you want:

- **(a) Real automated tests** — a proper test project referencing the mod's classes directly where
  possible (most of this mod's logic — Tolerance math, toxicity decay, stat-key generation, the
  timer's calendar-hours math — doesn't actually need a live `ICoreServerAPI`/`Entity` to test, it's
  plain C# logic wrapped around WatchedAttributes-shaped data), with a thin seam so the
  Vintage-Story-API-dependent parts can be faked/stubbed; or
- **(b) Manual "simulated scenario" walkthroughs** — I trace specific inputs through the code by
  hand/in-chat and report what would happen, without a runnable test suite; or
- **(c) A mix** — real automated tests wherever the logic is pure/decoupled enough to unit-test
  cheaply, manual walkthroughs only for the parts that are unavoidably coupled to the live API.

> Answer: Mostly B. Just be smart about it and actually examine what the code WILL do and not what it *SHOULD* do.

---

## Part E: Proposed Subsystem Plan Breakdown (draft, for sign-off)

Per D12, here's a proposed split into standalone build-and-debug plans, each producing a
self-contained, testable piece before any cross-system wiring happens. Roughly ordered by
dependency (later ones assume earlier ones' public surface exists), not by priority — everything's
in scope per D1/D11, so order here is about what unblocks what, not what matters more.

1. **Bug fixes + doc corrections** — the 3 known bugs (D3) and the 6 design-doc corrections (D2),
   done together as one small pass since neither depends on anything else.
2. **Real-time↔game-time helper** — the `Calendar.SpeedOfTime * Calendar.CalendarSpeedMul` utility;
   tiny, used by several later plans.
3. **`entity.Stats` effect-application layer** — fills `EffectThreadManager.ApplyEffect`'s
   placeholder for stat-blend-based effects; needed by nearly everything downstream.
4. **DoT integration layer** — the `EntityBehaviorHealth.ApplyDoTEffect`/`StopDoTEffect` wrapper;
   needed by Toxic Poison and possibly others.
5. **Toxicity/overdose counter** — standalone WatchedAttributes-backed system (per §5/§6),
   buildable and testable independent of what triggers it.
6. **Tolerance tracking** — standalone (5 per-cluster counters, decay, increment hook), buildable
   and testable independent of what calls it.
7. **Session/persistence wiring**: the `PlayerDisconnect` hook, `HandleGameWorldSaving`/
   `HandleForcefulEnd` call sites, and login-time timer reconstruction (D6) — this one actually sits
   closer to "wiring" than "standalone building block" since its whole job is connecting existing
   pieces; may make sense to build alongside plan 12 (final wiring) rather than before it. Flagging
   this as a placement question below rather than deciding unilaterally.
8. **`remedyandruinEffectByType` wildcard lookup + raw/cooked damage suppression** — standalone
   fix to the two Harmony patches.
9. **Antidote fixes + missing rules** — the bug fix, the between-doses reset rule, and the 2-hour
   restricted-diet window, all together as one plan (all touch the same class/area).
10. **Stacking** — resolved without code changes; see A10.
11. **Arrow poisoning** — dip-craft recipe + delivery, standalone.
12. **The 5 poison-cluster effects** (Toxic/Noxious/Cardiac/Neurotoxic/Mind Poison) — one plan,
    since they share the same `ApplyEffect` switch and the same tolerance-discount formula shape.
13. **The 8 remedy-potion effects** (Analgesic, Antinausea, Antiseptic, Antiviral, Mind Tonic,
    Sedative, Tonic, Topical Ointment) — one plan per D4's "always max/immediate" simplification.
14. **Bleeding condition** — standalone.
15. **Chest Cold family, Liver Disease, Resurrection Sickness** — grouped as one plan (all are
    standalone condition state machines of similar shape).
16. **Broken Bones (Part 4)** — its own large plan, buildable in isolation per your explicit note in
    D12.
17. **Renewable Cultivation (Part 5)** — its own large plan, fully independent of the poison/remedy
    systems.
18. **"R&R Ongoing Effects" UI tab** — standalone once there's real effect data to display.
19. **Final wiring plan** — connects everything: Hallucination/Temporal Fog/Concussion trigger
    swaps, Broken Bones ↔ poison/remedy cross-references (e.g. Bell proximity, splinting
    interactions), the session/persistence hooks from #7 if deferred here, and anything else that
    only makes sense once every piece exists.
20. **Handbook pages** — last, per the doc's own note.

Does this breakdown and ordering look right, or would you reshuffle/split/merge anything before I
start writing the first actual plan document?

> Answer: the ordering looks right

## Part F: Deferred / Post-Plan Ideas

Ideas raised after this plan was locked in, not part of the numbered sequence above. Revisit once
all 20 steps are complete.

- **Vial rack block** — a shelf-like storage block whose sole purpose is holding `Vial` blocks
  (empty or filled), each rendered in-world as if placed, retaining its liquid content and
  `poisonCharges` attribute. Feasibility researched: `BlockVial` already implements
  `IContainedMeshSource` (via `BlockLiquidContainerTopOpened`) and `vial.json` already sets
  `"shelvable": true`, so vanilla's `BlockEntityDisplay` (the base class behind shelves/bookshelves/
  tool racks) can likely be subclassed directly, inheriting mesh generation and save/load for free.
  Main custom work: `genTransformationMatrices()` for slot layout and put/take interaction logic.
  Verdict: easy-to-moderate. Worth double-checking whether `poisonCharges` needs to be folded into
  the mesh cache key so two vials with different charge counts don't render identically.
