# Běh celého vstupu

- **Začátek:** 2026-10-30 10:59 UTC; kontejner sady 10:59–11:06 UTC, z toho vlastní případ 2 min 22 s
- **Strom:** `6d5d5a5`; pracovní kopie nesla navíc test běhu (`Tests/Experiment/WholeInput.cs`, `WholeInputTest.cs`) a jeho dokumentaci (`architecture.md` §6.2, `runs/README.md`, `ORMConvertor/README.md`), překladač nezměněný
- **Stroj a prostředí:** MIS3; obrazy `tests` a `test_db` profilu `test` sestavené na začátku běhu z tohoto stromu; virtuální stroj se čtyřmi procesory, 12 GB a bez swapu (rozh. [193](../decisions/193-the-docker-vm-has-twelve-gigabytes-so-that-the-limits-inside-it-add-up-to-less-than-it.md)); `test_db` (SF 0,1) a `mssql_db` (katalog `Fiben`) obě s `max server memory` 2048 MB, `MAXDOP` výchozí, procesory 0–1; kontejner sady s 6 GB na procesorech 2–3; nejvyšší i nejnižší stav procesoru 99 %, turbo vypnuté (rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md)); Windows Update pozastavené
- **Co běželo:** `Experiment/WholeInputTest` ([README](../../ORMConvertor/README.md#the-whole-input-run)): všech 607 vstupů výpisu experimentálního běhu (druhy `matrix`, `cross`, `example` včetně výňatků reálných projektů, `ldbc`, `fiben`) přeložených jeden po druhém do každého ze šesti rámců a artefakty sloučené do jednoho projektu na rámec; pak každý ze šesti projektů do každého rámce, i do sebe — 36 překladů nad sjednocením katalogů fixtury, databáze případových studií a `Fiben`, čtečky bez cache; nic se nesoudilo; čas překladu a čas katalogu v něm zvlášť (S3)

## Výsledek

Šest celých vstupů:

| Rámec | Sloučených vstupů | Odmítnutých | Jednotek | Entit | Dotazů | KB | Zahozených duplicitních artefaktů entit (s jiným textem) | Překlady sloučení s | Z toho katalog s |
|---|---|---|---|---|---|---|---|---|---|
| Dapper | 607 | 0 | 1020 | 147 | 873 | 621 | 3714 (53) | 21,1 | 1,8 |
| NHibernate | 604 | 3 | 1140 | 142 | 856 | 762 | 7416 (92) | 17,8 | 11,7 |
| EF Core | 605 | 2 | 932 | 120 | 812 | 726 | 3712 (63) | 4,3 | 0,2 |
| Hibernate | 604 | 3 | 998 | 142 | 856 | 869 | 3708 (50) | 4,4 | 0,2 |
| EclipseLink | 604 | 3 | 998 | 142 | 856 | 884 | 3708 (50) | 4,6 | 0,2 |
| MyBatis | 607 | 0 | 1167 | 147 | 873 | 993 | 7428 (76) | 3,9 | 0,1 |

Odmítnutí při sloučení jsou tři příklady stránky bez katalogu (rozh. 099), jejichž zdroj Dapper či MyBatis nenese klíč pro NHibernate, Hibernate a EclipseLink, JPetStore do EF Core (sloupec výsledku bez typu) a Keycloak do EF Core (kolekce v `IN` nativního SQL) — totéž, co ukazuje [experimentální běh](2026-10-24-experiment.md). Konflikty jmen jsou věcí příkladů stránky: `Customer`, `Product`, `Employee` a dalších 24 jmen definuje několik příkladů různě; sloučení nechá text, na kterém se shodne nejvíc vstupů.

Čas překladu v sekundách, zdroj v řádku, cíl ve sloupci, v závorce čas katalogu v něm:

| Zdroj \ Cíl | Dapper | NHibernate | EF Core | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| **Dapper** | 4,6 (2,7) | 9,9 (7,7) | 3,4 (1,4) | 2,2 (0,3) | 2,0 (0,2) | 2,1 (0,5) |
| **NHibernate** | 1,3 (0,0) | 4,9 (3,3) | 1,5 (0,1) | 1,4 (0,2) | 1,4 (0,1) | 1,5 (0,2) |
| **EF Core** | 2,5 (0,0) | 5,4 (2,8) | 2,6 (0,1) | 2,6 (0,1) | 2,6 (0,1) | 2,4 (0,1) |
| **Hibernate** | 1,0 (0,0) | 3,2 (2,0) | 1,2 (0,1) | 1,2 (0,1) | 1,2 (0,2) | 1,1 (0,1) |
| **EclipseLink** | 1,0 (0,0) | 3,5 (2,2) | 1,3 (0,2) | 1,4 (0,1) | 1,3 (0,1) | 1,2 (0,1) |
| **MyBatis** | 1,2 (0,1) | 1,8 (0,2) | 1,6 (0,2) | 1,6 (0,2) | 1,6 (0,2) | 1,4 (0,2) |

Alokovaná paměť 310–716 MB na překlad, nejvíc Dapper → NHibernate. Každý z 36 překladů se vrátil.

**Proti S3** (30 s pro 100 entit a 100 dotazů): největší celý vstup má 147 entit a 873 dotazů. Nejpomalejší překlad, Dapper → NHibernate, trval 9,9 s, z toho 7,7 s čtení katalogu (cíl NHibernate žádá klíč každé entity a zdroj Dapper ho nenese, takže se katalog ptá na každou tabulku; čtečky bez cache). Bez času katalogu je nejpomalejší EF Core → NHibernate s 2,6 s, tedy desetina meze pro vstup s devítinásobkem dotazů, který mez vymezuje. Proti [běhu mezí](2026-10-29-limits.md) leží celý vstup hluboko pod každým stropem řady: řady `entities` dosáhly 800 entit cestou a 12 800 překladem, `conditions` 4096 podmínek — projekt z veškerých testovacích dat nástroje je řádově menší než body, na kterých překlad teprve přestává stačit.

**Stručná kontrola výstupu** (`whole-input-reasons.txt` běhu, mimo git): záznamy `Failure` po zdrojích — NHibernate a Hibernate kolem 30 na překlad, EF Core kolem 200, Dapper a MyBatis 225–275, EclipseLink 332–347 (všechny tyto počty jsou záznamy nad 812–873 dotazy). Důvody nejsou věcí velikosti, ale řetězení — nástroj čte svůj vlastní výstup:

| Zjištění | Kde | Čím |
|---|---|---|
| parametr dotazu nelze typovat (`The scalar of the parameter … does not follow from what it is compared against`) | zdroje Dapper a MyBatis, přes 200 dotazů; v menším u ostatních | artefakt Dapperu a MyBatisu nenese vazbu třídy na tabulku ani vlastnosti na sloupec (ztráta `ColumnName`, `TableName` zaznamenaná při prvním překladu), takže u výňatků reálných projektů se jmény sloupců `snake_case` dotaz zpět na entitu nenavážeš; totéž postihne každý projekt Dapperu, kde se jména liší |
| čárkou oddělené entity v JPQL (`Comma-separated entity references are a cross join…`) | zdroj EclipseLink, 302 dotazů | builder EclipseLinku píše join bez asociace jako výčet kořenů s podmínkou ve `WHERE`, vlastní parser JPQL tento tvar odmítá — artefakt nástroje pro EclipseLink se nástrojem nepřečte zpět; Hibernate píše `JOIN … ON` a čte se |
| SQL nástroje se nedá přečíst (`Incorrect syntax near 'User'`) | zdroje Dapper a MyBatis, 9 dotazů příkladu Cofoundry | builder SQL píše tabulku `User` bez uvozovek; `USER` je vyhrazené slovo T-SQL, dotaz by neprošel ani serverem |
| 54 entit bez klíče do NHibernate, Hibernate a EclipseLink | zdroj Dapper | entity příkladů stránky, jejichž tabulky nejsou v žádném katalogu běhu (rozh. 099) |
| predikáty, které strom podmínek nenese, `AVG` bez čísla, `SelectMany` se `Select` | zdroj EF Core, přes 100 dotazů | dotazy LINQ, které EF Core zapsal pro dotazy katalogu FIBEN a matice; vyslovené meze reprezentace, které první překlad do EF Core nemá jak ohlásit |

## Třídění

| Zjištění | Kam |
|---|---|
| odstup celého vstupu od meze S3 a od stropů řad | věta textu práce; řádek S3 v `traceability.md` |
| artefakt EclipseLinku se vlastním parserem nepřečte zpět; `User` bez uvozovek v SQL | otevřené položky — vady k rozhodnutí, zda je zpětná čitelnost vlastního výstupu nárok |
| vazba třídy na tabulku ztracená v Dapperu a MyBatisu | známá ztráta zdroje bez mapování; nic nového |

## Poznámky

- Harness běhu a výstupy (`whole-input-rows.tsv`, `whole-input-reasons.txt`, `whole-input-merge.log`, šest vstupů) zůstávají mimo git (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)); šest vstupů zůstává i ve svazku `ormconvertor_whole_input`.
- Katalog nese většinu času sloučení do NHibernate (11,7 s z 17,8 s) i překladu Dapper → NHibernate (7,7 s z 9,9 s): cíl NHibernate žádá klíč každé entity, zdroj Dapper ho nenese, a běh čte sjednocení tří databází čtečkami bez cache; nástroj čas katalogu hlásí zvlášť, jak S3 žádá.
