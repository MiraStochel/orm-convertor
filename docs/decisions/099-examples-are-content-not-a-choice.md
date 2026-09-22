# 099 — Příklady výkladové stránky jsou její obsah, ne volba; ostatní podoba obrazovek zůstává

Datum: 2026-09-22
Stav: platí
Požadavky: F7–F11, F14, S6, S7
Podklad: rozhodnutí [033](033-shape-of-the-static-frontend-screens.md), které tímto nahrazujeme, a [032](032-frontend-as-static-pages-without-a-build.md), bod e; dále [043](043-rest-contract-guarded-over-http.md), [066](066-records-attributed-to-the-input-unit.md), [090](090-the-cross-ecosystem-matrix-counts-itself.md) a [098](098-the-number-is-decided-once-per-release.md); [`architecture.md`](../architecture.md), §6.3 a §6.5

## Kontext

Rozhodnutí [033](033-shape-of-the-static-frontend-screens.md) určilo podobu obrazovek statického frontendu v devíti volbách. Osm z nich se týká nástrojových obrazovek a platí. Devátá dala výkladové stránce diagram pipeline a **právě dva živé příklady**, EF Core → NHibernate a Dapper → EF Core, a jejich počet odůvodnila větou *„Dva stačí: pokrývají oba směry ‚zdroj tvrdí hodně' i ‚zdroj tvrdí málo' a každý další by opakoval už ukázané."*

**Ta volba volbou nebyla.** Seznam příkladů je text stránky — co se na ní čtenáři ukazuje —, a zapsaný do rozhodnutí z něj udělal tvrzení o nástroji: každý další příklad se tím stal změnou rozhodnutí a potřeboval by vlastní soubor v `decisions/`. Rozhodnutí má zaznamenat, proč nástroj dělá něco tak a ne jinak; na otázku, proč stránka ukazuje zrovna tenhle příklad, odpovídá stránka sama svým výkladem, ne rozhodnutí.

Že to není akademická námitka, ukázalo, co od 2026-08-20 přibylo. Celý druhý ekosystém — Hibernate a EclipseLink jako dva profily jedné specifikace (rozhodnutí [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md) a [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md)) a MyBatis (rozhodnutí [084](084-mybatis-wrapper-over-the-shared-sql-reading.md)) — a s ním překlad přes hranici ekosystémů, který verze nárokuje (F10, rozhodnutí [090](090-the-cross-ecosystem-matrix-counts-itself.md)). Výkladová stránka z toho neukazovala nic a doplnit příklady uvnitř Javy, napříč ekosystémy a dva rozsáhlé převody by podle 033 znamenalo rozhodnutí o obsahu stránky — a při dalším příkladu znovu.

S rozsáhlejšími příklady přichází druhá, technická otázka: **odkud stránka vezme jejich vstup.** Stránka podle 033 skládala jednotky příkladu z `/required-content` a obsah brala z `/samples`, klíčovaného identifikátory těch jednotek — jeden vzorek na jednu jednotku, kterou chce překladová obrazovka. Příklad s pěti entitními jednotkami téhož typu obsahu, s pojmenovanými soubory a s daným směrem v tom tvaru místo nemá.

## Zvažované varianty

**Co o příkladech říká rozhodnutí.** Tři cesty.

1. **Seznam příkladů v rozhodnutí, jako v 033**, jen delší. Zamítáme: přesně tohle je vada, kterou napravujeme — každý přidaný, vyměněný nebo odebraný příklad by byl zase rozhodnutím o textu stránky.
2. **Nic — sada je volná úplně.** Zamítáme napůl. Obsah volný být má, ale výkladová stránka je místo, kde se nástroj ukazuje čtenáři práce, a bez spodní hranice by se mohla tiše vrátit k jednomu ekosystému, aniž by si toho kdokoli všiml; frontend automatické testy nemá (rozhodnutí 032, důsledky).
3. **Spodní hranice a jinak obsah.** Rozhodnutí fixuje jen to, co musí platit vždy, a hlídá to test; které příklady tu hranici plní, je obsah stránky. Tuhle volíme.

**Odkud vstup příkladu.** Tři cesty.

1. **Vstupy zapsané přímo ve stránce** — konstanty v `examples.js` nebo statické soubory ve `wwwroot`. Kontrakt REST se nemění, a to je jediná výhoda. Zamítáme: ukázkové vstupy by měly dva domovy (`SampleData` a `wwwroot`) a nic by je netestovalo, takže příklad, který změna parseru rozbije, by se ozval na stránce, ne v CI. Živý příklad se s nástrojem nerozejde v tom, co ukazuje, ale přestat fungovat může.
2. **Rozšířit `/samples` o identifikátory pro jednotky příkladů.** Zamítáme: `/samples` je kontrakt „vzorek pro jednotku, o kterou rozhraní žádá", který `ApiContentContractTest` hlídá v obou směrech, a pro směr převodu ani pro jména souborů v něm místo není.
3. **Samostatný čtecí koncový bod `GET /examples`,** který vydává celé vstupy převodu — klíč, směr a pojmenované jednotky — skládané ze `SampleData`. Tuhle volíme.

## Rozhodnutí

> **Která ukázka na výkladové stránce stojí, je obsah stránky, ne volba nástroje. Rozhodnutí fixuje jen spodní hranici — nejméně sedm živých příkladů, mezi nimi překlad uvnitř .NET, uvnitř Javy a v obou směrech napříč ekosystémy a každý framework aspoň jednou jako zdroj nebo cíl — a to, že vstupy příkladů vydává server čtecím koncovým bodem `GET /examples`. Ostatní volby rozhodnutí 033 přebíráme beze změny.**

**Příklady jsou obsah.** Přidat, vyměnit, upravit nebo odebrat příklad je práce, ne rozhodnutí: soubor v `decisions/` nevzniká, dokud drží spodní hranice. Mění se vstupy v `SampleData`, katalog v `ORMConvertorAPI/Data/Examples.cs`, výklad na stránce a popis v [`architecture.md`](../architecture.md), §6.3. Rozhodnutím zůstává jen změna hranice samotné — méně než sedm příkladů, vypuštěná hranice ekosystémů nebo framework, který stránka přestane ukazovat.

**Proč nejméně sedm.** Je to stav, se kterým tahle podoba stránky vzniká, a hranice ho drží jako minimum, ne jako kvótu: stránka smí růst, ale nesmí se tiše zmenšit. Samotné číslo nic nevysvětluje a vysvětlovat nemá — co stránka ukazovat musí, říkají druhé dvě podmínky, hranice ekosystémů a frameworky; počet je pojistka, že se nezúží na to nejnutnější, které by oběma podmínkám vyhovělo už čtyřmi příklady.

**Obsah v den rozhodnutí** — popis stavu, ne součást volby; platnou podobu nese [`architecture.md`](../architecture.md), §6.3. Uvnitř .NET EF Core → NHibernate a Dapper → EF Core, uvnitř Javy Hibernate → EclipseLink a Hibernate → MyBatis z téhož vstupu, takže rozdíl výstupů je rozdílem cílů, a napříč ekosystémy MyBatis → Dapper a dva rozsáhlé převody, po jednom v každém směru: kniha objednávek z EF Core do Hibernate a výpůjční knihovna z Hibernate do NHibernate. Rozsáhlé převody spouštějí fáze, které jedna entita nespustí — syntézu spojovací entity, ploché vykreslení složeného klíče, párování cizích klíčů napříč entitami převodu a víc dotazových jednotek v jednom převodu — a mají vlastní schémata (`Ordering`, `Lending`), aby se nepotkaly s ukázkovou databází WideWorldImporters, kterou má kontejnerová instance jako katalog.

**Vstupy vydává server.** `GET /examples` vrací seznam příkladů, každý jako `{ key, sourceOrm, targetOrm, units }`. Jednotka je tentýž `ConversionSource`, jaký bere `/convert` — typ obsahu, obsah a jméno —, takže ji stránka pošle beze změny, a jméno je jméno souboru, takže záznamy ukazují na soubor, který čtenář na stránce vidí (rozhodnutí [066](066-records-attributed-to-the-input-unit.md)). Výklad zůstává na stránce: próza, která říká, na co se dívat, je obsahem stránky, ne daty serveru. Stránka si svůj příklad najde podle klíče, který je zároveň kotvou její sekce, a klíč, který server nezná, ohlásí ve své sekci. Rejstřík příkladů, sbalení panelů u velkých příkladů a jiné rozvržení stránky jsou tentýž obsah jako příklady samy.

**Co drží test.** `ExampleCatalogTest` hlídá spodní hranici i to, že příklad opravdu funguje: každý se v procesu přeloží bez záznamu `Failure` a vydá entitní i dotazové artefakty; každá jednotka je pojmenovaná a v jazyce, který zdrojový framework podle `/required-content` čte; klíče jsou jedinečné kotvy; sada má nejméně sedm příkladů, přechází všechny čtyři hranice ekosystémů, jak je deklarují deskriptory (rozhodnutí 090), a zahrnuje každou hodnotu výčtu frameworků — sedmý framework tak do sady vstoupí tím, že existuje, stejně jako vstupuje do matice směrů. Přes HTTP (rozhodnutí [043](043-rest-contract-guarded-over-http.md)) se tvrdí, že koncový bod odpoví JSONem pod cestou `/orm` a že příklady dorazí se směrem jako čísly výčtu a s jednotkami beze změny.

**Ostatní volby rozhodnutí 033 platí beze změny** a jejich odůvodnění zůstává čitelné tam:

1. **Čtyři dokumenty a jejich role** — rozcestník, překlad, Advisor a výkladová stránka —, společná hlavička a relativní adresy (beze změny z 033).
2. **Překladová obrazovka je jedna stránka s pěti kroky** shora dolů, viditelnými najednou, s průběžným stavem na tlačítku převodu (beze změny z 033).
3. **Dávkový vstup podle F14 je seznam pojmenovaných jednotek** — z nahraných souborů, z prázdné jednotky nebo ze vzorků přes `/samples` —, s nabídkou typů podle `/required-content`, odesílaný jedním voláním `/convert` (z 033; jméno jednotky se od rozhodnutí 066 posílá s ní, aby na ni záznamy mohly ukazovat).
4. **Validace před odesláním** — prázdný seznam, prázdný obsah, typ mimo nabídku zdroje a XML přes `DOMParser` s číslem řádku; ostatní jazyky soudí server (beze změny z 033).
5. **Výsledek po souborech a záznamy po entitách**, s `Failure` první (z 033; od rozhodnutí 066 ukazují záznamy i na svou jednotku).
6. **Hlavička výsledku nese záznam běhu a stav katalogu** (beze změny z 033).
7. **Stažení kompletního výstupu balí server** koncovým bodem `POST /archive` (beze změny z 033).
8. **Advisor v dosavadním rozsahu** (beze změny z 033).

## Důsledky

**Rozhodnutí 033 dostává stav `nahrazeno 099` celé**, ačkoli se z něj mění jediný odstavec: nahrazené rozhodnutí se nepřepisuje a čtenář, který v něm najde „dva živé příklady", má z hlavičky poznat, že to neplatí. Jeho zbylé volby přebíráme výčtem výš, takže místa, která říkají, čím se obrazovky dnes řídí, jmenují 099; místa, která citují 033 jako původ některé volby — `/archive`, zobrazovací heuristiku jmen, řazení pásu záznamů —, platí dál, protože volba se přenesla beze změny.

**O obsahu výkladové stránky rozhodnutí napříště nevznikají.** Je to táž věta, jakou pracovní dohoda repozitáře říká obecně — ne každá změna je rozhodnutí —, tady vyslovená pro stránku, na které ji 033 jednou porušilo.

**REST kontrakt přibírá devátou trasu.** Čtecí, bez vstupu a vracející statická data ze sestavení; v modelu hrozeb stojí v témž řádku jako `/samples` ([`threat-model.md`](../threat-model.md)). Mění se tabulka koncových bodů v [`ORMConvertor/README.md`](../../ORMConvertor/README.md), snímek `ORMConvertorAPI/openapi.json` a seznam tras v `OpenApiDocumentTest`; zařazení změny do čísla vydání se rozhoduje až při vydání (rozhodnutí [098](098-the-number-is-decided-once-per-release.md)).

**Nárok výkladové stránky se nemění.** Nezakládá nárok na žádný požadavek (rozhodnutí 032, důsledky); test příkladů je tvrzením o datech, která server vydává, ne o stránce. Požadavky v hlavičce jsou ty, jejichž předmět obrazovky a příklady ukazují, ne ty, které by dokládaly.

**Dvě mezery jsou na rozsáhlých převodech vidět a nezakrývají se.** Dotaz z jednotky LINQ nebo JPQL jméno nenese, takže každá generovaná metoda se jmenuje `Query`, respektive `query`, a panely výstupu se pojmenují pořadím; jméno dostávají jen dotazy, které pojmenoval zdroj (rozhodnutí [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md)). Který artefakt vznikl ze které jednotky, server dál neříká — to je položka „Směr překladu jako jedna věc a vstup vedle výstupu" v [`open-items.md`](../open-items.md).

**Záznamy příkladů závisejí na instanci.** Bez katalogu nese každý převod záznam o tom, že katalog nastavený není; s katalogem, který tabulky příkladu nezná, záznam o chybějící tabulce u každé entity. Test běží bez katalogu, stejně jako testy REST kontraktu (rozhodnutí 043).

**Stránka při načtení spustí všechny převody najednou.** Bez katalogu trvá každý desítky milisekund, s katalogem, který se čte, stovky (S3 je změřený, [`architecture.md`](../architecture.md), §6), a běží souběžně, takže čtenář čeká jen na ten nejpomalejší; tlačítko *Run again* zůstává u každého příkladu.

**Co tohle rozhodnutí nerozhoduje:** zobrazení mezireprezentace zůstává otevřenou položkou „Mezireprezentace se nezobrazuje, ačkoli F14 ji jmenuje" — koncový bod `/examples` ji nevydává a vydávat nemá.
