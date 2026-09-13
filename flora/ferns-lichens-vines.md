# Catalogued Flora: Ferns, Lichens, and Vines

Source: `assets/survival/blocktypes/plant/fern.json`, `ferntree.json`, `tallfern.json`,
`lichen.json`, `hanginglichen.json`, `wildvine.json`, `wildvine-static.json`; display names
from `assets/game/lang/en.json`. Game version 1.22.7.

## Ferns

| In-game fern | Block code | Real-world plant | Potential herbal use |
|---|---|---|---|
| Cinnamon fern | `fern-cinnamonfern` | *Osmunda cinnamomea* | Traditionally used externally for rheumatism/joint pain, and for women's health complaints, chills, headache, colds |
| Deer fern | `fern-deerfern` | Likely *Blechnum spicant* | No specific traditional-use source found — flag as unresearched, not "no use" |
| Eagle fern | `fern-eaglefern` | Likely *Pteridium aquilinum* (bracken) — low confidence | No specific traditional-use source found in this pass; note real bracken fern is considered carcinogenic/toxic in quantity, so verify before using as a "safe" remedy ingredient |
| Hart's-tongue fern | `fern-hartstongue` | *Asplenium scolopendrium* | Historic (medieval herbalist) use for liver/spleen/digestive complaints, coughs, wounds, gout, fever — well documented in old herbals, less so in modern ones |
| Fern tree | `ferntree-normal-*` | Generic tree fern | No specific real-world species implied by the block; treat as decorative/no assigned use |
| Tall fern | `tallfern` | Generic | No named real-world species; decorative/no assigned use |

## Lichens

| In-game lichen | Block code | Real-world plant | Potential herbal use |
|---|---|---|---|
| Old man's beard | `hanginglichen-oldmans-*`, also `lichen-oldmansbeard-up` | *Usnea* spp. | Well-documented traditional antimicrobial (usnic acid) — used historically for urinary/respiratory infections, wound cleaning, and by multiple cultures (Hippocrates, Chinese, Native American traditions) independently |
| Lace lichen | `hanginglichen-lace-*` | Possibly *Ramalina menziesii* — low confidence | No specific traditional-use source found in this pass; visually/functionally similar to old man's beard in-game but a different real genus if this ID is right |

## Vines

| In-game vine | Block code | Real-world plant | Potential herbal use |
|---|---|---|---|
| Wild vine (+ tropical variant) | `wildvine-section/end-*`, `wildvine-tropical-section/end-*` | No specific real species implied by the game | Generic decorative/climbing vine — no real-world species to cross-reference; would need a design decision to assign one before giving it an effect |
| Wild vines (static) | `wildvinestatic-*` | Same as above | Static/non-growing variant of the same block |

## Notes

- Old man's beard is the standout here — a real, well-sourced antimicrobial with a strong
  traditional-medicine pedigree, and a strong flavor/lore fit for a "wound care" remedy branch.
- Several entries have no confirmed real-world species (deer fern, eagle fern, lace lichen,
  wild vine) — these are either under-researched in this pass or the game simply doesn't imply
  a specific real plant. Don't assign a medicinal effect to these without either more research
  or a deliberate design choice to invent a fictional property for them.
