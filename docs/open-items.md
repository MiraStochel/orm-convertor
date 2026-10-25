# Otevřené položky

Jediný seznam toho, co zbývá. **Rozhodnutí** končí novým souborem v [`decisions/`](./decisions/README.md) a řádkem v jeho rejstříku, **práce** (už rozhodnutá) kódem a aktualizací [`architecture.md`](./architecture.md), podle potřeby i [`subset.md`](./subset.md) a [`traceability.md`](./traceability.md). Hotová položka odsud mizí; co verze nárokuje, říká §9 a kanonicky sekce *Guarantees* kořenového [`README.md`](../README.md).

**Pořadí nesou jen značky** (rozh. [018](./decisions/018-work-order-as-item-marker.md)) na začátku kurzívového řádku vazeb: **Na řadě** nejvýš jedna položka, **Potom** nejvýš dvě, ostatní se berou podle priorit z požadavků. **Vada** — vstup v nároku, z něhož vznikne artefakt, který cíl odmítne nebo který tiše znamená něco jiného — se opravuje přednostně jako práce a nárok, který vyvrací, do opravy platí jen s výhradou; položka vady začíná slovem *Vada* v kurzívovém řádku vazeb.

## Přehled

Rejstřík, ne pořadí. Sloupec *Nutné pro* čte [specifikaci](./specifikace.tex): `projekt` — bez položky projekt nejde dokončit; `experimenty` — potřebují ji experimenty projektu (T1–T3) nebo příprava navazujících (T4–T6); prázdný — nepotřebuje ji ani jedno, tedy ani Advisor, který přijde až po experimentech, zbude-li čas (rozh. [171](./decisions/171-the-advisor-comes-after-the-experiments-if-time-remains.md)).

| Položka | Oblast | Druh | Nutné pro | Pož. |
|---|---|---|---|---|
| [Verdikt soudce po přepisu dotazů na cesty](#verdikt-soudce-nad-celou-sadou-sf-1-po-přepisu-dotazů-na-cesty) | Dotazy | práce | projekt | T2, T3, F13 |
| [Nárok T2 a T3 po prvním záznamu](#nárok-t2-a-t3-po-prvním-záznamu-experimentálního-běhu) | Experimenty | rozhodnutí | experimenty | T2, T3 |
| [První záznam běhu mezí](#první-záznam-běhu-mezí) | Experimenty | práce | | S3, T3–T5 |
| [Advisor nad všemi frameworky](#advisor-měří-jen-dapper-a-ef-core) | Advisor | práce | | F15, T7 |
| [Srovnání s variantami a heuristikou](#srovnání-advisoru-s-jednotlivými-frameworky-a-heuristikou-chybí) | Advisor | práce | | T7, F15 |
| [Izolace spouštění cizího kódu](#izolace-spouštění-cizího-kódu-advisorem) | Advisor | rozhodnutí | | S4, F15, T7 |
| [Javová větev Advisoru](#javová-větev-advisoru-podle-rozh-146-nestojí) | Advisor | práce | | F15, T7, S4 |
| [Buňka jako proces a cena prvního výsledku](#buňka-měření-jako-proces-a-cena-prvního-výsledku-podle-rozh-147-nestojí) | Advisor | práce | | F15, T7, S4, S6 |
| [Parametrizovaný dotaz](#parametrizovaný-dotaz-advisor-nezměří) | Advisor | rozhodnutí | | F15, T4, T7 |
| [Nedoplněný překlad](#advisor-měří-nedoplněný-překlad) | Advisor | rozhodnutí | | F15, T7 |
| [Jednotka s víc dotazy](#jednotka-s-víc-dotazy-vstupuje-do-advisoru-jako-jeden-dotaz) | Advisor | rozhodnutí | | F15, T7 |
| [ADO.NET provider v benchmarcích](#sjednocení-adonet-provideru-v-benchmarcích) | Advisor | rozhodnutí | | T7 |
| [Testy Advisoru a benchmarkingu](#advisor-a-benchmarking-nemají-testy-chování) | Advisor | práce | | T7, S6 |
| [Nedostupnost nativní knihovny](#advisor-hlásí-nedostupnost-nativní-knihovny-až-po-odeslání-běhu) | Advisor | práce | | F15, S7 |
| [Průběžný stav běhu Advisoru](#průběžný-stav-běhu-advisoru-po-buňkách) | Advisor | rozhodnutí | | S7, F15 |
| [Odevzdání do položek](#odevzdání-není-rozepsané-do-položek) | Odevzdání | práce | projekt | — |
| [Reprodukovatelnost sestavení](#vynucení-stylu-a-reprodukovatelnost-sestavení) | Užitečné | rozhodnutí | | S2, S5 |
| [Trvalý identifikátor vydání](#trvalý-identifikátor-vydání) | Užitečné | rozhodnutí | | — |

---

## Dotazy

Katalog LDBC (rozh. [110](./decisions/110-ldbc-snb-as-a-second-reference-domain.md)) je přeložený celý, katalog FIBEN (rozh. [149](./decisions/149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md)) od rozh. 161 a 162 také, se soudcem nad všemi jeho dotazy od rozh. [163](./decisions/163-the-fiben-judge-takes-the-eight-queries-of-decisions-161-and-162-from-the-dotnet-half-of-the-run-of-2026-10-22.md); stav obou vede [`architecture.md`](./architecture.md) §6.2, konstrukce a meze [`subset.md`](./subset.md).

#### Verdikt soudce nad celou sadou SF 1 po přepisu dotazů na cesty
*Na řadě. Práce podle rozh. [140](./decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) a [141](./decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md), v režimu serveru rozh. [164](./decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md) a [166](./decisions/166-the-judge-reads-a-segment-between-inserts-in-every-framework-at-once.md); samostatný běh obou sad, který spouští Míra jménem ([`ORMConvertor/README.md`](../ORMConvertor/README.md), *The LDBC judge*). Požadavky T2, T3, F13.*

IC 13 a IC 14 — ty z pěti dotazů na cesty, které soudí validační sada — jsou `AsSpecified` a soudce je posuzuje verdiktem, ne mírou; celou sadou SF 1 prošly jen jejich texty s mezí ([záznam](./runs/2026-10-12-ldbc-judge.md)), dnešní texty prefixem. Zbývá celá sada nad dnešními texty, .NET a javová sada po sobě, se záznamem do [`runs/`](./runs/README.md). Soudce běží v režimu soudce rozh. [164](./decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md) a [166](./decisions/166-the-judge-reads-a-segment-between-inserts-in-every-framework-at-once.md) (`--env-file judge.env`); podle prefixu se dá čekat zhruba den na sadu ([záznam režimu soudce](./runs/2026-10-22-ldbc-judge-mode.md)), a pozastavení Windows Update musí přesáhnout konec. Čtení IC 14 v EF Core trvají podle plánu, který server z parametrů přeloží, 1–2,5 s nebo desítky sekund (vnořené poddotazy jeho SQL), v ostatních frameworcích dvě; souběžné přehrání na takové čtení čeká. Neshoda IC 13 nebo IC 14 by byla chybou textu nebo překladu, ne vlastností sady; čtení IC 14 v EF Core nad mezí 600 s (rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md)) vlastností jeho SQL, kterou by řešilo nové rozhodnutí.

## Experimenty

Oblast je ze záruk vyňatá (§9, oblast 6). Experimentální běh, referenční databáze s případovou studií, její schémata v databázi případových studií a protokol srovnání s LLM jsou rozhodnuté a stojí (rozh. [173](./decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [174](./decisions/174-the-reference-databases-of-the-experiments-are-three-by-role-and-the-case-study-of-t1-is-the-keycloak-excerpt-judged-to-the-third-level.md), [175](./decisions/175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md), [176](./decisions/176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md); [`architecture.md`](./architecture.md) §6.2, *Experimentální běh*); první záznam běhu v zafixovaném prostředí stojí ([2026-10-24](./runs/2026-10-24-experiment.md)). Zbývá, co běh našel, a posouzení nároku.

#### Nárok T2 a T3 po prvním záznamu experimentálního běhu
*Potom. Rozhodnutí nad [záznamem z 2026-10-24](./runs/2026-10-24-experiment.md); mění §9 (oblast 6), sekci *Guarantees* kořenového [`README.md`](../README.md) a řádky T2 a T3 v [`traceability.md`](./traceability.md). Požadavky T2, T3.*

Záznam dává tabulku T2 × T3 nad maticí, katalogy, příklady a případovými studiemi v zafixovaném prostředí, a tím je splněná podmínka, za níž se mělo posoudit, zda T2 a T3 vystoupí z vyňaté oblasti 6 v užším rozsahu (tabulka bez měření času a bez experimentů T4–T6). Doporučujeme zatím ne: záznam nese buňky šesti dotazů FIBEN proti soudci z doby před rozh. 170 (soudce obnovil [běh 2026-10-24](./runs/2026-10-24-fiben.md)) a tři nálezy, které od té doby vyřešila rozh. [179](./decisions/179-eclipselink-carries-no-version-over-a-binary-column-so-the-store-generated-version-is-a-read-only-column-with-a-loss.md), [180](./decisions/180-a-full-outer-join-into-eclipselink-goes-out-in-native-sql.md) a [181](./decisions/181-an-alias-in-a-mybatis-select-with-a-result-type-states-the-column-of-the-property.md), ale žádný záznam je ještě nepřeměřil; nárok v užším rozsahu stojí za to vyslovit až nad záznamem, v němž je každá červená buňka odmítnutím podle rozhodnutí nebo vyslovenou mezí. Rozhodnutí pak řekne, co přesně se nárokuje, a §9, *Guarantees* a traceability to převezmou.

#### První záznam běhu mezí
*Práce podle rozh. [182](./decisions/182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md); běh spouští Míra jménem ([`ORMConvertor/README.md`](../ORMConvertor/README.md), *The limits run*), v měřicím prostředí rozh. [145](./decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), se záznamem v [`runs/`](./runs/README.md). Souvisí s rozh. [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md) a [`threat-model.md`](./threat-model.md), hrozba 2; srovnání s LLM nad body řad vede protokol rozh. [175](./decisions/175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md). Požadavky S3, T3, T4, T5.*

Řady třinácti os a běh, který je prochází do první meze, stojí (`Experiment/LimitSearchTest`, javová půlka `experiment/ExperimentRunTest` s druhem `series`); záznam ještě žádný. První běh na MIS3 v zafixovaném prostředí dá obě půlky a souhrn dvakrát — s fixturou jako katalogem a nasucho (`ORMCONVERTOR_SERIES_CATALOG`) —, a jeho záznam uvede po osách, kde která řada ve kterém směru skončila a čím. Co záznam najde, se pak třídí: mez, která je vadou (tichý neplatný artefakt, pád pod stropem 092, čas nad mezí S3 u vstupu, který by někdo napsal rukou), je položka nebo rozhodnutí; mez platformy nebo řád, který nikdo nenapíše, je věta textu práce o omezeních nástroje, a hrozba 2 v `threat-model.md` dostane změřená čísla místo dnešních dvou bodů.

## Advisor

Advisor není podmínkou odevzdání: rozšíření o další frameworky a srovnání podle T7 přijde na řadu až po experimentech, zbude-li do konce projektu čas (rozh. [171](./decisions/171-the-advisor-comes-after-the-experiments-if-time-remains.md)); specifikace ho vede jako sekundární, *podle časových možností*. Ze záruk je vyňatý vcelku (§9, oblast 1), popis je v [`architecture.md`](./architecture.md) §8. Značky pořadí položky oblasti nedostanou, dokud nejsou experimenty hotové.

#### Advisor měří jen Dapper a EF Core
*Práce. [Specifikace](./specifikace.tex) F15 a T7 („rozšířen o nové frameworky"), maketa obrazovky Advisoru se šesti kandidáty (kap. *Uživatelské rozhraní*). Javové frameworky nese [položka javové větve](#javová-větev-advisoru-podle-rozh-146-nestojí). Požadavky F15, T7.*

`SupportedFrameworks` drží Dapper a EF Core; NHibernate z `KnownFrameworks` vypadne bez záznamu i na výslovné vyžádání ([`architecture.md`](./architecture.md) §8). NHibernate je práce jen na .NET straně — harness nad vygenerovaným mapováním a HQL, který zůstává závazným tvarem (§9), s novou `ISession` na spuštění podle rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md).

#### Srovnání Advisoru s jednotlivými frameworky a heuristikou chybí
*Práce. [Specifikace](./specifikace.tex) T7 („porovnán s jednotlivými frameworky a alespoň jednou jednoduchou heuristikou"), kap. *Vyhodnocení* a tabulka variant na maketě obrazovky Advisoru (kap. *Uživatelské rozhraní*). Požadavky T7, F15.*

Obrazovka ukazuje po dotazech všechny změřené kandidáty s poměrem k nejrychlejšímu a součet přiřazených řádků (§6.3). Variant téhož workloadu, které specifikace žádá, není: pro každého kandidáta „vše v jednom frameworku", heuristika volící každému dotazu nejrychlejšího kandidáta a ILP. Všechny se spočítají z měření, která běh už má; otevřené je jen, zda součty variant váží vahami dotazů jako účel ILP (maketa je nevažuje) a jak varianta řekne, že porušila strop paměti či počtu frameworků.

#### Izolace spouštění cizího kódu Advisorem
*[`threat-model.md`](./threat-model.md), hrozba 1; hranici pro javovou větev vyslovilo rozh. [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md) a umístilo rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md), pro .NET větev rozh. [147](./decisions/147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md). Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a spouští: dnes ho `RoslynBenchmarkCompiler` zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho volá v procesu aplikace, s jejími právy a připojením, bez limitu CPU a paměti; čas drží jen mez spuštění dotazu (146). Hranice je rozhodnuta: podle 147 je každá buňka měření vlastním procesem v obou větvích (dětský proces runneru v .NET, dětský `java` služby `advisor_java`). Zbývá rozhodnout limity toho procesu — CPU, paměť, práva (S4), k nimž patří i prostředí, které zdědí: bez zásahu celé prostředí aplikace i s katalogovým připojovacím řetězcem, který měřený kód nepotřebuje (rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)) — a do té doby platí jen předpoklad důvěryhodné sítě.

#### Javová větev Advisoru podle rozh. 146 nestojí
*Práce podle rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md); hranici dalo rozh. [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), politiku rozh. [145](./decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), vzor klienta přes HTTP rozh. [078](./decisions/078-java-suite-as-a-client-of-a-running-instance.md). Požadavky F15, T7, S4.*

Hibernate, EclipseLink ani MyBatis nemá Advisor čím změřit. Rozh. 146 je měří touž smyčkou jako .NET větev ve službě `advisor_java` vedle aplikace: javový projekt vedle `JavaTests` (kompilace artefaktů `javax.tools.JavaCompiler`, továrny jako `HibernateBootstrap`, `EclipseLinkBootstrap` a `MyBatisBootstrap` javové sady, smyčka s hodnotami politiky z požadavku, `System.nanoTime()` a `ThreadMXBean#getCurrentThreadAllocatedBytes`, nová `EntityManager` či `SqlSession` na spuštění, `queryTimeout` z politiky v JDBC URL), stage obrazu ze stejného obrazu Maven a Temurin 25 jako `java-tests`, služba v compose s `cpuset` 2–3, v aplikaci klient `IBenchmarkExecutor` pro javové frameworky s adresou `Advisor:JavaRunnerUrl` (compose ji nastaví), `KnownFrameworks` a `SupportedFrameworks` o tři javové frameworky širší a kandidát bez služby vypadlý se záznamem. Odpověď služby má tvar `BenchmarkMeasurementDto`; obrazovka Advisoru pak ukáže šest kandidátů, jak ji kreslí maketa specifikace. Podle rozh. [147](./decisions/147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md) služba sama neměří: pro každou buňku spustí dětský proces `java` z téhož obrazu a classpath, v němž běží smyčka, spojení mu předá standardním vstupem (rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)) a vrací i jeho `SetupMilliseconds` a `FirstExecutionMilliseconds`.

#### Buňka měření jako proces a cena prvního výsledku podle rozh. 147 nestojí
*Práce podle rozh. [147](./decisions/147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md); smyčku a politiku dalo rozh. [145](./decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), mez času a javovou službu rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md). Požadavky F15, T7, S4, S6.*

Buňka měření běží v procesu aplikace a vrací jen ustálený stav; první spuštění, v němž se frameworky liší o řády, se zahazuje a nemá definovaný stav plánu. Rozh. 147 dělá z buňky proces a přidává pět veličin: v .NET větvi `RoslynBenchmarkCompiler` zapíše sestavení do souboru a `BenchmarkExecutor` spustí nový konzolový projekt `AdvisorBenchmarking.Runner` (do seznamu `.csproj` ve stage `dotnet-build` obrazu; připojovací řetězec mu předá standardním vstupem, ne na příkazové řádce, rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)), který provede `Setup` a smyčku rozh. 145 a vypíše JSON se `SetupMilliseconds`, `FirstExecutionMilliseconds` (první zahřívací spuštění, uchované) a dosavadními hodnotami; rodič proces po nejdelší přípustné době ukončí. `AdvisorRunCoordinator` před buňkou vyprázdní cache plánů měřené databáze (`ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE`), po ní přečte z `sys.dm_exec_query_stats`, `sys.dm_exec_query_plan` a `sys.dm_exec_plan_attributes` plány vzniklé od začátku buňky — `CompileMilliseconds` (součet `CompileTime`), `Plans`, `PhysicalReads` — a buňku s fyzickým čtením spustí ještě jednou celou; bez práv na pohledy vrátí serverové veličiny jako chybějící se záznamem. `BenchmarkMeasurement`, `BenchmarkMeasurementDto` a `advisor.js` nesou a ukazují nové veličiny vedle mediánu; ILP se nemění. Po implementaci `architecture.md` §8, tvar záznamu měření v [`runs/`](./runs/README.md#záznam) a hrozba 1 v `threat-model.md`.

#### Parametrizovaný dotaz Advisor nezměří
*Plyne z rozh. [083](./decisions/083-parameter-as-the-fifth-operand-shape.md). Požadavky F15, T4, T7.*

`EFCoreBenchmarkHarnessBuilder` hledá veřejnou metodu s právě jedním parametrem `DbContext`; metodu s parametry dotazu přeskočí a běh skončí „EF Core query method not found". Rozšířit hledání je snadné; rozhodnout je třeba, **čím se parametr naplní** — výchozí hodnotou typu (měří prázdný výsledek), hodnotou od volajícího (nové pole požadavku), nebo hodnotou z katalogu (váže měření na data) — a zda dotaz, jehož parametr naplnit nejde, vypadne se záznamem, nebo se běh odmítne.

#### Advisor měří nedoplněný překlad
*Rozh. [059](./decisions/059-advisor-response-carries-the-measured-translations.md) svou variantu 3 zamítlo jen pro teď; souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavky F15, T7.*

Překladová fáze `/advisor/run` volá `ConversionHandler.Convert` bez připojení, takže měří překlad bez doplnění z katalogu, kdežto `/convert` tentýž vstup doplní; odpověď to přiznává stavem měřených artefaktů. Rozhodnout, zda překladová fáze dostane tutéž `CachingCatalogReader` jako benchmarková — levné, ale mění kompilovaný harness, takže se musí přeměřit. K tomu patří agregace `CatalogReadTime` přes převody běhu, dnes u všech null.

#### Jednotka s víc dotazy vstupuje do Advisoru jako jeden dotaz
*Plyne z rozh. [108](./decisions/108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md) a [109](./decisions/109-a-code-unit-carries-every-query-it-hands-over.md). Požadavky F15, T7.*

`AdvisorRunCoordinator` měří každou jednotku workloadu jedním během a ILP dostane za identifikátor jednu cenu, jednotka ale může nést víc dotazů: `DapperBenchmarkHarnessBuilder` změří první `SqlQuery`, `EFCoreBenchmarkHarnessBuilder` první metodu, na kterou narazí reflexe, ostatní vypadnou beze slova. Rozhodnout: takovou jednotku odmítnout, nebo ji rozložit na víc dotazů workloadu (`Query01`, …) — to mění počet proměnných ILP, a tedy co je dotaz workloadu.

#### Sjednocení ADO.NET provideru v benchmarcích
*Podklad: audit [2026-08-02](./audits/2026-08-02-post-step-4-audit.md), kap. 3.4.2; [srovnání frameworků](./analysis/orm-frameworks-comparison.md) a `benchmarks/README.md`. Požadavek T7.*

Dapper, EF Core, linq2db a RepoDB běží v benchmarcích na `Microsoft.Data.SqlClient`, NHibernate, EF6 a PetaPoco na `System.Data.SqlClient` (přes `benchmarks/Common`) — metodologický confound. Rozhodnout: přepnout NHibernate na `MicrosoftDataSqlClientDriver`, najít pro PetaPoco provider nad `Microsoft.Data.SqlClient` a přeměřit, nebo confound popsat v textu práce.

#### Advisor a benchmarking nemají testy chování
*Práce; §8. Požadavky T7, S6.*

Testy oblasti drží jen to, že harness nenese připojení (rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md)) a že politika dává spuštění touž mez jako soudci (rozh. [146](./decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md)). Nepokrývají P/Invoke do ILP solveru, stavbu harnessů ani `HarnessGenerationUtilities`, které z generovaného textu tahá názvy typů, jmenné prostory a `[Table]` regulárními výrazy a nullabilitu přepisuje textovou náhradou — právě to se nejsnáz rozejde s generátorem. Proto je oblast ze záruk vyňatá vcelku; testy přijdou, až z vyňaté oblasti vystoupí.

#### Advisor hlásí nedostupnost nativní knihovny až po odeslání běhu
*Práce. Podle rozh. [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md) je Advisor kontejnerový a build pro Windows nevzniká. Požadavky F15, S7.*

Mimo Linux a Docker chybí `libadvisor.so`; `AdvisorRunHandler` vrátí text `DllNotFoundException` až po vyplnění a odeslání formuláře (úvod obrazovky na kontejner upozorňuje předem). Aby obrazovka nedostupnost řekla místo běhu, potřebuje koncový bod „je Advisor na tomhle hostiteli k dispozici?" — malý, ale ve vyňaté oblasti.

#### Průběžný stav běhu Advisoru po buňkách
*Rozhodnutí, až Advisor vystoupí z vyňaté oblasti 1; zrnitost buňky vyslovilo rozh. [160](./decisions/160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md), proces na buňku rozh. [147](./decisions/147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md). Požadavky S7, F15.*

Běh trvá minuty a skládá se z buněk (dotaz × kandidát); obrazovka ukazuje jen uplynulý čas a pevnou větu o čtyřech fázích (`advisor.js`). Dokud je oblast vyňatá, S7 průběh běhu nenárokuje. Je třeba zodpovědět, jak běh buňky hlásí: proudem řádků z jednoho požadavku (`/advisor/run` zůstává jedním voláním a klient čte tělo průběžně), nebo jako běh s vlastním identifikátorem a stavem, na který se obrazovka ptá.

## Odevzdání

#### Odevzdání není rozepsané do položek
*Práce. [Specifikace](./specifikace.tex), kap. *Milníky / Výstupy* a *Vyhodnocení*; konec projektu **2026-12-01** podle [záměru](./zamer.tex).*

Finální dokumentace, odevzdání a příprava obhajoby tu položky nemají; experimenty je mají v oblasti [Experimenty](#experimenty). Specifikace k odevzdání slibuje u obhajoby běžící instanci z `docker compose` s překladem reálných artefaktů mezi .NET a Javou, reprodukci testovací sady jedním příkazem a výsledky experimentů s naměřenými ukazateli kvality nástroje; výsledky mají být podkladem vědecké publikace. Je třeba to do seznamu rozepsat.

## Užitečné, ne nutné

Prospělo by to, ale žádná věta záruk na tom nestojí a nic tím není blokované; značky pořadí položky nedostávají.

#### Vynucení stylu a reprodukovatelnost sestavení
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.4. Souvisí s rozh. [034](./decisions/034-central-version-management.md) a [039](./decisions/039-container-configuration-of-the-environment.md). Požadavky S2, S5.*

„Reprodukovatelné prostředí" dnes znamená „jedním příkazem", ne „bajtově stejně": chybí zámek závislostí, základní obrazy i akce CI (`actions/checkout@v5` a spol.) visí na pohyblivých značkách a pravidla stylu build nevynucuje. Rozhodnout, zda se S2 rozšíří z výstupu překladu i na sestavení (zámek, digesty, styl v CI), nebo jde o údržbu, která počká.

#### Trvalý identifikátor vydání
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.1, a [fair-software.eu](https://fair-software.eu/). Souvisí s rozh. [098](./decisions/098-the-number-is-decided-once-per-release.md) a s položkou [Vynucení stylu a reprodukovatelnost sestavení](#vynucení-stylu-a-reprodukovatelnost-sestavení). Záznam v registru až úplně na konci vývoje.*

Citaci nesou jen značka v gitu a `CITATION.cff`; z pěti doporučení fair-software.eu chybí záznam v registru. Rozhodnout, zda se fork cizího prototypu archivuje pod vlastním identifikátorem (kde a s jakým autorstvím — `LICENSE` nese dva držitele). Vedle registru, nebo místo něj, lze vydání označit hašem SHA-256 celého balíčku: identifikátor odvozený z obsahu ověří kdokoli bez registru a bez otázky autorství a může ho nést už příští vydání, sám ale nikam nevede, takže dohledatelnost registru nenahradí. Vydání dnes balíček nemá (rozh. 098 vydává jen značku), takže je třeba určit, co se hašuje — archiv stromu značky je ze značky odvozený, vydávaný artefakt jako obraz aplikace by byl publikováním, při kterém se 098 nahrazuje — a zda má stejný haš vyjít i z nového sestavení, což předpokládá bajtově stejné sestavení z položky o reprodukovatelnosti.
