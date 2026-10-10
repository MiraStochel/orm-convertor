# Rozhodnutí

Jedno rozhodnutí = jeden soubor. Číslování je chronologické a stabilní, čísla se nepřepoužívají; **číslo 038 je vynechané**. Stav je převzatý z hlavičky souboru, sloupec *Požadavky* nese vazbu rozhodnutí → požadavek (opačný směr: [`traceability.md`](../traceability.md)).

| Č. | Rozhodnutí | Stav | Požadavky |
|---|---|---|---|
| 001 | [Odkaz na entitu ve vztahu jménem, ne referencí](001-entity-reference-by-name.md) | platí | F3, F10, F11 |
| 002 | [`IS NULL` jako porovnávací operátor](002-is-null-as-comparison-operator.md) | platí | F7–F10 |
| 003 | [Jednorázový přepis místo přechodného období](003-one-shot-migration.md) | platí | žádné |
| 004 | [Nevyjádřitelné fakty hlásit varováním, negenerovat náhražky](004-unexpressible-facts-as-warnings.md) | platí | F11 |
| 005 | [N:M jako explicitní junction entita](005-many-to-many-as-explicit-junction-entity.md) | platí | F3, F10, T1 |
| 006 | [Ploché vykreslení kompozitního klíče a identitní členy jako odpovědnost builderu](006-flat-composite-key-rendering.md) | platí | F1, F2, F7–F10, F11 |
| 007 | [Dokumentace organizovaná podle rozhodnutí, ne podle času](007-documentation-structure.md) | platí | žádné |
| 008 | [Databáze jako autoritativní doplněk chybějících mapovacích faktů](008-database-as-metadata-source.md) | nahrazeno [015](015-mapping-fact-completion-from-the-catalog.md) | F2, F4, F5, F6, F11, S1, S3 |
| 009 | [Deskriptor cílového frameworku místo vlastností rozptýlených v builderech](009-target-framework-descriptor.md) | revidováno | F2, F4, F7–F10, F11, S1 |
| 010 | [Diagnostika jako vrácená data, ne výjimka](010-diagnostics-as-returned-data.md) | revidováno | F5, F11, F14, T3, S6 |
| 011 | [Slovník strategií generování klíče](011-key-generation-strategy-vocabulary.md) | revidováno | F1, F2, F7–F10, F11, S2 |
| 012 | [Vykreslení cizího klíče v cílových frameworcích](012-foreign-key-rendering.md) | platí | F2, F3, F11, S2 |
| 013 | [Zafixované verze cílových frameworků](013-target-framework-versions.md) | platí | S2, S6, F7–F10 |
| 014 | [Jazykový typový model](014-language-type-model.md) | platí | F1, F2, F3, F7–F10, F11 |
| 015 | [Doplňování chybějících mapovacích faktů z databáze](015-mapping-fact-completion-from-the-catalog.md) | platí | F2, F4, F5, F6, F11, S1, S3 |
| 016 | [Stupně ověření generovaných artefaktů a zdroj testovací databáze](016-generated-artifact-verification-levels.md) | platí | F2, F3, F4, F6, F11, S2, S4, S5 |
| 017 | [Priorita zdrojů uvnitř vstupu](017-source-precedence-for-mapping-facts.md) | platí | F2, F4, F5, F6, F11, S1, S2 |
| 018 | [Pořadí práce jako značka u položky](018-work-order-as-item-marker.md) | platí | žádné |
| 019 | [Neutrální slovník databázových typů](019-neutral-database-type-vocabulary.md) | platí | F2, F5, F7–F10, F11, S2 |
| 020 | [Kanonický slovník parametrů generátoru](020-canonical-generator-parameter-vocabulary.md) | platí | F1, F2, F7–F10, F11, S1, S2 |
| 021 | [Výběr názvu generátoru ve výstupu](021-generator-name-selection.md) | platí | F2, F3, F7–F10, F11, S1, S2 |
| 022 | [Nativní syntaxe cílového frameworku v dotazových builderech](022-native-query-syntax-in-builders.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 023 | [Šablonová metoda dotazového builderu podle relačního pořadí](023-query-builder-template-method.md) | platí | F7–F10, F11, T2, S1, S2 |
| 024 | [Typovaný operand dotazové podmínky](024-typed-query-operand.md) | platí | F7–F10, F11, T2, T3, S2 |
| 025 | [Dotazový jazyk jako typ obsahu](025-query-language-as-content-type.md) | platí | F7–F10, F11, F14, S1, S2, S7 |
| 026 | [Kde bydlí sdílené čtení dotazů](026-home-of-shared-query-reading.md) | revidováno | F7–F10, S1, S2 |
| 027 | [Ověření generovaných dotazů](027-query-artifact-verification.md) | platí | F11, F13, T2, T3, S2 |
| 028 | [Název sestavení není náš, abychom ho vymýšleli](028-assembly-name-is-not-ours-to-invent.md) | platí | F2, F11, S2 |
| 029 | [Připojení do databáze je fakt konzumentského projektu](029-database-connection-is-the-consumer-projects-fact.md) | platí | F5, F11, S4 |
| 030 | [Rozsah verze 1.0](030-scope-of-version-1-0.md) | revidováno | F1–F15, S1–S7, T1–T7 |
| 031 | [Klíčová třída je deklarací částí klíče, ne entitou převodu](031-key-class-as-declaration-of-key-parts.md) | platí | F1, F2, F5, F7–F10, F11, F14, S1, S2 |
| 032 | [Frontend jako statické stránky bez buildu](032-frontend-as-static-pages-without-a-build.md) | platí | F11, F14, S5, S7 |
| 033 | [Podoba obrazovek statického frontendu](033-shape-of-the-static-frontend-screens.md) | nahrazeno [099](099-examples-are-content-not-a-choice.md) | F11, F14, S6, S7 |
| 034 | [Centrální správa verzí](034-central-version-management.md) | platí | S2, S6 |
| 035 | [Kolekce v NHibernate entitě deklarované rozhraním](035-nhibernate-collections-declared-by-interface.md) | platí | F3, F11, S2 |
| 036 | [Primární klíč pod pravidlem priority zdrojů](036-primary-key-under-source-precedence.md) | platí | F5, F11, F14, S2 |
| 037 | [Vazbu deklarace a emise vynucených členů drží test](037-enforced-member-binding-held-by-the-test.md) | revidováno | S1, S2 |
| 039 | [Kontejnerová konfigurace prostředí](039-container-configuration-of-the-environment.md) | platí | F4, F6, S2, S4, S5, S6 |
| 040 | [Hranice předávaného artefaktu vůči konzumentskému projektu](040-boundary-of-the-handed-over-artifact.md) | platí | F2, F5, F11, S1, S2 |
| 041 | [Verzování, vydání a posun zafixovaných verzí](041-versioning-and-release.md) | nahrazeno [069](069-major-marks-a-milestone-not-a-break.md) | S2, S4, S6 |
| 042 | [Naměřený výstup benchmarků mimo git](042-measured-benchmark-output-out-of-git.md) | platí | T1, T7, S5 |
| 043 | [REST kontrakt hlídaný testem přes HTTP](043-rest-contract-guarded-over-http.md) | platí | S2, S6, S7 |
| 044 | [Chybová odpověď jako `ProblemDetails`](044-error-response-as-problem-details.md) | platí | F11, S6, S7 |
| 045 | [Převod, ze kterého nic nevyšlo, to musí říct](045-a-conversion-that-produced-nothing-says-so.md) | platí | F11, F14, S6, S7 |
| 046 | [XML mapování píše zapisovač prvků, ne interpolace](046-xml-mapping-written-through-an-element-writer.md) | platí | F11, S1, S2 |
| 047 | [Typ obsahu dojde až do dotazového parseru](047-content-type-reaches-the-query-parser.md) | platí | F7–F10, F11, F14, S1, S2, S7 |
| 048 | [Mapovací fakt, pro který model nemá místo, je ztráta, ne slovník](048-a-fact-with-no-place-in-the-model-is-a-loss.md) | platí | F5, F11, S1 |
| 049 | [Jazyková fakta vlastnosti pod pravidlem priority zdrojů](049-language-facts-under-source-precedence.md) | platí | F5, F11, F14, S2 |
| 050 | [Jedno místo pro heuristiku jednotného a množného čísla](050-one-home-for-the-singular-plural-heuristic.md) | platí | F5, F11, S1, S2 |
| 051 | [Vzorec `LIKE` se do LINQ překládá, ne přenáší](051-like-pattern-translated-not-carried-over.md) | platí | F7–F10, F11, T2, T3, S2 |
| 052 | [Doslovný SQL typ dojde i do anotace EF Core](052-literal-sql-type-reaches-the-ef-core-annotation.md) | platí | F2, F5, F11, S2 |
| 053 | [Dotaz, který by vrátil jinou množinu řádků, se nevydá](053-a-query-that-would-return-other-rows-is-not-emitted.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 054 | [Jazyková nullabilita části klíče je hlášená ztráta](054-nullable-key-part-is-a-reported-loss.md) | platí | F1, F11, S2 |
| 055 | [Unikátní omezení je nesené mapovací fakt, ne vyňatá oblast](055-unique-constraint-as-a-carried-mapping-fact.md) | platí | F2, F4, F5, F11, S1, S2 |
| 056 | [Rozpracovaný vstup překladové obrazovky zůstává v prohlížeči](056-work-in-progress-input-stays-in-the-browser.md) | platí | F14, S7 |
| 057 | [Nasazovací pohled bydlí v provozní příručce, ne v architektuře](057-deployment-view-in-the-operating-manual.md) | nahrazeno [058](058-only-the-operational-half-of-the-deployment-view-moves.md) | žádné |
| 058 | [Do provozní příručky patří jen provozní polovina nasazovacího pohledu](058-only-the-operational-half-of-the-deployment-view-moves.md) | platí | žádné |
| 059 | [Odpověď Advisoru nese změřené překlady](059-advisor-response-carries-the-measured-translations.md) | platí | F15, S2, S7, T7 |
| 060 | [Stránkování jako nesená dotazová instrukce](060-pagination-as-a-query-instruction.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 061 | [Poddotaz jako operand podmínky](061-subquery-as-a-condition-operand.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 062 | [HQL se čte vlastním sestupným parserem v NHibernate wrapperu](062-hql-read-by-a-hand-written-parser.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 063 | [Vyslovená bezklíčovost je nesený fakt a rozpor s klíčem soudí sémantika zdroje](063-stated-keylessness-as-a-carried-fact.md) | platí | F5, F11, S2 |
| 064 | [Nepřítomnost generování je fakt katalogu](064-absence-of-generation-as-a-catalog-fact.md) | platí | F4, F6, F11 |
| 065 | [Hranicí pravidla o nevydaném dotazu je množina řádků](065-row-set-as-the-boundary-of-rule-053.md) | platí | F11, T2, T3, S2 |
| 066 | [Záznam se připisuje vstupní jednotce, ze které vzešel](066-records-attributed-to-the-input-unit.md) | platí | F11, F14, S1, S2, S6, S7 |
| 067 | [Odvozující konvence zdroje je tvrzení, absenční výchozí ne](067-a-derived-convention-is-a-statement-a-default-is-not.md) | platí | F2, F5, F6, F11, S1, S2 |
| 068 | [Dokumentovaná precedence zdrojového frameworku řadí čtení jeho artefaktů](068-source-framework-precedence-orders-the-reading.md) | platí | F2, F5, F7–F10, F11, S1, S2 |
| 069 | [MAJOR označuje milník zadání, ne rozbitou plochu](069-major-marks-a-milestone-not-a-break.md) | nahrazeno [098](098-the-number-is-decided-once-per-release.md) | S2, S6, F7–F10, F15, T7 |
| 070 | [Parser odmítá dotaz, jehož nepřečtená část by změnila množinu řádků](070-a-parser-refuses-what-would-change-the-row-set.md) | platí | F8, F11, T2, T3, S1, S2 |
| 071 | [Uzavřený seznam skalárů se rozšiřuje o pět hodnot s protějškem v obou ekosystémech](071-five-scalars-with-a-counterpart-in-both-ecosystems.md) | platí | F1, F6, F7–F10, F11, S2 |
| 072 | [Nepersistovaná vlastnost je nesený mapovací fakt, ne chybějící sloupec](072-a-transient-property-is-a-carried-mapping-fact.md) | platí | F5, F6, F7–F11, S1, S2 |
| 073 | [`DISTINCT` jako příznak (pod)dotazu vykreslovaný projekčním krokem](073-distinct-as-a-flag-of-the-query-scope.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 074 | [Výčet hodnot jako čtvrtý tvar operandu podmínky](074-a-list-of-values-as-the-fourth-operand-shape.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 075 | [Neznámý jazykový typ je po rozresolvování jmen hlášená neúplnost](075-unknown-language-type-is-a-reported-incompleteness.md) | platí | F10, F11, S1, S2, T3 |
| 076 | [Javová strana vstupuje jako wrappery v C#, JVM zůstává v kontejneru](076-java-wrappers-in-csharp-jvm-in-containers.md) | platí | F7–F10, F12, F13, F15, S1, S2, S4, S5, T7 |
| 077 | [Hibernate wrapper nad sdílenou JPA vrstvou](077-hibernate-wrapper-over-the-shared-jpa-layer.md) | platí | F1, F2, F3, F7, F9, F10, F11, F12, S1, S2 |
| 078 | [Javová sada je klientem běžící instance nástroje](078-java-suite-as-a-client-of-a-running-instance.md) | revidováno | F7, F9, F10, F12, F13, S2, S4, S5 |
| 079 | [Přesnost zlomků sekundy nese `secondPrecision`, ne `precision`](079-fractional-second-precision-as-second-precision.md) | platí | F7, F9, F10, F11, F12, S1, S2 |
| 080 | [EclipseLink jako druhý profil nad JPA vrstvou](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md) | revidováno | F9, F10, F12, S1, S2 |
| 081 | [Jednotka smí být mapováním i dotazem zároveň](081-a-unit-may-be-a-mapping-and-a-query-at-once.md) | platí | F8, F10, F11, F14, S1, S2, S7 |
| 082 | [Čtení i zápis T-SQL bydlí ve sdíleném projektu](082-t-sql-read-and-written-by-a-shared-project.md) | platí | F8, F10, F11, S1, S2 |
| 083 | [Parametr jako pátý tvar operandu podmínky](083-parameter-as-the-fifth-operand-shape.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 084 | [MyBatis wrapper nad sdíleným čtením T-SQL](084-mybatis-wrapper-over-the-shared-sql-reading.md) | revidováno | F6, F8, F10, F11, F12, S1, S2 |
| 085 | [Počet řádků je číslo, nebo parametr](085-a-row-count-is-a-number-or-a-parameter.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 086 | [Cílový databázový dialekt deklaruje deskriptor](086-target-database-dialect-declared-by-the-descriptor.md) | platí | F2, F5, F7–F10, F11, S1, S2, S6 |
| 087 | [Integrační test je běh proti databázi a sada si ho počítá sama](087-an-integration-test-is-a-run-against-the-database.md) | platí | F1, F2, F12, S2, S5, S6 |
| 088 | [Deklarovaný cizí dialekt zdroje se nečte](088-a-declared-foreign-source-dialect-is-not-read.md) | platí | F5, F6, F7–F10, F11, F14, S1, S2, S6 |
| 089 | [Diferenční ověření je čtvrtý stupeň nad dotazem](089-differential-verification-as-the-fourth-level-over-a-query.md) | revidováno | F12, F13, T2, T3, S2, S5 |
| 090 | [Cross-ecosystem překlad je celý převod přes vyslovenou hranici a matice si ho počítá sama](090-the-cross-ecosystem-matrix-counts-itself.md) | platí | F6, F10, S1, T2 |
| 091 | [Katalog doplní i deklarovaně cizí zdroj a řekne to](091-the-catalog-completes-a-foreign-source-and-says-so.md) | platí | F4, F5, F6, F11, S6 |
| 092 | [Strop hloubky zanoření vstupu, vynucený nad tokeny před sestupem](092-input-nesting-depth-capped-before-the-descent.md) | revidováno | F11, S4, S7 |
| 093 | [Neparsovatelný vstup je selhání jednotky, ne výjimka převodu](093-unreadable-input-is-a-unit-failure.md) | platí | F11, F14, S7 |
| 094 | [Totožnost entity uvnitř převodu je dvojice jmenný prostor a název](094-entity-identity-inside-a-conversion.md) | platí | F5, F11, F14, S1, S2 |
| 095 | [Datovaný záznam o běhu jmenuje commit; velikost sady tvrdí jediné místo](095-a-dated-run-record-names-its-commit.md) | platí | S2, S5, S6 |
| 096 | [Pravidlo článku se cituje tam, kde odůvodňuje volbu; druhá mapa nevzniká](096-a-rule-of-the-paper-is-cited-where-it-argues.md) | platí | žádné |
| 097 | [Vlastní typ výjimky bydlí tam, kde se vyhazuje; `Model` žádný nenese](097-an-exception-type-lives-where-it-is-thrown.md) | platí | S1 |
| 098 | [Číslo verze se rozhoduje jednou za vydání, ne u každé změny](098-the-number-is-decided-once-per-release.md) | platí | S2, S6 |
| 099 | [Příklady výkladové stránky jsou její obsah, ne volba; ostatní podoba obrazovek zůstává](099-examples-are-content-not-a-choice.md) | nahrazeno [100](100-interactive-comparison-as-a-frozen-mockup.md) | F7–F11, F14, S6, S7 |
| 100 | [Interaktivní srovnání je maketa nad zmrazeným během; pátý dokument frontendu](100-interactive-comparison-as-a-frozen-mockup.md) | platí | F11, F14, S7 |
| 101 | [Join po asociační cestě se odvozuje ze vztahu mezireprezentace; bez sloupců se odmítá jmenovitě](101-a-join-along-an-association-path-is-derived-from-the-relation.md) | platí | F7, F8, F9, F11, T1, T2 |
| 102 | [Agregační `DISTINCT`, únikový znak `LIKE` a parametr uvnitř výčtu hodnot se nesou v mezireprezentaci](102-aggregate-distinct-like-escape-and-a-parameter-among-listed-values-are-carried.md) | platí | F7–F10, F11, T2, T3, S1, S2 |
| 103 | [Dotazový výraz LINQ se čte jako svůj přepis na řetěz a koncová metoda na jeden řádek jako výřez](103-a-query-expression-is-read-as-its-rewrite-and-a-single-row-terminal-as-a-slice.md) | platí | F7–F10, F11, T1, T2 |
| 104 | [Projekce do SQL cíle se materializuje jako netypovaný řádek](104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md) | platí | F7–F10, F11, F13, T2, T3 |
| 105 | [Dotaz formuluje vlastní poptávku do katalogu: vazbu tabulky, ze které se typuje parametr](105-a-query-formulates-its-own-demand-on-the-catalog.md) | platí | F5, F6, F7–F10, F11, F13, T2, S1, S2, S3 |
| 106 | [Holý parametr za `IN` je kolekční parametr Dapperu a wrapper ho odloupne před gramatikou](106-a-bare-parameter-after-in-is-dappers-collection-parameter.md) | platí | F10, F11, F13, T2, S1, S2 |
| 107 | [Výraz je šestý tvar operandu a stojí všude, kde stojí operand: uzavřený slovník funkcí, projekce i řazení nad operandem](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md) | platí | F7–F10, F11, T1, T2, T3, S1, S2 |
| 108 | [Holá jednotka SQL nese dotaz za každý `SELECT` a nepojmenované dotazy čísluje pořadím](108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md) | platí | F8, F11, F14, S1, S2, S7 |
| 109 | [Dotazová jednotka v kódu nese každý dotaz, který předává frameworku, a dotaz složený přes proměnnou čte celý](109-a-code-unit-carries-every-query-it-hands-over.md) | platí | F7, F8, F9, F10, F11, F14, S1, S2 |
| 110 | [LDBC Social Network Benchmark jako druhá referenční doména: data v kontejnerové databázi a katalog jeho dotazů se stavem, který drží testy](110-ldbc-snb-as-a-second-reference-domain.md) | platí | T1, T2, T3, F4, F6, F11, S5, S7 |
| 111 | [Jednotka je celý zdrojový soubor: vstup deklaruje jen jazyk, role tříd rozdělí zdrojový framework a jméno zůstává nepovinným popiskem](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md) | platí | F11, F14, S1, S2, S6, S7, T1 |
| 112 | [Dotaz jako zdroj řádků je pojmenovaný mezivýsledek dotazu: odvozená tabulka i `WITH` se čtou do téže definice](112-a-query-as-a-row-source-is-a-named-intermediate-result.md) | platí | F7–F11, F13, T1, T2, S1, S2 |
| 113 | [Co dotazový jazyk cíle nevysloví, napíše cíl celé nativním SQL svého dialektu; slovník nese rekurzi, seskupení podle výrazu, okenní funkce, agregaci do seznamu a funkce, které potřebuje katalog LDBC](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) | platí | F7–F11, F13, T1, T2, T3, S1, S2, S6 |
| 114 | [Člen jména je kořenem dotazu EF Core, jen když jednotka neuvádí opak: navigace načteného řádku dotazem není a mapování převodu smí místo jen odmítnout](114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md) | platí | F7–F10, F11, F14, S1, S2 |
| 115 | [Explicitní načtení navigace EF Core je dotaz, který poskytovatel skládá: řádky navigace omezené cizím klíčem na klíč načtené entity, nesený jako parametr](115-explicit-loading-is-the-query-the-provider-composes.md) | platí | F7–F10, F11, F14, S1, S2 |
| 116 | [`[ConcurrencyCheck]` nad jedinou celočíselnou či datočasovou vlastností je sloupec verze, jehož hodnotu udržuje aplikace: mezireprezentace nese, kdo verzi zvyšuje, a cíl, který ji zvyšuje sám, převzetí ohlásí](116-concurrency-check-is-the-version-the-application-keeps.md) | platí | F5, F10, F11, S1, T3 |
| 117 | [Validační sada LDBC Interactive v1 je soudcem čtvrtého stupně nad katalogem LDBC: přehrává se v pořadí, vkládání nese loader, čtení generovaný artefakt a databáze se vrací kompenzací](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) | revidováno | F13, T2, T3, S2, S5 |
| 118 | [NHibernate vydává dotaz dvěma tvary: závazným HQL a vedle něj LINQ nad `session.Query<T>()`; druhý tvar píše sdílená vrstva zápisu LINQ, nese vlastní typ obsahu a jeho vynechání je záznam `Omitted`](118-nhibernate-writes-a-linq-form-beside-its-hql.md) | platí | F7–F10, F11, F13, T2, T3, S1, S2, S6, S7 |
| 119 | [Kvantifikované porovnání (`ALL`, `ANY`, `SOME`) je porovnání s kvantifikátorem nad poddotazem: `= ANY` je `IN`, `<> ALL` je `NOT IN`, LINQ ho píše jako `All()`/`Any()` nad projekcí a negaci převrací De Morganem](119-quantified-comparison-as-a-comparison-with-a-quantifier.md) | platí | F7–F10, F11, F13, T2, T3, S1, S2 |
| 120 | [Tělo poddotazového operandu smí být množinová operace: `IN (A UNION B)` a `EXISTS (A UNION B)` se nesou, cíl bez množinové operace jde únikovou cestou a skalární porovnání s takovým tělem do LINQ také](120-a-set-operation-as-the-body-of-a-subquery-operand.md) | platí | F7–F10, F11, F13, T2, T3, S1, S2 |

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

- **`Požadavky: žádné`** — rozhodnutí o způsobu práce, ne o nástroji (003, 007). Pole se nevynechává; prázdná vazba se vysloví.
- **`nahrazeno NNN`** — volba se změnila: vzniká nový soubor, původní zůstává čitelný i s tehdejší úvahou a nepřepisuje se.
- **`revidováno`** — volba platí, jen se doplnil případ, na který se nemyslelo: oprava na místě a záznam v *Historie*. Smí se jen, dokud podle rozhodnutí nevznikl kód; potom je namístě nahrazení.
