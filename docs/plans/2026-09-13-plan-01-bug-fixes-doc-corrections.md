# Plan 01: Bug Fixes + Design Doc Corrections

> Testing approach for this plan: manual trace-through, per project decision (see
> `03-implementation-audit-and-plan.md` D14) — no automated test harness exists in this repo.
> Each "verify" step means actually reading the code path with the described inputs and reporting
> what it does, not what it's supposed to do.

**Goal:** Fix the three known bugs and correct the design doc's six verified inaccuracies, with no
other behavior changes — this plan touches nothing that depends on any other plan.

**Architecture:** Two independent tracks (code fixes, doc fixes) that can be done in either order.

---

### Task 1: Fix the Antidote's `RRPotionEffects`/`RRPoisonEffects` mix-up

**Files:**
- Modify: `Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/GameEngineTweaks/EntityBehaviorRemedyEffects.cs:514`

- [ ] **Step 1: Read the current bug in context**

Read lines 497-521 of `EntityBehaviorRemedyEffects.cs`. Confirm line 514 currently reads:

```csharp
List<TreeAttribute> rrpoisons = RRPotionEffects.value.ToList<TreeAttribute>();
```

inside the `antidote` second-dose branch, whose surrounding comment says `//erase all poison
effects`. Confirm this is genuinely wrong: it reads from `RRPotionEffects` (potions) and later
writes the cleared list back to `RRPoisonEffects` (line 516) — so today, this branch clears
whatever was in the *potions* array (copying it into the poisons array, now emptied) while leaving
the real poison array's original contents completely untouched. Poisons are not actually cleared by
a second Antidote dose today.

- [ ] **Step 2: Fix it**

```csharp
List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
```

- [ ] **Step 3: Manually trace the fixed behavior**

Trace: player has 2 active poisons and 1 active potion tracked. They drink the Antidote twice
within 60 seconds. Confirm: `RRPoisonEffects` ends up as an empty array (both poisons cleared),
`RRPotionEffects` is untouched (the 1 active potion survives). Report this trace result before
moving on — do not assume the fix is correct without tracing it.

- [ ] **Step 4: Commit**

```bash
git add "Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/GameEngineTweaks/EntityBehaviorRemedyEffects.cs"
git commit -m "fix: antidote second dose now clears poisons instead of potions"
```

---

### Task 2: Wire `EffectThreadManager.HandleForcefulEnd()` into the Antidote's second dose

**Files:**
- Modify: `Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/GameEngineTweaks/EntityBehaviorRemedyEffects.cs:497-521`

Scope note: `HandleForcefulEnd()` stops every currently-running Poison/Illness `EffectTimerThread`
and removes their entries from `WatchedAttributes` in one pass (verified this session). It does
not depend on a `PlayerDisconnect` hook — that's a separate concern, reserved for
`HandleGameWorldSaving()` in a later plan (session/persistence wiring). This task only wires the
half that's safe to wire now.

- [ ] **Step 1: Read `HandleForcefulEnd`'s current signature**

Read `EffectThreadManager.cs`'s `HandleForcefulEnd()` method. Confirm it takes no arguments,
returns `bool`, and is a public method on `EffectThreadManager` — the same `threadManager` field
already used elsewhere in `EntityBehaviorRemedyEffects` (constructed in this class's own
constructor).

- [ ] **Step 2: Call it alongside the array clear**

In the same second-dose branch fixed in Task 1 (after the fix), add the call. The branch should
read:

```csharp
if (timeAntidoteConsumed.AddSeconds(60) > DateTime.Now)
{
    //within the time frame
    //erase all poison effects
    List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
    rrpoisons.Clear();
    RRPoisonEffects = new TreeArrayAttribute(rrpoisons.ToArray());
    threadManager.HandleForcefulEnd();
    drankAntidote = false;
    timeAntidoteConsumed = DateTime.MinValue;
}
```

- [ ] **Step 3: Manually trace for a double-clear race**

Trace: `HandleForcefulEnd()` internally does its own `RemoveGuidsFromWatchedAttributes` call,
which rewrites `RRPoisonEffects`/`RRIllnessEffects`/`RRPotionEffects` from scratch based on
whichever GUIDs it collects from currently-running threads. The manual `RRPoisonEffects = ...`
clear above happens *first*, then `HandleForcefulEnd()` runs and does its own separate rewrite of
all three arrays. Confirm whether this ordering causes any lost update or double-write — if
`HandleForcefulEnd()`'s snapshot of "threads that were running" was taken before the manual clear,
and its `RemoveGuidsFromWatchedAttributes` reads `RRPoisonEffects`/`RRIllnessEffects` fresh (not a
stale copy) when building its own final array, there's no conflict since both end up removing the
same entries by different means. Report whatever the actual trace shows, including if it finds
the manual clear above is now fully redundant with what `HandleForcefulEnd()` already does on its
own (in which case, say so plainly — don't silently leave dead code in place if the trace shows
the first three lines of this branch became unnecessary).

- [ ] **Step 4: Commit**

```bash
git add "Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/GameEngineTweaks/EntityBehaviorRemedyEffects.cs"
git commit -m "fix: wire HandleForcefulEnd into antidote's second dose"
```

---

### Task 3: Remove the dead `ConfigServer.debugMode` field

**Files:**
- Modify: `Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/Config/ConfigServer.cs`

- [ ] **Step 1: Confirm it's genuinely unread**

Search the whole `Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/` tree for `debugMode` (case
sensitive). Confirm the only hits are: the property declaration, its `[JsonProperty]` attribute,
and the copy-constructor line carrying it forward — no `if (Config.debugMode)` or similar read
anywhere.

- [ ] **Step 2: Remove it**

In `ConfigServer.cs`, delete the `debugMode` property declaration and its `[JsonProperty(Order =
2)]` attribute, and delete the line in the copy-constructor that carries it forward
(`debugMode = previousConfig.debugMode;`). Renumber the remaining `[JsonProperty(Order = N)]`
values so they stay sequential starting from 1 (this changes the generated JSON file's field
order on next config regeneration, which is expected and fine — `ModConfig.ReadConfig`'s
copy-constructor migration path already handles a changed shape).

- [ ] **Step 3: Commit**

```bash
git add "Remedy And Ruin/Remedy And Ruin/Remedy And Ruin/Config/ConfigServer.cs"
git commit -m "chore: remove unused ConfigServer.debugMode field"
```

---

### Task 4: Correct `02-design-overview.md`'s six verified inaccuracies

**Files:**
- Modify: `02-design-overview.md`

- [ ] **Step 1: Fix "Real-time steep duration" (Barrel `SealHours` is game-calendar time)**

At line 470, replace:

```
- Real-time steep duration, default ~1 week, defined per-recipe (same `SealHours` field as
```

with:

```
- Game-calendar steep duration, default ~1 week of in-game time, defined per-recipe (same
  `SealHours` field as
```

(keep whatever the line's continuation was — only the "Real-time" → "Game-calendar ... in-game
time" wording changes). Apply the same correction at line 672, which reads "Sealed real-time
steeping" — change to "Sealed game-calendar-time steeping". `SealHours` is verified
(`BlockEntityBarrel.cs`) to be measured against `Api.World.Calendar.TotalHours`, not real wall-clock
time — at the default 48 real-minutes-per-game-day ratio, a "~1 week" (168-hour) steep is roughly
half a real-world hour, not seven real days.

- [ ] **Step 2: Fix `walkSpeed` → `walkspeed` where it's asserted as a literal `EntityStats` code**

At line 1831, replace:

```
- **`healingeffectivness` and `walkSpeed`** (`EntityStats`) are the two workhorse debuff stats,
```

with:

```
- **`healingeffectivness` and `walkspeed`** (`EntityStats` — note the category string is all
  lowercase, `"walkspeed"`, confirmed against `EntityPlayer.cs`'s own `Stats.Register` calls; the
  camelCase `walkSpeed` used elsewhere in this doc is descriptive prose, not the literal string to
  pass to `Stats.Set`/`GetBlended`) are the two workhorse debuff stats,
```

Leave every other `walkSpeed` occurrence in the doc as-is — they're descriptive prose (e.g. "a
`walkSpeed`/stamina penalty"), not assertions about the literal code string, and don't need
changing. This is the only line making a code-identifier claim.

- [ ] **Step 3: Fix the Bowtorn spawn-distance claim to match the tuned, working value**

At line 1245, replace:

```
  invisible for its entire lifetime, at a point **roughly 20 blocks away, randomized within the
```

with:

```
  invisible for its entire lifetime, at a point **13-17 blocks away, randomized within the
```

(20 blocks was tuned down during implementation — too far away for the windup sound to read as a
real threat; 13-17 is the current, correct, working value in `HallucinationManager.cs`.)

- [ ] **Step 4: Add a note that DoT tick cadence is real (engine) time, not game-calendar time**

Near line 600, where the doc references `EntityBehaviorHealth`'s DoT tick loop, add one sentence
directly after that reference: *"DoT ticks run on real (engine) elapsed time, not the game
calendar — `EntityBehaviorHealth`'s tick loop is not scaled by `Calendar.SpeedOfTime`/
`CalendarSpeedMul`, confirmed against its source; any poison-duration math in this doc that assumes
DoT ticks scale with calendar speed does not hold and should be expressed in real seconds for the
tick interval specifically."* Read the surrounding paragraph first and phrase this so it reads as
current fact, not a changelog entry, per this repo's own documentation conventions.

- [ ] **Step 5: Add a note that no sleep-completion hook exists**

Find where the doc discusses effect timers needing to account for sleep (search for "sleep" near
any effect-persistence discussion — likely near the `TimeStarted`/`TimeLeft` persistence section).
Add a note: *"There is no engine event fired when sleep ends or by how many hours it advanced the
calendar — confirmed against `ModSleeping.cs` (a continuous time-speed-modifier ramp, not a
discrete jump) and `EntityBehaviorTiredness.cs` (which detects elapsed sleep purely by polling
`Calendar.TotalHours` before and after its own tick, the same approach this mod must use)."* If no
existing section discusses this, add it to the `TimeStarted`/`TimeLeft` persistence section, since
that's the mechanism actually affected by sleep jumps.

- [ ] **Step 6: Add a note on reading the real-time/game-time ratio live, not hardcoded**

In the same section as Step 5 (or immediately after), add: *"The real-time-to-game-time ratio
(default 48 real minutes per game day) is server-configurable at runtime via `CalendarSpeedMul`
and `SpeedOfTime` — any code converting between real time and game time must read
`Calendar.SpeedOfTime * Calendar.CalendarSpeedMul` live rather than hardcoding the default ratio,
matching how vanilla's own `EntityBehaviorHealth.ApplyRegenAndHunger` does this conversion."*

- [ ] **Step 7: Mark the Temporal Stability precedent as needing its own verification if relied on**

Find where the doc cites `SystemTemporalStability`/`TemporalStabilityEffects` as a pattern to
mirror (Part 1 area, "Temporal Stability" reference near the top of the doc, or wherever Part 3/4's
visual-effect sections cite it). Add: *"Only the client/server sync channel and a per-position
stability read have been verified against source so far — the specific tiered-threshold structure
this doc wants to mirror has not been confirmed and needs its own read of
`TemporalStabilityEffects.cs` (past its sync-channel setup) and `SystemTemporalStability.cs`
before any implementation plan treats its exact thresholds as precedent."*

- [ ] **Step 8: Commit**

```bash
git add 02-design-overview.md
git commit -m "docs: correct six verified inaccuracies in the design overview"
```

---

### Self-review checklist (run before calling this plan done)

- [ ] All 3 bugs from `03-implementation-audit-and-plan.md`'s Part A actually fixed (re-read the
  changed lines, don't just trust the diff).
- [ ] `HandleGameWorldSaving()` deliberately **not** touched — that's a later plan's job.
- [ ] All 6 original doc corrections plus the Bowtorn distance correction (7 total) applied.
- [ ] No other design-doc content changed beyond what's listed above.
- [ ] `git log` shows 5 commits, each scoped to exactly what its message says.
