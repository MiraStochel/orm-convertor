# Otevřené položky

Jediná odpověď na otázku „co zbývá". Popis současného chování je v [`architecture.md`](./architecture.md), hotová rozhodnutí v [`decisions/`](./decisions/README.md).

Každá položka je buď **rozhodnutí** — něco, co je potřeba nejdřív rozmyslet a zapsat do `decisions/` —, nebo **práce**, tedy něco už rozhodnutého, co zbývá naprogramovat nebo dopsat. Rozlišení je praktické: rozhodnutí se řeší v konverzaci a končí novým souborem v `decisions/`, práce končí kódem a aktualizací `architecture.md`.

Položka odsud zmizí, jakmile je hotová. Kdo ji odbavil a kdy, je v git historii; proč jsme se rozhodli takto, v příslušném rozhodnutí.

**Kde se pokračuje, říká značka na řádku vazeb položky** (rozhodnutí [018](./decisions/018-work-order-as-item-marker.md)): značku „Na řadě" nese nejvýš jedna položka, značku „Potom" nejvýš dvě. Samostatný seznam pořadí tenhle soubor nemá — rozcházel se s položkami pod ním.

**Položky jsou od 2026-08-24 seskupené do tří cílů v pořadí, ve kterém na ně dojde:** nejdřív dotáhnout překlad a dotazy tří .NET frameworků, potom tři javovské ORM (F7–F10, F12–F13), a teprve potom Advisor, benchmarking a experimenty (T1–T7). Práci na Advisoru, benchmarkingu a rozhraní jsme vědomě odložili stranou: jejich položky tu zůstávají zapsané, ale nepracuje se na nich a značky pořadí nedostávají. Značky se pohybují uvnitř prvního nedokončeného cíle.

**Cíl 1 je od 2026-08-26 hotový a vydaný jako `1.2.0`.** Jeho položky odsud podle pravidla výše zmizely i s celou sekcí; co vydání změnilo na tvaru výstupu, co přidalo a co pohnulo na hranici záruk, nese anotace té značky (`git tag -n99 1.2.0`), co nástroj nárokuje, [`architecture.md`](./architecture.md) §9. Čísla cílů drží historickou řadu a nepřidělují se znovu, aby odkazy na „cíl 1" v rozhodnutích [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [069](./decisions/069-major-marks-a-milestone-not-a-break.md) dál platily. **Pracuje se tedy na cíli 2** a vydání, které ho uzavře, je podle rozhodnutí 069 první **MAJOR**, tedy `2.0.0`; schopnost, která přibude uvnitř cíle dřív, se vydá jako MINOR.

---

## Cíl 2 — tři javovské ORM

Jádro rozšíření ze zadání: Hibernate (F7), MyBatis (F8) a EclipseLink (F9), cross-ecosystem překlad mezi .NET a Javou (F10) a k tomu testovací infrastruktura pro Javu s diferenčním ověřením výsledků dotazů (F12, F13) jako důkaz funkční ekvivalence. Slovníkové předpoklady jsou hotové: typový model je zneutralizovaný na jazykové (rozhodnutí 014) i databázové straně (rozhodnutí 019) a parametry generátoru se nesou kanonicky s výběrem názvu ve výstupu (rozhodnutí 020 a 021, obojí implementované). Každý velký blok si zaslouží vlastní rozhodnutí, než se do něj sáhne.

**Příprava na .NET straně před prvním javovým builderem (od 2026-09-15) je rozhodnutá celá a až na jednu práci hotová.** Pět položek sem přešlo z nálezů nad .NET frameworky, protože každá rozšiřuje model nebo slovník, který javové buildery zdědí, a rozšíření je levnější před šesti buildery než po nich; čtyři z nich odbavila rozhodnutí [071](./decisions/071-five-scalars-with-a-counterpart-in-both-ecosystems.md)–[074](./decisions/074-a-list-of-values-as-the-fourth-operand-shape.md) a záznam o neznámém jazykovém typu, který by jinak vyplaval až v kódu javového wrapperu, rozhodnutí [075](./decisions/075-unknown-language-type-is-a-reported-incompleteness.md). Modifikátor `virtual` zodpovědělo rozhodnutí [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md) jazykovou osou vynucených členů; zbývá ho odložit v kódu (položka práce níž). **Kudy javová strana do řešení vstupuje, říká totéž rozhodnutí:** jako tři wrappery v C# nad sdílenými projekty ekosystému, s JVM jedině v kontejneru a na runneru CI — javový testovací projekt je zároveň sadou podle F12 a ověřovacím harnessem 2. až 4. stupně pro javové cíle a javová větev Advisoru běží mimo proces aplikace. Schopnost, která přípravou přibyla, se podle rozhodnutí 069 vydá jako MINOR dřív, než cíl uzavře `2.0.0`.

Dvě dřívější otázky cíle 1 už otázky nejsou a u MyBatisu se jen aplikují. Pořadí čtení mezi artefakty frameworku dává rozhodnutí [068](./decisions/068-source-framework-precedence-orders-the-reading.md) — u JPA je dané specifikací (anotace první, `orm.xml` poslední, `xml-mapping-metadata-complete` anotace vypíná) a u MyBatisu ho [srovnání javových frameworků](./analysis/java-orm-frameworks-comparison.md) dohledalo: referenční dokumentace pravidlo pro souběh anotací a XML mapperu nevyslovuje, implementace 3.5.19 druhé shodné id odmítne výjimkou už při sestavení továrny, takže rozhodnutí k F8 zapíše `Failure` po vzoru rozhodnutí 063, ne `Conflict`. Kritérium čtení konvencí dává rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) — pojmenovací konvence mapperu se s přečtenou konfigurací materializuje, zbytek doplní katalog (F6).

### Rozhodnutí

#### Hibernate wrapper nad sdílenou JPA vrstvou
*Na řadě. Rozhodnutí k F7; kudy javová strana vstupuje, sdílenou JPA vrstvu, profil cílového frameworku a jazykovou osu vynucených členů vyslovilo rozhodnutí [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), takže tahle položka je až o Hibernate samotném. Předpokladem jsou rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md) a [031](./decisions/031-key-class-as-declaration-of-key-parts.md) (klíčová třída), [013](./decisions/013-target-framework-versions.md) (`hbm.xml` jako cíl mimo rozsah), [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [068](./decisions/068-source-framework-precedence-orders-the-reading.md) (materializace konvencí a precedence anotací před `orm.xml`) a [072](./decisions/072-a-transient-property-is-a-carried-mapping-fact.md). Podklad: [tutoriál k Hibernate](./analysis/tutorials/hibernate-getting-started.md) a [srovnání javových frameworků](./analysis/java-orm-frameworks-comparison.md), §3–§9 a §15. Požadavky F7, F10, F11, S1, S2.*

První javový wrapper vzniká podle rozhodnutí 076 nad projekty `JavaEntityParsing` a `JakartaPersistence`, které tím vznikají s ním; EclipseLink (F9) bude potom tenký wrapper nad touž vrstvou. Rozhodnout je třeba, co je Hibernate samotné a co vrstva: rozsah anotační podmnožiny, kterou parser čte a builder vypisuje (`@Entity`, `@Table`, `@Column`, `@Id`, `@IdClass`, `@GeneratedValue`, vztahy s `@JoinColumn(s)` a `@JoinTable`, `@Transient`, `@Version`, `@UniqueConstraint`), pravidla překladu `hbm.xml` → anotace, protože Hibernate 7 má XML mapování zavržené a překlad NHibernate → Hibernate je jediná cesta (rozhodnutí 013), syntéza klíčové třídy jako vynuceného členu u složeného klíče, přístup k hodnotě podle umístění `@Id` — na poli, nebo na getteru — a tedy tvar generované třídy, sémantika typu kolekce (`List` bez `@OrderColumn` je bag, `Set` množina), výchozí pojmenovací strategie a nationalizační režim profilu (holý Hibernate nepřevádí `bornOn` na `born_on`, Spring Boot ano; `string` je `varchar` bez `@Nationalized`), co z HQL nad rámec JPQL parser čte a co hlásí, a vzorky pro `/samples`. Testovací kritérium F7 — dvacet překladových testů entit, patnáct testů dotazů a „generované projekty se sestaví" — se dělí podle 076: tvar v xUnit, sestavení a přijetí v javové sadě, takže rozhodnutí má říct, co který test tvrdí.

#### Jednotka, která je mapování i dotaz zároveň
*Vstup rozhodnutí k F8; dotýká se orchestrace, takže podle invariantu S1 potřebuje rozhodnutí dřív, než se sáhne do kódu. Souvisí s rozhodnutími [025](./decisions/025-query-language-as-content-type.md), [047](./decisions/047-content-type-reaches-the-query-parser.md) a [066](./decisions/066-records-attributed-to-the-input-unit.md). Podklad: [srovnání javových frameworků](./analysis/java-orm-frameworks-comparison.md), §4 a §15; [`use-cases.md`](./use-cases.md), „Co je ve skutečnosti vstupem". Požadavky F8, F14, S1.*

Orchestrace dělí jednotky převodu podle typu obsahu na entitní a dotazové (`ConversionContentTypes.IsQuery`) a pouští je ve dvou průchodech: nejdřív entitní parsery nad mapovacími jednotkami, potom dotazové nad dotazovými ([`architecture.md`](./architecture.md), §5 a §7). Typ obsahu je tedy buď jedno, nebo druhé. XML mapper MyBatisu je ale obojí v jednom souboru — `<resultMap>` je mapování a `<select>` dotaz, a `<select>` na `resultMap` odkazuje nebo ho nese vnořený. Tentýž tvar má u JPA `@NamedQuery` na entitě; tam je to volitelná forma a rozhodnutí ji smí vynechat, u MyBatisu je to forma jediná. Rozhodnout je třeba, jestli přibude typ obsahu, který je obojí, a orchestrace ho pustí oběma průchody — to je změna orchestrace, tedy věc rozhodnutí, ne wrapperu —, nebo jestli má jednotku dělit klient, což jde proti F14 i proti tomu, že jednotka je obsah jednoho souboru. Do téhož rozhodnutí patří název hodnoty `XML` typu obsahu: dnes je popsaná jako mapování NHibernate, ač rozhodnutí 025 chce, aby hodnoty jmenovaly jazyk, ne framework, a s `orm.xml` obou implementací JPA a s mapperem MyBatisu ponese XML tři dialekty. Parser si typ obsahu nárokuje uvnitř zdrojového frameworku, takže hodnota může zůstat jedna, její popis ale ne.

#### Čtení SQL MyBatisu gramatikou T-SQL ze sdíleného projektu
*Vstup rozhodnutí k F8; nahrazuje dřívější větu položky o cílovém dialektu, která žádala vlastní parser SQL. Souvisí s rozhodnutími [013](./decisions/013-target-framework-versions.md), [026](./decisions/026-home-of-shared-query-reading.md) a [062](./decisions/062-hql-read-by-a-hand-written-parser.md). Podklad: [srovnání javových frameworků](./analysis/java-orm-frameworks-comparison.md), §10 a §15. Požadavky F8, F10, S1, S2.*

Položka o cílovém dialektu dosud tvrdila, že SQL MyBatisu, psané i pro jiné databázové systémy, přečte jedině vlastní parser SQL v javovém wrapperu. To jde proti dvěma rozhodnutím: 026 ručně psaný parser T-SQL výslovně zamítlo ve prospěch referenční gramatiky `TSql160Parser` a 062 obhájilo vlastní parser jen pro malý jazyk, který nástroj sám vydává a round-tripem drží. Oba ekosystémy jsou přitom zafixované na SQL Server 2022 (rozhodnutí 013), takže SQL MyBatisu je v tomto projektu T-SQL a jiný dialekt je mimo záruky stejně jako u Dapperu (vyňatá oblast 5). Cesta podle pravidla 026 je proto sdílený projekt čtení T-SQL po vzoru `LinqParsing` a `CSharpEntityParsing`, který zdědí Dapper i MyBatis wrapper; `DapperSqlQueryParser` se do něj přesune a v Dapper wrapperu zůstane jen to, čím se Dapper liší. Rozhodnutí má vyslovit tři věci: že čtení SQL je sdílené a jiný dialekt zůstává vyňatý; co se sdílí i na straně zápisu, protože visitor SQL v Dapper builderu bude MyBatis builder potřebovat týž; a že zástupné symboly `#{}`/`${}` a dynamické značky MyBatisu nejsou věc gramatiky T-SQL, ale vrstvy nad ní, kterou řeší operand parametru z rozhodnutí k F8 — gramatika je nepřečte, takže je parser musí nahradit dřív, než jí text předá. Přesun parseru do sdíleného projektu je pak práce, která smí javovému wrapperu předcházet.

### Práce

#### Javový testovací projekt, stupeň obrazu, služba compose a job CI
*Potom. Infrastruktura z rozhodnutí [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md); mechanismus, kterým se javová sada dostane k artefaktům, vysloví až rozhodnutí k F12, takže tahle položka staví jen kostru, která se sestaví a doběhne. Souvisí s rozhodnutím [039](./decisions/039-container-configuration-of-the-environment.md) a s pravidlem `run` bez `build` v [`ORMConvertor/README.md`](../ORMConvertor/README.md#tests). Požadavky F12, S5.*

Podle rozhodnutí 076 vzniká Maven projekt `ORMConvertor/JavaTests/` — uvnitř adresáře řešení kvůli triggeru CI, mimo `ORMConvertor.sln` —, stupeň `java-tests` v `ORMConvertorAPI/Dockerfile` postavený z obrazu `maven:3.9.11-eclipse-temurin-25-noble` se závislostmi staženými při stavbě obrazu, služba `java_tests` pod profilem `test` v `docker-compose.yml` seřazená za inicializaci databáze a druhý job workflow se service containerem SQL Serveru a `actions/setup-java`. Kostra nese zafixované závislosti z tabulky verzí (Hibernate 7.4.5.Final, EclipseLink 5.0.0, MyBatis 3.5.19, `mssql-jdbc` 13.4.0.jre11), kopii `Tests/Database/TestSchema.sql` a jeden test, který se připojí k databázi a schéma přečte — aby stupeň, služba i job dokazovaly, že celá cesta doběhne, dřív než bude co ověřovat. Maven wrapper nevzniká a `.gitattributes` se nemění. S implementací se tabulka zafixovaných verzí rozšíří o obraz stupně a o verzi JUnit a `ORMConvertor/README.md` popíše druhý příkaz profilu `test` a tutéž past se starým obrazem.

#### Vynucený člen zdroje se odkládá při čtení
*Potom. Práce z rozhodnutí [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), jazyková osa vynucených členů; před prvním javovým builderem. Souvisí s rozhodnutími [009](./decisions/009-target-framework-descriptor.md) a [037](./decisions/037-enforced-member-binding-held-by-the-test.md). Požadavky S1, S2.*

Entitní parser zdrojového frameworku zahodí modifikátor, který jeho vlastní deskriptor deklaruje jako vynucený člen — dnes je to jediný případ, `virtual` u NHibernate. Sdílený `CSharpEntityParser` k tomu dostane od potomka seznam odkládaných modifikátorů; Dapper a EF Core nepředají nic a jejich chování se nemění. `EFCoreNullabilityTest`, který ve výstupu EF Core ze zdroje NHibernate tvrdí `virtual`, se opraví na tvar bez něj; tvar výstupu se tím mění pro zdroj v NHibernate, což je podle rozhodnutí 069 PATCH. Vazbu mezi deklarací a odložením drží test po vzoru rozhodnutí 037: co deskriptor zdroje vynucuje, to v mezireprezentaci po přečtení není.

#### Cílový databázový dialekt v deskriptoru
*Sem odkázalo rozhodnutí [019](./decisions/019-neutral-database-type-vocabulary.md); deskriptor s cílovou verzí, na kterou se dialekt tvarem podobá, je hotový (rozhodnutí [013](./decisions/013-target-framework-versions.md)). Vyňatá oblast 5 hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavky F7–F10, S2.*

Cílový databázový dialekt je fakt o cíli převodu téhož tvaru jako verze frameworku v deskriptoru, a rozhodnutí 019 ho odmítlo řešit v typovém modelu. Bez jeho deklarace nelze emitovat `sql-type` odvozený z typové rodiny ani vybrat typ podle systému, protože konkrétní SQL typ z typu frameworku odvozuje právě dialekt — oba .NET buildery dnes propisují jen doslovný `SourceSqlType`, který nese zdroj (rozhodnutí [052](./decisions/052-literal-sql-type-reaches-the-ef-core-annotation.md) srovnalo EF Core s NHibernate), a odvozený název typu berou z pevné tabulky pro SQL Server. Dokud se dialekt nedeklaruje, je jediným dialektem SQL Server a nástroj nic víc netvrdí.

Zdrojová strana je jiná otázka než tahle položka a deklarace cílového dialektu ji nevyřeší: `DapperSqlQueryParser` čte T-SQL gramatikou `TSql160Parser` (rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md)), takže SQL napsané pro jiný databázový systém — u MyBatisu (F8) běžné — touhle cestou neprojde. Co s tím, říká položka „Čtení SQL MyBatisu gramatikou T-SQL ze sdíleného projektu" výš: v tomto projektu je i SQL MyBatisu T-SQL a jiný dialekt zůstává vyňatý; pole v deskriptoru to neřeší.

## Cíl 3 — Advisor, benchmarking a experimenty

Lepší Advisor a benchmarking a na nich stojící experimentální požadavky T1–T7; T7 navazuje na existující ILP Advisor. Advisor s benchmarkingem jsou ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) a celý cíl je vědomě odložený: dokud běží cíle 1 a 2, položky tady jen leží a značky pořadí nedostávají.

### Rozhodnutí

#### Izolace spouštění cizího kódu Advisorem
*Souvisí s [`threat-model.md`](./threat-model.md), hrozba 1, a s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), které hranici pro javovou větev už vyslovilo. První věta S4 je vyňatá ze záruk ([`architecture.md`](./architecture.md), §9, oblast 1) a předpokladem je, že Advisor z vyňaté oblasti vůbec vystoupí. Požadavky S4, F15, T7.*

`/advisor/run` je jediné místo, kde nástroj cizí kód kompiluje a **spouští**: `RoslynBenchmarkCompiler` ho zavede do kolektibilního `AssemblyLoadContext` a `BenchmarkExecutor` ho zavolá ve vlastním procesu aplikace, s jejími právy a s připojením do Advisor databáze. Kolektibilní kontext je úklid paměti, ne izolace — žádný limit CPU, paměti ani času, žádná hranice procesu. První věta S4 přesně tohle žádá a §9 ji poctivě nenárokuje, jenže přiznání mezery není její popis.

Rozhodnout je třeba, **kde ta hranice povede**, a volba není bezplatná ani technicky, ani metodologicky: samostatný proces s limity operačního systému a kontejner na běh měří jinak než dnešní běh v procesu — startovní režie, jiný JIT stav a jiná paměťová stopa vstupují do čísel, o která u Advisoru celou dobu jde (T7). Třetí cestou je zúžit vstup natolik, aby se nespouštělo nic libovolného, což ale mění, co Advisor umí. Pro javovou větev je hranice daná: rozhodnutí 076 drží JVM v kontejneru a javová větev Advisoru — implementace `IBenchmarkExecutor` pro javové frameworky, které F15 žádá měřit — běží mimo proces aplikace, buď jako `java` spuštěná v kontejneru aplikace, nebo jako samostatná služba runneru. Tím se otázka nezmenšuje, ale zostřuje: .NET měřený in-process a Java mimo proces se musí dát srovnat, takže volba hranice pro .NET větev a metodologie měření se rozhodují spolu. Dokud volba nepadne, drží tu oblast jediné: předpoklad nasazení v důvěryhodné síti.

#### Advisor měří nedoplněný překlad
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Sem odkázalo rozhodnutí [059](./decisions/059-advisor-response-carries-the-measured-translations.md), které svou variantu 3 zamítlo jen pro teď. Souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Požadavky F15, T7.*

Překladová fáze `/advisor/run` volá `ConversionHandler.Convert` bez připojovacího řetězce, takže benchmark kompiluje a měří překlad bez katalogového doplnění — kdežto `/convert` tentýž vstup doplní a uživatel by nasadil doplněnou verzi. Čísla Advisoru tedy platí o jiném kódu, než jaký si uživatel z nástroje odnese. Od rozhodnutí 059 je to aspoň vidět: odpověď nese měřené artefakty a jejich stav říká, že katalog nebyl použit. Rozhodnout je třeba, jestli má překladová fáze dostat tutéž cachovanou čtečku jako fáze benchmarková — technicky je to po zavedení `CachingCatalogReader` levné, jedna dávka na framework — a co to udělá s naměřenými čísly: doplněné entity nesou jiné atributy a vztahy, takže se mění kompilovaný harness, a změna metodologie měření se musí přeměřit, ne jen zapnout. K témuž rozhodnutí patří i agregace `CatalogReadTime` přes převody běhu, má-li se o katalogové ceně běhu Advisoru něco tvrdit: každý převod svou fázi měří (`architecture.md`, §5.2), ale dokud překladová fáze čtečku nedostane, je ten čas u všech převodů běhu null — součet by tvrdil nulu, která není měřením.

#### Iterační politika benchmarku je konstanta v kódu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9; popis v §8). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavek T7.*

`BenchmarkExecutor` měří každý pár (dotaz × framework) pevným postupem: dvě zahřívací iterace, pilotní běh a z něj odvozených 3–20 měřených iterací s cílem ~500 ms celkem. Konstanty jsou zapsané v kódu bez odůvodnění a bez možnosti je ovlivnit z rozhraní, přitom právě ony určují rozptyl a délku běhu, o které v T7 jde; nadbytečná náhledová invokace — celé jedno provedení dotazu jen kvůli ladicímu výpisu — už je zrušená. Rozhodnout je třeba, jestli jsou tyhle hodnoty součástí metodologie, kterou text práce vysloví a odůvodní, nebo parametrem požadavku, a čím se volba podloží; měnit je bez rozhodnutí znamená měnit význam všech dosavadních čísel.

#### Sjednocení ADO.NET provideru v benchmarcích
*Souvisí s T-požadavky. Podklad: audit 2026-08-02, kap. 3.4.2.*

Dapper, EF Core, linq2db a RepoDB běží na `Microsoft.Data.SqlClient`, NHibernate, EF6 a PetaPoco na `System.Data.SqlClient`, který k nim teče přes `benchmarks/Common`. Pro srovnání výkonu je to metodologický confound. **Rozsah je nově dohledaný celý** — u PetaPoco vyloučením, protože `Microsoft.Data.SqlClient` v grafu balíků obou jeho projektů není, u EF6 z `WWIDbConfiguration`; podrobnosti nese [srovnání frameworků](./analysis/orm-frameworks-comparison.md) a `benchmarks/README.md`. Zbývá tedy volba, ne zjišťování: buď přepnout NHibernate na `MicrosoftDataSqlClientDriver`, najít pro PetaPoco provider nad `Microsoft.Data.SqlClient` (samostatný balík, dnes nereferencovaný) a přeměřit, nebo confound explicitně popsat v textu práce. Benchmarking stojí mimo záruky vcelku ([`architecture.md`](./architecture.md), §9), takže srovnávat jeho konfiguraci nemá dnes proti čemu.

### Práce

#### Advisor a benchmarking nemají žádné testy
*Souvisí s [`architecture.md`](./architecture.md), §8. Požadavky T7, S6.*

Testovací projekt nepokrývá `Advisor` ani `AdvisorBenchmarking`. Netestovaný je tedy P/Invoke do ILP solveru, obě stavby benchmarkových harnessů i `HarnessGenerationUtilities`, které si názvy typů, jmenné prostory a atribut `[Table]` tahá z generovaného textu regulárními výrazy a nullabilitu hodnotových typů přepisuje textovou náhradou. Právě tahle část se nejsnáz rozejde s generátorem, protože stojí na jeho výstupním tvaru — a jednou už se rozešla: extrakce SQL z generované metody přestala být potřeba, teprve když builder začal vydávat holý dotaz zvlášť.

Obojí je ze záruk vyňaté vcelku ([`architecture.md`](./architecture.md), §9) právě proto, že netestované je; testovat oblast, na kterou nástroj neslibuje spoleh, by znamenalo otevírat novou část místo dokončení rozdělané.

#### Hláška o neřešitelném ILP modelu dorazí do logu až s dalším voláním
*Nalezeno při ověření (2026-08-24); popis v [`architecture.md`](./architecture.md), §8, je podle toho opravený. Vyňatá oblast 1 hranice záruk (§9). Souvisí s položkou „Advisor a benchmarking nemají žádné testy". Požadavky T7, S6.*

`solve_problem()` v `Advisor/ilp.c` vypisuje `No feasible solution found.` obyčejným `printf`. Standardní výstup je v kontejneru přesměrovaný na rouru, tedy plně bufferovaný, a nikdo ten buffer nevyprazdňuje. Hláška se do logu **dostane**, ale teprve až ji protlačí výstup dalšího volání: tři neřešitelné úlohy za sebou vydaly dvě hlášky, každou o jeden běh opožděnou. Vlastní výpis GLPK dorazí včas, protože nejde přes `stdio`, takže v logu stojí `PROBLEM HAS NO PRIMAL FEASIBLE SOLUTION` bez naší věty vedle sebe.

Oprava je jednořádková — `fflush(stdout)` za tím výpisem, případně řádkové bufferování při inicializaci knihovny —, zadarmo ale není: `libadvisor.so` se překládá jedině v Docker buildu, takže změnu je nutné přeložit a ověřit v kontejneru, a sahá se přitom do oblasti bez jediného testu, kterou vyjímáme ze záruk vcelku. Dokud se to nestane, drží ten stav §8 svým popisem, aby nikdo nehledal hlášku, která po jeho volání v logu ještě není. Návratový kód ani tělo odpovědi to nijak nemění — neřešitelnou úlohu pozná volající z **400**, respektive ze `status: -1`, přesně jak §8 popisuje a jak jsme ověřili.

#### Advisor hlásí nedostupnost nativní knihovny až po odeslání běhu
*Vyňatá oblast 1 hranice záruk ([`architecture.md`](./architecture.md), §9). Souvisí s rozhodnutím [069](./decisions/069-major-marks-a-milestone-not-a-break.md) — nový koncový bod je nová schopnost, tedy vydání MINOR — a s rozhodnutím [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), podle kterého je Advisor kontejnerový rozhodnutím a build nativní knihovny pro Windows nevzniká. Požadavky F15, S7.*

Mimo Linux a Docker chybí `libadvisor.so` a `AdvisorRunHandler` výjimku z P/Invoke zachytí a vrátí její text, takže uživatel se o nedostupnosti dozví jako o `DllNotFoundException` — po vyplnění celého formuláře a po odeslání běhu. Úvodní odstavec obrazovky přitom říká dopředu, že Advisor potřebuje kontejner; nedostupnost se tedy sděluje dvakrát, jednou naší větou předem a jednou hláškou zavaděče potom.

Aby to obrazovka mohla říct **místo** běhu a vlastními slovy, potřebuje se serveru zeptat, jestli je Advisor na tomhle hostiteli k dispozici — dnes na to není koncový bod a klient si to odvodit nemůže. Je to tedy nový koncový bod, tedy nová schopnost podle rozhodnutí 069; sám o sobě je malý, ale předchází mu volba, jestli do vyňaté oblasti sahat dřív, než se dodělá rozdělané.

## Stranou cílů — rozhraní

Zásahy do rozhraní jsme odložili stranou všech tří cílů. Z F14 je hotový vícesouborový vstup a výstup po souborech; zbytek bloku F14–F15 — dávkové vstupy a zobrazení mezireprezentace — je tady a optimalizační půlka F15 patří Advisoru (cíl 3).

### Rozhodnutí

#### Směr překladu jako jedna věc a vstup vedle výstupu
*Navazuje na rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md), které pětikrokový tvar obrazovky zvolilo a je podle něj napsaný kód; případnou změnu je proto třeba nahradit, ne revidovat. Souvisí s [032](./decisions/032-frontend-as-static-pages-without-a-build.md) a s rozhodnutím [066](./decisions/066-records-attributed-to-the-input-unit.md). Požadavky F14, S7.*

Rozhodnutí 033 dalo překladové obrazovce pět očíslovaných sekcí viditelných najednou a první dvě z nich jsou volba zdroje a volba cíle. Důsledek je, že směr překladu na obrazovce nikde nestojí jako jedna věc: „EF Core → NHibernate" se poprvé objeví až v hlavičce výsledku a prohodit obě volby jde jen ručně, dvěma zásahy do dvou rozbalovacích seznamů. Jedna řádka směru s tlačítkem pro prohození by z pěti sekcí udělala čtyři, což S7 nebrání — „nejvýš pět kroků" je strop, ne kvóta —, ale je to změna volby, kterou 033 vyslovilo výslovně, takže patří do nového rozhodnutí.

Do téhož rozhodnutí patří druhá otázka, protože obě mění tvar téže obrazovky a navrhovat je zvlášť by znamenalo navrhnout ji dvakrát: **jestli má vstup stát vedle výstupu.** Dnes jsou vstupní jednotky nahoře jako textová pole a artefakty dole jako panely, takže se zdroj a výsledek nedají číst současně — na výkladové stránce vedle sebe stojí, na nástrojové ne. Podstatná je tu poctivost, ne rozvržení: server neříká, který artefakt vznikl ze které jednotky (§9, zúžení F14), takže sloupce vedle sebe se buď musí spárovat toutéž jmennou heuristikou, jakou se artefakty pojmenovávají, a jako heuristika se i označit, nebo nesmí tvrdit párování vůbec a nesou pak nadpisy typu „co jste poslali" a „co přišlo zpět". Druhá cesta nic nevymýšlí. Půlka předpokladů skutečného párování už stojí: jednotky od rozhodnutí [066](./decisions/066-records-attributed-to-the-input-unit.md) nesou jméno a záznamy na ně ukazují; co dál chybí, je druhá půlka — aby artefakt výstupu nesl, ze které jednotky (přesněji: z které entity a jejích jednotek) vznikl.

#### Mezireprezentace se nezobrazuje, ačkoli F14 ji jmenuje
*Zúžení, které dnes vyslovuje [`architecture.md`](./architecture.md), §9 („zobrazení IR verze nenárokuje vůbec"). Souvisí s rozhodnutími [010](./decisions/010-diagnostics-as-returned-data.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md) a [069](./decisions/069-major-marks-a-milestone-not-a-break.md). Požadavky F11, F14.*

Požadavek F14 žádá zobrazení čtyř věcí — vstupu, mezireprezentace, výstupu a diagnostiky — a nárokujeme tři: `/convert` mezireprezentaci nevrací a rozhraní ji nemá odkud vzít. Je to jediné místo, kde se dnes nárok na F14 zužuje z důvodu, který leží na serveru, ne na obrazovce, a zároveň to nejlépe placené místo pro text práce: pipeline parse → doplnění → build se čtenáři, který nástroj nikdy nespustí, ukazuje právě prostředním článkem.

Rozhodnout je třeba dřív, než se cokoli začne psát, protože cena není ve vykreslení: serializovaný tvar `EntityMap`, klíče, vztahů a dotazových instrukcí by se stal součástí REST kontraktu se vším, co to znamená pro verzování (rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md), změna MINOR při přidání pole a PATCH při jeho odebrání). Otázka tedy zní, jestli se mezireprezentace vydává jako plnohodnotná část odpovědi, nebo jako výslovně nestabilní náhled, u kterého se dopředu řekne, že se může měnit mezi vydáními — a druhá odpověď je levnější jen zdánlivě, protože nestabilní část kontraktu je pořád část kontraktu.

### Práce

#### Editor jednotky nemá čísla řádků, na která se odvolává chybová hláška
*Souvisí s rozhodnutím [033](./decisions/033-shape-of-the-static-frontend-screens.md), které validaci XML s číslem řádku zavedlo, a s [032](./decisions/032-frontend-as-static-pages-without-a-build.md), bod f (žádná další vendorovaná knihovna bez rozhodnutí). Požadavek S7.*

Validace před odesláním hlásí u nesprávně utvořeného XML číslo řádku a serverová hláška u SQL nese řádek a sloupec z `TSql160Parser`. Editor jednotky je ale holý `<textarea>` bez číslování, takže „řádek 7" se v něm hledá počítáním. Doslovné znění S7 mluví o zvýraznění chyb na úrovni souboru a řádku a tohle je jeho druhá půlka, která chybí — první, tedy chyba přiřazená ke konkrétní jednotce, hotová je.

Práce je to hotová v zadání, ne v rozvaze: postranní sloupec s čísly řádků, který se posouvá spolu s textovým polem, je několik desítek řádků vlastního kódu a chová se spolehlivě. Co je potřeba nedělat, je sáhnout po hotovém editoru — CodeMirror nebo cokoli podobného by byla třetí vendorovaná knihovna, a to je podle bodu 032f samostatné rozhodnutí, ne detail implementace.

## Stranou cílů — sestavení

Reprodukovatelnost sestavení jsme odložili stejně jako zásahy do rozhraní: položka tu leží, pracuje se na ní až po cílech a značky pořadí nedostává.

### Rozhodnutí

#### Vynucení stylu a reprodukovatelnost sestavení
*Podklad: audit [2026-08-23](./audits/2026-08-23-post-release-1-1-0-audit.md), kap. 8.4. Souvisí s rozhodnutími [034](./decisions/034-central-version-management.md) a [039](./decisions/039-container-configuration-of-the-environment.md). Požadavky S2, S5.*

„Reprodukovatelné prostředí" dnes znamená „jedním příkazem", ne „bajtově stejně": soubor zámku závislostí neexistuje, základní obrazy kontejnerů jsou připnuté na pohyblivé značky a pravidla stylu, která v repozitáři jsou, build nevynucuje. Rozhodnout je třeba, jestli se nárok S2 rozšiřuje z výstupu překladu i na sestavení samo — zámek závislostí, obrazy podle digestu, styl vynucený v CI — a jestli je to tvrzení, které text práce potřebuje, nebo údržba, která počká; dokud volba nepadne, platí dnešní užší čtení a nic víc se netvrdí.

## Stranou cílů — zdokumentované nálezy nad .NET frameworky

Nálezy z porovnání [`analysis/`](./analysis/README.md) s kódem (2026-09-14) a k nim čtyři levné konstrukce, které rozhodnutí [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) vědomě nechalo odmítat, ač by je model unesl a všechny tři cíle vyjádří. Jsou tu **zapsané, ne zařazené**: cíl 1 zůstává uzavřený vydáním 1.2.0, položky nedostávají značky pořadí a nepracuje se na nich, dokud běží cíle 2 a 3 — případně vůbec. Pět původních položek jsme 2026-09-15 přeřadili do cíle 2 — skaláry mimo uzavřený seznam, modifikátor `virtual`, nepersistovanou vlastnost, `DISTINCT` a výčet v `IN` —, protože každá rozšiřuje model nebo slovník, který javové buildery zdědí, a rozšíření je levnější před šesti buildery než po nich; tady zůstává, co javové větvi nepřekáží. Zapisujeme je proto, aby nález nezůstal jen v konverzaci a aby text práce věděl, co nástroj o .NET frameworcích netvrdí. Jedna z nich se dotýká věty, kterou [`architecture.md`](./architecture.md), §5, dnes vyslovuje šířeji, než platí; místo je u položky jmenované a opraví se s ní.

### Rozhodnutí

#### Agregační `DISTINCT` nemá v projekci místo
*Souvisí s rozhodnutím [073](./decisions/073-distinct-as-a-flag-of-the-query-scope.md), které nese `DISTINCT` jako příznak (pod)dotazu a modifikátor agregátu vědomě nechalo stranou. Požadavky F8, F11, T2.*

`COUNT(DISTINCT c.CustomerId)` je modifikátor funkce, ne dotazu: `ProjectInstruction` nese funkci jako holý název a operand sloupce s agregační funkcí totéž, takže zhroucení uvnitř agregátu nemá kam. T-SQL parser ho do rozhodnutí 073 tiše přepisoval na `COUNT(c.CustomerId)`, tedy jinou hodnotu; dnes projekci s ním vypustí se záznamem `Loss` a v podmínce filtru nebo filtru po agregaci odmítne artefakt záznamem `Failure`, HQL parser ho ve své podmnožině gramatiky hlásí jako syntaktickou chybu a LINQ tvar `g.Select(e => e.Sloupec).Distinct().Count()` je nečitelná projekce se záznamem `Loss`. Všechny tři cíle modifikátor vyjádří — T-SQL i HQL `count(distinct …)`, LINQ `.Select(…).Distinct().Count()` nad skupinou — a v ručně psaném SQL, které žádá F8, je běžný. Rozhodnout je třeba, jestli funkce v projekci a v operandu dostane příznak zhroucení, a jak ho vypíše LINQ cíl bez seskupení, kde agregát v projekci dnes ani nenese.

#### Alias v SQL jako zdroj mapování Dapperu
*Souvisí s rozhodnutími [015](./decisions/015-mapping-fact-completion-from-the-catalog.md), [017](./decisions/017-source-precedence-for-mapping-facts.md) a [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md). Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §4, a [tutoriál k Dapperu](./analysis/tutorials/dapper-getting-started.md), krok 4. Požadavek F6.*

Jedinou formou mapování, kterou Dapper má, je alias `AS` v dotazu, a nástroj ji nečte: SQL parser aliasy nese jen jako alias projekce a entitní mapa Dapper zdroje dostává sloupce až z katalogu, který páruje sloupec s vlastností podle jména. Doménu z tutoriálu — vlastnost `Id` nad sloupcem `AuthorId` — tak katalog nespáruje a klíč nedodá. Rozhodnout je třeba, jestli má alias z dotazové jednotky téhož převodu propsat název sloupce do entitní mapy jako tvrzení prvního stupně; je to fakt jednoho dotazu, ne třídy, a dva dotazy mohou týž sloupec aliasovat různě, takže rozhodnutí musí říct, co je konflikt a co ne.

#### Fluent konfigurace EF Core jako vstupní jednotka
*Rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [068](./decisions/068-source-framework-precedence-orders-the-reading.md) s parserem fluent konfigurace počítají, ale položku k němu nikdo nezapsal. Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §4. Požadavky F1, F5.*

Srovnání frameworků označuje fluent API v `OnModelCreating` za primární formu mapování EF Core a nástroj čte jen anotace a konvence. Třída kontextu navíc není ze čtení vyloučená: každá deklarace třídy v jednotce je entita, takže vložený `DbContext` vyjde jako entita s kolekčními vztahy na své `DbSet` vlastnosti. Rozhodnout je třeba, jestli fluent konfigurace vstupuje jako další artefakt EF Core, čtený v pořadí, které 068 stanovilo, a co se do té doby dělá s třídou kontextu ve vstupu — vyloučení se záznamem je levnější než dnešní tichý omyl.

#### Dapper.Contrib v rozsahu, nebo mimo něj
*Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §2. Souvisí s rozhodnutím [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md).*

Dapper.Contrib přidává k holému Dapperu atributy `[Table]`, `[Key]` a `[ExplicitKey]`, tedy tři mapovací fakty, které holý Dapper nemá kde vyslovit. Nástroj čte jen holý Dapper. Rozhodnout je třeba, jestli je Contrib výslovně mimo rozsah, nebo jestli ho Dapper parser čte jako anotace — po vzoru EF Core parseru a s týmž kritériem rozhodnutí 067.

#### Klauzule `ESCAPE` u `LIKE` nemá v podmínce místo
*Souvisí s rozhodnutím [051](./decisions/051-like-pattern-translated-not-carried-over.md), které vzorek `LIKE` do LINQ překládá, a s [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md), které `ESCAPE` nechává odmítat. Požadavky F11, T2, T3.*

`LIKE 'A!_%' ESCAPE '!'` odmítá artefakt záznamem `Failure`, protože `ComparisonCondition` s operátorem `Like` nese jen vzorek a vzorek čtený bez únikového znaku vybere jiné řádky — podtržítko by bylo zástupným znakem. Všechny tři cíle únikový znak nesou: T-SQL i HQL klauzulí `escape`, EF Core přetížením `EF.Functions.Like(x, vzorek, únik)`. Rozhodnout je třeba, jestli únikový znak dostane místo na porovnání s operátorem `Like`, a jak se s ním vypořádá překlad vzorku z rozhodnutí 051: kotvený vzorek s únikem před zástupným znakem má stále přesný protějšek (`'A!_%'` je `StartsWith("A_")`), jen ho rozpoznání musí číst po únikovém znaku.

### Práce

#### Atributy NHibernate mapování, které parser přeskakuje bez záznamu
*Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md) a [004](./decisions/004-unexpressible-facts-as-warnings.md); vyňatá oblast 2 hranice záruk ([`architecture.md`](./architecture.md), §9) se týká prvků, ne atributů. Táž [`architecture.md`](./architecture.md), §5, tvrdí, že hranici plochého čtení vyslovuje záznam — u těchhle atributů zatím ne. Požadavek F11.*

XML parser čte z `<property>` název, sloupec, typ, délku, přesnost, nullabilitu a unikátnost a z `<class>` název, tabulku a schéma; `index`, `check` a `default` hlásí záznamem. Zbytek mizí beze slova: na `<property>` `formula`, `access`, `insert`, `update`, `lazy`, `generated` a `optimistic-lock`, na `<class>` `discriminator-value`, `where`, `mutable`, `optimistic-lock`, `dynamic-insert`, `dynamic-update`, `batch-size` a `lazy`. Přinejmenším `formula` a `where` mění význam — vlastnost s `formula` nemá sloupec a výstup jí ho vymyslí — a srovnání frameworků obojí jmenuje jako výrazovou schopnost NHibernate. Práce je vydat u každého z nich záznam `Loss` týmž tvarem, jakým se hlásí `check` a `default`.

#### Anotace EF Core, pro které model místo má, ale čtou se jako ztráta
*Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md); souvisí s [049](./decisions/049-language-facts-under-source-precedence.md). Požadavky F1, F5.*

Větev pro neznámou anotaci hlásí záznamem `Loss` i `[StringLength]`, což je délka, `[Unicode]`, což je faceta `IsUnicode`, a `[InverseProperty]`, pro které model nese dosud nevyužité pole `InverseRelationName`. `InventedFactsTest` přitom `[StringLength]` jako nepřečtenou anotaci tvrdí, takže se s ním pohne zároveň. Práce je číst tři anotace do faktů, které pro ně model má, a záznam nechat jen anotacím bez místa.

#### Bázová třída entity mizí bez záznamu
*Práce podle rozhodnutí [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md); dědičnost sama je vyňatá oblast 2 hranice záruk ([`architecture.md`](./architecture.md), §9). Požadavek F11.*

Sdílený C# parser čte z hlavičky třídy jen přístupový modifikátor a seznam bázových typů nečte. Hierarchie v EF Core zdroji — třída odvozená od jiné entity převodu, kterou EF Core mapuje konvencí jako TPH — tak nezanechá žádnou stopu, kdežto týž fakt v NHibernate mapování (`<subclass>`) záznam dostane. Práce je vydat záznam `Loss` u bázového typu, který jmenuje entitu převodu; co dědičnost znamená pro mezireprezentaci, zůstává vyňatou oblastí.

#### Dvojice kolekčních navigací EF Core je N:M, ne dvě 1:N
*Práce podle rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) a [005](./decisions/005-many-to-many-as-explicit-junction-entity.md); souvisí s [015](./decisions/015-mapping-fact-completion-from-the-catalog.md). Podklad: [srovnání frameworků](./analysis/orm-frameworks-comparison.md), §8. Požadavky F1, F3.*

EF Core čte dvě kolekční navigace mezi touž dvojicí entit bez vlastnosti cizího klíče jako N:M s implicitní spojovací tabulkou (od verze 5). Parser EF Core registruje každou kolekci hned jako inverzní 1:N, takže z dvojice vzniknou dva vztahy, které tvrdí cizí klíč na obou stranách; a fáze doplnění nabídne spojovací tabulku z katalogu jen kolekci, která ještě žádný vztah nenese, takže tentýž katalog, který Dapper zdroji spojovací entitu syntetizuje, EF Core zdroj nespraví. Je to dokumentované odvození z toho, co artefakt tvrdí, a mezera putuje k jinému vztahu, takže podle kritéria 067 se materializuje. Práce je nechat kolekci čekat jako konvenční navigaci a po doparsování všech entit spárovat dvojici na N:M týmž mechanismem, jakým se dnes materializují konvenční navigace N:1.

#### Konstruktor `DateTime` v LINQ predikátu se nečte jako konstanta
*Práce podle rozhodnutí [024](./decisions/024-typed-query-operand.md) — slovník `ScalarType` hodnotu `DateTime` má a všechny tři visitory pro ni větev vypisují, jen bez výrobce —; rozhodnutí [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md) konstrukci nechává odmítat a kvůli ní přišel vzorový dotaz EF Core o svůj filtr podle data. Požadavky F11, T2, T3.*

Sdílený LINQ parser čte v pozici operandu literál, sloupec, poddotaz a hodnotu ze scope; `new DateTime(2025, 1, 1)` je konstrukce objektu a odmítá artefakt jako nepřečtený filtr. Práce je číst `new DateTime(r, m, d)` — i s kvalifikací `System.` a s časovými složkami — nad celočíselnými literály jako `QueryConstant` typu `DateTime` v ISO zápisu bez zdobení, jak žádá 024; visitory ho pak vypíší jako `'2025-01-01'` v SQL a HQL a `DateTime.Parse("2025-01-01")` v LINQ, protože ty větve už mají. Opačný směr — řetězcový literál T-SQL porovnaný s datovým sloupcem, který se čte jako `String` a do LINQ vyjde jako nepřeložitelné porovnání data s řetězcem — je jiná mezera a tahle položka ji neřeší.
