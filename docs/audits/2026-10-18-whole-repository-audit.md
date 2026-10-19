# Revize celého repozitáře, 2026-10-18

Revize šest dní po vydání `2.2.0`, nad stromem, který od něj přibral 29 commitů: katalog FIBEN jako třetí referenční doménu (rozh. 149), zafixované prostředí měření (145), mez jednoho spuštění (146), dotazy LDBC na cesty jako řetěz úrovní (140, 141), pojmenované dotazy JPA tam, kde stojí (143), cestu přes asociaci mimo `join` (144) a navigaci v operandu LINQ (148). Rozhodnutí 095–149 žádná revize dosud neprošla; ta z [2026-09-21](2026-09-21-pre-release-2-0-0-audit.md) skončila u 094.

Postup je týž jako u předchozí revize — nároky proti kódu, rozhodnutí proti provedení, dokumenty proti pravidlům svého žánru, a ověření spuštěním — s jedním rozdílem: **co revize našla, v téže změně i opravila**, kde oprava nevolí mezi možnostmi, a kde volí, napsala rozhodnutí (150, 151, 152). U každého nálezu proto stojí, co se s ním stalo; kapitola 7 to shrnuje. Výrok, že něco je vadné, se v téhle revizi opírá o běh nástroje nebo zafixovaného frameworku, ne o čtení kódu: kde šlo o chování, změřila ho buď sonda převodu, nebo plán dotazu provideru (kapitola 6).

Nálezy jsou číslované po kapitolách a označené závažností — **kritické** (artefakt vrací jiné řádky či jiný tvar, než říká zdroj, nebo nepravdivé tvrzení, na kterém stojí záruka), **závažné** (dokument tvrdí nepravdu, kód odporuje rozhodnutí), **drobné**.

## Zafixované verze a stav, ke kterému revize platí

Pracovní kopie ke dni 2026-10-18 na MIS3, commit **`6ac5690` „Fix the CI warnings: unread visitor parameter, xUnit2029, upload-artifact v7"**, strom čistý. Poslední značka `2.2.0` (2026-10-12); `<Version>` v `Directory.Build.props`, pole `version` v `CITATION.cff` i dokument OpenAPI nesou `2.2.0`. Docker Desktop 29.8.2, .NET SDK 10.0.401, SQL Server LocalDB 17.0.4025.3 s databází `ORMConvertorTests`, compose `mssql_db` se svazkem `WideWorldImporters`, `LdbcSnb` (SF 1) a `Fiben`.

Tabulka „Zafixované verze" v [`architecture.md`](../architecture.md) §1 souhlasí s `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `JavaTests/pom.xml`, základními obrazy obou Dockerfilů i akcemi CI, řádek po řádku.

---

## 1. Hranice záruk na třech místech

Pořadí autorit je dané: sekce *Guarantees* kořenového [`README.md`](../../README.md), pak [`architecture.md`](../architecture.md) §9, pak [`traceability.md`](../traceability.md). Stavy požadavků (nárokované, v užším rozsahu, vyňaté) souhlasí na všech třech místech, každý řádek tabulky zúžení v README má protějšek v §9 a v traceability, čtyři vyňaté oblasti mají táž čísla a každý test, který traceability cituje jako důkaz, existuje. Vady jsou v tom, co se mezi místy rozešlo s kódem.

### 1.1 „Ověřeno zeleným během, který nic nepřeskočí" neplatí pro žádný běh — závažné

README (*Guarantees*, oblast 6) i §9 (*Javový nárok stojí na běhu v zafixovaném prostředí*) tvrdily, že javový nárok stojí na zeleném běhu obou sad v profilu `test` „bez selhání a přeskočení". Profil `test` ale databázi `Fiben` nenačítá (`FIBEN_DATASET: none`), takže celý běh katalogu FIBEN se v něm přeskočí; nese jen SF 0,1, takže hloubky dotazů na cesty změřené nad SF 1 (`LdbcPathQueryTest.TheDepthIsThatOfTheDataSet`) se přeskočí také; a buňky diferenční matice, kde druhý tvar NHibernatu nese záznam `Omitted`, se přeskakují v každém prostředí. `ORMConvertor/README.md` (*How large the suite is*) první a třetí skupinu znal, druhou ne, a záznamy běhů od 2026-10-12 přeskočení vykazují.

Běh .NET sady z hostitele nad `6ac5690` (kapitola 6): **11 994 testů, 0 selhaných, 1 802 přeskočených** — 1 660 běhu FIBEN, 90 soudce LDBC bez `LdbcSnb`, 52 buněk `Omitted`.

*Opraveno:* README, §9 i provozní README jmenují tři skupiny, které se přeskakují, a rozhodnutí, které je z prostředí vyjímá (118, 141, 149).

### 1.2 `SUM` nad prázdnou množinou vrací v LINQ jiné řádky, a nárok tvrdí, že takový dotaz se nevydá — kritické

README (*What the tool does*: „a query that would return a different set of rows is not emitted"), §9 a [`subset.md`](../subset.md) 2.13 („Žádné známé není") tvrdily, že tiché místo, kde by cíl vrátil jiné řádky, nástroj nezná. Běh katalogu FIBEN z téhož dne ho doložil: EF Core drží sémantiku `Enumerable.Sum` a píše `COALESCE(SUM(x), 0)`, takže `HAVING SUM(a) > (SELECT SUM(b) …)` vybere v EF Core řádky, které SQL vyloučí (q207: 1 řádek ze zdroje, 211 z EF Core; q150, q155, q211, q213 obdobně). [`open-items.md`](../open-items.md) ho vedl jako rozhodnutí, ačkoli podle vlastní definice je to **vada** — vstup v nároku, z něhož vznikne artefakt, který tiše znamená něco jiného — a nárok, který vyvrací, do opravy platí jen s výhradou. Výhrada nikde nestála. Druhý tvar NHibernatu (LINQ, rozh. 118) nese totéž.

*Opraveno co do textu:* výhrada stojí v README a v §9, `subset.md` 2.13 tichý případ jmenuje, položka v `open-items.md` je označená jako vada. Samotná volba — psát `SUM` se sémantikou SQL, nebo mez cíle — zůstává otevřeným rozhodnutím (kapitola 7).

### 1.3 Verdikt soudce LDBC nad celou sadou se vydával za verdikt nad dnešními texty — závažné

[`traceability.md`](../traceability.md), F13, uzavíral větou „verdikt je celá sada nad SF 1 — obě sady ji prošly zeleně, IC 13 a IC 14 se 100 % shodných čtení"; tabulka milníků v `open-items.md` říkala „doložený celou validační sadou SF 1 ve všech šesti frameworcích". Oba běhy celé sady (2026-10-12, 2026-10-14) ale soudily IC 13 a IC 14 ve znění s mezí hloubky, jak záznam *Whole-set runs* sám říká; dnešní texty (140) prošly jen prefixem a jejich verdikt je položka *Na řadě*.

*Opraveno:* traceability, `subset.md` 1.3 i tabulka milníků říkají, nad kterými texty verdikt padl.

### 1.4 F4 je nárokovaný celý, jeho ověřovací kritérium nikdo nespočítal — závažné

Kritérium F4 žádá „správně získáno ≥ 95 % sledovaných metadat". Traceability u F4 i ve vlastní kapitole *Kritéria ověření, která zatím nikdo nespočítal* říká, že podíl se nevykazuje; §9 a README přitom F4 nárokují bez zúžení. Testy katalogu tvrdí jednotlivé fakty schématu fixture, takže nárok obsahově stojí — chybí číslo, které kritérium žádá.

*Nově v `open-items.md`* jako práce: spočítat podíl nad `TestSchema.sql`; nevyjde-li, F4 zúžit.

### 1.5 Drobnosti souběhu tří míst — drobné

- **Stav dotazů BI je tvrzení autora** — rozhodnutí 117 výslovně žádá, aby to §9 i `subset.md` říkaly tímhle slovem; §9 to neříkal. *Opraveno* v řádku F13.
- **Zúžení, která má §9 a README ne:** vztahy v rozsahu IR u F7–F9, pozice u dotazů u F14, u S7 označený jen řádek. **Oblast 2** byla v `subset.md` širší (vložené hodnoty, kolekce hodnot, sekundární tabulky JPA, `[Owned]`, `[ComplexType]` a primitivní kolekce EF Core, bázová třída mimo převod) než v README a §9. **Ořez `CHAR(n)` v EclipseLinku** byl vyslovenou mezí jen v `subset.md` 2.2. *Opraveno* ve všech třech.
- **Podmínka F10** (Dapper → Hibernate či EclipseLink a MyBatis → NHibernate předají bez katalogu jen dotaz) stála jen v traceability a jako důkaz tam figurovala datová třída `CrossFrameworkInputs`. *Opraveno*: §9 podmínku vyslovuje, traceability cituje `QueryMatrixTest` nad těmi vstupy.
- **Kritérium F8** žádá z testů dotazů aspoň pět vícetabulkových; nikde to nestálo. *Opraveno* v traceability (kategorie s joinem či poddotazem ze zdroje MyBatis).
- **Čtení S4 a S7 bez vyslovení:** druhá věta S4 zakazuje údaje i v logu, `ArtifactCarriesNoCredentialsTest` hlídá jen artefakt; S7 jmenuje „průběžný stav zpracování", který žádné z tří míst nezmiňuje. *Neopraveno*, zapsáno v kapitole 7.

---

## 2. Vady v kódu

Kapitola, kterou předchozí revize neměla: nálezy, kde nástroj vydával artefakt, který říká něco jiného než zdroj. Každý je doložený převodem nebo plánem provideru zafixované verze.

### 2.1 Soudce FIBEN by v příštím celém běhu shodil osm odmítnutých dotazů — kritické

`FibenRunTest.TheSourceReturnsTheRowsOfTheJudge` (.NET i Java) neměl výjimku pro dotazy `NotTranslated`: v .NET je zdroj (Dapper) u nich odmítnutý, takže případ selže na `source.Ran`; v Javě zdroj text spustí přes JDBC a chybějící položka soudce se brala jako prázdný výsledek (`?? Digest([])`, `getOrDefault(…, digest(List.of()))`) — sedm dotazů by selhalo a q125 s nulou řádků by prošel proti položce, která neexistuje. Komentáře `judge.sql` a `FibenRun.cs` přitom tvrdily, že dotaz bez řádku v souboru vrátil nula řádků, kdežto hlavička `judge.tsv`, že nebyl spuštěn (pravdu má hlavička: i dotazy s nulou řádků položku mají). Nebylo to vidět, protože první doložený běh soudce neměl a kontroly po něm vzorkovaly dotazy, mezi nimiž odmítnutý nebyl.

*Opraveno* v obou sadách: případ soudce u dotazu `NotTranslated` se přeskočí s důvodem, chybějící položka u přeloženého dotazu je neshoda s větou, že nový soudce je rozhodnutí; komentáře opravené. Ověřeno kontrolou nad q1, q99, q125 a q207 (kapitola 6).

### 2.2 Navigace v operandu LINQ zdroje NHibernate se četla jako vnitřní join — kritické

Rozhodnutí 148 dalo NHibernatu „vnitřní join vždy" s odůvodněním, že jeho provider navigaci překládá na implicitní join HQL. Změřeno proti NHibernate 5.7.0 to neplatí: provider dělá z navigace `left outer join` v projekci, řazení, klíči seskupení, testu na `NULL` i v disjunkci a vnitřní join jen ve filtru, který `NULL` splnit nemůže. Zdroj `session.Query<Order>().Select(o => o.Customer.CustomerName)` nad volitelnou referencí tak vracel objednávky bez zákazníka, nástroj z něj dělal vnitřní join, který je vypustí.

*Rozhodnutí [151](../decisions/151-a-reference-in-a-linq-operand-joins-left-where-its-row-may-be-absent-under-both-providers.md), opraveno:* jedno pravidlo pro oba providery, fakt provideru drží `NHibernateLinqProviderTest`.

### 2.3 Reference za volitelným řádkem se joinovala vnitřně — kritické

Druh joinu se odvozoval jen z cizího klíče reference samé. `o.Customer.Region.RegionName` nad volitelným zákazníkem a povinným regionem dával `LEFT JOIN Customers … INNER JOIN Regions`; vnitřní join vypustil právě ty objednávky, které levý zachoval. Totéž pro referenci z pravé strany `LeftJoin`. EF Core 10 rozvine obojí jako dva levé joiny (změřeno, `EFCoreNavigationJoinProviderTest`).

*Rozhodnutí 151, opraveno:* řádek nese příznak volitelnosti, který se nese řetězem.

### 2.4 Implicitní join bez projekce měnil tvar výsledku — kritické

`ctx.Orders.Where(o => o.Customer.Name == "A")` vrací objednávky. Parser přidal join, ale projekci ne, a cíle si prázdnou projekci nad joinem vyložily jako celý spojený řádek (Q3): Dapper `SELECT *` přes obě tabulky, EF Core netypovaný `IQueryable` nad `new { o, o_Customer }`, HQL dvojici entit. Jen JPQL psal `select o`.

*Opraveno:* rozsah, do kterého cesta přinesla join a který nic nepromítá, promítne celý řádek zdroje; řádek složený selektorem joinu ho nese s sebou se záznamem `Loss`.

### 2.5 Jedna strana joinu, je-li to zdroj, vycházela v LINQ a HQL jako dvojice — kritické

`Join(…, (o, c) => o)` je projekce `o.*`. `QueryClauses` ale nerozlišoval „bez projekce" a „celý řádek zdroje" (`MaterializedAlias` vrací v obou případech alias zdroje), takže builder LINQ nenapsal `Select` a typoval metodu netypovaně a HQL vynechal `select` — oba vrátily dvojici. Commit `d4ec5df` v témže tvaru zrušil dřívější záznam `Loss`, takže to bylo ticho.

*Opraveno:* `QueryClauses.ProjectsOneWholeRow`; LINQ vybere řádek ze spojeného řádku (`.Select(t => t.o)`, `IQueryable<Order>`), HQL napíše `select o`.

### 2.6 Cesta končící referencí vydala sloupec, který tabulka nemá — závažné

`Select(o => new { o.Total, R = o.Customer.Region })` dal join `o_Customer` a projekci `o_Customer.Region` bez záznamu; 148 přitom říká, že reference sama operandem není. *Opraveno:* cesta, která končí referencí či kolekcí, operandem není a join pro ni nevznikne.

### 2.7 Neúplná stráž `NULL` se četla jako holá konkatenace — závažné

`c.Notes == null ? null : c.Code + " (" + c.Notes + ")"` s nulovatelným `Code` se četla jako `c.Code + ' (' + c.Notes + ')'` bez záznamu: zdroj při `NULL` v `Code` vrací text bez něj, SQL `NULL`. Čtečka hlídala jen to, že testované hodnoty jsou zdroje `NULL`, ne že netestované `NULL` nést nemohou. *Opraveno* pravidlem, kterým stráž píše builder LINQ (literál, parametr, sloupec, který mapování vyhlásí bez `NULL`, v řádku, který chybět nesmí).

### 2.8 Značka `!` rozbila čtení cesty — závažné

Sonda revize narazila na to, že `o.Customer!.Region!.RegionName` — běžný zápis v kódu s nulovatelnými referencemi — se nečte jako cesta a projekce vypadne se záznamem `Loss`; dotaz pak vrací celé objednávky. Značka za běhu nic nedělá. *Opraveno:* přepis dotazu ji zahodí.

### 2.9 Cesta přes asociaci v HQL a JPQL, kterou nelze odvodit, se v projekci a řazení zahodila — kritické

Rozhodnutí 144 o tom řeklo „filtr, seskupení a řazení `Failure`, projekce `Loss`"; kód dával `Loss` i v řazení. Cesta má ale sémantiku vnitřního joinu (Jakarta Persistence 3.2, §4.4.4), takže zdroj vypouští řádky, kde je asociace `NULL`, a dotaz bez cesty je vrací — a implicitní joiny asociací, které cesta stihla projít, v dotazu zůstaly. `select o.id, o.customer.region.name …` s regionem mimo převod vyšel jako `select o.id from Order o join Customer o_customer …`.

*Rozhodnutí [152](../decisions/152-an-association-path-that-cannot-be-derived-refuses-the-query-in-every-clause.md), opraveno:* taková cesta odmítne dotaz v každé klauzuli.

### 2.10 Menší vady čtení — drobné až závažné

| Nález | Závažnost | Co se stalo |
|---|---|---|
| Limit rekurze z `addQueryHint` se četl jen z literálu přesně `MAXRECURSION n`; seznam (`"MAXRECURSION 0, MAXDOP 1"`) i argument, který není literálem, dostaly záznam „nic to nemění na řádcích", ačkoli limit rozhoduje, zda dotaz doběhne (139) | závažné | opraveno: limit se čte ze seznamu, nečitelný argument to říká |
| Nárok klíčové třídy z cizí entity (`@EmbeddedId OrderLine.Key`) se vázal na první třídu toho jména a záznam o sdíleném jménu, který 142 skrývá, zmizel i tehdy | závažné | opraveno: záznam zůstane, kde jméno nárokuje někdo mimo rozsah |
| Zápis vnořené třídy v mapování NHibernate `Order+Key` pravidlo 142 nesplnil | drobné | opraveno |
| `<lock-mode>` v `orm.xml` je podle schématu potomek, čtečka ho brala jako atribut a hlásila „není částí schématu"; nečtené členy anotace (`entities`, `classes`, `columns` nativního dotazu) mizely beze slova | drobné | opraveno |
| Hibernate: zastíněná cílová entita joinu bez balíčku se psala holá, ačkoli ji Hibernate čte jako atribut | drobné | opraveno: nativní SQL se záznamem |
| Pojmenované dotazy JPA nedbají `metadata-complete` ani přednosti `orm.xml` | drobné | vyslovená mez v `subset.md` 2.3 |
| Zdroj EclipseLink s `HAVING` přestavěným na kořen: zdroj sám vrací řádky přestavěné klauzule | drobné | vyslovená mez v `subset.md` 2.9 |
| Kontext EF Core se sadami `=> Set<T>()` vydá dotaz celé tabulky za každou sadu (rozh. 111) | drobné | otevřené rozhodnutí v `open-items.md` |
| Javový soudce LDBC čekal na sdílený zámek nejvýš 600 s: `queryTimeout` v URL (146) platil i pro `sp_getapplock`, kdežto .NET čeká bez meze | závažné | opraveno |
| `LdbcPathQueryTest` nesl vlastní mez 900 s proti jediné mezi 146 | drobné | opraveno |
| `zip.js` hlásil prohlížeč bez `deflate-raw` i každou chybu rozbalení jedinou větou o poškozených datech, kde 136 žádá chybu dekomprese | drobné | opraveno |

---

## 3. Rozhodnutí a jejich rejstřík

Strojová kontrola: **148 souborů (001–149 bez 038), 148 řádků rejstříku**, žádný soubor bez řádku ani řádek bez souboru; datum, stav a požadavky souhlasí kromě poznámek „platí; nahrazuje…" u 142, 144 a 148, které stály jen v rejstříku. Všech třináct rozhodnutí se stavem `revidováno` má sekci *Historie*. Rozhodnutí 095–129, která žádná revize neprošla, jsou všechna provedená; kontrola po rozhodnutí nenašla mezi nimi rozpor.

### 3.1 Nahrazení části rozhodnutí se dělalo dvojím způsobem a ani jeden nebyl vyslovený — závažné

Řetěz obrazovek 124 → 125 → 135 → 137 nahrazoval celek a zbytek přebíral odkazem („vše ostatní platí"), takže každá citace 124, 125 a 135 v živých dokumentech se stala citací nahrazeného rozhodnutí (v `docs/` jich bylo sedmnáct). Jinde nové rozhodnutí nahradilo jen část (113 věty z 065, 107 a 112; 120 odrážku 061; 142 důsledek 094; 144 odstavec 101; 148 odstavec 144) a řeklo to nanejvýš ve vlastním řádku rejstříku — starý soubor dál tvrdil `platí` bez výhrady.

*Rozhodnutí [150](../decisions/150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md):* nahrazení části se vysloví na obou stranách stavem `platí; <část> nahrazuje NNN` / `platí; nahrazuje <část> rozh. NNN` v souboru i v rejstříku. Stavy jedenácti dotčených rozhodnutí jsou doplněné, živé dokumenty citují 137 místo 124, 125 a 135.

### 3.2 Rozhodnutí 148 zamítlo variantu na nezměřeném faktu provideru — závažné

Kořen nálezu 2.2: varianta „levý join vždy" padla kvůli větě o provideru NHibernatu, kterou nikdo nezměřil, ačkoli `NHibernateLinqProviderTest` k tomu nástroj má. Rozhodnutí 145 až 147 i 139 fakty frameworků měřila; 148 ne. Nahrazeno zčásti rozhodnutím 151.

### 3.3 Podmínka vzniku soudce FIBEN splněná nebyla — závažné

Rozhodnutí 149 dává soudci tři podmínky, první z nich „zdroj i všech pět cílů se nad daty shodnou". `judge.tsv` je zmrazený z běhu .NET sady ve 14:59 UTC, javový běh začal ve 15:01; z .NET cílů se EF Core neshodl v pěti buňkách vady `SUM`, javové cíle se podmínce podrobily až po opravách téhož dne a q63 v EclipseLinku (ořez `CHAR`) se liší dál. Záznam běhu v `ORMConvertor/README.md` přitom psal, že podmínky splněné jsou, a stránka `fiben.html` mluvila o pěti buňkách jako o jediných neshodách běhu proti vlastní tabulce (203 z 208 v EclipseLinku). Obsahem soudce jsou řádky zdroje, a ty javový běh textem přes JDBC reprodukoval u všech 229 dotazů — soudce tedy nejspíš správný je; neplatí jen věta, že vznikl podle pravidla.

*Opraveno co do záznamu* (provozní README, `fiben.html`); zda soudce platí, je otevřené rozhodnutí v `open-items.md`.

### 3.4 Drobnosti — drobné

- **Věta o třídě vydání**, kterou rozhodnutí 098 novým rozhodnutím zakazuje („podle rozhodnutí NNN je to MINOR"), stojí v 101–105, 107–109, 112–115, 118, 124, 128 a i po 098 ve 143, 144 a 148, několikrát s odvoláním na nahrazené 041 či 069. Rozhodnutí jsou zmražená; *opravené* jsou komentáře kódu, které 041 a 069 citovaly jako živé pravidlo (`RestContractTest`, `OpenApiDocumentTest`, `Program.cs`).
- **Mez `let` z rozhodnutí 103** v `subset.md` chyběla. *Opraveno.*
- **Dva důsledky 095 se rozešly s praxí:** §6.2 už záznam běhu nenese (záznamy jsou jen v provozním README podle pravidla „každý fakt na jednom místě"), a §9 u oblasti 1 neodkazoval na číslo pokrytí. Odkaz *doplněný*; přesun záznamu z §6.2 žádné rozhodnutí nevyslovilo.
- **Rozhodnutí 117 je `revidováno` při implementaci téhož dne**, kdežto rejstřík revizi dovoluje jen před vznikem kódu; *Historie* to říká otevřeně.
- **Rozbité odkazy:** 148 na neexistující soubor rozhodnutí 118, `open-items.md` na špatné jméno souboru 112, referenční odkazy `[148]` v `subset.md` a `[123]` v `traceability.md` bez definice. *Opraveno*; oprava odkazu v souboru 148 je zapsaná v [`README.md`](README.md) mezi opravami zmražených souborů.

---

## 4. Živé dokumenty a stránky proti kódu

### 4.1 Počty, které další změna posunula — závažné

Pravidlo živých dokumentů zakazuje počty, které příští změna posune, a ukázalo se proč: **„50 kategorií"** stálo na sedmi místech (`architecture.md` §6.2 dvakrát a §9, `subset.md` 1.3, `traceability.md` F13, T2 a úkoly záměru), manifest jich od commitu `b59da2d` má 51. Táž třída: „1375 dvojic, 99 v nativním SQL", „17 funkcí", „je jich 7", „13 scénářů", „5 mutací", „(dnes nic)", „(dnes žádný)", počty nativního SQL katalogu LDBC po cílech, počty příkladů na stránce. *Opraveno:* místo čísla stojí množina a místo, kde se počítá.

### 4.2 Zastaralé věty — drobné až závažné

| Místo | Co tvrdilo | Co platí |
|---|---|---|
| §6.2, `subset.md` 1.3 | BI 15 a BI 19 „nesou mez běhu nad SF 1 (122)" | od 140 běží v sekundách; poznámky nesou změřený čas |
| `subset.md` 1.3 | „19 dotazů Interactive"; FIBEN „dva dotazy … `NotTranslated`, ostatní…" | 21 (IS 1–7, IC 1–14); `NotTranslated` jsou dva tvary, osm dotazů |
| §6.2 | celá sada SF 1 „za 20 hodin, javová za 25" | měření před 145; v zafixovaném prostředí dny |
| §8 | diagram „zahřátí, pilot, měřený cyklus", „ms/op" | politika 145 pilota nemá, vrací medián |
| §6.3, provozní README | „šest dokumentů" | sedm: `ldbc-v2.html` vedle `ldbc.html` (4.4) |
| §5 *Jména entit* | „§6 *Pojmenované dotazy JPA*" | odstavec je v §5, *Kdo čte* |
| §2 | mimo `.sln` jen `JavaTests` | i `FibenImport` |
| [`threat-model.md`](../threat-model.md) | `/advisor/run` „bez limitu … času", seznam GET bez `/fiben` | mez jednoho spuštění 146; `/fiben` doplněn |
| provozní README, *Configuration* | bez klíčů soudce LDBC a běhu FIBEN, bez `FIBEN_DATASET` | doplněno odkazem na jejich tabulky |
| provozní README, záznam FIBEN | „první čtení katalogu 152 tabulek trvalo 408 ms" | je to první převod, který z katalogu čte tabulky q1 |

Vše *opraveno*.

### 4.3 Stránka příkladů popisovala výňatky, které už nejsou — závažné

Oddíl *Real projects* stránky `examples.html` popisoval stav z commitu `5656945`: třídu `KeycloakNamedQueries.java`, která „předává 47 dotazů `em.createQuery`", přepsané odvozené identity, osm přepsaných dotazů Ambari, přepsané `FindAsync` v Chinooku, jednu opravenou dotazu Dapperu místo dvou, výsledkové mapy s `<association>` v JPetStore a záznam `Conflict` o jedenácti třídách `Key`. Commity rozhodnutí 142–144 výňatky vrátily k textu projektů, README výňatků opravily, stránku ne; `ThirdParty/README.md` jmenoval soubor, který neexistuje. *Opraveno* podle README výňatků a podle změřeného výstupu každého příkladu (kapitola 6). Stránka `fiben.html` nesla dvě vady z doby před `466040c` (HAVING v EclipseLinku „otevřená vada", „jediné neshody běhu"); *opraveno*.

### 4.4 Druhá verze stránky LDBC — závažné

`wwwroot/ldbc-v2.html` je druhá verze stránky LDBC, kterou žádný sledovaný dokument nepopisoval; `ldbc.html` je podle vlastního úvodu verze 1 s textem z doby před přepisem dotazů na cesty („some only after a simplification", „recursive and bounded") a právě na ni vede navigace všech ostatních stránek a tlačítko rozcestníku. *Popsáno* v §6.3 a v provozním README; která verze je výchozí a zda verze 1 zůstane, je obsah stránek, ne volba nástroje.

### 4.5 Vyprávění v živých dokumentech — drobné

„Nalezeno během katalogu FIBEN", „od 2026-10-18 je to `Failure`", „dvakrát tiše vydal výsledek staršího stromu", historie vydání v §9 a vyprávění měření v položce *Na řadě*. *Opraveno*; řádky 3 a 4 tabulky oblastí v §9 (*vystoupila*) zůstávají, protože vysvětlují, proč se čísla 3 a 4 nepřidělují.

---

## 5. Mrtvý kód, konfigurace a velikost sdílených bází

- **Uzavřené nálezy starších revizí:** zakomentovaný `main()` v `Advisor/ilp.c` je pryč, `Tests/xunit.runner.json` také.
- **Deklarace bez čtenáře:** `JavaClass.Interfaces`, `JavaClass.IsStatic` a `QueryExpressionRewriter.Rewrote`; komentář `StatedBaseTypeKind.Interface` sliboval výrobce z Javy, který neexistuje; řetězec záznamu v `AbstractEntityBuilder` jmenoval EF Core, ačkoli záznam vzniká pro každý cíl. *Opraveno.*
- **Shodovalo se:** 23 projektů `.csproj`, z nich 22 v `ORMConvertor.sln` (`JavaTests` a `FibenImport` mimo), seznam `COPY` stupně `dotnet-build` týchž 22; jedenáct mapování v `Endpoints.cs` = jedenáct cest snímku OpenAPI = tabulka API = §6.5; CI a compose odpovídají provoznímu README i jeho schématu; každý `js/*.js` je použitý; knihovny ve `vendor/` nesou verzi a licenci. Složka *Solution Items* nemá `.gitattributes`, `.dockerignore` ani `ecosystem.config.js` (kosmetické, *neopraveno*).
- **Velikost sdílených bází** (počítáno jako 2026-09-21, včetně deklarace třídy):

| | 2026-09-21 | 2026-10-18 | rozdíl |
|---|---|---|---|
| `AbstractEntityBuilder`, řádků | 2 728 | 3 666 | +34 % |
| `AbstractEntityBuilder`, `virtual`/`abstract` členů | 9 | 9 | beze změny |
| `AbstractQueryBuilder` (obě části), řádků | 1 475 | 4 828 | +227 % |
| `AbstractQueryBuilder`, `virtual`/`abstract` členů | 16 | 21 | +5 |

Invariant S1 v kódu platí — jména frameworků se v `AbstractWrappers`, `Common`, `Model` ani v `ConversionHandler` nevyskytují mimo deklaraci `ORMEnum` — a trend nálezu 5.4 revize z 2026-09-21 pokračuje: entitní báze přibrala čtvrtinu a nenabídla jediný háček navíc.

---

## 6. Co bylo ověřeno spuštěním

Všechno na MIS3, 2026-10-18.

| Běh | Strom | Výsledek |
|---|---|---|
| sestavení Release | `6ac5690` | 0 varování, 0 chyb |
| .NET sada z hostitele (LocalDB) | `6ac5690` | 11 994 testů: 10 192 prošlých, 0 selhaných, 1 802 přeskočených (běh FIBEN 1 660, soudce LDBC 90, `Omitted` 52) |
| sestavení Release a .NET sada z hostitele po opravách | `6ac5690` + změny této revize | 0 varování; 12 021 testů: 10 219 prošlých, 0 selhaných, 1 802 přeskočených (tytéž tři skupiny) |
| obrazy profilu `test` (`tests`, `java_tests`, `test_app`, `test_app_ldbc`, `test_db`) | `6ac5690` + změny této revize | postavené; běh obou sad zastavil nedostatek paměti hostitele dřív, než začal první test |
| kontrola běhu FIBEN z hostitele proti `mssql_db` (`ORMCONVERTOR_FIBEN_QUERIES=q1,q99,q125,q207`, bez měření) | po opravě 2.1 | případ soudce u q99 a q125 přeskočen s důvodem, q1 a q207 se soudcem souhlasí, q207 v EF Core se od zdroje liší (vada `SUM`, 1.2) |

**Sondy.** Převody EF Core → Dapper, EF Core, NHibernate a Hibernate nad doménou objednávek, zákazníků a regionů ukázaly nálezy 2.4–2.8 v artefaktech a jejich opravu. Plán provideru NHibernate 5.7.0 nad mapováním s `<many-to-one>` (nález 2.2) a plán EF Core 10.0.10 nad povinným a volitelným řetězem (2.3) jsou dnes testy (`NHibernateLinqProviderTest.TheProviderJoinsANavigation`, `EFCoreNavigationJoinProviderTest`). Pět příkladů *Real projects* bylo převedeno a jejich výstup — entity, dotazy, záznamy — porovnán s textem stránky (4.3); Keycloak → EF Core dnes neodmítne nic.

**Co se nespustilo:** obě sady v zafixovaném prostředí — javová tedy vůbec, takže změny v `FibenRun.java`, `FibenRunTest.java` a `LdbcReplay.java` prošly jen čtením a stupeň `java-tests` je při stavbě obrazu jen přeložil —, běh FIBEN celý, soudce LDBC nad celou sadou a `/advisor/run`.

---

## 7. Co z revize plyne

Rozdělení podle rozhodnutí [007](../decisions/007-documentation-structure.md).

### Opravy, provedené v téže změně

1. Hranice záruk: skupiny přeskočených testů (1.1), výhrada vady `SUM` (1.2), verdikt LDBC nad dřívějšími texty (1.3), drobnosti souběhu (1.5).
2. Kód: soudce FIBEN (2.1), druh joinu navigace LINQ a řetěz za volitelným řádkem (2.2, 2.3, rozh. 151), projekce řádku za implicitním joinem (2.4), strana joinu, která je zdrojem (2.5), cesta končící referencí (2.6), neúplná stráž (2.7), značka `!` (2.8), cesta v HQL a JPQL v projekci a řazení (2.9, rozh. 152) a tabulka 2.10 kromě dvou mezí a jednoho otevřeného rozhodnutí. Ke každé opravě test.
3. Rozhodnutí: vyslovení nahrazení části (3.1, rozh. 150), rozbité odkazy a mez `let` (3.4).
4. Dokumenty a stránky: počty (4.1), zastaralé věty (4.2), stránky příkladů a FIBEN (4.3), druhá stránka LDBC popsaná (4.4), vyprávění (4.5), mrtvé deklarace (5).

### Potřebná rozhodnutí

1. **`SUM` nad prázdnou množinou v LINQ** (1.2) — vada; psát ho se sémantikou SQL, nebo ho vyslovit mezí cíle.
2. **Soudce FIBEN** (3.3) — platí tenhle, nebo vznikne znovu po opravě vady `SUM`.
3. **Vlastnost sady kontextu EF Core zapsaná výrazem** (2.10) — deklarace sady, nebo předání dotazu.
4. **Čtení S4 (log) a S7 (průběžný stav)** (1.5) — vyslovit, nebo zúžit.

### Práce

1. **Podíl kritéria F4** (1.4).
2. **Verdikt soudce LDBC nad dnešními texty** — beze změny položka *Na řadě*.
3. **Javová sada** nad změnami této revize v zafixovaném prostředí (6).

### Delší horizont

1. **Poměr těla a rozšiřovací plochy `AbstractEntityBuilder`** (5) — druhá revize po sobě bez nového háčku.
2. **Fakta frameworků měřit dřív, než se o nich rozhodne** (3.2) — `NHibernateLinqProviderTest` a `EFCoreQueryAcceptance` to umějí levně.
3. **Výchozí verze stránky LDBC** (4.4).
