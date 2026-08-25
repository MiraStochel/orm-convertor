# Rozhodnutí

Každé rozhodnutí je samostatný soubor. **Změna volby se nepřepisuje.** Vznikne nové rozhodnutí a to původní dostane stav `nahrazeno NNN` — původní úvaha včetně toho, proč tehdy dávala smysl, zůstává čitelná. Doplnění případu, na který se při psaní nemyslelo, je něco jiného: to se opraví na místě se stavem `revidováno`.

Číslování je chronologické a stabilní; čísla se nepřepoužívají. Číslo 038 je vynechané.

| # | Rozhodnutí | Datum | Stav | Požadavky |
|---|---|---|---|---|
| [001](001-entity-reference-by-name.md) | Odkaz na entitu ve vztahu jménem, ne referencí | 2026-07-16 | platí | F3, F10, F11 |
| [002](002-is-null-as-comparison-operator.md) | `IS NULL` jako porovnávací operátor | 2026-07-16 | platí | F7–F10 |
| [003](003-one-shot-migration.md) | Jednorázový přepis místo přechodného období | 2026-07-16 | platí | žádné |
| [004](004-unexpressible-facts-as-warnings.md) | Nevyjádřitelné fakty hlásit varováním, negenerovat náhražky | 2026-07-16 | platí | F11 |
| [005](005-many-to-many-as-explicit-junction-entity.md) | N:M jako explicitní junction entita | 2026-07-20 | platí | F3, F10, T1 |
| [006](006-flat-composite-key-rendering.md) | Ploché vykreslení kompozitního klíče a identitní členy jako odpovědnost builderu | 2026-08-02 | platí | F1, F2, F7–F10, F11 |
| [007](007-documentation-structure.md) | Dokumentace organizovaná podle rozhodnutí, ne podle času | 2026-08-03 | platí | žádné |
| [008](008-database-as-metadata-source.md) | Databáze jako autoritativní doplněk chybějících mapovacích faktů | 2026-08-11 | nahrazeno 015 | F2, F4, F5, F6, F11, S1, S3 |
| [009](009-target-framework-descriptor.md) | Deskriptor cílového frameworku místo vlastností rozptýlených v builderech | 2026-08-11 | revidováno | F2, F4, F7–F10, F11, S1 |
| [010](010-diagnostics-as-returned-data.md) | Diagnostika jako vrácená data, ne výjimka | 2026-08-12 | revidováno | F5, F11, F14, T3, S6 |
| [011](011-key-generation-strategy-vocabulary.md) | Slovník strategií generování klíče | 2026-08-13 | revidováno | F1, F2, F7–F10, F11, S2 |
| [012](012-foreign-key-rendering.md) | Vykreslení cizího klíče v cílových frameworcích | 2026-08-13 | platí | F2, F3, F11, S2 |
| [013](013-target-framework-versions.md) | Zafixované verze cílových frameworků | 2026-08-14 | platí | S2, S6, F7–F10 |
| [014](014-language-type-model.md) | Jazykový typový model | 2026-08-14 | platí | F1, F2, F3, F7–F10, F11 |
| [015](015-mapping-fact-completion-from-the-catalog.md) | Doplňování chybějících mapovacích faktů z databáze | 2026-08-15 | platí | F2, F4, F5, F6, F11, S1, S3 |
| [016](016-generated-artifact-verification-levels.md) | Stupně ověření generovaných artefaktů a zdroj testovací databáze | 2026-08-17 | platí | F2, F3, F4, F6, F11, S2, S4, S5 |
| [017](017-source-precedence-for-mapping-facts.md) | Priorita zdrojů uvnitř vstupu | 2026-08-18 | platí | F2, F4, F5, F6, F11, S1, S2 |
| [018](018-work-order-as-item-marker.md) | Pořadí práce jako značka u položky | 2026-08-18 | platí | žádné |
| [019](019-neutral-database-type-vocabulary.md) | Neutrální slovník databázových typů | 2026-08-18 | platí | F2, F5, F7–F10, F11, S2 |
| [020](020-canonical-generator-parameter-vocabulary.md) | Kanonický slovník parametrů generátoru | 2026-08-19 | platí | F1, F2, F7–F10, F11, S1, S2 |
| [021](021-generator-name-selection.md) | Výběr názvu generátoru ve výstupu | 2026-08-19 | platí | F2, F3, F7–F10, F11, S1, S2 |
| [022](022-native-query-syntax-in-builders.md) | Nativní syntaxe cílového frameworku v dotazových builderech | 2026-08-19 | platí | F7–F10, F11, T2, T3, S1, S2 |
| [023](023-query-builder-template-method.md) | Šablonová metoda dotazového builderu podle relačního pořadí | 2026-08-19 | platí | F7–F10, F11, T2, S1, S2 |
| [024](024-typed-query-operand.md) | Typovaný operand dotazové podmínky | 2026-08-19 | platí | F7–F10, F11, T2, T3, S2 |
| [025](025-query-language-as-content-type.md) | Dotazový jazyk jako typ obsahu | 2026-08-19 | platí | F7–F10, F11, F14, S1, S2, S7 |
| [026](026-home-of-shared-query-reading.md) | Kde bydlí sdílené čtení dotazů | 2026-08-19 | revidováno | F7–F10, S1, S2 |
| [027](027-query-artifact-verification.md) | Ověření generovaných dotazů | 2026-08-19 | platí | F11, F13, T2, T3, S2 |
| [028](028-assembly-name-is-not-ours-to-invent.md) | Název sestavení není náš, abychom ho vymýšleli | 2026-08-19 | platí | F2, F11, S2 |
| [029](029-database-connection-is-the-consumer-projects-fact.md) | Připojení do databáze je fakt konzumentského projektu | 2026-08-20 | platí | F5, F11, S4 |
| [030](030-scope-of-version-1-0.md) | Rozsah verze 1.0 | 2026-08-20 | revidováno | F1–F15, S1–S7, T1–T7 |
| [031](031-key-class-as-declaration-of-key-parts.md) | Klíčová třída je deklarací částí klíče, ne entitou převodu | 2026-08-20 | platí | F1, F2, F5, F7–F10, F11, F14, S1, S2 |
| [032](032-frontend-as-static-pages-without-a-build.md) | Frontend jako statické stránky bez buildu | 2026-08-20 | platí | F11, F14, S5, S7 |
| [033](033-shape-of-the-static-frontend-screens.md) | Podoba obrazovek statického frontendu | 2026-08-20 | platí | F11, F14, S6, S7 |
| [034](034-central-version-management.md) | Centrální správa verzí | 2026-08-20 | platí | S2, S6 |
| [035](035-nhibernate-collections-declared-by-interface.md) | Kolekce v NHibernate entitě deklarované rozhraním | 2026-08-21 | platí | F3, F11, S2 |
| [036](036-primary-key-under-source-precedence.md) | Primární klíč pod pravidlem priority zdrojů | 2026-08-21 | platí | F5, F11, F14, S2 |
| [037](037-enforced-member-binding-held-by-the-test.md) | Vazbu deklarace a emise vynucených členů drží test | 2026-08-21 | revidováno | S1, S2 |
| [039](039-container-configuration-of-the-environment.md) | Kontejnerová konfigurace prostředí | 2026-08-22 | platí | F4, F6, S2, S4, S5, S6 |
| [040](040-boundary-of-the-handed-over-artifact.md) | Hranice předávaného artefaktu vůči konzumentskému projektu | 2026-08-22 | platí | F2, F5, F11, S1, S2 |
| [041](041-versioning-and-release.md) | Verzování, vydání a posun zafixovaných verzí | 2026-08-22 | revidováno | S2, S4, S6 |
| [042](042-measured-benchmark-output-out-of-git.md) | Naměřený výstup benchmarků mimo git | 2026-08-23 | platí | T1, T7, S5 |
| [043](043-rest-contract-guarded-over-http.md) | REST kontrakt hlídaný testem přes HTTP | 2026-08-23 | platí | S2, S6, S7 |
| [044](044-error-response-as-problem-details.md) | Chybová odpověď jako `ProblemDetails` | 2026-08-23 | platí | F11, S6, S7 |
| [045](045-a-conversion-that-produced-nothing-says-so.md) | Převod, ze kterého nic nevyšlo, to musí říct | 2026-08-23 | platí | F11, F14, S6, S7 |
| [046](046-xml-mapping-written-through-an-element-writer.md) | XML mapování píše zapisovač prvků, ne interpolace | 2026-08-24 | platí | F11, S1, S2 |
| [047](047-content-type-reaches-the-query-parser.md) | Typ obsahu dojde až do dotazového parseru | 2026-08-24 | platí | F7–F10, F11, F14, S1, S2, S7 |
| [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md) | Mapovací fakt, pro který model nemá místo, je ztráta, ne slovník | 2026-08-24 | platí | F5, F11, S1 |
| [049](049-language-facts-under-source-precedence.md) | Jazyková fakta vlastnosti pod pravidlem priority zdrojů | 2026-08-24 | platí | F5, F11, F14, S2 |
| [050](050-one-home-for-the-singular-plural-heuristic.md) | Jedno místo pro heuristiku jednotného a množného čísla | 2026-08-24 | platí | F5, F11, S1, S2 |
| [051](051-like-pattern-translated-not-carried-over.md) | Vzorec `LIKE` se do LINQ překládá, ne přenáší | 2026-08-24 | platí | F7–F10, F11, T2, T3, S2 |
| [052](052-literal-sql-type-reaches-the-ef-core-annotation.md) | Doslovný SQL typ dojde i do anotace EF Core | 2026-08-24 | platí | F2, F5, F11, S2 |
| [053](053-a-query-that-would-return-other-rows-is-not-emitted.md) | Dotaz, který by vrátil jinou množinu řádků, se nevydá | 2026-08-24 | platí | F7–F10, F11, T2, T3, S1, S2 |
| [054](054-nullable-key-part-is-a-reported-loss.md) | Jazyková nullabilita části klíče je hlášená ztráta | 2026-08-24 | platí | F1, F11, S2 |
| [055](055-unique-constraint-as-a-carried-mapping-fact.md) | Unikátní omezení je nesené mapovací fakt, ne vyňatá oblast | 2026-08-24 | platí | F2, F4, F5, F11, S1, S2 |
| [056](056-work-in-progress-input-stays-in-the-browser.md) | Rozpracovaný vstup překladové obrazovky zůstává v prohlížeči | 2026-08-24 | platí | F14, S7 |
| [057](057-deployment-view-in-the-operating-manual.md) | Nasazovací pohled bydlí v provozní příručce, ne v architektuře | 2026-08-24 | nahrazeno [058](058-only-the-operational-half-of-the-deployment-view-moves.md) | žádné |
| [058](058-only-the-operational-half-of-the-deployment-view-moves.md) | Do provozní příručky patří jen provozní polovina nasazovacího pohledu | 2026-08-24 | platí | žádné |
| [059](059-advisor-response-carries-the-measured-translations.md) | Odpověď Advisoru nese změřené překlady | 2026-08-24 | platí | F15, S2, S7, T7 |
| [060](060-pagination-as-a-query-instruction.md) | Stránkování jako nesená dotazová instrukce | 2026-08-25 | platí | F7–F10, F11, T2, T3, S1, S2 |
| [061](061-subquery-as-a-condition-operand.md) | Poddotaz jako operand podmínky | 2026-08-25 | platí | F7–F10, F11, T2, T3, S1, S2 |
| [062](062-hql-read-by-a-hand-written-parser.md) | HQL se čte vlastním sestupným parserem v NHibernate wrapperu | 2026-08-25 | platí | F7–F10, F11, T2, T3, S1, S2 |

## Formát

```markdown
# NNN — Název

Datum: RRRR-MM-DD
Stav: platí | revidováno | nahrazeno NNN
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