# 087 — Integrační test je běh proti databázi a sada si ho počítá sama

Datum: 2026-09-21
Stav: platí
Požadavky: F1, F2, F12, S2, S5, S6
Podklad: rozhodnutí [016](016-generated-artifact-verification-levels.md), [034](034-central-version-management.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [078](078-java-suite-as-a-client-of-a-running-instance.md) a [084](084-mybatis-wrapper-over-the-shared-sql-reading.md); [`requirements.md`](../requirements.md), F12; [`architecture.md`](../architecture.md) §6.2; otevřená položka „Javová sada nemá vyslovený počet integračních testů"

## Kontext

Ověřovací kritérium F12 má tři věty: sada je **spustitelná jedním příkazem**, má **alespoň 60 javových testů** a **z toho nejméně 20 integračních**. První dvě jsou dnes doložené a zkontrolovatelné — sadu spouští jediné `docker compose --profile test run --rm java_tests` a Surefire na konci běhu vypíše, kolik testů proběhlo. Třetí věta doložená není, a není to nedopatření: **pojem „integrační test" v sadě neexistuje.** Testy se nijak nedělí, nic je neoznačuje, a zjistit jejich počet lze jedině tak, že člověk přečte sedm zdrojových souborů a sečte metody — u parametrizovaných ještě vynásobí počtem hodnot výčtu.

Číslo získané takhle je přesně ten druh tvrzení, který tenhle projekt jinde odmítá. Rozhodnutí 034 zavedlo jediné strojově psané místo pro verzi právě proto, aby dvě místa nemohla tvrdit každé jiné číslo, a [`architecture.md`](../architecture.md) §1 schválně nepíše ani to, kolik je balíčků, aby dvě místa nemusela počítat stejně. Nárok na F12 opřený o ruční přepočítání by zestárnul prvním přidaným testem a nikdo by se to nedozvěděl.

Rozhodnutí 084 posouzení sady podle F12 vědomě odložilo spolu s maticí F10; tohle rozhodnutí odbavuje tu první polovinu. Slovník pro to existuje: **rozhodnutí 016 dělí ověření generovaného artefaktu na čtyři stupně** — tvar, překlad, přijetí frameworkem a **běh proti databázi** — a čtvrtý stupeň je přesně to, čemu se v testovací praxi říká integrační test. Do javové sady ten slovník jen nikdo nepřenesl.

Otázky jsou dvě a každá má víc než jednu rozumnou odpověď: **co se počítá** a **kdo počítá**.

## Zvažované varianty

### 1 — Spočítat rukou a číslo zapsat do dokumentace

Do [`traceability.md`](../traceability.md) k řádku F12 napsat „67 testů, z toho 25 integračních" a hotovo.

Zamítáme. Není to kontrola, je to tvrzení, které se s prvním přidaným nebo odebraným testem tiše rozejde se skutečností — a rozejde se právě v dokumentu, jehož jediným úkolem je držet vazbu požadavku na důkaz. Tentýž důvod, pro který 034 zrušilo opisování verzí.

### 2 — Integračním testem je každý test, který sáhne na databázi

Nejjednodušší mechanická definice: má spojení, tedy je integrační.

Zamítáme, protože počítá věci, které o nástroji netvrdí nic. `TestSchemaTest` staví schéma fixture ze sdíleného skriptu a čte ho zpátky z katalogu — ověřuje **naši vlastní testovací infrastrukturu**, ne překlad; kdyby se počítal, nárok na F12 by se opíral o testy, které by prošly i tehdy, kdyby nástroj negeneroval vůbec nic. F12 žádá sadu, která „sestaví a **spustí vygenerovaný kód**", a to je jiná věta než „připojí se".

### 3 — Integračním testem je jen běh artefaktu vygenerovaného v témž běhu

Opačná krajnost: počítá se jedině to, co přišlo z běžící instance přes `/convert` (rozhodnutí 078).

Zamítáme jako příliš úzké. Vyřadilo by to `HibernateClaimsTest` a `MyBatisClaimsTest`, tedy testy nad entitami psanými rukou **v tom tvaru, v jakém je builder vydává**, a právě ty vynesly tři vady, které žádný jiný stupeň nenašel: přesnost zlomků sekundy (rozhodnutí [079](079-fractional-second-precision-as-second-precision.md)), rollback session MyBatisu a vázaný počet řádků (rozhodnutí [085](085-a-row-count-is-a-number-or-a-parameter.md)). Tvar, který builder vydává, je předmětem tvrzení úplně stejně jako text, který vydal v tomhle běhu; rozdíl je v tom, odkud text přišel, ne v tom, co se měří.

### 4 — Čtvrtý stupeň rozhodnutí 016, označený a spočítaný sadou samotnou

## Rozhodnutí

**Volíme variantu 4. Integračním testem je test, který provede databázovou operaci skrz artefakt, jaký nástroj vydává — ať už ho vydal v témž běhu, nebo je psaný rukou v tom tvaru, v jakém ho vydává. Je to čtvrtý stupeň ověření rozhodnutí 016, nese značku `@Tag("integration")`, a kolik jich je, si sada tvrdí sama.**

### Co značku nese a co ne

Značku nese metoda, jejíž verdikt závisí na tom, co se v databázi opravdu stalo: uložený a zpátky načtený řádek, počet vrácených řádků, sloupec přečtený z katalogu po DDL, kterým prošel generovaný nebo takto psaný artefakt. Nenese ji test, který artefakt jen přeloží (2. stupeň), předloží frameworku (3. stupeň) nebo posoudí text odpovědi.

Dvě třídy stojí za vyslovení, protože se u nich mohl čekat opak.

- **`TestSchemaTest` značku nemá.** Staví a čte schéma fixture; je to test naší infrastruktury, ne překladu, a do počtu, na kterém stojí nárok na F12, nepatří (viz varianta 2).
- **`EclipseLinkClaimsTest` ji nemá také**, ačkoli se jmenuje stejně jako dvě třídy, které ji nesou. Jeho tvrzení měří **vygenerovaný skript DDL**, ne databázi: nic se nevytváří a skript je celý důkaz. Je to 3. stupeň v jiném kabátě.

### Kdo počítá

**Sada si počet tvrdí sama, testem `SuiteSizeTest`.** Ten projde vlastní přeložené třídy a spočítá invokace: `@Test` je jedna, `@ParameterizedTest` se zdrojem `@EnumSource` jich má tolik, kolik má výčet hodnot. Značka se čte z metody i z třídy. Výsledek se porovná s oběma mezemi kritéria — **nejméně 60 testů a nejméně 20 s značkou** —, protože meze jsou to, co kritérium říká; přesné číslo by se rozbíjelo při každém přidaném testu a netvrdilo by o nic víc.

**Parametrizaci, kterou neumí spočítat, čítač odmítne** a test selže se jménem té metody. Je to schválně: nová podoba parametrizace musí čítač naučit, jinak by tiše počítala jedna a nárok by se propadl pod mez, aniž by se něco ozvalo.

Proč reflexe nad přeloženými třídami, a ne `Launcher.discover()` z `junit-platform-launcher`, který sada už má v závislostech: **discovery vrací u `@ParameterizedTest` šablonu, ne její invokace** — ty vznikají teprve při běhu —, takže by se parametrizovaná polovina sady počítala po jedné a čítač by ukazoval zhruba třetinu skutečnosti. Počítat se dá i ze sestav Surefire po běhu, jenže to už není test, nýbrž krok navíc, který by se v kontejneru i v CI musel spouštět zvlášť a mohl by se zapomenout.

Proč to počítá javová sada, a ne xUnit, který `pom.xml` jako text už čte (`JavaSuiteDependenciesMatchTheJavaDescriptors`): tam je předmětem **text** souboru, tady **přeložené třídy**, a ty má k dispozici jedině ta sada. Číst javový zdroj v C# a počítat v něm anotace by znamenalo napsat třetí čtenář Javy kvůli jednomu číslu.

### Co se tím doplňuje

Označit, co existuje, je půlka; druhá je zavřít mezeru, kterou označení ukáže. **Složený klíč dosud javová strana jen překládala a mapovala, nikdy nespustila.** Entita `CustomerOrder` se dvoudílným klíčem je ve všech třech zdrojových směrech vstupem sady od začátku, ale 4. stupeň nad ní neběžel ani u jednoho ze tří cílů — a složený identifikátor je věc, kolem které stojí F1 i F2 a celé zadání. Doplňujeme ho proto jako scénář 4. stupně u všech tří javových cílů ve všech třech směrech, v transakci s rollbackem podle pravidla fixture. U obou JPA cílů se objednávka uloží generovanou entitou, kontext se vyprázdní a řádek se najde zpátky instancí generované třídy `@IdClass`, takže obě části porovnává framework přes `equals` a `hashCode`, které napsal builder — a klíč lišící se v jedné části nesmí najít nic. U MyBatisu je to jinak a je to jeho věc: klíč entity nezná a dvě `<id>` jsou u něj identita *výsledku* (rozhodnutí 084), takže objednávku čte příkaz konzumentského projektu, který generovanou `<resultMap>` jmenuje kvalifikovaným id a ptá se oběma částmi. Tím dělá javová strana totéž, co .NET strana dělá `DapperToNHibernatePersistenceTest`em — identitní členy vykonává framework, ne aserce.

### Co se nemění

Sada zůstává bez přeskočení (rozhodnutí 076): značka vybírá, co se **spustí**, když o to někdo požádá, ale výchozí běh spouští všechno a chybějící databáze je dál selhání, ne tiché přeskočení. `mvn test -Dgroups=integration` je tedy nástroj pro ladění a pro text práce, ne druhý režim sady, a ani kontejner, ani CI ho nepoužívají.

## Důsledky

**Třetí věta kritéria F12 se stává kontrolovatelnou.** Od téhle chvíle nemůže počet klesnout pod mez, aniž by spadl build — a nemůže ani tiše zestárnout dokument, protože číslo v něm žádné není: [`traceability.md`](../traceability.md) odkazuje na test, ne na hodnotu.

**Javová strana dostává složený klíč doložený během.** Je to devět nových integračních testů (tři cíle × tři zdrojové směry) a zároveň první případ, kdy javový cíl provede identitu složeného klíče, místo aby se o ní jen tvrdilo, že je v artefaktu zapsaná. Sada tím má 78 testů, z toho 34 integračních.

**První běh těch devíti našel vadu, kterou žádný stupeň nad tvarem najít nemohl.** Vstupní jednotky `CustomerOrder` všech tří zdrojů pojmenovávaly tabulku `Orders`, ale nevyslovovaly její sloupec `PlacedAt`, který má fixture `NOT NULL` — takže generovanou entitou nešlo zapsat vůbec nic, a Hibernate i EclipseLink to řekly týmž způsobem, odmítnutím při zápisu. Chybný přitom nebyl artefakt, nýbrž **jednotka, která tabulku popisovala neúplně**, a to je vada, kterou 1. až 3. stupeň nemá jak vidět: neúplný popis dá dokonale platný artefakt. Vstupy sloupec od téhle chvíle vyslovují, u Hibernate rovnou i s `secondPrecision` rozhodnutí [079](079-fractional-second-precision-as-second-precision.md).

**F12 z vyňaté oblasti 6 nevystupuje tímhle rozhodnutím, nýbrž až zeleným během v zafixovaném prostředí** — profil `test` v compose, obraz Mavenu s Temurinem 25, SQL Server 2022 —, jak to platilo u F7, F8 i F9. Do té doby je požadavek dál vyňatý a [`open-items.md`](../open-items.md) to nese jako zbývající krok, ne jako hotovou věc.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to PATCH:** mění se testovací sada a nic ve tvaru vydávaného artefaktu, v REST kontraktu ani v hranici záruk. Až hranici změní ten zelený běh, bude to MINOR z téhož důvodu, z jakého jím bylo vstoupení F7, F8 a F9.

**Testy.** Vlastní počet sady oběma mezemi kritéria, a parametrizace, kterou čítač nezná, jako selhání se jménem metody. U všech tří javových cílů a ve všech třech zdrojových směrech uložení a zpětné načtení entity se dvoudílným klíčem podle obou jeho částí; u JPA cílů přes klíčovou třídu, kterou builder vydává, u MyBatisu přes parametry příkazu, protože jeho mapper klíčovou třídu nezná. Dosavadních 67 testů projde beze změny, protože značka na verdikt žádného z nich nesahá.
