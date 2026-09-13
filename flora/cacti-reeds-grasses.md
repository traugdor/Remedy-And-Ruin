# Catalogued Flora: Cacti, Reeds/Sedges, Bamboo, and Grasses

Source: `assets/survival/blocktypes/plant/barrelcactus.json`, `saguarocactus.json`,
`silvertorchcactus.json`, `reedpapyrus.json`, `bamboo.json`, `tallgrass.json`, `hay.json`;
display names from `assets/game/lang/en.json`. Game version 1.22.7.

## Cacti

| In-game plant | Block code | Real-world plant | Potential herbal/practical use |
|---|---|---|---|
| Barrel cactus | `barrelcactus-normal` | *Ferocactus* spp. | Traditional water source in survival contexts; pulp historically eaten (candied as "acitrón"); spines used as tools/fishhooks by some indigenous groups |
| Saguaro cactus (segment/branchy/tip/flowering/fruiting) | `saguarocactus-*` | *Carnegiea gigantea* | Fruit traditionally harvested and eaten by the Tohono O'odham and other groups, including ceremonial use (saguaro wine); ribs used structurally |
| Silver torch cactus | `silvertorchcactus` | *Cleistocactus strausii* | Ornamental columnar cactus; unlike some related torch cacti (e.g. San Pedro, *Echinopsis pachanoi*), silver torch is not established as psychoactive/medicinal — mostly a decorative species in real life |

## Reeds and Sedges (`reedpapyrus.json` — code `tallplant`, land/water/ice habitat variants)

| In-game plant | Block code | Real-world plant | Potential herbal/practical use |
|---|---|---|---|
| Cattails | `tallplant-coopersreed-*` | *Typha* spp. | Extensively documented: edible rhizome/shoots/pollen; traditional Chinese medicine use of the pollen (Pu Huang) as a hemostatic/diuretic; Native American use of stem juice as an analgesic/skin antiseptic and fluff as wound-bandage material |
| Papyrus | `tallplant-papyrus-*` (land and standard variants) | *Cyperus papyrus* | Historically used for paper-making; rhizome reportedly used as food/fuel in antiquity |
| Tule | `tallplant-tule-*` | Likely *Schoenoplectus acutus* | Traditionally used by Native American groups for weaving (mats, baskets, boats) and the rhizome as a food source |
| Brown sedge | `tallplant-brownsedge-*` | Generic sedge — low confidence on exact species | No specific traditional medicinal use found in this pass |

## Bamboo

| In-game plant | Block code | Real-world plant | Potential herbal/practical use |
|---|---|---|---|
| Bamboo (brown/green, segmented) | `bamboo-*` | Various bamboo species | Shoots widely eaten (must be cooked — raw shoots contain cyanogenic compounds); used in traditional Chinese medicine for respiratory/anti-inflammatory purposes; major structural/building material historically and today |

## Grasses

| In-game plant | Block code | Real-world plant | Potential herbal/practical use |
|---|---|---|---|
| Tall grass (veryshort/short/mediumshort/medium/tall/verytall/eaten) | `tallgrass-*` | Generic grass, fertility-value-driven, no named species | Pure gameplay mechanic (hay/fiber source tied to soil fertility) — no real-world species implied, no herbal use |
| Hay (normal/aged) | `hay-*` | Dried generic grass | Building/feed material in-game; real-world hay has no direct herbal-remedy tradition beyond animal feed |

## Notes

- Cattails stand out as the strongest real-world "wound care / hemostatic" candidate in this
  group, with independent corroboration from both Traditional Chinese Medicine and multiple
  Native American traditions — a good fit alongside old man's beard lichen for an early
  wound-treatment remedy tier.
- Papyrus and tule are culturally/practically significant (paper, weaving, food) but weaker
  fits for a medicinal remedy effect specifically — better suited as crafting/utility items than
  as ingredients, if that distinction matters for the eventual system design.
- Tallgrass and hay are structural/gameplay-mechanic plants with no real-world species behind
  them — not natural remedy-system candidates, but could serve as filler/neutral ingredients if
  the brewing system wants "junk" options that dilute a recipe.
