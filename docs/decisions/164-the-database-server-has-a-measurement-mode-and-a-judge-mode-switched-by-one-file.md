# 164 — Server databáze má dva režimy: měřicí, výchozí v obrazu i v compose, a režim soudce, do něhož ho přepne jeden soubor proměnných; paralelismus, paměť a procesory databáze bere obraz z proměnných a běh FIBEN měří jen nad serverem bez paralelismu

Datum: 2026-10-22
Stav: platí; nahrazuje větu o prostředí soudce LDBC v odstavci *Prostředí* a důsledek *Obraz databáze* rozh. 145
Požadavky: T2, T3, T7, F13, S5, S6
Podklad: rozhodnutí [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) (zafixované prostředí ve třech vrstvách), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) (soudce LDBC), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (běh FIBEN měří politikou 145), [039](039-container-configuration-of-the-environment.md) (jeden compose, jeden obraz databáze), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); záznamy *Whole-set runs* (2026-10-12 a 2026-10-14, mimo zafixované prostředí: 20 h a 25 h, obě sady bez neshody) a projekce *Time by step* z 2026-10-18 (v zafixovaném prostředí s turbem 2,5–5 dní pro .NET, 2,5–3 pro Javu) v `ORMConvertor/README.md`; běh FIBEN z 2026-10-22 nad `a2f498a` (103 min, z toho 18 min první čtení a zbytek měření); měření na MIS3 z 2026-10-22 (níž)

## Kontext

Rozhodnutí 145 zafixovalo měřicí prostředí ve třech vrstvách a hodnoty serveru napsalo přímo do obrazu: `init-db.sh` nastaví při každém startu `max degree of parallelism` 1 a `max server memory (MB)` 4096 a compose dává databázím pevně procesory 0–1. Soudce LDBC v tomtéž prostředí běží také, jen s turbem — jiné prostředí obraz neuměl. Soudce ale čas neměří: jeho veličinou je shoda řádků (117), a ta na paralelismu nezávisí; obě sady prošly celou sadou SF 1 v bězích z 12. a 14. 10., ještě před rozh. 145, se serverem bez meze paralelismu nad všemi osmi vlákny hostitele, bez jediné neshody. Zafixované prostředí je pro soudce jen cena: projekce z 18. 10. dává celou sadu za 2,5–5 dní na sadu proti 20 a 25 h obou záznamů, a hostitel přitom musí celé dny nedělat nic jiného, s oběma známými zabijáky běhu (Windows Update, nové vytvoření `mssql_db`). V menším totéž platí pro běh FIBEN: kontrola správnosti — překlad, kompilace a jedno čtení každé buňky — v něm zabere 18 ze 103 minut a nepotřebuje jednovláknové plány o nic víc než soudce.

Co soudce zdržuje, je procesor, ne paměť. Měření z 2026-10-22 na MIS3 (kontejner `mssql_db` s `cpuset` 0–1, VM se čtyřmi procesory a 8 GB):

| Veličina | Hodnota |
|---|---|
| procesory VM (`/proc/cpuinfo` v kontejneru) | 4 |
| procesory, které vidí SQL Server (`sys.dm_os_sys_info.cpu_count`, plánovače `VISIBLE ONLINE`) | 2 a 2 — server respektuje `cpuset`, takže `MAXDOP` 0 znamená počet procesorů kontejneru, ne VM |
| data (`used_pages`) | `LdbcSnb` 2 179 MB, `Fiben` 540 MB, `WideWorldImporters` 391 MB — každá databáze se do poolu 4 096 MB vejde |
| paměť, kterou kontejner vidí | 7,76 GiB (VM bez swapu) |

A tři vlastnosti compose, změřené zkušebním souborem s týmž zápisem (Docker Engine 29.8.2):

- `cpuset: "${X-0,1}"` s proměnnou `X` nastavenou na prázdný řetězec klíč z konfigurace vypustí a kontejner dostane všechny procesory VM (`cpuset.cpus.effective` 0–3); nenastavená proměnná dá 0–1.
- Seznam procesorů, které VM nemá (0–7 na čtyřech), Docker odmítne („Requested CPUs are not available").
- `docker compose up -d <služba>` bez týchž proměnných, s nimiž byla spuštěna databáze, na níž služba závisí, databázi **znovu vytvoří** s výchozími hodnotami; s `--no-deps` ji nechá být.

## Zvažované varianty

### 1 — Soudce dál v měřicím prostředí

Zamítáme. Platí se dny za vlastnost, kterou soudce nepotřebuje. Jednovláknový plán na dvou procesorech je správný pro měření, protože dává všem kandidátům týž tvar plánu a čas nezávislý na volných jádrech (145, varianta 3); soudce srovnává řádky, a ty jsou tytéž v jednovláknovém i paralelním plánu, jak ukázaly oba běhy celou sadou. Čím déle verdikt trvá, tím spíš ho ukončí něco zvenčí, a verdikt, který nedoběhne, není žádný.

### 2 — Přepnout server za běhu: `sp_configure` z řídicího spojení soudce před přehráním a zpět po něm, procesory `docker update --cpuset-cpus`

Zamítáme. Režim by žil v testovém kódu, ne v prostředí: přehrání, které spadne nebo které někdo zastaví, nechá server v režimu soudce, a další měření v něm tiše měří. `docker update` není v compose a další vytvoření kontejneru ho zapomene. Obě sady, .NET i Java, by musely nést totéž přepínání, a běh FIBEN potřetí.

### 3 — Vlastní databázová služba soudce s vlastním svazkem

Zamítáme. Druhá kopie `LdbcSnb` SF 1 a `Fiben` na disku, druhý server v téže VM s 8 GB a druhá instance aplikace pro javového soudce, jen aby jeden server měl jiná tři čísla. Rozh. 039 drží jeden obraz databáze pro systém i testy; dvě služby z téhož obrazu se liší jen konfigurací, a tu umí nést i jedna služba.

### 4 — Tři proměnné bez souboru, každá zvlášť na příkazové řádce

Zamítáme jako rozhraní, ne jako mechanismus. Tři hodnoty patří k sobě a běh s polovinou z nich není ani jedno prostředí. Příkaz soudce by je navíc musel opakovat u každého volání compose, i u spuštění instance aplikace pro javového soudce, jinak compose databázi vrátí do výchozích hodnot (výš).

### 5 — Režim jako jedna proměnná obrazu (`DATABASE_MODE=judge`), z níž `init-db.sh` odvodí hodnoty

Zamítáme. Procesory kontejneru nastavuje compose, ne obraz, takže režim by se stejně rozpadl na proměnnou compose a proměnnou obrazu; hodnoty režimu by byly schované ve skriptu místo v souboru, který si čtenář přečte, a stroj s jinou VM by je neměl jak změnit.

### 6 — Dva režimy: proměnné s měřicími výchozími hodnotami v obrazu a v compose, hodnoty soudce v jednom souboru repozitáře, který se předává `--env-file`

## Rozhodnutí

**Volíme variantu 6.**

**Mechanismus.** `init-db.sh` bere `max degree of parallelism` z proměnné `DATABASE_MAXDOP` a `max server memory (MB)` z `DATABASE_MAX_SERVER_MEMORY_MB`; bez nich nastaví hodnoty rozh. 145, 1 a 4096, takže obraz spuštěný čímkoli je v měřicím režimu. Hodnotu, která není celé nezáporné číslo, odmítne a kontejner skončí, a při každém startu napíše do logu, s jakými hodnotami server běží. Compose předává obě proměnné oběma databázovým službám, `mssql_db` i `test_db`, a jejich procesory bere z `DATABASE_CPUSET`, výchozí 0–1; prázdná hodnota znamená všechny procesory VM. Klienti zůstávají na procesorech 2–3 v obou režimech: v režimu soudce se o ně s databází dělí, což soudci nevadí, a měřicí režim zůstává takový, jaký ho 145 změřilo.

**Režim soudce** je soubor `ORMConvertor/judge.env`: `DATABASE_CPUSET` prázdné (všechny procesory, které VM má), `DATABASE_MAXDOP` 0 (paralelní plány přes všechny) a paměť 4 096 MB. Paměť soudce nezvyšujeme: největší data, `LdbcSnb`, mají 2,2 GB a do dnešního poolu se vejdou, a VM bez swapu by server, který přeroste svůj podíl, nezpomalila, ale ukončila — uprostřed vícedenního běhu. Proměnná paměti je tu pro VM, která paměti má víc. Velikost VM zůstává věcí hostitele jako v 145: soubor o ní nic nepředpokládá, protože prázdný `cpuset` a `MAXDOP` 0 vezmou, co VM dává, a dá-li hostitel pro běh soudce VM všech osm vláken, server je použije beze změny souboru. Soubor je v repozitáři, protože popisuje prostředí stejně jako compose, ne stav jednoho stroje.

**Přepnutí** je nové vytvoření databázového kontejneru s jinými proměnnými: `docker compose --env-file judge.env up -d mssql_db` do režimu soudce, `docker compose up -d mssql_db` zpět. Data svazku to přežijí (nový kontejner nad nahraným svazkem nahrávání přeskočí). V režimu soudce nese `--env-file judge.env` každý příkaz compose, který se databáze dotkne, i spuštění instance aplikace pro javového soudce; příkaz bez něj databázi vrátí do měřicího režimu a přeruší běžící přehrání, jako každé jiné nové vytvoření `mssql_db`.

**Kdo běží v kterém režimu.** Soudce LDBC, celou sadou i prefixem, v režimu soudce, s turbem jako dosud. Měření — Advisor, záznamy časů katalogu, měření běhu FIBEN — jen v měřicím režimu. Profil `test` zůstává ve výchozím, měřicím režimu, beze změny proti dnešku; s `--env-file judge.env` přejde do režimu soudce i on.

**Běh FIBEN se řídí serverem.** Na začátku přečte řídicím spojením nastavený `MAXDOP` databáze `Fiben` (konfiguraci s rozsahem databáze, je-li nenulová, jinak serveru), `max server memory` a počet procesorů, které server vidí, a zapíše je do souhrnu běhu. Měří jen tehdy, je-li `MAXDOP` 1; jinak měření vynechá a souhrn řekne proč. Týž příkaz je tedy nad serverem v režimu soudce kontrolou správnosti a nad serverem v měřicím režimu záznamem verze s mediány, a číslo z režimu soudce vzniknout nemůže. Rozhoduje `MAXDOP`, ne procesory a paměť, protože právě on mění tvar plánu (145, varianta 3) a jeho měřicí hodnota nezávisí na stroji; ostatní dvě hodnoty souhrn jen vykáže.

### Proč dva režimy, a ne kompromis

Mezi „jednovláknově na dvou procesorech" a „paralelně na všech" by šlo hledat hodnotu, která je pro měření ještě únosná a pro soudce už rychlá, třeba `MAXDOP` 2 na procesorech 0–2. Zhoršila by obojí: měření by dostalo zpět volbu plánu závislou na prahu ceny, kterou 145 odstranilo, a soudce by stál na polovině stroje. Soudce a měření se ptají na různé věci — soudce, zda řádky souhlasí, měření, kolik stojí jedno spuštění v prostředí, které je pro všechny kandidáty stejné —, a každá otázka má své nejlepší prostředí. Společný jim zůstává obraz, data a indexy, takže soudce ověřuje přesně ty texty nad přesně těmi daty, které měření měří.

### Proč soubor, a ne proměnné

Soubor dává režimu jméno, které stojí v každém příkazu, a obsah, který se dá přečíst: kdo uvidí `--env-file judge.env`, ví, v jakém prostředí příkaz běží, a kdo otevře soubor, ví proč. Výchozí je měřicí režim, takže zapomenutý soubor dá pomalejší, ale platné prostředí, nikdy rychlejší a neplatné pro měření. Cena je past, kterou měření výš ukázalo: v režimu soudce musí soubor nést každý příkaz compose nad systémem, jinak compose databázi vrátí zpět. Past není nová — vznikla s každým jiným rozdílem konfigurace — a `ORMConvertor/README.md` ji říká u příkazů soudce.

### Proč běh FIBEN hlídá MAXDOP sám

Rozh. 145 drží prostředí tím, co nese obraz, a záznamem, který říká, co obraz nést nemůže (turbo). Se dvěma režimy nese obraz obojí, a měření nad serverem v režimu soudce by nedalo číslo s výhradou, nýbrž číslo jiné veličiny — plán a čas závislé na volných jádrech. Běh, který měří, se proto na režim ptá serveru, jako se soudce LDBC ptá na `ldbc.validation`. Advisor se neptá: měří nad serverem, který mu dá uživatel, a ten nemusí být náš obraz; jeho záznam na MIS3 říká režim stejně jako turbo.

## Důsledky

- **Obraz databáze:** `database/init-db.sh` bere `DATABASE_MAXDOP` a `DATABASE_MAX_SERVER_MEMORY_MB` s výchozími 1 a 4096, hodnotu kontroluje a píše ji do logu; důsledek *Obraz databáze* rozh. 145 (hodnoty napevno) tím přestává platit.
- **Compose:** `mssql_db` a `test_db` berou `cpuset` z `DATABASE_CPUSET` (výchozí `0,1`) a obě proměnné předávají kontejneru; `ORMConvertor/judge.env` nese režim soudce.
- **Běh FIBEN** (.NET `FibenJudge/FibenRun`, Java `fiben/FibenRun`): nastavení serveru v souhrnu, měření jen při `MAXDOP` 1.
- **Dokumentace:** `architecture.md` §8 (dva režimy) a §6.2 (běh FIBEN), `ORMConvertor/README.md` (*Docker*, *The LDBC judge*, *The FIBEN run*: příkazy s `--env-file judge.env` a past nového vytvoření).
- **Čísla:** záznam běhu soudce od tohoto rozhodnutí říká režim; projekce *Time by step* z 18. 10. platí pro měřicí režim a o režimu soudce řekne první běh.
