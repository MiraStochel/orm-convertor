# Srovnání javových ORM frameworků: Hibernate, EclipseLink a MyBatis

Tento dokument shrnuje srovnání tří javových ORM frameworků, které má převodník zpracovávat podle požadavků F7 (Hibernate), F8 (MyBatis) a F9 (EclipseLink). Je to javový protějšek [srovnání .NET frameworků](orm-frameworks-comparison.md): kapitoly mají stejná čísla, stejné pořadí a stejné řádky tabulek, aby se oba dokumenty daly položit vedle sebe a číst po řádcích. Slouží jako podklad pro analytickou část práce a jako vstup pro rozhodnutí o javové straně nástroje ([`open-items.md`](../open-items.md), cíl 2).

Srovnání je organizované tematicky, ne po frameworcích. U každého tématu sledujeme tři osy: **co lze vyjádřit** (expresivita), **jak se to vyjadřuje** (syntax) a **co platí implicitně** (defaulty, které parser musí materializovat).

Sloupce odpovídají sloupcům .NET dokumentu jen zčásti. Hibernate je přímý protějšek NHibernate a MyBatis je protějšek Dapperu; EclipseLink žádný .NET protějšek nemá a ve druhém sloupci sedí proto, že je druhým plnohodnotným ORM, ne proto, že by se podobal EF Core. Nejdůležitější rozdíl proti .NET trojici je jinde: **Hibernate a EclipseLink jsou dvě implementace téže specifikace, Jakarta Persistence 3.2.** Řádky, kde se shodují proto, že to určuje specifikace, jsou označené **(JPA)** — ta množina je kandidát na sdílenou vrstvu obou wrapperů (F7, F9) a pro návrh je stejně důležitá jako rozdíly. Řádky, kde se shodují všechny tři frameworky, jsou ponechané záměrně ze stejného důvodu jako v .NET dokumentu: společná podmnožina vymezuje, co IR modelovat nemusí.

**Na rozdíl od .NET dokumentu nestojí tahle srovnání na kódu v repozitáři.** Javový projekt v repozitáři není, benchmarky mají jen .NET větev a z trojice jsme spustili jen Hibernate ([tutoriál](tutorials/hibernate-getting-started.md)). Tvrzení o EclipseLinku a MyBatisu jsou z dokumentace zafixovaných verzí; kde se něco dá rozhodnout jen vygenerovaným DDL, je to řečeno u položky.

## Zafixované verze

Všechna tvrzení v dokumentu platí pro následující verze (kanonická tabulka je v [`architecture.md`](../architecture.md), „Zafixované verze"). Tam, kde je schopnost vázaná na konkrétní verzi, je to uvedeno přímo u položky.

| Komponenta | Verze | Souřadnice Maven |
|---|---|---|
| JDK | **25** (LTS) | — |
| Jakarta Persistence | **3.2** | `jakarta.persistence:jakarta.persistence-api` (tranzitivně z obou implementací) |
| Hibernate ORM | **7.4.5.Final** | `org.hibernate.orm:hibernate-core` |
| EclipseLink | **5.0.0** | `org.eclipse.persistence:org.eclipse.persistence.jpa` |
| MyBatis | **3.5.19** | `org.mybatis:mybatis` |
| `mssql-jdbc` | **13.4.0.jre11** | `com.microsoft.sqlserver:mssql-jdbc` |
| SQL Server | **2022** | (`mcr.microsoft.com/mssql/server:2022-latest`, tentýž obraz jako pro .NET) |

Čtyři skutečnosti o těchto verzích mají dopad na obsah tabulek:

- **Hibernate 7.4.5.Final** (2026-07-12) patří do řady 7.x, která od 7.0 (květen 2025) staví na Javě 17 a Jakarta Persistence 3.2. Verze 7.0 zároveň **změnila licenci z LGPL 2.1 na Apache 2.0** (od 7.0.0.Beta5) a **odstranila** metody `Session.save`, `update`, `saveOrUpdate` a `delete` ve prospěch `persist`, `merge` a `remove`. Kód generovaný pro Hibernate 6 by se tedy proti 7.4.5 nezkompiloval. `hbm.xml` je od 7.0 zavržené a v 8.0 zmizí; řada 7.4 (7.4.0 vyšlo 2026-05-26) přidala mimo jiné `@Nationalized` na úrovni balíčku a bezpečné stránkování přes `join fetch` kolekce.
- **EclipseLink 5.0.0** (2026-03-23) je hlavní vydání pro Jakarta EE 11: implementuje Jakarta Persistence 3.2, vyžaduje Javu 17 a odstranilo CORBA, RMI a SDO. Obě implementace JPA tak stojí na téže úrovni specifikace, což je předpoklad sdílené vrstvy — na JPA 3.1 (Hibernate 6.2, EclipseLink 4.0) by řada řádků níž vyšla jinak. Vydání 5.0.1 existuje (červen 2026); zafixovaná je 5.0.0.
- **MyBatis 3.5.19** (2025-01-02) je poslední vydání řady, která běží na Javě 8; řada 3.6 zvedá minimum na Javu 11. Na JDK 25 běží bez výhrad. Je to jediný z trojice bez specifikace za zády a jediný, kde SQL píše autor — cílový dialekt databáze je tedy jeho starost, přesně jako u Dapperu.
- **Všechny tři jedou přes týž ovladač `mssql-jdbc` 13.4.0** (klasifikátor `jre11` platí pro Javu 11 a novější, tedy i pro 25). Ovladač je čistě javový a mluví jen TCP, `encrypt=true` je od verze 10.2 výchozí, takže `trustServerCertificate=true` je nutné u všech tří ze stejného důvodu jako v .NETu. Vlastnosti připojení jsou proto pro celou javovou trojici identické.

---

## 1. Identita frameworků

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Kategorie** | plnohodnotný ORM s mapováním, správou identity a generováním SQL; implementace Jakarta Persistence | plnohodnotný ORM téhož druhu; **referenční implementace** Jakarta Persistence (od JPA 2.0) | SQL mapper; materializátor výsledků podle deklarovaného `resultMap`, binder parametrů a šablonovací jazyk pro SQL |
| **Původ** | Gavin King, 2001; dnes tým Red Hatu; předloha NHibernate | Oracle TopLink (kořeny ve Smalltalku, Java od 1998), věnovaný Eclipse Foundation v roce 2007 | iBATIS (Clinton Begin, 2002, Apache); v roce 2010 odchod od Apache a přejmenování na MyBatis |
| **Správa a kadence** | Red Hat; nová minor řada každých několik měsíců (7.0 05/2025, 7.1 08/2025, 7.2 12/2025, 7.4 05/2026), starší řady jen v omezené podpoře | projekt Eclipse EE4J; hlavní verze vázané na vydání Jakarta EE (4.0 pro EE 10, 5.0 pro EE 11) | komunita na GitHubu (mybatis.org); řada 3.5.x od roku 2019, opravná vydání nepravidelně |
| **Licence** | **Apache 2.0 od 7.0**; do 6.x LGPL 2.1, kterou NHibernate má dodnes | EPL 2.0 a EDL 1.0 (dvojí licence, EDL je BSD-style) | Apache 2.0 |
| **Minimální Java** | 17 (od 7.0); běží na 17, 21, 25 a 26 | 17 (od 5.0) | 8 (celá řada 3.5.x) |
| **Standardizační vrstva** | Jakarta Persistence 3.2 a Jakarta Data 1.0 | Jakarta Persistence 3.2 | **žádná** |
| **Filozofie** | doménový model je primární, databáze detail; HQL nad rámec JPQL, bohatá vlastní anotační vrstva | totéž; TopLinkové dědictví je vidět v nativním API (`ClassDescriptor`, `Expression`) a v rozsahu konfigurace cache | SQL je primární a viditelné; framework řeší hranici mezi JDBC a objekty a k tomu **deklaruje**, jak výsledek složit |
| **Co framework ví o schématu** | vše, co je v mapování — tabulky, sloupce, klíče, relace, dědičnost, typy | totéž | jen to, co říká `resultMap`: dvojice sloupec–vlastnost a tvar vnoření; **o tabulkách nic** |
| **Role v převodníku** | zdrojový i cílový framework (F7); přímý protějšek NHibernate | zdrojový i cílový framework (F9); sdílí s Hibernate JPA vrstvu, F9 žádá pět testů překladu právě mezi nimi | zdrojový i cílový framework (F8); protějšek Dapperu, ale s deklarativním místem pro mapování, které Dapper nemá |

---

## 2. Instalace a struktura projektu

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Artefakty Maven** | `hibernate-core` 7.4.5.Final + `mssql-jdbc` 13.4.0.jre11 | `org.eclipse.persistence.jpa` 5.0.0 + `mssql-jdbc` | `mybatis` 3.5.19 + `mssql-jdbc` |
| **Proč ovladač ručně** | JDBC ovladač se registruje sám přes `ServiceLoader`, stačí, že je na classpath; v kódu ani konfiguraci se nejmenuje | totéž | totéž pro externí `DataSource`; vestavěný `POOLED`/`UNPOOLED` zdroj chce třídu ovladače v konfiguraci (`driver=com.microsoft.sqlserver.jdbc.SQLServerDriver`) |
| **Tranzitivní závislosti** | `jakarta.persistence-api` 3.2, `jakarta.transaction-api`, `jboss-logging`, `byte-buddy` (proxy a enhancement), `antlr4-runtime` (parser HQL), `hibernate-models`, `jandex`, `classmate` | `org.eclipse.persistence.core`, `.asm` (bytecode weaving), `.jpa.jpql` (parser JPQL), `jakarta.persistence-api` 3.2 | **žádné povinné** — OGNL i Javassist jsou zabalené uvnitř (`org.apache.ibatis.ognl`, `org.apache.ibatis.javassist`) |
| **Závislost entit na frameworku** | na `jakarta.persistence` (API specifikace), ne na implementaci; vendor anotace z `org.hibernate.annotations` váží na Hibernate | na `jakarta.persistence`; vendor anotace z `org.eclipse.persistence.annotations` váží na EclipseLink | **žádná** — čisté POJO; anotace `@Select`, `@Results` sedí na mapper rozhraní, ne na entitě |
| **Zásah do build souboru** | žádný; `src/main/resources` je na classpath automaticky | žádný | žádný; XML mapper vedle rozhraní (stejná cesta balíčku pod `resources`) se načte sám při registraci rozhraní |
| **Doplňkové balíčky v ekosystému** | `hibernate-processor` (statický metamodel, kontrola HQL při kompilaci, Jakarta Data repozitáře), `hibernate-scan-jandex` (skenování classpath), `hibernate-hikaricp`/`-agroal` (pool), `hibernate-jcache` (cache 2. úrovně), `hibernate-vector`, `hibernate-spatial`, Maven plugin pro transformaci `hbm.xml` | `org.eclipse.persistence.jpa.modelgen.processor` (statický metamodel), weaving agent (`-javaagent`) nebo `StaticWeave`, MOXy (JAXB), JPA-RS, DBWS, NoSQL | `mybatis-spring` a `mybatis-spring-boot-starter`, MyBatis Generator (entity a mappery z katalogu databáze), MyBatis Dynamic SQL (typovaný builder), MyBatis Migrations; třetí strana **MyBatis-Plus** (`@TableName`, `@TableId`, `BaseMapper` s CRUD) |

MyBatis-Plus je javový protějšek Dapper.Contrib: nad holým MyBatisem zavádí anotace pro tabulku a klíč a generické CRUD metody, takže zdrojový projekt s ním by nesl mapovací fakty, které holý MyBatis nemá kde vyslovit. Otázka rozsahu je stejná jako u Contribu ([`open-items.md`](../open-items.md), nálezy nad .NET frameworky) a rozhodnutí k F8 by ji mělo zodpovědět stejným kritériem.

---

## 3. Požadavky na doménové třídy

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Modifikátor `final`** | **(JPA)** třída entity nesmí být `final` a žádná persistovaná pole ani metody nesmějí být `final`; Hibernate to jen zaloguje a vzdá se proxy, takže líné načtení `@ManyToOne` padne na eager | **(JPA)** totéž; weaving `final` třídu neumí upravit a líné načtení i sledování změn pro ni odpadá | bez omezení |
| **`virtual`** | odpadá — v Javě jsou metody virtuální implicitně; požadavek proxy se obrací na „ne `final`" | totéž | nerelevantní |
| **Bezparametrický konstruktor** | **(JPA)** povinný, `public` nebo `protected`; od 7.3 ho enhancer umí dogenerovat | **(JPA)** povinný | nepovinný — `<constructor>` v `resultMap`, nebo automatické konstruktorové mapování (podle pořadí sloupců, od 3.5.10 i podle názvů argumentů přes `argNameBasedConstructorAutoMapping`); záznamy (`record`) tak jdou mapovat |
| **Záznamy (`record`) jako entita** | **(JPA)** ne — entita nesmí být `record`, `enum` ani rozhraní; záznam smí být `@Embeddable` (od JPA 3.2) | **(JPA)** totéž; 5.0 záznamy pro `@Embeddable` podporuje | ano, přes konstruktorové mapování |
| **Přístup k hodnotě** | **(JPA)** podle umístění `@Id` pro celou třídu — na poli přístup přes pole, na getteru přes vlastnost; přebít lze `@Access` | **(JPA)** totéž | reflexí: setter, a když není, přímo pole; gettery a settery nejsou potřeba |
| **Přístupnost setterů** | mohou chybět; přístup přes pole čte i zapisuje soukromá pole | totéž | mohou chybět (viz výš) |
| **Vlastnosti pro cizí klíč** | **(JPA)** sloupec FK nese `@JoinColumn` na navigaci; skalární atribut nad týmž sloupcem projde jen s `insertable = false, updatable = false`, jinak je to opakovaný sloupec — totéž pravidlo jako v NHibernate | **(JPA)** totéž | jen holý FK sloupec; `<association>` se naplní jen JOINem nebo vnořeným `select` |
| **Kolekce** | **(JPA)** `Set`, `List`, `Map`, `Collection`; typ má sémantický význam — `List` je bez `@OrderColumn` bag, s ním nese pořadí; `Map` přes `@MapKey` | **(JPA)** totéž | libovolná `Collection`; `<collection ofType="Book">`, výchozí `javaType` je `ArrayList`; typ nic neznamená |
| **Nullabilita v jazyce** | primitiv nemůže být `null`; Hibernate z primitivu odvodí `not null` v DDL; klíč musí být obalový typ (`Integer`), aby šel rozeznat nepřiřazený stav | totéž | primitiv nad sloupcem s `NULL` zůstane tiše na výchozí hodnotě, protože setter se u `null` nevolá (`callSettersOnNulls=false`); se zapnutým voláním setter s `null` na primitivu vyhodí výjimku |
| **Rovnost** | **(JPA)** třída složeného klíče musí být `Serializable` a přepsat `equals` i `hashCode`; entita sama nemusí | **(JPA)** totéž | `<id>` v `resultMap` říká, podle čeho porovnávat řádky při vnořování; `equals` se nevyžaduje |

**Důsledek pro IR.** Řádky „požadavky na entitu" jsou i tady omezení cílového frameworku, ne fakta o doméně, a builder je má generovat automaticky. Proti .NETu se ale liší v tom, že polovina z nich jsou omezení **jazyka**: „ne `final`" a `virtual` je tentýž požadavek proxy mechanismu vyjádřený opačně, protože C# a Java mají opačný default, a `Integer` proti `int` je nullabilita vyjádřená typem místo `?`. Tutoriál k Hibernate to zapsal jako jazykovou osu vynucených členů a rozhodnutí o javové straně ([`open-items.md`](../open-items.md)) ji musí dát deskriptoru. MyBatis nevynucuje nic — jako Dapper.

---

## 4. Kde žije mapování

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Primární forma** | **(JPA)** anotace `jakarta.persistence.*` na entitě | **(JPA)** totéž | XML mapper: `<mapper namespace="…">` s `<resultMap>` a příkazy `<select>`, `<insert>`, `<update>`, `<delete>` |
| **Alternativní formy** | **(JPA)** `orm.xml` (standardní deskriptor); hibernátovské `mapping.xml` (rozšířené schéma `orm.xml`, cíl transformace z `hbm.xml`); `hbm.xml` zavržené, v 8.0 odstraněné | **(JPA)** `orm.xml`; `eclipselink-orm.xml` (rozšířené schéma); `DescriptorCustomizer` a `SessionCustomizer` v kódu; nativní `ClassDescriptor` API z TopLinku | anotace na mapper rozhraní (`@Select` s `@Results`/`@Result`, `@One`, `@Many`); `@ResultMap` odkazující na XML; aliasy `AS` v SQL; automatické mapování podle shody názvů |
| **Počet vrstev** | **(JPA)** tři: konvence → anotace → `orm.xml`; XML anotace přebíjí a `<xml-mapping-metadata-complete/>` je vypíná úplně (`metadata-complete="true"` totéž pro jednu entitu) | **(JPA)** totéž | dvě: automatické mapování → `resultMap`; explicitní mapování vítězí a automatické doplní, co `resultMap` nejmenuje (`autoMapping="false"` na `resultMap` nebo `autoMappingBehavior=NONE` to vypne). **Anotace a XML se nevrství**: stejné id v obou je chyba při sestavení továrny |
| **Umístění vůči doméně** | na třídě (anotace) nebo mimo ni (`orm.xml`) | totéž | mimo třídu, v XML vedle mapper rozhraní; **per příkaz, ne per třída** — jedna třída může mít víc `resultMap` a každý dotaz si vybírá |
| **Kontrola v době kompilace** | anotace sedí na atributu, takže překlep v názvu atributu nemůže nastat; řetězce `mappedBy`, `referencedColumnName` a JPQL nekontrolované — HQL v `@NamedQuery` a `@HQL` kontroluje `hibernate-processor` při kompilaci | totéž bez kontroly dotazů; metamodel procesor dává typované Criteria | žádná; XML se čte až při sestavení továrny, názvy vlastností se tam proti třídě ověří, názvy sloupců nikdy |
| **Deklarativní místo pro mapování** | ano, jedno | ano, jedno | **ano** — `<resultMap>`; v tom se MyBatis od Dapperu liší nejvíc |
| **Nutná explicitní konfigurace pro funkční model** | **(JPA)** minimum je `@Entity` a `@Id`; zbytek konvence | **(JPA)** totéž | pro mapování nic, když názvy sedí; SQL vždy |
| **Podpora v IDE** | IntelliJ IDEA zná JPA anotace i JPQL (JPA konzole) | totéž | plugin MyBatisX; XML mapper má XSD, takže strukturu IDE kontroluje, SQL uvnitř ne |

`orm.xml` je pro převodník podstatný ze dvou důvodů: je standardní, na implementaci nezávislý, a pořadí čtení je dané specifikací — anotace první, XML poslední, s možností anotace vypnout. Je to přesně tvar, se kterým počítá rozhodnutí [068](../decisions/068-source-framework-precedence-orders-the-reading.md), a pro F7 i F9 ho stačí aplikovat.

### Názvové defaulty

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Tabulka** | **(JPA)** při vynechání `@Table` název entity (`Author`, bez pluralizace) | **(JPA)** název entity, EclipseLink ho ale v DDL zapíše **velkými písmeny** (`AUTHOR`) | není pojem tabulky |
| **Sloupec** | **(JPA)** název atributu (`bornOn`) | název atributu **velkými písmeny** (`BORNON`) — vlastnost `eclipselink.jpa.uppercase-column-names`, ve výchozím stavu zapnutá, simuluje necitlivost na velikost písmen | název sloupce musí odpovídat názvu vlastnosti |
| **Sloupec FK** | **(JPA)** `<atribut>_<sloupec PK>`, tedy `author_AuthorId` | totéž s velkými písmeny | nerelevantní |
| **Spojovací tabulka N:M** | **(JPA)** `<tabulka vlastníka>_<tabulka inverzní strany>`, tedy `Authors_Books`; sloupce z názvu entity, resp. inverzního atributu, a klíče | totéž | nerelevantní |
| **Generátor bez strategie** | `AUTO` → **sekvence** `<Entita>_SEQ` s krokem 50 (od Hibernate 6) | `AUTO` → **tabulka** `SEQUENCE` se sloupci `SEQ_NAME`, `SEQ_COUNT`, řádek `SEQ_GEN`, krok 50 | nerelevantní |
| **Porovnávání názvů** | přesné, pokud pojmenovací strategie neříká jinak | necitlivé na velikost písmen díky uppercase | **case-insensitive**; podtržítka na camelCase (`stock_item_id` na `stockItemId`) zapíná `mapUnderscoreToCamelCase`, ve výchozím stavu **vypnuté** — totéž, co u Dapperu dělá `MatchNamesWithUnderscores` |
| **Vyměnitelná pojmenovací strategie** | **ano** — `hibernate.implicit_naming_strategy` a `hibernate.physical_naming_strategy`; Spring Boot dosazuje strategii s podtržítky, holý Hibernate ne | jako pojem ne; přejmenovat lze v kódu přes `SessionCustomizer` | ne; jen výše uvedený přepínač |

**Důsledek pro IR.** Třetí osa analýzy tu má ostřejší hranu než v .NETu: **tatáž anotace dává v obou implementacích JPA jiný výchozí název i jiný generátor.** `@Entity class Author` bez `@Table` je v Hibernate tabulka `Author` a v EclipseLinku `AUTHOR`; `@GeneratedValue` bez strategie je v Hibernate sekvence a v EclipseLinku tabulka. Kritérium rozhodnutí [067](../decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) platí beze změny — absenční default parser nematerializuje a mlčení doplní katalog (F6) —, ale s dvěma dodatky. Za prvé profil cílového frameworku musí jmenovat implementaci, ne jen specifikaci, protože z „JPA 3.2" se výchozí generátor odvodit nedá. Za druhé názvy z EclipseLinku se s katalogem musí párovat necitlivě na velikost písmen; na SQL Serveru s výchozí kolací to databáze sama nerozliší, párování v nástroji ale ano.

---

## 5. Primární klíče

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Jednoduchý klíč, deklarace** | **(JPA)** `@Id`, **povinné, žádná konvence**; bez něj `AnnotationException: No identifier specified for entity` | **(JPA)** `@Id` povinné | **nedeklaruje se**; `<id column="AuthorId" property="id"/>` v `resultMap` je jen příznak identity pro vnořené výsledky a cache, ne klíč tabulky |
| **Kompozitní klíč, deklarace** | **(JPA)** `@IdClass(BookTranslationId.class)` s víc `@Id` přímo na entitě, nebo `@EmbeddedId` s jedním vnořitelným atributem | **(JPA)** totéž | víc `<id>` elementů v `resultMap`; ve `WHERE` ručně |
| **Požadavek na klíčovou třídu** | **(JPA)** **vždy** — i u `@IdClass` existuje samostatná třída: veřejná, s bezparametrickým konstruktorem, `Serializable`, s `equals` a `hashCode`; u `@EmbeddedId` smí být záznam (JPA 3.2) | **(JPA)** totéž | žádný |
| **Generování hodnoty** | **(JPA)** `@GeneratedValue(strategy = AUTO \| IDENTITY \| SEQUENCE \| TABLE \| UUID)` s `@SequenceGenerator`/`@TableGenerator`; `AUTO` = sekvence; navíc `@UuidGenerator`, `@Generated`, `@IdGeneratorType` pro vlastní generátory; sekvence nelze kombinovat s jiným než `@SequenceGenerator` (zpřísněno v 7.0) | **(JPA)** táž anotace; `AUTO` = tabulka; navíc `@UuidGenerator` a nativní sekvence | `useGeneratedKeys="true" keyProperty="id"` na `<insert>` (JDBC `getGeneratedKeys`, na SQL Serveru funguje s IDENTITY), nebo `<selectKey order="AFTER">SELECT SCOPE_IDENTITY()</selectKey>`; globální `useGeneratedKeys` je ve výchozím stavu vypnuté |
| **Naplnění klíče po insertu** | automatické | automatické | poloautomatické — deklarované per příkaz přes `keyProperty` |
| **Entita bez klíče** | **(JPA)** ne, každá entita má `@Id`; pro readonly výsledky konstruktorový výraz `select new Dto(…)` nebo `@SqlResultSetMapping` u nativního dotazu | **(JPA)** totéž | přirozený stav; výsledkem může být cokoli včetně `Map` |
| **Alternativní a unikátní klíče** | **(JPA)** `@Column(unique = true)`, `@UniqueConstraint` v `@Table`; hibernátovské `@NaturalId` s `Session.byNaturalId`, od 7.3 `@NaturalIdClass` a `KeyType` pro `find` | **(JPA)** `@UniqueConstraint`; `@CacheIndex` pro vyhledání v cache podle jiného sloupce | v DDL |

**Důsledek pro IR.** Rozhodnutí [006](../decisions/006-flat-composite-key-rendering.md) zvolilo `@IdClass` jako javový cíl a tutoriál ověřil, že cesty k vlastnostem zůstávají ploché. Klíčová třída je ale v JPA vždy, ne volitelně — builder ji syntetizuje jako vynucený člen s podmínkou složeného klíče a název bere ze zaznamenané klíčové třídy zdroje (rozhodnutí [031](../decisions/031-key-class-as-declaration-of-key-parts.md)). Nový je řádek o `AUTO`: je to tvrzení „hodnota se generuje", jehož mechanismus určuje implementace. Parser JPA zdroje tedy může kanonický parametr generátoru (rozhodnutí [020](../decisions/020-canonical-generator-parameter-vocabulary.md)) naplnit jen s profilem zdrojové implementace v ruce, a builder by `AUTO` nikdy neměl vypsat — překlad Hibernate → EclipseLink by jinak tiše vyměnil sekvenci za tabulku.

---

## 6. Sloupce, typy a nullabilita

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Deklarace sloupce** | **(JPA)** `@Column(name = "Title", length = 300, nullable = false)`, `@Basic(optional = false)` | **(JPA)** totéž | `<result column="Title" property="title" javaType="String" jdbcType="NVARCHAR"/>`; délka ani nullabilita nemají kam |
| **Typový systém** | dvojice `JavaType` + `JdbcType` na atribut (od 6.0): `@JdbcTypeCode(SqlTypes.NVARCHAR)`, `@JavaType`, vlastní `UserType` přes `@Type`; **(JPA)** `AttributeConverter` s `@Convert` | **(JPA)** `AttributeConverter`; nativní `@Converter`, `@TypeConverter`, `@ObjectTypeConverter`; SQL typ volí `DatabasePlatform` z javové třídy | registr `TypeHandler` podle dvojice `javaType` × `jdbcType`; vlastní `BaseTypeHandler<T>`; `jdbcType` je JDBC rodina, ne DDL |
| **`String` → SQL typ** | **`varchar(255)`**; `nvarchar` přes `@Nationalized` na atributu, od 7.4 i na balíčku, nebo globálně `hibernate.use_nationalized_character_data=true` | **`VARCHAR(255)`**; `nvarchar` jen přes `columnDefinition`, globální přepínač po vzoru Hibernate nemá | podle DDL; parametr `String` ale ovladač posílá jako `nvarchar` (`sendStringParametersAsUnicode=true`), což platí pro všechny tři |
| **Datum a čas** | `LocalDate` → `date`, `LocalDateTime` → `datetime2` s přesností 7 (výchozí přesnost pro SQL Server změněná v 7.0), `OffsetDateTime` → `datetimeoffset`; `Instant` (JPA 3.2) | `LocalDate`, `LocalDateTime`, `Instant`, `Year` (5.0); SQL typ volí platforma — ověřit z DDL | vestavěné handlery pro `java.time` od 3.4.5; typ určuje ovladač přes JDBC 4.2 |
| **Délka řetězce** | **(JPA)** `length`, výchozí 255 | **(JPA)** totéž | v DDL |
| **Přesnost decimal** | **(JPA)** `precision = 18, scale = 2`; bez nich doplní dialekt výchozí přesnost | **(JPA)** totéž; bez nich platforma | v DDL |
| **Nullabilita** | **(JPA)** `nullable = false`, `@Basic(optional = false)`, `@ManyToOne(optional = false)`, `@JoinColumn(nullable = false)`; primitiv je `not null` sám od sebe; výchozí stav nullable | **(JPA)** totéž | v DDL; na straně objektu jen volba primitiv/obal |
| **Výchozí hodnota** | `@ColumnDefault("…")` (hibernátovské) | jen přes `columnDefinition` | v DDL |
| **Počítané sloupce** | `@Formula("…")` — SQL výraz místo sloupce, jen pro čtení | nic anotačního; jen nativním deskriptorem | v SELECT |
| **Ignorování vlastnosti** | **(JPA)** `@Transient` nebo modifikátor `transient` | **(JPA)** totéž | co není v `resultMap`, se nemapuje; s automatickým mapováním se sloupec bez protějšku tiše zahodí (`autoMappingUnknownColumnBehavior=NONE`) |
| **Vlastní SQL typ** | **(JPA)** `@Column(columnDefinition = "nvarchar(300)")`; `@JdbcTypeCode` | **(JPA)** `columnDefinition` | `jdbcType` na `<result>` a v `#{}`, tedy rodina JDBC, ne typ v DDL |
| **Kdo rozhoduje o SQL typu** | dialekt z `JdbcType` a délky | `DatabasePlatform` (`SQLServerPlatform`) z javové třídy | autor DDL |
| **JSON** | `@JdbcTypeCode(SqlTypes.JSON)`; na SQL Serveru 2022 uložené jako `nvarchar(max)` | v 5.0 rozšířená podpora JSON (`jakarta.json`); podoba na SQL Serveru podle platformy | vlastní `TypeHandler` nad Jacksonem |
| **Typ `vector`** | modul `hibernate-vector`; podpora pro SQL Server od 7.2 vyžaduje SQL Server 2025 — v tomto prostředí nedostupné, stejně jako u EF Core | ne | ne |

**Důsledek pro IR.** Typový model je zneutralizovaný na obou stranách (rozhodnutí [014](../decisions/014-language-type-model.md) a [019](../decisions/019-neutral-database-type-vocabulary.md)) a tahle tabulka ukazuje proč. Obě implementace JPA dávají z `String` **`varchar`**, oba .NET ORM `nvarchar` — tentýž jazykový typ na téže databázi, jiný sloupec, a je to jeden typ ve dvou nationalizačních režimech, ne dva typy. Hibernate má k tomu explicitní přepínač na třech úrovních (atribut, balíček, konfigurace), EclipseLink jen `columnDefinition`, takže nationalizační režim profilu cíle se do EclipseLink výstupu propíše doslovným typem, ne anotací. A o konkrétním SQL typu rozhoduje dialekt, resp. platforma — což je položka „cílový databázový dialekt v deskriptoru" v [`open-items.md`](../open-items.md).

Verzování zjištění je tu nutné dvakrát: `Instant` a `Year` jsou standardní typy až od JPA 3.2, a `@Nationalized` na úrovni balíčku existuje až od Hibernate 7.4 — na 7.0 by tentýž fakt šel vyslovit jen per atribut nebo globálně.

---

## 7. Relace 1:N a N:1

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Deklarace strany „jedna"** | **(JPA)** `@OneToMany(mappedBy = "author")` na kolekci | **(JPA)** totéž | `<collection property="books" ofType="Book" resultMap="bookMap"/>` (vnořený výsledek z JOINu) nebo `select="selectBooksByAuthor" column="AuthorId"` (vnořený dotaz); anotačně `@Many` |
| **Deklarace strany „mnoho"** | **(JPA)** `@ManyToOne` s `@JoinColumn(name = "AuthorId")` | **(JPA)** totéž | `<association property="author" javaType="Author" …>`; anotačně `@One` |
| **Určení vlastníka vztahu** | **(JPA)** `mappedBy` na inverzní straně = `inverse="true"` z NHibernate; FK zapisuje strana s `@JoinColumn`. Jednosměrné `@OneToMany` s `@JoinColumn` je povolené a FK pak zapisuje rodič dodatečným `UPDATE` | **(JPA)** totéž | nerelevantní |
| **Kaskády** | **(JPA)** `cascade = {PERSIST, MERGE, REMOVE, REFRESH, DETACH, ALL}`, `orphanRemoval = true`; hibernátovské `@OnDelete(action = CASCADE)` pro `ON DELETE CASCADE` v DDL; `SAVE_UPDATE` a `DELETE` z `@Cascade` v 7.0 odstraněné | **(JPA)** totéž; nativně `@PrivateOwned` a `@CascadeOnDelete` | `ON DELETE CASCADE` v DDL |
| **Sirotci** | **(JPA)** `orphanRemoval = true` — odebrání z kolekce znamená DELETE | **(JPA)** totéž | ruční DELETE |
| **Pořadí insertů** | odvozeno z grafu (`hibernate.order_inserts` pro dávky) | odvozeno z grafu podle omezení FK | řeší autor kódu |
| **Naplnění FK** | automatické z navigace | automatické | ruční přiřazení |
| **Vícesloupcový FK** | **(JPA)** `@JoinColumns({@JoinColumn(…), @JoinColumn(…)})` | **(JPA)** totéž | v `ON` klauzuli JOINu; `column="{bookId=BookId,lang=LanguageCode}"` u vnořeného dotazu |
| **Načtení souvisejících dat** | **(JPA)** `join fetch` v JPQL, grafy entit (`@NamedEntityGraph`, hinty `jakarta.persistence.fetchgraph`); hibernátovské `@BatchSize`, `@Fetch(SUBSELECT)` | **(JPA)** `join fetch`, grafy entit; nativně `@JoinFetch`, `@BatchFetch(JOIN \| EXISTS \| IN)`, hinty `eclipselink.batch`, `eclipselink.join-fetch` | JOIN s vnořeným `resultMap` a deduplikací podle `<id>`, nebo vnořený `select` |

`mappedBy` je totéž, co `inverse="true"` — a na rozdíl od EF Core je to samostatný pojem, ne odvození z umístění FK. Při překladu z Javy do EF Core informace mizí a nevadí to; opačným směrem ji builder odvodí z toho, která strana FK vlastní. `Relation` s rolemi `Owning`/`Inverse` v IR tak má přímý protějšek ve všech čtyřech plnohodnotných frameworcích a mimo ně stojí Dapper i MyBatis, které vztahy neznají — MyBatis zná jen **tvar výsledku**: `<collection>` říká, že se řádky mají složit do kolekce, ne že existuje cizí klíč.

---

## 8. Relace N:M

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Idiomatický zápis** | **(JPA)** `@ManyToMany` s `@JoinTable(name = "BookCategories", joinColumns = @JoinColumn(name = "BookId"), inverseJoinColumns = @JoinColumn(name = "CategoryId"))`; `mappedBy` na druhé straně | **(JPA)** totéž | JOIN přes spojovací tabulku v SQL a `<collection>` ve výsledku |
| **Junction jako entita** | ne — tabulka je zmíněná jen v `@JoinTable` | ne | ano, protože jinak než v SQL o ní mluvit nejde |
| **Skip navigation** | ano, fakticky — `Book.categories` míří rovnou na `Category` | ano | neexistuje |
| **Payload na junction tabulce** | nelze na `@ManyToMany`; nutný rozpad na dvě `@ManyToOne` z explicitní entity, obvykle s `@EmbeddedId` | totéž | přirozeně |

Platí totéž, co v .NET dokumentu: IR generuje N:M jako explicitní junction entitu (rozhodnutí [005](../decisions/005-many-to-many-as-explicit-junction-entity.md)), zatímco idiomatický zápis obou implementací JPA je skip navigation. Z šesti frameworků tak junction entitu idiomaticky nemá pět a MyBatis ji má jen proto, že nemá nic jiného. Rozhodnutí 005 zůstává vědomou volbou za cenu odchylky od zvyklostí; příznak `IsJunctionTable` v `EntityMap` je i pro javové buildery signál k budoucímu zploštění.

---

## 9. Dědičnost

| Strategie | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Table per hierarchy (TPH)** | **(JPA)** `@Inheritance(strategy = SINGLE_TABLE)` s `@DiscriminatorColumn` a `@DiscriminatorValue`; hibernátovské `@DiscriminatorFormula` | **(JPA)** totéž; nativně `@ClassExtractor` místo diskriminátoru | `<discriminator javaType="int" column="Kind"><case value="1" resultMap="…"/></discriminator>` — volba `resultMap` podle hodnoty sloupce; ruční `WHERE` |
| **Table per type (TPT)** | **(JPA)** `JOINED` | **(JPA)** `JOINED` | ruční JOIN |
| **Table per concrete class (TPC)** | **(JPA)** `TABLE_PER_CLASS` — ve specifikaci nepovinná, Hibernate ji umí | **(JPA)** umí | ruční UNION |
| **Výchozí chování** | **(JPA)** `SINGLE_TABLE` se sloupcem `DTYPE`, jakmile jedna entita dědí od druhé — jako TPH v EF Core, ne jako NHibernate | **(JPA)** totéž | žádné |
| **Nemapovaný předek** | **(JPA)** `@MappedSuperclass` — atributy se dědí, tabulka pro předka nevzniká | **(JPA)** totéž | nerelevantní |
| **Pokrytí v repozitáři** | není | není | není |

Konceptuální shoda s NHibernate (`<subclass>`, `<joined-subclass>`, `<union-subclass>`) i s EF Core (TPH, TPT, TPC) je tu skoro 1:1 a MyBatis má na rozdíl od Dapperu alespoň `<discriminator>`, tedy deklarovanou volbu typu podle hodnoty sloupce. Dědičnost zůstává nejlepším kandidátem na kapitolu, kde se ukáže, že jednotné IR je realistické — v repozitáři ale není javový kód, na kterém by se to dalo ukázat.

---

## 10. Konfigurace připojení

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Kde se konfiguruje** | v kódu (`StandardServiceRegistryBuilder`), v `hibernate.properties`, v `META-INF/persistence.xml`; **(JPA)** od 3.2 i programově přes `PersistenceConfiguration` bez `persistence.xml` | **(JPA)** `META-INF/persistence.xml`, mapa vlastností pro `Persistence.createEntityManagerFactory`, `PersistenceConfiguration` | `mybatis-config.xml` s `<environments>` (`<dataSource>`, `<transactionManager>`) a `<mappers>`, nebo objekt `Configuration` v kódu |
| **Oddělení konfigurace a mapování** | oddělené — konfigurace ve vlastnostech, mapování v anotacích | oddělené | oddělené — konfigurace jmenuje mappery, mapování je v nich |
| **Ovladač** | JDBC 4 se registruje sám; `jakarta.persistence.jdbc.driver` je nepovinné | totéž | vestavěný zdroj chce `driver` explicitně; `JNDI` nebo externí `DataSource` ne |
| **Dialekt** | **odvozený z JDBC metadat za běhu** (od Hibernate 6 jedna třída dialektu na produkt); `hibernate.dialect` netřeba | `eclipselink.target-database`, výchozí `Auto` — **odvozený z metadat**; explicitní `SQLServer` je nutný jen pro generování skriptů bez připojení | **žádný**; přenositelnost je na autorovi SQL |
| **Connection string** | `jdbc:sqlserver://localhost:1433;databaseName=…;encrypt=true;trustServerCertificate=true` | shodný | shodný |
| **`trustServerCertificate=true`** | nutné | nutné | nutné |
| **Důvod** | `mssql-jdbc` má od 10.2 `encrypt=true` jako výchozí a lokální server má self-signed certifikát; Java ho ověřuje proti vlastnímu truststoru, chyba zní `PKIX path building failed` | totéž | totéž |
| **Logování SQL** | `hibernate.show_sql`, `format_sql`, `highlight_sql`; hodnoty parametrů přes logger `org.hibernate.orm.jdbc.bind` na TRACE — vyžaduje logovací backend, Hibernate žádný nemá | `eclipselink.logging.level.sql=FINE`, `eclipselink.logging.parameters=true`; výchozí backend je `java.util.logging`, SLF4J přes `eclipselink.logging.logger` | nastavení `logImpl` (SLF4J, LOG4J2, JDK_LOGGING, STDOUT_LOGGING); logger jmenného prostoru mapperu na DEBUG vypíše `Preparing:`, `Parameters:` i `Total:` |
| **Pool připojení** | vestavěný jen pro vývoj (varování `not intended for production use`); HikariCP nebo Agroal modulem — Vibur, Proxool a UCP v 7.0 vypadly | vestavěný pool (`eclipselink.jdbc.connection_pool.*`) nebo `DataSource` | vestavěný `POOLED`, nebo externí `DataSource` |

Proti NHibernate je dialekt u obou implementací JPA fakt, který v kódu vůbec není a bez připojení k databázi ho nelze doplnit — tutoriál to zapsal jako argument pro F4–F6 a platí to pro EclipseLink stejně. MyBatis ho nemá vůbec a jeho SQL může být napsané pro jiný systém než SQL Server; to je důvod, proč položka o cílovém dialektu v [`open-items.md`](../open-items.md) žádá pro javový wrapper vlastní parser SQL místo gramatiky `TSql160Parser`.

---

## 11. Správa schématu

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Vytvoření databáze** | ne — `Cannot open database "…"` | ne | ne |
| **Vytvoření tabulek** | **(JPA)** `jakarta.persistence.schema-generation.database.action=create \| drop-and-create \| drop \| none`; hibernátovsky `hibernate.hbm2ddl.auto` (`create`, `create-drop`, `update`, `validate`, `none`) | **(JPA)** táž vlastnost; nativně `eclipselink.ddl-generation` (`create-tables`, `drop-and-create-tables`, `create-or-extend-tables`, `none`) s `eclipselink.ddl-generation.output-mode` (`database`, `sql-script`, `both`) | ručně psané DDL v `<update>`, nebo mimo aplikaci |
| **DDL bez připojení k databázi** | **(JPA)** `schema-generation.scripts.action=create` s `scripts.create-source=metadata`; od 3.2 i API `SchemaManager` (`emf.getSchemaManager().create(true)`) | **(JPA)** totéž, ale s `Auto` platformou potřebuje spojení — bez něj nutné `eclipselink.target-database=SQLServer` | triviálně — DDL píše autor |
| **Aktualizace existujícího schématu** | `hbm2ddl.auto=update` (best-effort, nedestruktivní) | `create-or-extend-tables` (přidá tabulky a sloupce, nikdy nemaže) | ručně; MyBatis Migrations je samostatný nástroj |
| **Validace schématu proti modelu** | `hbm2ddl.auto=validate`, `SchemaManager.validate()` | `SchemaManager.validate()` (JPA 3.2) | žádná |
| **Smazání schématu** | `drop`, `SchemaManager.drop()` | `drop-and-create-tables`, `SchemaManager.drop()` | `DROP TABLE IF EXISTS` |
| **Vhodnost pro produkci** | generování ne, `update` s výhradami | generování ne, `create-or-extend-tables` s výhradami | ruční správa mimo aplikaci |

Migrace nemá v jádře žádný z trojice; Flyway a Liquibase jsou externí nástroje stejně jako FluentMigrator v .NETu. Na obsah IR to nemá vliv. Podstatné je, že generování DDL je od JPA 3.2 **standardní** a shodné v obou implementacích — stejný `SchemaManager` a stejné vlastnosti dají DDL z Hibernate i z EclipseLinku, což je nejlevnější cesta k porovnání jejich výchozích typů (viz §22).

---

## 12. Životní cyklus a jednotka práce

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Hlavní objekt** | `Session` ze `SessionFactory`; **(JPA)** `Session` rozšiřuje `EntityManager` a `SessionFactory` rozšiřuje `EntityManagerFactory`, obě API jsou k dispozici naráz | **(JPA)** `EntityManager` z `EntityManagerFactory` | `SqlSession` ze `SqlSessionFactory`; práce přes proxy mapper rozhraní z `session.getMapper(…)` |
| **Cena vytvoření** | továrna drahá (staví model), vytváří se jednou; session levná | totéž; EclipseLink navíc ve výchozím stavu nasazuje líně, až při prvním `EntityManager` (`eclipselink.deploy-on-startup=false`) | továrna drahá (parsuje všechny XML mappery), jednou; `SqlSession` levná |
| **Doporučená životnost** | session na požadavek nebo jednotku práce | totéž | `SqlSession` na požadavek, zavřít vždy (`try-with-resources`) |
| **Thread safety** | továrna ano, session ne | továrna ano, `EntityManager` ne | továrna ano, `SqlSession` ne |
| **Otevření spojení** | líné | líné | líné, při prvním příkazu |
| **Stav objektů** | **(JPA)** transient / managed / detached / removed | **(JPA)** totéž | žádný — obyčejné instance |
| **Znovupřipojení odpojené entity** | **(JPA)** `merge()`; `update()` a `saveOrUpdate()` v 7.0 zmizely; `Session.lock()` | **(JPA)** `merge()` | nerelevantní |
| **Bezstavová varianta** | `StatelessSession` — bez persistence contextu, s `insert`/`update`/`delete`/`upsert`; v 7.0 nově používá cache 2. úrovně | není; readonly dotazy přes hint `eclipselink.read-only` | výchozí a jediný stav |

---

## 13. Zápis dat

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Vložení** | **(JPA)** `persist(entity)` — `save()` s návratem identifikátoru v 7.0 zmizelo | **(JPA)** `persist(entity)` | `<insert>` volaný jako metoda mapperu; vrací počet řádků |
| **Aktualizace** | **(JPA)** změna spravované entity a flush; dirty checking porovnáním se snímkem, s bytecode enhancementem sledováním změn | **(JPA)** změna spravované entity; s weavingem sledování změn na úrovni atributů, bez něj porovnání se záložní kopií při commitu | `<update>` explicitně |
| **Smazání** | **(JPA)** `remove(entity)` — `delete()` v 7.0 zmizelo | **(JPA)** `remove(entity)` | `<delete>` |
| **Model práce** | **(JPA)** jednotka práce nad grafem; flush při commitu | **(JPA)** totéž | jeden příkaz = jedno volání |
| **Kdy se odešle SQL** | **(JPA)** při flush — při commitu, před dotazem, který by mohl být ovlivněn (`FlushModeType.AUTO`), nebo ručně | **(JPA)** totéž | okamžitě, ale v session bez autocommitu; s `ExecutorType.BATCH` až při `flushStatements()` |
| **Dávkování** | `hibernate.jdbc.batch_size` (ve výchozím stavu vypnuté) s `order_inserts`/`order_updates`; IDENTITY dávkování insertů vylučuje | `eclipselink.jdbc.batch-writing=JDBC` (ve výchozím stavu vypnuté), `batch-writing.size` | session s `ExecutorType.BATCH`, nebo `<foreach>` skládající vícenásobný `INSERT` |
| **Získání generovaného klíče** | automatické | automatické | `useGeneratedKeys` nebo `<selectKey>` per příkaz |
| **Hromadné operace bez načtení entit** | **(JPA)** JPQL `UPDATE`/`DELETE` přes `executeUpdate()`; HQL navíc `INSERT … SELECT`; `createMutationQuery` | **(JPA)** JPQL `UPDATE`/`DELETE` | přirozený stav |
| **Optimistické zamykání** | **(JPA)** `@Version` (`int`, `long`, `short`, `Timestamp`, od 3.2 `Instant` a `LocalDateTime`); hibernátovsky `@OptimisticLocking(type = ALL \| DIRTY \| VERSION \| NONE)`, `@OptimisticLock(excluded = true)` | **(JPA)** `@Version`; nativně `@OptimisticLocking(type = ALL_COLUMNS \| CHANGED_COLUMNS \| SELECTED_COLUMNS \| VERSION_COLUMN)` | ruční `WHERE` s verzovacím sloupcem |
| **Pesimistické zamykání** | **(JPA)** `LockModeType.PESSIMISTIC_WRITE` — na SQL Serveru hint `updlock`; 7.1 a 7.2 zamykání rozšířily | **(JPA)** `LockModeType` | hint v SQL |

---

## 14. Transakce

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Explicitní transakce nutná** | **(JPA)** ano pro zápis — v `RESOURCE_LOCAL` `em.getTransaction().begin()`/`commit()`; bez commitu se nic neuloží; `SessionFactory.inTransaction` a od 3.2 `EntityManagerFactory.runInTransaction` jsou obaly | **(JPA)** totéž | `openSession()` je **bez autocommitu** — bez `session.commit()` se změny zahodí; `openSession(true)` zapne autocommit |
| **Zápis** | `sessionFactory.inTransaction(session -> { … })` | `emf.runInTransaction(em -> { … })` | `try (SqlSession s = factory.openSession()) { …; s.commit(); }` |
| **Předání transakce operacím** | implicitní, transakce visí na session | implicitní | implicitní — mappery vzešlé ze session sdílejí její transakci |
| **Úroveň izolace** | globálně `hibernate.connection.isolation`; per transakce jen přes `doWork` na spojení | globálně na `DataSource`; JPA API úroveň nezná | `openSession(TransactionIsolationLevel.READ_COMMITTED)` per session |
| **Vnořené transakce** | nepodporované; savepointy jen na JDBC spojení | nepodporované | nepodporované; jen `session.getConnection()` |
| **Kontejnerové (JTA)** | **(JPA)** `transaction-type="JTA"` v `persistence.xml`; `@Transactional` přes Spring nebo Quarkus | **(JPA)** totéž | `<transactionManager type="MANAGED"/>`, nebo `mybatis-spring` |

---

## 15. Dotazování

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Hlavní API** | HQL přes `createSelectionQuery`; **(JPA)** JPQL přes `createQuery` — HQL je nadmnožina JPQL | **(JPA)** JPQL přes `createQuery`; EclipseLink Query Language (EQL) je jeho nadmnožina | SQL v XML mapperu nebo v `@Select`, volané jako metoda rozhraní |
| **Další API** | **(JPA)** Criteria API s hibernátovskými rozšířeními, nativní SQL (`createNativeQuery`), `@NamedQuery`; Jakarta Data repozitáře a metody `@Find`/`@HQL`/`@SQL` generované `hibernate-processor`; `find`, `findMultiple`, `byNaturalId`; klíčové stránkování `KeyedPage` | **(JPA)** Criteria API, nativní SQL, `@NamedQuery`; nativní `Expression` framework (`ReadAllQuery`, `ExpressionBuilder`); JPA-RS | `SqlSession.selectOne`/`selectList`/`selectMap`/`selectCursor`; provider anotace (`@SelectProvider`) skládající SQL v Javě; MyBatis Dynamic SQL |
| **Množinové operace** | **(JPA)** `UNION`, `INTERSECT`, `EXCEPT` — standardní **až od JPQL 3.2**, v HQL od 6.0 | **(JPA)** totéž; v EQL jako rozšíření už dřív | píše se v SQL |
| **Explicitní LEFT a RIGHT JOIN** | **(JPA)** `LEFT JOIN` standard; `RIGHT JOIN` a JOIN mezi nesouvisejícími entitami s `ON` jen v HQL | **(JPA)** `LEFT JOIN`; JOIN nesouvisejících entit s `ON` jako rozšíření EQL | v SQL |
| **Poddotaz ve `FROM`** | HQL ano | EQL ano | v SQL |
| **Filtry na úrovni modelu** | `@FilterDef`/`@Filter` s `session.enableFilter(…)`; statické `@SQLRestriction` (nahradilo `@Where`, které v 7.0 zmizelo); `@SoftDelete` | `@AdditionalCriteria` (parametrizované vlastnostmi session), `@Multitenant` | žádné; jen ručně vkládané fragmenty `<sql>`/`<include>` |
| **Dynamické SQL** | ne | ne | `<if>`, `<choose>`/`<when>`/`<otherwise>`, `<where>`, `<set>`, `<trim>`, `<foreach>`, `<bind>` s výrazy OGNL; v anotacích přes `<script>` |
| **Parametrizace** | **(JPA)** pojmenované `:id` a poziční `?1`, typované `setParameter` | **(JPA)** totéž | `#{id}` → `?` v `PreparedStatement`; `${id}` je textová substituce; víc parametrů přes `@Param` |
| **Ochrana proti SQL injection** | ano | ano | ano u `#{}`, **ne** u `${}` |
| **Kontrola v době kompilace** | HQL v `@NamedQuery` a `@HQL` ověřuje `hibernate-processor`; Criteria typované metamodelem; ostatní pojmenované dotazy při stavbě továrny | Criteria typované metamodelem; pojmenované JPQL při nasazení, ostatní za běhu | ne; XML se ověří strukturně při stavbě továrny, SQL až za běhu |
| **Uložené procedury** | **(JPA)** `@NamedStoredProcedureQuery`, `createStoredProcedureQuery` | **(JPA)** totéž | `statementType="CALLABLE"` s `{call …}` a `mode=OUT` u parametrů |
| **Materializace do DTO** | **(JPA)** konstruktorový výraz `select new Dto(a.name, count(b))`; `Tuple`; Hibernate i záznamy a `Map` | **(JPA)** konstruktorový výraz, `Tuple` | `resultType="Dto"` — jakákoli třída, nebo `Map` |
| **Více entit z jednoho výsledku** | **(JPA)** `select a, b` → `Object[]`/`Tuple`; `@SqlResultSetMapping` u nativního dotazu | **(JPA)** totéž | vnořený `resultMap` |
| **Asynchronní varianty** | **ne** — JPA je synchronní; Hibernate Reactive je samostatný projekt nad Vert.x | **ne** | **ne**; `selectCursor` streamuje, ale synchronně |
| **Zobrazení SQL před spuštěním** | ne; jen log při spuštění | `query.unwrap(JpaQuery.class).getDatabaseQuery()` po `prepareCall` vrátí `getSQLString()` bez provedení — vyžaduje nasazenou session | **`configuration.getMappedStatement(id).getBoundSql(param).getSql()`** — SQL s `?` po vyhodnocení dynamických značek, **bez databáze** |

Řádek o asynchronním API je největší rozdíl proti .NET trojici, kde asynchronní varianty mají všichni tři; pro převodník to znamená, že javový builder nemá co generovat tam, kde .NET zdroj používá `ToListAsync` — jde o vynechaný tvar, ne o ztracený fakt.

`getBoundSql` je javový protějšek `ToQueryString()` a metodologicky ještě cennější: dává SQL bez běžící databáze **pro konkrétní sadu parametrů**, takže dynamické SQL MyBatisu jde zkoumat jako čistou textovou analýzu. Zároveň je to přesně místo, kde F8 vstupuje do IR: dynamický příkaz je rodina SQL příkazů, z níž `getBoundSql` vrací jednoho člena, a mezireprezentace nemá ani pojem parametru (rozhodnutí [024](../decisions/024-typed-query-operand.md) ho vědomě odložilo). Rozhodnutí [070](../decisions/070-a-parser-refuses-what-would-change-the-row-set.md) tak není jen .NET věc: parametr ve zdroji dnes dotaz odmítá a operand parametru dostane vlastní rozhodnutí až s F8, protože bez něj F8 splnit nejde vůbec.

---

## 16. Načítání souvisejících dat

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Výchozí chování** | **(JPA)** `@ManyToOne` a `@OneToOne` **EAGER**, `@OneToMany` a `@ManyToMany` **LAZY** — asymetrie daná specifikací | **(JPA)** totéž na papíře; líné `@ManyToOne` funguje **jen s weavingem**, bez něj se tiše načte eager | vnořený `select` běží hned (`lazyLoadingEnabled=false`); vnořený `resultMap` je jeden JOIN |
| **Vynucení eager** | **(JPA)** `join fetch`, grafy entit; `@Fetch(JOIN)` | **(JPA)** `join fetch`, grafy entit; `@JoinFetch`, hint `eclipselink.join-fetch` | JOIN v SQL |
| **Vynucení lazy** | `fetch = LAZY` — proxy (třída ne `final`) nebo bytecode enhancement | `fetch = LAZY` + weaving (`-javaagent`, v kontejneru automaticky, nebo `StaticWeave`) | `fetchType="lazy"` na `<association>`/`<collection>` s vnořeným `select` a zapnutým `lazyLoadingEnabled`; proxy Javassist; volání `equals`, `hashCode`, `toString` nebo `clone` spustí načtení (`lazyLoadTriggerMethods`) |
| **Chyba mimo kontext** | `LazyInitializationException` — stejná chyba i název jako u NHibernate | **žádná** — indirekce načte přes session továrny i na odpojené entitě, dokud továrna žije | `ExecutorException: Executor was closed` po zavření session |
| **Problém N+1** | reálný u výchozích líných kolekcí; `@BatchSize`, `@Fetch(SUBSELECT)`, `hibernate.default_batch_fetch_size` | reálný; `@BatchFetch(JOIN \| EXISTS \| IN)`, hint `eclipselink.batch` | vnořený `select` je N+1 z definice; řešením je JOIN s vnořeným `resultMap` |
| **Duplicitní řádky z JOINu** | řeší framework — od 6.0 kořenové entity z `join fetch` vždy deduplikované, `distinct` jde až do SQL | **řeší autor** — `join fetch` kolekce vrací duplicitní rodiče, nutné `SELECT DISTINCT` | řeší framework podle `<id>` ve vnořeném `resultMap` |
| **Rozdělení do víc dotazů** | `@BatchSize`, subselect | `@BatchFetch(IN)` | vnořený `select` |

Weaving je pro EclipseLink to, co bytecode enhancement pro Hibernate, s tím rozdílem, že bez něj se `fetch = LAZY` na `@ManyToOne` tiše ignoruje. Pro převodník je to fakt o nasazení, ne o doméně — strategie načítání do IR nepatří (článek §5.4) —, ale patří do profilu cíle jako varování: vygenerovaný EclipseLink kód se bez agenta chová jinak, než jeho anotace slibují.

---

## 17. Identita objektů a cache

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Identity map** | **(JPA)** persistence context — dva dotazy na tentýž řádek vrátí **tutéž instanci** | **(JPA)** totéž | **ne** jako identity map; lokální cache session je klíčovaná příkazem a parametry, takže **týž dotaz** vrátí tutéž instanci, jiný dotaz na týž řádek jinou (`localCacheScope=SESSION`; `STATEMENT` to vypne) |
| **Vypnutí sledování** | `StatelessSession`, `session.setDefaultReadOnly(true)`, `Query.setReadOnly(true)` | hint `eclipselink.read-only`, `@ReadOnly` na entitě | výchozí a jediný stav |
| **Cache druhé úrovně** | **vypnutá**; providery přes JCache (`hibernate-jcache`) nebo Infinispan, `@Cacheable` a `@Cache(usage = …)` per entita a kolekce; `StatelessSession` ji od 7.0 používá | **zapnutá ve výchozím stavu** — sdílená cache objektů na továrnu (`eclipselink.cache.shared.default=true`), `@Cache(type, size, expiry)`, `@Cacheable(false)` pro vypnutí; koordinace cache mezi uzly (JMS, RMI) | `<cache/>` per jmenný prostor mapperu, bez deklarace nic; LRU, 1024 položek, `readOnly="false"` vrací serializované kopie (objekty musí být `Serializable`); každý zápis v prostoru ji vyprázdní |
| **Cache dotazů** | `setCacheable(true)` s `hibernate.cache.use_query_cache` | hint `eclipselink.query-results-cache` | `<cache/>` **je** cache výsledků dotazů, ne entit |
| **Dopad na sémantiku** | změna entity detekovaná při flush | totéž; se zapnutou sdílenou cache riziko zastaralých dat při zápisu mimo EclipseLink | žádný — UPDATE se píše explicitně |

Zapnutá sdílená cache EclipseLinku je nejznámější rozdíl mezi oběma implementacemi v provozu: totéž JPA mapování se chová jinak, jakmile do databáze zapisuje ještě někdo jiný. Do IR nepatří, do profilu cíle a do textu práce ano.

---

## 18. Diagnostika a introspekce

| | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Zobrazení generovaného SQL** | log při běhu (`show_sql`, logger `org.hibernate.SQL`) | log při běhu (`eclipselink.logging.level.sql=FINE`); `getSQLString()` po přípravě dotazu | log při běhu; **staticky `getBoundSql`** |
| **Hodnoty parametrů v logu** | logger `org.hibernate.orm.jdbc.bind` na TRACE (kategorie platí od 6.0; starší `org.hibernate.type.descriptor.sql` nefunguje) | `eclipselink.logging.parameters=true` | řádek `Parameters:` na DEBUG |
| **Introspekce modelu** | **(JPA)** `Metamodel` — atributy a typy, bez sloupců; hibernátovsky `MappingMetamodel`/`EntityPersister` (tabulka, sloupce) a boot-time `Metadata` s `PersistentClass`, `Table`, `Column` — **plná** | **(JPA)** `Metamodel`; nativně `ClassDescriptor` přes `em.unwrap(JpaEntityManager.class).getServerSession().getDescriptor(…)` — tabulky, `DatabaseMapping` s `DatabaseField` — **plná** | `Configuration.getResultMap(id).getResultMappings()` (sloupec, vlastnost, `javaType`, `jdbcType`) a `getMappedStatements()` — **mapování ano, schéma ne**; víc než Dapper |
| **Statistiky** | `hibernate.generate_statistics` a `Statistics` — počty dotazů, cache hit ratio | `eclipselink.profiler=PerformanceMonitor` | žádné |
| **Interceptory** | `Interceptor`, event listenery, `StatementInspector` pro přepis SQL | `SessionEventListener`, `DescriptorEventListener`, `QueryRedirector` | pluginy `@Intercepts` nad `Executor`, `StatementHandler`, `ParameterHandler`, `ResultSetHandler` |
| **Testcontainers** | ano, modul `mssqlserver` | ano | ano |

Postup „vypsat model, zakomentovat část mapování, vypsat znovu" z .NET dokumentu má v Javě dvě cesty. U obou implementací JPA je to standardní generování DDL do skriptu (§11): rozdíl dvou skriptů je přesně množina faktů doplněných konvencemi, a protože mechanismus je standardní, dají se tak porovnat i **defaulty obou implementací mezi sebou** — nejpřímější způsob, jak doložit §4 a §6. U MyBatisu je to výpis `Configuration` po sestavení továrny.

---

## 19. Kdy se projeví chyba

| Druh chyby | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| **Překlep v názvu atributu v mapování** | v anotaci nemůže nastat; v `mappedBy`, `orm.xml` nebo JPQL při stavbě továrny (`AnnotationException`, `hibernate.query.startup_check`) | v `mappedBy` nebo `orm.xml` při nasazení (`ValidationException`) — ve výchozím stavu až u prvního `EntityManager`, při startu jen s `eclipselink.deploy-on-startup=true` | v `resultMap` při stavbě továrny (`ReflectionException: There is no setter for property …`); v `#{}` až za běhu |
| **Překlep v názvu sloupce** | runtime, při dotazu | runtime, při dotazu | v `resultMap` runtime (`The column name … is not valid`); u automatického mapování **tiše** — vlastnost zůstane prázdná |
| **Chybějící registrace entity** | runtime — `Not an entity` / `Unknown entity` při dotazu | runtime — totéž | `BindingException: Type … is not known to the MapperRegistry` při `getMapper` |
| **Nenačtený mapovací soubor** | `orm.xml` mimo výchozí `META-INF/orm.xml` musí být v `persistence.xml`; jinak se tiše nepoužije | totéž | **runtime** — `Invalid bound statement (not found)` až při volání metody, XML mapper nikdo dřív nehledá |
| **Chybějící bezparametrický konstruktor** | při materializaci; od 7.3 ho enhancer dogeneruje | při nasazení | při materializaci, pokud nesedí konstruktorové mapování |
| **`final` třída** | varování při stavbě továrny, proxy vypnuté | weaving třídu přeskočí, líné načtení a sledování změn odpadnou | nemůže nastat |
| **Nesoulad modelu a schématu** | runtime, SQL chyba; případně `validate` | runtime, SQL chyba | runtime, `Invalid column name` |
| **Přejmenování atributu v Javě** | anotace se přesunou s atributem; rozbijí se řetězce `mappedBy` a JPQL → stavba továrny nebo runtime | totéž → nasazení nebo runtime | `property` v `resultMap` → stavba továrny; `#{}` v SQL → runtime |
| **Chybný `<id>` ve vnořeném `resultMap`** | nerelevantní | nerelevantní | tiše špatně složené objekty — protějšek chybného `splitOn` |

Obecná tendence: Hibernate odhalí nejvíc při stavbě továrny, EclipseLink totéž, ale ve výchozím stavu o krok později (při prvním `EntityManager`), MyBatis polovinu při stavbě továrny (struktura XML, názvy vlastností) a polovinu až při prvním volání (chybějící příkaz, názvy sloupců) — a část vůbec, tichým nenaplněním. To je tatáž vrstva jako u Dapperu, jen posunutá o jeden stupeň dřív díky `resultMap`.

---

## 20. Souhrn expresivity

Klíčová tabulka pro návrh IR a pro diagnostiku podle rozhodnutí [004](../decisions/004-unexpressible-facts-as-warnings.md) — javový protějšek §20 .NET dokumentu, se stejnými řádky a třemi navíc pro fakty, které mají jen javové frameworky.

| Fakt o doméně | Hibernate 7.4.5 | EclipseLink 5.0.0 | MyBatis 3.5.19 |
|---|---|---|---|
| Název tabulky | `@Table` **(JPA)** | `@Table` | **jen v SQL řetězcích** |
| Název sloupce | `@Column(name)` **(JPA)** | `@Column(name)` | `<result column>` — **deklarativně**, nebo alias `AS` |
| Datový typ | `@Column(length, precision, scale, columnDefinition)` **(JPA)**, `@JdbcTypeCode` | `@Column(…)`, konvertory | `javaType` a `jdbcType` — rodina JDBC, **délka jen v DDL** |
| Nullabilita | `nullable`, `@Basic(optional)` **(JPA)** | totéž | **jen v DDL** |
| Primární klíč | `@Id` **(JPA)** | `@Id` | `<id>` jen jako příznak identity, `keyProperty` jen u insertu |
| Kompozitní klíč | `@IdClass`, `@EmbeddedId` s klíčovou třídou **(JPA)** | totéž | víc `<id>` |
| Strategie generování klíče | `@GeneratedValue` **(JPA)**, `AUTO` = sekvence | `@GeneratedValue`, `AUTO` = tabulka | `useGeneratedKeys`, `<selectKey>` — per příkaz |
| Alternativní klíč | `unique`, `@UniqueConstraint` **(JPA)**, `@NaturalId` | `@UniqueConstraint`, `@CacheIndex` | **jen v DDL** |
| Index | `@Index` v `@Table(indexes)` **(JPA)** | totéž, plus nativní `@Index` | **jen v DDL** |
| Relace 1:N | `@OneToMany(mappedBy)` **(JPA)** | totéž | `<collection>` — tvar výsledku, **ne cizí klíč** |
| Relace N:1 | `@ManyToOne` s `@JoinColumn` **(JPA)** | totéž | `<association>` — totéž omezení |
| Relace N:M | `@ManyToMany` s `@JoinTable` **(JPA)** | totéž | **jen v JOIN klauzulích** |
| Vícesloupcový FK | `@JoinColumns` **(JPA)** | totéž | v `ON` klauzuli |
| Vlastník vztahu | `mappedBy` **(JPA)** | `mappedBy` | nerelevantní |
| Kaskádové mazání | `cascade`, `orphanRemoval` **(JPA)**; DDL přes `@OnDelete` | `cascade`, `orphanRemoval`; `@PrivateOwned`, `@CascadeOnDelete` | **jen v DDL** |
| Dědičnost | `@Inheritance` se třemi strategiemi **(JPA)** | totéž | `<discriminator>` — jen volba typu výsledku |
| Identita entity | persistence context **(JPA)** | totéž | **neexistuje** |
| Verzování a concurrency | `@Version` **(JPA)**, `@OptimisticLocking` | `@Version`, `@OptimisticLocking` | ruční `WHERE` |
| Výchozí hodnota sloupce | `@ColumnDefault` | jen `columnDefinition` | **jen v DDL** |
| Počítaný sloupec | `@Formula` | **jen nativním deskriptorem** | v SELECT |
| Filtr na úrovni modelu | `@FilterDef`/`@Filter`, `@SQLRestriction`, `@SoftDelete` | `@AdditionalCriteria`, `@Multitenant` | **nikde** |
| Způsob přístupu k hodnotě | umístění `@Id`, `@Access` **(JPA)** | totéž | nerelevantní |
| Nationalizace řetězce | `@Nationalized` (atribut, balíček), globální přepínač | jen `columnDefinition` | **jen v DDL** |
| Dynamicky složený dotaz | ne | ne | `<if>`, `<foreach>` a další |

Řádky, kde má MyBatis hodnotu **nikde** nebo **jen v DDL**, jsou seznam varování, která MyBatis builder bude emitovat z deskriptoru cíle (rozhodnutí [009](../decisions/009-target-framework-descriptor.md)) — a je kratší než u Dapperu. Název sloupce, příznak identity, tvar vnoření a volba typu podle diskriminátoru mají v MyBatisu deklarativní místo, takže „neúplné mapování" z F6 je u MyBatisu méně neúplné než u Dapperu. Asymetrie mezi oběma mapper frameworky je jednostranná: Dapper → MyBatis nic nevymýšlí, MyBatis → Dapper ztrácí celý `resultMap` a jediné, kam ho složit, je alias v SQL — zrcadlo nálezu „alias v SQL jako zdroj mapování Dapperu" z [`open-items.md`](../open-items.md).

Řádky označené **(JPA)** jsou sdílená vrstva. Kde se Hibernate a EclipseLink liší, je to buď implementačně specifická anotace (`@Formula`, `@NaturalId`, `@AdditionalCriteria`) — ta se v překladu mezi nimi hlásí záznamem stejně jako mezi NHibernate a EF Core —, nebo **jiný default za touž anotací** (`AUTO`, velikost písmen v názvech, cache); ty druhé jsou nebezpečnější, protože v textu artefaktu nejsou vidět.

---

## 21. Společná podmnožina

Následující vlastnosti sdílejí všechny tři frameworky. IR je proto nemusí modelovat:

- Běží na JDK 25, s minimy 17, 17 a 8, a instalují se z Maven Central jako běžné závislosti; zdroje v `src/main/resources` jsou na classpath bez zásahu do build souboru.
- Pod kapotou stojí na JDBC a v tomto projektu na SQL Serveru 2022 přes týž `mssql-jdbc` 13.4.0. JDBC URL, `encrypt=true` jako výchozí i nutnost `trustServerCertificate=true` jsou proto identické; stejně tak to, že ovladač mluví jen TCP a na SQL Server Express s vypnutým TCP/IP se nepřipojí — past, kterou .NET trojice nezná.
- Pracují s obyčejnými třídami. Žádný nevyžaduje dědění od bázové třídy ani implementaci rozhraní; JPA žádá bezparametrický konstruktor a „ne `final`", MyBatis nic.
- Parametrizují dotazy a tím chrání proti SQL injection — u MyBatisu s výhradou `${}`.
- **Žádný nemá asynchronní API.** V .NETu ho mají všichni tři.
- Nechávají volbu izolační úrovně na JDBC nebo na uživateli a vnořené transakce neumějí.
- Žádný nevytvoří databázi, jen tabulky.
- XML je u všech tří plnohodnotná forma mapování (`orm.xml`, `eclipselink-orm.xml`, XML mapper). V .NETu ji má jen NHibernate.
- Jsou open source, dva pod Apache 2.0 a jeden pod EPL/EDL, a mají srovnatelně dlouhou historii produkčního nasazení — všechny tři sahají před rok 2005.

Výhrada, která u .NETu neplatila: **nic z toho není podložené kódem v repozitáři.** Benchmarková sada `ORMComparison.sln` má jen .NET větev, javový testovací projekt neexistuje (vyňatá oblast 6 hranice záruk, [`architecture.md`](../architecture.md) §9) a z trojice jsme spustili jen Hibernate. Tvrzení o EclipseLinku a MyBatisu jsou z dokumentace zafixovaných verzí; tam, kde dokumentace mlčí — hodnota `AUTO` v EclipseLinku, chování MyBatisu při stejném id v anotaci a v XML —, jsou z chování implementace doloženého mimo repozitář a je to u položky řečeno.

---

## 22. Metodické poznámky

**Pokrytí v repozitáři.** Žádné. Z tutoriálů existuje jen Hibernate; díly pro MyBatis a EclipseLink ve stejné doméně a se stejným číslováním kroků jsou nejlevnější cesta, jak tabulky výš doložit během — a EclipseLink díl narazí hned v kroku 6 na weaving (§16) a v kroku 8 na velká písmena v DDL (§4). Kompozitní klíče, dědičnost, alternativní klíče a verzování bude nutné napsat od nuly stejně jako v .NETu.

**Verzování zjištění je nutné, ne kosmetické.** Tento dokument nese čtyři případy, kdy by tvrzení bez verze bylo nepřesné:

- `Session.save`, `update`, `saveOrUpdate` a `delete` — existují v 6.x, v 7.0 odstraněné; builder pro Hibernate, který by je vypsal, generuje kód, který se proti 7.4.5 nezkompiluje
- `UNION`, `INTERSECT` a `EXCEPT` v JPQL — standardní až od 3.2; na JPA 3.1 by šlo o hibernátovské, resp. EclipseLinkové rozšíření a sdílená vrstva by je vyslovit nemohla
- `@Nationalized` na úrovni balíčku — až od Hibernate 7.4
- MyBatis 3.5.19 — poslední vydání pro Javu 8; řada 3.6 mění minimum

**Dvě čísla verze na framework.** U Hibernate a EclipseLinku nestačí verze implementace: úroveň specifikace určuje, co je sdílená vrstva, a verze implementace, co je nad ní. Deskriptor cíle nese dnes jediné číslo (rozhodnutí [013](../decisions/013-target-framework-versions.md)); profil javového cíle bude potřebovat obě, vedle pojmenovací strategie a nationalizačního režimu z tutoriálu — a podle §4 a §5 i implementaci samu jako klíč pro defaulty.

**Ground truth je generované SQL.** Java je pro textovou analýzu vybavená lépe než .NET: DDL bez databáze dávají obě implementace JPA standardním mechanismem, a MyBatis dá `getBoundSql` SQL konkrétního volání bez databáze. Pro citovatelný důkaz v textu práce je nejsilnější vygenerovaný DDL a vygenerovaný dotaz, ne popis API; a rozdíl dvou DDL ze stejných anotací je nejsilnější důkaz o defaultech implementací.

**Cílové verze javových ORM nedeklaruje zatím nic než tabulka v [`architecture.md`](../architecture.md).** Javový wrapper ani jeho deskriptor neexistují, takže na rozdíl od .NET trojice není verze vydávaná za běhu ani vázaná testem na balíčky ověřovacího stupně. Kudy javová strana do řešení vstupuje, je položka „Na řadě" v [`open-items.md`](../open-items.md); tenhle dokument je jejím podkladem vedle tutoriálu k Hibernate.
