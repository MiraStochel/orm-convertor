# 078 — Javová sada je klientem běžící instance nástroje

Datum: 2026-09-17
Stav: revidováno
Požadavky: F7, F9, F10, F12, F13, S2, S4, S5
Podklad: rozhodnutí [016](016-generated-artifact-verification-levels.md), [027](027-query-artifact-verification.md), [037](037-enforced-member-binding-held-by-the-test.md), [039](039-container-configuration-of-the-environment.md), [040](040-boundary-of-the-handed-over-artifact.md), [043](043-rest-contract-guarded-over-http.md), [066](066-records-attributed-to-the-input-unit.md), [069](069-major-marks-a-milestone-not-a-break.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md) a [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md); [`use-cases.md`](../use-cases.md), „Co je ve skutečnosti vstupem"; [`architecture.md`](../architecture.md), §6.2 a §6.5; JLS §7.6 (jeden veřejný typ na soubor svého jména); otevřená položka „Předání generovaných artefaktů javové sadě"

## Kontext

Javová sada `JavaTests/` existuje od 2026-09-17 jako kostra: sestaví se v kontejneru i v CI, připojí se k databázi, postaví sdílené schéma a nad ručně psanými entitami rozsoudila obě tvrzení rozhodnutí 077 ([`architecture.md`](../architecture.md), §6.2). Ke generovaným artefaktům se ale nedostane. Nástroj je vydává jedině z procesu .NET, sada běží v JVM, a rozhodnutí 076 cestu in-process vyloučilo — z důvodů, které platí dál: překladová cesta nezná žádný runtime cílového ekosystému a JVM žije jen v kontejneru a na runneru CI. Totéž rozhodnutí nabídlo dvě cesty přes hranici procesu — sada volá běžící API, nebo čte artefakty, které odložila sada .NET — a volbu i tvar předání odkázalo sem. Dokud se nerozhodne, nemá věta „generované projekty se sestaví" z kritérií F7 a F9 nad čím být vyslovena a F7 zůstává ve vyňaté oblasti 6 (§9).

Co přesně se má předat, je dané tím, co nástroj po drátě vydává. `/convert` vrací vedle záznamu běhu seznam jednotek `sources`: každá nese typ obsahu a text, žádná jméno — párování výstupu se vstupem je otevřená položka rozhraní a §9 to u F14 vyslovuje jako zúžení. Javová entita je jedna jednotka `JavaEntity`: řádek `package` z jmenného prostoru zdroje, importy, třída s vnořenou klíčovou třídou u složeného klíče (077). Dotaz jsou jednotky dvě: holá statická metoda nad `EntityManager` (`JavaQuery`) a holý JPQL (`JpqlQuery`). Výčty cestují jako čísla, jak kontrakt od rozhodnutí 043 tvrdí testem.

Co s tím má sada udělat, stanovilo rozhodnutí 076: druhý stupeň je `javac` uvnitř JVM, třetí stavba továrny cílového frameworku, čtvrtý běh proti SQL Serveru. Každý z těch tří kroků něco chce, co v jednotce není. `javac` chce soubor, jehož jméno je jméno veřejné třídy, v adresáři, který je balíkem (JLS §7.6). Holá metoda chce třídu, ve které stojí, a importy `jakarta.persistence.*`. Továrna chce třídy zavedené do JVM, ve kterém běží. Na .NET straně dělá tutéž práci harness ověření: `GeneratedEntityCompiler` dodává implicitní usings a reference balíku cíle, `GeneratedQueryCompiler` obaluje metodu třídou — jako příspěvek konzumentského projektu, ne artefaktu (§6.2, rozhodnutí 040).

A kde dnes nástroj běží. Na hostiteli ze zdrojů; v compose jako služba `ormconvertor` z obrazu `runtime`, vedle databáze s WideWorldImporters; a v CI **jedině uvnitř xUnit** — testy kontraktu ho startují v paměti přes `WebApplicationFactory` (043). Profil `test` žádnou službu aplikace nemá: sada .NET ověřuje 2. až 4. stupeň přes orchestraci ve vlastním procesu a přes HTTP jen kontrakt. Job `java-test` má JDK a service container SQL Serveru a nic jiného.

Vstupy, které by se nabízely, jsou tři a žádný neleží tam, kde ho javová sada může vzít: vzorky `/samples` popisují `Sales.Customers` z WideWorldImporters, ne schéma fixture, takže nad nimi 4. stupeň nemá tabulky; `CrossFrameworkInputs` jsou konstanty C# uvnitř testovacího sestavení .NET a jejich `Shop.Customer` nad schématem fixture také nestojí; `DapperSourceEntities` nad schématem fixture stojí, ale jsou to zase konstanty C#.

Rozhodnout je tedy třeba čtvero: cestu přes hranici procesu, vstup, kdo mapuje jednotky odpovědi na soubory a balíky, a jak se v compose i v CI dostane instance nástroje před javovou sadu.

## Zvažované varianty

### Cesta

**1 — Sada čte artefakty, které odložila sada .NET.** Má dvě podoby a obě zamítáme. Odložené *do gitu* — vygenerované jednou a commitnuté jako testovací zdroje — jsou otisk: druhý zdroj pravdy vedle běžícího nástroje, který tiše zestárne a který dokazuje sám sebe, ne nástroj. Je to doslova varianta 2 rozhodnutí 043 a slabina 1. stupně z rozhodnutí 016, přenesená o ekosystém dál; a padla by na první změně tvaru výstupu, kterou by javová sada „ověřila" nad textem z minulého týdne. Odložené *za běhu* — sada .NET je při svém běhu zapíše do sdíleného místa, svazku compose nebo artefaktu workflow, a javová sada je odtud čte — je poctivější, ale platí se třikrát. Obě sady se seřadí za sebe: javová by čekala na doběhnutí .NET sady, v CI přes `needs` a předávaný artefakt, tedy minuty za cestu, která dnes běží paralelně. Vznikl by soukromý kontrakt dvou testovacích sad — rozvržení adresáře, názvy souborů, formát —, který žádný konzument nástroje nemá a který by musel udržovat každý, kdo sáhne na jednu z nich. A sada .NET by musela artefakty pojmenovat, tedy mimochodem a v testu vyřešit položku párování, kterou §9 vede jako otevřenou. Hlavně by ale javová sada ověřovala soubory, ne nástroj.

**2 — Nástroj jako proces, který sada spustí.** Příkazová řádka, které by sada předala vstupy a přečetla výstup. Žádná neexistuje, obraz javové sady nemá runtime .NET a nástroj by tím dostal čtvrtou plochu vedle REST kontraktu, frontendu a hranice záruk (069), jen kvůli testu. Zamítáme.

**3 — Sada je HTTP klientem běžící instance.** Volíme, níž.

### Vstup

Vzorky z `/samples`, vstupy `CrossFrameworkInputs`, nebo vlastní soubory javové strany. Vzorky jsou ukázka nad cizí databází, ne pravda nad schématem fixture; `CrossFrameworkInputs` jsou za hranicí procesu a šly by jen zkopírovat. Zbývá třetí, níž.

### Soubor a balík

Buď artefakty pojmenuje server — pak je to změna REST kontraktu, tedy vydání MINOR podle 069, a je to právě položka párování výstupu se vstupem, která má vlastní otázky (z které entity a jednotek artefakt vznikl) a nemá se rozhodnout bokem —, nebo je jméno odvodí sada z obsahu pravidlem cílového jazyka. Volíme druhé, níž.

### Pořadí

V compose je odpověď jediná: služba aplikace v profilu `test`. V CI jsou tři: postavit v jobu obraz `runtime` a spustit ho, spojit `java-test` s jobem `test`, nebo spustit na runneru publikovanou aplikaci. Obraz v CI zamítlo už rozhodnutí 039 — bez vrstvové cache by se platilo minutami. Spojení jobů by zpomalilo obě sady a smíchalo jejich zprávy. Volíme třetí, níž.

## Rozhodnutí

**Javová sada dostává generované artefakty jako HTTP klient běžící instance nástroje: volá `/convert` na adrese z proměnné `ORMCONVERTOR_API_URL`, posílá vlastní vstupní soubory nad sdíleným schématem fixture a z odpovědi si soubory a balíky odvodí pravidlem Javy. Instanci staví v compose služba `test_app` z obrazu `runtime` a v CI publikovaná aplikace spuštěná na runneru; sada na ni sama počká a bez ní selže, stejně jako bez databáze.**

### Klient, ne čtenář souborů

Důvody jsou čtyři a každý stojí sám.

**Javová sada je konzument, pro kterého je nástroj psaný.** [`use-cases.md`](../use-cases.md) říká, co je ve skutečnosti vstupem: uživatel má projekt, pošle množinu jednotek najednou a dostane artefakty, které vloží do vlastního projektu a přeloží. Přesně to sada dělá — pošle entity a dotazy, dostane javové soubory, přeloží je `javac`, předloží Hibernate a spustí. Že se přitom chová jako každý jiný klient, není náhoda, ale důkaz: F12 žádá „sestavit a spustit vygenerovaný kód", a nejkratší cesta, jak to dokázat, je nechat ho vygenerovat nástrojem, který se ověřuje, ve verzi, která se ověřuje, v témž běhu.

**REST kontrakt je produktová plocha nástroje** (043, 069) a tohle je poprvé, kdy přes ni vede ověření celé cesty — od JSON na drátě po továrnu ve druhém runtimu. Testy kontraktu z rozhodnutí 043 tvrdí, že nástroj odpovídá tak, jak o sobě říká; javová sada od teď tvrdí, že to, co odpoví, cizí ekosystém přeloží a spustí. A na rozdíl od hostitele v paměti je instance v compose ten obraz, který `docker compose up` skutečně spouští: „celý systém" z S5 v doslovném smyslu.

**Nevzniká žádný nový formát ani soukromý kontrakt.** Sada čte odpověď `/convert` tak, jak je, a nic se kvůli ní na nástroji nemění — ani pojmenování artefaktů, ani odkládání souborů. Obě testovací sady zůstávají nezávislé a běží paralelně; každá tvrdí své a žádná nečeká na druhou.

**Selhání ukazuje na místo.** Odpověď má stavový kód, záznamy a verze; `javac` má diagnostiky s řádkem; továrna má výjimku s názvem třídy. Sada je hlásí odděleně, jak to dělají stupně na .NET straně (016): scénář, který selže, řekne, jestli nástroj odmítl, `javac` nepřeložil, Hibernate nepřijal, nebo běh nedopadl.

**Instance je produkční tvar nástroje.** Běží z obrazu `runtime` — téhož, ze kterého běží služba `ormconvertor` — v prostředí `Production`, o kterém mluví předpoklad nasazení (043), a dostává klíč katalogu `ConnectionStrings__CatalogDatabase` mířící na testovací databázi `ORMConvertorTests`, aby fáze doplnění (rozhodnutí 015) byla k dispozici stejně jako v nasazené instanci; podle rozhodnutí 039 je to popis prostředí s heslem jednorázové instance, ne přihlašovací údaj v artefaktu. Advisor sada nikdy nevolá, takže na nativní knihovně nic nezávisí.

**Adresa je konfigurace a bez ní se selže.** `ORMCONVERTOR_API_URL` nese základní adresu včetně cesty `/orm` (`http://testapp:5072/orm`, na runneru `http://localhost:5072/orm`). Jméno `testapp` je síťový alias služby `test_app`, ne druhá služba: podtržítko není v názvu hostitele legální znak, takže `java.net.URI` čte `http://test_app:5072/orm` jako adresu bez hostitele a `HttpClient` z JDK ji odmítne („unsupported URI"). Databázových služeb se to netýká, ty čte JDBC driver. Chybí-li, sada selže s uvedeným důvodem — týž princip jako u `ORMCONVERTOR_TEST_JDBC_URL` a týž argument z rozhodnutí 076: sada běží jen tam, kde pro ni někdo instanci spustil, takže přeskočení nemá co říct. **Na instanci čeká sada sama:** před prvním převodem se ohraničeně ptá na `/required-content`, dokud neodpoví 200. Je to jediný mechanismus pro obě prostředí — obraz `aspnet` nenese `curl`, kterým by compose zdravotní kontrolu napsal, a v CI žádný compose není —, a compose jen seřadí start (`depends_on`) tak, jako řadí sadu za inicializaci databáze.

**Co sada o odpovědi tvrdí, než cokoli přeloží.** Stavový kód 200. `targetFramework` je Hibernate a `targetFrameworkVersion` se rovná verzi, kterou JVM opravdu zavedlo (`org.hibernate.Version.getVersionString()`) — vazba deskriptoru na skutečně načtenou implementaci provedená za běhu, javový protějšek `DeclaredVersionsMatchTheVerificationPackages`; textový test nad `pom.xml` v xUnit zůstává, protože běží bez JVM, a tenhle ho doplňuje z druhé strany. Záznam `Failure` scénář shodí ještě před překladem: nástroj artefakt odmítl (070) a překládat odmítnutý artefakt by netvrdilo nic. Ostatní záznamy se tvrdí tam, kde jsou předmětem scénáře, jinak se nechají být — sada ověřuje přijetí a běh, ne diagnostiku, kterou tvrdí xUnit.

**Nástroje klienta.** HTTP přes `java.net.http.HttpClient` z JDK; JSON přes Jackson, zafixovaný v `pom.xml` jako nářadí sady vedle JUnit — verzi určí implementace, jako u JUnit v rozhodnutí 076. Výčty se posílají a čtou jako čísla, jak kontrakt stanovuje; sada nese jejich hodnoty jako konstanty s odkazem na `ORMEnum` a `ConversionContentType`, protože jinou cestu k nim za hranicí procesu nemá.

### Vstupy jsou soubory javové sady nad sdíleným schématem

Sada posílá **vlastní soubory** z `src/test/resources/`, psané v jazycích zdrojových frameworků — javová entita, třída C# s `hbm.xml`, třída C# s anotacemi EF Core, dotaz v jazyce zdroje — a popisující tabulky **schématu fixture** `Tests/Database/TestSchema.sql`, které sada už staví. Jen tak má 4. stupeň proti čemu běžet: rozhodnutí 016 udělalo ze schématu fixture zdroj pravdy a očekávanou odpověď, a vstup, který popisuje jiné tabulky, by na 4. stupni neměl kam uložit řádek. Konzument posílá svůj projekt, ne ukázky nástroje; sada je konzument, tak posílá svůj.

**Jednotka nese jméno souboru a typ obsahu podle přípony** — touž tabulkou, kterou používá frontend (`.java`, `.cs`, `.hbm.xml`/`.xml`, `.jpql`, `.sql`, …). Záznamy odpovědi pak na soubor ukazují jménem (066) a hláška sady ho může citovat.

**Dvě sady, dvě množiny vstupů nad jedním schématem.** Přijímáme to a říkáme, proč to není rozchod: schéma je jedno a obě sady čtou týž skript, takže vstupy na obou stranách popisují touž pravdu a rozchod se pozná na 4. stupni té strany, která se rozešla. Sdílet i vstupy by dnes znamenalo přepsat konstanty .NET sady na soubory kvůli tomu, aby je četla sada, která je ještě neposílá. Kdyby je diferenční ověření podle F13 potřebovalo na obou stranách totožné — zdrojový dotaz běží v jednom runtimu, přeložený ve druhém —, rozhodne to rozhodnutí k F13, které na tohle podle 076 navazuje; mechanismus už existuje, pom čte `../Tests/Database` jako testovací zdroj a stupeň obrazu to místo kopíruje.

**První scénáře jsou směry, jejichž zdroj tvrdí všechno sám** — Hibernate → Hibernate, EF Core → Hibernate, NHibernate → Hibernate —, protože 2. až 4. stupeň nad nimi běží bez katalogu, tak jako u .NET sady běží nasucho převod EF Core ↔ NHibernate (016). **Dapper → Hibernate** potřebuje katalog a instance klíč má; požadavek na katalog ale bez schématu vysloveného zdrojem — a Dapper ho vyslovit neumí — hledá tabulku v kterémkoli schématu databáze, takže ve chvíli, kdy v `ORMConvertorTests` stojí schémata obou sad najednou, je čtení nejednoznačné. Scénář F6 nad javovým cílem proto počká, až bude sada umět katalog nasměrovat na své schéma; je to fakt pro implementaci, ne pro tohle rozhodnutí, a zapisujeme ho, aby ho nikdo neobjevil jako záhadné selhání.

### Soubor a balík určuje jazyk, ne server

Sada odvodí jméno souboru a adresář z obsahu jednotky: jméno veřejné třídy a řádek `package`. **Nic si tím nevymýšlí** — je to pravidlo Javy (JLS §7.6: veřejný typ nejvyšší úrovně musí ležet v souboru svého jména, adresář odpovídá balíku), které by musel dodržet každý konzument, a `javac` ho vymáhá sám. Frontend dělá totéž jako zobrazovací heuristiku (033); tady to heuristika není, protože jazyk jiné jméno nepřipouští. Položku párování výstupu se vstupem tím nepředbíháme: ta je o tom, z které entity a jednotek artefakt vznikl, a její odpověď na drátě by pravidlo Javy nezměnila — až server artefakty pojmenuje, sada smí jméno číst, ale soubor `Customer.java` v adresáři balíku to bude dál.

**Metodu dotazu obalí harness** třídou v balíku entit, s importy `jakarta.persistence.EntityManager`, `TypedQuery` a `Query` — příspěvek konzumentského projektu, přesně jako `GeneratedQueryCompiler` na .NET straně (027, 040). Holý JPQL jde na 3. stupeň bez obalu: `createSelectionQuery` nad továrnou ho přeloží proti metamodelu bez provedení, což je verdikt téhož druhu jako `session.CreateQuery(hql)` u NHibernate (§6.2) a odmítne i nenamapovanou entitu a neexistující atribut.

**Překlad a zavedení.** `ToolProvider.getSystemJavaCompiler()` přeloží soubory scénáře do dočasného adresáře nad classpath sady — Hibernate i `jakarta.persistence` už tam jsou —, `URLClassLoader` na scénář je zavede a `MetadataSources.addAnnotatedClass` je předá továrně; bootstrap je ten z `HibernateClaimsTest` (standardní názvy vlastností JPA, schéma sady jako výchozí), s generováním schématu vypnutým, protože tabulky jsou skriptu. Čtvrtý stupeň zapisuje v transakci s rollbackem podle pravidla fixture a čte zpět s touž identitou — týž scénář, jaký na .NET straně předvádějí `DapperTo…PersistenceTest`. Negativní polovina je povinná i tady — stupeň, který nikdy neřekne ne, by nedokazoval nic (016) —, a co Hibernate odmítá kdy, se musí změřit, ne předpokládat. Naměřeno (2026-09-18) to vychází takto: **na 3. stupni odmítá chybějící identifikátor**, tedy jedinou kategorii, kterou deskriptor vede jako `Required`; **bezparametrický konstruktor nekontroluje při stavbě továrny, ale při prvním čtení**, které entitu instancuje, takže jeho absenci chytá 4. stupeň; a **`@IdClass` ani `equals` klíčové třídy nevyžaduje vůbec** — obojí je v artefaktu proto, že to žádá specifikace a že s takovým klíčem pracuje kód konzumenta, ne proto, že by si toho tahle implementace všimla. Sada tvrdí všechny tři případy v naměřené podobě, aby se vydání, které začne kontrolovat, ozvalo v testu.

### Compose a CI

**Compose.** Profil `test` dostává službu `test_app`: `target: runtime` z `ORMConvertorAPI/Dockerfile`, `ASPNETCORE_ENVIRONMENT=Production`, klíč katalogu na `test_db`/`ORMConvertorTests`, `depends_on: test_db_init` dokončená. Služba `java_tests` na ní závisí (`service_started`) a dostává `ORMCONVERTOR_API_URL`; služba `tests` se nemění, protože .NET sada ověřuje v procesu a kontrakt si startuje sama (043). Obraz je pořád jeden, jen se z něj v profilu `test` staví o cíl víc, a past `run` bez `build` platí i pro něj — pár příkazů v README se rozšíří o `test_app`.

**CI.** Job `java-test` dostává `actions/setup-dotnet`, publikuje `ORMConvertorAPI` v `Release`, spustí ji na pozadí na `http://localhost:5072` v prostředí `Production` s klíčem katalogu na service container, a teprve pak `mvn -B test`; sada si na instanci počká sama. Nativní knihovna Advisoru v publikaci není a nevadí to — sada Advisor nevolá. Dělba z rozhodnutí 039 tím drží: **workflow tvrdí, že sada prochází proti nástroji tohoto checkoutu a SQL Serveru dané verze, compose tvrdí, že totéž jde v prostředí, kde není nic.** Job `test` se nemění a oba běží dál paralelně.

## Důsledky

**Položka rozhodnutí končí a začíná práce, která je na řadě:** klient a čekání na instanci, odvození souborů a obalení metody, překlad a zavedení, scénáře přijetí a běhu nad Hibernate s vlastními vstupy nad schématem fixture, Jackson v `pom.xml`, `test_app` v compose, .NET v jobu `java-test`, a s tím README (*Tests*), `architecture.md` §6.2 a řádky F7, F10 a F12 v `traceability.md`.

**Až práce doběhne, má F7 na čem stát celé.** Dvě čísla kritéria dodává xUnit, třetí větu javová sada; F7 pak může vstoupit do nároku. Vyňatá oblast 6 je ale vyslovená vcelku (030) a pokrývá F7–F13 i T1–T7, takže jak se hranice překreslí — Hibernate ven, zbytek uvnitř —, řekne §9 v tu chvíli, a vydání, které to nárokuje, je podle 069 MINOR před `2.0.0`. F12 zůstává vyňaté, dokud neexistují wrappery MyBatisu a EclipseLinku a dokud sada nemá šedesát testů a dvacet integračních; F10, dokud se matice neuzavře s F8 a F9.

**Vazba verzí dostává druhou polovinu.** Textový test nad `pom.xml` tvrdí, že deskriptor a pom souhlasí; javová sada od teď tvrdí, že artefakt vydaný pro verzi z odpovědi překládá proti verzi, kterou JVM opravdu zavedlo. Rozejít se mohou jen obě naráz.

**Model hrozeb se nemění.** Překladová cesta dál nic nekompiluje a nespouští; co kompiluje a spouští, je sada, ve vlastním procesu, v kontejneru nebo na runneru. Instance v profilu `test` má klíč katalogu k jednorázové databázi, kterou týž soubor vytváří a zahazuje (029, 039).

**Cena.** Job `java-test` platí instalací .NET SDK a publikací, řádově minutu; profil `test` třetím kontejnerem a delší první stavbou; sada závislostí na Jacksonu; a každý scénář možností selhat na čtyřech místech, což je důvod, proč sada musí místo pojmenovat, ne důvod to nedělat.

**Co toto rozhodnutí neurčuje:** formát normalizovaného výsledku a data pro diferenční ověření (F13 — vlastní rozhodnutí podle 076, které na tohle navazuje); pojmenování artefaktů serverem a jejich párování se vstupem (položka rozhraní); jestli .NET sada někdy bude číst tytéž vstupní soubory; a jak sada nasměruje katalog na své schéma pro scénář Dapper → Hibernate (implementace).

## Historie

**2026-09-18 — revidováno.** Volba se nemění: sada je dál HTTP klientem běžící instance, vstupy jsou dál její vlastní soubory nad schématem fixture a soubory s balíky si dál odvozuje pravidlem Javy. Opravené jsou dva případy, na které se při psaní nemyslelo a které našla implementace téhož dne.

Adresa v compose nemohla platit tak, jak tu stála. Podtržítko není legální znak názvu hostitele, takže `java.net.URI` z `http://test_app:5072/orm` nepřečte hostitele a `HttpClient` adresu odmítne ještě před odesláním; služba si proto vedle svého jména nese síťový alias `testapp` a proměnná míří na něj. Jméno služby zůstává `test_app` kvůli souhlasu se sousedy v témž souboru a alias je zapsaný tam, kde překáží.

A negativní polovina předpokládala víc, než Hibernate dělá. Původní znění tvrdilo, že továrna musí odmítnout artefakt, kterému harness odebere vynucený člen, a jmenovalo bezparametrický konstruktor a `equals` klíčové třídy. Hibernate 7.4.5 postaví továrnu nad oběma a navíc i nad složeným klíčem bez `@IdClass` — několik atributů `@Id` bez klíčové třídy mu stačí. Chybějící bezparametrický konstruktor se ozve teprve při čtení, které entitu instancuje, tedy na 4. stupni; při stavbě továrny odmítá Hibernate z toho, co artefakt nese, chybějící identifikátor. Věta o negativní polovině je proto přepsaná na to, co se změřilo, a sada tvrdí všechny tři případy v naměřené podobě — týž postup, jakým se 2026-09-17 vyrovnala s `precision` u `LocalDateTime` (rozhodnutí [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md)). Že vynucené členy zůstávají v artefaktu, se tím nemění: hranici předávaného artefaktu určuje přenositelnost podle specifikace (rozhodnutí [040](040-boundary-of-the-handed-over-artifact.md)), ne to, co jedna implementace odpustí.
