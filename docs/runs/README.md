# Záznamy běhů

Datované záznamy běhů obou sad a měření (rozh. [167](../decisions/167-records-of-runs-and-measurements-have-a-folder-of-their-own.md)): **co se kdy naměřilo nad kterým stromem**. Záznam je událost — nezastarává, protože o dnešku nic netvrdí (rozh. [095](../decisions/095-a-dated-run-record-names-its-commit.md)) —, a proto se po zapsání nemění. Jaké běhy jsou, kdy a v jakém prostředí se spouštějí, říká oddíl [Metodika](#metodika) (rozh. [168](../decisions/168-the-overview-of-runs-carries-the-method-of-runs.md)), jediná živá část přehledu; příkazy nese [`ORMConvertor/README.md`](../../ORMConvertor/README.md), co který běh dokazuje, [`traceability.md`](../traceability.md) a `architecture.md` §9.

## Metodika

### Druhy běhů

| Běh | Kdy | Server | Turbo | Záznam | Doba |
|---|---|---|---|---|---|
| CI, obě sady | push a pull request do `main`, který mění `ORMConvertor/**` nebo `.github/workflows/**` | služba SQL Server 2022 jobu, bez `LdbcSnb` a `Fiben` | — | žádný | — |
| obě sady v kontejnerech (`build` + `run`) | první krok úplného běhu; k vydání | `test_db` (SF 0,1); soudce LDBC nad prvními 1000 řádky sady jako kontrola | lhostejné | řádek [velikosti sady](#velikost-sady-a-pokrytí) | — |
| běh FIBEN | na vyžádání, za verzi nebo zásadní změnu (rozh. [149](../decisions/149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md)) | `mssql_db` v měřicím režimu (rozh. [164](../decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md)) | vypnuté | soubor | [2026-10-22](2026-10-22-fiben-2.md) |
| soudce LDBC nad celou sadou SF 1 | na vyžádání, spouští ho Míra jménem, v sezení, které nic jiného nedělá | `mssql_db` v režimu soudce (rozh. 164, [166](../decisions/166-the-judge-reads-a-segment-between-inserts-in-every-framework-at-once.md)) | zapnuté | soubor | [2026-10-12](2026-10-12-ldbc-judge.md) před oběma režimy; [odhad](2026-10-22-ldbc-judge-mode.md) v režimu soudce |
| měření času (katalog LDBC, přehrání soudce po krocích) | k rozhodnutí, které je potřebuje (rozh. [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md), [141](../decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md), [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), 164, 166) | `mssql_db`, v režimu, o kterém měření je | podle účelu, v záznamu | soubor | v záznamu |
| běh cizích artefaktů (rozh. [172](../decisions/172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md)) | na vyžádání, nad artefakty, které nástroj nevyrobil; proces na artefakt s mezí času (`foreign-run.sh`) | `test_db` nebo databáze fixture hostitele; pro klíče `fiben:` `mssql_db` se soudcem | lhostejné (správnost, ne čas) | soubor s původem artefaktů z manifestů (producent, model, prompt, pokusy) a podíly po stupních a cílích | — |
| experimentální běh (rozh. [173](../decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md)) | na vyžádání, za verzi nebo zásadní změnu; .NET půlka, javová půlka nad týmiž vstupy, souhrn | `test_db` pro matici, scénáře, příklady a LDBC; pro klíče `fiben:` `mssql_db` se soudcem a instance nad `Fiben` | lhostejné (správnost, ne čas) | soubor s tabulkou T2 × T3 ze `summary.md` (podíly po dvojicích ekosystémů, směrech, kategoriích a druzích vstupu, úplnost mapování) | — |

Kontrola (oddíl [Záznam](#záznam)) se pouští kdykoli. Měřicí prostředí a oba režimy serveru, jak je nastavuje obraz a compose, popisuje `architecture.md` §8; výstupy běhu po buňkách a logy zůstávají mimo git (rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md)).

### Před dlouhým během

Na MIS3, kde se měří; platí pro běh FIBEN, soudce LDBC a měření času.

- **Obrazy z dnešního stromu.** Strom čistý a každý obraz, který běh spouští, sestavený z něj. `run` bez `build` běží nad stromem, ze kterého obraz vznikl, a jediným signálem je počet testů proti [velikosti sady](#velikost-sady-a-pokrytí).
- **Stav procesoru.** Pro měření nejvyšší i nejnižší stav procesoru `0x63`, tedy 99 % bez turba a bez náběhu z klidu (`powercfg /query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX` a `PROCTHROTTLEMIN`); soudce LDBC není měření a běží s turbem. Záznam říká, co platilo.
- **Virtuální stroj** WSL2 se čtyřmi procesory, 8 GB a bez swapu (`%USERPROFILE%\.wslconfig`, §8), jak je hlásí `docker info`.
- **Windows Update pozastavené** přes očekávaný konec běhu, jinak restartuje stroj kolem 04:00 a po restartu nikdo není přihlášený, takže Docker Desktop neběží. Uspání a hibernace vypnuté.
- **Nic vedle.** Jeden běh po druhém; dlouhý běh nechává hostiteli málo paměti. Kontejnery předchozích běhů se odstraní, až jsou jejich výsledky venku.
- **Server v režimu, který běh žádá.** Dokud `mssql_db` běží v režimu soudce, nese každý příkaz compose nad ním `--env-file judge.env`, jinak ho compose vytvoří znovu v měřicím režimu; každé nové vytvoření `mssql_db` přehrání soudce přeruší.
- **Kontejner běhu** odpojený, pojmenovaný a bez `--rm`, aby výsledek držel `docker logs`.

### Pořadí úplného běhu

1. Sestavit všechny obrazy: `tests`, `java_tests`, `test_app`, `test_app_ldbc`, `test_db`, `mssql_db`, `ormconvertor`.
2. .NET sada, pak javová sada v kontejnerech.
3. `mssql_db` v měřicím režimu a instance nad `Fiben`; počkat na poslední řádek loaderu.
4. Běh FIBEN: .NET sada, pak javová.
5. Je-li vyžádán soudce LDBC: `mssql_db` v režimu soudce a instance nad `LdbcSnb`, soudce .NET sady, pak javové.
6. `mssql_db` zpět v měřicím režimu, instance zpět nad WideWorldImporters.

Proč v tomto pořadí a nikdy dva běhy najednou, říká rozh. 168.

## Záznam

- **Co se zapisuje:** verdikt soudce LDBC nad celou sadou, běh FIBEN, měření časů, velikost sady a pokrytí, podíl F4. Kontrola — prefix sady, výběr dotazů FIBEN (`ORMCONVERTOR_FIBEN_QUERIES`), běh z hosta mimo zafixované prostředí — se nezapisuje, s jedinou výjimkou: kontrola, jejíž zjištění **změnilo vstupy experimentu** (příklad, výňatek), se zapíše jako popsaný běh s výsledkem před změnou a po ní a s důvodem změny, protože záznam verze by jinak stál nad jinými vstupy a nic by to nevysvětlovalo.
- **Popsaný běh** má soubor `RRRR-MM-DD-<druh>.md` podle dne, kdy začal; druhý běh téhož druhu téhož dne dostane příponu `-2`. Obě půlky běhu .NET a javové sady nad týmž stromem jsou jeden soubor. Hlavička: začátek, strom (commit), stroj a prostředí — režim serveru (rozh. [164](../decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md)) a nejvyšší a nejnižší stav procesoru (rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md)) —, pak výsledek a poznámky. V rejstříku dostane řádek s jednou větou výsledku; čísla stojí jen v souboru.
- **Krátký záznam** — řádek s commitem a poznámkou — je řádkem tabulky v tomto souboru a vlastní soubor nemá.
- **Omyl** zjištěný později se v záznamu neopravuje; uvede se v oddílu [Nesrovnalosti](#nesrovnalosti).

## Popsané běhy

| Začátek | Druh | Strom | Výsledek |
|---|---|---|---|
| [2026-10-12](2026-10-12-ldbc-judge.md) | soudce LDBC, celá sada SF 1 | `580ab90`, `5656945` | obě sady zeleně, každé čtení ve shodě; IC 13 a IC 14 ještě v textech s mezí hloubky |
| [2026-10-15](2026-10-15-ldbc-catalog-times.md) | časy katalogu LDBC, hloubky dotazů na cesty | `86a102b` + následující | okno rozh. 140 nad SF 1 před zafixováním prostředí; hloubky dat pro dotazy na cesty |
| [2026-10-17](2026-10-17-ldbc-catalog-noise.md) | šum měření katalogu LDBC | `6f2c1a2` + následující | rozptyl mediánů před zafixováním prostředí a v něm |
| [2026-10-17](2026-10-17-ldbc-catalog-times.md) | časy katalogu LDBC v zafixovaném prostředí | `d3d1a9a` | každý spuštěný dotaz pod horní mezí okna; BI 14 nespuštěn |
| [2026-10-18](2026-10-18-fiben.md) | běh FIBEN | `8e1cdd7` + následující | první doložený běh; z jeho .NET půlky první soudce |
| [2026-10-18](2026-10-18-ldbc-judge-time-by-step.md) | doba přehrání soudce LDBC po krocích, měřicí režim | `32f0949` | každá buňka ve shodě; projekce celé sady v měřicím režimu |
| [2026-10-19](2026-10-19-fiben.md) | běh FIBEN | `9f6082d` + následující | každá buňka ve shodě se zdrojem kromě q63 v EclipseLinku; soudce znovu z .NET půlky |
| [2026-10-22](2026-10-22-fiben.md) | běh FIBEN, jen .NET půlka | `a2f498a` | všechny dotazy katalogu ve třech cílech .NET; z běhu soudce osmi dotazů rozh. 163 |
| [2026-10-22](2026-10-22-fiben-2.md) | běh FIBEN | `ee29333` | první běh s vypnutým turbem; každá buňka ve shodě se zdrojem kromě q63 v EclipseLinku |
| [2026-10-22](2026-10-22-ldbc-judge-mode.md) | doba přehrání soudce LDBC v režimu soudce | `1bc69c3` + změna rozh. 165 a 166 | režim soudce proti měřicímu, přehrání postupné a souběžné |
| [2026-10-24](2026-10-24-experiment-examples.md) | kontrola experimentálního běhu nad příklady, .NET půlka na MIS2 | `0b6c875` + úprava vstupů | příklady a výňatky se v cílech .NET nekompilovaly kvůli typům mimo vstup, výčtům a mapám; vstupy upraveny a po úpravě každý řádek mimo odmítnutí podle rozhodnutí do třetího stupně, až na Chinook bez klíče do EF Core |
| [2026-10-24](2026-10-24-ldbc-catalog-times.md) | čas BI 14 v zafixovaném prostředí | `07fc101` | BI 14 pod horní mezí okna; se záznamem z 2026-10-17 změřen celý katalog |

**Sloupce běhu FIBEN:** *přeloženo* je vlastní jazyk cíle, *nativní SQL* úniková cesta rozh. [113](../decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), *odmítnuto* `Failure` v překladu; *shoda se zdrojem* je čtvrtý stupeň proti řádkům zdroje téhož běhu; *medián* je medián mediánů buněk podle politiky rozh. 145 a *součet* jejich součet přes katalog.

## Velikost sady a pokrytí

Jediné místo, kde se velikost sady zapisuje (rozh. 095); každý řádek jmenuje commit. Ve všech bězích 0 selhaných. Přeskočený test patří do jedné ze tří skupin, každá se svým důvodem: diferenční buňky tvaru LINQ v NHibernatu, který jeho provider nevysloví (záznam `Omitted`, rozh. [118](../decisions/118-nhibernate-writes-a-linq-form-beside-its-hql.md); v každém prostředí), soudce LDBC bez `LdbcSnb` a hloubky jeho dotazů na cesty nad jiným data setem než SF 1, a běh FIBEN bez `Fiben`. *Zafixované* = dvojice `build` + `run`; *host* = `dotnet test` na MIS3 jen s LocalDB; integrační testy počítá `-Dgroups=integration`.

| Datum | Strom | .NET | Java (integrační) | Poznámka |
|---|---|---|---|---|
| 2026-09-21 | `3e8a286` | 1551, zafixované | 146 (97), zafixované | **vydání `2.0.0`** |
| 2026-10-03 | `c32948b` + následující commit | 7746, zafixované | 2082 (1527), zafixované | **vydání `2.1.0`** |
| 2026-10-12 | `fcb4ccb` + následující commit ([133](../decisions/133-an-add-on-package-over-a-framework-is-not-that-framework.md)) | 9936, zafixované (46 přeskočeno: tvary LINQ `Omitted`) | 2757 (2023), zafixované | **vydání `2.2.0`**; soudce nad 1000 řádky SF 0,1 v obou |
| 2026-10-15 | `86a102b` + následující commit ([140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md), [141](../decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md)) | 9931, host (137 přeskočeno: 46 tvarů LINQ `Omitted`, soudce a dotazy na cesty bez `LdbcSnb`) | 2765 (nepřepočteno), zafixované | na hostu běžely soudce a `LdbcPathQueryTest` zvlášť nad `mssql_db` (SF 1) |
| 2026-10-18 | `6ac5690` + následující commit ([150](../decisions/150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md)–[152](../decisions/152-an-association-path-that-cannot-be-derived-refuses-the-query-in-every-clause.md)) | 12021, host (1802 přeskočeno: běh FIBEN 1660, soudce LDBC 90, tvary LINQ `Omitted` 52) | nespuštěno | zafixovaný běh se zastavil pro nedostatek paměti hostitele |
| 2026-10-22 | `ee29333` | 12889, zafixované (1720 přeskočeno) | 4660 (nepřepočteno), zafixované (1660 přeskočeno) | první krok úplného běhu před [během FIBEN](2026-10-22-fiben-2.md); soudce nad 1000 řádky SF 0,1 v obou |

**Počet patří stromu**, ne stroji. Jiný součet znamená jiný strom, obvykle zastaralý obraz; prostředí posune jen rozdělení na prošlé a přeskočené, a přeskočené testy o F4 a F6 nic nedokazují.

**Pokrytí** se měří, nikdy není prahem; pro javovou sadu se neměří.

| Datum | Strom | Řádky | Větve | Poznámka |
|---|---|---|---|---|
| 2026-09-21 | `36d688d` | **80,0 %** (12 650 / 15 801) | **69,1 %** (8 047 / 11 642) | týchž 1551 testů; `Advisor` a `AdvisorBenchmarking` **0,0 %**, změřený základ oblasti 1 v `architecture.md` §9 |

## Podíl F4

`Catalog/CatalogMetadataShareTest` nad oběma schématy testovací databáze (`architecture.md` §5.2): podíl správně přečtených sledovaných metadat proti mezi 95 %, odchylky zapsané v testu.

| Datum | Strom | `TestSchema.sql` | LDBC | Testovací databáze | Odchylky |
|---|---|---|---|---|---|
| 2026-10-21 | `223e791` + následující commit, host | 163 / 163 | 299 / 299 | 462 / 462 = 100 % | žádné |

## Výkon překladu (S3)

Mez 30 s pro 100 entit a 100 dotazů přes `ConversionHandler.Convert`, EF Core → NHibernate, s ověřenými počty artefaktů; sestavení Release, 16 GB RAM. Záznamy předcházejí rozh. 095 a commit nejmenují.

| Datum | Test | Stroj | Naměřeno |
|---|---|---|---|
| 2026-08-21 | `TranslationPerformanceTest`, bez katalogu | Intel Core i9-9900 (8 jader), Windows 10 Pro | ~0,2 s |
| 2026-08-24 | `TranslationPerformanceTest`, bez katalogu | Intel Core i7-1065G7 (4 jádra), Windows 11 Pro | 0,4–0,7 s |
| 2026-08-26 | `CatalogTranslationPerformanceTest`, lokální SQL Server | Intel Core i9-9900 (8 jader), Windows 10 Pro | 0,4–0,5 s, čtení katalogu (`CatalogReadMilliseconds`) 210–330 ms |

Odstup je nejméně čtyřicetinásobný. V katalogové variantě odpovídají tři entity testovacímu schématu a `OrderLine` existuje jen díky klíči, který dodá katalog.

## Nesrovnalosti

- [2026-10-15, časy katalogu LDBC](2026-10-15-ldbc-catalog-times.md): věta o dotazech pod 0,2 s jmenuje i IC 14, jehož řádek tabulky ukazuje 2 243 ms.
- [2026-10-18, doba přehrání po krocích](2026-10-18-ldbc-judge-time-by-step.md): záznam říká, že turbo bylo zapnuté, ale čte nejvyšší stav procesoru `0x63`, při kterém je turbo vypnuté (rozh. 145); jedno z obojího je omyl.
