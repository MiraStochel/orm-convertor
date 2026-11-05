# Experimentální běh

- **Začátek:** sestavení obrazů 2026-11-05 11:02 UTC, .NET půlka 11:07 UTC, javová půlka 11:34 UTC, souhrn 11:59 UTC
- **Strom:** `a105b3d`
- **Stroj a prostředí:** MIS3; obrazy profilu `test` a `ormconvertor` sestavené těsně předtím z tohoto stromu, obraz databáze z [2026-11-02](2026-11-02-fiben.md) (`database/` se od té doby nezměnilo). `test_db` (SF 0,1) pro fixturu a databázi případových studií, `mssql_db` v měřicím režimu (procesory 0–1, `MAXDOP` 1) s `Fiben` a soudcem rozh. [170](../decisions/170-rows-tied-on-the-ordering-key-compare-as-a-set-and-a-fiben-slice-gets-a-deciding-key.md) pro druh `fiben`; instance `test_app`, `test_app_ldbc`, `test_app_cases` a `ormconvertor` nad `Fiben`. Oba servery s mezí paměti 2048 MB ([metodika](README.md#před-dlouhým-během)), virtuální stroj 4 procesory a 12 GB (rozh. [193](../decisions/193-the-docker-vm-has-twelve-gigabytes-so-that-the-limits-inside-it-add-up-to-less-than-it.md)). Turbo vypnuté (nejvyšší i nejnižší stav procesoru `0x63`, zůstalo z běhu FIBEN), což pro běh správnosti nerozhoduje.
- **Co běželo:** tři kroky rozh. [173](../decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md) za sebou nad jedním svazkem: .NET půlka (`Experiment/ExperimentRunTest`, 27 min, 5 616 testů, 0 selhalo), javová půlka nad týmiž vstupy (`experiment/ExperimentRunTest`, 26 min, 1 872 testů, 0 selhalo), `ExperimentSummaryTest`. Žádná sada před tím: obě běžely nad `63d0b81` ([řádek velikosti sady](README.md#velikost-sady-a-pokrytí)) a `a105b3d` se od něj liší jen dokumentací a číslem verze. Soubory běhu (`tool-rows.tsv`, `java-tool-rows.tsv`, `mapping-rows.tsv`, `summary.md`, `inputs/`) zůstávají mimo git (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)); verze nástroje v řádcích 2.3.0. Běh za vydání `2.3.0`.

## Vstupy

624 vstupů výpisu, každý do šesti cílů: matice 318 (každá z 59 kategorií `categories.txt` z každého zdroje, který ji vysloví), FIBEN 237, LDBC 41, příklady 22 (z nich šest případových studií: `keycloak-identity`, `cofoundry-pages`, `ambari-alerts`, `chinook-dapper`, `chinook-efcore`, `jpetstore-mybatis`), scénáře `cross` 6. Celkem 3 744 řádků nástroje (1 872 v každé sadě) a 3 744 řádků úplnosti mapování; žádný případ žádné sady neselhal. Proti [2026-10-24](2026-10-24-experiment.md) přibyly čtyři kategorie matice a výňatek Cofoundry (rozh. [184](../decisions/184-t1-leaves-the-exempt-area-in-a-narrower-sense-the-case-study-meets-the-criteria-counted-from-the-excerpt-and-cofoundry-becomes-its-second-application.md)).

## Výsledek

Sloupce podle `summary.md`: *n* překlady; *odmítnuto* bez artefaktu (záznam `Failure`); *nativní SQL* dotaz vyšel v SQL dialektu přes API frameworku (rozh. [113](../decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md)); *přečteno / zkompilováno / přijato* artefakt dosáhl stupně (T3: parsovatelný, kompilovatelný, přijatý); *n4* překlady klíčů se čtvrtým stupněm (matice, FIBEN, případová studie nad prázdnými tabulkami svého schématu), odmítnutí včetně; *běží* z n4 (T3: spustitelný); *shoda* z překladů n4 s měřítkem ekvivalence (matice, FIBEN; T3: funkčně ekvivalentní); *mapování* fakta, která parser cíle přečte z artefaktu zpět, z faktů reference, které cíl vyjádří (T3: úplnost přenesených mapovacích vlastností), sečtená přes skupinu.

### Tabulka T2 × T3 po dvojicích ekosystémů

| Dvojice | n | odmítnuto | nativní SQL | přečteno | zkompilováno | přijato | n4 | běží | shoda | mapování |
|---|---|---|---|---|---|---|---|---|---|---|
| .NET → .NET | 1341 | 1 | 129 | 1340 (100 %) | 1338 (100 %) | 1337 (100 %) | 1186 | 1183 (100 %) | 1179 (100 %) | 93 044 (99 %) |
| .NET → Java | 1341 | 2 | 95 | 1339 (100 %) | 1336 (100 %) | 1335 (100 %) | 1186 | 1183 (100 %) | 1179 (100 %) | 77 232 (69 %) |
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
| případová studie | .NET → .NET | 9 | 0 | 0 | 6 | 7 | 4 | — | 85 % |
| případová studie | .NET → Java | 9 | 0 | 0 | 6 | 7 | 4 | — | 66 % |
| případová studie | Java → .NET | 9 | 3 | 0 | 6 | 6 | 3 | — | 93 % |
| případová studie | Java → Java | 9 | 2 | 0 | 7 | 6 | 4 | — | 76 % |
| příklad | .NET → .NET | 21 | 1 | 0 | 20 | — | — | — | 87 % |
| příklad | .NET → Java | 21 | 2 | 0 | 18 | — | — | — | 73 % |
| příklad | Java → .NET | 27 | 1 | 2 | 26 | — | — | — | 88 % |
| příklad | Java → Java | 27 | 2 | 2 | 25 | — | — | — | 89 % |
| scénář `cross` | každá | 9 | 0 | 0 | 9 | — | — | — | 82–98 % |

Každá z 59 kategorií matice je ve všech čtyřech dvojicích přijatá, běží a shoduje se s kanonickým výsledkem; kategorie, které v některém cíli jdou nativním SQL, vede `summary.md`. Dotazy LDBC končí třetím stupněm podle rozh. [172](../decisions/172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md) ve všech šesti cílech bez odmítnutí; FIBEN je ve shodě se soudcem v každém Kimově typu a každém cíli.

### Po cíli

| Cíl | odmítnuto | nativní SQL | přijato (z 624) | shoda (z 555 s měřítkem) | mapování |
|---|---|---|---|---|---|
| Dapper | 0 | 0 | 623 | 555 | — (deskriptor nevyjadřuje žádný mapovací fakt) |
| EF Core | 2 | 81 | 621 | 555 | 99 % |
| NHibernate | 3 | 90 | 620 | 555 | 99 % |
| Hibernate | 3 | 15 | 620 | 555 | 66 % |
| EclipseLink | 3 | 109 | 619 | 555 | 72 % |
| MyBatis | 0 | 0 | 623 | 555 | 80 % |

### Případové studie (rozh. [176](../decisions/176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md))

Stupeň, kterého projekt studie dosáhl jako celek; Dapper a MyBatis končí třetím stupněm záměrně (SQL bez schématu).

| Studie (zdroj) | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| `keycloak-identity` (Hibernate, 47 dotazů) | 3 | odmítnuto: kolekční parametr v nativním SQL EF Core | 4 | 4 | 4 | 3 |
| `cofoundry-pages` (EF Core, 39 jednotek dotazů) | 1: typ `Template` mimo výňatek | 1: `PageDirectoryClosure.PageDirectoryId` | 2: HQL `query.PageId` | 1: `Template` | 1: `Template` | 1: `Template` |
| `ambari-alerts` (EclipseLink, 39 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `chinook-dapper` (Dapper, 38 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `chinook-efcore` (EF Core, 35 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `jpetstore-mybatis` (MyBatis, 14 dotazů) | 3 | odmítnuto: sloupce `supplierId`, `billAddress1` výsledku bez typu (rozh. [105](../decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md)) | odmítnuto: klíč | odmítnuto: klíč | odmítnuto: klíč | 3 |

Cofoundry, druhá aplikace T1 podle rozh. 184, je v tomto běhu poprvé a nedošla do druhého stupně v žádném cíli (níže); ostatní studie stojí jako [2026-10-24](2026-10-24-experiment.md).

### Červené řádky

- **Odmítnutí** (11 řádků, všechna podle rozhodnutí): zdroje Dapper a MyBatis bez katalogu do cílů, které klíč vyžadují — `dapper-to-efcore` a `mybatis-to-dapper` do NHibernatu, Hibernatu a EclipseLinku, `jpetstore-mybatis` do týchž tří s katalogem studie; `jpetstore-mybatis` do EF Core (sloupce bez typu, rozh. 105); `keycloak-identity` do EF Core (kolekční parametr v nativním SQL).
- **Nekompilující artefakty** (5 řádků, všechny `cofoundry-pages`): do Dapperu, Hibernatu, EclipseLinku a MyBatisu projekce dotazu v `GetPageRegionDetailsByPageVersionIdQueryHandler` sahá na typ `Template`, který výňatek nenese (`CS0246`, `cannot find symbol`); do EF Core dotaz nad `PageDirectoryClosure` sahá na `PageDirectoryId`, entita má jen `AncestorPageDirectoryId` a `DescendantPageDirectoryId` (`CS1061`).
- **Nepřijaté artefakty** (2 řádky): `cofoundry-pages` do NHibernatu — HQL `from Page p where p.PageId = query.PageId`, kde `query.PageId` je člen zachyceného objektu dotazu (parametr), ne cesta (`Invalid path`); `marketplace-fluent` ze zdroje EF Core do EclipseLinku — nasazení jednotky selže `EclipseLink-46`: *There should be one non-read-only mapping defined for the primary key field [Market.CustomerProfiles.CustomerId]*, klíč `CustomerProfile`, který je zároveň cizím klíčem, vyšel jen v mapováních jen pro čtení; Hibernate týž artefakt přijal. Obojí jsou položky v [`open-items.md`](../open-items.md). Vady EclipseLinku z 2026-10-24 (verze nad `byte[]`, `full join`) jsou pryč (rozh. [179](../decisions/179-eclipselink-carries-no-version-over-a-binary-column-so-the-store-generated-version-is-a-read-only-column-with-a-loss.md), [180](../decisions/180-a-full-outer-join-into-eclipselink-goes-out-in-native-sql.md)): `helpdesk-application`, `order-book`, `order-repository` a `marketplace-query-tour` EclipseLink přijal.
- **FIBEN:** každá z 711 buněk obou půlek ve shodě se soudcem rozh. 170; šest dotazů, které se 2026-10-24 lišily proti soudci z doby před ním, se shoduje.

### Úplnost mapování

Podíl přečtených faktů z vyjádřitelných po cíli je v tabulce výše; po zdroji se liší málo (EF Core 98–100 %, NHibernate 96–100 %, MyBatis 72–83 %, EclipseLink 69–75 %, Hibernate 62–71 %). Proti 2026-10-24 stouply podíly cílů .NET z 91–99 % na 99 % zpětným čtením, které uzná `Identity` za `Auto` a klíč za NOT NULL svých sloupců (rozh. [188](../decisions/188-a-value-read-back-that-states-the-reference-counts-identity-for-auto-and-a-key-for-not-null-while-the-mybatis-id-and-the-jpa-java-type-stay-limits-of-the-measure.md)). Nižší podíly javových cílů dávají fakta, která parser JPA ani MyBatisu z vlastního artefaktu nečte zpět, jak je jménem drží `Experiment/MappingCompletenessTest` ([`traceability.md`](../traceability.md), T3); sloupec `detail` každého řádku `mapping-rows.tsv` jmenuje chybějící fakt s očekávanou a přečtenou hodnotou.

## Poznámky

- Běh neměří čas, takže stav procesoru ani pozastavení Windows Update nerozhodovaly; celý běh včetně sestavení obrazů trval 58 min.
- Dva nálezy běhu — výňatek Cofoundry a klíč, který je zároveň cizím klíčem, v EclipseLinku — jsou položky v [`open-items.md`](../open-items.md). Ani jeden nemění nárok: případová studie je nárokovaný vstup, ne výsledek (rozh. 184), příklady se měří, nenárokují (§9).
