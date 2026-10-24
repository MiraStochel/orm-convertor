# Experimentální běh

- **Začátek:** javová sada 2026-10-24 15:54 UTC, .NET půlka 16:07 UTC, javová půlka 16:35 UTC, souhrn 17:04 UTC
- **Strom:** `5caefc5`
- **Stroj a prostředí:** MIS3; obrazy profilu `test` a `ormconvertor` sestavené těsně předtím z tohoto stromu, obraz databáze z 2026-10-22 (`database/` se od té doby nezměnilo). `test_db` (SF 0,1) pro fixturu a databázi případových studií, `mssql_db` v měřicím režimu (procesory 0–1, `MAXDOP` 1) s `Fiben` a soudcem rozh. [163](../decisions/163-the-fiben-judge-takes-the-eight-queries-of-decisions-161-and-162-from-the-dotnet-half-of-the-run-of-2026-10-22.md) pro druh `fiben`; instance `test_app`, `test_app_ldbc`, `test_app_cases` a `ormconvertor` nad `Fiben`. Oba servery s mezí paměti 2048 MB místo 4096 MB obrazu — dva servery vedle sady se do virtuálního stroje (4 procesory, 7,75 GB) nevejdou a běh čas neměří. Turbo zapnuté (nejvyšší stav procesoru `0x64`, nejnižší `0x05`), což pro běh správnosti nerozhoduje ([metodika](README.md#druhy-běhů)).
- **Co běželo:** nejdřív celá javová sada v kontejneru jako kontrola změn od [záznamu z 2026-10-22](2026-10-22-fiben-2.md) (řádek [velikosti sady](README.md#velikost-sady-a-pokrytí)), pak tři kroky rozh. [173](../decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md) za sebou nad jedním svazkem: .NET půlka (`Experiment/ExperimentRunTest`, 28 min), javová půlka nad týmiž vstupy (`experiment/ExperimentRunTest`, 29 min), `ExperimentSummaryTest`. Soubory běhu (`tool-rows.tsv`, `java-tool-rows.tsv`, `mapping-rows.tsv`, `summary.md`, `inputs/`) zůstávají mimo git (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)); verze nástroje v řádcích 2.2.0.

## Vstupy

606 vstupů výpisu, každý do šesti cílů: matice 301 (každá kategorie `categories.txt` z každého zdroje, který ji vysloví), FIBEN 237, LDBC 41, příklady 21 (z nich pět případových studií: `keycloak-identity`, `ambari-alerts`, `chinook-dapper`, `chinook-efcore`, `jpetstore-mybatis`), scénáře `cross` 6. Celkem 3636 řádků nástroje (1818 v každé sadě) a 3636 řádků úplnosti mapování; žádný případ žádné sady neselhal.

## Výsledek

Sloupce podle `summary.md`: *n* překlady; *odmítnuto* bez artefaktu (záznam `Failure`); *nativní SQL* dotaz vyšel v SQL dialektu přes API frameworku (rozh. [113](../decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md)); *přečteno / zkompilováno / přijato* artefakt dosáhl stupně (T3: parsovatelný, kompilovatelný, přijatý); *n4* překlady klíčů se čtvrtým stupněm (matice, FIBEN, případová studie nad prázdnými tabulkami svého schématu), odmítnutí včetně; *běží* z n4 (T3: spustitelný); *shoda* z překladů n4 s měřítkem ekvivalence (matice, FIBEN; T3: funkčně ekvivalentní); *mapování* fakta, která parser cíle přečte z artefaktu zpět, z faktů reference, které cíl vyjádří (T3: úplnost přenesených mapovacích vlastností), sečtená přes skupinu.

### Tabulka T2 × T3 po dvojicích ekosystémů

| Dvojice | n | odmítnuto | nativní SQL | přečteno | zkompilováno | přijato | n4 | běží | shoda | mapování |
|---|---|---|---|---|---|---|---|---|---|---|
| .NET → .NET | 1314 | 1 | 127 | 1313 (100 %) | 1313 (100 %) | 1313 (100 %) | 1159 | 1159 (100 %) | 1137 (98 %) | 85 840 (95 %) |
| .NET → Java | 1314 | 2 | 95 | 1312 (100 %) | 1312 (100 %) | 1308 (100 %) | 1159 | 1159 (100 %) | 1137 (98 %) | 66 075 (61 %) |
| Java → .NET | 504 | 4 | 39 | 500 (99 %) | 500 (99 %) | 500 (99 %) | 465 | 462 (99 %) | 459 (100 %) | 44 224 (95 %) |
| Java → Java | 504 | 4 | 29 | 500 (99 %) | 500 (99 %) | 499 (99 %) | 465 | 463 (100 %) | 459 (100 %) | 40 564 (68 %) |

### Po druhu vstupu

| Druh | Dvojice | n | odmítnuto | nativní SQL | přijato | n4 | běží | shoda | mapování |
|---|---|---|---|---|---|---|---|---|---|
| matice | .NET → .NET | 444 | 0 | 26 | 444 | 444 | 444 | 444 (100 %) | 94 % |
| matice | .NET → Java | 444 | 0 | 24 | 444 | 444 | 444 | 444 (100 %) | 60 % |
| matice | Java → .NET | 459 | 0 | 37 | 459 | 459 | 459 | 459 (100 %) | 95 % |
| matice | Java → Java | 459 | 0 | 27 | 459 | 459 | 459 | 459 (100 %) | 67 % |
| FIBEN | .NET → .NET | 711 | 0 | 84 | 711 | 711 | 711 | 693 (97 %) | 97 % |
| FIBEN | .NET → Java | 711 | 0 | 55 | 711 | 711 | 711 | 693 (97 %) | 66 % |
| LDBC | .NET → .NET | 123 | 0 | 17 | 123 | — | — | — | 95 % |
| LDBC | .NET → Java | 123 | 0 | 16 | 123 | — | — | — | 57 % |
| případová studie | .NET → .NET | 6 | 0 | 0 | 6 | 4 | 4 | — | 87 % |
| případová studie | .NET → Java | 6 | 0 | 0 | 6 | 4 | 4 | — | 63 % |
| případová studie | Java → .NET | 9 | 3 | 0 | 6 | 6 | 3 | — | 89 % |
| případová studie | Java → Java | 9 | 2 | 0 | 7 | 6 | 4 | — | 72 % |
| příklad | .NET → .NET | 21 | 1 | 0 | 20 | — | — | — | 84 % |
| příklad | .NET → Java | 21 | 2 | 0 | 15 | — | — | — | 66 % |
| příklad | Java → .NET | 27 | 1 | 2 | 26 | — | — | — | 87 % |
| příklad | Java → Java | 27 | 2 | 2 | 24 | — | — | — | 87 % |
| scénář `cross` | každá | 9 | 0 | 0 | 9 | — | — | — | 71–95 % |

Každá z 55 kategorií matice je ve všech čtyřech dvojicích přijatá, běží a shoduje se s kanonickým výsledkem; kategorie, které v některém cíli jdou nativním SQL, vede `summary.md`. Dotazy LDBC končí třetím stupněm podle rozh. [172](../decisions/172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md) ve všech šesti cílech bez odmítnutí; FIBEN po Kimových typech: typy J, JA a N ve shodě se soudcem v každém cíli, nevnořené dotazy a typ A nesou šest dotazů níže.

### Po cíli

| Cíl | odmítnuto | nativní SQL | přijato (z 606) | shoda (z 538 s měřítkem) | mapování |
|---|---|---|---|---|---|
| Dapper | 0 | 0 | 606 | 532 | — (deskriptor nevyjadřuje žádný mapovací fakt) |
| EF Core | 2 | 81 | 604 | 532 | 99 % |
| NHibernate | 3 | 85 | 603 | 532 | 91 % |
| Hibernate | 3 | 15 | 603 | 532 | 57 % |
| EclipseLink | 3 | 109 | 598 | 532 | 63 % |
| MyBatis | 0 | 0 | 606 | 532 | 80 % |

### Případové studie (rozh. [176](../decisions/176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md))

Stupeň, kterého projekt studie dosáhl jako celek; Dapper a MyBatis končí třetím stupněm záměrně (SQL bez schématu).

| Studie (zdroj) | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| `keycloak-identity` (Hibernate, 22 entit, 47 dotazů) | 3 | odmítnuto: kolekční parametr v nativním SQL EF Core | 4 | 4 | 4 (smíšený jazyk) | 3 |
| `ambari-alerts` (EclipseLink, 39 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `chinook-dapper` (Dapper, 38 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `chinook-efcore` (EF Core, 35 dotazů) | 3 | 4 | 4 | 4 | 4 | 3 |
| `jpetstore-mybatis` (MyBatis, 14 dotazů) | 3 | odmítnuto: sloupce `supplierId`, `billAddress1` výsledku bez typu (rozh. [105](../decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md)) | odmítnuto: klíč | odmítnuto: klíč | odmítnuto: klíč | 3 |

Proti [kontrole z téhož dne](2026-10-24-experiment-examples.md) přibyl katalog studie: Chinook ze zdroje Dapper už EF Core nemapuje jako entity bez klíče a běží, a všechny studie kromě JPetStore mají čtvrtý stupeň v každém cíli, který je neodmítne podle rozhodnutí. JPetStore do cílů, které klíč vyžadují, odmítá i s katalogem („neither the source nor a catalog supplied it"), ač `schema.sql` výňatku má primární klíč na každé tabulce — kterému entitnímu typu klíč chybí, řádek neříká; položka v [`open-items.md`](../open-items.md).

### Červené řádky

- **Odmítnutí** (11 řádků, všechna podle rozhodnutí): zdroje Dapper a MyBatis bez katalogu do cílů, které klíč vyžadují — `dapper-to-efcore` a `mybatis-to-dapper` do NHibernatu, Hibernatu a EclipseLinku, `jpetstore-mybatis` do týchž tří s katalogem studie (výše); `jpetstore-mybatis` do EF Core (sloupce bez typu, rozh. 105); `keycloak-identity` do EF Core (kolekční parametr v nativním SQL).
- **Nepřijaté artefakty** (5 řádků, všechny EclipseLink 5.0.0, Hibernate tytéž artefakty přijal): `helpdesk-application`, `marketplace-fluent`, `order-book` a `order-repository` ze zdroje EF Core nesou `[Timestamp] byte[] RowVersion`, které vrstva JPA píše jako `@Version byte[]`, a EclipseLink jednotku odmítne při nasazení (`EclipseLink-7168`: verze smí být jen `int`, `Integer`, `short`, `Short`, `long`, `Long`, `Timestamp`); `marketplace-query-tour` ze zdroje Hibernate má dotaz s `full join`, který do EclipseLinku vyjde jako JPQL s `full join`, a EclipseLink ho při vytvoření odmítne (syntaktická chyba; Jakarta Persistence úplné vnější spojení nemá). Obojí jsou vady; položky v [`open-items.md`](../open-items.md).
- **FIBEN bez shody se soudcem** (36 buněk): dotazy q24, q31, q36, q38, q178 a q179 se liší ve všech šesti cílech stejně, text katalogu (cíl Dapper) včetně — není to překlad, ale soudce: `Judge` v `mssql_db` je z doby před rozh. [170](../decisions/170-rows-tied-on-the-ordering-key-compare-as-a-set-and-a-fiben-slice-gets-a-deciding-key.md), a všech šest dotazů stojí v jeho tabulce v *Kontextu* (q24, q36, q38 seřazené bez výřezu — pořadí uvnitř remízy; q31 `TOP (3)`; q178 a q179 `TOP (1)` ve skalárním poddotazu, kterému rozhodující klíč změní celý vnější výsledek: q178 z 13 řádků na 0, q179 ze 2 na 6). Obnovu soudce vede položka *Soudce FIBEN po rozh. 170*. Ostatních 231 dotazů ve shodě v každém cíli.

### Úplnost mapování

Podíl přečtených faktů z vyjádřitelných po cíli je v tabulce výše; po zdroji se liší málo (EF Core 99–100 %, NHibernate 87–92 %, MyBatis 70–83 %, EclipseLink 56–67 %, Hibernate 51–63 %). Nižší podíly javových cílů dávají fakta, která parser JPA ani MyBatisu z vlastního artefaktu nečte zpět, jak je jménem drží `Experiment/MappingCompletenessTest` ([`traceability.md`](../traceability.md), T3): nullabilita sloupců klíče, databázový typ odvozený z javového typu a strategie klíče profilu u JPA, `<id>` MyBatisu jako klíč. Sloupec `detail` každého řádku `mapping-rows.tsv` jmenuje chybějící fakt s očekávanou a přečtenou hodnotou.

## Poznámky

- Javová sada před během: 4693 testů, 0 selhalo, 1662 přeskočeno — běh FIBEN bez `Fiben` (1660), experimentální běh a běh cizích artefaktů bez svého adresáře (po jednom); soudce LDBC nad prvními 1000 řádky SF 0,1 ve shodě. .NET sada v kontejneru tentokrát neběžela celá, jen krok experimentálního běhu.
- Běh neměří čas, takže stav procesoru ani pozastavení Windows Update nerozhodovaly; celý běh včetně sestavení obrazů a javové sady trval 1 h 16 min.
- Tři nálezy běhu — dvě vady EclipseLinku a klíč JPetStore — jsou položky v [`open-items.md`](../open-items.md); zda T2 a T3 s tímto záznamem vystoupí z vyňaté oblasti 6 v užším rozsahu, je tam položka rozhodnutí.
