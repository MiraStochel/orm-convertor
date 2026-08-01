# Srovnání ORM frameworků: NHibernate, EF Core a Dapper

Tento dokument shrnuje srovnání tří .NET ORM frameworků, které převodník zpracovává. Slouží jako podklad pro analytickou část práce a jako vstup pro návrh mezireprezentace (IR).

Srovnání je organizované tematicky, ne po frameworcích, protože takto je členěná i analytická kapitola. U každého tématu sleduji tři osy: **co lze vyjádřit** (expresivita), **jak se to vyjadřuje** (syntax) a **co platí implicitně** (defaulty, které parser musí materializovat).

Řádky, kde se všechny tři frameworky shodují, jsou v tabulkách ponechané záměrně — společná podmnožina vymezuje, co IR modelovat nemusí, a je pro návrh stejně důležitá jako rozdíly.

## Zafixované verze

Všechna tvrzení v dokumentu platí pro následující verze. Tam, kde je schopnost vázaná na konkrétní verzi frameworku, je to uvedeno přímo u dané položky.

| Komponenta | Verze |
|---|---|
| .NET | **10** (`net10.0`) |
| NHibernate | **5.7.0** |
| Microsoft.EntityFrameworkCore(.SqlServer) | **10.0.10** |
| Dapper | **2.1.79** |
| Microsoft.Data.SqlClient | **7.0.2** |
| System.Data.SqlClient (pouze `benchmarks/Common`) | 4.9.1 |
| Microsoft.CodeAnalysis.CSharp (Roslyn) | 5.6.0 |
| SQL Server | **2022** (`mcr.microsoft.com/mssql/server:2022-latest` v obou Dockerfilech) |

Tři skutečnosti o těchto verzích mají dopad na obsah tabulek:

- **NHibernate 5.7.0** je první verze, která cílí přímo na .NET 10. Přineslo to úpravy kvůli jazykové funkci C# 14 „first-class span types" a s ní související změny v serializaci.
- **EF Core 10.0.10** je LTS vydání (listopad 2025, podpora do listopadu 2028). Vyžaduje .NET 10 SDK i runtime a neběží na starším .NETu ani na .NET Framework.
- **SQL Server je 2022, ne 2025.** Několik novinek EF Core 10 — nativní typ `json`, typ `vector` a funkce `VECTOR_DISTANCE()` — je vázaných na SQL Server 2025 nebo Azure SQL. V tomto prostředí tedy nejsou dostupné a do srovnání patří pouze jako poznámka.

---

## 1. Identita frameworků

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Kategorie** | plnohodnotný ORM s mapováním, správou identity a generováním SQL | plnohodnotný ORM s mapováním, správou identity, generováním SQL a migracemi | micro-ORM; materializátor výsledků a binder parametrů |
| **Původ** | port Javovského Hibernate; první vydání ~2005; koncepty i názvy převzaté z Hibernate téměř 1:1 | Microsoft; EF Core 1.0 v roce 2016 jako přepis staršího Entity Framework 6 | Stack Overflow, ~2011; vzniklo jako řešení výkonnostních problémů vlastního provozu |
| **Správa a kadence** | komunitní projekt, vydání nepravidelně | Microsoft, součást .NET cyklu; velké verze každý listopad, čísla sledují .NET; 10.0 je LTS s podporou do 11/2028, příští stabilní EF 11 v 11/2026 | Stack Overflow a komunita |
| **Licence** | LGPL 2.1 | MIT | Apache 2.0 |
| **Cílové frameworky balíčku** | .NET 10 (nově v 5.7.0), .NET Framework 4.6.1, .NET Standard 2.0 a další starší | **výhradně .NET 10**; neběží na starším .NETu ani na .NET Framework | .NET Standard 2.0 a výš; běží prakticky všude |
| **Filozofie** | doménový model je primární, databáze je detail; framework se snaží databázi maximálně skrýt | doménový model je primární, ale s explicitnějším přiznáním relační vrstvy; konvence šetří psaní | SQL je primární a viditelné; framework řeší jen hranici mezi ADO.NET a objekty |
| **Co framework ví o schématu** | vše, co je v mapování — tabulky, sloupce, klíče, relace, dědičnost, typy | totéž, plus historii schématu přes migrace | nic |
| **Role v převodníku** | zdrojový i cílový framework; přímý protějšek Hibernate pro plánovanou Java větev | zdrojový i cílový framework | zdrojový i cílový framework; nejslabší článek každého převodního cyklu |

---

## 2. Instalace a struktura projektu

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **NuGet balíčky** | `NHibernate` 5.7.0 + `Microsoft.Data.SqlClient` 7.0.2 | `Microsoft.EntityFrameworkCore.SqlServer` 10.0.10 | `Dapper` 2.1.79 + `Microsoft.Data.SqlClient` 7.0.2 |
| **Proč provider ručně** | driver si `Microsoft.Data.SqlClient` načítá reflexí; bez reference v projektu se assembly nezkopíruje do outputu a vznikne runtime chyba | není třeba, přijde tranzitivně přes provider balíček | `SqlConnection` se instancuje přímo v kódu |
| **Tranzitivní závislosti** | `System.Configuration.ConfigurationManager`, `Remotion.Linq` (LINQ provider), `Antlr3.Runtime` (parser HQL) | `Microsoft.EntityFrameworkCore`, `.Relational`, `.Abstractions`, `Microsoft.Data.SqlClient` | žádné podstatné; Dapper je jediná assembly bez závislostí mimo BCL |
| **Závislost entit na frameworku** | vyžaduje jen jazykové konstrukce, ale mapování je vázané na assembly a namespace | žádná u fluent API; s data annotations závislost na `System.ComponentModel.DataAnnotations` | žádná — `DapperEntities.csproj` v benchmarcích má nulový počet `PackageReference` |
| **Zásah do `.csproj`** | nutný — `<EmbeddedResource Include="Mappings\**\*.hbm.xml" />`, jinak se mapování nenačte | žádný nad rámec `PackageReference` | žádný |
| **Doplňkové balíčky v ekosystému** | Fluent NHibernate (fluent mapování), NHibernate.Caches.* (cache 2. úrovně) | `.Design` a `.Tools` pro migrace, `.Proxies` pro lazy loading, `.InMemory` pro testy | Dapper.Contrib, Dapper.SimpleCRUD, Dapper.FluentMap — samostatné balíčky, ne součást Dapperu |

Dapper.Contrib zavádí atributy `[Table]`, `[Key]`, `[ExplicitKey]` a metody `Get`/`Insert`/`Update`. Zdrojový projekt používající Contrib by nesl podstatně víc informací než holý Dapper. Zbývá rozhodnout, jestli to spadá do rozsahu práce, nebo je explicitně mimo něj.

---

## 3. Požadavky na doménové třídy

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **`virtual` na mapovaných členech** | **povinné**; framework generuje za běhu proxy potomka a bez `virtual` nemůže přístup zachytit. Chyba: `method get_X should be virtual` | nepovinné; change tracking jede přes snapshot stavu, ne přes intercept. Povinné se stane při použití `Microsoft.EntityFrameworkCore.Proxies` | nepovinné, framework s objektem po jeho vytvoření nic nedělá |
| **Bezparametrický konstruktor** | **povinný**, stačí `protected`; proxy ho potřebuje | nepovinný; umí materializovat konstruktorem s parametry, pokud názvy odpovídají vlastnostem | nepovinný pro `Query<T>`; Dapper umí i konstruktorovou materializaci |
| **Přístupnost setterů** | může být `protected`, mapování umí `access="field"` a další strategie | `private set` funguje; umí zapisovat přímo do backing fieldu | setter musí být přístupný, nebo musí sedět konstruktor |
| **Modifikátor třídy** | nesmí být `sealed` (kvůli proxy) | může být `sealed` | může být `sealed` |
| **Vlastnosti pro cizí klíč** | nemapují se samostatně — buď `<many-to-one>`, nebo `<property>`, ne obojí na týž sloupec | volitelně obojí: navigační vlastnost i skalární FK. Bez FK vlastnosti vznikne stínová (shadow) vlastnost v modelu | jen holý FK sloupec; navigační vlastnost by nikdy nebyla naplněná |
| **Kolekce** | `ISet<T>`, `IList<T>`, `IDictionary<K,V>`, `ICollection<T>`; typ kolekce určuje element mapování (`<set>`, `<bag>`, `<list>`, `<map>`) a má sémantický význam | `ICollection<T>` a potomci; typ kolekce nemá vliv na mapování | libovolná kolekce; framework ji nikdy nenaplní sám |
| **Nullable reference types** | bez vlivu na mapování; nullabilitu určuje `not-null` v XML | **má vliv** — `string` je NOT NULL, `string?` je NULL | bez vlivu; nullabilita je jen v DDL |

**Důsledek pro IR.** Řádky „požadavky na entitu" jsou fakta, která nelze přenést ani odvodit — jsou to omezení cílového frameworku, ne informace o doméně. Builder je musí generovat automaticky (doplnit `virtual` při výstupu do NHibernate), parser je musí ignorovat (nepovažovat `virtual` za fakt o doméně).

---

## 4. Kde žije mapování

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Primární forma** | XML soubor `*.hbm.xml`, jmenný prostor `urn:nhibernate-mapping-2.2` | fluent API v `OnModelCreating(ModelBuilder)` | aliasy `AS` uvnitř SQL řetězců |
| **Alternativní formy** | mapping by code (`ModelMapper`, součást NHibernate), Fluent NHibernate (externí balíček) | data annotations na entitě; `IEntityTypeConfiguration<T>` jako samostatná třída na entitu | `SqlMapper.SetTypeMap` s `CustomPropertyTypeMap` (globální, reflexní); Dapper.FluentMap |
| **Počet vrstev** | jedna — co není v mapování, neexistuje (kromě názvových defaultů, viz níž) | **tři, skládají se v pořadí** konvence → data annotations → fluent API; pozdější přebíjí dřívější | žádná; existuje jen shoda názvů |
| **Umístění vůči doméně** | mimo třídu, mimo assembly jako zdroj — kompiluje se dovnitř jako resource | mimo třídu (fluent) nebo na třídě (annotations) | rozptýleně po celém kódu, v každém dotazu zvlášť |
| **Kontrola v době kompilace** | žádná — XML se validuje až při `AddAssembly`; překlep v názvu vlastnosti je runtime chyba | plná u fluent API (lambda výrazy jsou typované); u `HasColumnName` je řetězec nekontrolovaný | žádná; SQL je řetězec |
| **Deklarativní místo pro mapování** | ano, jedno | ano, jedno | **neexistuje** |
| **Nutná explicitní konfigurace pro funkční model** | ano, kompletní; bez mapování `No persister for: ...` | ne, konvence stačí na běžný model | nerelevantní — není co konfigurovat |
| **Podpora v IDE** | pro `hbm.xml` ve Visual Studiu IntelliSense obvykle nefunguje (schéma není registrované); na běh to nemá vliv | plná | pro SQL řetězce žádná |

### Názvové defaulty

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Tabulka** | při vynechání `table` se použije **název třídy** (bez pluralizace) | při vynechání `ToTable` se použije **název `DbSet` vlastnosti**; bez `DbSet` název typu | není pojem tabulky |
| **Sloupec** | při vynechání `column` se použije název vlastnosti | název vlastnosti | název sloupce musí odpovídat názvu vlastnosti |
| **Porovnávání názvů** | přesné | přesné | **case-insensitive, podtržítka se ignorují** (`stock_item_id` se namapuje na `StockItemId`) |

**Důsledek pro IR.** Toto je jádro třetí osy analýzy. Parser musí defaulty materializovat — z `<property name="Name" />` odvodit sloupec `Name`, z `DbSet<Author> Authors` odvodit tabulku `Authors`. Bez toho by IR neslo neúplnou informaci a builder do jiného frameworku by odvodil jiné jméno (`Author` místo `Authors`), protože defaulty se mezi frameworky liší.

---

## 5. Primární klíče

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Jednoduchý klíč, deklarace** | `<id name="Id" column="AuthorId" type="int">` s vnořeným `<generator>` | konvence (vlastnost `Id` nebo `<Typ>Id`), nebo `[Key]`, nebo `HasKey(a => a.Id)` | **nedeklaruje se nikde** |
| **Kompozitní klíč, deklarace** | `<composite-id>` s vnořenými `<key-property>` nebo `<key-many-to-one>` | `HasKey(t => new { t.BookId, t.LanguageCode })`; nebo `[PrimaryKey(nameof(A), nameof(B))]` — atribut existuje od EF Core 7, v 10.0.10 je tedy dostupný | v ručním SQL, ve `WHERE` klauzuli |
| **Požadavek na klíčovou třídu** | `<composite-id>` může použít samostatnou třídu (atribut `class`) nebo mapovat přímo vlastnosti entity; **v obou případech vyžaduje override `Equals` a `GetHashCode`**, protože identita entity je odvozená od hodnoty klíče | žádný; klíč je jen množina vlastností, žádná zvláštní třída, žádný požadavek na `Equals` | žádný |
| **Generování hodnoty** | `<generator class="...">`: `identity`, `native`, `sequence`, `hilo`, `seqhilo`, `guid`, `guid.comb`, `uuid.hex`, `assigned`, `foreign`, `increment` | konvence pro `int` PK → IDENTITY; explicitně `ValueGeneratedOnAdd()`, `UseIdentityColumn()`, `UseHiLo()`, `UseSequence()`, `HasDefaultValueSql()` | `SCOPE_IDENTITY()` napsaný ručně a přiřazený do vlastnosti ručně |
| **Naplnění klíče po insertu** | automatické; framework ví, který sloupec je klíč a že je generovaný | automatické | **ruční** — `QuerySingle<int>` nad `SELECT CAST(SCOPE_IDENTITY() AS INT)` |
| **Entita bez klíče** | `<class>` klíč vyžaduje; pro readonly výsledky `<sql-query>` s `<return-scalar>` nebo dotaz na DTO | `HasNoKey()` — plnohodnotný keyless entity type (v benchmarcích u `PurchaseOrderUpdate`) | přirozený stav — každý dotaz vrací cokoli |
| **Alternativní a unikátní klíče** | `<natural-id>`, případně `unique="true"` na `<property>` | `HasAlternateKey(...)`, `HasIndex(...).IsUnique()` | v DDL |

**Důsledek pro IR.** Nejostřejší rozdíl je v tom, že u NHibernate a JPA kompozitní klíč táhne s sebou sémantiku identity — vyžaduje `Equals` a `GetHashCode`, případně samostatnou klíčovou třídu. U EF Core je to čistě množina sloupců. Otevřená otázka: stačí `PrimaryKeyPart` jako seřazený seznam sloupců, nebo IR potřebuje volitelný koncept „klíčové třídy", aby builder pro NHibernate a JPA uměl třídu syntetizovat a doplnit `Equals`? Bez toho bude výstup do NHibernate nekompilovatelný, respektive sémanticky vadný.

---

## 6. Sloupce, typy a nullabilita

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Deklarace sloupce** | `<property name="Title" column="Title" type="string" length="300" not-null="true" />` | `Property(b => b.Title).HasColumnName("Title").HasMaxLength(300).IsRequired()` | jen alias `AS` v SELECT |
| **Typový systém** | **vlastní vrstva NHibernate typů** (`string`, `AnsiString`, `DateTime`, `Timestamp`, `Decimal`, `Binary`, `Guid`, …), která stojí mezi CLR typem a SQL typem; lze psát vlastní `IUserType` | mapování CLR typ → SQL typ přes provider; `HasColumnType("...")` pro explicitní SQL typ; `ValueConverter` pro převod hodnot; `ValueComparer` pro porovnávání | výchozí mapování ADO.NET; `SqlMapper.AddTypeHandler` pro vlastní typy |
| **`DateTime` v DDL** | `datetime2` — dialekty pro SQL Server 2008+ ho používají od NHibernate 5.0.0; návrat k `datetime` je možný nastavením `sql_types.keep_datetime` | `datetime2` | v DDL, volba je na autorovi schématu |
| **`DateOnly` a `TimeOnly`** | podporované **až od 5.7.0**; ve verzi 5.5.2 nikoli | podporované | přes ADO.NET a provider |
| **Délka řetězce** | `length="300"` | `HasMaxLength(300)` | v DDL |
| **Přesnost decimal** | `precision="18" scale="2"`; maximální přesnost je pro většinu dialektů 28, což odpovídá limitu .NET `decimal`; hodnoty nad ni se ořežou | `HasPrecision(18, 2)` | v DDL |
| **Nullabilita** | `not-null="true"`; výchozí stav je nullable | z nullable reference types a `?` u hodnotových typů; přebít lze `IsRequired()` / `IsRequired(false)` | v DDL |
| **Výchozí hodnota** | `<property>` nemá přímý ekvivalent; řeší se přes `default` v DDL nebo v konstruktoru | `HasDefaultValue(...)`, `HasDefaultValueSql(...)` | v DDL |
| **Počítané sloupce** | `formula="..."` na `<property>` | `HasComputedColumnSql(...)` | v SELECT |
| **Ignorování vlastnosti** | prostě se nenamapuje | `Ignore(...)` nebo `[NotMapped]` | nerelevantní |
| **Vlastní SQL typ** | `sql-type` na `<column>` | `HasColumnType("...")` | v DDL |
| **Kdo rozhoduje o SQL typu** | dialekt na základě NHibernate typu a délky | provider na základě CLR typu a anotací | autor DDL |
| **Nativní typ `json`** | ne | podporovaný v EF Core 10, ale vyžaduje SQL Server 2025 nebo Azure SQL — v tomto prostředí (SQL Server 2022) nedostupné | ne |
| **Typ `vector`** | ne | podporovaný v EF Core 10 včetně `VECTOR_DISTANCE()`, vyžaduje SQL Server 2025 nebo Azure SQL — zde nedostupné | ne |

**Důsledek pro IR.** Zde je vidět problém, který čeká Java větev. NHibernate má vlastní typovou vrstvu mezi CLR a SQL — `type="string"` není `System.String`. EF Core mapuje CLR typ přímo. IR dnes drží CLR typ; jakmile přibude Java, ani jedno nebude stačit a bude potřeba jazykově neutrální reprezentace typu, kterou JSS článek označuje jako `LangType`. Typová vrstva NHibernate je přitom docela dobrý model toho, jak taková neutrální vrstva může vypadat.

Podpora `DateOnly` a `TimeOnly` je zároveň ukázkou, proč je nutné u každého zjištění evidovat verzi: na verzi 5.5.2 by tato buňka vyšla jako „nelze vyjádřit", na 5.7.0 vychází jako podporovaná.

---

## 7. Relace 1:N a N:1

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Deklarace strany „jedna"** | `<set name="Books" inverse="true"><key column="AuthorId" /><one-to-many class="Book" /></set>` | `HasMany(a => a.Books).WithOne(b => b.Author)` | neexistuje |
| **Deklarace strany „mnoho"** | `<many-to-one name="Author" column="AuthorId" class="Author" not-null="true" />` | `HasOne(b => b.Author).WithMany(a => a.Books).HasForeignKey(b => b.AuthorId)` | neexistuje |
| **Určení vlastníka vztahu** | `inverse="true"` na kolekci znamená, že FK zapisuje druhá strana; bez toho pošle NHibernate po insertu zbytečný `UPDATE` | odvozeno z toho, kde je FK vlastnost; není to samostatný pojem | nerelevantní |
| **Kaskády** | `cascade="none\|save-update\|delete\|all\|all-delete-orphan"` na kolekci i na `<many-to-one>` | `OnDelete(DeleteBehavior.Cascade \| Restrict \| SetNull \| NoAction \| ClientCascade \| ClientSetNull)`; konvence: non-nullable FK → Cascade | `ON DELETE CASCADE` v DDL |
| **Sirotci** | `all-delete-orphan` — odebrání z kolekce znamená DELETE | odebrání z kolekce u required vztahu → DELETE; u optional → nastavení FK na NULL | ruční DELETE |
| **Pořadí insertů** | odvozeno z grafu | odvozeno z grafu | řeší autor kódu |
| **Naplnění FK** | automatické z navigační vlastnosti | automatické z navigační vlastnosti nebo přímo z FK vlastnosti | ruční přiřazení |
| **Vícesloupcový FK** | `<key>` a `<many-to-one>` umí víc `<column>` elementů | `HasForeignKey(b => new { b.X, b.Y })` | v `ON` klauzuli JOINu |
| **Načtení souvisejících dat** | `FetchMany(a => a.Books)`, nebo lazy loading při přístupu | `Include(a => a.Books)`, `ThenInclude(...)` | ruční JOIN, multi-mapping a deduplikace přes dictionary |

`inverse="true"` nemá v EF Core protějšek jako samostatný pojem — plyne z umístění FK. Při převodu NHibernate → EF Core tato informace mizí a nevadí to; opačným směrem ji builder musí odvodit z toho, která strana FK vlastní. Je to příklad faktu, který v IR nemusí být uložený, ale musí být odvoditelný.

---

## 8. Relace N:M

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Idiomatický zápis** | `<set table="BookCategories"><key column="BookId" /><many-to-many column="CategoryId" class="Category" /></set>` | `HasMany(b => b.Categories).WithMany(c => c.Books)` | JOIN přes junction tabulku v SQL |
| **Junction jako entita** | ne — tabulka je zmíněná jen jako atribut `table` na kolekci | ne — vzniká implicitní typ; konfigurovat jej lze přes `UsingEntity(...)` | ano, protože jinak než explicitně o ní mluvit nejde |
| **Skip navigation** | ano, fakticky — `Book.Categories` míří rovnou na `Category` | ano, oficiálně tak pojmenované (od EF Core 5) | neexistuje |
| **Payload na junction tabulce** | nelze na `<many-to-many>`; nutný rozpad na dvě 1:N přes explicitní entitu | nelze na implicitní junction; buď `UsingEntity<T>` s vlastní entitou, nebo rozpad na dvě 1:N | přirozeně |

Zde vzniká rozpor, který je pro práci důležitý. Podle `docs/design/002-many-to-many-generation.md` generuje IR vztah N:M jako **explicitní junction entitu** (varianta B), zatímco idiomatický výstup pro NHibernate i EF Core je **skip navigation**. Benchmarky to potvrzují: `WWIContext.OnModelCreating` používá `HasMany().WithMany().UsingEntity(...)` a mapování NHibernate používá `<many-to-many>`; ani v jednom případě neexistuje entita pro `StockItemStockGroups`.

Není to chyba návrhu. Explicitní junction entita je obecnější — unese payload i kompozitní klíč — a je to jediná forma, kterou umí vyjádřit všechny tři frameworky včetně Dapperu. Proto je v `EntityMap` příznak `IsJunctionTable` jako opt-in signál pro budoucí zploštění na skip navigation. Do analýzy tento případ patří jako doklad rozdílu mezi **expresivitou** (co lze vyjádřit) a **idiomatičností** (jak by to napsal člověk).

---

## 9. Dědičnost

| Strategie | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Table per hierarchy (TPH)** | `<subclass>` s `<discriminator>` na rodiči | výchozí strategie; `HasDiscriminator(...)` pro konfiguraci | ruční `WHERE` na diskriminátor a ruční volba typu |
| **Table per type (TPT)** | `<joined-subclass>` | `UseTptMappingStrategy()` | ruční JOIN |
| **Table per concrete class (TPC)** | `<union-subclass>` | `UseTpcMappingStrategy()` (od EF Core 7) | ruční UNION |
| **Výchozí chování** | žádné — dědičnost se musí namapovat explicitně | TPH | žádné |
| **Pokrytí v benchmarcích** | není | není | není |

Konceptuální shoda mezi NHibernate a EF Core je zde téměř 1:1, což z dědičnosti dělá dobrého kandidáta na kapitolu, kde se ukáže, že jednotné IR je realistické. Pro Dapper platí, že dědičnost nemá žádnou reprezentaci — je to pouze konvence v ručně psaných dotazech.

V `ORMComparison.sln` dědičnost úplně chybí, experimenty k tomuto tématu bude nutné napsat od nuly.

---

## 10. Konfigurace připojení

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Kde se konfiguruje** | objekt `Configuration` v kódu (`DataBaseIntegration`), nebo `hibernate.cfg.xml`, nebo sekce v `app.config` | `OnConfiguring(DbContextOptionsBuilder)` uvnitř `DbContext`, nebo `DbContextOptions<T>` předané konstruktorem (varianta pro DI, použitá v benchmarcích) | connection string předaný do `new SqlConnection(...)` |
| **Oddělení konfigurace a mapování** | oddělené — konfigurace v kódu, mapování v XML | **sloučené** — obojí v `DbContext` | nerelevantní |
| **Driver a provider** | `db.Driver<MicrosoftDataSqlClientDriver>()`; driver existuje od NHibernate 5.3. Benchmarky používají starší `SqlClientDriver` nad deprecated `System.Data.SqlClient` | `UseSqlServer(...)` | přímá instance `SqlConnection` |
| **Dialekt** | **povinný** — `db.Dialect<MsSql2012Dialect>()`; nejnovější dialekt pro SQL Server ve verzi 5.7.0. Určuje generované SQL i DDL | implicitní v provideru | **žádný**; přenositelnost mezi databázemi je plně na autorovi kódu |
| **Connection string** | shodný formát pro všechny tři | shodný | shodný |
| **`TrustServerCertificate=True`** | nutné | nutné | nutné |
| **Důvod** | `Microsoft.Data.SqlClient` má od verze 4.0 `Encrypt=true` jako výchozí a lokální SQL Server má self-signed certifikát | totéž | totéž |
| **Logování SQL** | `db.LogSqlInConsole = true`, `db.LogFormattedSql = true`; případně `show_sql`; plné logování přes log4net | `LogTo(Console.WriteLine, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)`, `EnableSensitiveDataLogging()` pro hodnoty parametrů | nepotřebné — SQL je doslova v kódu |

---

## 11. Správa schématu

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Vytvoření databáze** | ne — pokud databáze neexistuje, skončí to chybou `Cannot open database ... requested by the login` | **ano**, `EnsureCreated()` vytvoří databázi i tabulky | ne |
| **Vytvoření tabulek** | `new SchemaExport(cfg).Create(useStdOut, execute)` | `EnsureCreated()` | ručně psané DDL v `Execute(...)` |
| **DDL bez připojení k databázi** | `new SchemaExport(cfg).Create(s => sb.AppendLine(s), execute: false)` | `context.Database.GenerateCreateScript()` | triviálně — DDL píše autor |
| **Aktualizace existujícího schématu** | `SchemaUpdate` (best-effort, needestruktivní, neumí mazat) | **Migrations** — verzované, reverzibilní, s historií v tabulce `__EFMigrationsHistory` | ručně |
| **Validace schématu proti modelu** | `SchemaValidator` | `context.Database.GetPendingMigrations()`, plus runtime chyby | žádná |
| **Smazání schématu** | `SchemaExport.Drop(...)`; `Create` napřed dropne, pak vytvoří | `EnsureDeleted()` | `DROP TABLE IF EXISTS` |
| **Vhodnost pro produkci** | `SchemaExport` ne, `SchemaUpdate` s výhradami | Migrations ano | ruční správa, obvykle mimo aplikaci |

Migrations jsou koncept, který NHibernate v jádru nemá vůbec; externí projekty jako FluentMigrator nejsou součástí frameworku. Do srovnání proto patří spíš jako poznámka o rozsahu frameworku než jako řádek o expresivitě mapování — na obsah IR nemají vliv.

---

## 12. Životní cyklus a jednotka práce

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Hlavní objekt** | `ISession` z `ISessionFactory` | `DbContext` | `IDbConnection` |
| **Cena vytvoření** | `ISessionFactory` je drahá (parsuje mapování, staví model) a vytváří se jednou na aplikaci; `ISession` je levná a krátkodobá | `DbContext` je relativně levný; model se cachuje na úrovni typu kontextu | `SqlConnection` je levná; pool řeší ADO.NET |
| **Doporučená životnost** | session na požadavek nebo jednotku práce | context na požadavek nebo jednotku práce | connection na operaci, v `using` |
| **Thread safety** | `ISessionFactory` ano, `ISession` ne | `DbContext` ne | `SqlConnection` ne |
| **Otevření spojení** | líné — session otevře spojení, až když je potřeba | líné | explicitní, nebo automatické při prvním volání |
| **Stav objektů** | transient / persistent / detached | Added / Unchanged / Modified / Deleted / Detached (`ChangeTracker`) | žádný — objekty jsou obyčejné instance |
| **Znovupřipojení odpojené entity** | `Update()`, `Merge()`, `Lock()` | `Attach()`, `Update()`, nastavení `State` | nerelevantní |

---

## 13. Zápis dat

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Vložení** | `session.Save(entity)` — vrací vygenerovaný identifikátor | `context.Add(entity)` nebo `AddRange(...)`, pak `SaveChanges()` | `connection.Execute("INSERT ...", parametry)` |
| **Aktualizace** | `session.Update(entity)`, `Merge(entity)`, nebo prostá změna persistentní entity (dirty checking při flush) | změna sledované entity a `SaveChanges()` | `Execute("UPDATE ...")` |
| **Smazání** | `session.Delete(entity)` | `context.Remove(entity)` a `SaveChanges()` | `Execute("DELETE ...")` |
| **Model práce** | operace na jednotlivé entitě, plus dirty checking celé session při flush | operace nad **grafem**; `SaveChanges` vyhodnotí všechny sledované změny najednou | jeden příkaz = jedno volání |
| **Kdy se odešle SQL** | při `Flush()` — při commitu transakce, před dotazem, který by mohl být ovlivněn, nebo ručně | při `SaveChanges()` | okamžitě |
| **Dávkování** | `adonet.batch_size` v konfiguraci; od verze 5.6.0 podpora `DbBatch` (dávkové API ADO.NET) | automatické; `MaxBatchSize` konfigurovatelné; využívá `MERGE` a vícenásobné `INSERT` | žádné — `Execute` s kolekcí provede příkaz opakovaně, ne v dávce |
| **Získání generovaného klíče** | automatické | automatické | `SELECT CAST(SCOPE_IDENTITY() AS INT)` ručně |
| **Hromadné operace bez načtení entit** | HQL `DELETE` a `UPDATE`, nebo native SQL | `ExecuteDelete()`, `ExecuteUpdate()` (od EF Core 7, v EF Core 10 vylepšené) | přirozený stav |
| **Optimistické zamykání** | element `<version>`, atribut `optimistic-lock` na `<class>` | `IsRowVersion()`, `IsConcurrencyToken()` | ruční `WHERE` s verzovacím sloupcem |

---

## 14. Transakce

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Explicitní transakce nutná** | **ano** — bez `BeginTransaction()` se neprovede flush a data se neuloží | ne — `SaveChanges()` si transakci otevře sám | ne, každé volání je autocommit |
| **Zápis** | `using var tx = session.BeginTransaction(); ...; tx.Commit();` | `using var tx = context.Database.BeginTransaction();` při potřebě víc volání `SaveChanges` v jedné transakci | `using var tx = connection.BeginTransaction();` a předání `tx` do každého volání |
| **Předání transakce operacím** | implicitní, transakce visí na session | implicitní | **explicitní** — `Execute(sql, param, transaction: tx)` |
| **Úroveň izolace** | parametr `BeginTransaction(IsolationLevel)` | parametr `BeginTransaction(IsolationLevel)` | parametr `BeginTransaction(IsolationLevel)` |
| **Vnořené transakce** | nepodporované; savepointy přes native SQL | `CreateSavepoint()` a `RollbackToSavepoint()` | `tx.Save(name)` na `SqlTransaction` |
| **`TransactionScope`** | podporované | podporované | podporované |

---

## 15. Dotazování

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Hlavní API** | LINQ přes `session.Query<T>()` (namespace `NHibernate.Linq`) | LINQ nad `DbSet<T>` | SQL řetězec |
| **Další API** | QueryOver (typované fluent), HQL (`CreateQuery`), ICriteria (legacy), native SQL (`CreateSQLQuery`) | `FromSql`, `FromSqlRaw`, `FromSqlInterpolated`, `SqlQuery<T>` pro skalární typy (od EF Core 8) | `Query<T>`, `QueryFirst`, `QueryFirstOrDefault`, `QuerySingle`, `QuerySingleOrDefault`, `ExecuteScalar`, `QueryMultiple` |
| **Explicitní LEFT a RIGHT JOIN v LINQ** | přes `Fetch`, HQL nebo QueryOver | `LeftJoin` a `RightJoin` — **nové v EF Core 10**; umožňují explicitně určit typ JOINu i mezi nesouvisejícími entitami | píše se přímo v SQL |
| **Filtry na úrovni modelu** | `<filter-def>` a `session.EnableFilter(...)` | `HasQueryFilter(...)`; **pojmenované filtry s víc filtry na entitu a selektivním vypínáním jsou nové v EF Core 10** | žádné |
| **Parametrizace** | automatická z LINQ výrazu | automatická z LINQ výrazu | anonymní objekt, například `new { OrderID = 530 }` |
| **Ochrana proti SQL injection** | ano | ano | ano, dokud se používají parametry a SQL se neskládá řetězcem |
| **Kontrola v době kompilace** | u LINQ a QueryOver ano, u HQL ne | ano | ne |
| **Uložené procedury** | `CreateSQLQuery`, `<sql-query>` v mapování | `FromSqlRaw` | přímé volání s `CommandType.StoredProcedure` |
| **Materializace do DTO** | projekce v LINQ, `<return-scalar>` u SQL dotazů | projekce v LINQ do anonymního typu nebo DTO | `Query<Dto>` — jakákoli třída |
| **Více entit z jednoho výsledku** | `<return>` v native query, nebo transformer | projekce | `Query<T1, T2, TReturn>` s `splitOn` |
| **Asynchronní varianty** | `SaveAsync`, `ToListAsync`, `QueryAsync` — ano | ano, kompletní | `QueryAsync`, `ExecuteAsync` — ano |
| **Zobrazení SQL před spuštěním** | ne; jen logování při spuštění | **`ToQueryString()`** — vrátí SQL jako řetězec bez provedení dotazu | triviálně, SQL je vstup |

`ToQueryString()` je metodologicky nejcennější metoda z celé trojice: dovoluje získat generované SQL bez běžící databáze a dělat srovnání jako čistou textovou analýzu. NHibernate ekvivalent nemá, tam je nutné dotaz spustit a odchytit log.

---

## 16. Načítání souvisejících dat

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Výchozí chování** | **lazy** — kolekce i `<many-to-one>` jsou ve výchozím stavu líné (`lazy="proxy"`) | **eager pouze na vyžádání** — bez `Include` se navigace nenaplní a zůstane prázdná; lazy loading je opt-in balíček | nic se nenaplní nikdy |
| **Vynucení eager** | `FetchMany(a => a.Books)`, `ThenFetchMany(...)`, `Fetch(...)`, nebo `fetch="join"` v mapování | `Include(a => a.Books)`, `ThenInclude(...)`, `AsSplitQuery()` | ruční JOIN |
| **Vynucení lazy** | výchozí | balíček `Microsoft.EntityFrameworkCore.Proxies` s `UseLazyLoadingProxies()` a `virtual` na navigacích; alternativně `ILazyLoader` injektovaný do entity | neexistuje |
| **Chyba mimo kontext** | `LazyInitializationException` při přístupu ke kolekci po zavření session | navigace zůstane prázdná — tichý stav, potenciálně horší než výjimka | nerelevantní |
| **Problém N+1** | reálný kvůli výchozí lazy strategii; řeší se batch fetch (`batch-size`) nebo explicitním fetch | méně častý díky opt-in přístupu; možný při lazy proxies | nemůže vzniknout náhodou |
| **Duplicitní řádky z JOINu** | řeší framework | řeší framework | řeší autor kódu přes dictionary |
| **Rozdělení do víc dotazů** | `batch-size` na kolekci | `AsSplitQuery()` (od EF Core 5) | ruční |

---

## 17. Identita objektů a cache

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Identity map** | ano, v rámci session (first-level cache); dva dotazy na tentýž řádek vrátí **tutéž instanci** | ano, v rámci `DbContext` (`ChangeTracker`) | **ne** — dva dotazy vrátí dvě různé instance téhož řádku |
| **Vypnutí sledování** | `SetReadOnly(true)`, `StatelessSession` | `AsNoTracking()`, `AsNoTrackingWithIdentityResolution()` | výchozí a jediný stav |
| **Cache druhé úrovně** | podporovaná přes providery (`NHibernate.Caches.SysCache`, Redis a další), konfiguruje se per entita a per kolekce. Na .NET 10 s omezením: od verze 5.7.0 funkce postavené na binární serializaci vyhazují `SerializationException` a `BinaryFormatter` je opt-in. Distribuovaná cache serializující binárně vyžaduje nastavení vlastní strategie přes `Cfg.Environment.SerializationStrategy` | **není v jádře**; řeší se externími knihovnami nad aplikací | není |
| **Cache dotazů** | ano, `SetCacheable(true)` spolu s cache 2. úrovně | není | není |
| **Dopad na sémantiku** | změna entity je automaticky detekovaná při flush (dirty checking) | totéž přes snapshot | žádný — UPDATE se musí napsat explicitně |

---

## 18. Diagnostika a introspekce

| | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Zobrazení generovaného SQL** | log při běhu (`LogSqlInConsole`, `show_sql`, log4net) | log při běhu (`LogTo`) i staticky (`ToQueryString()`) | SQL je zdrojový kód |
| **Hodnoty parametrů v logu** | ano, přes konfiguraci log4net | `EnableSensitiveDataLogging()` | nerelevantní |
| **Introspekce modelu** | omezená — `sessionFactory.GetClassMetadata(type)`, `GetAllClassMetadata()`, `GetCollectionMetadata(role)`; `IClassMetadata` nese názvy vlastností, typy a identifikátor | **plná a dotazovatelná** — `context.Model.GetEntityTypes()`, na každém `GetTableName()`, `GetProperties()` s `GetColumnName()`, `GetColumnType()`, `IsNullable`, `ValueGenerated`, dále `GetKeys()`, `GetForeignKeys()`, `GetIndexes()`, `GetNavigations()` | není co introspektovat |
| **Statistiky** | `sessionFactory.Statistics` — počty dotazů, cache hit ratio, čas | diagnostické eventy `DbContext`, interceptory | žádné |
| **Interceptory** | `IInterceptor`, event listenery | `IDbCommandInterceptor`, `ISaveChangesInterceptor`, `IMaterializationInterceptor` | žádné; lze obalit `IDbConnection` |
| **Testcontainers** | podpora přidána ve verzi 5.7.0 | běžně používané | nerelevantní |

`context.Model` je nejpřímější nástroj na třetí osu analýzy. Postup: vypsat model, zakomentovat část `OnModelCreating`, vypsat znovu a porovnat. Rozdíl je přesně ta množina faktů, kterou EF Core doplnil konvencemi — tedy množina, kterou musí parser umět materializovat. U NHibernate tento postup nefunguje stejně dobře, protože `IClassMetadata` je výrazně chudší; tam je spolehlivější cestou vygenerované DDL ze `SchemaExport`.

---

## 19. Kdy se projeví chyba

| Druh chyby | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| **Překlep v názvu vlastnosti v mapování** | runtime, při `AddAssembly` nebo stavbě session factory | compile-time u fluent API (lambda), runtime u řetězcových přetížení | runtime, tiše nenaplněná vlastnost |
| **Překlep v názvu sloupce** | runtime, při dotazu | runtime, při dotazu | runtime, tiše nenaplněná vlastnost |
| **Chybějící mapování entity** | runtime — `No persister for: ...` | runtime — entita není v modelu | nerelevantní |
| **Nenačtený mapovací soubor** | runtime, tatáž hláška — chybí `EmbeddedResource` v `.csproj` | nemůže nastat | nemůže nastat |
| **Chybějící `virtual`** | runtime při stavbě session factory | nemůže nastat (bez proxies) | nemůže nastat |
| **Nesoulad modelu a schématu** | runtime, SQL chyba | runtime, SQL chyba; případně `SchemaValidator` nebo pending migrations | runtime, `Invalid column name` |
| **Přejmenování vlastnosti v C#** | rozbije mapování → runtime | fluent API se nezkompiluje → **compile-time** | nic se nestane, rozbije se běh |
| **Chybný `splitOn`** | nerelevantní | nerelevantní | runtime výjimka, nebo tiše špatně naplněné objekty |

Obecná tendence: EF Core posouvá nejvíc chyb do compile-time, NHibernate je odhaluje při startu aplikace, Dapper až za běhu konkrétního dotazu — a část z nich vůbec ne, jen tichým nenaplněním dat.

---

## 20. Souhrn expresivity

Klíčová tabulka pro návrh IR a pro diagnostiku podle rozhodnutí 7.4 z design docu 001.

| Fakt o doméně | NHibernate 5.7.0 | EF Core 10.0.10 | Dapper 2.1.79 |
|---|---|---|---|
| Název tabulky | `table` na `<class>` | `ToTable(...)` nebo konvence z `DbSet` | **jen v SQL řetězcích** |
| Název sloupce | `column` na `<property>` | `HasColumnName(...)` nebo konvence | jen v aliasech `AS`, rozptýleně |
| Datový typ | `type`, `length`, `precision` | CLR typ s `HasColumnType`, `HasMaxLength`, `HasPrecision` | **jen v DDL** |
| Nullabilita | `not-null` | NRT a `IsRequired()` | **jen v DDL** |
| Primární klíč | `<id>` nebo `<composite-id>` | `HasKey(...)`, `[Key]`, `[PrimaryKey]`, konvence | **nikde** |
| Kompozitní klíč | `<composite-id>` s `Equals`/`GetHashCode` | `HasKey(t => new {...})`, `[PrimaryKey(...)]` | **nikde** |
| Strategie generování klíče | `<generator>` | `ValueGenerated*`, `UseIdentityColumn` | **nikde** |
| Alternativní klíč | `<natural-id>`, `unique` | `HasAlternateKey`, `HasIndex().IsUnique()` | **jen v DDL** |
| Index | `<property index="...">` | `HasIndex(...)` | **jen v DDL** |
| Relace 1:N | `<set>` s `<one-to-many>` | `HasMany().WithOne()` | **jen v JOIN klauzulích** |
| Relace N:1 | `<many-to-one>` | `HasOne().WithMany()` | **jen v JOIN klauzulích** |
| Relace N:M | `<many-to-many>` | `HasMany().WithMany()` | **jen v JOIN klauzulích** |
| Vícesloupcový FK | víc `<column>` v `<key>` | `HasForeignKey(b => new {...})` | v `ON` klauzuli |
| Vlastník vztahu | `inverse="true"` | odvozeno z umístění FK | nerelevantní |
| Kaskádové mazání | `cascade="..."` | `OnDelete(...)` | **jen v DDL** |
| Dědičnost | `<subclass>`, `<joined-subclass>`, `<union-subclass>` | TPH, TPT, TPC | **nikde** |
| Identita entity | identity map session | `ChangeTracker` | **neexistuje** |
| Verzování a concurrency | `<version>` | `IsRowVersion()` | ruční `WHERE` |
| Výchozí hodnota sloupce | přes DDL nebo konstruktor | `HasDefaultValue(Sql)` | **jen v DDL** |
| Počítaný sloupec | `formula` | `HasComputedColumnSql` | v SELECT |
| Filtr na úrovni modelu | `<filter-def>` | `HasQueryFilter(...)`, pojmenované filtry | **nikde** |

Řádky, kde má Dapper hodnotu **nikde** nebo **jen v DDL**, tvoří přímý seznam varování, která musí Dapper builder emitovat: každý takový fakt při převodu do Dapperu nenávratně mizí a uživatel se to musí dozvědět.

Řádky, kde mají NHibernate i EF Core obsah, ale liší se strukturou — kompozitní klíč, N:M, vlastník vztahu — jsou kandidáti na místa, kde IR musí být obecnější než oba frameworky.

---

## 21. Společná podmnožina

Následující vlastnosti sdílejí všechny tři frameworky. IR je proto nemusí modelovat:

- Běží na .NET 10, instalují se z NuGetu, projekty jsou SDK-style. Rozdíl je jen v tom, že EF Core 10 na starším .NETu běžet **nemůže**, zatímco NHibernate 5.7.0 a Dapper 2.1.79 by uměly i starší cíle.
- Pod kapotou stojí na ADO.NET a v tomto projektu konkrétně na SQL Serveru 2022. Connection string, jeho syntax i nutnost `TrustServerCertificate=True` jsou proto identické.
- Pracují s obyčejnými třídami. Žádný nevyžaduje dědění od bázové třídy ani implementaci rozhraní.
- Parametrizují dotazy a tím chrání proti SQL injection.
- Mají kompletní asynchronní API.
- Nechávají volbu izolační úrovně transakce na uživateli a podporují `TransactionScope`.
- Jsou open source a mají srovnatelně dlouhou historii produkčního nasazení.
- Žádný z nich neřeší připojení k více databázím současně v rámci jedné jednotky práce bez distribuovaných transakcí.

Výhrada k poslednímu bodu o ADO.NET: v `benchmarks/ORMComparison.sln` to neplatí důsledně. Dapper, EF Core, linq2db a RepoDB jedou na `Microsoft.Data.SqlClient` 7.0.2, ale NHibernate (přes `db.Driver<SqlClientDriver>()`) a EF6 jedou na `System.Data.SqlClient` 4.9.1, který k nim teče přes `benchmarks/Common`. U PetaPoco to není dohledané. Pro srovnání expresivity to nevadí, pro měření výkonu je to metodologický confound.

---

## 22. Metodické poznámky

**Pokrytí v benchmarcích.** Dobře pokryté a stačí přečíst: základní mapování, dotazy, agregace, relace 1:N a N:M, JSON sloupce, množinové operace, uložené procedury, práce s metadaty výsledku. Chybí a bude potřeba dopsat od nuly: kompozitní klíče (v `ORMComparison.sln` není žádný `HasKey` ani `<composite-id>`, protože Wide World Importers jede celý na IDENTITY klíčích), dědičnost v jakékoli podobě, alternativní klíče, verzování.

**Verzování zjištění je nutné, ne kosmetické.** Tento dokument sám nese tři případy, kdy by tvrzení bez uvedení verze bylo nepřesné:

- `DateOnly` a `TimeOnly` v NHibernate — nedostupné na 5.5.2, dostupné na 5.7.0
- `[PrimaryKey]` v EF Core — neexistuje před verzí 7
- `LeftJoin` a `RightJoin` v EF Core — neexistují před verzí 10

**Ground truth je generované SQL.** Dvě API mohou vypadat podobně a generovat jiné SQL. Pro citovatelný důkaz v textu práce je nejsilnější vygenerovaný DDL a vygenerovaný dotaz, ne popis API.

**Cílové verze ORM nejsou v převodníku deklarované.** Wrappery v `ORMConvertor` nereferencují žádný ORM balíček, pouze Roslyn 5.6.0. Cílové verze frameworků tedy nejsou nikde uvedené a existuje jen implicitní předpoklad o cílové syntaxi — `[PrimaryKey]` vyžaduje EF Core 7 a vyšší, NHibernate builder generuje `urn:nhibernate-mapping-2.2`. Požadavek S6 přitom vyžaduje verze frameworků v záznamu překladu zaznamenávat. Je to otevřený bod k rozhodnutí, mimo rozsah této analýzy.
