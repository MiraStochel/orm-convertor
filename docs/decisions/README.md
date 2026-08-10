# Rozhodnutí

Každé rozhodnutí je samostatný soubor. **Rozhodnutí se nepřepisují.** Pokud se názor změní, vznikne nové rozhodnutí a to původní dostane stav `nahrazeno NNN` — původní úvaha včetně toho, proč tehdy dávala smysl, zůstává čitelná.

Číslování je chronologické a stabilní; čísla se nepřepoužívají.

| # | Rozhodnutí | Datum | Stav | Požadavky |
|---|---|---|---|---|
| [001](001-entity-reference-by-name.md) | Odkaz na entitu ve vztahu jménem, ne referencí | 2026-07-16 | platí | F3, F10, F11 |
| [002](002-is-null-as-comparison-operator.md) | `IS NULL` jako porovnávací operátor | 2026-07-16 | platí | F7–F10 |
| [003](003-one-shot-migration.md) | Jednorázový přepis místo přechodného období | 2026-07-16 | platí | — |
| [004](004-unexpressible-facts-as-warnings.md) | Nevyjádřitelné fakty hlásit varováním | 2026-07-16 | platí | F11 |
| [005](005-many-to-many-as-explicit-junction-entity.md) | N:M jako explicitní junction entita | 2026-07-20 | platí | F3, F10, E1 |
| [006](006-flat-composite-key-rendering.md) | Ploché vykreslení kompozitního klíče | 2026-08-02 | platí | F1, F2, F7–F10, F11 |
| [007](007-documentation-structure.md) | Dokumentace organizovaná podle rozhodnutí, ne podle času | 2026-08-03 | platí | — |
| [008](008-database-as-metadata-source.md) | Databáze jako autoritativní doplněk chybějících mapovacích faktů | 2026-08-10 | platí | F4, F5, F6, F11, S1, S3 |

## Formát

```markdown
# NNN — Název

Datum: RRRR-MM-DD
Stav: platí | nahrazeno NNN | revidováno NNN | zavrženo
Požadavky: F3, F10
Podklad: analysis/…            (nepovinné)

## Kontext
## Zvažované varianty
## Rozhodnutí
## Důsledky
## Historie                     (jen u revidovaných)
```
