# 149 — FIBEN jako třetí referenční doména: katalog 237 dotazů s otázkou v přirozeném jazyce, který neběží při každém sestavení, nýbrž na vyžádání pro každou verzi; soudce vzniká jednou z prvního doloženého běhu a každý další běh se s ním srovnává a měří se jako LDBC

Datum: 2026-10-18
Stav: platí; první podmínku odstavce *Soudce* nahrazuje 157; místo záznamu v odstavci *Co se měří* a v důsledku *Záznam ke každé verzi* nahrazuje 167
Požadavky: T1, T2, T3, T4, T5, F4, F6, F11, S2, S5, S6
Podklad: repozitář `IBM/fiben-benchmark` (Apache-2.0; poslední změna 2020-12-07, archivovaný 2024-07-22): `FIBEN.sql`, `FIBEN_Queries.json`, `tablelist.txt`, `data.zip`, `db2loaddata.sh`; J. Sen et al., *ATHENA++: Natural Language Querying for Complex Nested SQL Queries*, PVLDB 13(11), 2020; W. Kim, *On optimizing an SQL-like nested query*, ACM TODS 7(3), 1982 (taxonomie vnořených dotazů N/A/J/JA); rozhodnutí [016](016-generated-artifact-verification-levels.md), [042](042-measured-benchmark-output-out-of-git.md), [078](078-java-suite-as-a-client-of-a-running-instance.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [098](098-the-number-is-decided-once-per-release.md), [099](099-examples-are-content-not-a-choice.md), [110](110-ldbc-snb-as-a-second-reference-domain.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md), [141](141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md), [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), [146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md); měření z 2026-10-18 na MIS3 (třída `LdbcCatalogTest` nad LocalDB 30 s pro 41 dotazů, celá .NET sada 3 min 9 s); [`open-items.md`](../open-items.md), položky *Experimentální pipeline*, *Referenční databáze experimentů*, *Meze složitosti* a *Metodika srovnání s LLM*; [`architecture.md`](../architecture.md) §6.2 a §9

## Kontext

Rozhodnutí 110 dalo nástroji první měřítko, které si nevybral: 41 čtecích dotazů LDBC Social Network Benchmark, a rozhodnutí 117 k nim nezávislého soudce. Katalog je dnes přeložený celý a doložený celou validační sadou ve všech šesti frameworcích. Co po něm zůstává, jsou tři mezery, všechny zapsané v [`open-items.md`](../open-items.md):

- **Matice T2 má od každého tvaru jeden exemplář.** Kategorie `SubqueryAsTheRightSideOfIn`, `CorrelatedExistsWithAJoin` nebo `ScalarSubqueryAgainstABoundValue` mají po jednom dotazu nad fixturou pěti entit, katalog LDBC jich přidává několik. Podíly T3 (parsovatelné, kompilovatelné, spustitelné, funkčně ekvivalentní) se nad jedním exemplářem počítat nedají; a právě u vnořených dotazů se nástroj naposledy zlomil (BI 16: typ parametru porovnaného se skalárním poddotazem).
- **Příprava T4 a T5 nemá vstup, který by nenapsal autor nástroje.** Zero-shot, few-shot a RAG varianta potřebují vedle zdrojového kódu *záměr* dotazu — co má vrátit, řečeno jinak než kódem —, jinak LLM překládá text, ne úlohu. Dotazy matic i katalogu LDBC mají jen název a náš text.
- **Katalog instance byl dosud úzký.** WideWorldImporters i LDBC mají desítky tabulek s klíči, které jsme znali předem. Doplnění z katalogu (rozhodnutí 015) nad širokým schématem, kde vztahy stojí jen v deklaracích cizích klíčů a jména nesou cizí konvenci, nikdo neměřil.

**FIBEN** (*Finance Benchmark*, IBM Research) je benchmark dotazování v přirozeném jazyce, ke kterému byl vyvinut systém ATHENA++. Jeho data emulují finanční datový sklad nad ontologiemi FIBO a FRO: veřejné společnosti, jejich finanční výkazy podle XBRL, cenné papíry, účty, držení a transakce. Co obsahuje, jsme ověřili nad staženými soubory, ne z README:

| | FIBEN |
|---|---|
| schéma | 152 tabulek, 159 deklarovaných cizích klíčů, každý primární klíč jediný sloupec `BIGINT`; typy `BIGINT`, `VARCHAR(1024)`, `DOUBLE PRECISION`, `DATE`, `TIMESTAMP`, `CHARACTER(5)`; žádný index mimo klíče, žádná sekvence ani pohled; DDL pro Db2 a PostgreSQL |
| data | `data.zip` 80 MB, 400 MB CSV; největší tabulky `ELEMENTSOFFINANCIALREPORT` 137 MB, `ELEMENTOFFINANCIALSTATEMENT` 82 MB, `SECURITIESTRANSACTION` 48 MB, `DISCLOSURE` 39 MB, `HOLDING` 37 MB |
| dotazy | 300 otázek v přirozeném jazyce, z toho 63 parafrází; 237 různých dotazů SQL: 130 jednoblokových, 170 vnořených v Kimově taxonomii (28 typu N, 64 typu A, 40 typu J, 38 typu JA) |
| konstrukce | jen vnitřní joiny nad rovnostmi; `GROUP BY` a `HAVING` se `SUM`, `AVG`, `MIN`, `MAX`, `COUNT`; poddotazy v `IN`, `NOT IN` a za porovnáním s agregátem v `HAVING`; několik `UNION`; `FETCH FIRST n ROWS ONLY`; bez vnějšího joinu, `DISTINCT`, `LIKE`, `CASE`, okenní funkce a stránkování |
| dialekt | Db2: identifikátory v uvozovkách velkými písmeny pod schématem `FIBEN`, `FETCH FIRST`, `1.0E6` |
| soudce | žádný — benchmark vydává otázku a SQL, ne očekávaný výsledek |
| licence | Apache-2.0, včetně dat |

Je to tedy doplněk LDBC, ne jeho náhrada: užší do šířky (žádný složený klíč, žádný vnější join, žádné cesty), hlubší do zanoření, a jediný, který k dotazu říká lidskou řečí, co má vrátit.

**Co stojí běh takového katalogu, jsme změřili.** Třída `LdbcCatalogTest` — překlad každého dotazu do pěti cílů s katalogem (první stupeň), kompilace artefaktů EF Core a NHibernate (druhý stupeň) a spuštění každého textu nad prázdnými tabulkami — trvá nad LocalDB na MIS3 30 s pro 41 dotazů: 9 s překlad 205 buněk, 19 s kompilace 82 artefaktů, 2 s texty. Celá .NET sada trvá 3 min 9 s. Přepočteno na 237 dotazů je to asi 3 minuty navíc v .NET sadě a odhadem 6–25 minut v javové (711 buněk, každá jeden `javac` a vytvoření dotazu; továrna JPA se drží na doménu). Čtvrtý stupeň nad načtenými daty — asi 1 400 spuštění pětitabulkových joinů s agregací nad miliony řádků bez indexů — nemá žádné změřené číslo a než se data načtou a každý text změří jako okno rozhodnutí 140, je to kdekoli mezi deseti minutami a dvěma hodinami na sadu.

Rozhodnout je třeba šest věcí: **který benchmark**, **kde poběží**, **kdo ho soudí**, **kde data bydlí**, **jak se katalog píše** a **co se měří**.

## Zvažované varianty

**Který benchmark.** Druhá populace dotazů, kterou nenapsal autor nástroje.

1. **Žádná — LDBC stačí.** Zamítáme: 41 dotazů dává od každé kategorie jednotky exemplářů, podíly T3 nad nimi nemají váhu a k žádnému dotazu nepatří záměr řečený jinak než kódem.
2. **Spider nebo BIRD** (benchmarky text-to-SQL s mnoha databázemi). Zamítáme: stovky malých schémat o několika tabulkách v dialektu SQLite s daty na ukázku; každé schéma by bylo nový katalog a dotazy jsou krátké. To, co nás zajímá — hluboké zanoření nad jedním schématem s daty —, tam není.
3. **TPC-H, Join Order Benchmark.** Zamítáme: 22, resp. 113 dotazů bez otázky v přirozeném jazyce; TPC-H má 8 tabulek, JOB data IMDb s licencí, která pro redistribuci nestačí.
4. **FIBEN.** Tuhle volíme: jedno schéma, 237 dotazů se zveřejněnou taxonomií zanoření, otázka v přirozeném jazyce ke každému, data s licencí, která dovoluje nést je v obrazu, a autor, který s nástrojem nemá nic společného.

**Kde poběží.** Rozhodnutí 110 dalo katalog LDBC do sad, které běží při každém sestavení, a stav každého dotazu drží test.

1. **Do sad jako LDBC.** Prvních tři stupně v obou sadách při každém sestavení, čtvrtý v samostatném běhu jako soudce LDBC. Zamítáme, a ne kvůli třem minutám. Sady drží **invarianty nástroje** — co tvrdí §9 o každém commitu. Stav 237 dotazů není invariant: je to **míra verze**, číslo do tabulky T2 × T3, které se mění s každou změnou parseru a jehož smysl je právě v tom, že se čte po verzích vedle sebe. Test, který po každé změně překladu žádá přepsat 237 stavů, by katalog buď zmrazil (stav by se neposouval, aby test zůstal zelený), nebo by se vypnul. A čtvrtý stupeň nad načtenými daty do sestavení nepatří tak jako tak (rozhodnutí 117).
2. **Běh na vyžádání pro každou verzi nebo zásadní změnu**, obě sady po sobě, pojmenovaný a odpojený jako soudce LDBC, s výsledkem zapsaným k verzi. Tuhle volíme. Při sestavení neběží nic: katalog se neověřuje testem, ověřuje ho běh, který začíná kontrolou, že katalog je úplný a každý dotaz se vyslovuje celý.

**Kdo soudí.** FIBEN žádný očekávaný výsledek nevydává.

1. **Jen diferenční ověření** (rozhodnutí 089): zdroj proti každému cíli nad týmiž daty. Nutné, ale nestačí: porovnává cíl se zdrojem a neví, zda zdroj čte otázku správně — tatáž mezera, kterou 117 pojmenovalo u LDBC. A hlavně neříká nic **mezi verzemi**: dvě verze, které se shodnou samy se sebou, se mohly lišit navzájem.
2. **Očekávané výsledky napsat ručně.** Zamítáme: 237 agregací nad miliony řádků nikdo ručně nespočítá.
3. **Vzít výsledky z referenčního běhu nad Db2 nebo PostgreSQL.** Zamítáme: žádný z nich nemáme a soudil by text Db2, ne náš text v T-SQL nad SQL Serverem; rozdíl v kolaci nebo v zaokrouhlení by byl rozdílem systémů, ne překladu.
4. **Vlastní soudce vytvořený jednou.** První běh, který **doložíme** — texty v T-SQL přečtené proti otázce v přirozeném jazyce, všech šest frameworků ve shodě nad týmiž daty (varianta 1 jako podmínka vzniku), vzorek dotazů přepočítaný ručně nad podmnožinou dat —, zapíše výsledek každého dotazu v kanonickém tvaru rozhodnutí 089 a ten se zmrazí. Každý další běh se srovnává s ním. Tuhle volíme. Je to **regresní soudce**: tvrdí, že verze vrací, co vrátila první doložená verze, a mění se jen novým rozhodnutím — opravou textu, který četl otázku špatně, nebo změnou dat —, s datem a důvodem. Není to nezávislá pravda jako validační sada LDBC a §9 to řekne.

**Kde soudce bydlí.** Validační sadu LDBC nese obraz databáze v tabulce vedle dat, obě sady ji čtou odtud (117). Soudce FIBEN jde touž cestou: soubor v repozitáři pod `database/fiben/`, načtený při prvním startu do tabulky databáze `Fiben` vedle dat, které soudí; obě sady čtou z databáze, žádný nový mechanismus sdílení souboru mezi ekosystémy. Rozhodnutí 042 vyloučilo z gitu *naměřený výstup*; soudce měřením není, je to referenční artefakt téhož druhu jako validační sada — zmrazený, datovaný a s důvodem každé změny.

**Kde data bydlí.** Tři cesty rozhodnutí 110 platí beze změny: schéma uvnitř WideWorldImporters míchá domény, samostatný profil zdvojuje server, testovací databáze by data načítala při každém běhu. Volíme **třetí databázi `Fiben` v kontejneru `mssql_db`** vedle WideWorldImporters a `LdbcSnb`, s archivem staženým při sestavení obrazu, načtením při prvním startu svazku, rozšířenou vlastností s jménem archivu a přepínačem `FIBEN_DATASET=none`. Do `test_db` profilu `test` data **nejdou**: sady při sestavení katalog nepotřebují a profil by jen těžkl. Běh na vyžádání jde proti `mssql_db` systémového profilu, jako soudce LDBC nad celou sadou.

**Jak se katalog píše.** Dotazy jsou v dialektu Db2 a každý jmenuje tabulky, které čte.

1. **Texty převést skriptem jednou a nést je v katalogu** (`SampleData`, jako `LdbcSnbSample`): schéma `FIBEN."X"` na `{{schema}}.[X]`, `FETCH FIRST n ROWS ONLY` na `TOP n`, zbytek je T-SQL. Katalog nese ke každému dotazu klíč, otázku v přirozeném jazyce (parafráze jako další otázky téhož dotazu), Kimův typ, text v T-SQL a stav podle 110 (přeložený podle benchmarku, se zjednodušením, nepřeložený) s poznámkou. Tuhle volíme. Text po převodu je **náš** text (jako u LDBC, kde jsme texty psali podle definic), a to je důvod, proč soudce vzniká doloženým během a ne převzetím.
2. **Každý převod dostane jen entity, které dotaz čte**, ne všech 152. Změřený důvod: katalog LDBC posílá s každým dotazem všech 17 entit a buňka stojí desetiny sekundy; 152 tříd parsovaných, doplněných a kompilovaných s každou z 1 185 buněk by tři minuty změnilo na patnáct až třicet. Tabulky dotazu jsou v jeho textu, katalog je nese u dotazu.

**Co se měří.** Jako u LDBC: každé čtení běhu je v zafixovaném prostředí rozhodnutí 145 měřeno po frameworcích iterační politikou téhož rozhodnutí (zahřátí do ustálení, medián, nejméně tři spuštění) s jednou mezí času na spuštění (146). Záznam běhu — verze a commit, datum, věta o prostředí, stupeň dosažený každým dotazem v každém frameworku, shoda se soudcem, medián času čtení — jde do `ORMConvertor/README.md` vedle záznamů LDBC, jen souhrn; řádky po dotazech zůstávají ve výstupu běhu mimo git (042).

## Rozhodnutí

> **Nástroj se zkouší také nad benchmarkem FIBEN. Jeho data nese třetí databáze `Fiben` kontejneru `mssql_db`, načtená z archivu při prvním startu jako `LdbcSnb`; katalog nese všech 237 dotazů převedených do T-SQL, ke každému otázku v přirozeném jazyce, Kimův typ, tabulky, které čte, a stav podle rozhodnutí 110. Katalog neběží při žádném sestavení: běží na vyžádání pro každou verzi nebo zásadní změnu, obě sady po sobě, ve všech čtyřech stupních rozhodnutí 016 a v zafixovaném prostředí rozhodnutí 145. Čtvrtý stupeň soudí soudce vytvořený jednou z prvního doloženého běhu a zmrazený v repozitáři; každý další běh se s ním srovnává a svůj záznam zapisuje k verzi.**

**Katalog je obsah, hranice je rozhodnutí** — ve smyslu rozhodnutí 099 a 110. Posunout stav dotazu, opravit text nebo doplnit poznámku je práce; rozhodnutím zůstává, že katalog nese všech 237 dotazů, žádný nevynechává, a že ke každému patří otázka, kterou nenapsal autor nástroje.

**Zdrojem je Dapper**, jako u LDBC: text je SQL, Dapper netvrdí klíč, vztah ani typ, všechno bere z katalogu `Fiben`. Entitní jednotky jsou třídy Dapper nad tabulkami FIBEN, generované z DDL jednou a nesené v `SampleData`; dotazu se posílají jen ty, které čte.

**Běh na vyžádání** je test obou sad vázaný na proměnnou prostředí, která jmenuje databázi `Fiben` (a pro javovou sadu instanci s katalogem `Fiben`, rozhodnutí 078); bez ní se přeskakuje, s ní se nesmí přeskočit. Spouští se pojmenovaný a odpojený, jak to pro soudce LDBC popisuje `ORMConvertor/README.md`, a jde v pořadí: úplnost katalogu → překlad do pěti cílů s katalogem → kompilace → spuštění zdroje proti soudci → spuštění každého cíle proti soudci → měření. Neshoda zdroje se soudcem je chyba textu nebo změna, kterou je třeba doložit novým soudcem; shoda zdroje a neshoda cíle je chyba překladu (117).

**Soudce** vzniká z prvního běhu, který splní tři podmínky: zdroj i všech pět cílů se nad daty shodnou, texty jsou přečtené proti otázkám, a vzorek dotazů všech čtyř Kimových typů je přepočítaný ručně nad podmnožinou dat. Zapisuje se v kanonickém tvaru 089 do `database/fiben/` s datem a commitem, loader ho nahrává do tabulky vedle dat. Nový soudce vzniká jen novým rozhodnutím.

**Co se netvrdí.** Nejde o běh benchmarku FIBEN ani o jeho účel: nástroj nedotazuje přirozeným jazykem, otázka je u dotazu jako záměr pro T4 a T5 a pro čtenáře. FIBEN není aplikace, T1 tím nemění čtení. A soudce není nezávislý: §9 u FIBEN řekne, že čtvrtý stupeň je shoda s první doloženou verzí, ne s pravdou napsanou jinde.

## Důsledky

- **Podíly T3 dostávají populaci.** 237 dotazů, 170 vnořených ve čtyřech typech, v šesti frameworcích — a tabulka, která se čte po verzích vedle sebe. Pokrytí vnořených tvarů matice T2 se nad nimi dá změřit, ne odhadnout.
- **T4 a T5 mají vstup se záměrem.** Ke každému dotazu je otázka, kterou lze dát LLM vedle kódu; parafráze dávají míru stálosti výsledku vůči formulaci.
- **Obraz databáze těžkne** o 80 MB stažených při sestavení a asi minutu načítání při prvním startu svazku; `FIBEN_DATASET=none` vrací dřívější stav. Profil `test` se nemění.
- **REST kontrakt přibírá čtecí koncový bod `GET /fiben`** (MINOR podle 041), protože javová sada bere katalog z instance (078); stránka frontendu ho ukazuje jako `ldbc.html` katalog LDBC.
- **Katalog instance doplňuje 152 tabulek.** Co to stojí (čtení katalogu, S3) se změří v běhu; jména tabulek FIBEN se nesmí potkat s WideWorldImporters ani LDBC v jednom katalogu — jsou v jiné databázi, takže ne.
- **Záznam ke každé verzi.** Značka verze (098) odkazuje na záznam běhu v `ORMConvertor/README.md`; verze bez běhu to říká.
- **Otevírá práci, kterou toto rozhodnutí nedělá** a která je v [`open-items.md`](../open-items.md): převod a katalog, loader, koncový bod a stránka, běh v obou sadách, první doložený běh a soudce, záznam.
