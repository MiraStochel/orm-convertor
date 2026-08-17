# Rozhodnutí

Každé rozhodnutí je samostatný soubor. **Změna volby se nepřepisuje.** Vznikne nové rozhodnutí a to původní dostane stav `nahrazeno NNN` — původní úvaha včetně toho, proč tehdy dávala smysl, zůstává čitelná. Doplnění případu, na který se při psaní nemyslelo, je něco jiného: to se opraví na místě se stavem `revidováno`.

Číslování je chronologické a stabilní; čísla se nepřepoužívají.

| # | Rozhodnutí | Datum | Stav | Požadavky |
|---|---|---|---|---|
| [001](001-entity-reference-by-name.md) | Odkaz na entitu ve vztahu jménem, ne referencí | 2026-07-16 | platí | F3, F10, F11 |
| [002](002-is-null-as-comparison-operator.md) | `IS NULL` jako porovnávací operátor | 2026-07-16 | platí | F7–F10 |
| [003](003-one-shot-migration.md) | Jednorázový přepis místo přechodného období | 2026-07-16 | platí | žádné |
| [004](004-unexpressible-facts-as-warnings.md) | Nevyjádřitelné fakty hlásit varováním | 2026-07-16 | platí | F11 |
| [005](005-many-to-many-as-explicit-junction-entity.md) | N:M jako explicitní junction entita | 2026-07-20 | platí | F3, F10, T1 |
| [006](006-flat-composite-key-rendering.md) | Ploché vykreslení kompozitního klíče | 2026-08-02 | platí | F1, F2, F7–F10, F11 |
| [007](007-documentation-structure.md) | Dokumentace organizovaná podle rozhodnutí, ne podle času | 2026-08-03 | platí | žádné |
| [008](008-database-as-metadata-source.md) | Databáze jako autoritativní doplněk chybějících mapovacích faktů | 2026-08-11 | nahrazeno 015 | F2, F4, F5, F6, F11, S1, S3 |
| [009](009-target-framework-descriptor.md) | Deskriptor cílového frameworku místo vlastností rozptýlených v builderech | 2026-08-11 | platí | F2, F4, F7–F10, F11, S1 |
| [010](010-diagnostics-as-returned-data.md) | Diagnostika jako vrácená data, ne výjimka | 2026-08-12 | platí | F5, F11, F14, T3, S6 |
| [011](011-key-generation-strategy-vocabulary.md) | Slovník strategií generování klíče | 2026-08-13 | revidováno | F1, F2, F7–F10, F11, S2 |
| [012](012-foreign-key-rendering.md) | Vykreslení cizího klíče v cílových frameworcích | 2026-08-13 | platí | F2, F3, F11, S2 |
| [013](013-target-framework-versions.md) | Zafixované verze cílových frameworků | 2026-08-14 | platí | S2, S6, F7–F10 |
| [014](014-language-type-model.md) | Jazykový typový model | 2026-08-14 | platí | F1, F2, F3, F7–F10, F11 |
| [015](015-mapping-fact-completion-from-the-catalog.md) | Doplňování chybějících mapovacích faktů z databáze | 2026-08-15 | platí | F2, F4, F5, F6, F11, S1, S3 |
| [016](016-generated-artifact-verification-levels.md) | Stupně ověření generovaných artefaktů a zdroj testovací databáze | 2026-08-17 | platí | F2, F3, F4, F6, F11, S2, S4, S5 |

## Formát

```markdown
# NNN — Název

Datum: RRRR-MM-DD
Stav: platí | revidováno | nahrazeno NNN | zavrženo
Požadavky: F3, F10 | žádné
Podklad: analysis/…            (nepovinné)

## Kontext
## Zvažované varianty
## Rozhodnutí
## Důsledky
## Historie                     (jen u revidovaných; datum a co se doplnilo)
```

**`Požadavky: žádné`** patří rozhodnutím o způsobu práce, ne o nástroji — jako 003 nebo 007. Pole se nevynechává, aby z jeho nepřítomnosti nešlo usoudit na opomenutí; prázdná vazba se vysloví.

**Revize versus nahrazení.** `revidováno` znamená, že volba platí dál a jen se doplnil případ, na který se při psaní nemyslelo; text se opraví na místě a sekce Historie zaznamená co a kdy. `nahrazeno NNN` znamená, že se volba změnila — pak vzniká nový soubor a původní zůstává čitelný i s tím, proč tehdy dával smysl. Revidovat na místě je bezpečné jen dokud rozhodnutí není naimplementované; jakmile podle něj vznikne kód, potřebuje čtenář obě znění vedle sebe a namístě je nahrazení.