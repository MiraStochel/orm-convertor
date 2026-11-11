# 215 — Databázový obraz bere datovou sadu nejdřív z vlastní kopie projektu, potom od vydavatele, a soubor, jehož jméno zná seznam součtů v repozitáři, se s ním musí shodnout

Datum: 2026-11-11
Stav: platí
Požadavky: S5, T1
Podklad: rozhodnutí [110](110-ldbc-snb-as-a-second-reference-domain.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [186](186-the-build-environment-is-pinned-on-content-lock-files-image-digests-and-action-commits-moved-by-dependabot.md) a [208](208-sede-as-the-fourth-reference-domain-over-the-dba-stack-exchange-dump-judged-by-a-judge-of-our-own.md); vydání `data_sets` repozitáře projektu

## Kontext

`database.Dockerfile` stahuje datové sady při sestavení obrazu z adres jejich vydavatelů. Repozitář `IBM/fiben-benchmark` z GitHubu zmizel (404 pro repozitář i pro `data.zip` commitu `96eaffc`), takže `docker compose up --build` v čistém prostředí skončí chybou a projde jen s `FIBEN_DATASET=none`, bez databáze `Fiben`. Požadavek S5 přitom žádá, aby čisté prostředí systém sestavilo a sadu reprodukovalo jedním příkazem; rozhodnutí 186 připnulo základní obrazy a balíčky na obsah, což chrání před změnou, ne před zmizením. Stejně jako FIBEN může zmizet každý zdroj, který nedržíme.

K repozitáři projektu jsme proto připojili vydání `data_sets` s kopiemi archivů, jejichž licence šíření dovoluje:

| Příloha vydání | Co to je | Velikost |
|---|---|---|
| `fiben-benchmark-96eaffc-data.zip` | `data.zip` repozitáře `IBM/fiben-benchmark` v commitu, z něhož vznikl katalog (Apache-2.0; licence je přiložena jako `fiben-benchmark-LICENSE.txt`) | 80 MB |
| `social_network-sf0.1-CsvMergeForeign-StringDateFormatter.tar.zst` | data LDBC SNB Interactive v1, scale factor 0.1 — data profilu `test` | 17 MB |
| `validation_params-sf0.1.csv.zst` | validační sada téhož scale factoru, jeden soubor z archivu LDBC, zabalený | 12 MB |

Zbývá rozhodnout, jak sestavení kopie a adresy vydavatelů spojí, čím pozná, že dostalo správný soubor, a co udělá, když sadu nedostane.

## Zvažované varianty

1. **Jen vlastní kopie.** Sestavení by nestálo na žádné cizí adrese, ale vydání dnes nenese scale factor 1 systémového profilu ani jiné scale factory, které `LDBC_SCALE_FACTOR` nabízí; přepínač by přestal platit, dokud bychom nezkopírovali vše. Zamítáme.
2. **Vydavatel napřed, vlastní kopie jako záloha.** Sestavení by dál záviselo na tom, jak cizí server selže: zmizelá adresa odpoví 404 hned, ale pomalý nebo přesměrovaný server (archive.org shazuje spojení po hodině) by sestavení zdržel, než by na kopii došlo. A soubor od vydavatele by byl ten, který nikdo neověřil. Zamítáme.
3. **Data v repozitáři** (Git LFS nebo přímo). Klon by nesl stovky MB, které většina čtenářů nepotřebuje, a LFS má vlastní kvóty. Zamítáme.
4. **Vlastní kopie napřed, vydavatel potom, a součet v repozitáři u každého souboru, který držíme.**

## Rozhodnutí

**Volíme variantu 4.**

- **Pořadí adres.** Archiv se nejdřív hledá ve vydání `data_sets` (`ARG DATASETS_URL` v `database.Dockerfile`), a teprve když tam není, na adrese vydavatele. Jedno pravidlo platí pro každou sadu a každý scale factor: co do vydání přibude, začne sestavení brát odtud bez změny `database.Dockerfile`. FIBEN adresu vydavatele nemá, protože žádná není.
- **Součet.** `database/datasets.sha256` nese SHA-256 souborů, které držíme, pod jménem, jaké má soubor v obraze. Soubor, jehož jméno seznam zná, se s ním musí shodnout, **ať ho podala kterákoli adresa**; jiný obsah pod stejným jménem je selhané sestavení, ne jiná datová sada. Soubor, který seznam nezná, se bere, jak přišel. Seznam je v repozitáři, ne u vydání, protože přílohu vydání lze nahradit, kdežto commit ne — záznam běhu jmenuje commit (rozh. [095](095-a-dated-run-record-names-its-commit.md)) a s ním i data, nad kterými běžel.
- **Když sada není.** Sestavení skončí chybou, která jmenuje sadu a přepínač, jímž se obraz sestaví bez ní (`LDBC_SCALE_FACTOR=none`, `FIBEN_DATASET=none`). Obraz, který by se tiše sestavil bez databáze, by vypadal jako úplný systém a nepřítomnost by vyšla najevo až u běhu, který databázi potřebuje.
- **Jedno místo.** Stahování, pořadí adres a kontrolu součtu dělá `database/fetch-dataset.sh`; `database.Dockerfile` mu dává cíl a adresy. Validační sada, kterou LDBC vydává jen jako jeden archiv všech scale factorů, má vedle toho původní krok, který z archivu vybere soubor svého scale factoru, a nastupuje, jen když vlastní kopie chybí.

Jména datových sad, podle nichž zavaděče poznají načtenou databázi (`ldbc.dataset`, `ldbc.validation`, `fiben.dataset`), se nemění, takže existující svazek se znovu nenačítá.

## Důsledky

- **Čisté prostředí sestaví profil `test` bez cizí adresy datových sad** a systémový profil s databází `Fiben`. Záloha WideWorldImporters, scale factor 1 LDBC s jeho validační sadou a výpis SEDE se dál berou od vydavatelů; dokud jejich kopie ve vydání nejsou, zůstává to otevřenou položkou.
- **Sestavení závisí na vydání `data_sets`.** Vydání se nemaže a příloha se nenahrazuje jiným obsahem: nová sada je nová příloha a nový řádek seznamu součtů. Fork, který chce vlastní kopie, nastaví `DATASETS_URL`.
- **Nový archiv ve vydání** znamená řádek v `database/datasets.sha256`; bez něj se soubor stáhne, ale neověří.
- **`FIBEN_DATASET`** dál jmenuje commit zmizelého repozitáře; jiný commit než ten, jehož archiv držíme, sestavení odmítne.
- **Dotčená místa:** `database.Dockerfile`, `database/fetch-dataset.sh`, `database/datasets.sha256`; `architecture.md` (§6.2 a tabulky databází LDBC a FIBEN), `ORMConvertor/README.md`, `traceability.md` (S5).
