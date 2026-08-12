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
| [008](008-database-as-metadata-source.md) | Databáze jako autoritativní doplněk chybějících mapovacích faktů | 2026-08-11 | revidováno | F2, F4, F5, F6, F11, S1, S3 |
| [009](009-target-framework-descriptor.md) | Deskriptor cílového frameworku místo vlastností rozptýlených v builderech | 2026-08-11 | platí | F2, F4, F7–F10, F11, S1 |
| [010](010-diagnostics-as-returned-data.md) | Diagnostika jako vrácená data, ne výjimka | 2026-08-12 | platí | F5, F11, F14, E3, S6 |

## Formát

```markdown
# NNN — Název

Datum: RRRR-MM-DD
Stav: platí | revidováno | nahrazeno NNN | zavrženo
Požadavky: F3, F10
Podklad: analysis/…            (nepovinné)

## Kontext
## Zvažované varianty
## Rozhodnutí
## Důsledky
## Historie                     (jen u revidovaných; datum a co se doplnilo)
```

**Revize versus nahrazení.** `revidováno` znamená, že volba platí dál a jen se doplnil případ, na který se při psaní nemyslelo; text se opraví na místě a sekce Historie zaznamená co a kdy. `nahrazeno NNN` znamená, že se volba změnila — pak vzniká nový soubor a původní zůstává čitelný i s tím, proč tehdy dával smysl. Revidovat na místě je bezpečné jen dokud rozhodnutí není naimplementované; jakmile podle něj vznikne kód, potřebuje čtenář obě znění vedle sebe a namístě je nahrazení.