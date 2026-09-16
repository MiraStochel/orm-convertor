# 076 — Javová strana vstupuje jako wrappery v C#, JVM zůstává v kontejneru

Datum: 2026-09-16
Stav: platí
Požadavky: F7–F10, F12, F13, F15, S1, S2, S4, S5, T7
Podklad: rozhodnutí [009](009-target-framework-descriptor.md), [013](013-target-framework-versions.md), [016](016-generated-artifact-verification-levels.md), [026](026-home-of-shared-query-reading.md), [039](039-container-configuration-of-the-environment.md), [040](040-boundary-of-the-handed-over-artifact.md), [062](062-hql-read-by-a-hand-written-parser.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [068](068-source-framework-precedence-orders-the-reading.md) a [069](069-major-marks-a-milestone-not-a-break.md); JSS §3.2, §4.3, §5.1 a §9.1; otevřené položky „Kudy do řešení vstupuje javová strana" a „Modifikátor `virtual` je vynucený člen, a přesto cestuje"; tutoriály k [Hibernate](../analysis/tutorials/hibernate-getting-started.md), [EclipseLinku](../analysis/tutorials/eclipselink-getting-started.md) a [MyBatisu](../analysis/tutorials/mybatis-getting-started.md); [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §3, §4, §15, §20 a §22; [`threat-model.md`](../threat-model.md), hrozba 1

## Kontext

Řešení je .NET a javový projekt v repozitáři není: javové řádky tabulky zafixovaných verzí — JDK 25, Jakarta Persistence 3.2, Hibernate 7.4.5.Final, EclipseLink 5.0.0, MyBatis 3.5.19, `mssql-jdbc` 13.4.0.jre11 — jsou deklarace, ne závislost (rozhodnutí 013). Druhý cíl žádá tři javové frameworky (F7–F9), překlad napříč ekosystémy (F10), javovou testovací sadu, která generovaný kód sestaví a spustí (F12), a diferenční ověření výsledků dotazů (F13). Než se sáhne na Hibernate, je třeba říct, kudy javová strana do řešení vstupuje: jestli javový wrapper čte i zapisuje javový zdroj vlastním kódem v C#, jak to dělá NHibernate wrapper s holým HQL (rozhodnutí 062), nebo jestli k řešení přibývá javová komponenta a s ní JVM.

Odpověď není volba uvnitř jednoho wrapperu. Dnešní stavba ji z větší části předepisuje a stojí za to ji vyjmenovat:

- **Wrappery generují text a vstup čtou parserem jazyka, ne runtimem frameworku.** C# čte Roslyn, T-SQL gramatika `TSql160Parser`, HQL vlastní sestupný parser. S1 wrapperu zakazuje závislost na frameworku, pro který generuje, a rozhodnutí 022, 026 a 062 to třikrát vyslovila: rozdíl mezi „čtu jazyk, ve kterém je vstup napsaný" a „volám framework, pro který generuji" je ten, na kterém celá stavba stojí. Článek to říká v §4.3: parser analyzuje artefakty — zdrojový kód, anotace, XML deskriptory, výrazy LINQ i JPQL — a builder generuje artefakty cíle; orchestrace závisí jen na rozhraních (§9.1).
- **Ověřovací stupně stojí celé na .NET a běží v testovací sadě, ne za běhu.** Druhý stupeň kompiluje Roslynem, třetí předkládá artefakt NHibernate nebo EF Core, čtvrtý ho spouští proti databázi (rozhodnutí 016, [`architecture.md`](../architecture.md) §6.2); překladová cesta cizí kód nekompiluje ani nespouští (§9, F11). Pro javový artefakt dnes není čím ověřit víc než tvar.
- **Advisor je jediné místo, kde nástroj cizí kód spouští, a dělá to in-process** — Roslyn, kolektibilní `AssemblyLoadContext`, bez limitu CPU, paměti a času (model hrozeb, hrozba 1; první věta S4 je vyňatá ze záruk). A běží jen v kontejneru: `libadvisor.so` se staví jedině ve stupni `advisor-native` a název v P/Invoke je linuxový (§8).
- **Kontejnerová konfigurace je jeden compose soubor se dvěma profily a testovací obraz je stupeň jediného `Dockerfile`** (rozhodnutí 039). S5 jmenuje vedle systému a databáze i javové testovací projekty; drží je vyňatá oblast 6 jen proto, že neexistují.
- **Hostitel vývoje nemá JDK.** Všechny javové běhy, ze kterých vzešly tutoriály a srovnání, proběhly v obrazu `maven:3.9.11-eclipse-temurin-25-noble` mimo pracovní kopii; nic javového se na hostitel neinstaluje.

Položka jmenovala trojí důsledek volby a k němu přibyl čtvrtý. Ověření javového artefaktu nad rámec tvaru potřebuje `javac` a bootstrap cílového frameworku. F12 a F13 žádají generovaný kód opravdu sestavit a spustit, tedy JVM s JDBC připojením do téže databáze. S5 chce javový testovací projekt v téže kontejnerové konfiguraci. A F15 žádá pro týž workload varianty „vše Hibernate", „vše MyBatis" a „vše EclipseLink", takže Advisor musí javový kód spouštět a měřit — což dnešní `BenchmarkExecutor`, který sestavení zavádí do vlastního procesu, pro JVM udělat nemůže.

Tutoriály a srovnání dodaly pět vstupů, které rozhodnutí musí zodpovědět, nebo výslovně odkázat dál. **Profil cílového frameworku musí jmenovat implementaci, ne jen úroveň specifikace:** `@GeneratedValue` bez strategie je v Hibernate sekvence a v EclipseLinku tabulka `SEQUENCE`, `@Entity` bez `@Table` dá `Note`, resp. `NOTE`, EclipseLink má sdílenou cache zapnutou a `fetch = LAZY` na `@ManyToOne` bez weavingu tiše ignoruje — doloženo rozdílem DDL nad znak po znaku týmiž anotacemi. **Jazyková osa vynucených členů:** `virtual` u NHibernate a „ne `final`" u JPA je tentýž požadavek proxy mechanismu vyjádřený opačně, protože C# a Java mají opačný default; sdílený C# parser dnes `virtual` podrží a Dapper i EF Core builder ho vypíší. **Sdílená JPA vrstva pro Hibernate a EclipseLink:** kroky 3, 4 a 7 obou tutoriálů se nezměnily ani o znak, liší se bootstrap a defaulty. **Operand parametru** pro dynamicky parametrizované dotazy MyBatisu (F8) a **čtení SQL MyBatisu** mají vlastní položky a tohle rozhodnutí je neřeší.

A čtyři infrastrukturní detaily, které by jinak vyplavaly až v kódu: kde javový projekt bydlí, když CI běží jen na změny pod `ORMConvertor/**` a řešení .NET ho nesestaví; konce řádků, když `.gitattributes` dává CRLF všemu kromě `*.sh`; společné schéma a data, když `Tests/Database/TestSchema.sql` je embedded resource .NET testů; a obraz a služby, když rozhodnutí 039 zakazuje čtvrtý Dockerfile.

## Zvažované varianty

### 1 — Javová komponenta v překladové cestě

Řešení by dostalo javový proces nebo knihovnu, kterou by .NET volal při čtení a zápisu javového zdroje: parser Javy z javového světa, nebo rovnou bootstrap Hibernate či EclipseLinku, který zdroj načte a vydá metamodel. Zamítáme, a to ze čtyř důvodů, z nichž každý stačí sám.

Za prvé S1 doslova: wrapper by závisel na runtimu frameworku, pro který generuje — přesně to, co rozhodnutí 022 a 062 wrapperům zakázala u NHibernate, jen o ekosystém dál. Za druhé S2: výstup překladu by závisel na dvou runtimech a na jejich vzájemném volání a determinismus by se dokazoval nad dvěma procesy místo nad jedním. Za třetí model hrozeb: překladová cesta je dnes bezpečná tím, že nic nekompiluje a nespouští; druhý runtime v ní tuhle vlastnost ruší pro každý požadavek na `/convert`, ne jen pro Advisor. Za čtvrté, a to je nejpodstatnější, bootstrap frameworku není parser. Framework materializuje defaulty mlčky — právě to, co rozhodnutí 067 parseru zakazuje — a vydá metamodel po vlastní interpretaci, takže by se do mezireprezentace nedostal zdroj, ale to, co si o něm framework myslí. Článek klade parser nad artefakty (§4.3); framework nad artefakty je konzument, ne čtenář.

Cesta přes IKVM, který přeloží javový bytecode do .NET, by JVM přinesla do procesu zadními vrátky a všechny čtyři důvody by platily dál.

### 2 — Javové wrappery v C#, JVM nikde

Překladová cesta by byla čistě .NET a javové artefakty by se ověřovaly jen tvarem, tedy 1. stupněm. Zamítáme. F12 a F13 žádají generovaný kód sestavit a spustit, kritérium F7 i F9 říká „generované projekty se sestaví", a to nejde tvrdit bez `javac`. Byl by to návrat před rozhodnutí 016 pro šest nových builderů — třída chyb z rozhodnutí 006, syntakticky správný a přitom nespustitelný výstup, by u nich neměla kdo odhalit. A profil implementace, na kterém stojí determinismus javového cíle, nemá z čeho vzniknout jinak než z běhu: tutoriály ho doložily právě tam, kde dokumentace mlčela.

### 3 — Javové wrappery v C#, JVM jen v kontejneru a v CI

Překladová cesta nezná JVM; JVM žije tam, kde už dnes žije všechno, co spouští cizí kód nebo cizí runtime — v testovací sadě a v kontejneru Advisoru.

Uvnitř téhle varianty stojí dvě podotázky, které jsou rozhodnutím samy o sobě. **Čím se čte javová třída:** vlastní parser podmnožiny, nebo ANTLR runtime pro C# s gramatikou Javy z `grammars-v4`. **Jak se sdílí JPA:** jeden wrapper s implementací jako parametrem, sdílený projekt se dvěma tenkými wrappery po vzoru `LinqParsing` a `CSharpEntityParsing`, nebo dva nezávislé wrappery kopií.

## Rozhodnutí

**Volíme variantu 3. Javová strana vstupuje do řešení jako tři wrappery v C# — parser, builder a deskriptor na framework — nad třemi sdílenými projekty ekosystému. Překladová cesta nezná žádný runtime cílového ekosystému. JVM žije jedině v kontejneru a na runneru CI: v javovém testovacím projektu, který je zároveň sadou podle F12 a ověřovacím harnessem 2. až 4. stupně pro javové cíle, a v javové větvi Advisoru, která běží mimo proces aplikace.**

### Překladová cesta bez JVM

`ORMEnum` dostává hodnoty `Hibernate`, `EclipseLink` a `MyBatis` a každá má vlastní projekt wrapperu s parserem, builderem a deskriptorem, přesně jako tři .NET frameworky (rozhodnutí 009). Čte se takto:

- **Javová třída entity** — `JavaEntityParsing`, sdílený projekt téhož druhu jako `CSharpEntityParsing`, ze kterého dědí entitní parsery všech tří javových wrapperů. Je to vlastní parser v C#: lexer pokrývá celou lexikální gramatiku Javy — komentáře, řetězcové a znakové literály, textové bloky, unicode escapy —, parser jen podmnožinu, kterou mezireprezentace unese: `package`, importy, hlavičku třídy s anotacemi a modifikátory, pole s anotacemi a modifikátory, vnořenou statickou třídu klíče; těla metod a inicializátory se přeskakují párováním závorek nad úplným lexerem, takže lambda nebo anonymní třída v inicializátoru parser nerozhodí. Referenční parser Javy pro .NET neexistuje — `javac` je v Javě — a argument rozhodnutí 062 tu platí beze změny: jazyk je uzavřený tím, co mezireprezentace nese, nástroj ho sám vydává a round-trip builder → parser → builder ho přibíjí; parser smí nerozumět, nesmí rozumět jinak. Neznámý tvar je `Failure` s řádkem a sloupcem, konstrukce mimo model záznam podle rozhodnutí 010. ANTLR jsme zvažovali vážně, protože na rozdíl od HQL gramatika v `grammars-v4` míří na správný jazyk; zamítáme ho, protože generátor parseru je sám javový program — generovaný C# by se musel commitovat a regenerovat v kontejneru — a protože by přinesl runtime a tisíce řádků cizího kódu kvůli čtečce třídy s anotacemi. Kdyby se ukázalo, že podmnožina přerůstá čtečku třídy s anotacemi, je ANTLR runtime s commitnutým generovaným parserem náhradní cesta; byla by to změna volby, tedy nové rozhodnutí.
- **JPQL** — vlastní sestupný parser jako u HQL (rozhodnutí 062). HQL je nadmnožina JPQL a tvar parseru je týž, ale kód `NHibernateWrappers` javový wrapper referencovat nesmí; parser JPQL proto bydlí ve sdílené JPA vrstvě (níž). Rozšíření HQL a EQL nad JPQL končí záznamem, ne odhadem.
- **`orm.xml`** — `XDocument` jako u `hbm.xml`, s precedencí danou specifikací: anotace první, XML poslední, `<xml-mapping-metadata-complete/>` anotace vypíná (rozhodnutí 068).
- **MyBatis** — XML mapper přes `XDocument`; SQL uvnitř čte sdílený projekt T-SQL, který má vlastní položku; zástupné symboly `#{}`/`${}` a operand parametru řeší rozhodnutí k F8.

Zápis je text u všech tří. Sdílené projekty javové strany jsou tedy tři: `JavaEntityParsing` pro všechny tři wrappery, JPA vrstva pro dva a sdílené čtení T-SQL pro Dapper a MyBatis. Platí pro ně pravidlo rozhodnutí 026: jsou vrstvou ekosystému, ne jádra — `AbstractWrappers`, `Common` ani orchestrace se kvůli nim nemění a javový wrapper `LinqParsing` ani `CSharpEntityParsing` nikdy neuvidí, stejně jako .NET wrappery neuvidí `JavaEntityParsing`.

### Sdílená JPA vrstva se dvěma tenkými wrappery

Projekt `JakartaPersistence` nese to, co je věcí specifikace 3.2 a mezi Hibernate a EclipseLinkem se nemění ani o znak: čtení a zápis anotací `jakarta.persistence.*`, čtení `orm.xml`, parser a visitor JPQL, abstraktní JPA entitní builder a abstraktní JPA dotazový builder. `HibernateWrappers` a `EclipseLinkWrappers` z něj dědí a vyplňují čtyři háčky — přesně tolik, kolik míst rozdílu jmenuje §20 srovnání:

1. **defaulty implementace** — názvy tabulek, sloupců a spojovacích tabulek, generátor za `@GeneratedValue` bez strategie, párování názvů s katalogem (u EclipseLinku necitlivé na velikost písmen);
2. **vykreslení databázového typu** — nationalizace řetězce je u Hibernate `@Nationalized`, u EclipseLinku jen `columnDefinition`;
3. **vendor anotace při čtení** — `@Formula`, `@NaturalId`, `@AdditionalCriteria` a podobné se čtou ve vlastním wrapperu a při přechodu do druhé implementace se hlásí záznamem, stejně jako mezi NHibernate a EF Core;
4. **dialekt dotazu** — co HQL nebo EQL přidává nad JPQL, čte wrapper a hlásí ztrátu tam, kde to mezireprezentace nenese.

**Vrstva není implementačně nezávislá, je implementací parametrizovaná — a profilem je wrapper sám.** To je důvod, proč ne jeden wrapper s implementací jako parametrem: parser JPA zdroje bez profilu implementace nemůže materializovat defaulty (rozhodnutí 067 žádá, aby odvozující konvence zdroje byla tvrzením, a `AUTO` je jednou sekvence a jednou tabulka), builder bez něj nemůže volit `@Nationalized` proti `columnDefinition` a deskriptor z rozhodnutí 009 je deskriptorem frameworku, ne parametrizovanou strukturou. V jednom wrapperu by se implementace stala hodnotou za běhu a rozdíly větvením `if`; ve dvou wrapperech jsou přepisem háčku a deskriptor každého z nich říká svou pravdu. Kopii zamítlo rozhodnutí 026 jednou provždy.

**Builder JPA vrstvy nikdy nespoléhá na default cíle.** Každý název tabulky, sloupce, spojovací tabulky a cizího klíče a každá strategie generování se vypíší explicitně; `AUTO` se nevypisuje nikdy — kanonický parametr generátoru (rozhodnutí 020) se propisuje vždy konkrétní strategií. Je to rozhodnutí 067 čtené ze strany zápisu: co parser nesmí materializovat z mlčení, to builder nesmí do mlčení schovat. Výstup má tím styl „explicitní JPA", ne „idiomatický Hibernate", a artefakt je přenositelný mezi implementacemi až na háčky 2 až 4. Vyslovujeme to, protože je to volba stylu, kterou by jinak někdo četl jako upovídanost.

**Vynucené členy specifikace se deklarují jednou.** „Ne `final`" na třídě a persistovaných členech, bezparametrický konstruktor `public` nebo `protected` a klíčová třída u složeného klíče — veřejná, `Serializable`, s `equals` a `hashCode` (rozhodnutí 006, 009 a 031) — jsou věc Jakarta Persistence a JPA vrstva je nese jako základ deskriptoru; deskriptor každého wrapperu k nim přidává jen to, co je jeho.

**„JPA" jako čtvrtý javový cíl nevzniká.** Žádný požadavek ho nežádá a bez implementace by se nedal ověřit 3. stupněm.

**Klíčem vrstvy je úroveň specifikace, klíčem wrapperu verze implementace.** Vrstva platí pro Jakarta Persistence 3.2. Rozejdou-li se implementace v úrovni specifikace — Jakarta Persistence 4.0 je plánovaná na konec roku 2026 —, musela by vrstva nést dvě úrovně, a to rozhodnutí 013 už dnes vyhrazuje novému rozhodnutí.

### Profil cílového frameworku místo jediného čísla verze

Deskriptor javového wrapperu nese vedle verze implementace **úroveň specifikace, implementaci samu jako klíč k defaultům, pojmenovací strategii a nationalizační režim**. Rozhodnutí 013 se nemění — verze zůstává v deskriptoru a zafixovaná hodnota platí, dokud si převod nezvolí jinou —, jen tvar údaje je u javového cíle bohatší; deskriptorů .NET frameworků se to nedotýká. Weaving je fakt o cíli, který v artefaktu není vidět: `fetch = LAZY` na `@ManyToOne` pod EclipseLinkem bez weavingu tiše padá na eager a žádný ověřovací stupeň to nenajde. Deskriptor EclipseLinku to proto nese jako podmíněný záznam u každé líné reference a hranice záruk dostane větu, že líné načtení reference nástroj u EclipseLinku netvrdí.

### Jazyková osa vynucených členů

Mezireprezentace nese modifikátory vlastnosti jako seznam textu z C# (`OtherModifiers`) a to je pro javový builder nepoužitelný slovník: `virtual` v Javě neexistuje, `required` a `override` mají jiný význam. Rozhodujeme trojí.

**Vynucený člen zdroje se odkládá při čtení, podle deskriptoru zdroje.** Parser zdrojového frameworku zahodí modifikátor, který jeho vlastní deskriptor deklaruje jako vynucený člen — `virtual` u NHibernate —, protože je to požadavek frameworku, ne fakt o doméně. Při čtení, ne při emisi: kdyby vynucené členy cizích zdrojů odkládal builder cíle, musel by znát deskriptory všech zdrojů, tedy párovou znalost, kterou S1 a §4.3 článku vylučují; parser zná jen svůj deskriptor a mezireprezentace zůstává bez předpokladů o frameworku (třetí přednost §4.3). Builder cíle pak přidá vynucené členy cíle, jak to dělá dnes.

**Jazykový fakt domény cestuje.** Modifikátor, který zdrojový framework nevynucuje, je vlastnost třídy a mezireprezentace ho nese jako dosud; `virtual` z EF Core zdroje je tedy fakt, `virtual` z NHibernate zdroje ne.

**Do cizího jazyka se modifikátor nepřenáší, ale překládá.** Builder cizího ekosystému nevypisuje tokeny druhého jazyka; vykresluje význam. Nullabilita se v C# vykresluje otazníkem a v Javě obalovým typem (rozhodnutí 014 a 049). Modifikátor, jehož význam cílový jazyk nese implicitně — `virtual` v Javě, kde jsou metody virtuální —, se nevypíše a nic se neztratilo. Modifikátor bez protějšku v cílovém jazyce je ztráta se záznamem podle rozhodnutí 004. Uvnitř jednoho ekosystému se nemění nic.

### JVM jen v kontejneru a v CI

**Javový testovací projekt** je Maven projekt `JavaTests/` v adresáři řešení — uvnitř `ORMConvertor/`, aby trigger CI platil; řešení .NET ho nezná a staví ho jedině stupeň obrazu a job CI. Je dvojím zároveň: sadou podle F12 a ověřovacím harnessem 2. až 4. stupně pro javové cíle. Druhý stupeň je `javac` uvnitř JVM (`ToolProvider.getSystemJavaCompiler()`), třetí stavba továrny cílového frameworku — `EntityManagerFactory` s nasazením při startu, `SqlSessionFactory` u MyBatisu —, čtvrtý běh proti SQL Serveru a nad ním diferenční ověření podle F13. První stupeň, tvar javového textu, zůstává v xUnit. Důkaz se tím dělí: xUnit tvrdí tvar a přijetí .NET frameworky, javová sada přijetí javovými frameworky, běh a diferenční ověření; řádky F7 a F9 v traceability („generované projekty se sestaví") ukazují na javovou sadu.

**Artefakty dostává javová sada přes hranici procesu, nikdy in-process.** Buď volá běžící API, nebo čte artefakty, které odložila sada .NET. Doporučujeme API — je to konzument, pro kterého je nástroj psaný, a profil `test` jen přibere službu aplikace —, volbu ale vysloví rozhodnutí k F12 spolu s tvarem předání. Totéž plyne pro F13: zdrojový dotaz běží v jednom runtimu a přeložený ve druhém, takže diferenční ověření napříč ekosystémy jsou dva procesy, normalizovaný výsledek se serializuje a porovnává v jednom z nich; formát je věcí rozhodnutí k F13.

**Čtyři infrastrukturní detaily:**

- **Konce řádků.** Maven wrapper `mvnw` nevzniká; Maven i JDK dodává obraz `maven:3.9.11-eclipse-temurin-25-noble`, tentýž, ve kterém běžely tutoriály. `.gitattributes` se nemění; `pom.xml` a javové zdroje s CRLF ničemu nevadí.
- **Schéma.** Build kontext compose je `ORMConvertor/`, takže stupeň obrazu kopíruje `Tests/Database/TestSchema.sql` vedle javového projektu; schéma zůstává jedno. Data, která přežijí déle než jeden test, řeší rozhodnutí k F13.
- **Obraz a služby.** Stupeň `java-tests` v `ORMConvertorAPI/Dockerfile` (rozhodnutí 039: ne čtvrtý Dockerfile) staví z obrazu Mavenu a závislosti stáhne při stavbě obrazu, takže běh nepotřebuje síť — táž úvaha jako u pečení balíčků do stupně `tests`; služba `java_tests` pod profilem `test`, seřazená za inicializaci databáze. Platí táž past jako u `tests`: `run` bez `build` měří strom, ze kterého byl obraz postaven.
- **CI.** Druhý job v témž workflow: service container SQL Serveru jako dnes, `actions/setup-java` s Temurinem 25 a cache Mavenu, `mvn -B test`. Je to dělba z rozhodnutí 039: workflow tvrdí, že sada prochází proti SQL Serveru dané verze, compose tvrdí, že totéž jde v prostředí, kde není nic; compose v CI by stavěl obraz bez vrstvové cache. JVM na runneru není JVM na hostiteli.

**Na hostiteli JDK nebude.** Vývojová smyčka javového builderu je proto 1. stupeň na hostiteli a 2. až 4. stupeň přes `docker compose --profile test` nebo v CI. Je to vědomá cena — pomalejší než u .NET, kde 3. stupeň běží nasucho v milisekundách — a má precedens: scénáře se zdrojem v Dapperu potřebují databázi a bez ní se na hostiteli přeskakují (rozhodnutí 016).

### Advisor: javová větev mimo proces, Advisor kontejnerový rozhodnutím

F15 žádá měřit tři javové frameworky a JVM v procesu aplikace být nemůže. Javová větev Advisoru — implementace `IBenchmarkExecutor` pro javové hodnoty `ORMEnum` — proto předává harness samostatnému procesu JVM: buď `java` spuštěné v kontejneru aplikace, jehož obraz by pak nesl JDK a offline repozitář Mavenu se zafixovanými závislostmi, nebo samostatné službě runneru v compose. Která z obou cest a jak se změří tak, aby se .NET měřený in-process a Java mimo proces daly srovnat (T7), je položka „Izolace spouštění cizího kódu Advisorem" v cíli 3 — tohle rozhodnutí vyslovuje jen hranici: JVM je v kontejneru a javová větev mimo proces aplikace, tedy tam, kam první věta S4 chce dostat i tu .NET. Advisor je tím kontejnerový rozhodnutím, ne shodou okolností: linuxový název v `LibraryImport` je zamýšlený stav, build `advisor.dll` pro Windows nevzniká a na hostiteli vracejí koncové body Advisoru hlášku jako dosud. Vyňatá oblast 1 se tím nemění.

## Důsledky

**Dvě položky rozhodnutí končí a dvě práce začínají.** Položka „Kudy do řešení vstupuje javová strana" je zodpovězená tímhle textem a položka o modifikátoru `virtual` s ní. Z odpovědi na jazykovou osu plyne práce na .NET straně před prvním javovým builderem: entitní parser NHibernate odloží `virtual` podle vlastního deskriptoru a `EFCoreNullabilityTest`, který dnes `virtual` ve výstupu tvrdí, se opraví — tvar výstupu EF Core a Dapperu se pro zdroj v NHibernate změní, což je podle rozhodnutí 069 PATCH. A z JVM v kontejneru plyne infrastruktura: `JavaTests/` se stavbou, stupeň obrazu, služba compose a job CI, které mají vzniknout dřív, než první javový builder bude co ověřovat — táž úvaha, kterou rozhodnutí 016 postavilo prostředí před čtečku katalogu.

**Na řadě je rozhodnutí k F7.** Hibernate wrapper nad JPA vrstvou musí vyslovit rozsah anotační podmnožiny, pravidla překladu `hbm.xml` → anotace (rozhodnutí 013), syntézu klíčové třídy (006, 031), přístup k hodnotě podle umístění `@Id`, typ kolekce, nepersistovanou vlastnost (072), výchozí pojmenovací strategii a nationalizační režim profilu, rozšíření HQL nad JPQL a vzorky pro `/samples`. EclipseLink (F9) je pak tenký wrapper nad touž vrstvou a MyBatis (F8) má tři vlastní vstupy v položkách.

**Tabulka zafixovaných verzí se s implementací rozšíří** o Maven 3.9.11 s Temurinem 25 jako obraz stupně a o verzi JUnit, kterou určí implementace; javové řádky přestanou být deklarací. Vazbu deskriptoru na skutečně načtené balíky, kterou u .NET drží `DeclaredVersionsMatchTheVerificationPackages`, drží u Javy xUnit test, který čte `JavaTests/pom.xml` jako text a porovnává verze s javovými deskriptory — bez JVM.

**Hranice záruk se zatím nemění.** Vyňatá oblast 6 platí, dokud javové wrappery a sada neexistují; S5 rozšíří nárok o javový testovací projekt s první stavbou obrazu a uzavření cíle je podle rozhodnutí 069 vydání `2.0.0`.

**Model hrozeb dostane první spuštění mimo proces.** Javová větev Advisoru bude prvním místem, kde nástroj cizí kód spouští za hranicí procesu; .NET větev zůstává in-process a hrozba 1 se zužuje jen o javovou polovinu. Zapíše se, až bude existovat.

**Co toto rozhodnutí neurčuje:** operand parametru a jednotku, která je mapování i dotaz zároveň (F8); sdílené čtení T-SQL (vlastní položka); mechanismus předání artefaktů javové sadě a formát normalizovaného výsledku (F12, F13); návrh runneru Advisoru a metodologii měření (cíl 3); verzi JUnit a to, jak compose složí obě sady pod jeden hlavní příkaz S5 (implementace).

**Cena.** Pomalejší vývojová smyčka javových builderů; druhý job v CI závislý na SQL Serveru, řádově minuty na běh; a později obraz s JDK pro javovou větev Advisoru.
