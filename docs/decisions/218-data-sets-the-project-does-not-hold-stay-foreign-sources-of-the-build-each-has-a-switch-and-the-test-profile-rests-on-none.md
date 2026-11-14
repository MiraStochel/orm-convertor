# 218 — Datové sady, které projekt nedrží, zůstávají cizími zdroji sestavení: každou lze vynechat přepínačem, profil `test` na žádné nestojí a cizí zdroje sestavení jsou vypsané na jednom místě

Datum: 2026-11-14
Stav: platí; nahrazuje důsledek rozh. 215 o kopiích zbylých sad
Požadavky: S5
Podklad: rozhodnutí [039](039-container-configuration-of-the-environment.md), [186](186-the-build-environment-is-pinned-on-content-lock-files-image-digests-and-action-commits-moved-by-dependabot.md) a [215](215-the-database-image-takes-a-data-set-from-the-projects-own-copy-first-and-checks-it-against-a-committed-checksum.md); `database.Dockerfile`, `docker-compose.yml`

## Kontext

Rozhodnutí 215 zavedlo vlastní kopie datových sad ve vydání `data_sets` a nechalo otevřené, zda do něj přibudou i zbylé archivy: data LDBC scale factoru 1 s validační sadou, výpis SEDE a záloha WideWorldImporters. Ty tři se při sestavení dál berou od vydavatelů.

Stav před tímto rozhodnutím měl tři vady, které s otázkou kopií souvisejí:

- **Záloha WideWorldImporters neměla přepínač.** Stahovala se v prvním kroku `database.Dockerfile`, mimo `database/fetch-dataset.sh`, takže na adrese vydání `Microsoft/sql-server-samples` stály oba profily — i profil `test`, jehož sady vzorovou databázi nečtou.
- **Výpis SEDE a validační sada LDBC selhávaly bez rady.** Sestavení skončilo chybou stahování a neřeklo, jak obraz sestavit bez sady, ačkoli přepínače `SEDE_DATASET=none` a `LDBC_SCALE_FACTOR=none` existují.
- **Cizí zdroje sestavení nebyly vypsané na jednom místě.** Datové sady byly u jednotlivých databází, základní obrazy a balíčky v rozhodnutí 186.

Požadavek S5 žádá, aby čisté prostředí systém sestavilo a testovací sadu reprodukovalo jedním příkazem. Je třeba rozhodnout, co z toho má projekt držet sám.

## Zvažované varianty

1. **Kopie všech sad ve vydání.** Sestavení by nestálo na žádné cizí adrese datových sad. Jenže na cizích adresách stojí tak jako tak: základní obrazy z `mcr.microsoft.com` a z Docker Hubu, balíčky NuGet a Maven Central, balíčky `apt`. Rozhodnutí 186 je připíná na obsah, nedrží je — a projekt je držet nemůže. Kopie datových sad by tedy odstranila jednu třídu cizích zdrojů z několika, a to za cenu, že se projekt stane distributorem stovek MB cizích dat s licenčními povinnostmi u každé sady zvlášť (výpis SEDE je obsah uživatelů Stack Exchange pod CC BY-SA 4.0). Zamítáme.
2. **Sestavení, které nedostupnou sadu tiše vynechá.** Systém by se sestavil vždy. Rozhodnutí 215 tuto cestu zamítlo a důvod trvá: obraz bez databáze by vypadal jako úplný systém a nepřítomnost by vyšla najevo až u běhu, který databázi potřebuje. Zamítáme.
3. **Sady, které projekt nedrží, jsou vyslovené cizí zdroje sestavení; každou lze vynechat přepínačem a profil `test` na žádné nestojí.**

## Rozhodnutí

**Volíme variantu 3.** Měřítkem není, zda sestavení stojí na cizí adrese — to stojí vždy —, ale co se stane, když adresa zmizí. Odpověď má dvě části: co S5 dokazuje, se staví z toho, co držíme; všechno ostatní je volitelné a jeho nepřítomnost je vidět.

- **Co projekt drží.** Vydání `data_sets` nese to, z čeho se staví profil `test` (data LDBC scale factoru 0.1 s validační sadou), a to, co už vydavatele nemá (FIBEN). Zbylé kopie nepřidáváme. Pořadí adres rozhodnutí 215 platí dál pro každou sadu, takže kopie, která do vydání přibude, se začne brát odtud beze změny `database.Dockerfile`.
- **Profil `test` nestojí na žádné adrese vydavatele datové sady.** `test_db` se staví s `WWI_BACKUP=none` vedle `FIBEN_DATASET=none` a `SEDE_DATASET=none`; zbývají data, jejichž kopii držíme. Reprodukce testovací sady, kterou S5 dokazuje, tak závisí jen na vydání `data_sets` a na připnutém prostředí rozhodnutí 186.
- **Každá sada databázového obrazu má přepínač, který obraz sestaví bez ní.**

  | Sada | Přepínač | Sestavení bez ní |
  |---|---|---|
  | záloha WideWorldImporters | `WWI_BACKUP=none` | systém bez vzorové databáze; katalog instance jmenuje `ORMCONVERTOR_CATALOG_DATABASE` |
  | data LDBC a jejich validační sada | `LDBC_SCALE_FACTOR=none`, nebo `0.1` z vlastní kopie | bez `LdbcSnb`, nebo `LdbcSnb` ve scale factoru profilu `test` |
  | FIBEN | `FIBEN_DATASET=none` | bez `Fiben` |
  | výpis SEDE | `SEDE_DATASET=none` | bez `Sede` |

- **Sada, která není nikde, je selhané sestavení, které jmenuje sadu a její přepínač** — pravidlo rozhodnutí 215 teď platí pro všech pět stahovaných souborů, ne jen pro data LDBC a FIBEN. Záloha WideWorldImporters a výpis SEDE se hledají nejdřív ve vlastní kopii jako ostatní; výpis SEDE si pro adresu vydavatele nechává vlastní krok s navazováním přerušeného stahování a kontrolu MD5.
- **Jedno místo.** Cizí zdroje sestavení — základní obrazy, balíčky, datové sady, akce CI —, čím je každý připnutý a co sestavení udělá bez něj, vypisuje sekce *External dependencies of the build* v [`ORMConvertor/README.md`](../../ORMConvertor/README.md#external-dependencies-of-the-build). Ostatní dokumenty na ni odkazují.

## Důsledky

- **Zmizí-li vydavatel, systém se sestaví s přepínačem a ztratí jednu databázi.** Bez LDBC scale factoru 1 nelze v čistém prostředí zopakovat celosadový verdikt soudce nad ním; `LDBC_SCALE_FACTOR=0.1` staví `LdbcSnb` z vlastní kopie. Bez výpisu SEDE nelze zopakovat běh SEDE, který je mimo záruky. Bez zálohy WideWorldImporters nemá systémový profil vzorovou databázi a Advisor, který je vyňatý, nemá nad čím měřit. Překlad sám databázi nepotřebuje.
- **Existující svazek se nemění.** Jména datových sad, podle nichž zavaděče poznají načtenou databázi, zůstávají; `init-db.sh` vzorovou databázi obnoví, jen když svazek zálohu nese, a svazek poprvé naplněný obrazem bez ní zůstává bez vzorové databáze.
- **Co od vydavatele přijde neověřené:** data LDBC mimo scale factor 0.1 a záloha WideWorldImporters se berou, jak přišly; výpis SEDE se ověřuje proti MD5 položky. Záznam běhu jmenuje commit a jméno sady (rozh. [095](095-a-dated-run-record-names-its-commit.md)).
- **Balíčky `apt` obrazů připnuté nejsou**; seznam cizích zdrojů to říká.
- **Otevřená položka o kopiích zbylých sad zaniká.**
- **Dotčená místa:** `database.Dockerfile` (`ARG WWI_BACKUP`, hlášení u validační sady a u výpisu SEDE), `database/init-db.sh`, `docker-compose.yml`; `ORMConvertor/README.md`, `architecture.md` (§6.2), `traceability.md` (S5), kořenové `README.md`.
