# 197 — Připojení ke katalogu čeká omezeně: `Connect Timeout` 5 s bez opakování, nevysloví-li řetězec jinak; jména tabulek jdou do katalogu po dávkách pod mezí parametrů příkazu

Datum: 2026-10-30
Stav: platí
Požadavky: S3, S7, F4, F6
Podklad: rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md), [105](105-a-query-formulates-its-own-demand-on-the-catalog.md), [160](160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md); revize odolnosti [2026-10-30](../audits/2026-10-30-robustness-audit.md), nálezy 2.1 a 2.2; měření `Microsoft.Data.SqlClient` 7.0.2 na MIS2 z 2026-10-30 (níž)

## Kontext

Katalog je nepovinný vstup překladu (rozh. 015): není-li připojení nastavené, překlad jde po konvencích a záznam to řekne; je-li nastavené a čtení selže, stav běhu je `Unreachable` a překlad jde dál stejně. Co rozh. 015 neřeklo, je **jak dlouho** se na nedostupný katalog čeká. `SqlServerCatalogReader` otevíral `SqlConnection` nad řetězcem provozovatele tak, jak přišel, a SqlClient čekal podle svých výchozích hodnot: `Connect Timeout` 15 s a k tomu opakování připojení (`Connect Retry Count` 1, interval 10 s).

Revize z 2026-10-30 to změřila na instanci, jejíž katalog mířil na adresu, která neodpovídá (`10.255.255.1`): převod jedné entity trval **37,5 s** a skončil 200 se stavem `Unreachable`. Další požadavek proběhl za 0,1 s, protože pool SqlClientu po selhání blokuje pokusy na několik sekund — a po uplynutí té doby čeká příští požadavek zase 37 s. Frontend čekání nijak neomezuje: tlačítko je zablokované, dokud odpověď nepřijde (rozh. 160). Z hlediska S3 je čas katalogu vykázaný zvlášť (`CatalogReadTime`) a nárok to nemění; z hlediska S7 je to půl minuty, po kterou uživatel neví, zda nástroj pracuje.

Druhý nález téže revize, nález 2.2: čtečka posílá jména tabulek jako parametry jednoho příkazu, `@n0 … @nN`, a SQL Server přijme nejvýš **2100 parametrů na příkaz**. Projekt, jehož entity dají víc kandidátů jmen (dva na entitu — jednotné a množné číslo —, tedy od zhruba tisíce entit), by dostal `SqlException` z prvního příkazu, čtení by selhalo jako celek a záznam by řekl *katalog nešlo přečíst* — vykázal by mez velikosti jako výpadek. Běh mezí (rozh. 182) k tomu nedošel, protože jeho řady s katalogem rostou po jiné ose.

### Co měření ukázalo

Sonda `SqlConnection.Open()` nad adresou, která neodpovídá, SqlClient 7.0.2, Windows (MIS2), 2026-10-30:

| Řetězec | Čekání |
|---|---|
| `Server=10.255.255.1` bez dalšího | 37,2 s |
| `Server=10.255.255.1;Connect Timeout=5;Connect Retry Count=0` | 27,3 s — **mez neplatí** |
| `Server=10.255.255.1,1433;Connect Timeout=5;Connect Retry Count=0` | 5,1 s |
| `Server=tcp:10.255.255.1;Connect Timeout=5;Connect Retry Count=0` | 5,0 s |
| `Server=tcp:10.255.255.1,1433;Connect Timeout=5;Connect Retry Count=0` | 5,0 s |

Mez tedy drží jen tam, kde zdroj dat jmenuje port nebo protokol; holé jméno hostitele nechá SqlClient hledat instanci i mimo ni a mez při tom nedrží. Řetězce, které nástroj sám předepisuje — compose (`localhost,1444`), README (*Deploying a real instance*), oba stroje (`localhost\SQLEXPRESS`, `(localdb)\MSSQLLocalDB` — pojmenované instance, které port nemají, ale odpovídají hned nebo vůbec) —, do té pasti nespadají; holý hostitel je případ provozovatele, který nastavil jen jméno.

## Zvažované varianty

### 1 — Nechat čekání na provozovateli (dnešní stav)

Řetězec je jeho fakt a `Connect Timeout` do něj může napsat. Zamítáme: nikdo ho nepíše, protože výchozí hodnota SqlClientu je rozumná pro aplikaci, která bez databáze neběží — tou nástroj není; a 37 s není 15 s, které by provozovatel z dokumentace SqlClientu čekal, protože k mezi se přičítá opakování.

### 2 — Čtečka vysloví mez tam, kde ji řetězec nevyslovil

`SqlServerCatalogReader` přepíše řetězec přes `SqlConnectionStringBuilder`: `Connect Timeout` 5 s a `Connect Retry Count` 0, každé jen tam, kde řetězec klíčové slovo sám nenese (`ShouldSerialize`, pod kterýmkoli z jeho zápisů — `Connection Timeout`, `Timeout`, `ConnectRetryCount`); co provozovatel napsal, vyhrává. Řetězec, který SqlClient nepřečte, zůstane, jak je, aby selhání, které způsobí, bylo hlášeno tam, kde každé jiné selhání čtení. Volíme ji: mez je fakt nástroje — *katalog je nepovinný a čeká se na něj krátce* — a stojí v kódu, kde ji drží test; provozovatelovo slovo zůstává nadřazené.

### 3 — Zrušení z požadavku klienta

Předat `CancellationToken` požadavku až do čtečky. Zamítáme: synchronní `Open()` zrušit nejde a uživatel, který odpověď nevidí, požadavek neruší — obnoví stránku, a instance čeká dál.

### 4 — Sonda katalogu při startu instance

Ověřit připojení jednou při startu a při selhání katalog vypnout. Zamítáme: čtečka o živosti mezi dvěma voláními nic netvrdí (její vlastní dokumentace) a katalog, který při startu nestál, může za minutu stát; stav `Unreachable` po jednotlivých bězích to vykazuje správně.

### Dávky jmen

K nálezu 2.2 jsou dvě cesty bez volby, která by stála za víc než řádek: **dávky** — tentýž příkaz tolikrát, kolik je třeba, s nejvýš 2000 jmény —, nebo tabulková proměnná či dočasná tabulka, do níž se jména vloží předem. Volíme dávky: nemění tvar žádného z pěti dotazů do `sys.*`, nepotřebují práva na nic než čtení katalogu a každá tabulka padne právě do jedné dávky, takže seskupení řádků, na němž stojí čtení omezení a cizích klíčů (*jeden nepřerušený běh na omezení*), platí po dávkách stejně jako dřív.

## Rozhodnutí

**Čtečka katalogu čeká na připojení omezeně a čte jména po dávkách.**

- `SqlServerCatalogReader.BoundedConnection` dá řetězci `Connect Timeout` = `DefaultConnectTimeoutSeconds` (5) a `Connect Retry Count` = 0 tam, kde řetězec žádný z těchto klíčů nenese; vyslovená hodnota provozovatele vyhrává pod každým zápisem klíče. Pět sekund: katalog, který neodpoví do pěti sekund, není katalog, na který má smysl čekat, protože i ten, co odpoví, čte celý projekt v desítkách milisekund (§5.2 `architecture.md`); a pět sekund je pod tím, kdy uživatel začne tlačítko považovat za rozbité.
- **Mez drží jen se zdrojem dat, který jmenuje port nebo protokol** (tabulka výš). Nástroj zdroj dat nepřepisuje — `tcp:` před pojmenovanou instancí či LocalDB by připojení rozbilo —, takže to říká README provozovateli: hostitel bez portu čeká na neodpovídající adresu desítky sekund i s mezí.
- Každý z pěti dotazů do `sys.*` i sonda junction tabulek jdou po dávkách nejvýš 2000 jmen (`NamesPerCommand`), pod mezí 2100 parametrů; víc tabulek, než jedna dávka pojme, je víc příkazů nad týmž připojením, ne selhání.

## Důsledky

- **Kód:** `SqlServerCatalogReader` — `DefaultConnectTimeoutSeconds`, `BoundedConnection`, `boundedConnectionString` v `Open()`; `NamesPerCommand`, `Batches` a smyčka v `LoadColumns`, `LoadPrimaryKeys`, `LoadUniqueConstraints`, `LoadForeignKeys` a v sondě `FindJunctionTablesCore`. Začernění (rozh. 160) stojí nad původním řetězcem, kterému mez nic nepřidala ani neubrala.
- **Testy:** `Catalog/CatalogConnectionBoundTest` (mez a nulové opakování tam, kde řetězec mlčí; vyslovená hodnota vyhrává pod třemi zápisy meze a dvěma zápisy opakování; nečitelný řetězec beze změny), `Catalog/SqlServerCatalogReaderTest.ReadsMoreNameCandidatesThanOneCommandTakes` (2501 kandidátů, jediný platný v druhé dávce).
- **Měření po opravě** (tatáž instance, týž černý hostitel, zdroj dat bez portu): 27,5 s místo 37,5 s — opakování odpadlo, mez bez portu nedrží, jak sonda ukázala; s portem 5 s.
- **Dokumenty:** `architecture.md` §5.2; `threat-model.md`, vstupní bod `/convert` a hrozba 2; `traceability.md` S3; `ORMConvertor/README.md`, řádek `ConnectionStrings:CatalogDatabase`.
- **Co rozhodnutí neřeší:** čas provedení dotazu do `sys.*` nad katalogem, který se připojí a pak neodpovídá — `Command Timeout` zůstává výchozích 30 s SqlClientu; takový server revize neviděla a mez by byla číslo bez měření.
