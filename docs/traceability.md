# Plnění požadavků a trasovatelnost

**Účel:** požadavek z [`requirements.md`](./requirements.md) → implementace → důkaz a úkol schváleného záměru ([`zamer.tex`](./zamer.tex)) → co ho plní. Opačný směr (rozhodnutí → požadavek) nese [`decisions/README.md`](./decisions/README.md), scénáře [`use-cases.md`](./use-cases.md).

Popis současného stavu (žánr jako [`architecture.md`](./architecture.md)). Když se tahle tabulka rozejde s nárokem v [§9], platí §9; nad §9 samotným platí sekce *Guarantees* kořenového [`README.md`](../README.md). Co který framework přečte, vydá, odmítne nebo napíše nativním SQL, vede [subset.md].

**Stav** říká, co tvrdíme: **nárokované** — spoleh v celém rozsahu požadavku; **nárokované v užším rozsahu** — méně, než čte zadání, se zúžením v §9; **vyňaté** — nic, ačkoli oblast může v repozitáři být a běžet.

Testy .NET jsou uvedené cestou ve `Tests/`, javové s předponou *javová sada* (`ORMConvertor/JavaTests/`); závazná vazba požadavek → test je tahle tabulka, ne komentáře v kódu.

---

## Funkční požadavky

**Dotazy u F7–F10 a F13 (a experimentálně T2) společně:** žádný dotazový parser nevynechá známou konstrukci beze slova, každá konstrukce slovníku je vymezená rozhodnutím a podmnožina stojí celá v [subset.md] — nárok v [§9]; doklad je matice kategorií v obou sadách na stupních 1–4 (rozpis u F13 a T2) a to, že část 2.13 katalogu nevede u čtení dotazů žádné tiché místo.

| Pož. | Stav | Implementace | Důkaz | Zúžení / poznámka |
|---|---|---|---|---|
| **F1** komplexní identifikátory v IR | nárokované | `Model` — `PrimaryKey`: pořadí, typy a sloupce částí, strategie generování (rozh. [011]) | `Combined/CompositeKeyTest`, `Combined/PrimaryKeyTest`; klíče o 2–4 částech v `Database/TestSchemaFixtureTest` | — |
| **F2** klíče ve všech frameworcích | nárokované | wrappery obou ekosystémů; ploché vykreslení (rozh. [006]), klíčová třída (rozh. [031]) | `NHibernate/NHibernateCompositeIdTest`; 3. stupeň `Verification/EmbeddedKeyClassVerificationTest`; javová sada: 4. stupeň ve třech cílech a třech směrech | — |
| **F3** cizí klíče na složené klíče | nárokované | `Relation` na `EntityMap`, N:M jako spojovací entita (rozh. [005]); klíč inverzní kolekce z vlastnící strany | 4. stupeň pro každý typ vztahu: 1:N `Verification/DapperTo{NHibernate,EFCore}PersistenceTest`, 1:1 `…/DapperOneToOnePersistenceTest`, M:N `…/DapperManyToManyPersistenceTest`; `Combined/InverseCollectionKeyTest` | — |
| **F4** metadata z katalogu | nárokované | `DatabaseCatalog` — `SqlServerCatalogReader`, jediné místo, které čte databázi (rozh. [015]) | `Catalog/SqlServerCatalogReaderTest` proti SQL Serveru (typy, délky, přesnost, nullabilita, identita, klíče, FK, unikátní omezení); `Catalog/CatalogUniqueConstraintTest` | Podíl ≥ 95 % se nevykazuje (níž). |
| **F5** sloučení více zdrojů | nárokované | fáze doplňování mezi parserem a builderem; priorita zdrojů (rozh. [017]); totožnost entity = jmenný prostor + název | `Combined/SourcePrecedenceTest` (13 scénářů vč. čtyř z kritéria), `Combined/EntityIdentityTest`, `Catalog/CatalogForeignSourceDialectTest` | `Conflict` dostanou dvě entity téhož prostého jména a zdroj s cizím dialektem, kterému katalog fakt doplnil (rozh. [091]). |
| **F6** úplné mapování z neúplného vstupu | nárokované | doplnění z katalogu nad vstupem Dapperu ([§5.2]), i u zdroje s cizím dialektem; dotaz si tabulku pro parametr poptá sám (rozh. [105]) | `Verification/DapperTo{EFCore,NHibernate}VerificationTest` (2.–3. stupeň) a `…PersistenceTest` (4.); `Catalog/CatalogCompletionTest`, `Catalog/QueryDemandCompletionTest`, `Catalog/CatalogInverseCollectionTest` | Bez katalogu se parametr Dapperu odmítá záznamem, který chybějící katalog jmenuje. |
| **F7** Hibernate | nárokované v užším rozsahu | `HibernateWrappers` nad `JakartaPersistence` a `JavaEntityParsing` (rozh. [077]): anotace, `orm.xml`, JPQL | 20 testů entit a 15 dotazů ve `Hibernate/`; „projekty se sestaví": javová sada `hibernate/GeneratedArtifactTest` (2.–4. stupeň, tři směry, rozh. [078]), `hibernate/HibernateClaimsTest`, `hibernate/ReferenceKeyClaimsTest` (sloupec cizího klíče bez vlastnosti, 4. stupeň) | Sám za sebe, ne javový ekosystém; dědičnost, komponenty a spojené tabulky vyňaté (oblast 2) — [§9]. |
| **F8** MyBatis | nárokované v užším rozsahu | `MyBatisWrappers` nad `JavaEntityParsing` a `TransactSql` (rozh. [084]): doménová třída, rozhraní a XML mapperu | `MyBatis/MyBatisMappingParserTest`, `MyBatis/MyBatisBuilderTest`, `MyBatis/MyBatisQueryTest`, `Combined/QueryMatrixTest` (11 směrů); javová sada `mybatis/GeneratedArtifactTest` (2.–4. stupeň), `mybatis/MyBatisClaimsTest` | Sám za sebe. Dynamický příkaz (`<if>`, `<choose>`, `<bind>`) je `Failure`; „dynamicky parametrizované" čteme jako parametr ([subset.md], část 3); kanonický `<foreach>` se překládá. |
| **F9** EclipseLink | nárokované v užším rozsahu | `EclipseLinkWrappers` nad `JakartaPersistence` (rozh. [080]): profil implementace, nationalizace doslovným typem | `EclipseLink/EclipseLinkEntity{Parser,Builder}Test`, `EclipseLink/EclipseLinkQueryTest`; `EclipseLink/HibernateToEclipseLinkTest` (7 překladů); javová sada `eclipselink/GeneratedArtifactTest`, `eclipselink/EclipseLinkClaimsTest`, `eclipselink/ReferenceKeyClaimsTest` (sloupec cizího klíče bez vlastnosti, 4. stupeň) | Sám za sebe. Líné načtení reference netvrdíme: `fetch = LAZY` na `@ManyToOne`/`@OneToOne` je bez weavingu eager, zdroj dostane `Loss`. |
| **F10** cross-ecosystem překlad | nárokované | pivot přes IR, ekosystémy se potkávají jen v `AbstractWrappers`; ekosystém vyslovuje deskriptor (rozh. [090]) | `Combined/CrossEcosystemMatrixTest` — nejméně 30 překladů přes hranici (celé vzorkové převody a dvojice diferenční matice) a end-to-end scénář ve všech čtyřech dvojicích ekosystémů; `Combined/CrossFrameworkInputs` (18 směrů) | Dapper → Hibernate/EclipseLink a MyBatis → NHibernate předají bez katalogu jen dotaz (zdroj nemá klíč; to je F6). |
| **F11** validace artefaktů | nárokované v užším rozsahu | kontrola úplnosti a strukturovaná diagnostika (rozh. [010], [§5.1]); stupně ověření (rozh. [016]); dotaz s jinými řádky se nevydá (rozh. [070]) | `Combined/DiagnosticsTest`, `Combined/QueryFaithfulnessTest`, `Combined/DeclaredSourceDialectTest` (rozh. [088]), `Combined/UnreadableXmlTest`, `Combined/NestingDepthCapTest`, `Combined/EntityBaseTypeTest`, složka `Verification/` | Syntaxe se ověřuje testy, ne za běhu ([§9]). Zábrana cizího dialektu chrání jen toho, kdo ho vysloví; doplněný fakt je sporný jen původem. Beze slova dál: atributy `<id>`, `<version>` a vztahů NHibernate, bázový typ mimo převod ([open-items.md]). |
| **F12** testovací sada pro Javu | nárokované | `ORMConvertor/JavaTests/` — Maven + JUnit mimo .sln; stupeň `java-tests`, služby `java_tests` a `test_app`, job `java-test` (rozh. [076], [078]); [§6.2] | javová sada: `SuiteSizeTest` hlídá obě meze (integrační = 4. stupeň, `@Tag("integration")`, rozh. [087]); `GeneratedArtifactTest` tří cílů nad touž databází; .NET `TargetFrameworkDescriptorTest` váže verze na `pom.xml` | Jedním příkazem `docker compose --profile test run --rm java_tests` (po `build`). Počty: [README]. |
| **F13** diferenční ověření | nárokované v užším rozsahu | `Tests/Differential/` a javová sada `differential/` nad `Tests/Database/Differential/matrix.txt` (rozh. [089]): 6 vlastních dotazů a 40 kategorií T2 z každého zdroje — 1105 dvojic, 73 v nativním SQL | `DifferentialMatrixTest`, `DifferentialVerificationTest` (odmítnutí a `Fallback` podle matice), `DifferentialMutationTest` (5 mutací artefaktu), `RendererConformanceTest` | Null je vždy holé `NULL` — zúžení proti „konfigurovatelná" ([§9]); přesnost čísel volí matice (`decimalScale`, `floatDigits`). Neseřazený dotaz se porovná jako množina. |
| **F14** celé třídy a dávky v UI | nárokované v užším rozsahu | vícejednotkový převod, výstup po souborech, `/archive`; jednotka je celý soubor s dotazem za každé předání (rozh. [111]); záznam ukazuje na jednotku | `Combined/TranslationPerformanceTest` (200 jednotek), `Api/ConvertEndpointTest` (pole `unit`), `Combined/WholeSourceFileTest`, `Combined/CodeQueryUnitTest` | Na jednotku ukazují jen záznamy ze čtení. Zobrazení IR, vstupní archiv a procházení repozitáře se nenárokují ([§9]). Kód, který dotaz nepředává, se čte jako entita; navigace entity z jiného souboru je bez její jednotky dotazem, s ní odmítnutím. |
| **F15** výběr cíle a optimalizace | vyňaté | `Advisor` (ILP přes GLPK) a `AdvisorBenchmarking` běží a jsou v rozhraní | žádný test | Oblast 1 ([§9 hranice]). |

## Systémové požadavky

| Pož. | Stav | Implementace | Důkaz | Zúžení / poznámka |
|---|---|---|---|---|
| **S1** modulární rozšiřitelnost | nárokované | wrapper na framework, rozhraní v `AbstractWrappers`, deskriptor (rozh. [009]); sdílené vrstvy `LinqParsing`, `CSharpEntityParsing`, `JavaEntityParsing`, `JakartaPersistence`, `TransactSql` (rozh. [082]) | `Combined/TargetFrameworkDescriptorTest`, `Combined/EnforcedMembersTest`, `Combined/ApiContentContractTest` (každá hodnota `ORMEnum` v `/required-content`), `Combined/SharedSqlReadingTest` | Wrappery nereferencují `DatabaseCatalog` ani jiný wrapper; čtvrtý framework změnil `AbstractWrappers` jen o zápis vnořeného klíče a jazyk artefaktu dotazové metody. |
| **S2** determinismus | nárokované | buildery bez závislosti na prostředí; zafixované verze (rozh. [013]) | `Combined/RunRecordTest` — bajtově shodné artefakty i záznamy ve 36 směrech i při prohozeném pořadí jednotek; `Api/ConvertEndpointTest` — po drátě týž artefakt | — |
| **S3** výkon překladu | nárokované | jednofázová pipeline bez kompilace | `Combined/TranslationPerformanceTest` — 100 entit a 100 dotazů EF Core → NHibernate proti 30 s; čtení katalogu zvlášť v `CatalogReadTime` | Dva stroje (~3× rozdíl výkonu), odstup ≥ 40× ([`README`](../ORMConvertor/README.md#translation-performance-s3)). |
| **S4** izolace a bezpečnost | první věta **vyňatá**, druhá **nárokovaná** | druhá věta konstrukcí: žádný builder nevypisuje připojení (rozh. [029]) | `Combined/ArtifactCarriesNoCredentialsTest` (36 směrů), `Combined/ConsumerProjectFactsTest` | Izolace s limity se nenárokuje: cizí kód spouští jen Advisor (oblast 1, [`threat-model.md`](./threat-model.md)). Překladová cesta má jediný limit, strop zanoření. |
| **S5** přenositelné prostředí | nárokované v užším rozsahu | compose se dvěma profily (rozh. [039]); `ORMConvertorAPI/Dockerfile` (`tests`, `java-tests`), `database.Dockerfile`; `java_tests` s instancí `test_app` (rozh. [078]) | `docker compose --profile test build` + `run --rm tests` / `java_tests` ([*Tests*](../ORMConvertor/README.md#tests)) na stroji jen s Dockerem; CI obě sady proti SQL Serveru, .NET s `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1` | Experimentální pipeline neexistuje ([§9]). |
| **S6** pozorovatelnost | nárokované | `ConversionResult` — identifikátor běhu, verze nástroje a frameworků (z deskriptorů), záznamy, stav katalogu, `TargetDatabaseDialect` (rozh. [086]), `DeclaredSourceDialect` (rozh. [088]); [§5.1] | `Combined/RunRecordTest`, `Api/ConvertEndpointTest`, `Api/OpenApiDocumentTest`, `Combined/TargetFrameworkDescriptorTest`, `Combined/DeclaredSourceDialectTest` | — |
| **S7** uživatelská přívětivost | nárokované v užším rozsahu | statické stránky bez buildu (rozh. [032]); pět kroků [`use-cases.md`](./use-cases.md), UC4; rozpracovaný vstup v prohlížeči | `Combined/ApiContentContractTest`, `Api/RestContractTest`, `Api/ArchiveEndpointTest` (ZIP výstupu); řádek a sloupec: `Combined/NestingDepthCapTest`, `Combined/UnreadableXmlTest` | Validace na klientovi je pomocník, ne brána; řádek a sloupec chybí u C# ([§9]). Test frontendu neexistuje. |

## Experimentální požadavky

Všechny jsou **vyňaté** (oblast 6, [§9 hranice]); *Implementace* říká, co z toho existuje, ne „splněno".

| Pož. | Stav | Implementace | Důkaz | Zúžení / poznámka |
|---|---|---|---|---|
| **T1** případová studie | vyňaté | nic; scénář [`use-cases.md`](./use-cases.md), UC5 | — | LDBC SNB (databáze `LdbcSnb`, rozh. [110]) splňuje číselná kritéria (17 tabulek, 41 čtecích dotazů), aplikace to ale není. |
| **T2** matice překladů | vyňaté | 40 kategorií v `Tests/Database/QueryShapes/categories.txt` z každého zdroje, který je vysloví, do šesti cílů; buňka: jazyk cíle / nativní SQL (`Fallback`, rozh. [113]) / odmítnuto | `Combined/QueryShapeMatrixTest` (1. stupeň, 2. nad SQL a .NET cíli); javová sada `shapes/QueryCategoryTest` (2.–3. stupeň); 4. stupeň viz F13; `Combined/LdbcCatalogTest` | Co zdroj nevysloví, buňkou není ([subset.md] 1.3–1.4). LDBC: 41 ze 41 přeloženo, 6 se zjednodušením; odmítá jen EclipseLink u BI 12. |
| **T3** metriky korektnosti | vyňaté | čtyřstupňový model ověření (rozh. [016]) dává, co se dá počítat | — | Čísla se nevykazují (níž). |
| **T4** LLM baseline | vyňaté | nic | — | — |
| **T5** RAG / agentní varianta | vyňaté | nic | — | — |
| **T6** ablace | vyňaté | nic | — | — |
| **T7** baseline optimalizace | vyňaté | ILP model v `Advisor` (GLPK přes P/Invoke) běží | žádný test | Heuristiky ani srovnání nejsou. |

---

## Úkoly záměru

Záměr ([`zamer.tex`](./zamer.tex)) jmenuje pět úkolů řešitele: 1–4 jsou splněné a nárokované se zúženími níž, 5 jen v ověřovací polovině. Případy bez úplného nebo jednoznačného překladu, které záměr slibuje zdokumentovat, vede [subset.md].

| Úkol záměru | Co ho plní | Zúžení a proč |
|---|---|---|
| **1** „rozšířit mezireprezentaci … o složitější mapovací konstrukce, zejména komplexní identifikátory" | F1–F3: `Model` (`PrimaryKey`, `Relation`), wrappery Dapper, EF Core, NHibernate (rozh. [005], [006], [031]) | Dědičnost, komponenty a spojené tabulky model nenese: oblast 2 ([§9 hranice]). |
| **2** „doplňování a slučování metadat … ze zdrojového kódu a databázového schématu" | F4–F6: `DatabaseCatalog`, fáze doplňování mezi parserem a builderem (rozh. [015], [017]) | Katalog čte jen SQL Server, cílem je jen SQL Server 2022; druhý dialekt je oblast 5 ([§9 hranice]). |
| **3** „podporu Java frameworků Hibernate, EclipseLink a MyBatis" | F7–F10, F12: tři javové wrappery nad `JavaEntityParsing`, `JakartaPersistence` a `TransactSql` (rozh. [076], [077], [080], [084]); javová sada | Každý framework sám za sebe, ne „javový ekosystém" ([§9]). MyBatis nepřekládá dynamický příkaz (`<if>`, `<choose>`, `<bind>`): rozh. [084] čte „dynamicky parametrizované dotazy" z F8 jako parametry, argument v [subset.md], části 3. EclipseLink netvrdí líné načtení reference (rozh. [080]). |
| **4** „rozšířit podporu překladu read-only dotazů" | překlad dotazů ve 36 směrech (sekce *Guarantees*, *Covered*), změřený na 40 kategoriích matice T2 a diferenčně podle F13 (rozh. [089], [113]) | Meze konstrukce po konstrukci: [subset.md], část 2; nativní SQL váže artefakt na SQL Server 2022. T2 jako experimentální požadavek (tabulka s metrikami T3) zůstává vyňatý (oblast 6). |
| **5** „testovací a běhové prostředí pro automatizované ověřování a experimenty" | jen ověřovací polovina: S5 — profil `test`, obě sady v kontejneru a v CI (rozh. [039], [089]) | Experimentální pipeline neexistuje (zúžení S5, [§9]); otevřené v [`open-items.md`, *Experimentální pipeline, kterou záměr žádá, neexistuje*](./open-items.md#experimentální-pipeline-kterou-záměr-žádá-neexistuje). |

**Cross-language použitelnost mezireprezentace** platí pro entity a mapování v rozsahu podmnožiny ([subset.md], 1.5 a 2.1–2.2; F10), pro dotazy na změřeném vzorku kategorií T2: každá z každého zdroje, který ji vysloví, na 1. a 4. stupni, nad SQL a .NET cíli na 2., nad javovými na 2. a 3. v javové sadě.

### Volba vlastního parseru Javy místo javového nástroje

Vědomá odchylka od věty záměru „vhodné nástroje pro Java frameworky budou vybrány na základě analýzy" (rozh. [076]):

- **Volba:** analýza proběhla ([`analysis/`](./analysis/README.md)) a v překladové cestě nezůstal žádný javový nástroj: `JavaEntityParsing` má vlastní lexer celé lexikální gramatiky Javy a parser podmnožiny, JPQL čte vlastní sestupný parser v `JakartaPersistence`. JVM běží jen v javové sadě; verdikt dává její běh v kontejneru a v CI.
- **Proč:** javová komponenta v cestě by porušila S1 a S2 a vnesla do překladu cizí runtime; ANTLR odpadl, protože jeho generátor je sám javový program.
- **Cena:** čte se jen podmnožina Javy — těla metod se přeskakují, z kódu se bere jen předání dotazu (`createQuery` a spol.) a volání na objektu dotazu. Javová strana nároku stojí na bězích javové sady v kontejneru a v CI.
- **Proč to záměr připouští:** žádá nástroje vybrat analýzou, ne nějaký použít. Javové nástroje (`javac`, Maven, tři frameworky) slouží tam, kam je analýza umístila: k ověření generovaného kódu.

---

## Kritéria ověření, která zatím nikdo nespočítal

- **F4** „správně získáno ≥ 95 % sledovaných metadat": očekávaná odpověď (skript schématu, rozh. [016]) existuje, podíl se ale nevykazuje.
- **T3** je celý o podílech; spočítají se až v textu práce.

Pokrytí kódu se měří (`dotnet test --collect:"XPlat Code Coverage"`, v CI každý běh); čísla nese [README].

## Jak se to udržuje

Povinný uzavírací krok: změna chování, která se dotkne toho, co požadavek tvrdí nebo čím se dokazuje, aktualizuje `architecture.md` i **řádek zde**. Test sem patří jen jako *důkaz*; všechny testy jsou v `Tests/`.

[§5.1]: ./architecture.md#51-diagnostika-převodu
[§5.2]: ./architecture.md#52-doplňování-mapovacích-faktů-z-katalogu
[§6.2]: ./architecture.md#62-ověření-generovaných-artefaktů
[§9]: ./architecture.md#co-verze-nárokuje
[§9 hranice]: ./architecture.md#hranice-záruk
[subset.md]: ./subset.md
[open-items.md]: ./open-items.md
[README]: ../ORMConvertor/README.md#how-large-the-suite-is-and-what-it-covers
[005]: ./decisions/005-many-to-many-as-explicit-junction-entity.md
[006]: ./decisions/006-flat-composite-key-rendering.md
[009]: ./decisions/009-target-framework-descriptor.md
[010]: ./decisions/010-diagnostics-as-returned-data.md
[011]: ./decisions/011-key-generation-strategy-vocabulary.md
[013]: ./decisions/013-target-framework-versions.md
[015]: ./decisions/015-mapping-fact-completion-from-the-catalog.md
[016]: ./decisions/016-generated-artifact-verification-levels.md
[017]: ./decisions/017-source-precedence-for-mapping-facts.md
[029]: ./decisions/029-database-connection-is-the-consumer-projects-fact.md
[031]: ./decisions/031-key-class-as-declaration-of-key-parts.md
[032]: ./decisions/032-frontend-as-static-pages-without-a-build.md
[039]: ./decisions/039-container-configuration-of-the-environment.md
[070]: ./decisions/070-a-parser-refuses-what-would-change-the-row-set.md
[076]: ./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md
[077]: ./decisions/077-hibernate-wrapper-over-the-shared-jpa-layer.md
[078]: ./decisions/078-java-suite-as-a-client-of-a-running-instance.md
[080]: ./decisions/080-eclipselink-as-the-second-profile-over-the-jpa-layer.md
[082]: ./decisions/082-t-sql-read-and-written-by-a-shared-project.md
[084]: ./decisions/084-mybatis-wrapper-over-the-shared-sql-reading.md
[086]: ./decisions/086-target-database-dialect-declared-by-the-descriptor.md
[087]: ./decisions/087-an-integration-test-is-a-run-against-the-database.md
[088]: ./decisions/088-a-declared-foreign-source-dialect-is-not-read.md
[089]: ./decisions/089-differential-verification-as-the-fourth-level-over-a-query.md
[090]: ./decisions/090-the-cross-ecosystem-matrix-counts-itself.md
[091]: ./decisions/091-the-catalog-completes-a-foreign-source-and-says-so.md
[105]: ./decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md
[110]: ./decisions/110-ldbc-snb-as-a-second-reference-domain.md
[111]: ./decisions/111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md
[113]: ./decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md
