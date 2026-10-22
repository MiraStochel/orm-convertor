# 145 — Měření stojí na zafixovaném prostředí a vyslovené iterační politice: SQL Server s pevnou pamětí a bez paralelismu, databáze a klienti na oddělených procesorech, virtuální stroj s pevnými procesory a pamětí; krátký dotaz se neprodlužuje, opakuje se do meze a vykazuje se medián

Datum: 2026-10-17
Stav: platí; větu o prostředí soudce LDBC v odstavci *Prostředí* a důsledek *Obraz databáze* nahrazuje 164; zahřátí do shody v odstavci *Iterační politika* nahrazuje 165
Požadavky: T2, T3, T7, S5, S6
Podklad: rozhodnutí [140](140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) (okno 0,2–300 s), [141](141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md) (spodní mez drží opakování), [121](121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md) (indexy `LdbcSnb`), [039](039-container-configuration-of-the-environment.md) (compose), [095](095-a-dated-run-record-names-its-commit.md) (záznam běhu); položka *Iterační politika benchmarku je konstanta v kódu* (specifikace, kap. *Rizika*: politiku vyslovit a odůvodnit); měření nad `LdbcSnb` SF 1 na MIS3 z 2026-10-17 nad `6f2c1a2` (níž, záznam v `ORMConvertor/README.md`, *Catalog query times*)

## Kontext

Rozhodnutí 140 a 141 nechala spodní mez okna — dotaz má na MIS3 běžet aspoň 0,2 s — na opakování v měření, protože vyhledání přes index žádný scale factor nad mez nezvedne. Specifikace zároveň slibuje vyslovit iterační politiku benchmarku, kterou `BenchmarkExecutor` nesl jako neodůvodněné konstanty (dvě zahřátí, pilot, 3–20 iterací s cílem 500 ms, průměr). Než se politika vysloví, bylo třeba dořešit tři věci: zda krátký dotaz nejde prodloužit jinak než daty, proč týž dotaz dává na MIS3 v různých průchodech různé časy, a co stojí studený start.

### Prodloužení krátkého dotazu

Měření 2026-10-17 (`6f2c1a2`, kontejner `mssql_db`, text katalogu s příkladovými parametry přes Dapper z hostitele, čtyři běhy na dotaz a průchod, medián tří teplých; indexy rozh. 121 shozeny a obnoveny skriptem `database/ldbc/indexes.sql`):

| Dotaz | S indexy (tři průchody, medián v ms) | Bez indexů (dva průchody) | Vliv indexů |
|---|---|---|---|
| IS 1, IS 3–6 | 1–3 | 1–5 | žádný: jen vyhledání přes seskupený primární klíč |
| IS 2, IS 7 | 2–4 | 48–173 | 20× — stále pod 0,2 s |
| IC 8 | 8–31 | 122–310 | 10× |
| IC 4, BI 7, BI 13, BI 20 | 57–306 | 198–1 460 | 3–8× |
| IC 1, IC 2, IC 7, IC 11, IC 13 | 48–254 | 40–253 | žádný |
| IC 5, IC 9, IC 10, BI 1 | 157–936 | 123–670 | v šumu |

### Šum

Šum téhož prostředí byl větší než vliv indexů: IC 5 měl se shodnými indexy mediány 936, 214 a 608 ms ve třech průchodech, IC 9 813, 230 a 599 ms; první průchod po startu kontejneru byl všude 2–4× pomalejší. MIS3 je notebook ASUS ZenBook UX425JA: Intel Core i7-1065G7 (4 jádra, 8 vláken, 1,3 GHz základ, turbo do 3,9 GHz, 15 W), 16 GB paměti, Docker Desktop nad WSL2; virtuální stroj Dockeru neměl žádné nastavení (polovina paměti, všech osm vláken, dynamicky), SQL Server žádnou mez paměti ani paralelismu, kontejnery sdílely všechny procesory. Příčiny podle velikosti: frekvence procesoru (jedno vlákno běží podle teploty a předchozí zátěže kdekoli mezi 1,3 a 3,9 GHz, sám o sobě trojnásobný rozsah); studený stav po startu; tlak na paměť mezi Windows a virtuálním strojem; paralelní plány (čas závisí na volných jádrech); ostatní procesy.

Po zafixování (níž) tři průchody týchž dotazů: IC 5 1 810, 1 790 a 1 782 ms, IC 9 1 301, 1 318 a 1 350 ms, BI 1 2 246, 2 230 a 2 333 ms, IS 1–7 stále 2–4 ms — rozptyl mezi průchody klesl z 2–4× pod 5 %. Absolutní časy vzrostly 4–9×: asi 4× dělá vypnuté turbo s databází na dvou procesorech (tentýž průchod s povoleným paralelismem: IC 5 943, BI 1 1 459, IC 10 574 ms), zbylých 1,5–2× `max degree of parallelism` 1.

### Studený start

V zafixovaném prostředí se studený start už nerozkládá přes celý průchod (mediány prvního a druhého průchodu se liší o jednotky procent); zbývá první spuštění každého dotazu: IC 2 3 362 ms proti 270–285 ms dál, IC 5 3 735 proti 1 780–1 900, IS 1 261 a 91 proti 2–3. První spuštění platí stránky dotazu z disku do buffer poolu, překlad plánu a na klientovi fond spojení, JIT a zkompilovaný harness.

## Zvažované varianty

### 1 — Krátký dotaz prodloužit změnou databáze: shodit indexy, zrušit seskupený primární klíč, větší data, studená cache

Zamítáme. Shození indexů pět ze sedmi krátkých dotazů nepohne a zbylé dva zvedne pod mez. Zrušení seskupeného klíče je jediný zásah, který dosáhne i na IS 1 a IS 3–6, a koupí jen průchod tabulkou: Person má 11 tisíc řádků (jednotky ms), Message 3,6 milionu — přesně průchod, který IS 2 a IS 7 bez indexu dělaly, 50–150 ms, stále pod mez; cizí klíče `constraints.sql` na primárních klíčích stojí, takže by šly první, a seskupený klíč nad Message SF 1 se staví minuty. Větší data (SF 3, SF 10) rostou lineárně u průchodů a logaritmicky u vyhledání (zamítnuto už v 141). Studená cache před každým během (`DBCC DROPCLEANBUFFERS`) dá IS 1 70–180 ms, ale měří disk a překlad plánu. Všem těmto zásahům je společné, že mění databázi, nad kterou byly dotazy validovány (117), a přičítají každému frameworku touž konstantu: Advisor i matice srovnávají frameworky nad týmž SQL, databázová část času je pro všechny kandidáty stejná a rozdíl bydlí v klientovi (mapování, materializace, driver). Pomalejší databáze rozlišení srovnání snižuje, opakování ho zvyšuje.

### 2 — Stropy `deploy.resources.limits`, priorita procesů, `nice`, `cpu_shares`

Zamítáme. Strop chrání hostitele před kontejnerem, ne kontejner před hostitelem ani kontejnery mezi sebou; priorita a váhy nepůsobí na škálování frekvence, které je hlavní příčinou.

### 3 — Paralelismus povolit (`max degree of parallelism` 4), aby střední dotazy běžely rychleji

Zamítáme. Virtuální stroj má čtyři procesory: databáze se čtyřmi vlákny buď sdílí jádra s měřicím klientem (vrací se rušení, které `cpuset` odstranil), nebo stroj vezme všech osm vláken hostitele a „čtyři" jsou sourozenci SMT zaneprázdněných jader, ne čtyři jádra. Paralelismus dělá z volby plánu proměnnou: plánovač rozhoduje nad prahem ceny, který šest mírně odlišných textů téhož dotazu od šesti frameworků překročí nebo nepřekročí, a matice by měřila práh plánovače, ne mapovací vrstvu; paralelní plány jsou samy šumnější (výměny, nerovnoměrná práce vláken, plánování vláken ve stroji) a čtyři zaneprázdněná vlákna na 15W čipu škrtí i pod základní frekvenci. Zisk je malý: pomáhá středním a dlouhým dotazům, krátké zůstanou na 2–4 ms.

### 4 — Studený start odstranit zahřátím v kontejneru (`init-db.sh` přečte všechny tabulky)

Zamítáme. Zahřálo by celou databázi, která se do 4 GB vedle WideWorldImporters vejít nemusí a vytlačila by se sama, nezahřálo by plán ani klienta a zdrželo by každý start včetně profilu `test`. Nic by neskrylo, co měření neskryje samo: první framework přečte stránky dotazu jednou a dalších pět je už najde v paměti, protože čtou táž data.

### 5 — Politika jako parametr požadavku

Zamítáme. Čísla z běhů s různou politikou se nesrovnávají; hodnoty jsou součást metodiky a běh je vykáže.

### 6 — Zafixovat prostředí ve třech vrstvách, politiku vyslovit v jednom místě a spodní mez okna nechat opakování

## Rozhodnutí

**Volíme variantu 6.**

**Prostředí.** Měřicí prostředí je zafixované ve třech vrstvách, které nástroj nese: obraz databáze (`database/init-db.sh`) nastaví SQL Serveru při každém startu `max server memory (MB)` = 4096 a `max degree of parallelism` = 1; `docker-compose.yml` přiděluje databázím (`mssql_db`, `test_db`) procesory 0–1 a všem klientům (`ormconvertor`, `tests`, `test_app`, `test_app_ldbc`, `java_tests`) procesory 2–3; hostitel Dockeru dává virtuálnímu stroji pevné čtyři procesory a 8 GB paměti bez swapu (na WSL2 soubor `.wslconfig`). Zrychlení procesoru (turbo, boost) hostitele je mimo nástroj: může časy ovlivnit, nástroj ho nenastaví ani nezjistí, a záznam měření (rozh. 095) říká, zda bylo vypnuté — na MIS3 nejvyšší i nejnižší stav procesoru 99 % v plánu napájení (bez turba a bez náběhu z klidu) a napájení ze sítě. Záznam bez té věty je záznam z nezafixovaného hostitele. Soudce LDBC (117) ověřuje správnost, ne čas, a běží s turbem zapnutým; zafixované je jen to, co nese obraz.

**Iterační politika** (`AdvisorBenchmarking/BenchmarkPolicy`, jediné místo hodnot, běh je vrací v odpovědi jako `policy`): zahřátí, dokud se dvě po sobě jdoucí spuštění neshodnou na 10 % pomalejšího, nejvýš pět spuštění; pak měřená spuštění, dokud úhrnný čas nedosáhne 1 000 ms a nejméně tři — nejmenší počet, pro který existuje medián; krátký dotaz se tak opakuje stokrát, dlouhý běží třikrát. Vykazuje se **medián** doby jednoho spuštění (cena $c_{q,f}$ ILP) a průměr bajtů alokovaných jedním spuštěním na měřicím vlákně ($m_{q,f}$), s počtem zahřívacích a měřených spuštění u každé buňky. Běh jde po dotazech, v každém dotazu přes všechny frameworky: drift zasáhne kandidáty téhož dotazu stejně a první buňka dotazu zahřeje buffer pool dalším — zahřátí do ustálení je tím i „startovní běh": první buňka celého běhu se zahřívá déle (fond spojení, JIT, Roslyn), samostatný mechanismus nevzniká. Čísla změřená před tímto rozhodnutím (záznamy 2026-10-15 a 2026-10-17 v `ORMConvertor/README.md`) vznikla v nezafixovaném prostředí a s čísly po něm se nesrovnávají.

### Proč opakování, a ne pomalejší dotaz

Opakování je jediná páka, která nemění, co se měří: vyhledání za 2 ms zůstane vyhledáním za 2 ms a jen se sečte. Tak řeší práci pod rozlišením každý benchmarkový harness (cílový čas iterace v BenchmarkDotNet) a tak měří i driver LDBC, který jedno spuštění dotazu IS nikdy neměří — měří proud operací s mnoha dosazovanými parametry.

### Proč tři vrstvy, a ne jedna

Každá vrstva drží jinou příčinu. Pevná paměť serveru drží buffer pool: data SF 1 se do 4 GB vejdou a po zahřátí se každý běh čte z paměti; `max degree of parallelism` 1 dává jednovláknové, deterministické plány téhož tvaru pro všechny kandidáty. Oddělené procesory drží měřicí proces a server od společného jádra — jediná docker vrstva, která vzájemné rušení kontejnerů odstraní. Pevný virtuální stroj drží paměť stroje před Windows a nechává čtyři vlákna hostiteli. Cena je absolutní (4–9×) a soudce nad celou sadou se prodlouží; srovnání frameworků nad týmž dotazem, což je veličina, kterou Advisor a matice potřebují, se stane rozlišitelným, a absolutní čísla jsou „změřeno na MIS3".

### Proč medián a nejméně tři

Průměr nese každý výpadek (jedno čtení IC 14 nad SF 1 vypršelo po 30 s a ručně trvalo 2,2 s, záznam 2026-10-15); medián ho nenese a při rozptylu pod 5 % se od průměru neliší. Tři je nejmenší počet, který medián má; u dlouhého dotazu stojí tři spuštění nejvýš minuty a jediné spuštění by výpadek nepoznalo. Kde zrychlit běh beze ztráty stálosti, je tedy počet spuštění dlouhých dotazů, ne server — a ten politika drží na třech.

## Důsledky

- **Obraz databáze:** `database/init-db.sh` nastaví obě hodnoty `sp_configure` při každém startu (idempotentně, i nad existujícím svazkem); platí pro `mssql_db` i `test_db`.
- **Compose:** `cpuset` u všech sedmi služeb; předpokládá stroj s aspoň čtyřmi procesory (hostitel s méně procesory službu nespustí — řekne to Docker, ne tichý běh na sdíleném jádru).
- **Hostitel MIS3:** `.wslconfig` (4 procesory, 8 GB, bez swapu), plán napájení s nejvyšším i nejnižším stavem procesoru 99 %; popsáno v `ORMConvertor/README.md`, *Docker*.
- **Kód:** `BenchmarkPolicy` (hodnoty), `BenchmarkExecutor` (zahřátí do shody, měření do meze, medián), `BenchmarkMeasurement` a `BenchmarkMeasurementDto` nesou `MedianDurationMilliseconds`, `AllocatedBytes`, `WarmupIterations`, `Iterations`; `AdvisorRunResult` nese `Policy`; obrazovka Advisoru ukazuje medián.
- **Dokumentace:** `architecture.md` §8, `ORMConvertor/README.md` (*Docker*; záznamy časů označené jako před zafixováním a opravený procesor MIS3, dříve mylně i9-9900); položka *Iterační politika benchmarku* z `open-items.md` mizí.
