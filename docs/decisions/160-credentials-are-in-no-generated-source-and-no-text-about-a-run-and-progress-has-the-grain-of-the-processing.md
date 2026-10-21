# 160 — Druhá věta S4 platí pro každý zdroj, který nástroj vygeneruje, a pro každý text o běhu, Advisor včetně; cizí text odchází jedním začerněním. Průběžný stav S7 má zrnitost zpracování

Datum: 2026-10-21
Stav: platí; nahrazuje výklad zákazu S4 rozh. 029 (zákaz jen na předávaný artefakt, harness mimo něj)
Požadavky: S4, S7, S6, S3
Podklad: zadání ([`requirements.md`](../requirements.md), S4, S7), [specifikace](../specifikace.tex) (S7; kap. *Uživatelské rozhraní*: „Po dobu volání je tlačítko zablokované a průběžný stav se hlásí pod ním", běh Advisoru „trvá minuty"); rozhodnutí [029](029-database-connection-is-the-consumer-projects-fact.md), [033](033-shape-of-the-static-frontend-screens.md), [039](039-container-configuration-of-the-environment.md), [146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md), [147](147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); [`threat-model.md`](../threat-model.md), aktiva a hrozby 1 a 4; měření z 2026-10-21 níže

## Kontext

S4 má dvě věty. První — sestavení a spuštění cizího a generovaného kódu v izolovaném prostředí s limity CPU, paměti a času — verze nenárokuje: cizí kód spouští jen Advisor, vyňatá oblast 1. Druhá zakazuje přihlašovací údaje k databázi „v generovaných zdrojových souborech ani v lozích". §9, [`traceability.md`](../traceability.md) i sekce *Guarantees* kořenového README ji nárokují, obsahem nároku je ale jen předávaný artefakt překladu, který hlídá `ArtifactCarriesNoCredentialsTest`. Tak ji vyložilo rozh. 029: zákaz dopadá na to, co nástroj předává; harness Advisoru je vnitřní prostředek měření, a proto na něj nedopadá; nic se tím prý nezužuje.

Kód ukazuje, že se zužuje:

| Místo | Co v něm je |
|---|---|
| harness Advisoru (`DapperBenchmarkHarnessBuilder`, `EFCoreBenchmarkHarnessBuilder`) | celý připojovací řetězec s heslem jako `private const string ConnectionString`; Roslyn ho zkompiluje do sestavení, v němž literál zůstává, a podle 147 se to sestavení zapíše do souboru |
| log procesu | překladová cesta do něj nepíše nic; Advisor počty a časy a při selhání buňky výjimku i s jejím textem (`LogError(ex, …)`), který neurčuje nástroj, ale SqlClient, framework nebo měřený kód |
| záznam převodu (strojově čitelný záznam S6) | selže-li čtení katalogu, nese důvod s textem výjimky (`CatalogCompletion`, `QueryDemandCompletion`) |
| chybová odpověď | text výjimky v `detail` (`Endpoints.Failure`) |

Co text výjimky nese, jsme změřili 2026-10-21 (Microsoft.Data.SqlClient 7.0.2; SQL Server 2022 služby `mssql_db`, LocalDB 17.0): převod Dapper → EF Core s katalogovým řetězcem nesoucím zkušební jméno a heslo, sedm případů. Server se smíšeným přihlášením, který jméno nezná, dá záznam „The catalog could not be read (Login failed for user 's4probe_user'.)"; integrované přihlášení do neexistující databáze končí větou „Login failed for user '…'" se jménem účtu procesu. Nedosažitelný server, neznámé klíčové slovo („Keyword not supported: 'bogus key'."), heslo na místě klíčového slova („Format of the initialization string does not conform to specification starting at index 30."), neplatná hodnota („Invalid value for key 'Encrypt'.") a SQL přihlášení proti LocalDB, které zná jen integrované, jméno nevydají. **Heslo nenesl žádný ze sedmi textů, jméno dva.** Záznam převodu tedy dnes vydá jméno, pod kterým se nástroj přihlašuje, a heslo z logu nedrží nástroj, ale dnešní zvyk SqlClientu.

S7 jmenuje „průběžný stav zpracování". Rozh. 033 ho vyložilo jako zablokované tlačítko s indikátorem běhu po dobu volání a schválená specifikace totéž kreslí; obrazovka překladu tak dělá (`aria-busy`, věta „Converting…" v prvku `role="status"`, po návratu počet artefaktů a záznamů, nebo „The conversion failed."). README, §9 ani traceability o průběžném stavu nemluví. Překlad je jeden synchronní požadavek: měření S3 dává 0,2–0,7 s pro 200 jednotek bez katalogu a 0,4–0,5 s s ním ([`ORMConvertor/README.md`](../../ORMConvertor/README.md#translation-performance-s3)). Běh Advisoru trvá minuty a obrazovka u něj ukazuje uplynulý čas a pevnou větu o čtyřech fázích.

Je třeba zodpovědět, zda druhá věta S4 platí pro artefakt i log, nebo se nárok zúží na artefakt a zúžení jmenuje i harness, a zda průběžný stav S7 splní značka zaneprázdnění, nebo se S7 v té části zúží.

## Zvažované varianty

Druhá věta S4:

1. **Zúžit nárok na předávaný artefakt** a harness i log jmenovat jako vyňaté — čtení 029 vyslovené v nároku.
2. **Nárokovat ji celou a do logu, záznamu a chybové odpovědi pouštět jen text, který nástroj složil sám** — počty, časy, identifikátory, jména typů, čísla chyb —, nikdy text výjimky.
3. **Nárokovat ji celou: harness bez údajů a jedno začernění údajů, které nástroj dostal, na každém výstupu textu, který nesložil.**

Průběžný stav S7:

- A. **Zúžit S7** o průběžný stav.
- B. **Hlásit fáze překladu během požadavku** (čtení, doplnění z katalogu, generování) proudem ze serveru.
- C. **Stav v zrnitosti, kterou zpracování má:** u překladu zablokované tlačítko se stavovou větou po dobu volání (033).

## Rozhodnutí

**Varianta 3 a varianta C. Druhá věta S4 platí celá — pro každý zdrojový text, který nástroj vygeneruje, a pro každý text, který o běhu vydá —, i ve vyňaté oblasti 1. Průběžný stav S7 se nárokuje celý a u překladu ho splňuje zablokované tlačítko se stavovou větou.**

### Co druhá věta S4 zahrnuje

**Generovaný zdrojový soubor je každý zdrojový text, který nástroj vygeneruje — předávaný artefakt překladu i harness Advisoru v obou větvích.** Rozdíl mezi textem, který nástroj předá, a textem, který sám zkompiluje, zadání nedělá a nedělá ho ani únik: zkompilované sestavení nese literál beze změny a podle 147 leží v souboru dočasného adresáře běhu. Výklad 029 tím nahrazujeme v té části, která zákaz omezila na předávaný artefakt a harness z něj vyňala; co 029 říká o připojení jako faktu konzumentského projektu, platí beze změny.

**Log je každý text, který nástroj o běhu vydá mimo artefakt:** řádek logu procesu (aplikace, runner rozh. 147, služba `advisor_java` rozh. 146), záznam převodu a běhu Advisoru a text chybové odpovědi. Záznam převodu je strojově čitelný záznam běhu, který žádá S6, tedy log v nejpřímějším smyslu; chybová odpověď nese týž text výjimky jako řádek logu. Popis prostředí — compose soubor a workflow s vývojovým heslem zahoditelné instance — není logem ani generovaným zdrojem (rozh. 039).

**Přihlašovací údaje jsou to, čím se nástroj k databázi prokazuje:** heslo a jméno, ke kterému patří (`Password` a `User ID` připojovacího řetězce, `password` a `user` JDBC URL), celý řetězec, který je nese, a při integrovaném přihlášení jméno účtu procesu, které server do textu chyby napíše stejně (měření výše). Jméno serveru a databáze mezi ně nepatří; jejich výskyt v textu chyby je hrozba 4. Údaje ve vstupu — `OnConfiguring` s `UseSqlServer(…)` ve vloženém kontextu — se podle 029 nečtou a do artefaktu nejdou; tentýž test drží, že nejdou ani do záznamů a knihy tvrzení, protože prohledává celý výsledek převodu, ne jen jeho artefakty.

### Harness bez údajů

**Připojovací řetězec je v harnessu hodnotou za běhu, ne textem.** `BenchmarkHarnessBuilder.Build` ho vůbec nedostává — co builder nedostane, nemůže napsat —, a harness Dapperu i EF Core ho bere parametrem `Setup(string connectionString)`. `BenchmarkExecutor` mu ho předá i s `Command Timeout` meze rozh. 146, takže zdroj ani sestavení literál nenesou. Záložní `OnConfiguring` harnessu EF Core, které řetězec jako konstantu četlo, mizí: kontext harnessu vzniká vždy s nastavením a záloha nikdy neplatila. Až bude buňka procesem podle 147, předá řetězec rodič runneru standardním vstupem, ne na příkazové řádce a ne souborem v dočasném adresáři: příkazová řádka je přesně ten text, který ukazuje výpis procesů a který si log startu procesu přirozeně zapíše, a soubor by byl zase generovaným artefaktem s údajem. Javová větev generovaný zdroj s údaji nemá z konstrukce — služba kompiluje jen přeložené artefakty a smyčka je pevný kód, který spojení dostane za běhu —, a dětskému `java` podle 147 ho služba předá týmž standardním vstupem.

### Jedno začernění

**Text, který nesložil nástroj, odchází jen přes jedno začernění — `CredentialRedaction` v projektu `DatabaseCatalog`.** Postaví se z připojovacích řetězců, které součást drží, a každý výskyt jejich údajů v textu nahradí `***`. Údaji jsou hodnoty klíčů hesla (`Password`, `PWD`) a jména (`User ID`, `UID`, `User`, `UserID`, `Username`, `User Name`), celý řetězec a při `Integrated Security` či `Trusted_Connection` rovném `true`, `yes` nebo `sspi` účet procesu ve tvaru `DOMÉNA\jméno`, v jakém ho SQL Server píše. Řetězec čte obecný `DbConnectionStringBuilder`, ne `SqlConnectionStringBuilder`: ten odmítne klíčové slovo, které SqlClient nezná, a heslo takového řetězce by zůstalo neznámé. Řetězec, který nepřijme ani obecný builder, vydá aspoň sebe a hodnoty svých segmentů `klíč=hodnota`. Výskytem je přesná hodnota bez ohledu na velikost písmen, s níž nesousedí písmeno ani číslice, aby krátké jméno nepožralo slova; delší hodnoty se hledají dřív, takže celý řetězec zmizí jako jeden, ne po částech. `DatabaseCatalog` je jediný projekt, na kterém stojí obě součásti, které začernění potřebují — čtečka katalogu i aplikace —, a jediný, který s připojovacími řetězci pracuje.

Začernění leží tam, kde text výjimky vzniká v součásti, která řetězec drží, a na výstupu logu:

- **Čtečka katalogu.** `SqlServerCatalogReader` obalí každé selhání čtení výjimkou, jejíž zpráva je začerněná proti jeho vlastnímu řetězci; původní výjimka zůstává vnitřní. Fáze doplnění (`CatalogCompletion`, `QueryDemandCompletion`), která zprávu píše do důvodu záznamu, dostává jen `ICatalogReader` a řetězec nezná; zná ho jen čtečka. Selhání tak odchází začerněné ke každému, kdo čtečku použije — do záznamu překladu přes rozhraní i při volání `ConversionHandler` přímo, a do benchmarkové fáze Advisoru, která čte katalog týmž typem.
- **Chybová odpověď.** Aplikace drží jedno začernění jako singleton, postavené ze všech `ConnectionStrings:*` instance; koncové body ho dostávají ze služeb a `Endpoints.Failure` jím pustí `detail`.
- **Výstup logu, ne jednotlivá volání.** `Program` odstraní výchozí poskytovatele logu (Debug, EventSource, EventLog by formátovač obešli) a loguje jen do konzole — v kontejneru je to `docker logs` — přes `RedactingConsoleFormatter`. Ten složí řádek v rozvržení výchozího jednoduchého formátovače (úroveň, kategorie s číslem události, odsazená zpráva, výjimka i se zásobníkem) a celý ho pustí přes týž singleton. Nový řádek logu je tak pokrytý, aniž by na pravidlo musel kdokoli myslet; i `LogError(ex, …)` selhané buňky Advisoru, jejíž řetězec nese navíc `Command Timeout`, přijde o heslo a jméno, ačkoli celý řetězec se už jako jeden neshoduje. Runner podle 147 a služba podle 146 se budou řídit týmž pravidlem; co runner vypíše, přečte rodič a do logu to půjde přes jeho formátovač.

Text serveru zůstává, odchází jen údaj: „Login failed for user '***'." Uživatel dál vidí, proč katalog nedosáhl, a jméno, které sám nastavil, zpátky číst nepotřebuje.

**Proč ne varianta 2.** Text SQL Serveru je pro uživatele jediné vysvětlení, proč katalog nedosáhl — „Cannot open database … requested by the login" řekne víc než číslo 4060 —, a S6 i S7 žádají chyby, ne jejich čísla. Pravidlo „jen vlastní text" by navíc platilo u každého místa zvlášť a každé nové místo by na ně muselo pamatovat; začernění na výstupu logu pokryje i řádky, které teprve vzniknou.

**Proč ne varianta 1.** Zúžení by nárok vyprázdnilo: předávaný artefakt údaj nenesl nikdy, protože jeho tvar spojení nepřipouští (029), a místa, kde nástroj údaj skutečně drží a kudy může odejít, jsou právě harness, log a záznam. Záruka, která pokrývá jen místo, kde tajemství nikdy není, neříká nic.

### Hranice proti první větě: cizí kód

**Druhá věta zaručuje, co píše nástroj; co udělá cizí kód, je věc první věty.** Začernění chytí údaj, jak ho nástroj dostal, ne údaj, který si cizí kód sám zakóduje. Kód, který Advisor měří, běží se spojením do měřené databáze, a údaje tedy drží v paměti, ať mu je harness předá jakkoli; kdyby je chtěl vynést, log je nejmenší z jeho cest — soubor, síť i tabulka v databázi jsou po ruce. To není nástroj, který údaj píše, ale cizí kód, který běží s právy nástroje, tedy izolace podle první věty: oblast 1 ji nenárokuje a limity procesu buňky rozhoduje položka *Izolace spouštění cizího kódu Advisorem*. K těm limitům patří i to, co proces buňky zdědí: bez zásahu dostane celé prostředí aplikace, včetně katalogového připojovacího řetězce, který měřený kód nepotřebuje.

**Proč oblast 1 druhou větu nese a průběžný stav S7 ne.** Oblast 1 je vyňatá proto, že verze neslibuje funkci Advisoru — výběr frameworku, ILP, měření (F15, T7). Únik hesla škodí, ať verze funkci slibuje, nebo ne, a oprava je malá a ověřitelná bez nativní knihovny i bez měřené databáze; vyjmout ji spolu s funkcí by znamenalo dovolit únik právě tam, kde nástroj údaj drží nejdéle. Slibovat průběžný stav běhu, jehož výsledek verze neslibuje, by naopak byl slib bez obsahu.

### Průběžný stav S7

**Průběžný stav zpracování je to, co uživatel během zpracování ví o tom, kde stojí — v zrnitosti, kterou zpracování má.** Překlad je jeden synchronní požadavek. Mezi odesláním a odpovědí server nevydává žádný stav, podle kterého by uživatel mohl jednat, a klient ví všechno, co vědět lze: odesláno, běží, hotovo, nebo selhalo. Zablokované tlačítko a stavová věta pod ním (`role="status"`, takže ji ohlásí i čtečka obrazovky) jsou proto celým průběžným stavem překladu, ne jeho náhražkou; tak S7 čte i schválená specifikace. Délku dává měření S3: 200 jednotek pod sekundu, s katalogem do půl sekundy. Fáze hlášené proudem (varianta B) by svítily milisekundy a za proudový kontrakt `/convert` by uživateli nic neřekly. Zúžení (varianta A) by se vzdalo něčeho, co nástroj má a co zadání žádá.

**Běh Advisoru má zrnitost buňky.** Trvá minuty a skládá se z buněk (dotaz × kandidát); uplynulý čas s pevnou větou o čtyřech fázích stavem v té zrnitosti není. Dokud je Advisor vyňatý, S7 jeho průběh nenárokuje (odstavec výš); až z oblasti 1 vystoupí, váže S7 i jeho, a jak běh buňky hlásí — proudem řádků z jednoho požadavku, nebo jako běh s vlastním stavem, na který se obrazovka ptá —, se rozhodne tehdy. Proces na buňku podle 147 mu k tomu dává přirozené místo.

## Důsledky

**Rozhraní harnessu.** `BenchmarkHarnessBuilder` a `BenchmarkSource` jsou veřejné, aby harness šlo postavit v testu bez běhu Advisoru; `BenchmarkExecutor` hledá `Setup(string)` a bez něj harness odmítne.

**Důkaz.** Čtyři testovací třídy, žádná z nich nepotřebuje nativní knihovnu:

- `Api/CredentialsLeaveNoTextTest` běží nad vlastním hostitelem `CredentialProbeHost`, který dává oběma připojením instance zkušební jméno a heslo proti serveru testovací databáze, a bez testovací databáze proti portu, na kterém nikdo neposlouchá. `/convert` holé entity Dapperu dostane záznam „The catalog could not be read" a odpověď — artefakty, záznamy, kniha tvrzení — nenese ani jméno, ani heslo. Druhý test drží log: jediným poskytovatelem hostitele je konzole, jejím formátovačem ten začerňující, a řádek, jaký píše selhaná buňka Advisoru, i s textem odmítnutého přihlášení odejde začerněný. Log se nezachytává během `/convert`, protože překlad do logu nepíše nic a takový záchyt by nedokazoval nic; log má jediný výstup, a ten je celou otázkou.
- `Combined/HarnessCarriesNoCredentialsTest` postaví harness z překladu ukázky Advisoru do Dapperu i EF Core nad podvrženým katalogem a ověří `Setup(string connectionString)` a nepřítomnost všech značek připojení — první test oblasti 1, bez databáze.
- `Catalog/CredentialRedactionTest` drží, co je výskyt: texty z měření, celý řetězec jako jeden, heslo kdekoli, velikost písmen, krátké jméno vedle písmene či číslice, neznámé klíčové slovo, řetězec, který nepřijme žádný builder, účet procesu, nic.
- `ArtifactCarriesNoCredentialsTest` u údaje ve vstupu prohledá serializovaný výsledek převodu celý, ne jen artefakty.

Jméno do textu chyby napíše jen server se smíšeným přihlášením — v CI, v profilu `test` a ve službě `mssql_db`. Tam test API začernění skutečně cvičí: 2026-10-21 nad `mssql_db` prošel a s vypnutým začerněním ve čtečce selhal. Týž převod, jímž vzniklo měření v kontextu, dává teď záznam „The catalog could not be read (Login failed for user '***'.)" a u integrovaného přihlášení „… Login failed for user '***'."; zbylých pět textů se nezměnilo. LocalDB zná jen integrované přihlášení a nikoho nejmenuje, takže na hostiteli s LocalDB drží test nárok, aniž by začernění cvičil. Měření z kontextu se tím stává testem: změní-li SqlClient text chyby, ukáže to sada, ne čtenář.

**Nárok.** §9, traceability a sekce *Guarantees* README říkají, že druhá věta S4 platí celá, i v oblasti 1, která dál nenárokuje F15, T7 a první větu S4; [`threat-model.md`](../threat-model.md) u aktiv a u hrozby 4 říká, kudy text výjimky odchází a co začernění nepokrývá, a [`quality-model.md`](../quality-model.md) u S4 jmenuje vedle konstrukce začernění.

**S7.** Průběžný stav platí bez změny kódu, takže ho §9, traceability a README nárokují s důkazem projitím scénáře S7 (test frontendu podle rozh. [032](032-frontend-as-static-pages-without-a-build.md) neexistuje) a délkou z měření S3.

**029 dostává stav podle 150.** Platí dál pro připojení jako fakt konzumentského projektu a pro údaje ve vstupu; větu, že zákaz dopadá jen na předávaný artefakt a na harness ne, nahrazuje toto rozhodnutí.
