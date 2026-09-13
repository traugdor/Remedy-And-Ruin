# Catalogued Flora: Flowers

Source: `assets/survival/blocktypes/plant/flower.json`, `flower-lupine.json`, `rafflesia.json`,
`croton.json`, `waterlily.json`; variants from `assets/survival/worldproperties/block/flower.json`;
display names from `assets/game/lang/en.json`. Game version 1.22.7.

| In-game flower | Block code | Real-world plant | Potential herbal use (web cross-referenced) |
|---|---|---|---|
| Catmint | `flower-catmint-*` | *Nepeta* spp. | Traditional sedative/antispasmodic tea for anxiety, insomnia, colic, cough |
| Cornflower | `flower-cornflower-*` | *Centaurea cyanus* | Folk eyewash/astringent for mild eye and skin inflammation |
| Forget me not | `flower-forgetmenot-*` | *Myosotis* spp. | Little documented medicinal history; mostly ornamental/symbolic |
| Edelweiss | `flower-edelweiss-*` | *Leontopodium nivale* | Alpine folk remedy for stomach/respiratory complaints; modern antioxidant skincare ingredient |
| Heather (+ Small heather variant) | `flower-heather-*` | *Calluna vulgaris* | Infusions for urinary/digestive complaints; poultices for skin and wound healing |
| Horsetail | `flower-horsetail-*` | *Equisetum* spp. | High-silica traditional remedy for skin, hair, nail, connective-tissue support |
| Orange mallow | `flower-orangemallow-*` | Likely *Sphaeralcea* (globe mallow) — low confidence, species not confirmed by game | Mallow-family plants traditionally used as a demulcent for sore throat/skin |
| Wild daisy | `flower-wilddaisy-*` | *Bellis perennis* | Long-standing "bone flower"/arnica-like remedy for bruises, wounds, coughs; some modern wound-healing study support |
| Dwarf furze (Western gorse) | `flower-westerngorse-*` | *Ulex gallii* | Flowers edible; no strong distinct medicinal tradition beyond general gorse foraging |
| Cow parsley | `flower-cowparsley-*` | *Anthriscus sylvestris* | Occasionally used for kidney/urinary stones, but rarely used medicinally — closely resembles poisonous hemlock (**toxic lookalike risk**) |
| Golden poppy | `flower-goldenpoppy-*` | Likely *Eschscholzia californica* (California poppy) — low confidence | Traditional mild, non-narcotic sedative/analgesic, including for children |
| Lily of the valley | `flower-lilyofthevalley-*` | *Convallaria majalis* | Historically used for cardiac conditions via cardiac glycosides — **all parts highly toxic**, unsupported by modern safety data |
| Woad | `flower-woad-*` | *Isatis tinctoria* | Ancient antiseptic/wound/fever remedy; root is in the European Pharmacopoeia today; classic blue dye plant |
| Redtop grass | `flower-redtopgrass-*` | *Agrostis* sp. | Ornamental/pasture grass — no herbal use found |
| Bluebell | `flower-bluebell-*` | Likely *Hyacinthoides* | **Toxic**, no established herbal remedy use |
| Ghost pipe (white/pink/red) | `flower-ghostpipewhite/pink/red-*` | *Monotropa uniflora* | Indigenous/Eclectic-physician tradition as nervine/sedative/antispasmodic; modern herbalist tincture use for pain and restlessness |
| Daffodil | `flower-daffodil-*` | *Narcissus poeticus* | **Toxic** (lycorine, galanthamine); not used as folk remedy |
| Mugwort | `flower-mugwort-*` | *Artemisia vulgaris* | Well-known herb for dream work/moxibustion, digestive bitter, traditional emmenagogue |
| Lupine (blue/orange/purple/red/white) | `flower-lupine-{color}-*` | *Lupinus* spp. | Some species used as processed food bean after debittering; many wild lupines contain toxic alkaloids — **caution** |
| Rafflesia (brown/red) | `flower-rafflesia-{brown/red}` | *Rafflesia* spp. | Southeast Asian folk postpartum tonic — purify womb, aid recovery after birth, boost vitality; no pharmacological studies confirm efficacy |
| Water lily | `block-waterlily` | *Nymphaea* spp. | Traditional mild sedative/astringent, used in folk traditions for sleep and reproductive complaints |
| Small/medium croton | `flower-croton-*` | *Codiaeum variegatum* (ornamental foliage plant) | Decorative croton, **not** the medicinal *Croton* genus (e.g. dragon's blood) — no herbal use found |

## Notes

- Several entries are flagged **toxic** (lily of the valley, daffodil, bluebell, cow parsley) —
  real-world folklore includes some of these historically, but treat as poison-risk information.
  These are strong candidates for the *poison* side of a remedy/poison crafting system rather
  than the remedy side.
- Lower-confidence species matches (orange mallow, golden poppy) are marked — the game doesn't
  specify a real-world binomial, so these are best-guess matches by name/appearance and should
  be re-verified before being used as the basis for specific in-game effects.
