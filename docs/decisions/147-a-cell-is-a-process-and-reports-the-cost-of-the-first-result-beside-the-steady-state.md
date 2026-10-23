# 147 — Buňka měření běží v novém procesu a vedle ustáleného stavu vykazuje cenu prvního výsledku: start frameworku a první spuštění na klientovi, překlad plánu a počet plánů na serveru; ILP zůstává nad ustáleným stavem

Datum: 2026-10-17
Stav: revidováno; počet zahřívacích spuštění v mezi čekání rodiče nahrazuje 165
Požadavky: F15, T7, S4, S5, S6
Podklad: rozhodnutí [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) (iterační politika: zahřátí do shody, medián měřených spuštění, první spuštění se zahazuje), [146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md) (javová větev jako služba, nová jednotka práce na spuštění, mez času na spojení), [076](076-java-wrappers-in-csharp-jvm-in-containers.md) (JVM v kontejneru), [140](140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) a [141](141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md) (okno 0,2–300 s); položky *Izolace spouštění cizího kódu Advisorem* a *Javová větev Advisoru podle rozh. 146 nestojí*; článek *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection* (kap. o Advisoru: `cost(q, f)` jako průměrná doba spuštění, `mem(q, f)` jako špička; chování při spuštění — cache, sezení — mimo rozsah překladu); měření celého katalogu LDBC v zafixovaném prostředí z 2026-10-17 nad `d3d1a9a` (`ORMConvertor/README.md`, *Catalog query times*)

## Kontext

Buňka měření — jeden dotaz v jednom frameworku — dnes vrací medián doby jednoho spuštění v ustáleném stavu a průměr bajtů alokovaných jedním spuštěním (rozh. 145). Spuštění zahřívací fáze se zahazují; první z nich nese to, co žádné další: první výsledek dotazu v tomto frameworku. Měření z 2026-10-17 ukázalo, kolik v něm je: IS 1 183 ms proti 3 ms dál, IC 2 3 163 proti 274, IC 3 5 331 proti 2 804, dlouhé dotazy 1,1–1,5× — a to přes Dapper, tedy přes framework, jehož vlastní první spuštění je nejlevnější z šesti.

To číslo je směs čtyř vrstev, z nichž dvě o frameworku nic neříkají a dvě o něm říkají víc než medián:

| Vrstva | Co stojí | Čí vlastnost |
|---|---|---|
| 1 — stránky z disku do buffer poolu | u dlouhých dotazů většina rozdílu | dat a přístupové cesty; všech šest frameworků čte tytéž tabulky |
| 2 — překlad plánu textu na serveru | parse, bind, optimalizace; u vnořeného textu desítky až stovky ms | **textu, který framework posílá**: šest frameworků posílá šest textů a šest tvarů parametrů (Dapper deklarovaný typ, EF Core `nvarchar(4000)`), a server pro ně staví různé plány, ne jen různě dlouho |
| 3 — start frameworku a první dotaz na klientovi | Dapper emituje materializér (ms); EF Core staví model a překládá dotaz (stovky ms); NHibernate, Hibernate a EclipseLink staví továrnu (sekundy); MyBatis parsuje konfiguraci | **frameworku**, a je to vrstva, v níž se frameworky liší o řády |
| 4 — proces harnessu | fond spojení, JIT harnessu, hostitel | měření, ne dotazu |

ILP Advisoru vidí jen ustálený stav: $c_{q,f}$ je medián, $m_{q,f}$ alokace na spuštění. Článek definuje `cost(q, f)` jako průměrnou dobu spuštění a nikde nemluví o zahřátí ani o prvním spuštění; chování za běhu — cache, sezení — vylučuje z rozsahu *překladu*, ne výběru. Advisor ale vybírá framework pro workload, a kdo workload spouští v krátce žijícím procesu — testovací sada, dávkový či migrační job, nástroj příkazové řádky, obsluha požadavku v prostředí, které proces po obsluze zahodí —, platí cenu prvního výsledku při každém startu, a frameworky se v ní liší víc než v mediánu. Srovnání T7 (ILP proti každému frameworku a heuristice) by bez té veličiny vynechalo právě tu dimenzi, v níž je rozdíl mezi kandidáty největší. Požadavky ji nežádají a v `use-cases.md` nemá scénář; je to doplnění na straně práce, a to určuje, co s ní smíme dělat: změřit ji, vykázat ji a nechat ji mimo účelovou funkci, dokud ji nějaký scénář nepřivede dovnitř.

### Proč ji dnešní harness změřit nemůže

`BenchmarkExecutor` kompiluje harness Roslynem do kolektibilního `AssemblyLoadContext` a volá ho v procesu aplikace. Nový kontext dá nový *typ* harnessu, ne studený *framework*: EF Core drží překlad modelu a zkompilované dotazy v procesových singletonech (`ServiceProviderCache`, cache modelu podle typu kontextu, cache dotazů poskytovatele), Dapper drží materializéry ve statických slovnících `SqlMapper`, a JIT frameworku je po první buňce procesu teplý pro všechny další. Druhá buňka téhož frameworku v témž procesu tedy měří framework částečně zahřátý jinou buňkou, a kolik z něj, závisí na pořadí běhu. Javová větev podle rozh. 146 je dlouho žijící JVM a má totéž: `SessionFactory` se staví znovu, třídy Hibernatu a jejich JIT ne. Studený framework existuje jen v novém procesu.

Překlad plánu (vrstva 2) je naproti tomu práce serveru a server ji eviduje bez ohledu na klienta: plán v cache nese v XML atribut `CompileTime` (ms) a `sys.dm_exec_query_stats` k němu počet spuštění, fyzická čtení a čas vzniku. Dnes se na to nikdo neptá, a první zahřívací spuštění navíc nemá definovaný stav plánu: byl-li text spuštěn v dřívějším běhu, plán v cache je a překlad se neplatí.

## Zvažované varianty

### 1 — Odečíst: první spuštění minus medián, rozdělit odhadem na vrstvy

Zamítáme. Rozdíl prvního spuštění a mediánu je součet všech čtyř vrstev a žádný odhad ho nerozloží; vrstva 1 záleží na pořadí buněk (první framework dotazu platí disk za ostatní) a vrstva 4 na tom, kolikátá je buňka v běhu. Vykázalo by se číslo, které měří pořadí.

### 2 — Studený framework v procesu: nový `AssemblyLoadContext`, nová továrna, nový `URLClassLoader`

Zamítáme (výše): statické cache a JIT frameworku zůstávají, výsledek je hybrid, který nikdo neprovozuje — framework zahřátý jiným dotazem —, a obě větve by ho dosáhly jinak dobře, .NET přes kontext zavádění, Java přes classloader, jenž statická pole knihoven nezná.

### 3 — Studená cache serveru před prvním spuštěním (`DBCC DROPCLEANBUFFERS`)

Zamítáme. Učinilo by první spuštění „opravdu studeným" i ve vrstvě 1, ale ta měří disk hostitele, ne framework — a rozh. 145 ji odmítlo i jako prostředek prodloužení krátkého dotazu. Buffer pool má být při prvním spuštění naopak teplý, aby číslo neslo jen vrstvy 2 a 3.

### 4 — Druhý průchod: samostatný „startovní běh" vedle běhu ustáleného stavu

Zamítáme. První spuštění v smyčce rozh. 145 už je — jako první zahřívací —, jen se zahazuje. Druhý průchod by každou buňku spustil dvakrát, zavedl druhý mechanismus pro veličinu, kterou první mechanismus už drží v ruce, a čísla obou průchodů by si navzájem kazila stav (druhý průchod najde plány a stránky prvního).

### 5 — Cena prvního výsledku do ILP: pevný člen $s_f \, y_f$, nebo překlad plánu přičtený do $c_{q,f}$

Zamítáme pro tuto chvíli. Start frameworku se platí jednou za život procesu, překlad plánu jednou za život plánu v cache, medián jednou za spuštění; aby stály v jedné sumě, musí model znát horizont — kolikrát za workload proces startuje, kolikrát se plán vyhodí — a ten žádný požadavek ani scénář nedává. Váha vymyšlená pro úplnost by srovnání T7 vzdálila od modelu článku, proti němuž se srovnání vede, a číslo v účelové funkci by znamenalo víc, než víme. Člen $s_f \, y_f$ je přitom lineární v $y_f$ a do modelu zapadne, až scénář horizont vysloví; to je nové rozhodnutí nad změřenými čísly, ne tohle.

### 6 — Buňka je proces; první spuštění smyčky se vykazuje; překlad plánu a počet plánů dává server; buňka s fyzickým čtením se opakuje; ILP beze změny

## Rozhodnutí

**Volíme variantu 6.**

**Buňka je proces.** Každá buňka měření běží v novém procesu, který vzniká pro ni a s ní končí, v obou větvích. V .NET větvi `RoslynBenchmarkCompiler` zapíše zkompilovaný harness do souboru v dočasném adresáři běhu a `BenchmarkExecutor` spustí `AdvisorBenchmarking.Runner` — konzolový projekt řešení, který odkazuje tytéž balíky frameworků jako aplikace, aby se odkazy harnessu vyřešily — s cestou k sestavení, frameworkem, připojovacím řetězcem (s `Command Timeout` podle rozh. 146) a hodnotami politiky; runner sestavení zavede, zavolá `Setup`, provede smyčku rozh. 145 beze změny a vypíše na standardní výstup jeden JSON s výsledky buňky, který rodič přečte. Rodič čeká nejdéle tolik, kolik smyčka pod politikou může trvat — $(\text{MaxWarmupIterations} + \text{MinIterations}) \cdot \text{ExecutionTimeoutSeconds}$ plus cílový úhrn —, pak proces ukončí a buňka selže jako každá jiná. V javové větvi je služba `advisor_java` rozh. 146 koordinátorem stejně jako aplikace a pro každou buňku spustí dětský proces `java` z téhož obrazu a téže classpath, v němž běží tatáž smyčka; služba sama neměří nic. Smyčka, politika, jednotka práce i mez času rozh. 146 platí beze změny; mizí jen asymetrie, kterou 146 vyslovilo — .NET v procesu aplikace, Java mimo něj —, protože teď je buňka procesem v obou větvích a hranice procesu, kterou hledala položka o izolaci, vzniká pro .NET větev z konstrukce, jako v 146 pro javovou.

**Na klientovi** měří dětský proces dvě doby, `Stopwatch` v .NET a `System.nanoTime()` v Javě, a vrací je vedle mediánu:

- `SetupMilliseconds` — od prvního řádku `Setup` po jeho návrat: zavedení sestavení či tříd frameworku, stavba továrny (`DbContextOptions` a kontext; `SessionFactory`; `EntityManagerFactory`; `SqlSessionFactory`), otevření spojení. Co framework dělá v továrně a co až při prvním dotazu, je jeho vlastnost — EF Core staví model líně při prvním použití, Hibernate v továrně —, a právě proto se obě doby vykazují zvlášť a jejich součet je cena prvního výsledku.
- `FirstExecutionMilliseconds` — doba prvního zahřívacího spuštění smyčky rozh. 145, které se dosud zahazovalo a teď se uchová; nese první práci frameworku nad dotazem (překlad modelu a dotazu a emise materializéru v EF Core, překlad HQL a plán dotazu v Hibernatu, materializér Dapperu) i překlad plánu a spuštění na serveru. Žádné spuštění nepřibývá: smyčka je táž a medián se dál počítá jen z měřených spuštění.

Start procesu po vstup do `main` se neměří: je to cena běhového prostředí, které si uživatel volí nezávisle na frameworku, a v javové větvi by startu JVM nebylo s čím .NET srovnat.

**Na serveru** měří koordinátor — aplikace, pro obě větve stejně, přes vlastní spojení — tři veličiny jedním mechanismem pro všech šest frameworků, aniž se kterýkoli z nich dotkne: před buňkou vyprázdní cache plánů měřené databáze (`ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE`), takže první spuštění má definovaný stav — plán studený, buffer pool teplý —, a po buňce přečte z `sys.dm_exec_query_stats`, `sys.dm_exec_query_plan` a `sys.dm_exec_plan_attributes` plány té databáze vzniklé od začátku buňky:

- `CompileMilliseconds` — součet `CompileTime` z XML těch plánů: práce serveru nad textem, který framework poslal, oddělená od spuštění bez odhadu;
- `Plans` — počet těch plánů: víc než jeden znamená, že framework neposílá jeden parametrizovaný text — literály místo parametrů, délky parametrů podle hodnoty —, vlastnost frameworku, kterou medián neukáže a která v produkci plní cache plánů;
- `PhysicalReads` — součet fyzických čtení těch plánů, kontrola stavu buffer poolu (níž).

Čtení z pohledů žádá právo `VIEW SERVER PERFORMANCE STATE` a vyprázdnění cache právo `ALTER ANY DATABASE SCOPED CONFIGURATION`; přihlášení měření je v compose `sa` a obě má. Bez nich buňka vrátí serverové veličiny jako chybějící se záznamem, nikdy mlčky, a klientské drží.

**Teplý buffer pool je podmínka, kterou číslo kontroluje samo.** Cena prvního výsledku je definována nad teplým buffer poolem, protože studené stránky jsou vrstva 1. Pořadí běhu rozh. 145 — po dotazech, v dotazu přes frameworky — ale nechává první framework každého dotazu stránky zaplatit; buňka, jejíž `PhysicalReads` je větší než nula, se proto spustí ještě jednou celá, v novém procesu, a druhý běh je její měření. Pravidlo odstraňuje pořadí z čísla podle počítadla serveru, ne podle odhadu, zachytí i vytlačení stránek ze 4 GB poolu kdykoli v běhu, a stojí zpravidla jednu buňku navíc na dotaz. Čte-li i druhý běh z disku, číslo platí a záznam běhu to řekne: data se do poolu nevešla.

**ILP se nemění.** Účelová funkce i omezení zůstávají podle článku: $c_{q,f}$ medián, $m_{q,f}$ alokace na spuštění. Pět nových veličin nese každá buňka v odpovědi (`BenchmarkMeasurementDto`), obrazovka Advisoru je ukazuje vedle mediánu a záznam běhu je vykazuje; interpretace je na čtenáři, s horizontem, který zná on. Srovnání T7 tak dostane dvě tabulky nad týmž během — ustálený stav, podle něhož ILP volí, a cenu prvního výsledku, v níž se frameworky liší nejvíc — a práce může ukázat, kde by volba podle jedné odporovala druhé.

### Proč proces, a ne zavaděč

Studený framework je definován tím, co má v paměti: nic. Jediná hranice, za kterou žádný statický slovník ani JIT nepřežije, je proces; vše slabší měří framework zahřátý něčím jiným, v míře dané pořadím. Proces zároveň dává .NET větvi to, co 146 dalo javové z konstrukce — hranici, za níž cizí kód neběží s pamětí aplikace —, a položka o izolaci se zužuje na limity té hranice (S4: CPU, paměť, práva), ne na její vedení. Cena je start procesu na buňku, řádově stovky ms v .NET a sekundy v JVM, mimo každé měřené číslo; při 41 dotazech a šesti frameworcích minuty na běh, který trvá hodiny.

### Proč první spuštění ze smyčky, a ne zvlášť

Smyčka rozh. 145 první spuštění provádí a jen ho zahazuje; uchovat ho nic nestojí a nic nemění — medián, zahřátí do shody ani počet spuštění. Definovaný stav mu dává vyprázdněná cache plánů před buňkou, kterou dnes nikdo nedrží: bez ní první spuštění platí překlad plánu podle toho, zda text někdo spustil dřív. Vyprázdnění nezasáhne medián, protože překlad proběhne v prvním zahřívacím spuštění, které medián nevidí.

### Proč server, a ne framework

Překlad plánu je práce serveru nad textem, a server ji eviduje s plánem; číst ji z pohledů je totéž, co rozh. 146 udělalo s mezí času: jedno místo pro šest frameworků, žádný artefakt se nemění, žádné rozhraní frameworku se nevolá. Alternativa — `SET STATISTICS TIME` a zprávy spojení — by šla přes spojení každého frameworku, v Javě přes `SQLWarning`, a vrátila text k parsování. Počet plánů se z klienta nedá zjistit vůbec.

### Proč harness a pohledy serveru, a ne monitorovací služba

Měření by mohla nést i obecná monitorovací infrastruktura, jak se nasazuje u provozovaných služeb: Prometheus s exportérem SQL Serveru a databází časových řad. Zamítáme ji ze čtyř důvodů.

- **Měří jinou veličinu.** Prometheus v pevném intervalu stahuje metriky ze sledovaných cílů (výchozí je minuta, běžně se nastavuje 15 s) a ukládá je jako časové řady; dobu spuštění nese jen agregovaně, jako počty v přihrádkách histogramu nebo kvantily souhrnu spočtené klientem. Buňka potřebuje dobu každého jednotlivého spuštění — u IS 1 tři milisekundy — a medián z nich přiřazený k dotazu, frameworku a commitu; to je `cost(q, f)` článku. Měří ho `Stopwatch` a `System.nanoTime()` přímo v harnessu (rozh. 145). Prometheus by jen přenášel, co harness už změřil, s přesností danou hranicemi přihrádek.
- **Nepřiřadí serverové veličiny buňce.** `CompileMilliseconds`, `Plans` a `PhysicalReads` mají smysl jen ohraničené buňkou: cache plánů se před ní vyprázdní a pohledy se čtou hned po ní. Exportér, který se serveru ptá v intervalu, buňku nezná — buňku o několika milisekundách by jeho vzorek vůbec nezachytil a dlouhou by rozdělil mezi dva vzorky spolu s buňkou sousední.
- **Přidává šum, který rozh. 145 odstranilo.** Prometheus a exportér jsou další procesy ve virtuálním stroji se čtyřmi pevně rozdělenými procesory, a exportér klade měřené databázi vlastní dotazy nad týmiž pohledy právě během měření: souběžnou zátěž serveru, kterou zafixované prostředí vyloučilo oddělením databáze od klientů.
- **Nepřidá nic k reprodukovatelnosti.** Záznam běhu váže čísla na commit a prostředí (rozh. 095, záznamy v `docs/runs/`), výstup po buňkách zůstává v souborech běhu mimo git (rozh. 042). Databáze časových řad je váže na čas hodin a je další službou, kterou je třeba spustit, nastavit a verzovat, aby šel běh zopakovat (S5, S6).

Smysl by služba dávala nanejvýš jako dohled nad hostitelem při vícedenním běhu soudce LDBC — paměť, restarty. Dvěma věcem, které takový běh skutečně ukončí, restartu stroje aktualizací Windows a novému vytvoření `mssql_db`, by ale nezabránila, a výsledek běhu drží `docker logs` pojmenovaného kontejneru.

## Důsledky

- **Kód (práce, položka v `open-items.md`):** projekt `AdvisorBenchmarking.Runner` (konzole; do seznamu `.csproj` ve stage `dotnet-build` obrazu aplikace); `RoslynBenchmarkCompiler` zapisuje sestavení do souboru; `BenchmarkExecutor` spouští proces, čte JSON, hlídá nejdelší přípustnou dobu; `BenchmarkMeasurement` a `BenchmarkMeasurementDto` nesou `SetupMilliseconds`, `FirstExecutionMilliseconds`, `CompileMilliseconds`, `Plans`, `PhysicalReads`; `AdvisorRunCoordinator` před buňkou vyprázdní cache plánů, po buňce čte pohledy a opakuje buňku s fyzickým čtením; `advisor.js` ukazuje nové sloupce. Javová větev dostává dětský proces na buňku jako součást položky *Javová větev Advisoru podle rozh. 146 nestojí*.
- **Položka *Izolace spouštění cizího kódu Advisorem*** se zužuje: hranice procesu je rozhodnuta pro obě větve; zbývají limity CPU, paměti a práv toho procesu (S4).
- **Rozh. 146** platí beze změny; věta o asymetrii umístění obou větví přestává platit tímto rozhodnutím.
- **Záznam běhu** (rozh. 095) vykazuje vedle mediánu i pět nových veličin a říká, které buňky se opakovaly pro fyzické čtení. Čísla změřená před tímto rozhodnutím jsou jen ustáleného stavu; cena prvního výsledku se s nimi nesrovnává, protože vznikala v procesu zahřátém jinými buňkami.
- **Dokumentace:** `architecture.md` §8 po implementaci (buňka jako proces, nové veličiny, pravidlo opakování), `ORMConvertor/README.md` (*Catalog query times*: tvar záznamu), `threat-model.md` (hrozba 1: hranice procesu u .NET větve).

## Historie

- **2026-10-23** — revidováno: doplněn odstavec *Proč harness a pohledy serveru, a ne monitorovací služba* (Prometheus s exportérem SQL Serveru jako zamítnutá cesta měření) a k požadavkům S5. Volba se nemění; rozhodnutí ještě není implementované.
