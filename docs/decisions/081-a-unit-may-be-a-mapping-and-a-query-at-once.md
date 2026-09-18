# 081 — Jednotka smí být mapováním i dotazem zároveň

Datum: 2026-09-18
Stav: platí
Požadavky: F8, F10, F11, F14, S1, S2, S7
Podklad: rozhodnutí [025](025-query-language-as-content-type.md), [045](045-a-conversion-that-produced-nothing-says-so.md), [047](047-content-type-reaches-the-query-parser.md), [066](066-records-attributed-to-the-input-unit.md) a [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md); [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §4 a §15; [`use-cases.md`](../use-cases.md), „Co je ve skutečnosti vstupem"

## Kontext

Orchestrace pouští převod ve dvou průchodech ([`architecture.md`](../architecture.md), §5 a §7): nejdřív entitní parsery nad mapovacími jednotkami do jednoho sdíleného entitního builderu, pak dotazové parsery nad dotazovými jednotkami, každou do vlastního dotazového builderu. Pořadí není libovolné — dotaz sám o sobě často nenese název tabulky ani sloupce, takže dotazový parser dostává hotové `EntityMaps`, a mezi oba průchody se navíc vklíní fáze doplnění z katalogu (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)). Dotaz se tedy překládá až proti mapování, které je úplné.

Rozhodčím mezi průchody je typ obsahu: `ConversionContentTypes.IsQuery()` rozdělí seznam jednotek na dvě disjunktní části. Typ obsahu je proto **buď** mapování, **nebo** dotaz, a deklaruje ho u každé jednotky klient.

Mapovací dokumenty, které nástroj čte, ale pojmenovaný dotaz nesou — a nesou ho v témž souboru jako mapování:

- **NHibernate `hbm.xml`** má `<query>` a `<sql-query>` jako sourozence `<class>` pod kořenem `<hibernate-mapping>`. Parser čte jen `<class>` a každý jiný prvek kořene hlásí záznamem `Loss` o nepřečteném prvku.
- **JPA** má `@NamedQuery` na entitě a `<named-query>` v `orm.xml` u obou implementací. Od rozhodnutí [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md) je to rovněž `Loss`.
- **XML mapper MyBatisu** má `<resultMap>` jako mapování a `<select>` jako dotaz — a je to **forma jediná**: mapper je primární mapovací artefakt frameworku i jediné místo, kde žije jeho SQL (§4 a §15 srovnání).

Dosud to nevadilo, protože u NHibernate i u JPA je pojmenovaný dotaz volitelná forma: uživatel ji nemusí použít a její nepřečtení nástroj hlásí. F8 tuhle únikovou cestu bere. Jednotkou převodu je přitom obsah jednoho souboru ([`use-cases.md`](../use-cases.md)) a záznam ze čtení na ten soubor jménem ukazuje (rozhodnutí [066](066-records-attributed-to-the-input-unit.md)).

**A není to jen „obojí".** Jeden mapper nese `<resultMap>`ů i `<select>`ů víc. Dotazový průchod má dnes jeden builder na jednotku a všech pět dotazových builderů vypisuje metodu s pevným názvem (`Query`, v JPA `query`), takže dva `<select>`y téhož souboru by vydaly dva artefakty se stejně pojmenovanou metodou. Dvojakost jednotky a mnohost dotazů v ní jsou jedna otázka, ne dvě: kdo připustí první, musí odpovědět i na druhou.

Do téhož rozhodnutí patří hodnota `XML` typu obsahu. Rozhodnutí [025](025-query-language-as-content-type.md) chce, aby hodnoty jmenovaly jazyk, ne framework; `XML` dnes jazyk jmenuje, ale popis v modelu i popisek v rozhraní slibují mapování, a s `orm.xml` obou implementací JPA a s mapperem MyBatisu ponese ta jedna hodnota tři dialekty, z nichž jeden mapování není.

## Zvažované varianty

### 1 — Jednotku rozřeže klient

Uživatel — nebo naše vlastní rozhraní za něj — mapper rozstříhá na mapovací část a na dotazové části a pošle je jako samostatné jednotky. Orchestrace se nemění vůbec.

Zamítáme, a rozhoduje o tom nemožnost, ne nechuť. `<select resultMap="authorMap">` odkazuje na `<resultMap>` identifikátorem uvnitř téhož dokumentu nebo ho nese vnořený, `<include>` vtahuje fragment `<sql>` odjinud z téhož souboru a `namespace` stojí na `<mapper>`. Vyříznutý `<select>` tedy není platný vstup — aby platný byl, musel by ho někdo doplnit o kontext, a to je čtení mapperu, tedy přesně ta práce, kvůli které nástroj existuje. Vedle toho jde varianta proti dvěma vyřčeným větám: jednotkou je obsah jednoho souboru ([`use-cases.md`](../use-cases.md)) a rozhraní má vzít, co uživatel má (F14); a proti S7, protože k použití převodníku MyBatisu by bylo potřeba umět MyBatis skládat. Nakonec by problém ani nezmizel, jen by se přestěhoval: naším klientem je naše vlastní rozhraní, takže rozřezávací kód by vznikl tak jako tak — v JavaScriptu, mimo dosah testovací sady a mimo záznamy podle 066, které by pak ukazovaly na fragmenty vyrobené klientem, ne na soubor, který má uživatel v ruce.

### 2 — Role vstoupí do slovníku typů obsahu

Přibude hodnota, která znamená „obojí" (třeba `XmlMapper`), případně měkčí forma: vedle `IsQuery()` druhý predikát `CarriesQuery()` nad dosavadními hodnotami. Orchestrace pak podle výčtu pozná, kterou jednotku pustit oběma průchody.

Zamítáme. Rozhodnutí 025 vyhradilo slovník **jazyku** a to vyhrazení je důvod, proč výčet neroste s wrappery. Dvojakost ale vlastnost jazyka není: tentýž XML dokument je dvojaký pod MyBatisem a čistě mapovací pod NHibernate i pod oběma implementacemi JPA. Role je fakt o zdrojovém frameworku, ne o jazyce, a zapsat ji do výčtu znamená vrátit tam framework oklikou — `JavaEntity` by se musela rozdělit na dvě hodnoty v den, kdy JPA parser začne číst `@NamedQuery`. Klient by navíc musel vědět, kterou z nich poslat, tedy znát pravidla čtení zdrojového frameworku; to je varianta 1 v převleku. Druhý predikát na tom nic nemění a přidává vlastní vadu: `JavaEntity` by „nesla dotaz" pro každý zdrojový framework, takže každá hibernátovská entitní jednotka by vstoupila do dotazového průchodu, nenašla parser a vysloužila si `Failure` o dotazu, který nikdo nenapsal.

### 3 — Jeden dotazový builder drží víc dotazů

`AbstractQueryBuilder` dostane `BeginQuery()` po vzoru `BeginEntity()` a `Build()` vydá artefakty všech dotazů, které do něj parser nasypal. Rozhraní parserů se nemění.

Zamítáme; souměrnost s entitní stranou je tu jen zdánlivá. Entitní builder drží víc entit proto, že se entity **slučují** napříč jednotkami podle priority zdrojů (rozhodnutí [017](017-source-precedence-for-mapping-facts.md) a [036](036-primary-key-under-source-precedence.md)) — najdi-nebo-založ je celý smysl té kolekce. Dotazy se neslučují nikdy; každý je samostatný a o sousedovi nic vědět nepotřebuje. Za tu neexistující výhodu by se platilo tím, že mezi dotazy je nutné vynulovat `QueryClauses`, `QueryArtifact`, zásobník značek poddotazů i příznak `refused` — ve sdílené abstraktní třídě s šesti potomky, kde zapomenuté vynulování **mlčí**: druhý dotaz by zdědil `WHERE` prvního nebo jeho odmítnutí a vydal by artefakt, který nikdo nenapsal. Čerstvý builder na každý dotaz tuhle třídu vad nemá vůbec.

### 4 — Jednotku dostanou oba průchody a o přijetí rozhodne `CanParse`

## Rozhodnutí

**Volíme variantu 4. Jednotka se nedělí a nedostává novou hodnotu typu obsahu; orchestrace ji nabídne oběma průchodům a každý si vezme to, co jeho parsery přijmou.**

**Orchestrace přestává jednotky dělit a začíná se ptát.** Místo rozdělení seznamu podle `IsQuery()` projde každý průchod všechny neprázdné jednotky a čte ty, na které se některý jeho parser přihlásí přes `CanParse`. Pořadí průchodů ani fáze doplnění mezi nimi se nemění. U dosavadních pěti frameworků se tím nemění nic pozorovatelného — `CSharpEntity` si nárokuje jen entitní parser, `SqlQuery` jen dotazový —, u MyBatisu se `XML` mapper přihlásí oběma a přečte se dvakrát: jednou kvůli `<resultMap>`, podruhé kvůli `<select>`.

**Dvojakost je tvrzení dvojice parserů zdrojového frameworku, ne hodnota ve slovníku a ne volba klienta.** Je to pravidlo rozhodnutí 025 („parser si typ obsahu nárokuje uvnitř zdrojového frameworku") dotažené o patro dál: hodnota jmenuje jazyk, framework si jmenuje roli. Důsledek je přesně ten, kvůli kterému variantu 2 zamítáme — až JPA parser začne číst `@NamedQuery`, bude ta změna celá uvnitř wrapperu (dotazový parser si nárokuje `JavaEntity` a `XML`), bez zásahu do orchestrace a bez nové hodnoty výčtu. Invariant S1 tím platí doslova i pro framework, jehož mapování a dotazy bydlí v jednom souboru.

**Dvojí čtení je čtení dvou různých částí, ne dvojí parsování téhož.** Každá polovina dokumentu má svého parsera a svůj cíl: mapovací polovina plní sdílený entitní builder, dotazová plní buildery dotazů. Sdílet mezi nimi stav nelze a nemá se: mezi průchody stojí fáze doplnění z katalogu, takže dotaz čtený spolu s mapováním by se překládal proti mapám, do kterých katalog ještě nepromluvil. Cena je rozparsování téhož XML dvakrát a je to cena vědomá — jediné čtení by znamenalo buď dotazy překládané proti neúplným mapám, nebo entitní parser, který zná dotazový builder cíle, tedy porušení S1.

**Dotazový parser dostává továrnu builderů a `Parse` vrací buildery, které naplnil — jeden na každý přečtený dotaz.** Je to táž úprava, jakou na entitní straně udělalo rozhodnutí [066](066-records-attributed-to-the-input-unit.md): parser vysloví, co z jednotky vzešlo, místo aby to orchestrace odhadovala z vedlejších účinků. Prázdná kolekce znamená „žádný dotaz z téhle jednotky nevzešel" a **sama o sobě chyba není** — mapper, ve kterém jsou jen `<resultMap>`y, je legitimní vstup. Builder si parser vyrobit nesmí, protože je to komponenta zdrojového frameworku a builder patří cíli (S1); továrnu proto dodává orchestrace, která na každém vydaném builderu zároveň nastaví `EntityMaps`. Návratová hodnota je povinná a bez výchozího tvaru, ze stejného důvodu jako u rozhodnutí [047](047-content-type-reaches-the-query-parser.md) a 066: výchozí tvar by byl odhad ve zdrobnělé podobě.

**Záznam o jalové jednotce je nově výrok o obou průchodech.** Pravidlo 066 zůstává — neprázdná jednotka, ze které nic nevzešlo, je `Failure` —, jen se „nic" počítá napříč oběma stranami: jednotka je jalová, když z ní nevzešla ani entitní mapa, ani dotaz. Ze stejného důvodu se slučuje i záznam o jednotce, kterou nikdo nenárokoval: dnes ho píšou obě větve zvlášť, nově ho píše jedna kontrola nad oběma, aby dvojaká jednotka nedostala dva záznamy o téže události. Záznam „cíl nemá dotazový builder" se píše dál, ale jen tam, kde se na jednotku přihlásil dotazový parser — jinak by ho dostala každá entitní jednotka při převodu do cíle bez dotazového builderu.

**Dotaz smí nést jméno ze zdroje a záznamy i vygenerovaná metoda ho nesou dál.** `<select id="findById">`, `@NamedQuery(name = …)` i `<query name=…>` dotaz pojmenovávají a všechny tři formy jméno **vyžadují**; holá dotazová jednotka (`SqlQuery`, `HqlQuery`, `JpqlQuery`, LINQ) ho nemá a nese právě jeden dotaz. Odtud pravidlo, které drží bez výjimky: **jednotka s víc než jedním dotazem je právě ta jednotka, jejíž dotazy zdroj pojmenoval**, takže pevný název `Query` se sám se sebou nikdy nesrazí. Kde jméno je, vypíše ho builder místo `Query` — převod na legální identifikátor cílového jazyka je jeho práce jako u každého jiného názvu — a `ConversionRecord` dostává nepovinné pole `Query` se jménem tak, jak ho napsal zdroj. Bez něj by tři neúspěšné `<select>`y jednoho mapperu vydaly tři záznamy nerozlišitelné jinak než pořadím.

**Hodnota `XML` zůstává jedna a opravuje se, co o sobě tvrdí.** Tři dialekty na ní (hbm.xml, `orm.xml` obou implementací JPA, mapper MyBatisu) jsou důsledek pravidla 025, ne jeho porušení: hodnota jmenuje jazyk a zdrojový framework říká, který dialekt to je a jakou má roli. Rozdělit ji na hodnoty podle dialektu je přesně to, co 025 zamítlo, a přejmenovat ji na `XmlMapping` by bylo nepravdivé v den, kdy dorazí MyBatis. Že jako jediná hodnota nejmenuje roli, není nesoulad se `SqlQuery` ani s `CSharpEntity` — role u ní není vlastnost jazyka. Mění se tedy popis v modelu a popisek v rozhraní, který dnes slibuje mapování; jméno konkrétního dokumentu už dnes nese `RequiredContent` po frameworcích („XML Mapping" u NHibernate, „orm.xml Mapping" u JPA), takže MyBatis tam přibude jako „XML Mapper" a klient nic nového vědět nemusí.

## Důsledky

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR navenek a MAJOR uvnitř řešení.** Do odpovědi přibývá nepovinné pole `query` u záznamu a žádné nemizí; požadavek se nemění vůbec, protože dvojakost klient nedeklaruje. Uvnitř se mění veřejné rozhraní `IQueryParser` — konstrukce s továrnou místo jednoho builderu a návratová hodnota `Parse` — a s ním jeho implementace (`DapperSqlQueryParser`, `LinqQueryParser` se dvěma potomky, HQL parser NHibernate a JPQL parser sdílené JPA vrstvy), z nichž každá si vyžádá právě jeden builder a právě jeden vrátí.

**`IsQuery()` zůstává, ale přestává orchestraci směrovat.** Je to dál výrok o jazyce a jako takový ho používá `AdvisorRunCoordinator`, který si vstupy dělí na entitní a dotazové kvůli měření, a kontraktní test rozhraní. Dvojaká jednotka se tam započítá jako entitní; Advisor pracuje jen s Dapperem a EF Core a jako celek stojí mimo záruky (vyňatá oblast 1, [`architecture.md`](../architecture.md) §9), takže tohle zúžení nikomu nic neslibuje a nic neruší.

**Chování dosavadních pěti frameworků se nemění.** Devět .NET směrů ani dvacet pět směrů s javovou stranou nemá jednotku, kterou by si nárokovaly obě strany, takže artefakty i záznamy zůstávají stejné — což je zároveň to, čím se změna ověří: matice směrů musí projít beze změny očekávaných výstupů.

**`@NamedQuery` a `<query>` v `hbm.xml` přestávají být nemožné a stávají se volbou wrapperu.** Tohle rozhodnutí je nečte a nezavádí — u JPA i u NHibernate zůstává dosavadní `Loss` v platnosti —, ale odstraňuje jedinou překážku, která jejich čtení bránila z orchestrace. Kdo je začne číst, přidá nárok na `XML`, respektive na `JavaEntity`, ve svém dotazovém parseru a víc nic.

**Co tohle rozhodnutí neřeší.** Jak se `<resultMap>` mapuje do mezireprezentace, co udělá dynamické SQL a zástupné symboly `#{}`/`${}` a jak se čte SQL mapperu — to je věc rozhodnutí k F8 a položky o sdíleném čtení T-SQL; operand parametru zůstává otevřený a dotaz s parametrem se dál odmítá záznamem (rozhodnutí [070](070-a-parser-refuses-what-would-change-the-row-set.md)). Neřeší ani párování výstupního artefaktu se vstupní jednotkou: pojmenovaný dotaz sice svůj artefakt odliší jménem metody, ale jméno jednotky výstup dál nenese a odložená položka rozhraní platí.

**Implementace se neodděluje od prvního čtenáře.** Změna orchestrace bez frameworku, který dvojakou jednotku skutečně pošle, by byla infrastruktura bez testu; proto se zapisuje jako práce svázaná s prvním čtenářem dvojaké jednotky a ti jsou tři — `<query>` v `hbm.xml` u NHibernate (obě poloviny existují, čte se HQL parserem z rozhodnutí [062](062-hql-read-by-a-hand-written-parser.md)), `@NamedQuery` a `<named-query>` u obou implementací JPA, a mapper MyBatisu podle F8. Nejlevnější je první z nich a má i metodickou výhodu: ověří orchestraci proti frameworku, který umíme testovat v xUnit, dřív než na ní bude stát javový wrapper.

**Testy.** Že jednotku, na kterou se přihlásí obě strany, přečtou obě a vydá mapovací fakty i dotazy; že jednotka, na kterou se nepřihlásí nikdo, vydá právě jeden záznam, ne dva; že dvojaká jednotka bez dotazů jalová není; že jednotka se dvěma pojmenovanými dotazy vydá dva artefakty s různě pojmenovanou metodou a záznamy z nich dotaz jmenují; a že žádný z dosavadních směrů nezmění ani artefakt, ani záznam.
