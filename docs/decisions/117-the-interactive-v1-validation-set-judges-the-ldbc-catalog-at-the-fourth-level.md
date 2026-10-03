# 117 — Validační sada LDBC Interactive v1 je soudcem čtvrtého stupně nad katalogem LDBC: přehrává se v pořadí, vkládání nese loader, čtení generovaný artefakt a databáze se vrací kompenzací

Datum: 2026-10-03
Stav: revidováno
Požadavky: F13, T2, T3, S2, S5
Podklad: rozhodnutí [016](016-generated-artifact-verification-levels.md), [027](027-query-artifact-verification.md), [039](039-container-configuration-of-the-environment.md), [078](078-java-suite-as-a-client-of-a-running-instance.md), [087](087-an-integration-test-is-a-run-against-the-database.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [099](099-examples-are-content-not-a-choice.md), [110](110-ldbc-snb-as-a-second-reference-domain.md) a [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md); specifikace *The LDBC Social Network Benchmark*, verze 2.2.4, kapitola 6 (zátěž Interactive) a příloha A (definice čtení IS 1–7, IC 1–14 a vkládání INS 1–8); README referenčních implementací `ldbc_snb_interactive_v1_impls` (oddíl o validačních parametrech) a dokumentace driveru `ldbc_snb_interactive_v1_driver` (*Validating a Database Connector*); archiv `validation_params-interactive-v1.0.0-sf0.1-to-sf10.tar.zst` na `datasets.ldbcouncil.org/interactive-v1/`; [`architecture.md`](../architecture.md) §6.2 a §9

## Kontext

Rozhodnutí 110 dalo nástroji měřítko, které si nevybral: 41 čtecích dotazů LDBC Social Network Benchmark nad daty Interactive v1 ve scale factoru 1. Zároveň ale výslovně řeklo, že katalog drží jen **první stupeň** ověření podle rozhodnutí 016 — každý text se spustí nad prázdnými tabulkami LDBC a každý přeložený dotaz projde do každého cíle, který ho neodmítá —, a čtvrtý stupeň nad LDBC nechalo jako otevřenou práci. Dnes katalog tvrdí 35 dotazů *přeložených podle specifikace* a 6 *se zjednodušením*, žádný nepřeložený ([`architecture.md`](../architecture.md) §6.2).

**Stav „podle specifikace" je tvrzení autora a nic jiného ho nedrží.** Text dotazu v T-SQL jsme napsali sami podle definice ve specifikaci a porovnali čtením s referenčními implementacemi LDBC; žádný test ale neví, co má dotaz nad daty vrátit. Diferenční ověření rozhodnutí 089 tu nepomůže, a je důležité říct proč: porovnává **cíl se zdrojem**. Když text zdroje čte specifikaci špatně — vynechá podmínku, zamění směr hrany, špatně spočítá vzdálenost —, všech šest cílů vrátí stejně špatné řádky a matice je zelená. Rozhodnutí 110 se tak zbavilo slabiny *dotazy píše autor nástroje* u výběru dotazů, ale ne u jejich správnosti: tu dál soudí týž člověk, který je psal. Je to přesně ta mezera, kterou F13 nejmenuje a kterou T3 („funkční ekvivalence") předpokládá zaplněnou — podíl správných výsledků se nedá spočítat bez někoho, kdo ví, co je správně.

**LDBC takového soudce publikuje.** Pro zátěž Interactive v1 — a jen pro ni, to byl jeden z důvodů, proč ji 110 zvolilo — jsou k dispozici **validační parametry** pro scale factory 0,1 až 10, v jednom archivu, vyrobené referenční implementací nad Neo4j a ověřené proti ostatním implementacím. Soubor je po řádcích `operace|očekávaný výsledek`, obojí JSON, a vznikl jednovláknovým během driveru, který čte prokládá **vkládáním z proudu aktualizací** (INS 1–8: nová osoba, like, fórum, členství, příspěvek, komentář, přátelství). Pro SF 1 má přes 138 tisíc řádků. Řádek se čtením nese parametry a výsledek, který platí **ve stavu databáze v okamžiku toho řádku**, tedy po všech vkládáních před ním; řádek s vkládáním nese parametry a výsledek žádný. Nic takového pro zátěž BI nad daty Interactive v1 publikované není.

**Data v `LdbcSnb` tomu stavu neodpovídají.** Loader rozhodnutí 110 načítá hromadnou část data setu — 90 % sítě — a proud aktualizací vědomě nenačítá (`database/ldbc/load.sql`). Čtení ze sady spuštěné nad takovou databází dá u části řádků jiný výsledek, než sada očekává, a to ne proto, že by byl dotaz špatně, nýbrž proto, že chybí řádky, které driver před tím čtením vložil. Soudce, který se někdy mýlí, je horší než žádný: neshoda pak neříká nic.

Rozhodnout je proto třeba čtyři věci: **jak uvést databázi do stavu, který sada předpokládá**, **kde a kdy to poběží**, **jak se porovnají tvary výsledků** a **co se tím tvrdí o katalogu**.

## Zvažované varianty

### 1 — Vzít jen čtení, kterých se žádné vkládání netýká

Přehrávat nic a ze sady použít jen řádky čtení před prvním vkládáním, nebo ty, jejichž výsledek žádné předchozí vkládání nemohlo změnit.

Zamítáme. První čtení v sadě závisí na vkládáních velmi brzy, takže prefix před prvním vkládáním je malý. A „netýká se ho" nelze spočítat: IC 1 čte okolí osoby do tří kroků, takže je ovlivněné každým novým přátelstvím kdekoli v tom okolí, IC 2 a IC 9 čtou zprávy přátel před datem, IS 1–7 čtou přímo to, co INS 1–8 vkládají. Vybrat čtení, která *nejspíš* sedí, by navíc znamenalo vybírat ze soudce ta tvrzení, která se nám hodí — tatáž předpojatost, kvůli které 110 LDBC vůbec zvolilo.

### 2 — Načíst celý proud aktualizací předem a čtení spustit potom

Loader by načetl i `updateStream_*.csv`, databáze by držela 100 % sítě a čtení by se spustila nad hotovým stavem.

Zamítáme. Očekávané výsledky nejsou výsledky nad koncovým stavem, nýbrž nad **mezistavy**: čtení na pozici *k* vidí vkládání 1 až *k* − 1 a žádné pozdější. Dotaz s datovým oknem nebo s pořadím podle data (IC 2, IC 8, IC 9, IS 2, IS 7) vrátí nad koncovým stavem řádky, které v okamžiku čtení ještě neexistovaly, a výřez `TOP` je posune. Soudce by hlásil rozdíly, které nejsou, a to je přesně vlastnost, kterou jsme v kontextu označili za diskvalifikující.

### 3 — Vyrobit si vlastní očekávané výsledky referenční implementací

Spustit referenční implementaci LDBC nad PostgreSQL s driverem v režimu tvorby validačních parametrů nad týmiž daty a soudit podle jejího výstupu.

Zamítáme. Je to další systém k provozu — PostgreSQL, JDK, driver — jen kvůli tomu, abychom znovu vyrobili, co LDBC publikovalo, a vyrobili to hůř: publikovaná sada je křížově ověřená mezi implementacemi, naše by byla výstupem jedné. A problém, kvůli kterému se varianta nabízí, neřeší: driver vkládání prokládá stejně, takže přehrávat bychom museli tak jako tak.

### 4 — Přehrávat v jedné transakci a na konci ji vrátit

Vkládání i čtení v jediné transakci nad sdílenou `LdbcSnb`, `ROLLBACK` na konci, databáze beze změny.

Zamítáme z důvodu architektury, ne pohodlí. Čtení neprovádí testovací kód přímo, nýbrž **generovaný artefakt** — `DbContext` EF Core, session NHibernate, `SqlConnection` Dapperu, a na javové straně Hibernate, EclipseLink a MyBatis přes JDBC —, a každý si otevírá vlastní spojení, které nevidí nepotvrzené řádky cizí transakce. Zapojit je do jedné transakce znamená u .NET sdílené spojení nebo eskalaci na distribuovanou transakci, kterou SQL Server v kontejneru na Linuxu nemá, a u javové sady hranici procesu, přes kterou žádná lokální transakce nevede. Rozhodnutí 089 tutéž hranici obešlo read-only fixturou; tady fixtura read-only být nemůže, protože soudce vkládání předepisuje.

### 5 — Kopie databáze na každý běh

`BACKUP`/`RESTORE` nebo databázový snapshot před přehráváním a návrat po něm.

Zamítáme. Kopie 3,3 GB na každý běh každé sady je minuty navíc, snapshot i obnova chtějí výhradní přístup a práva `dbcreator`/`sysadmin`, a javová sada se na MIS2 přihlašuje SQL loginem s právy `db_owner` jedné databáze — záměrně, podle zásady, že testovací login nemá vidět nic jiného. Varianta by tedy buď rozšířila práva testovacího loginu, nebo by na jednom ze dvou strojů neběžela.

### 6 — Přehrát v pořadí, vkládání potvrdit a databázi vrátit kompenzací

## Rozhodnutí

**Volíme variantu 6. Validační sada Interactive v1 je soudcem čtvrtého stupně rozhodnutí 016 nad katalogem LDBC. Nahrává se do databáze `LdbcSnb` vedle dat, které soudí; každá sada ji přehrává v pořadí řádků — vkládání jako DML z repozitáře, potvrzené, čtení jako běh generovaného artefaktu každého cíle, který sada vlastní, proti očekávanému výsledku převedenému do kanonického tvaru rozhodnutí 089 —, a před přehráváním i po něm vrátí databázi kompenzací do hromadně načteného stavu.**

### Soudce bydlí v databázi, kterou soudí

Sada se nahrává do tabulky `ValidationOperation` databáze `LdbcSnb` — pozice řádku, typ operace, parametry a očekávaný výsledek, poslední dva jako text JSON tak, jak jsou v souboru — týmž loaderem, který načítá data. Archiv validačních parametrů se stahuje při sestavení obrazu databáze jako archiv dat (kontejner startuje bez sítě, rozhodnutí 110); obraz si z něj nechá soubor svého scale factoru. Hotové načtení zapíše do druhé rozšířené vlastnosti databáze (`ldbc.validation`) jméno sady; databáze s daty a bez ní dostane sadu donačtenou, data se kvůli tomu znovu nenačítají.

Tři věci z toho plynou a všechny jsou záměr. **Scale factor se nedá splést:** loader zná scale factor dat, která načítá, a vezme sadu téhož, takže soudce vždy odpovídá datům, nad kterými sedí. **Obě sady čtou totéž:** .NET i javová sada se připojují k `LdbcSnb` tak jako tak, žádný další mechanismus sdílení souboru mezi dvěma ekosystémy nevzniká — tutéž zásadu zavedlo rozhodnutí 076 pro schéma a 089 pro kanonické výsledky. A **soudce je nezávislý na ukázkových parametrech katalogu:** řádek sady nese vlastní parametry, takže katalog může dál ukazovat hodnoty zvolené nad SF 1 a soudit se dá nad kterýmkoli publikovaným scale factorem.

### Vkládání nese loader, čtení generovaný artefakt

**INS 1–8 jsou DML, ne překlad.** Nástroj překládá dotazy jen pro čtení (rozhodnutí 030, 053) a vkládání do osmi tabulek s výpočtem kořene vlákna a přátelstvím v obou směrech je práce téhož druhu, jakou dělá `load.sql`. Operace proto stojí jako osm parametrizovaných skriptů T-SQL v `database/ldbc/updates/` vedle `schema.sql`, se zástupným `{{schema}}`, s parametry pojmenovanými podle polí operace; INS 6 a INS 7 odvodí `RootPostId`, `ContainerForumId` a `RootPostLanguage` nové zprávy z rodiče, jako to dělá loader u hromadné části, INS 8 zapíše přátelství v obou směrech. Obě sady skripty jen vážou a spouštějí, nic víc o operaci neví.

**Čtení IS 1–7 a IC 1–14 je běh generovaného artefaktu**, tak jako v diferenční matici: .NET sada spouští Dapper, EF Core a NHibernate, javová Hibernate, EclipseLink a MyBatis, každá ve svém běhovém prostředí, a cíl, který dotaz píše nativním SQL (`Fallback`, rozhodnutí 113), běží jako kterýkoli jiný překlad. Artefakt vzniká převodem z Dapperu s **`LdbcSnb` jako katalogem** — s databází, nad kterou poběží —, protože doplnění z katalogu píše do mapování jméno schématu a artefakt doplněný nad prázdným schématem fixture by nad `dbo` databáze `LdbcSnb` nenašel tabulky. Javová sada k tomu potřebuje instanci aplikace s katalogem `LdbcSnb` (rozhodnutí 078): v compose je to druhá služba `test_app_ldbc` téhož obrazu s katalogem `LdbcSnb`, na MIS2 druhý běh aplikace s `ConnectionStrings__CatalogDatabase` na `LdbcSnb`.

**Zdrojová varianta soudí text, přeložené soudí překlad.** Jako v rozhodnutí 089 běží nejdřív zdroj: Dapper s textem katalogu proti soudci. Neshoda tam znamená, že **text čte specifikaci špatně** — oprava textu, obsah podle 110. Shoda zdroje a neshoda cíle znamená **chybu překladu** — vada nástroje. Čtvrtý stupeň tím od sebe odděluje dvě věci, které první stupeň odlišit neumí, a teprve to z katalogu dělá měřítko překladu, ne měřítko autorova čtení.

### Databáze se vrací kompenzací a běh se sám uzdravuje

Vkládání se potvrzují, aby je artefakty viděly ze svých spojení. Po přehrání vrátí sada databázi do hromadně načteného stavu **kompenzací**: operace INS 1–8 jsou všechny vkládání s klíči, které sada jmenuje v parametrech — id osoby, zprávy, fóra, dvojice (osoba, zpráva), (osoba, fórum), (osoba, osoba) —, takže k nim existuje mazání po klíči v pořadí respektujícím cizí klíče, jeden skript `database/ldbc/updates/undo.sql` nad touž tabulkou `ValidationOperation`. Mazání neexistujícího řádku je prázdná operace, a proto je kompenzace **idempotentní a nezávislá na tom, kolik z přehrávání proběhlo**: sada ji spustí **před** přehráváním i po něm. Běh, který spadl uprostřed — zabitý kontejner, výjimka artefaktu —, nenechá po sobě nic, co by příští běh napřed neuklidil, bez práv nad rámec `db_owner` a bez snapshotu.

Dvě sady nad jednou databází nesmějí přehrávat zároveň, protože by si navzájem měnily stav. Přehrávání drží po dobu svého spojení zámek `sp_getapplock` nad `LdbcSnb` a druhé čeká; zámek vázaný na spojení zaniká s ním, takže ani tady spadlý běh nic nezablokuje. Řádky soudce, které čtou osobu nebo zprávu vloženou dřív v sadě, jsou tím obslouženy přirozeně — stav v okamžiku čtení je přesně stav po předchozích řádcích.

### Co je shodný výsledek

Očekávaný výsledek se **převádí do kanonického tvaru rozhodnutí 089** a porovnává se stejnými pravidly a stejným vykreslovačem (`ResultRow`, `RendererConformance`) jako řádky diferenční matice; nový tvar porovnání nevzniká. Převod určuje **vazba** u každého dotazu Interactive v katalogu — která operace driveru dotazu odpovídá, které parametry operace jsou které parametry textu, které pole výsledku je který sloupec projekce, a která pole jsou seznamy —, **po jménech polí**, pod kterými driver parametry i výsledek zapisuje: řádek sady je dvojice objektů JSON a svou operaci nejmenuje, loader ji pozná podle pole, které má jen ona. Pravidla, která přibývají nad 089, jsou čtyři:

- **Okamžiky.** Driver zapisuje data a okamžiky jako čísla milisekund od epochy v UTC; převádějí se na okamžik vykreslený podle 089 s přesností, kterou tvrdí mapování sloupce (`DATETIME2(3)`, rozhodnutí 079). `Birthday` je datum bez času a vykreslí se jako datum.
- **Seznamy.** Specifikace vrací e-maily, jazyky, univerzity a firmy jako seznamy; katalog je od rozhodnutí 113 píše jako `STRING_AGG` s oddělovačem. Porovnávají se **jako množiny prvků**: obě strany se rozloží na prvky, seřadí po kódových bodech a spojí, protože specifikace pořadí uvnitř seznamu nedefinuje a `STRING_AGG` bez `WITHIN GROUP` ho nezaručuje. Prvek, který je sám n-ticí (jméno, rok, místo), se porovnává jako text spojený čárkami, jak ho katalog píše.
- **Jeden řádek.** Operace, jejíž výsledek je jediný objekt (IS 1, IS 4, IS 5, IS 6, IC 13), se převádí na výsledek o jednom řádku.
- **Pravdivostní hodnoty.** Driver zapisuje příznak (IS 7, IC 7) jako `true`/`false`, katalog jako 1 a 0; porovnává se to číslo, a vrátí-li cíl pravdivostní hodnotu, převede se na ně také.

Pořadí rozhoduje mezireprezentace jako v 089: každý dotaz Interactive řazení nese a specifikace ho dotahuje až po identifikátor, takže se řádky porovnávají v pořadí — s jedinou výjimkou IC 14, kterou specifikace řadí jen podle váhy cesty, takže dvě cesty téže váhy smějí přijít v libovolném pořadí; ty vazba označí a porovnají se jako množina. Hodnoty, ne typy, tak jako tam. Výsledky Interactive nemají desetinná čísla a jediné přibližné, váhu cesty IC 14 v násobcích jedné poloviny, vykreslí výchozí nastavení přesně, takže nastavení přesnosti zůstává výchozí.

### Co se soudí a co se měří

**Soudce drží tvrzení „přeloženo podle specifikace".** Každý dotaz Interactive s tímto stavem musí odpovídat soudci na **každém** řádku čtení své operace — jako zdroj i v každém cíli, který ho neodmítá — a je to aserce; neshoda jediného řádku je červený test, protože to je přesně význam toho stavu. Dnes je takových dotazů 18 z 21.

**Dotaz se zjednodušením se neposuzuje, měří se.** IC 7 (minuty latence přes `DATEDIFF`), IC 13 a IC 14 (cesty do tří kroků) tvrdí v poznámce, čím se od specifikace liší; soudce nad nimi běží také, ale sada u nich zapíše **podíl shodných čtení** do výstupu běhu místo aserce. Číslo říká, jak časté zjednodušení ve skutečnosti je, patří do metrik T3, a dotaz, u kterého vyjde 100 %, je kandidát na změnu stavu — práce podle 110, ne rozhodnutí. Pevně zakódovat tolerance podle poznámky by znamenalo psát soudce znovu rukou, tedy přesně to, čeho se zbavujeme.

**Zátěž BI zůstává na prvním stupni.** LDBC pro ni nad daty Interactive v1 očekávané výsledky nepublikuje a vyrábět si je sami zamítla varianta 3. BI 1–20 dál drží test rozhodnutí 110 a jejich stav je dál tvrzení autora — a §9 i [`subset.md`](../subset.md) to musí říkat tímhle slovem.

**Negativní polovina** nevyžaduje fixturu navrženou proti mutacím jako v 089, protože soudce je nezávislý na nás: mutace vydaného artefaktu podle 089 (vypuštěný filtr, obrácený operátor, vypuštěné řazení, změněný počet řádků, prohozená pole) se aplikuje na jeden dotaz Interactive v každém cíli a musí skončit neshodou se soudcem, aby bylo doloženo, že porovnání na chybu reaguje.

### Kde to běží

Soudce potřebuje `LdbcSnb` **s daty**, kterou testovací databáze rozhodnutí 016 nemá a mít nemá (110, varianta 3). Připojení je proto vlastní konfigurace vedle `ConnectionStrings:TestDatabase`: `ConnectionStrings:LdbcDatabase` pro .NET sadu a `ORMCONVERTOR_TEST_LDBC_JDBC_URL` s `ORMCONVERTOR_LDBC_API_URL` pro javovou, bez nich se testy soudce **přeskočí s uvedeným důvodem**. `ORMCONVERTOR_REQUIRE_TEST_DATABASE` slibuje testovací databázi, ne data set, a tak se na soudce nevztahuje; prostředí, které slibuje i soudce, to řekne vlastní proměnnou `ORMCONVERTOR_REQUIRE_LDBC_DATABASE=1` a přeskočení je tam selháním (zásada rozhodnutí 039).

| Prostředí | Soudce | Jak |
|---|---|---|
| compose, profil `test` (MIS3) | ano, výchozí **SF 0,1** | `test_db` se staví z `database.Dockerfile` jako `mssql_db`, s `LDBC_TEST_SCALE_FACTOR` (výchozí `0.1`: načtení v sekundách, sada malá); `test_db_init` čeká na rozšířenou vlastnost `ldbc.validation`; `tests` i `java_tests` dostávají připojení a `REQUIRE` a přehrávají prefix 1000 řádků (`ORMCONVERTOR_LDBC_VALIDATION_ROWS`, prázdná proměnná celou sadu); `test_app_ldbc` je instance s katalogem `LdbcSnb` |
| compose, systém (MIS3) | ano, SF 1 | `mssql_db` nese sadu vedle dat; verdikt o katalogu nad SF 1 se bere tady |
| MIS2, SQLEXPRESS | ano, SF 1 | `LdbcSnb` dostane sadu donačtenou skriptem loaderu; druhý běh aplikace s katalogem `LdbcSnb` pro javovou sadu; kontrola, ne verdikt (prostředí není zafixované) |
| CI | ne | servisní kontejner je holý obraz bez data setu; soudce se přeskočí a `ORMCONVERTOR_REQUIRE_LDBC_DATABASE` tam nastavená není. Načíst SF 0,1 do CI je práce experimentální pipeline, ne tohoto rozhodnutí |

**Prefix sady je sám soudcem.** Protože stav po *k* řádcích je konzistentní, je prvních *k* řádků sady platnou menší sadou. Běh smí přehrávání zastavit po nastaveném počtu řádků (`ORMCONVERTOR_LDBC_VALIDATION_ROWS`); bez nastavení přehrává celou sadu. Celá sada nad SF 1 na MIS3 je verdikt, prefix je kontrola — stejná dvojice slov, jakou používá *Two machines* pro MIS2.

### Co to tvrdí

Soudce **neposouvá hranici nároku** a §9 ani sekce *Guarantees* kořenového README nedostávají novou větu o LDBC: podmnožina konstrukcí je dál vymezená [`subset.md`](../subset.md) a 113. Mění se **doklad**: stav „podle specifikace" u dotazů Interactive drží od implementace čtvrtý stupeň proti nezávisle publikovanému výsledku, ne čtení autora, a [`traceability.md`](../traceability.md) to u F13 a T2 zapíše jako doklad vedle diferenční matice. Nejde o běh benchmarku LDBC ani o výsledek LDBC Benchmark: sada je použitá jako testovací oracle podle licence CC BY 4.0 a zásad férového užití LDBC, tak jako data (110).

## Důsledky

**Katalog dostává u každého dotazu Interactive vazbu na soudce** — operaci driveru, pořadí parametrů, pole výsledku a seznamy — a test tvrdí, že ji má každý z 21 dotazů. Je to obsah ve smyslu rozhodnutí 099 a 110; rozhodnutím je, že vazba existuje a že dotaz Interactive bez ní je selhání testu, ne dotaz mimo soud.

**Loader roste o sadu, INS 1–8 a kompenzaci.** `database.Dockerfile` stahuje druhý archiv (přes 200 MB, nechá si soubor svého scale factoru), `load-ldbc.sh` zapisuje druhou rozšířenou vlastnost a umí donačíst sadu do databáze, která už data má; `database/ldbc/updates/` nese osm skriptů vkládání a jeden mazání. Skript na MIS2 (*The databases on MIS2*) sadu donačte bez opakování 70sekundového načtení dat.

**Compose profil `test` se mění** — `test_db` z holého obrazu na obraz databáze se scale factorem 0,1, nová služba `test_app_ldbc`, nové proměnné —, a tím i doba prvního startu profilu o obnovu WideWorldImporters a načtení SF 0,1. `ORMConvertor/README.md` (*Tests*) to musí říkat vedle pasti s `run` bez `build`.

**Obě sady dostávají přehrávač.** Je malý — čtení tabulky v pořadí, vázání JSON na parametry skriptu, volání běžce artefaktu, který už existuje (`DotNetQueryRunner`, javový protějšek), převod JSON do kanonického řádku — a je ve dvou jazycích, takže musí vzniknout dřív než cokoli jiného **test shody převodu**: tentýž očekávaný výsledek v JSON převedený oběma sadami dá bajtově týž kanonický text, rozšíření `renderer-conformance.txt` o řádky s milisekundami, seznamy a jedním objektem. Totéž, co 089 označilo za vlastní past, platí tu znovu a navíc o parsování JSON.

**Běh je dlouhý a je to vědomé.** Sada má pro každý scale factor přes 138 tisíc řádků, z toho přes 130 tisíc čtení, a čtení stojí nad SF 0,1 desítky až stovky milisekund krát tři frameworky na sadu — celá sada jsou hodiny i nad SF 0,1 (prefix 800 řádků: .NET 3,4 min, Java 4–8 min). Proto prefix, proto SF 0,1 s prefixem 1000 řádků jako výchozí v profilu `test`, a proto soudce není součástí rychlé zpětné vazby `dotnet test` bez konfigurace; celá sada nad SF 1 je samostatný běh.

**Čtvrtý stupeň nad LDBC najde, co nižší nevidí** — a nejspíš i v textech, které dnes stojí jako „podle specifikace". To není argument proti: je to důvod, proč soudce existuje, a každý takový nález je buď oprava textu podle 110, nebo vada překladu, a běh zdroje řekne která. Výsledek prvního úplného běhu se zapíše do záznamu běhů v README (rozhodnutí 095) a do traceability, ne sem.

**Testy.** Že tentýž očekávaný výsledek převedený oběma sadami dá týž kanonický text — první, protože na něm stojí ostatní. Že kompenzace vrátí databázi do načteného stavu: počty řádků každé tabulky před přehráváním a po něm jsou stejné. Že každý dotaz Interactive má vazbu na soudce. Že zdroj Dapper odpovídá soudci na každém čtení každého dotazu „podle specifikace" a že totéž platí pro každý cíl, který dotaz neodmítá, včetně cílů v nativním SQL. Že dotaz se zjednodušením dá číslo, ne verdikt. Že každá z pěti mutací nad jedním dotazem Interactive skončí v každém cíli neshodou. A že běh bez připojení k `LdbcSnb` se přeskočí s důvodem, a s `ORMCONVERTOR_REQUIRE_LDBC_DATABASE=1` selže.

**Co to neotevírá.** Soudce pro BI nevzniká (varianta 3), proud aktualizací se do dat nenačítá (varianta 2), koncový bod, který by cizí kód spouštěl, nevzniká — artefakty běží v sadách jako dosud (089, varianta 1) — a otázka, zda je `LdbcSnb` referenční databází experimentů T2 a T3, zůstává vlastní otevřenou položkou; tohle rozhodnutí jí dává první argument, ale neodpovídá na ni.

## Historie

**2026-10-03 — revidováno.** Volba se nemění; při implementaci, týž den, se ukázaly čtyři věci, které text tvrdil nepřesně, a ty jsou opravené výš. Za prvé **tvar sady**: řádek je dvojice objektů JSON s pojmenovanými poli a svou operaci nejmenuje, takže vazba páruje po jménech polí, ne pořadím podle přílohy A, a loader pozná operaci podle pole, které má jen ona. Za druhé **čtvrté pravidlo převodu**: driver píše příznak IS 7 a IC 7 jako pravdivostní hodnotu, katalog jako 1 a 0. Za třetí **pořadí IC 14**: specifikace ho dotahuje jen po váhu cesty, takže se IC 14 porovnává jako množina. Za čtvrté **doba běhu** byla odhadnutá o dva řády níž: celá sada jsou hodiny i nad SF 0,1, a profil `test` proto přehrává prefix 1000 řádků.
