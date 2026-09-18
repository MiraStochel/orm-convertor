# 082 — Čtení i zápis T-SQL bydlí ve sdíleném projektu

Datum: 2026-09-18
Stav: platí
Požadavky: F8, F10, F11, S1, S2
Podklad: rozhodnutí [013](013-target-framework-versions.md), [026](026-home-of-shared-query-reading.md), [062](062-hql-read-by-a-hand-written-parser.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md) a [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md); [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §10 a §15; [`architecture.md`](../architecture.md) §5 a §9 (vyňatá oblast 5); JSS §3.2 a dotazová část Table 2

## Kontext

Rozhodnutí 026 vyslovilo dvě pravidla naráz a o tom, které z nich na daný jazyk padne, rozhoduje jediná otázka: kolik frameworků ten jazyk čte. Jazyk sdílený víc frameworky dostává vlastní projekt na úrovni ekosystému — tak vznikl `LinqParsing` a po jeho vzoru `CSharpEntityParsing`, `JavaEntityParsing` i `JakartaPersistence`. Jazyk jediného frameworku bydlí ve wrapperu toho frameworku, který v něm píše, a smí si na něj přinést balíček — tak T-SQL zůstalo v `DapperWrappers` i s parserem `Microsoft.SqlServer.TransactSql.ScriptDom`. V srpnu to byla správná větev, protože SQL v řešení opravdu četl jediný framework.

Od té doby se ten fakt změnil dvakrát.

**MyBatis (F8) je druhý čtenář.** Dotazová část Table 2 článku dává MyBatisu u pravidel Q2–Q12 tentýž sloupec jako Dapperu — „SQL only", bez strukturované reprezentace dotazu (Q1) a bez podmínkového stromu (Q5) —, takže dotazovým artefaktem MyBatisu je text SQL v XML mapperu nebo v anotaci `@Select`. Není to podobnost, je to táž kategorie: obojí je framework, kde dotaz je jen řetězec, a mezireprezentace do něj vstupuje toutéž cestou.

**NHibernate je od 2026-09-18 třetí.** Rozhodnutí 081 otevřelo `hbm.xml` dotazovému průchodu; `<query>` s HQL se čte, ale druhá forma pojmenovaného dotazu, `<sql-query>`, nese nativní SQL. Mapovací parser ji dnes hlásí jako `Loss` a důvod je jediný: gramatika, která SQL čte, sedí uvnitř Dapper wrapperu, kam `NHibernateWrappers` podle S1 nesmí. Není to mez modelu ani mez jazyka — mezireprezentace ten dotaz unese a parser by ho přečetl —, je to mez rozmístění kódu, a takovou mez otevřená položka nést nemá.

Tím pravidlo 026 vybírá druhou větev a otázka, kde T-SQL bydlí, se otevírá znovu.

Otevřená položka k ní navíc nesla tvrzení, které je třeba vyvrátit dřív, než se na ni odpoví. Znělo, že SQL MyBatisu — psané i pro jiné databázové systémy, protože MyBatis pojem dialektu nemá vůbec a přenositelnost nechává na autorovi SQL (§10 srovnání) — přečte jedině vlastní parser SQL v javovém wrapperu. Je to věta z Důsledků rozhodnutí 026 a jde proti dvěma jeho vlastním úvahám: 026 ručně psaný parser T-SQL zamítlo ve prospěch referenční gramatiky a 062 obhájilo vlastní parser jedině pro malý jazyk, který nástroj sám vydává a round-tripem drží.

Nerozhodnuté tedy zůstává trojí: kde bydlí čtení, co se s ním sdílí na straně **zápisu**, a co se stane se zástupnými symboly `#{}` a `${}` a s dynamickými značkami MyBatisu, které T-SQL nejsou.

## Zvažované varianty

### 1 — Nechat T-SQL v Dapper wrapperu a dát MyBatisu vlastní parser SQL

Doslovné čtení Důsledků 026. Zamítáme, a stojí za to říct, proč to zamítnutí není revizí toho rozhodnutí: ta věta byla predikcí následku, ne argumentovanou variantou, a předpokládala, že druhým čtenářem SQL bude framework nad jiným dialektem. Důvody jsou tři.

**Zamítnutí varianty 5 rozhodnutí 026 platí beze změny.** Tam šlo o to, že proti vlastnímu parseru stojí referenční parser od výrobce jazyka, zadarmo a generovaný z formální gramatiky, takže vlastní parser je luxus placený vlastními dírami v gramatice — a třída selhání není výjimka, nýbrž tiše jiný význam dotazu. Nic z toho se nezměnilo.

**Tři rozdíly, kterými 062 tentýž závěr obrátilo, tady nevycházejí ani jeden.** Jazyk není menší: MyBatis píše celé T-SQL, ne podmnožinu vymezenou tím, co model unese. Kotva round-tripu nedrží: u HQL je vstupem parseru výstup builderu, kdežto SQL zdroje píše člověk a shoda s tím, co vydává Dapper builder, netvrdí nic o tom, co přijde na vstup. A volba nezní „vlastní, nebo žádný", jako u HQL za zdí S1, nýbrž „vlastní, nebo referenční".

**A nepomohlo by to ani NHibernate.** `<sql-query>` by si po téhle cestě žádalo třetí parser téhož jazyka ve třetím wrapperu. Duplicitu tohohle druhu už repozitář jednou zaplatil u `DapperEntityParser` a `NHibernateEntityParser` a je to přesně důvod, pro který 026 vzniklo.

### 2 — Multidialektová knihovna třetí strany

`SqlParserCS` (port `sqlparser-rs`) nebo gramatika z `grammars-v4` nad ANTLR by přečetly i SQL psané pro MySQL, PostgreSQL či Oracle. Rozhodnutí 026 tuhle variantu nezamítlo navždy — odložilo ji ke druhému dialektu. Tvrdíme, že druhý dialekt tímhle rozhodnutím nepřichází, a zamítáme.

**Dialekt není fakt o MyBatisu, nýbrž o projektu.** Rozhodnutí 013 fixuje na obou stranách SQL Server 2022 a §9 architektury drží cílový dialekt jako vyňatou oblast 5: jediným dialektem je SQL Server a víc nástroj netvrdí. Přečíst `LIMIT 10` psané pro MySQL a vydat z něj `OFFSET/FETCH` by nebylo splnění F8, nýbrž tichý překlad dialektu — artefakt by vypadal správně a tvrdil by něco, co verze nenárokuje. To je horší než odmítnutí, protože odmítnutí je vidět: syntaktická chyba je `Failure` s řádkem a sloupcem, týž tvar, jaký `TSql160Parser` vydává dnes a na kterém stojí věta S7 o chybě na úrovni souboru a řádku.

**Na dialekt, který máme, by to byla slabší záruka.** Pro jediný dialekt v řešení dává komunitní gramatika osmi dialektů slabší záruku než gramatika generovaná výrobcem toho jednoho; a tvrzení o tom, co nástroj přečte, je podle 026 tvrzením o verzi parseru, tedy věcí S2.

**Otázka se vrátí, ale s jiným vstupem.** Znovu ji otevře deklarace cílového dialektu v deskriptoru — vlastní otevřená položka —, ne příchod frameworku, který dialekt nemá.

### 3 — Vložit čtení T-SQL do `Common` nebo do `AbstractWrappers`

Zamítáme týmiž dvěma důvody, kterými 026 zamítlo tytéž dvě varianty u LINQ, a jsou doslovné, ne analogické. `AbstractWrappers` dnes nemá závislost na ScriptDomu; kdyby ji dostal, poteče parser T-SQL do každého konzumenta překladového kontraktu — tedy i do Hibernate a EclipseLink wrapperu, které SQL nečtou ani nepíší. `Common` navíc leží pod `AbstractWrappers`, takže by nemohl referencovat `AbstractQueryBuilder`, do kterého parser zapisuje. Základ popisuje překlad, ne jazyk jednoho ekosystému.

### 4 — Vlastní projekt jazyka, referencovaný wrappery, které v něm čtou nebo píší

## Rozhodnutí

**Volíme variantu 4. Čtení i zápis T-SQL dostávají vlastní projekt `TransactSql`, který referencují `DapperWrappers`, `NHibernateWrappers` a — až vznikne — `MyBatisWrappers`. Dialektem zůstává SQL Server 2022 a jiný dialekt zůstává vyňatý ze záruk.**

**Pravidlo 026 se tím naplňuje, ne ruší.** Jeho druhá věta nevybírala Dapper wrapper natrvalo, nýbrž proto, že T-SQL tehdy četl jediný framework; první věta je pravidlo pro jazyk sdílený víc frameworky a od 2026-09-18 platí právě ta. Projekt je proto vrstvou ekosystému, ne jádra: referencuje `Model`, `AbstractWrappers` a `Common`, nese balíček ScriptDom a nikdo jiný než tři jmenované wrappery ho nevidí. **Je to ovšem první sdílený projekt, který překračuje hranici ekosystémů** — dva jeho konzumenti jsou .NET a třetí javový —, a je to v pořádku: vrstvu nevymezuje ekosystém, nýbrž jazyk, a tím je tu T-SQL na obou stranách. Rozhodnutí 076 ho ostatně mezi sdílenými projekty javové strany jmenovalo předem („sdílené čtení T-SQL pro Dapper a MyBatis"); tohle rozhodnutí k nim přidává třetího konzumenta a pojmenovává obsah.

**Název projektu jmenuje dialekt schválně.** `TransactSql` říká, co je uvnitř a kam až to sahá, stejně jako `JakartaPersistence` jmenuje sdílenou specifikaci a ne činnost; hranice z varianty 2 je tím vidět ve stromu řešení, ne až v dokumentaci.

**Sdílené čtení je třída, ne základní třída — a v tom se od `LinqParsing` liší.** `LinqQueryParser` je abstraktní `IQueryParser` se zásuvným bodem, protože LINQ má co parametrizovat: kořen dotazu se u EF Core a NHibernate píše jinak. T-SQL nemá nic. Text `SELECT`u je v Dapperu, v `<sql-query>` i v mapperu MyBatisu týž jazyk s touž sémantikou a liší se jedině tím, **odkud se ten text bere** — a to není zásuvný bod gramatiky, nýbrž čtení vlastního artefaktu wrapperu. Rozhoduje o tom třetí konzument: `NHibernateXmlQueryParser` už existuje, nárokuje si typ obsahu `XML` a pro každý `<query>` deleguje na `NHibernateHqlQueryParser`; zdědit navíc sdílený parser SQL by nemohl, protože dědí se jednou a on už je čtenářem dvou jazyků naráz. Kompozice tedy není vkusová volba, nýbrž jediná, která tenhle případ unese.

`TransactSql` proto vydává tři věci:

- **`SqlQueryReader`** — čte jeden text `SELECT`u do dodaného `AbstractQueryBuilder`u. Builder a hlásicí kanál bere v konstruktoru, tedy v témž tvaru, jaký v Dapper wrapperu dnes má `DapperSqlQueryVisitor`, a `IQueryParser` není: který parser si nárokuje který typ obsahu, zůstává tvrzením wrapperu (rozhodnutí 025, 047 a 081).
- **`SqlQueryVisitor`** — zápis instrukcí do SQL, dnešní `DapperSqlQueryVisitor` beze změny chování.
- **`AbstractSqlQueryBuilder`** — sedm kroků šablony, skládání klauzulí ve správném pořadí včetně umístění `TOP` a `OFFSET/FETCH`, vykreslení množinových operací a poddotazů a odvození výsledného typu z tabulky.

**Na straně zápisu se sdílí víc než jen visitor a je třeba to vyslovit, protože položka žádala jen jeho.** Projdeme-li dnešní `DapperSqlQueryBuilder` krok po kroku, je frameworkově specifická jediná metoda — ta, která hotový `SELECT` zabalí do artefaktů: Dapper z něj dělá metodu nad `IDbConnection` a vedle ní vydává holý dotaz (rozhodnutí 025), kdežto MyBatis týž text zabalí do `<select>` s `resultType` uvnitř mapperu. Všechno ostatní, včetně odvození výsledného typu z tabulky se záznamem `Convention`, je SQL, a MyBatis to potřebuje beze zbytku: `resultType` je táž otázka jako materializační typ Dapperu, protože ani jeden framework ho v dotazu nejmenuje. `AbstractSqlQueryBuilder` má proto tvar `AbstractJpaQueryBuilder` z rozhodnutí 077 — potomek dodává obálku artefaktu a deskriptor, zbytek dědí. Že tenhle tvar udělá druhý wrapper tenkým, není domněnka: rozhodnutí 080 to na JPA vrstvě doložilo pěti soubory EclipseLinku.

**Zástupné symboly a dynamické značky nejsou věc gramatiky.** `#{id}` ani `${table}` T-SQL nejsou, `TSql160Parser` je nepřečte a učit ho je nesmíme — vlastní úprava referenční gramatiky je přesně ten privátní fork, kterému se celé tohle rozhodnutí vyhýbá. Jsou to prvky vrstvy nad jazykem a patří parseru MyBatisu: ten nejdřív vyhodnotí dynamické značky (`<if>`, `<choose>`, `<where>`, `<set>`, `<trim>`, `<foreach>`, `<bind>`) a nahradí zástupné symboly, a teprve výsledný prostý T-SQL předá sdílenému čtení. Javový protějšek téhle operace existuje — `getBoundSql` vrací SQL pro konkrétní sadu parametrů bez databáze (§15 srovnání) —, ale nástroj MyBatis nespouští a JVM ve wrapperu nemá (rozhodnutí 076), takže substituci musí udělat sám. **Co se čím nahradí a co z toho unese mezireprezentace, je věcí rozhodnutí k F8** spolu s operandem parametru; 024 pojem parametru vědomě odložilo a 070 dotaz s parametrem dnes odmítá. Jednu hranici ale vyslovujeme už tady, protože plyne z pravidla 053 a rozhodnutí k F8 ji nesmí přehlédnout: `#{}` je zástupce **hodnoty** a kandidát na operand parametru, kdežto `${}` je textová substituce, která může nést jméno tabulky nebo celou klauzuli — parser, který by ji nahradil parametrem, by tvrdil něco, co zdroj neříká, a výsledná množina řádků by mohla být jiná.

**S1 se naplňuje, ne obchází.** Vytknout parser z `DapperWrappers` je zásah do cizího wrapperu a děje se **jednou a teď**, přesně jako vytknutí čtení LINQ z `EFCoreWrappers`; od té chvíle je přidání MyBatis wrapperu čistě přírůstkové. A `NHibernateWrappers`, který nově referencuje `TransactSql`, nezávisí na cizím wrapperu ani na frameworku, pro který generuje: závisí na projektu **jazyka**, což je týž rozdíl, jaký 026 vyslovilo o samotném ScriptDomu — čtu jazyk, ve kterém je vstup napsaný, nevolám framework, pro který generuji.

**Přesun je jednorázový přepis** (rozhodnutí 003): žádné přechodné období, žádná kopie navíc. Že jde opravdu o přesun, a ne o nové psaní, drží existující sada — dotazové testy Dapperu musejí projít beze změny očekávaných výstupů.

## Důsledky

**Balíček se stěhuje, gramatika zůstává týmž tvrzením.** `Microsoft.SqlServer.TransactSql.ScriptDom` přechází z `DapperWrappers` do `TransactSql`; Roslyn v Dapper wrapperu zůstává, protože ten dál vytahuje řetězcový literál z volání `Query<T>` (rozhodnutí 047). Tabulka zafixovaných verzí v `architecture.md` drží oba řádky i nadále — verze balíčku a verze gramatiky `TSql160Parser` jsou dvě různá tvrzení o tom, co nástroj přečte (S2) —, mění se jen projekt, ve kterém balíček je.

**Nový projekt znamená dva zápisy mimo `.csproj`.** Do `ORMConvertor.sln` a do stupně `dotnet-build` v `ORMConvertorAPI/Dockerfile`, který kopíruje každý soubor projektu jménem. Ten seznam už dvakrát rozbil stavbu obrazu, naposledy u `EclipseLinkWrappers`, a je to jediné místo, kde se nový projekt neohlásí sám.

**`<sql-query>` se dočte prací uvnitř wrapperu a žádné další rozhodnutí nepotřebuje.** `NHibernateXmlQueryParser` si ho přečte sdíleným čtením vedle `<query>` a mapovací parser přestane hlásit `Loss`; nativní dotaz z `hbm.xml` se tím stává zdrojem pro všech devět .NET směrů i pro směry s javovou stranou. **Platí pro něj přitom totéž, co pro `#{}`:** nativní dotaz NHibernate umí nést zástupné symboly `{alias}.*` a návratové mapování v `<return>`, což T-SQL rovněž není. Pravidlo je tedy obecné a ne mybatisovská výjimka — wrapper předává sdílenému čtení prostý T-SQL, nebo dotaz odmítne záznamem; co přesně z těch dvou u `<return>` nastane, řekne implementace té položky.

**Hranice záruk se tímhle rozhodnutím nehýbe.** Vyňatá oblast 5 zůstává, položka o cílovém dialektu v deskriptoru zůstává otevřená a nárok je týž jako dosud: zdrojová i cílová strana je T-SQL nad SQL Serverem. Přibývá jen to, že totéž tvrzení nově platí pro tři wrappery místo jednoho.

**Rozhodnutí 026 nedostává stav `nahrazeno`.** Jeho pravidlo platí celé a beze změny; mění se jediná věta jeho Důsledků — ta, která předpovídala vlastní parser SQL v javovém wrapperu —, a mění ji tenhle soubor, ne přepis tamtoho. Je to táž úvaha, kterou rozhodnutí 021 nechalo v platnosti 011: označit celé 026 za nahrazené by čtenáře poslalo hledat pravidlo o domovech sdíleného čtení do souboru, který ho neobsahuje. Vazbu proto nese tenhle směr a index.

**Hibernate a EclipseLink `TransactSql` nikdy neuvidí.** Platí pro něj pravidlo 026 v obou směrech: javové wrappery kromě MyBatisu ho nereferencují, stejně jako .NET wrappery nevidí `JavaEntityParsing`. Sdílených vrstev ekosystému je tím pět — `LinqParsing`, `CSharpEntityParsing`, `JavaEntityParsing`, `JakartaPersistence` a `TransactSql` — a žádná z nich se nedotýká `AbstractWrappers`, `Common` ani orchestrace, což je cena, kterou S1 pro nový framework slibuje.

**Testy.** Přesun sám dokládá stávající sada beze změny očekávaných výstupů; to je jeho definice. Nad rámec toho platí pro sdílené čtení týž důkaz, jaký 026 vyžádalo pro LINQ: **týž text SQL přečtený z Dapper jednotky a z `<sql-query>` musí dát tutéž mezireprezentaci.** Že je jazyk jeden, se tím přestává tvrdit o kódu a začíná tvrdit o chování.
