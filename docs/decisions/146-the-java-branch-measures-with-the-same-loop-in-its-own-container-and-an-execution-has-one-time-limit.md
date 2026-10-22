# 146 — Javovou větev Advisoru měří tatáž smyčka jako .NET větev, ve vlastním kontejneru s JVM jako služba vedle aplikace; spuštění dotazu má jednu mez času, ve smyčce obou větví i u soudce, nastavenou na spojení ovladače

Datum: 2026-10-17
Stav: platí; zahřátí do shody v odstavci *Smyčka* nahrazuje 165
Požadavky: F15, T7, S4, S5, T2, T3, F13
Podklad: rozhodnutí [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) (iterační politika a zafixované prostředí), [076](076-java-wrappers-in-csharp-jvm-in-containers.md) (JVM zůstává v kontejneru, javová větev mimo proces aplikace), [078](078-java-suite-as-a-client-of-a-running-instance.md) (javová sada jako klient instance), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) (soudce), [140](140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) a [141](141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md) (okno 0,2–300 s); položky *Čím Advisor měří javové frameworky* a *Izolace spouštění cizího kódu Advisorem*; riziko *Metodologie měření pro Javu* ve specifikaci (kap. *Rizika*); záznam z 2026-10-15 (jedno čtení IC 14 v EF Core vypršelo na výchozích 30 s příkazu, ručně týž příkaz 2,2 s) a měření z 2026-10-17 v zafixovaném prostředí (`ORMConvertor/README.md`, *Catalog query times*)

## Kontext

Advisor měří jen .NET frameworky. `BenchmarkExecutor` zkompiluje harness nad přeloženými artefakty a spustí ho v procesu aplikace vlastní smyčkou podle politiky rozh. 145: zahřátí, dokud se dvě po sobě jdoucí spuštění neshodnou na 10 %, pak měřená spuštění, dokud úhrn nedosáhne 1 000 ms a nejméně tři; cenou $c_{q,f}$ ILP je medián doby jednoho spuštění ze `Stopwatch`, pamětí $m_{q,f}$ průměr bajtů, které jedno spuštění alokovalo na měřicím vlákně (`GC.GetAllocatedBytesForCurrentThread`). Hibernate, EclipseLink ani MyBatis nemá Advisor čím změřit, a specifikace vede *metodologii měření pro Javu* jako riziko. Položka v `open-items.md` postavila tři varianty měřicího jádra: JMH jen pro javovou větev, vlastní smyčku zrcadlící `BenchmarkExecutor`, a JMH s BenchmarkDotNet pro obě větve. Hranici dalo rozh. 076: JVM zůstává v kontejneru a javová větev běží mimo proces aplikace.

S měřením souvisí druhá věc, kterou nikdo nerozhodl: **mez času jednoho spuštění dotazu.** Harnessy Advisoru ani buňky soudce LDBC žádnou nenastavují, takže platí výchozí hodnota ovladače — 30 s u `Microsoft.Data.SqlClient`, nekonečno u `mssql-jdbc`. Tu hodnotu nikdo nezvolil a obě sady se na témž pomalém čtení chovají různě: .NET sada ho po 30 s zahodí jako chybu buňky, javová čeká bez konce. Vlastní příkazy soudce (vkládání, kompenzace, počty řádků) mají mez 600 s vyslovenou na dvou místech v každé sadě. Záznam z 2026-10-15 ukázal, co výchozích 30 s stojí: nad prefixem 2 000 řádků SF 1 vypršelo jedno čtení IC 14 v EF Core, ač týž příkaz s týmiž parametry běží ručně 2,2 s. V zafixovaném prostředí rozh. 145 vzrostly časy dotazů 4–9× (polovinu dělá `max degree of parallelism` 1, zbytek vypnuté turbo a dva procesory databáze), takže čtení, která byla pod mezí s rezervou, se k ní blíží — a totéž platí pro Advisor: dotaz delší než 30 s nejde změřit, ačkoli okno rozh. 140 připouští dotaz do 300 s. Mez tedy musí být vyslovená, jedna pro obě větve měření i pro soudce, a nastavená tak, aby ji nesl každý ze šesti frameworků stejně.

## Zvažované varianty

### Měřicí jádro javové větve

**1 — JMH jen pro javovou větev.** Zamítáme. JMH řeší, co vlastní smyčka v JVM neřeší — on-stack replacement, eliminaci mrtvého kódu přes `Blackhole`, profil JIT oddělený forkem —, jenže to jsou problémy mikrobenchmarku v řádu nanosekund. Dotaz do databáze trvá milisekundy až sekundy, jeho výsledek se materializuje do objektů a vrací volajícímu, a kompilátor ho nevypustí. Co by JMH přinesl navíc — fork JVM, statistiku, interval spolehlivosti —, by měla jen javová větev, a čísla obou větví by do jednoho modelu ILP vstupovala z různých metodik: .NET z vlastní smyčky v procesu, Java z harnessu s forkem. Rozh. 145 právě vyslovilo politiku nad vlastní smyčkou a odůvodnilo ji; javová větev s jinou politikou by ji popřela. K tomu anotační procesor `jmh-generator-annprocess` při kompilaci harnessu a další závislost GPLv2 s Classpath Exception.

**2 — JMH a BenchmarkDotNet, obě větve na zavedeném harnessu mimo proces aplikace.** Zamítáme. Je to nejobhajitelnější srovnatelnost na papíře, ale za cenu přestavby .NET větve, která funguje a jejíž politiku rozh. 145 právě zafixovalo: BenchmarkDotNet generuje vlastní projekt a staví ho `dotnet build` za běhu Advisoru, nebo s `InProcessEmitToolchain` nechá kód v procesu a výhodu samostatného procesu ztratí. Dosavadní čísla by změnila význam, dvě politiky — BDN pilotuje a iteruje po svém — by nahradily jednu vyslovenou, a výsledkem by byl Advisor závislý na dvou harnessových knihovnách, jejichž vnitřní rozhodnutí by práce musela vysvětlit, místo jedné smyčky o čtyřech konstantách, kterou vysvětlí sama.

**3 — Vlastní smyčka zrcadlící `BenchmarkExecutor`.** Volíme (níže).

### Kde javová větev běží

**a — JDK v obrazu aplikace a proces `java` spouštěný aplikací.** Zamítáme. Rozh. 076 nechalo JVM v kontejneru odděleném od aplikace a obraz aplikace bez JDK; kompilace a spuštění cizího kódu by se přesunuly do procesu, který aplikace spouští s vlastními právy, a hrozba 1 modelu hrozeb by se rozšířila o druhý jazyk.

**b — Aplikace spouští kontejner přes Docker socket.** Zamítáme. Socket Dockeru v kontejneru aplikace je právo roota na hostiteli; žádná izolace cizího kódu nemůže začínat tím, že aplikace dostane víc práv, než má dnes.

**c — Služba vedle aplikace, volaná přes HTTP.** Volíme. Je to zrcadlo rozh. 078: javová sada je klientem běžící instance nástroje a bere si artefakty přes HTTP; javová větev Advisoru je službou, které instance nástroje artefakty přes HTTP posílá. Hranice procesu, kterou hledá položka o izolaci, tu vzniká z konstrukce — kontejner s vlastním JVM, vlastní pamětí a vlastními procesory — a nic na aplikaci se kvůli ní nemění.

### Co je jedno spuštění v Javě

**Jedna `EntityManager`, `Session` či `SqlSession` přes všechna spuštění.** Zamítáme. Persistence context JPA vrací při druhém spuštění téhož dotazu spravované instance z prvního, bez hydratace řádků; MyBatis má lokální cache na session. Měřila by se cache, ne mapovací vrstva, a čísla by se lišila podle toho, kolikrát se buňka zahřívala.

**`clear()` po každém spuštění.** Zamítáme. Vyprázdnění kontextu je zásah do jednotky práce uprostřed jejího života, který aplikace nedělá, a jeho cena i význam se mezi Hibernatem a EclipseLinkem liší.

**Nová jednotka práce na každé spuštění, továrna mimo měření.** Volíme (níže).

### Mez času spuštění

**Žádná.** Zamítáme. Čtení, které se zaseklo, by zastavilo celodenní běh soudce bez verdiktu a běh Advisoru bez odpovědi; javová sada tak dnes běží.

**Výchozí hodnota ovladače.** Zamítáme. Je to dnešní stav: 30 s v .NET, nekonečno v Javě, nikým nezvolené, a záznam z 2026-10-15 ukazuje, že 30 s utne správné čtení.

**Mez rozhraním každého frameworku.** Zamítáme. `CommandTimeout` v možnostech `DbContext`, `command_timeout` NHibernatu, `setTimeout` dotazu Hibernatu, hint `jakarta.persistence.query.timeout` EclipseLinku, `defaultStatementTimeout` MyBatisu — šest mechanismů pro jednu hodnotu, a buď by je nesl generovaný artefakt, čímž by se překlad lišil od toho, co uživatel žádal, nebo by je soudce i harness znaly za každý framework zvlášť.

**Jedna hodnota na spojení ovladače.** Volíme (níže).

## Rozhodnutí

**Volíme variantu 3 pro měřicí jádro, umístění c, novou jednotku práce na spuštění a jednu mez času na spojení ovladače.**

**Smyčka.** Javovou větev měří tatáž smyčka a táž politika jako .NET větev: zahřátí do shody dvou po sobě jdoucích spuštění v toleranci, nejvýš pětkrát; měřená spuštění do cílového úhrnu a nejméně tři; medián doby jednoho spuštění jako $c_{q,f}$ a průměr bajtů alokovaných jedním spuštěním na měřicím vlákně jako $m_{q,f}$. Čas dává `System.nanoTime()`, alokace `com.sun.management.ThreadMXBean#getCurrentThreadAllocatedBytes` — přesný protějšek `GC.GetAllocatedBytesForCurrentThread`, který počítá jen vlákno, jež dotaz spustilo, a tedy totéž, co .NET větev. Hodnoty politiky zůstávají na jediném místě, v `BenchmarkPolicy`: aplikace je pošle v požadavku a javová služba je vezme jako parametry smyčky, žádnou vlastní konstantu nemá. Paměť obou větví je alokace na spuštění, ne špička: špička procesu JVM je dána `-Xmx` a špička procesu .NET chováním GC, ani jedna není vlastností dotazu, kdežto alokace měřicího vlákna jsou tím, co mapovací vrstva za jedno spuštění skutečně vyrobila. Článek *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection* bere za $mem(q,f)$ špičku; rozh. 145 vykázalo alokace pro .NET a tímto rozhodnutím totéž platí pro obě větve.

**Služba.** Javová větev je služba `advisor_java` v compose, postavená ze stejného obrazu Maven a Temurin 25, který staví javovou sadu, s vlastním javovým projektem vedle `JavaTests`. Přijme přes HTTP framework, přeložené artefakty, připojení k databázi a politiku; artefakty zkompiluje `javax.tools.JavaCompiler` tak, jak to dělá javová sada, postaví továrnu frameworku jako ona (`HibernateBootstrap`, `EclipseLinkBootstrap`, `MyBatisBootstrap` jsou předlohou), proměří buňku smyčkou výše a vrátí medián, alokace a počty spuštění v tomtéž tvaru, v jakém je nese `BenchmarkMeasurementDto`. Aplikace jí posílá javové kandidáty a .NET kandidáty měří dál sama; adresu služby dává konfigurace (`Advisor:JavaRunnerUrl`, compose ji nastaví), a bez ní javový kandidát z běhu vypadne se záznamem, nikdy mlčky. Obě větve běží na procesorech 2–3 podle rozh. 145, databáze na 0–1; start JVM, JIT a kompilace artefaktů patří do přípravy a zahřátí buňky, ne do měřených spuštění, stejně jako Roslyn a JIT v .NET větvi. Asymetrie, která zůstává — .NET v procesu aplikace, Java ve vlastním procesu —, je vyslovená: obě větve měří touž smyčkou nad touž databází, v témž prostředí a s touž politikou, a ILP smí v jednom běhu míchat kandidáty obou ekosystémů, protože to po něm F15 žádá — framework pro workload, ne framework pro platformu.

**Jedno spuštění.** V každém ze šesti frameworků je jedním spuštěním čtení, které si ze spuštění předchozího nenese žádný stav jednotky práce: v Dapperu dotaz nad otevřeným spojením, v EF Core dotaz nad jedním `DbContext` bez sledování (`NoTracking`, tedy bez identity map), v Hibernatu a EclipseLinku dotaz v nové `EntityManager` otevřené pro to spuštění a po něm zavřené, v MyBatisu dotaz v nové `SqlSession`; NHibernate, až ho Advisor změří, dostane novou `ISession`. Továrna — `SessionFactory`, `EntityManagerFactory`, `SqlSessionFactory`, fond spojení — vzniká v přípravě buňky mimo měření, se stejnými výchozími hodnotami, s jakými ji staví javová sada. Otevření jednotky práce je vlastní cena frameworku za bezstavové čtení a platí ji každý kandidát stejně; co si framework drží v továrně sám od sebe (sdílená cache EclipseLinku, fond spojení), je částí toho, co uživatel s artefakty nástroje dostane, a měří se s ním. Soudce LDBC čte MyBatisem už dnes session na čtení a EF Core jedním kontextem; tvar spuštění je tedy týž, jaký ověřuje čtvrtý stupeň.

**Mez času.** Jedno spuštění dotazu má mez **600 s**, nastavenou na spojení ovladače: klíčové slovo `Command Timeout` v připojovacím řetězci `Microsoft.Data.SqlClient` a vlastnost `queryTimeout` v JDBC URL `mssql-jdbc`. Obě jsou výchozí hodnotou každého příkazu vytvořeného nad tím spojením, ať ho vytvoří Dapper, EF Core, NHibernate, Hibernate, EclipseLink nebo MyBatis — žádný z nich mez sám nenastavuje, nepřijde-li s dotazem (ověřeno pro `Microsoft.Data.SqlClient` 7.0.2 a ovladač NHibernatu 5.7.0) —, takže jedna hodnota na jednom místě drží všech šest frameworků a generovaný artefakt se nemění. Hodnotu nese `BenchmarkPolicy.ExecutionTimeoutSeconds` pro obě větve měření a `LdbcDatabase` obou sad pro soudce, který ji dává spojení předávanému buňkám i svým vlastním příkazům — je to táž hodnota 600 s, kterou vkládání a kompenzace soudce měly, a test .NET sady drží, že se ta tři místa shodují. Je to dvojnásobek horní meze okna rozh. 140, aby parametr z validační sady, který je pomalejší než příkladový, neshodil čtení, jež okno připouští. Spuštění nad mezí končí chybou ovladače: u soudce je to neshoda buňky, ne vynechané čtení, protože čtení, které v zafixovaném prostředí netrvá ani deset minut, by bylo vlastností SQL, jež framework píše, a tu by řešilo nové rozhodnutí; u Advisoru selhání buňky jako každé jiné.

### Proč stejná smyčka, a ne stejný harness

Srovnatelnost dvou čísel dává totožná definice toho, co se měří — totéž spuštění, totéž zahřátí, totéž kritérium ukončení, týž odhad polohy —, ne totožná knihovna. Smyčka rozh. 145 má čtyři konstanty a každou odůvodňuje; přenést ji do Javy znamená přenést definici, a práce ji vysvětlí jednou pro obě větve. Dva zavedené harnessy by srovnatelnost předstíraly: každý má vlastní pilotní fázi, vlastní heuristiku počtu iterací a vlastní statistiku, a to, že se obě shodnou na sémantice alokací, neznamená, že se shodnou na tom, kdy je buňka zahřátá.

### Proč služba, a ne proces

Mezi aplikací a JVM je hranice jazyka i hranice procesu, a rozh. 076 je položilo do kontejneru. Služba ji nechává tam, kde je, dává javové větvi vlastní paměť a procesory stejně jako databázi, a pro aplikaci je to jedno volání s touž odpovědí, jakou dává `BenchmarkExecutor`. Proces `java` v kontejneru aplikace by hranici zrušil, socket Dockeru by ji obrátil proti hostiteli.

### Proč mez na spojení, a ne v politice každého frameworku

Mez je vlastnost měření a soudce, ne vlastnost dotazu ani frameworku. Na spojení ji dostane každý příkaz, který nad ním kdo vytvoří, a nikdo nemusí vědět, jak se jmenuje v šesti rozhraních; artefakt zůstane takový, jaký nástroj přeložil. Hodnota 600 s není měřená konstanta prostředí, ale strop pro chybu: dotazy Interactive běží s příkladovými parametry v řádu sekund i v zafixovaném prostředí, okno rozh. 140 připouští 300 s, a mez se tak nedotkne ničeho, co není vadné nebo zaseknuté.

## Důsledky

- **Mez času (hned):** `BenchmarkPolicy.ExecutionTimeoutSeconds` = 600; `BenchmarkHarnessBuilder` dá připojovacímu řetězci harnessu `Command Timeout` z politiky; `BenchmarkPolicyDto` a odpověď běhu ji vracejí. `LdbcJudge/LdbcDatabase` (.NET) a `ldbc/LdbcDatabase` (Java) nesou touž hodnotu a vracejí připojovací řetězec, resp. JDBC URL s ní; explicitní 600 s u vkládání, kompenzace a počtů řádků mizí, protože je nese spojení. `LdbcValidationTest` obou sad drží tvar řetězce a .NET test shodu tří míst (politika, .NET soudce, javový soudce — ten čtením zdroje, jako `TargetFrameworkDescriptorTest` čte `pom.xml`).
- **Javová větev (práce, položka v `open-items.md`):** projekt javové služby vedle `JavaTests`, stage obrazu a služba `advisor_java` v compose s `cpuset` 2–3, klient v aplikaci (`IBenchmarkExecutor` pro javové frameworky), `KnownFrameworks` a `SupportedFrameworks` rozšířené o Hibernate, EclipseLink a MyBatis; kandidát bez služby vypadne se záznamem. Harness NHibernatu zůstává samostatnou prací na .NET straně a dostane novou `ISession` na spuštění.
- **Položka *Izolace spouštění cizího kódu Advisorem*** zůstává jen pro .NET větev: javová větev je mimo proces z konstrukce.
- **Dokumentace:** `architecture.md` §6.2 (mez čtení soudce) a §8 (mez v politice, javová větev podle tohoto rozhodnutí), `ORMConvertor/README.md` (*The LDBC judge*).
