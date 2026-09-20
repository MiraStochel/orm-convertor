# 084 — MyBatis wrapper nad sdíleným čtením T-SQL

Datum: 2026-09-20
Stav: revidováno
Požadavky: F6, F8, F10, F11, F12, S1, S2
Podklad: rozhodnutí [001](001-entity-reference-by-name.md), [004](004-unexpressible-facts-as-warnings.md), [009](009-target-framework-descriptor.md), [010](010-diagnostics-as-returned-data.md), [011](011-key-generation-strategy-vocabulary.md), [013](013-target-framework-versions.md), [014](014-language-type-model.md), [015](015-mapping-fact-completion-from-the-catalog.md), [017](017-source-precedence-for-mapping-facts.md), [019](019-neutral-database-type-vocabulary.md), [022](022-native-query-syntax-in-builders.md), [025](025-query-language-as-content-type.md), [026](026-home-of-shared-query-reading.md), [028](028-assembly-name-is-not-ours-to-invent.md), [035](035-nhibernate-collections-declared-by-interface.md), [037](037-enforced-member-binding-held-by-the-test.md), [040](040-boundary-of-the-handed-over-artifact.md), [046](046-xml-mapping-written-through-an-element-writer.md), [048](048-a-fact-with-no-place-in-the-model-is-a-loss.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [063](063-stated-keylessness-as-a-carried-fact.md), [066](066-records-attributed-to-the-input-unit.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [068](068-source-framework-precedence-orders-the-reading.md), [069](069-major-marks-a-milestone-not-a-break.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [077](077-hibernate-wrapper-over-the-shared-jpa-layer.md), [078](078-java-suite-as-a-client-of-a-running-instance.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [082](082-t-sql-read-and-written-by-a-shared-project.md) a [083](083-parameter-as-the-fifth-operand-shape.md); JSS §4.3, §5.3 a §6.1, dotazová část Table 2; [tutoriál k MyBatisu](../analysis/tutorials/mybatis-getting-started.md), kroky 4, 5, 6, 7 a 9, sekce *Anotace a XML se nevrství* a *Co z toho plyne pro převodník*; [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §3–§7, §15 a §20; MyBatis 3.5.19; otevřená položka „MyBatis wrapper nad sdíleným čtením T-SQL"

## Kontext

MyBatis je třetí a poslední javový wrapper (F8) a jediný z šestice, který nestojí na žádné sdílené vrstvě frameworku. Hibernate a EclipseLink jsou dvě implementace téže specifikace, takže druhý z nich stál pět souborů (rozhodnutí 080); MyBatis není implementací ničeho. Sdílí s ostatními jen dvě věci — jazyk entity, tedy `JavaEntityParsing` (rozhodnutí 076), a jazyk dotazu, tedy `TransactSql` (rozhodnutí 082) —, a obojí je vrstva **jazyka**, ne frameworku.

Právě proto je tohle rozhodnutí delší než 080 a ne kratší: tenkost druhého wrapperu byla tvrzením o JPA vrstvě, ne o javové straně vůbec.

**Pět věcí je už rozhodnutých a jen se aplikuje.** Pořadí čtení mezi anotacemi a XML mapperem včetně toho, že druhé shodné `id` je `Failure`, a ne `Conflict` (rozhodnutí 068 — doloženo během, ne dokumentací: MyBatis 3.5.19 odmítne model už při stavbě továrny). Materializace pojmenovací konvence mapperu s doplněním z katalogu (rozhodnutí 067 a F6, protože MyBatis je vedle Dapperu druhý framework s neúplným mapováním). Jednotka, která je mapováním i dotazem zároveň (rozhodnutí 081, a mapper MyBatisu je její nejčistší případ — `<resultMap>` a `<select>` jsou sourozenci v jednom dokumentu). Čtení i zápis SQL sdíleným projektem (rozhodnutí 082). A parametr jako pátý tvar operandu podmínky (rozhodnutí 083, podle kterého je `#{id}` pojmenovaný parametr a metoda mapperu ho nese s `@Param`).

**Vázané je rozhodnutí přitom trojím způsobem a stojí za to to říct, protože to zužuje prostor voleb.**

- **Mezireprezentace je pivot a je hotová.** `Relation` visí na `EntityMap` a entity se odkazují jménem (001), klíč je uspořádaný seznam částí (011), typ je neutrální na jazykové i databázové straně (014, 019), podmínky jsou strom (§4.4 architektury) a hodnota dodaná volajícím je operand parametru (083). Nic z toho se kvůli MyBatisu neposouvá — a je to poprvé, kdy do modelu vstupuje framework, který **mapování nemá vázané na třídu**, takže je to zkouška pivotu, ne jeho rozšíření.
- **Kritérium 067 rozhoduje, co parser materializuje**, a u MyBatisu je ostřejší než kdekoli jinde, protože MyBatis o databázi netvrdí skoro nic: tabulka je jen v řetězcích SQL, délka, nullabilita a unikátní omezení jedině v DDL, které píše autor (§20 srovnání). Co parser nesmí materializovat, doplní katalog — a to je F6, která MyBatis jmenuje.
- **Ověření je rozdělené rozhodnutím 076.** První stupeň, tvar javového a XML textu, je v xUnit; druhý až čtvrtý — `javac`, stavba `SqlSessionFactory` nad generovanými artefakty a běh proti SQL Serveru — v javové sadě, která má od rozhodnutí 078 generované artefakty z běžící instance a od rozhodnutí 080 dva cíle. MyBatis je třetí.

**Ze čtyř doložených skutečností rozhodnutí počítá jako s daty**, a všechny jsou z běhu v tutoriálu, ne z dokumentace: `<id>` ve `<resultMap>` je příznak identity pro skládání vnořeného výsledku, ne primární klíč, a tabulka žádný klíč mít nemusí; `<collection>` nese tvar výsledku, ne cizí klíč, a vlastnictví vztahu MyBatis nezná vůbec; týž `<select>` dal `getBoundSql` **čtyři různé příkazy** pro čtyři sady parametrů, takže dotazová jednotka není dotaz, ale rodina; a `#{}` se stává vázaným `?`, kdežto `${}` textovou substitucí, která může nést jméno tabulky i celou klauzuli.

Nerozhodnuté zůstává pětero, a otevřená položka to pětero jmenuje: rozsah `resultMap` a jeho vztah k mapovací mezireprezentaci, vnořené mapování versus ruční SQL join, osud dynamických značek při čtení i při zápisu spolu s tím, čím se `#{}` nahradí před předáním gramatice, tvar vydávaného artefaktu a to, co z profilu implementace patří do deskriptoru po vzoru obou JPA profilů.

## Zvažované varianty

Otázek je pět, každou z nich klade otevřená položka a **čtyři z nich měly skutečné varianty**; u páté — profilu implementace — nevzešla odpověď z porovnání variant, nýbrž z toho, proč profil vůbec existuje.

### Dynamický příkaz: rodina, jeden její člen, nebo mez vedená po OGNL

`<select id="findAuthors">` s dvěma `<if>` je podle kroku 9 tutoriálu čtyři příkazy, a která z nich se pošle do databáze, rozhodnou až hodnoty parametrů. Nabízely se čtyři cesty.

**Vyhodnotit jednu sadu parametrů** — typicky „všechny podmínky platí" — je nejjednodušší a zamítáme to bez váhání. Jeden člen rodinu nereprezentuje: `[oba parametry null]` a `[oba parametry]` se liší celou klauzulí `WHERE`, takže překlad postavený na jediné sadě by tiše vydal dotaz s **jinou množinou řádků**, než jakou zdroj napsal. To je přesně to, co zakazuje pravidlo 053, a tiché je to dvojnásob, protože výsledný artefakt vypadá bezvadně.

**Zanést rodinu do mezireprezentace** — podmínka opatřená stráží nad přítomností parametru — je jediná cesta, která by rodinu nesla celou. Zamítáme, a ne kvůli ceně implementace. Fakt, který **žádný z pěti ostatních cílů neumí vyjádřit**, je podle rozhodnutí 004 a pravidla Q14 hlášená ztráta, ne nový pojem modelu: nést ho znamená rozšířit `IQueryVisitor` a každý builder o tvar, který pět ze šesti cílů vždycky zahodí. Dynamické SQL nemá v šestici jediný protějšek (§15 srovnání) a rozhodnutí 083 zavedlo parametr právě proto, že protějšek **má** ve všech šesti.

**Vydat jeden artefakt na každého člena rodiny** je poctivé a technicky možné — dotazový parser vrací od rozhodnutí 081 builderů tolik, kolik dotazů přečetl, takže by jich vrátil 2ⁿ. Zamítáme třemi důvody. Počet je exponenciální v počtu značek a nikde neomezený. Jména těch členů zdroj neřekl, takže by je vymyslel parser — přesně to, co zakazuje rozhodnutí 028. A hlavně: jeden `<select>` na vstupu by dal čtyři `<select>` na výstupu, takže převod MyBatis → MyBatis by přestal být převodem a S2 by nemělo co tvrdit.

**Vést mez po tom, co k vyhodnocení potřebuje OGNL**, je cesta, kterou volíme, a je to táž úvaha, jakou vedlo rozhodnutí 082 o gramatice: cizí jazyk buď čteme jeho vlastním nástrojem, nebo ho nečteme, ale nepíšeme si k němu vlastní poloviční vyhodnocovač. **OGNL nevyhodnocujeme vůbec.** Značka, jejíž rozvinutí je čistě textové — `<include>`/`<sql>`, `<where>`, `<set>`, `<trim>`, `extends` na `<resultMap>` —, se rozvine a výsledek je týž pro každou sadu parametrů; značka, jejíž rozvinutí rozhoduje výraz OGNL — `<if>`, `<choose>`/`<when>`/`<otherwise>`, `<bind>` —, je `Failure`, který značku jmenuje. Jediná výjimka je `<foreach>`, a není to výjimka z pravidla, nýbrž jeho důsledek: `<foreach>` v kanonickém tvaru nad `IN` nepotřebuje OGNL k ničemu — jeho `collection` je jméno parametru — a mezireprezentace pro něj **místo má**, totiž kolekční parametr rozhodnutí 083, kam ho rozhodnutí 074 poslalo jmenovitě.

### `<resultMap>`: mapování třídy, mapování s klíčem a cizím klíčem, nebo nic

`<resultMap>` je to, čím se MyBatis liší od Dapperu nejvíc (§4 srovnání): pojmenovatelné deklarativní mapování, které nese dvojice sloupec–vlastnost i strukturu vnoření. Otázka zní, kolik z něj je mapovací fakt.

**Nečíst ho a vzít MyBatis jako Dapper**, tedy všechno z katalogu, zamítáme: dvojice sloupec–vlastnost jsou vyslovený fakt zdroje, a fakt, který zdroj vyslovil a my ho zahodíme, je podle rozhodnutí 048 ztráta, ne mlčení. Byla by to navíc ztráta zbytečná — F6 by u MyBatisu musela doplnit i to, co v artefaktu stojí napsané.

**Přečíst ho celý, včetně klíče z `<id>` a cizího klíče z `ON` klauzule příkazu**, zamítáme také, a je to ta zajímavější půlka. `<id column="AuthorId">` neříká „tohle je primární klíč tabulky", nýbrž „podle tohohle sloupce se pozná, že dva řádky patří témuž objektu" — tabulka klíč mít nemusí a MyBatis se na něj nikdy nezeptá (§5 srovnání, krok 4 tutoriálu). Materializovaný klíč by přitom neprošel prvním testem rozhodnutí 067 (framework to jako pravidlo nedokumentuje) a měl by horší následek než mezera: **materializovaný fakt katalog nepřepíše**, jen se s ním porovná, takže `<id>` nad neklíčovým sloupcem by vyrobilo `Conflict` proti skutečnému klíči ze schématu místo tichého `Supplied`. Cizí klíč z `ON` je ještě slabší: spojovací podmínka může být libovolná — `ON x.AuthorId = a.AuthorId AND x.Active = 1`, spojení přes neklíčový sloupec, nebo vůbec žádná, když se vnořený výsledek plní vnořeným `<select>`em —, takže by nástroj tvrdil o schématu něco, co v něm být nemusí.

**Přečíst z něj to, co je mapováním, a zbytek nechat katalogu**, je cesta, kterou volíme, a vychází z ní dělba, kterou je možné vyslovit jednou větou: **`<resultMap>` je mapování sloupců na vlastnosti, `<collection>`/`<association>` je navigace bez cizího klíče, a `<id>` je dvojice sloupec–vlastnost jako každá jiná.** Klíč a cizí klíč doplní katalog (F6, rozhodnutí 015), který obojí umí — cizí klíč navíc umí doplnit **do existujícího vztahu**, jehož sloupce nikdo neřekl, což je přesně tenhle případ.

### Dva `<resultMap>` téže třídy, které si odporují

MyBatis dovolí nad `Author` dva `<resultMap>`, z nichž jeden mapuje `name` na sloupec `Name` a druhý na `AuthorName`; každý příkaz si vybírá svůj. Mezireprezentace má na třídu jedno mapování (001), takže se oba dostanou do jednoho `EntityMap`u.

**Odmítnout takový vstup jako `Failure`** by bylo nejpřísnější a zamítáme to, protože by nástroj vymýšlel chybu, kterou zdroj nemá: MyBatis oba mapy přijme a projekt s nimi běží. Kritérium rozhodnutí 068 je v tomhle jednoznačné — kolize dopadá tak, jak dopadá ve frameworku samém —, a právě proto je souběh anotace a XML `Failure` (framework model odmítne postavit), kdežto tohle jím není.

**Založit z každého `<resultMap>` vlastní entitu** by rozpor odstranilo tím, že by ho zdvojilo. Zamítáme: entita je v modelu identifikovaná jménem (001), dvě entity téhož jména nejsou zapsatelné a dvě různá jména by si nástroj musel vymyslet.

Volíme **mechanismus rozhodnutí 017 beze změny**: dřív přečtená dvojice platí, pozdější doplní jen prázdné místo a rozdíl je záznam `Conflict` se zachovanou první hodnotou. Pořadím je pořadí v dokumentu, tedy deterministicky (S2). Není to nový nástroj, je to týž nástroj o patro níž — dosud řešil rozpor **mezi artefakty**, tady řeší rozpor **mezi dvěma prvky jednoho artefaktu** —, a je to odpověď, kterou si tutoriál vyžádal jako vstup rozhodnutí k F8.

### Tvar výstupu: XML mapper, nebo anotace

MyBatis umí mapování i příkaz vyslovit dvakrát: v XML mapperu a v anotacích nad rozhraním. Vydávat **obojí** nelze — stejné `id` v obou je chyba při stavbě továrny (68) —, takže je to volba.

**Anotace** by držely dotaz v Javě, blíž Dapperu, a ušetřily by jeden artefakt. Zamítáme je jako **výstupní** tvar ze tří důvodů: XML mapper je primární forma frameworku (§4 srovnání), `<resultMap>` s vnořením se v anotacích vyjadřuje hůř, a dynamickou značku by anotace potřebovala obalit do `<script>`, což je XML uvnitř řetězce uvnitř Javy. Navíc `XmlEmitter` v `Common.Xml` vznikl rozhodnutím 046 s mapperem MyBatisu mezi vyjmenovanými budoucími konzumenty, takže záruka správně utvořeného dokumentu je hotová.

Volíme **XML mapper na výstupu a obě formy na vstupu**. Asymetrie je vědomá a má precedens: rozhodnutí 013 označilo cestu `hbm.xml` → anotace za jedinou, protože zdrojů je víc než tvarů, které chceme vydávat. Požadavek F8 přitom obě formy na vstupu jmenuje doslova („XML nebo anotované mapování").

### Profil implementace

Pátou otázku klade položka po vzoru obou JPA profilů a odpověď na ni je **žádný profil**; zdůvodnění je v Rozhodnutí níž, protože nevzniklo porovnáním variant, nýbrž z toho, proč profily vůbec existují.

## Rozhodnutí

**MyBatis wrapper vzniká jako projekt `MyBatisWrappers` nad dvěma jazykovými vrstvami — `JavaEntityParsing` pro Javu a `TransactSql` pro SQL — a nad žádnou vrstvou frameworku. `ORMEnum` dostává hodnotu `MyBatis`. Slovník typů obsahu se nemění ani o hodnotu: mapper je `XML`, doménová třída `JavaEntity`, rozhraní mapperu `JavaQuery`. `AbstractWrappers`, `Common`, `Model` ani orchestrace se nemění; `TransactSql` dostává jediný nepovinný argument konstruktoru a `JavaEntityParsing` čtení rozhraní.**

### Jednotky, pořadí čtení a dvojakost

Wrapper si nárokuje tři jednotky, a **dvě ze tří jsou mapováním i dotazem zároveň** — víc než u kteréhokoli dosavadního frameworku, takže MyBatis je nejčistším dokladem rozhodnutí 081:

| Jednotka | Typ obsahu | Na entitním průchodu | Na dotazovém průchodu |
|---|---|---|---|
| doménová třída | `JavaEntity` | jazyková fakta vlastností | — |
| rozhraní mapperu | `JavaQuery` | `@Results`/`@Result`/`@One`/`@Many`, podpisy metod | `@Select` |
| XML mapper | `XML` | `<resultMap>` | `<select>` |

Pořadí je **výchozí pořadí rozhodnutí 017**, protože MyBatis mezi anotacemi a XML žádnou precedenci nedokumentuje — dokumentuje jejich souběh jako chybu (068): vstupní text frameworku (1a) před pomocnými mapovacími artefakty (1b), tedy doménová třída, rozhraní, mapper. Že se pořadí mezi dvěma mapovacími formami neprojeví na faktu, o kterém obě mluví, je zaručeno jinak: **týž `<namespace, id>` vyslovený v anotaci i v XML je `Failure`**, který obě místa jmenuje, po vzoru rozhodnutí 063. Souběh není rozpor mezi dvěma tvrzeními, mezi nimiž lze rozhodnout precedencí; je to vstup, se kterým zdrojový projekt nenastartuje.

Souběh se pozná napříč jednotkami, takže wrapper má **sdílený čtecí kontext** vyráběný továrnou — tvar, který zavedlo rozhodnutí 077 pro dvojici parserů JPA. Kontext nese tři věci: `<namespace, id>` už přečtených příkazů, fragmenty `<sql>` a **podpisy metod rozhraní**. Entitní průchod běží celý před dotazovým, takže podpisy jsou v kontextu dřív, než je dotazový parser potřebuje.

`mybatis-config.xml` **nečteme ani nevydáváme**. Nevydáváme ho podle rozhodnutí 040, které konfiguraci ekosystému jmenuje mezi fakty konzumentského projektu vedle `persistence.xml`; a nečteme ho proto, že obě věci, kterými by přispěl, umíme jinak nebo nepotřebujeme. Alias `type="Author"` z `<typeAliases>` rozřešíme **jménem** proti entitám převodu (001) a balík vezmeme z doménové třídy, přesně tak, jak rozhodnutí 077 řeší třídu klíče v jiné jednotce; plně kvalifikovaný `type` nese obojí sám. A `mapUnderscoreToCamelCase` se uplatní jedině při **automatickém** mapování, tedy tam, kde `<resultMap>` není — a to je případ, který tohle rozhodnutí do mapování nečte vůbec (viz níž). Rozhodnutí 067 obě větve předvídalo a tuhle popsalo doslova: „bez konfigurace platí identita, mezera nikam neputuje a zbytek doplní katalog". Vedlejší přínos stojí za vyslovení: konfigurace nese zdroj dat, takže jednotka, kterou bychom po uživateli chtěli, by běžně nesla přihlašovací údaje (S4).

### Rozsah `resultMap`

Čtení je zároveň převodní tabulkou mezireprezentace → MyBatis, tedy pravidly zápisu čtenými opačně:

| Fakt mezireprezentace | Čtení (MyBatis → IR) | Zápis (IR → MyBatis) |
|---|---|---|
| entita, jmenný prostor | `type`/`ofType` na `<resultMap>`, `@Results` u metody; balík z doménové třídy | `type` plně kvalifikovaným jménem, balík z `Namespace` beze změny |
| tabulka, schéma | **nečte se** — MyBatis pojem tabulky nemá, jméno je jen uvnitř SQL | **nevypisuje se** (`NotExpressible`, mechanický záznam podle Q14) |
| sloupec | `column` na `<id>`, `<result>` i `@Result` | `<result column property>`; u částí klíče `<id column property>` |
| databázový typ | `jdbcType` jako rodina s facetami (rozhodnutí 019; `NVARCHAR` je `VarChar` + unicode) | `jdbcType` z rodiny a facet; doslovný typ zdroje se nevypisuje, protože `columnDefinition` tu obdobu nemá |
| délka, přesnost, měřítko, nullabilita | **nečte se** — v mapperu nemají kam (§20 srovnání) | **nevypisuje se** (`NotExpressible`) |
| jazykový typ vlastnosti | `javaType` na `<result>`, jinak typ pole doménové třídy | typ pole doménové třídy |
| klíč a jeho strategie | **nečte se** — `<id>` je příznak identity, `useGeneratedKeys` je vlastnost `<insert>`, který nečteme | `<id>` v pořadí částí klíče; strategie `NotExpressible` |
| vztah | `<collection>`/`<association>` a `@Many`/`@One` jako navigace bez sloupců (viz níž) | **nevypisuje se** (`NotExpressible`) |
| nepersistovaná vlastnost | vlastnost bez `<result>` — jen při `autoMapping="false"` | vynecháním `<result>` nad `<resultMap autoMapping="false">` |
| verze, unikátní omezení | **nečte se** — ruční `WHERE`, respektive DDL | **nevypisuje se** (`NotExpressible`) |

Co je mimo tabulku, není mlčení (048): `<discriminator>` je volba typu výsledku, pro kterou model nemá místo stejně jako pro `@Inheritance` (077), `<constructor>` mapuje sloupce na argumenty konstruktoru, `fetchType` a `@One(fetchType)` jsou strategie načítání, kterou mezireprezentace nenese (080), a `resultType`/`resultMap` na `<select>` je návratové mapování — všechno to je `Loss` s názvem prvku. **U posledního jmenovaného je to týž záznam a týž důvod jako u `<return>` v `<sql-query>` NHibernate** (rozhodnutí 082): výsledný typ odvozuje z tabulky každý cíl sám, takže jeho zahození nechává řádky, jak jsou.

**Klíč a jeho absence.** Entita bez klíče je u MyBatisu přirozený stav (§5 srovnání), takže z jeho nepřítomnosti nevydáváme záznam — konstantní záznam, který by nesl každý převod z MyBatisu bez výjimky, netvrdí nic (010, 028) a patří do dokumentace. Místo toho vydáváme záznam `Incompleteness` tam, kde zdroj `<id>` **vyslovil**: jmenuje označené sloupce a říká, že je MyBatis označuje jako identitu výsledku, ne jako klíč tabulky, takže klíč se doplní z katalogu (F6). Ten záznam konstantní není — jmenuje sloupce tohoto vstupu — a je pro uživatele návodem, co udělat (připojit katalog), což je přesně to, co F11 po diagnostice chce.

### Vnořené mapování a ruční join

**Vnořené mapování je mapováním dvou entit a navigací mezi nimi; spojovací podmínka příkazu je dotazem. Jedno se nikdy nečte jako druhé.**

`<collection property="books" ofType="Book">` s vlastními `<id>`/`<result>` uvnitř tedy dělá tři věci naráz: doplní mapování `Author.books`, **založí nebo doplní entitu `Book`** z vnořených dvojic — jeden `<resultMap>` smí nést mapování víc entit, což je v šestici nový tvar a model ho unese, protože entity se odkazují jménem (001) — a zapíše vztah. Vztah nese navigaci a kardinalitu, a **nenese sloupce**:

- **Kardinalita a role plynou z tvaru navigace**, ne z konvence MyBatisu: `<collection>` je 1:N a strana s kolekcí fyzický cizí klíč nedrží nikdy, takže je `Inverse`; `<association>` je N:1 a je `Owning`. Je to odvození z relačního modelu, ne z frameworku, a je to jediné odvození, které z kolekční navigace vůbec vést lze.
- **`ColumnPairs` zůstávají prázdné** a doplní je fáze doplnění z katalogu do existujícího vztahu (015). Bez katalogu vztah sloupce nemá a cíl si poradí tak, jak si radí dnes: JPA builder vypíše `@JoinColumn` s odvozeným názvem a záznamem `Convention` (077).
- **Vnořený `<select>` místo vnoření** (cesta N+1) se čte stejně: navigace vznikne, sloupce ne. Atribut `select` sám je `Loss`, protože říká, **jak** se kolekce načte, a to model nenese.

Spojovací podmínka `LEFT JOIN Books b ON b.AuthorId = a.AuthorId` se naproti tomu čte **jen jako `JoinInstruction`** sdíleným čtením T-SQL, tedy jako dotaz. Že požadavek F8 jmenuje „ručně psané SQL joiny", je tím splněné celé a na správném místě: ruční join je dotazový fakt, který nástroj přeloží do joinu cíle, ne mapovací fakt, ze kterého by se dal odvodit cizí klíč. Odvozovat ho z něj by znamenalo tvrdit o schématu něco, co v něm být nemusí — a je to tentýž důvod, pro který 082 zamítlo tichý překlad dialektu: artefakt by vypadal správně a tvrdil by víc, než zdroj řekl.

### Dynamické značky, `#{}` a `${}`

Wrapper předá sdílenému čtení **prostý T-SQL**, nebo dotaz odmítne záznamem — pravidlo, které 082 vyslovilo obecně a `<sql-query>` NHibernate už podle něj čte. Konkrétně ve čtyřech krocích, v tomhle pořadí:

1. **Statické značky se rozvinou.** `<include refid>` vloží tělo `<sql>` téhož dokumentu včetně `<property>` substitucí, `extends` na `<resultMap>` sloučí rodičovské dvojice, `<where>` a `<set>` doplní klíčové slovo a odstraní úvodní `AND`/`OR`, respektive koncovou čárku, `<trim>` udělá totéž podle svých atributů. Výsledek je pro každou sadu parametrů týž, takže je to rozvinutí, ne volba. `refid` mimo dokument je `Failure`, který ho jmenuje: jednotkou převodu je jeden artefakt a hledat fragment jinde by udělalo výsledek závislým na tom, co uživatel přiložil.
2. **Značka vedená OGNL je `Failure`**, který ji jmenuje: `<if>`, `<choose>`, `<when>`, `<otherwise>`, `<bind>`. Dotaz se nevydá — jeho rodinu bychom zastoupili jedním členem a ten má jinou množinu řádků (053).
3. **`<foreach>` je kolekční parametr.** V kanonickém tvaru — `collection` je jméno parametru, `item` se uvnitř použije jedinkrát jako `#{item}`, `open`/`close` jsou závorky a `separator` čárka — a **jedině v pozici pravého operandu `IN`**, kam ho rozhodnutí 083 taxativně umístilo, se celá značka nahradí jediným parametrem se zapnutým příznakem kolekčnosti. Každý jiný `<foreach>` je `Failure`, který řekne, čím se od kanonického tvaru liší.
4. **Zástupné symboly se nahradí.** `#{name}` se stává `@name`, tedy `VariableReference`, kterou sdílené čtení od rozhodnutí 083 čte jako pojmenovaný parametr `name` — substituce je proto triviální a **přesná**, protože obě strany mluví o vázané hodnotě. Atributy uvnitř se odloží stranou: `javaType` je vyslovený skalár parametru, ostatní (`jdbcType`, `typeHandler`, `mode`) jsou `Loss`. `#{user.name}` je cesta k vlastnosti parametrického objektu a je `Failure` podle 083. **`${}` parametrem není** a je `Failure`, který řekne, že zdroj substituuje text, ne hodnotu (082, 083).

**Jedna past je vidět jen odsud a zapisujeme ji jako pravidlo.** Po kroku 4 je `@name` k nerozeznání od `@name`, které v textu stálo **už předtím** — a to by byl parametr, který MyBatis nikdy neváže, přeložený jako parametr. Zdroj MyBatisu, jehož SQL obsahuje identifikátor uvozený `@` před substitucí, je proto `Failure`, který ten identifikátor jmenuje. Kontrola stojí před substitucí, protože po ní už ji udělat nelze.

**Vyslovený skalár parametru má v MyBatisu tři zdroje** a tenhle wrapper je prvním producentem větve, kterou rozhodnutí 083 popsalo a nikdo nenaplnil: `javaType` uvnitř `#{}`, `parameterType` na `<select>`, který jmenuje třídu a jehož vlastnost `#{name}` typuje, je-li ta třída entitou převodu, a **typ parametru metody rozhraní**, který je podle kroku 9 tutoriálu jediným místem, kde typ parametru v MyBatisu vůbec žije. Vyslovený skalár má přednost před odvozeným a rozdíl je `Conflict` (083). Dostane se do sdíleného čtení jediným nepovinným argumentem konstruktoru `SqlQueryReader`u — mapou jméno → vyslovené fakty o parametru, tedy skalár a příznak kolekčnosti (viz Historie). **Není to zásuvný bod gramatiky**, který 082 u T-SQL odmítlo jako zbytečný, nýbrž fakt o textu, který wrapper ze zdroje odloupl dřív, než ho gramatika uvidí; stojí proto v téže pozici jako hlásicí kanál.

Párování metody rozhraní s příkazem se dělá jménem: `@Param("x")`, u metody s jediným parametrem jeho název. Kde se nespáruje, **nevydáváme verdikt** a skalár odvodí brána šablony jako u ostatních zdrojů. Že by takový projekt v MyBatisu spadl na chybějící getter, je pravděpodobné, ale nezměřili jsme to, a nezměřený default není náš, abychom o něj opírali odmítnutí (080).

**Na straně zápisu žádná dynamická značka nevzniká, s jedinou výjimkou.** Mezireprezentace dynamičnost nenese, takže `<if>`, `<choose>`, `<trim>` ani `<set>` nemá builder z čeho vydat. `<where>` nevydává také, ačkoli by mohl: podmínkový strom je úplný a builder o něm ví, jestli je prázdný, takže prosté `WHERE` je přesné, kdežto `<where>` by nechalo tvar výsledku na tom, jak cíl ořezává spojky — a builder nespoléhá na default cíle (076). Výjimkou je `<foreach>`, který builder vydává pro kolekční parametr v kanonickém tvaru, tedy **přesně v tom, který parser čte**; jiná cesta ani není, MyBatis seznam za `#{}` nerozepíše. Round-trip se tím uzavírá na jediné značce, kterou model nese.

### Vydávaný artefakt

Jednotka vstupu je jeden artefakt, a tak i výstup: **dvojice na entitu a dvojice na dotaz**, nikdy projekt (040).

- **Entita:** doménová třída v Javě (`JavaEntity`) a dokument mapperu (`XML`) s prologem, DOCTYPE, kořenem `<mapper namespace>` a jediným `<resultMap autoMapping="false">`. Třída je prostý POJO — žádný import z frameworku, protože MyBatis žádný nežádá; pole, getter a setter na vlastnost podle jazykové osy rozhodnutí 077, kolekce s prázdným inicializátorem (035).
- **Dotaz:** deklarace metody rozhraní mapperu (`JavaQuery`) s `@Param` u každého parametru a dokument mapperu (`XML`) s jediným `<select id resultType>`. Metoda je fragment bez obalového rozhraní, stejně jako ji Dapper i oba JPA buildery vydávají bez obalové třídy (022).

**`<select>` a metoda se nepřekrývají:** SQL je jedině v XML a metoda nenese `@Select`. Je to doslovná aplikace nálezu 068 — kdybychom vydali obojí, byl by generovaný projekt přesně tím vstupem, který jsme si na straně čtení zakázali.

**`resultType`, ne `resultMap`.** Rozhodnutí 082 to určilo předem a důvod platí: výsledný typ neuvádí u MyBatisu ani u Dapperu dotaz sám, takže ho `AbstractSqlQueryBuilder` odvozuje z tabulky — z entity převodu, je-li nějaká nad tou tabulkou, jinak z názvu tabulky se záznamem `Convention`. `resultMap` by navíc odkazoval do druhého dokumentu, který v převodu jen dotazu není.

**`autoMapping="false"` je na každém vydaném `<resultMap>`** a je to víc než detail. Se zapnutým automatickým mapováním by sloupec, který jsme nepojmenovali, přesto naplnil vlastnost shodného jména, takže by význam artefaktu záležel na shodě názvů a na nastavení `mapUnderscoreToCamelCase` v konfiguraci, kterou nevidíme. Vypnutím se mapování stává **uzavřeným**: platí přesně to, co je vypsané. Je to rozhodnutí 076 („builder nespoléhá na default cíle") na jediném řádku a je to zároveň to, co dělá nepersistovanou vlastnost vyjádřitelnou — vynechání `<result>` je tvrzení jedině tehdy, když automatické mapování neběží.

**Jmenný prostor mapperu** je `<balík>.<Entita>Mapper` u entity a `<balík>.<NázevDotazu>Mapper` u dotazu, balík ze zdroje beze změny (040). Že jméno dokumentu vymýšlíme, je táž třída faktu jako název generované metody: je to identita **našeho** artefaktu, deterministická (S2), a protože vzniká u každého převodu do MyBatisu stejně, záznam nevydává a je popsaná v dokumentaci (010, 028). Dokument na dotaz má tím jmenný prostor jedinečný z konstrukce, takže nic nespoléhá na to, jak MyBatis slučuje dva dokumenty téhož jmenného prostoru — což jsme neměřili.

### Deskriptor bez profilu

**Profil implementace MyBatis nemá a nedostane ho.** Profil vznikl rozhodnutími 076 a 077 k jedinému účelu: rozlišit dvě implementace **jedné specifikace**, u nichž táž anotace znamená jinou věc, a 080 ho naplnilo právě takovými fakty (rozklad `AUTO`, velká písmena, nationalizační režim). MyBatis není implementací ničeho, takže není co od čeho odlišit, a verze v deskriptoru (013) plus obě tabulky podpory říkají všechno.

Čtyři nastavení, která by do profilu jinak putovala — `mapUnderscoreToCamelCase`, `autoMappingBehavior`, `callSettersOnNulls` a globální `useGeneratedKeys` —, jsou navíc **fakty konzumentské konfigurace** (040), a volíme k nim silnější odpověď než popis: **artefakt děláme na nich nezávislým.** `autoMapping="false"` a vypsaný `column` u každé vlastnosti berou prvním dvěma vliv úplně; `useGeneratedKeys` se týká `<insert>`, který nevydáváme; a `callSettersOnNulls` — nastavení, kvůli kterému `null` do primitivní vlastnosti tiše nechá nulu (krok 3 tutoriálu) — se nemá na čem projevit, protože jazyková osa rozhodnutí 077 vypisuje nullovatelnou vlastnost obalovým typem a primitiv jedině tam, kde zdroj tvrdí NOT NULL. Popsat default je slabší než nedat mu prostor.

Deskriptor sám nese verzi `3.5.19` (013) a dvě tabulky. **Podpora mapovacích faktů** je proti Dapperu kratší přesně o to, co má `<resultMap>` (§20 srovnání): `ColumnName`, `DatabaseType`, `PrimaryKey` a `TransientProperty` jsou `Expressible`, všechno ostatní `NotExpressible` — název tabulky i schématu, délka, přesnost, nullabilita, strategie klíče, sloupce cizího klíče, verzovací sloupec a unikátní omezení. `DatabaseType` je `Expressible` v tom rozsahu, v jakém ho `jdbcType` nese, tedy jako rodina s facetami; délka a přesnost vedle něj zůstávají `NotExpressible`, a proto se hlásí zvlášť. `PrimaryKey` je `Expressible`, protože `<id>` vypíše tytéž sloupce v témže pořadí — a asymetrii vyslovujeme: **zapsat klíč do `<id>` je přesné, přečíst klíč z `<id>` není**, protože MyBatis tímtéž prvkem označuje i identitu, která klíčem není. Round-trip MyBatis → MyBatis tak klíč beze katalogu neudrží, a je to vidět, ne tiché: nese to záznam popsaný výš. **Podpora dotazů** je táž jako u Dapperu — SQL vyjádří všechno, `QueryParameter` včetně.

**Vynucené členy: jediný, bezparametrický konstruktor**, podmínka *vždy*, vyslovený zakázaným markerem jako u JPA. Srovnání i tutoriál uvádějí, že MyBatis nevynucuje nic, a v tom smyslu, v jakém to říkají — žádná bázová třída, žádné rozhraní, žádná závislost domény na frameworku —, to platí a je to nejčistší případ pravidla „požadavky na entitu jsou omezení cílového frameworku, ne fakta o doméně". Výsledný objekt ale MyBatis vytváří přes `ObjectFactory`, jejíž výchozí implementace sahá po bezparametrickém konstruktoru, a náš builder žádný konstruktor nevypisuje, takže je to členem, který drží implicitně — a přesně takový člen zavedlo rozhodnutí 009 vyslovit místo toho, aby byl splněný náhodou. **Je to tvrzení, které xUnit ověřit neumí, a javová sada ho má potvrdit jako první**, v téže roli, v jaké potvrzovala dvě tvrzení 077 a dvě tvrzení 080.

### Rozhraní a vzorky

`ORMEnum.MyBatis = 60`, tři jednotky `/required-content` (doménová třída, rozhraní mapperu, XML mapper) s vlastními vzorky (`CustomerSampleMyBatis`) a řádek v mirroru výčtu ve frontendu; typy obsahu, přípony ani zvýrazňovač se nemění, protože jazyky jsou tytéž. Vzorek je týž `Customer` jako u ostatních pěti frameworků, takže se dá číst vedle hibernátovského jako doklad toho, co zdrojový text tvrdí a co ne — a u MyBatisu je to nejméně ze všech šesti. `ParserFactory` staví seznam ve výchozím pořadí s komentářem, který říká, že MyBatis precedenci mezi anotací a XML nedokumentuje, nýbrž souběh odmítá.

### Co tvrdí xUnit a co javová sada

Kritérium F8 — dvacet testů mapování, patnáct testů dotazů, z toho nejméně pět vícetabulkových — se dělí podle 076 stejně jako u F7 a F9. **xUnit tvrdí tvar a čtení**: každý řádek převodní tabulky výš má test čtení i test zápisu; `<id>` nezaloží klíč; dva `<resultMap>` téže třídy dají `Conflict` se zachovanou první hodnotou; týž `<namespace, id>` v anotaci i v XML dá `Failure`; statické značky se rozvinou a značka vedená OGNL dotaz odmítne; `<foreach>` v kanonickém tvaru dá kolekční parametr a mimo něj `Failure`; `#{}` se stane `@`, `${}` a `@` už přítomné v textu odmítnou dotaz; vyslovený skalár z podpisu metody přebije odvozený a rozdíl je `Conflict`; vnořené mapování založí druhou entitu a vztah bez sloupců, které doplní katalog; a **týž text SQL přečtený z Dapper jednotky, ze `<sql-query>` a z mapperu MyBatisu dá tutéž mezireprezentaci** — rozšíření důkazu, který si vyžádalo rozhodnutí 082, na třetího čtenáře. MyBatis k tomu vstupuje do matice `CrossFrameworkInputs`, která tím roste na **šestatřicet směrů**, z toho osmnáct napříč ekosystémy (F10), a do matice vynucených členů (037). **Javová sada tvrdí přijetí a běh** (2.–4. stupeň): `javac` nad doménovou třídou a rozhraním, stavba `SqlSessionFactory` nad generovaným mapperem a běh `<select>` proti SQL Serveru, ve třech zdrojových směrech, plus negativní polovina. Bootstrap je **programový**, jako u EclipseLinku a ze stejného důvodu: konfiguraci nevydáváme, takže sada si `Configuration` sestaví v kódu, mapper přidá jako zdroj a rozhraní zaregistruje sama.

Tvrzení, která xUnit ověřit neumí a javová sada je má potvrdit jako první, jsou tři: že bezparametrický konstruktor je opravdu vynucený, že `autoMapping="false"` mapování skutečně uzavře — tedy že sloupec bez `<result>` vlastnost nenaplní ani při shodě názvů —, a že `<foreach>` ve vydaném tvaru kolekci naváže.

## Důsledky

**F8 zůstává vyňaté, dokud javová sada nad MyBatisem neprojde v zafixovaném prostředí.** Je to táž opatrnost, s jakou do nároku vstupovalo F7 a po něm F9: wrapper existuje, kritérium F8 má v xUnit všechny tři počty, ale větu o přijetí frameworkem doloží teprve zelený job `java-test` nad SQL Serverem 2022. Do té chvíle je MyBatis v repozitáři a v rozhraní, jen na něj verze neslibuje spoleh (§9, oblast 6).

**F10 a F12 se dotýkají, ale neuzavírají.** Matice roste na šestatřicet směrů, tedy na tvar, který F10 žádá, a javová sada dostává třetí cíl, který F12 jmenuje; F12 ale žádá i šedesát javových testů s dvaceti integračními a F10 nejméně třicet cross-ecosystem překladů, takže obojí se posoudí až s hotovou sadou. Co se posouvá jistě, je vzorek: **od téhle chvíle stojí na každé straně matice framework, který mapování vyslovuje, i framework, který ho nevyslovuje**, v obou ekosystémech — a asymetrie Dapper ↔ MyBatis je tím prvním doloženým párem, kde se F6 dá měřit oběma směry.

**Mezireprezentace se nemění a pivot obstál.** `Model` nedostává jediný typ ani hodnotu, `IQueryVisitor` se nemění, deskriptory ostatních pěti cílů se nemění, `AbstractWrappers`, `Common` i orchestrace zůstávají beze změny až na řádky továren — tedy přesně to, co S1 pro nový framework slibuje a co §4.3 článku žádá. Mění se dvě vrstvy **jazyka**, a obě o věc svého jazyka: `JavaEntityParsing` umí nově přečíst rozhraní a deklarace jeho metod, `TransactSql` bere nepovinnou mapu vyslovených skalárů. Že šestý framework a první framework bez mapování vázaného na třídu stál jeden projekt a dvě jazykové drobnosti, je nejsilnější doklad S1, jaký zatím máme — silnější než EclipseLink, protože ten sdílel celou specifikaci.

**Dvě věty dřívějších dokumentů se tímhle rozhodnutím opravují a ani jedna nemění volbu, kterou nesly.** Tutoriál k MyBatisu odvozoval, že pořadí čtení musí u MyBatisu začít konfigurací kvůli `<typeAliases>`; konfiguraci nečteme a alias rozřešíme jménem, takže platí výchozí pořadí 017. A rozhodnutí 067 nechávalo obě větve `mapUnderscoreToCamelCase` otevřené; tímhle rozhodnutím platí ta druhá — konvence se nematerializuje nikdy, protože jediné místo, kde by se uplatnila, do mapování nečteme.

**Slovník typů obsahu se nerozšiřuje a dvě jeho hodnoty se popisem rozšiřují** — `JavaEntity` je nově javová třída, ať už anotovaná, nebo prostá, a `JavaQuery` javový soubor nesoucí dotazy, ať už metodou s `createQuery`, nebo rozhraním mapperu. Je to rozhodnutí 025 v praxi: hodnota jmenuje **jazyk**, ne framework, takže šestý framework do ní vstoupil bez jediné nové hodnoty.

**Co tohle rozhodnutí neurčuje.** Parametrizované stránkování (`limit #{n}`) zůstává vyňaté a má vlastní položku, otevřenou rozhodnutím 083. Automatické mapování — `<select resultType>` bez `<resultMap>`, kde je jediným mapovacím tvrzením alias v SQL — patří pod položku o aliasu jako zdroji mapování Dapperu, protože je to tatáž otázka u obou mapper frameworků; tohle rozhodnutí o ní netvrdí nic než to, že dnes mapováním není. MyBatis-Plus se svými `@TableName` a `@TableId` je javovým protějškem Dapper.Contribu a soudí se položkou o něm, týmž kritériem. `@SelectProvider`, tedy SQL skládané javovým kódem, je `Failure`, který anotaci jmenuje — je to program, ne artefakt, a hranici mezi obojím drží rozhodnutí 040. A deklarace cílového dialektu, diferenční ověření podle F13 i javová větev Advisoru zůstávají tam, kde byly.

**Cena.** Šestá hodnota `ORMEnum` zdražuje každou matici (šestatřicet směrů místo pětadvaceti) a javová sada roste o třetinu, protože MyBatis má vlastní programový bootstrap a vlastní negativní polovinu. Dvě jednotky ze tří jsou dvojaké, takže se každý mapper rozparsuje dvakrát — cena rozhodnutí 081, kterou tam platí i NHibernate. A dynamický příkaz, tedy to, kvůli čemu se MyBatis v praxi používá, se z poloviny nepřeloží: rodinu příkazů model nenese a `<if>` dotaz odmítne. Zapisujeme to jako vyslovenou mez, ne jako díru — je to jediné místo, kde je MyBatis jako **zdroj** výrazně chudší než ostatních pět, a čtenáři práce to říká víc o mezireprezentaci než deset přeložených dotazů.

**Podle rozhodnutí 069 je to MINOR** — přibývá šestý framework, veřejné rozhraní ani tvar odpovědi se nemění —, a je to poslední MINOR cíle 2: vydání, které cíl uzavře, je `2.0.0`.

## Historie

**2026-09-20 — revidováno.** Volba se nemění v ničem: MyBatis je dál wrapper nad dvěma vrstvami jazyka a nad žádnou vrstvou frameworku, `<foreach>` v kanonickém tvaru je dál jediný kolekční parametr a jediná dynamická značka, kterou builder vydává, a všechno ostatní z textu výš implementace potvrdila. Opravená je jedna věta o mechanismu, na kterou se přišlo teprve při psaní kódu.

Nepovinný argument konstruktoru `SqlQueryReader`u nemůže být mapa jméno → skalár, jak tu stálo: musí nést i **příznak kolekčnosti**. Text výš totiž zároveň říká, že se celá značka `<foreach>` nahradí jediným parametrem se zapnutým příznakem kolekčnosti — a T-SQL nemá pro holý parametr za `IN` žádný tvar, takže se značka do textu propíše jako `IN (@ids)`, což je pro gramatiku jednoprvkový výčet hodnot a pro každého jiného čtenáře jazyka přesně to. Odkud by se tedy kolekčnost vzala, není v textu: gramatika ji nevidí a odvodit ji nelze, protože `WHERE x IN (@a)` z dapperovské jednotky znamená něco jiného. Argument je proto mapa jméno → `SqlParameterFacts`, tedy dvojice skalár a kolekčnost. Je to táž věc, jakou původní věta mínila — fakt o textu, který wrapper ze zdroje odloupl dřív, než ho gramatika uvidí, a který stojí v téže pozici jako hlásicí kanál —, jen o jedno pole širší; zásuvným bodem gramatiky se tím nestává, protože gramatika se o něj nikde neptá.
