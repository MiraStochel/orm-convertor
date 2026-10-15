# 140 — Dotazy katalogu LDBC na cesty píše prohledání do šířky rozepsané do nerekurzivních definic s hloubkou rovnou změřenému průměru dat; čas běhu normalizuje volba dat, ne text

Datum: 2026-10-15
Stav: platí
Požadavky: T1, T2, T3, F11, F13
Podklad: [`analysis/recursion-and-its-limit.md`](../analysis/recursion-and-its-limit.md) (oddíly 5 a 6, měření nad `LdbcSnb` SF 1 z 2026-10-15); rozhodnutí [110](110-ldbc-snb-as-a-second-reference-domain.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) (odstavec *Co rekurze v SQL Serveru neumí*), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md), [121](121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md), [122](122-the-text-of-a-catalog-query-takes-the-shape-a-targets-implementation-needs-under-the-same-criterion.md); běh soudce nad celou sadou SF 1 (`ORMConvertor/README.md`, *Whole-set runs*)

## Kontext

Katalog LDBC nese 41 čtecích dotazů, z nich pět se zjednodušením: IC 13, IC 14, BI 15, BI 19 a BI 20 hledají cestu po `knows` a text je omezuje mezí hloubky — dvě procházky po dvou krocích z obou konců, u IC 14 jedna do tří. Důvod je v rekurzivním členu SQL Serveru (113): nesmí slučovat řádky ani se dívat na dřívější úrovně, takže vyjmenovává sledy, a sledů je stupeň<sup>*k*</sup>. Nad SF 1 (průměrný stupeň 39,4) je to od příkladové osoby 910 tisíc sledů ve třetím kroku a 95 milionů ve čtvrtém — pro 26 nových osob. Rozh. 113 i 122 zamítly „rekurzi rozepsanou do pevného počtu joinů" jako tvar textu katalogu, protože se tím dotaz vzdává konstrukce, kterou jazyk má.

**Nový cíl: každý dotaz katalogu má na MIS3 běžet mezi 0,2 s a 300 s**, aby měření T2 a T3 mělo smysl v obou mezích — krátké běhy se utápějí v režii, dlouhé nedovolí opakování. Pákou má být volba dat (scale factor, popřípadě upravená data, sady dotazů podle databáze), ne úprava textu kvůli času. Dnes se o času ví jen z běhu soudce (IC 14 1,2 s, IC 9 0,9 s, IC 3 0,65 s na čtení) a z rozh. 122 (BI 15 asi 49 s, BI 19 nedoběhne do 500 s); z pěti dotazů na cesty porušují okno BI 15 a BI 19, kvůli plánovači nad definicí vah, ne kvůli rekurzi.

Analýza změřila dvě věci, které volbu zužují. **Žádná data neumožní text bez meze:** na cyklickém grafu rekurzivní `WITH` buď běží do limitu a skončí chybou, nebo s hlídáním cyklu vyjmenuje všechny jednoduché cesty, a těch není omezeně mnoho s průměrem grafu; menší SF nebo prořídlé hrany mění konstantu, ne tvar. **Mez čtyř kroků nad SF 1 není přesná:** ve vzorku 199 osob leží 55 z 1,82 milionu dvojic ve vzdálenosti 5 a dvanáct osob má excentricitu 5; že soudce IC 13 na všech 6 818 čteních souhlasí, je vlastnost validační sady, ne textu.

## Zvažované varianty

### 1 — Dnešní texty s mezí, přesnost doložit změřeným průměrem dat

Beze změny textu; změří se průměr komponenty `knows` a mez se zvedne tak, aby ho dosáhla (3 + 3 z obou konců). Zamítáme: sledy rostou se stupněm na třetí, takže větší data — která okno 0,2 s pro ostatní dotazy vyžaduje — zvedají cenu právě tam, kde mez zvedáme; vážené dotazy navíc mezí v krocích přicházejí o levnější cestu s víc hranami, což průměr nezachytí; a stav *zjednodušený* by zůstal, jen lépe doložený.

### 2 — Uložená procedura nebo tabulková funkce se smyčkou

Prohledání do šířky nad dočasnou tabulkou je bez meze a lineární v hranách (tak jsou změřena čísla analýzy). Zamítáme: tělo se nepřekládá, dotaz by byl volání a matice by měřila obal kolem jednoho `EXEC` — týž důvod, kterým 022 a 113 odmítly nativní SQL tam, kde jazyk cíle konstrukci má; k tomu nová konstrukce slovníku, kterou čtečka T-SQL odmítá (108).

### 3 — Graf SQL Serveru, `MATCH(SHORTEST_PATH(…))`

Ověřeno nad kopií `knows` SF 1: IC 13 přesně a bez meze. Zamítáme: vrací jednu cestu na koncovou osobu (IC 14 chce všechny) a váhy nezná (BI 15, 19, 20); žádný z šesti frameworků `MATCH` nevysloví, takže by šel všude nativním SQL; vyžaduje tabulky uzlů a hran vedle relačních.

### 4 — Jiná databázová platforma se sloučením mezi úrovněmi

PostgreSQL (`UNION` bez `ALL`, `CYCLE`) nebo DuckDB (`USING KEY`) napíší IC 13 přesně jedním rekurzivním dotazem. Zamítáme: druhý dialekt vylučuje rozh. [129](129-sql-server-2022-stays-the-only-database-dialect.md) jako vymezení předmětu práce; zůstává jako argument do textu práce, proč SQL Server zjednodušení vynucuje.

### 5 — Upravit data tak, aby rekurzivní text doběhl

Zamítáme z důvodu v kontextu: nejde to na žádném grafu s cykly a netriviálním stupněm, a upravená data by vzala soudce všem devatenácti dotazům Interactive, protože data sdílejí.

### 6 — Prohledání do šířky rozepsané do nerekurzivních definic, hloubka z dat

## Rozhodnutí

**Volíme variantu 6. Pět dotazů katalogu na cesty — IC 13, IC 14, BI 15, BI 19, BI 20 — píše prohledání do šířky jako řetěz nerekurzivních definic `L0 … Ld`: úroveň *k* + 1 jsou sousedé úrovně *k* bez osob úrovní *k* a *k* − 1 (`DISTINCT`, `NOT EXISTS`), vážené dotazy týmž řetězem jako Bellman–Ford, kde úroveň nese pro každou osobu nejlevnější cenu sledu o nejvýš *k* hranách. Hloubka *d* je parametr data setu, nad kterým katalog běží: změřený průměr komponenty `knows` pro nevážené dotazy a změřený největší počet hran nejlevnější cesty pro vážené; zapisuje se k data setu a k dotazu, ne hádá. Text je jeden `SELECT`, roste lineárně s hranami a s *d* aspoň rovným změřené hodnotě je nad těmi daty přesný; takový dotaz katalog vede jako *podle specifikace*, s hloubkou uvedenou v poznámce. IC 12 zůstává rekurzivní: hierarchie tříd tagů je strom. Čas běhu v okně 0,2–300 s zajišťuje volba dat — scale factor pro dotazy, které procházejí tabulky, počet opakování v měření pro dotazy, které vyhledávají přes index —, nikdy úprava textu kvůli času.**

### Proč to není „rekurze rozepsaná do joinů", kterou 113 a 122 zamítají

Zamítnutý tvar nahrazoval rekurzi pevným počtem joinů, který vyjmenovává tytéž sledy jako rekurzivní člen, jen bez konstrukce, kterou jazyk má — ztráta bez zisku. Tenhle tvar je **jiný algoritmus**: každá úroveň slučuje a vyřazuje, což rekurzivní člen SQL Serveru zakazuje, a právě proto je lineární místo exponenciálního. Rekurze tu není co rozepisovat; rekurzivní `WITH` SQL Serveru nejkratší cestu nad cyklickým grafem nevysloví (6.2 analýzy), a tvar, který ji vysloví, konstrukci jazyka neobchází, protože žádná není. Katalog tím o rekurzi nepřichází: IC 12 ji nese, kategorie T2 `RecursiveDescentOfAHierarchy` a `RecursiveWalkOfACyclicGraph` také. Kritérium 122 (b) — tvar, jaký by referenční implementace mohla napsat — se tu doplňuje: referenční implementace LDBC stojí na platformách, které slučování mezi úrovněmi mají, a tenhle text je nejbližší tvar téhož nad platformou, která ho nemá.

### Proč hloubka z dat, a ne pevná

Pevná hloubka je mez, tedy zjednodušení, jen levnější. Hloubka rovná změřené vlastnosti dat je dokazatelná přesnost nad těmi daty: dvojice dál než průměr neexistuje. Cena je lineární v hranách pro každou úroveň, takže *d* = 6 místo 4 stojí o dvě úrovně víc, ne o stupeň na druhou; proto si přesnost můžeme dovolit. Měření průměru je jeden skript (analýza, oddíl 7) a opakuje se pro každý data set, který katalog ponese.

### Proč text nikdy neohýbat kvůli času

Rozh. 121 a 122 dovolují textu tvar, který plánovač nebo implementace cíle **potřebuje**, aby dotaz vůbec běžel správně; okno 0,2–300 s je mez měření, ne správnosti. Text upravený kvůli času by měřil něco jiného než text, který by napsal vývojář, a matice T2 by to nerozlišila. Data mají ten rozdíl v sobě: větší data zpomalí průchod tabulkou lineárně a vyhledání přes index skoro vůbec — proto spodní mez u vyhledávacích dotazů nedosáhne žádný scale factor a řeší ji počet opakování, který měření už má (Advisor iteruje).

### Co rozhodnutí nechává otevřené

Který data set pro kterou databázi — oficiální scale factor, nebo upravená data pro databázi jen s dotazy BI — a jestli BI 19 a BI 20 dostanou tabulku vah hran naplněnou při načtení jako datovou volbu po vzoru `RootPostId`. To rozhodne další rozhodnutí až po tabulce časů všech dotazů nad SF 1 a po změřeném průměru a stupni kandidátních SF (položka *Normalizace běhů katalogu LDBC* v `open-items.md`).

## Důsledky

- **Katalog:** pět textů v `SampleData/LdbcSnbSample.cs` přepsat podle rozhodnutí, stav `Simplified` → `AsSpecified` s hloubkou a data setem v poznámce; soudce .NET znovu nad celou sadou SF 1 pro IC 13 a IC 14; BI 15, 19, 20 ověřit alespoň proti algoritmu v testu, protože soudce BI není.
- **Nástroj:** beze změny; tvar je dnešním slovníkem přeložen (Dapper, MyBatis, Hibernate jazykem; EF Core, NHibernate, EclipseLink nativním SQL). Před přepisem opravit vadu *Konstanta bez aliasu v dalším operandu množinové operace* (`open-items.md`).
- **Dokumentace po implementaci:** `architecture.md` §6.2 (počty `AsSpecified` / `Simplified`, poznámka o `FallbackBy`), `subset.md` 1.3 (katalog) a 2.10 (řádek „mez hloubky, sledy místo vrcholů"), `traceability.md` T2; `README.md` *Guarantees*, řádek o rekurzi.
- **Rozh. 113 a 122 platí dál**; věta o zamítnutí rekurze rozepsané do pevného počtu joinů platí pro tvar, který sledy vyjmenovává, a tohle rozhodnutí vymezuje, co pod ni nespadá.
- **Měření:** tabulka časů všech dotazů nad SF 1 na MIS3 jako první krok; z ní rozdělení na průchody a vyhledání a volba data setů.
