# Handbook Draft — Remedy & Ruin

Working draft for this mod's in-game handbook pages, sorted into the same one-page-per-mechanic
layout the base game uses (checked against `13-temporalstability.json`, `23-alcohol.json`, and
`04-mealcooking.json` for structure/tone). Each section below is one handbook page. Written for
players, not for the design docs — no engine citations, no balance formulas, no `Part N §M`
references. When this gets authored for real, each page becomes a `pageCode`/`title`/`text` JSON
entry (`config/handbook/`) with the title and text bodies as lang keys using vanilla's VTML tags
(`<strong>`, `<i>`, `<br>`, `<a href="handbook://...">`, `<hk>key</hk>`) — this draft uses plain
Markdown formatting standing in for those tags.

Source: `02-design-overview.md`, the full design document this is simplified from.

---

## Page: Crafting Mechanic: Brewing Remedies and Poisons

*Steep, boil, and hope you brewed the right thing.*

Every remedy and poison in this mod starts the same way: a cookpot, a liter of water, and three
handfuls of the right plant. Pick ingredients that don't belong together and you won't get a
remedy at all — you'll get sludge, good for nothing.

**The Potion Base**

Place 1L of water and three stacks of six matching plants into a cookpot. The result is a Potion
Base — already usable in a pinch (eat it directly for a short-lived version of its effect), but
it spoils fast, so don't let it sit around.

**Diluting**

Seal a Potion Base with 10L of water in a barrel and give it about a week. What comes out is a
Diluted Potion — too weak to do much of anything on its own. It's an ingredient for the next
step, not a drink.

**Distilling**

Boil the Diluted Potion and catch the vapor with a condenser, the same way you'd distill spirits.
The result is a proper Potion. Distill it a second time and you get a Concentrated Potion —
stronger, but riskier to drink too much of.

**Dosing**

Fill a vial for a clean, single dose. Drinking straight from a bowl or jug works too, but you'll
down four doses in one gulp — convenient, and a good way to make yourself sicker than you meant
to.

---

## Page: Crafting Mechanic: The Remedies

*Nine ailments, eight cures.*

- **Antiseptic** — treats bandages and poultices to fight infection.
- **Antinausea** — settles a sick stomach and lets you eat again.
- **Sedative** — lets you sleep even when you're not tired enough to otherwise.
- **Antiviral** — eases the symptoms of a cold, flu, bronchitis, or pneumonia.
- **Analgesic** — dulls pain, headaches, and fevers.
- **Topical Ointment** — treated onto a bandage to soothe skin irritation.
- **Tonic** — drink it before tending to the sick to avoid catching what they have. Also restores
  some health on the spot, making it worth keeping on hand in a fight.
- **Mind Tonic** — clears confusion, brain fog, and headaches.

---

## Page: Game Mechanic: Wounds and Bleeding

*A scratch is nothing. A gash is a problem.*

A bad enough hit will start you bleeding. Small wounds stop on their own; a serious one won't,
and can kill you if you ignore it long enough. Bandage it — any bandage or poultice will stop the
bleeding, whatever it's made from.

Left untreated, a wound can also turn infected. Only bandages or poultices soaked in Antiseptic
actually clear an infection — plain ones stop the bleeding but do nothing for infection. An
infected wound saps your strength and slows your healing until it's treated, and if it's bad
enough, the limb itself starts to fail you the way a broken one would.

If you can't reach for a bandage — mid-fall, mid-swim, mid-fight — a Tonic can buy you time. It
heals the wound's damage but doesn't stop the bleeding itself, so you'll still need to patch
yourself up properly once you can.

---

## Page: Game Mechanic: Sickness

*Not every ailment comes from a blade.*

**Upset Stomach** can come from spoiled food, drinking too much of any potion, or catching something
from another sick player. Antinausea is the real
cure; left alone it usually passes, but pushing your luck with more food too soon can bring it
right back.

**Colds and worse** start with a simple Chest Cold from exposure to cold, wet weather, or from
another sick player. Left untreated, it can turn into Bronchitis or the Flu, and either one can
worsen into Pneumonia if you keep ignoring it. Antiviral eases the symptoms at every stage.

**Sickness spreads.** Being in the same room as a contagious player, especially standing close,
risks catching whatever they have. A Tonic beforehand helps you avoid it — and keeping a sick
patient isolated genuinely helps everyone else too.

**Liver Disease** creeps up on you from eating too much cooked bushmeat, or from drinking too
many potions — especially Concentrated ones. Left untreated and still indulging, it can turn into
something much worse.

---

## Page: Game Mechanic: Poisons

*Some things are better left uneaten.*

Certain mushrooms and flowers are dangerous rather than useful. Eat one raw, or cook it into a
meal, and nothing happens right away — but the poison is working, and it will catch up with you.
That delay is your only warning, so if you've eaten something you're not sure about, don't wait
too long to find out.

Brewed properly, the same dangerous plants become weapons — coat an arrow, or drink the poison
yourself if you dare. Each poison behaves differently:

- **Toxic Poison** — a slow, draining wound that never heals on its own.
- **Noxious Poison** — fever, sickness, and vomiting. Miserable, but survivable.
- **Cardiac Poison** — strikes the heart directly. Moving around while affected only makes it
  worse.
- **Neurotoxic Poison** — weakens the body, and enough of it can paralyze or kill.
- **Mind Poison** — confusion, hallucinations, and a ravenous hunger.

A poisoned arrow only holds its coating until you pick it back up, and dipping it again just
wastes the poison unless you're switching to a different one. Killing an animal outright with a
poison-laced wound tends to ruin the meat — a poisoned arrow can be a faster kill, or a wasted
one.

---

## Page: Crafting Mechanic: The Antidote

*Field Mushroom, Red Wine Cap, Bitter Bolete. Brew carefully.*

There's one cure that works against every poison, brewed from three specific mushrooms — no
substitutes. Like any other remedy, it has to be fully distilled before it does any good.

It takes two doses to work. The first one will make you vomit — an unpleasant but necessary step.
The second is what actually cures you, clearing out any poison in your system, even one that
hasn't caught up with you yet.

For a couple of hours afterward, go easy: stick to broths and juice, skip the alcohol and solid
food, and don't drink any other potions. Breaking that rule brings the vomiting right back, and
drinking a potion during that window risks losing its effect entirely. It's a real inconvenience
— but a small price for surviving something that would otherwise kill you.

---

## Page: Game Mechanic: Broken Bones

*Not every injury is a wound.*

A hard enough hit doesn't just hurt — it can strain or break the bone underneath. Your head,
chest, arms, and legs can each be hurt this way, and it gets worse the more you're hit in the
same place without treatment.

A strained limb aches and, if it's your head, leaves you with a pounding headache. Left alone, it
can worsen into a fracture — more painful, and if it's your skull, genuinely dangerous. A broken
arm slows your work and might make you drop whatever's in your off hand; a broken leg slows you
down and might stop you moving at all. The worst injuries make those problems permanent until
they're treated, and come with a health cost on top.

Analgesic dulls the pain from any of it. Mind Tonic clears the headache and confusion that comes
with a hurt skull. What armor you're wearing — and where — makes a real difference in whether a
hit turns into a lasting injury at all.

---

## Page: Crafting Mechanic: Growing Herbs

*You don't have to wait for the world to grow it back.*

Wild plants don't regrow once picked clean, so it's worth growing your own. Plant a suitable
herb, flower, or mushroom in a flowerpot or planter, and given time it'll grow back — harvest a
flowerpot for two, or a planter for four.

Mushrooms need it dark and enclosed to grow at all — a cellar works, a sunny greenhouse won't.
Other plants are far less picky, and actually grow faster in a greenhouse than anywhere else.
