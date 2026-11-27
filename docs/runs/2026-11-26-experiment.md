# Experimentální běh

- **Začátek:** sestavení obrazů 2026-11-26 22:28 UTC, .NET půlka 22:30 UTC, javová půlka 22:58 UTC, souhrn 23:32 UTC
- **Strom:** `ab2bbcf`; jeho `ORMConvertor/` je shodné se značkou `2.4.2` (`4b3412e`), dva commity za ní mění jen `docs/report/`
- **Stroj a prostředí:** MIS3; obrazy profilu `test` a `ormconvertor` sestavené těsně předtím z tohoto stromu, obraz databáze z 2026-11-25 (`database/` se od `802a49c` nezměnilo). `test_db` (SF 0,1) pro fixturu a databázi případových studií, `mssql_db` v měřicím režimu (procesory 0–1, `MAXDOP` 1) s `Fiben` a soudcem rozh. [170](../decisions/170-rows-tied-on-the-ordering-key-compare-as-a-set-and-a-fiben-slice-gets-a-deciding-key.md) pro druh `fiben`; instance `test_app`, `test_app_ldbc`, `test_app_cases` a `ormconvertor` nad `Fiben`. Oba servery s mezí paměti 2048 MB ([metodika](README.md#před-dlouhým-během)), virtuální stroj 4 procesory a 12 GB (rozh. [193](../decisions/193-the-docker-vm-has-twelve-gigabytes-so-that-the-limits-inside-it-add-up-to-less-than-it.md)). Turbo zapnuté (nejvyšší stav procesoru `0x64`, nejnižší `0x05`), což pro běh správnosti nerozhoduje.
- **Co běželo:** tři kroky rozh. [173](../decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md) za sebou nad jedním svazkem: .NET půlka (`Experiment/ExperimentRunTest`, 26 min, 5 616 testů, 0 selhalo), javová půlka nad týmiž vstupy (`experiment/ExperimentRunTest`, 34 min, 1 872 testů, 0 selhalo), `ExperimentSummaryTest`. Žádná sada před tím: obě běžely nad stromem vydání 2026-11-23 ([řádek velikosti sady](README.md#velikost-sady-a-pokrytí)). Soubory běhu (`tool-rows.tsv`, `java-tool-rows.tsv`, `mapping-rows.tsv`, `summary.md`, `inputs/`) zůstávají mimo git (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)); verze nástroje v řádcích 2.4.2. Běh za vydání `2.4.2`.

## Vstupy

624 vstupů výpisu, každý do šesti cílů, jako [2026-11-05](2026-11-05-experiment.md): matice 318, FIBEN 237, LDBC 41, příklady 22 (z nich šest případových studií: `keycloak-identity`, `cofoundry-pages`, `ambari-alerts`, `chinook-dapper`, `chinook-efcore`, `jpetstore-mybatis`), scénáře `cross` 6. Celkem 3 744 řádků nástroje (1 872 v každé sadě) a 3 744 řádků úplnosti mapování; žádný případ žádné sady neselhal. Výňatek Cofoundry je ve znění po `d2d8bc4`, který ho proti 2026-11-05 změnil ([nesrovnalosti](README.md#nesrovnalosti)).

## Výsledek

Sloupce podle `summary.md`: *n* překlady; *odmítnuto* bez artefaktu (záznam `Failure`); *nativní SQL* dotaz vyšel v SQL dialektu přes API frameworku (rozh. [113](../decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md)); *přečteno / zkompilováno / přijato* artefakt dosáhl stupně (T3: parsovatelný, kompilovatelný, přijatý); *n4* překlady klíčů se čtvrtým stupněm (matice, FIBEN, případová studie nad prázdnými tabulkami svého schématu), odmítnutí včetně; *běží* z n4 (T3: spustitelný); *shoda* z překladů n4 s měřítkem ekvivalence (matice, FIBEN; T3: funkčně ekvivalentní); *mapování* fakta, která parser cíle přečte z artefaktu zpět, z faktů reference, které cíl vyjádří (T3: úplnost přenesených mapovacích vlastností), sečtená přes skupinu.

### Tabulka T2 × T3 po dvojicích ekosystémů

| Dvojice | n | odmítnuto | nativní SQL | přečteno | zkompilováno | přijato | n4 | běží | shoda | mapování |
|---|---|---|---|---|---|---|---|---|---|---|
| .NET → .NET | 1341 | 1 | 129 | 1340 (100 %) | 1340 (100 %) | 1340 (100 %) | 1185 | 1185 (100 %) | 1179 (100 %) | 93 044 (99 %) |
| .NET → Java | 1341 | 2 | 95 | 1339 (100 %) | 1339 (100 %) | 1339 (100 %) | 1185 | 1185 (100 %) | 1179 (100 %) | 77 232 (69 %) |
| Java → .NET | 531 | 4 | 42 | 527 (99 %) | 527 (99 %) | 527 (99 %) | 492 | 489 (99 %) | 486 (100 %) | 48 849 (99 %) |
| Java → Java | 531 | 4 | 29 | 527 (99 %) | 527 (99 %) | 527 (99 %) | 492 | 490 (100 %) | 486 (100 %) | 47 064 (75 %) |

### Po druhu vstupu

| Druh | Dvojice | n | odmítnuto | nativní SQL | přijato | n4 | běží | shoda | mapování |
|---|---|---|---|---|---|---|---|---|---|
| matice | .NET → .NET | 468 | 0 | 28 | 468 | 468 | 468 | 468 (100 %) | 100 % |
| matice | .NET → Java | 468 | 0 | 24 | 468 | 468 | 468 | 468 (100 %) | 69 % |
| matice | Java → .NET | 486 | 0 | 40 | 486 | 486 | 486 | 486 (100 %) | 100 % |
| matice | Java → Java | 486 | 0 | 27 | 486 | 486 | 486 | 486 (100 %) | 74 % |
| FIBEN | .NET → .NET | 711 | 0 | 84 | 711 | 711 | 711 | 711 (100 %) | 100 % |
| FIBEN | .NET → Java | 711 | 0 | 55 | 711 | 711 | 711 | 711 (100 %) | 71 % |
| LDBC | .NET → .NET | 123 | 0 | 17 | 123 | — | — | — | 100 % |
| LDBC | .NET → Java | 123 | 0 | 16 | 123 | — | — | — | 66 % |
| případová studie | .NET → .NET | 9 | 0 | 0 | 9 | 6 | 6 | — | 85 % |
| případová studie | .NET → Java | 9 | 0 | 0 | 9 | 6 | 6 | — | 66 % |
| případová studie | Java → .NET | 9 | 3 | 0 | 6 | 6 | 3 | — | 93 % |
| případová studie | Java → Java | 9 | 2 | 0 | 7 | 6 | 4 | — | 76 % |
| příklad | .NET → .NET | 21 | 1 | 0 | 20 | — | — | — | 87 % |
| příklad | .NET → Java | 21 | 2 | 0 | 19 | — | — | — | 73 % |
| příklad | Java → .NET | 27 | 1 | 2 | 26 | — | — | — | 88 % |
| příklad | Java → Java | 27 | 2 | 2 | 25 | — | — | — | 89 % |
| scénář `cross` | každá | 9 | 0 | 0 | 9 | — | — | — | 82–98 % |

Každá kategorie matice je ve všech čtyřech dvojicích přijatá, běží a shoduje se s kanonickým výsledkem; kategorie, které v některém cíli jdou nativním SQL, vede `summary.md`. Dotazy LDBC končí třetím stupněm podle rozh. [172](../decisions/172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md) ve všech šesti cílech bez odmítnutí; FIBEN je ve shodě se soudcem v každém cíli.

### Po cíli

| Cíl | odmítnuto | nativní SQL | přijato (z 624) | shoda (z 555 s měřítkem) | mapování |
|---|---|---|---|---|---|
| Dapper | 0 | 0 | 624 | 555 | — (deskriptor nevyjadřuje žádný mapovací fakt) |
| EF Core | 2 | 81 | 622 | 555 | 99 % |
| NHibernate | 3 | 90 | 621 | 555 | 99 % |
| Hibernate | 3 | 15 | 621 | 555 | 66 % |
| EclipseLink | 3 | 109 | 621 | 555 | 72 % |
| MyBatis | 0 | 0 | 624 | 555 | 80 % |

V každém cíli je přijatý každý řádek, který není odmítnutý.

### Případové studie (rozh. [176](../decisions/176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md))

Stupeň, kterého projekt studie dosáhl jako celek; Dapper a MyBatis končí třetím stupněm záměrně (SQL bez schématu).

| Studie (zdroj) | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| `keycloak-identity` (Hibernate, 47 dotazů) | 3 | odmítnuto: kolekční parametr v nativním SQL EF Core | 4 | 4 | 4 | 3 |
| `cofoundry-pages` (EF Core, 45 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `ambari-alerts` (EclipseLink, 39 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `chinook-dapper` (Dapper, 38 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `chinook-efcore` (EF Core, 35 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `jpetstore-mybatis` (MyBatis, 14 dotazů) | 3 | odmítnuto: sloupce `supplierId`, `billAddress1` výsledku bez typu (rozh. [105](../decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md)) | odmítnuto: klíč | odmítnuto: klíč | odmítnuto: klíč | 3 |

Počet dotazů je počet, který nese řádek projektu v `tool-rows.tsv` a `java-tool-rows.tsv`.

### Proti 2026-11-05

Sedm řádků se liší, ostatních 3 737 má týž jazyk, stupeň, výsledek a shodu; řádky úplnosti mapování se v počtech faktů neliší žádný.

| Řádek | 2026-11-05 | 2026-11-26 |
|---|---|---|
| `cofoundry-pages` → Dapper, MyBatis | 1, nezkompilováno | 3, přijato |
| `cofoundry-pages` → EF Core, Hibernate, EclipseLink | 1, nezkompilováno | 4, běží |
| `cofoundry-pages` → NHibernate | 2, nepřijato | 4, běží |
| `marketplace-fluent` (EF Core) → EclipseLink | 2, nepřijato | 3, přijato |

Řádek `marketplace-fluent` změnilo rozh. [206](../decisions/206-a-single-part-shared-key-is-mapsid-over-a-writable-join-column-and-a-composite-shared-key-keeps-the-read-only-reference-in-both-jpa-profiles.md). Dva řádky Cofoundry do Dapperu a MyBatisu došly do třetího stupně, který je pro tyto cíle poslední, a proto už nepatří do *n4*: v obou dvojicích ze zdroje .NET kleslo z 1186 na 1185.

### Řádky pod nejvyšším stupněm

- **Odmítnutí** (11 řádků, všechna podle rozhodnutí, táž jako 2026-11-05): zdroje Dapper a MyBatis bez katalogu do cílů, které klíč vyžadují — `dapper-to-efcore` a `mybatis-to-dapper` do NHibernatu, Hibernatu a EclipseLinku, `jpetstore-mybatis` do týchž tří s katalogem studie; `jpetstore-mybatis` do EF Core (sloupce bez typu, rozh. 105); `keycloak-identity` do EF Core (kolekční parametr v nativním SQL).
- **Nekompilující a nepřijaté artefakty:** žádné. Každý řádek mimo odmítnutí došel do třetího stupně.
- **FIBEN:** každá z 711 buněk obou půlek ve shodě se soudcem rozh. 170.

### Úplnost mapování

Podíly po cíli jsou v tabulce výše a po zdroji stojí jako [2026-11-05](2026-11-05-experiment.md), protože se řádky úplnosti v počtech faktů neliší. Nižší podíly javových cílů dávají fakta, která parser JPA ani MyBatisu z vlastního artefaktu nečte zpět (rozh. [188](../decisions/188-a-value-read-back-that-states-the-reference-counts-identity-for-auto-and-a-key-for-not-null-while-the-mybatis-id-and-the-jpa-java-type-stay-limits-of-the-measure.md)); sloupec `detail` každého řádku `mapping-rows.tsv` jmenuje chybějící fakt s očekávanou a přečtenou hodnotou.

## Poznámky

- Běh neměří čas, takže stav procesoru ani pozastavení Windows Update nerozhodovaly; celý běh včetně sestavení obrazů, které vyšlo celé z mezipaměti, trval 66 min.
