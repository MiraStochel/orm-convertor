# Architektura aplikace – současný stav

**Účel:** referenční popis architektury tak, jak je dnes implementovaná, nezávisle na akademickém textu diplomky, ze které projekt vzešel. Odpovídá na otázku „jak to teď funguje"; proč jsme co zvolili, je v [`decisions/`](./decisions/README.md), co zbývá udělat, v [`open-items.md`](./open-items.md).
**Zdroj faktů:** vlastní čtení zdrojového kódu + kapitola *Architecture and Implementation* z původní diplomky (fakta přepsaná vlastními slovy, ne citace). Naposledy ověřeno proti kódu: 2026-08-02.

---

## 1. Přehled

Aplikace překládá entity, mapování a dotazy mezi třemi .NET ORM frameworky – Dapper, NHibernate a EF Core – přes společnou frameworko-nezávislou mezireprezentaci. Pipeline má dvě fáze: parser převede zdrojový kód do mezireprezentace, builder z ní vygeneruje kód pro cílový framework. Nad tím běží ještě Advisor – optimalizační modul, který pro danou sadu dotazů doporučí nejvhodnější framework (nebo kombinaci frameworků) na základě reálně naměřeného výkonu.

### Zafixované verze

Veškerá tvrzení o chování frameworků v tomto dokumentu i v `docs/analysis/` platí proti těmto verzím. Tabulka je jejich kanonické místo; audity uvádějí verze jen jako snímek k datu svého vzniku. Proč právě tyhle, říká rozhodnutí [013](./decisions/013-target-framework-versions.md).

| Komponenta | Verze |
|---|---|
| .NET | 10 (`net10.0`) |
| NHibernate | 5.7.0 |
| Microsoft.EntityFrameworkCore(.SqlServer) | 10.0.10 |
| Dapper | 2.1.79 |
| Microsoft.Data.SqlClient | 7.0.2 |
| Microsoft.CodeAnalysis.CSharp (Roslyn) | 5.6.0 |
| xUnit | v3 (3.2.2) |
| JDK | 25 (LTS) |
| Jakarta Persistence | 3.2 |
| Hibernate ORM | 7.4.5.Final |
| EclipseLink | 5.0.0 |
| MyBatis | 3.5.19 |
| `mssql-jdbc` | 13.4.0.jre11 |
| SQL Server | 2022 |

Javová část je zatím deklarace, ne závislost — v repozitáři žádný javový projekt není.

## 2. Struktura řešení (.NET solution)

Aplikace je .NET 10 solution (povýšená z .NET 8) rozdělená na projekty tří typů:

- **ASP.NET web projekt** – `ORMConvertorAPI`, poskytuje REST API a servíruje zkompilovaný Angular frontend jako statické soubory. API dokumentace je automaticky generovaná přes Swagger; UI je zapnuté jen v Development prostředí, pak je dostupné na `/orm/swagger`.
- **Testovací projekt (xUnit v3)** – `Tests`, testy parserů a builderů pro všechny tři ORM (převod do mezireprezentace a z ní, identity testy) a kombinované end-to-end testy pro dvojice EF Core ↔ NHibernate a EF Core → Dapper (dotazy).
- **Class library projekty** – zbytek, nejsou samostatně spustitelné, používají se jen jako reference.

### Knihovní projekty a jejich zodpovědnost

| Projekt | Zodpovědnost |
|---|---|
| `Model` | centrální doménové modely – entity/mapování mezireprezentace i instrukce pro dotazy; referencovaný téměř všude |
| `Common` | sdílené konvertory nezávislé na frameworku – `AccessModifierConvertor`, `CLRTypeConvertor`; používají je všechny buildery |
| `AbstractWrappers` | rozhraní pro parsery a buildery a deskriptor cílového frameworku; buildery mají společnou funkcionalitu řešenou jako abstraktní třídy, parsery jsou čistá rozhraní |
| `DapperWrappers` / `EFCoreWrappers` / `NHibernateWrappers` | konkrétní implementace parserů/builderů pro jednotlivé ORM, každý framework izolovaný ve vlastním projektu |
| `OrmConvertor` | orchestrace – třída `ConversionHandler` přijímá zdrojový vstup, přes factory třídy najde správný parser/builder a vrátí výstup; s konkrétními implementacemi pracuje jen přes rozhraní |
| `SampleData` | ukázkové vstupy (entity, mapování, dotazy) pro frontend – servírované přes endpointy `/samples` a `/samples-advisor`; zároveň se používají v testech |
| `Advisor` | ILP optimalizátor (GLPK), vybírá framework/kombinaci frameworků podle naměřených nákladů – detaily v §8 |
| `AdvisorBenchmarking` | dynamická kompilace a spuštění vygenerovaného kódu (Roslyn) pro reálné změření běhu a paměti |

## 3. Použité návrhové vzory

- **Adapter** – každý wrapper (parser i builder) adaptuje rozhraní konkrétního ORM na společnou mezireprezentaci.
- **Visitor** – generování výstupního kódu z instrukcí dotazu (`IQueryVisitor` a jeho implementace).
- **Builder** – entity/query buildery skládají výstupní kód postupně přes `StringBuilder`.
- **Factory** – výběr správné konkrétní implementace parseru/builderu podle zvoleného ORM.

## 4. Mezireprezentace

Mezireprezentace má dvě části: entitní a mapovací model (`Model.AbstractRepresentation`) a dotazové instrukce (`Model.QueryInstructions`).

### 4.1 Entity a mapování

`Entity` a `Property` popisují aplikační vrstvu – název, jmenný prostor, přístupové modifikátory, jazykový typ. `EntityMap` k entitě přidává databázová fakta:

```csharp
public class EntityMap
{
    public required Entity Entity { get; set; }
    public string? Table { get; set; }
    public string? Schema { get; set; }
    public List<PropertyMap> PropertyMaps { get; set; } = [];
    public PrimaryKey? PrimaryKey { get; set; }
    public List<Relation> Relations { get; set; } = [];
    public bool IsJunctionTable { get; set; } = false;
}
```

`PropertyMap` nese název sloupce, databázový typ, délku, precision, scale, nullabilitu a volný slovník `OtherDatabaseProperties` pro ostatní metadata ve tvaru klíč–hodnota. Vztah na `PropertyMap` **není** – žije výhradně na entitě, viz §4.3.

### 4.2 Primární klíč

```csharp
public sealed class PrimaryKey
{
    public required IReadOnlyList<PrimaryKeyPart> Parts { get; init; }   // vždy seřazené podle Order
    public SourceKeyClass? SourceKeyClass { get; init; }                 // nepovinný záznam o zdroji
}

public sealed class PrimaryKeyPart
{
    public required PropertyMap PropertyMap { get; init; }
    public required int Order { get; init; }                             // explicitní, 1-based
    public PrimaryKeyStrategy Strategy { get; init; } = PrimaryKeyStrategy.Unspecified;   // per-part
    public string? SourceStrategyName { get; init; }                     // co zdroj řekl navíc
    public Dictionary<string, string> StrategyParameters { get; init; } = [];   // parametry generátoru
}

public sealed class SourceKeyClass          // konstruktor validuje dvojici Form / PropertyName
{
    public string ClassName { get; }
    public KeyClassForm Form { get; }        // Embedded | Mirrored
    public string? PropertyName { get; }     // jen u Embedded
}
```

Klíč je seřazený seznam částí; jednoduchý klíč je degenerovaný případ o jedné části. Pořadí je explicitní hodnota, ne pozice v seznamu: zdroje ho samy nečíslují – EF Core je dané pořadím argumentů v `[PrimaryKey(...)]`, NHibernate pořadím prvků `<key-property>` – a bez explicitního čísla by se pořadí částí dalo splést s pořadím parsování.

Řazení podle `Order` vynucuje setter vlastnosti `Parts`, takže invariant platí na každé konstrukční cestě včetně přímé inicializace objektu a buildery mohou seznam iterovat tak, jak je. Prázdný klíč je odmítnut výjimkou a stejně tak klíč se dvěma částmi téhož pořadí: při duplicitě by výsledné pořadí určil vstup, ne model, a to je nedeterminismus, kterému brání S2. Souvislé číslování od jedničky se naopak nevyžaduje, protože čísla vznikají až v parseru (rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md)).

Strategie generování je per-part a výčet `PrimaryKeyStrategy` pojmenovává mechanismus, ne generátor konkrétního frameworku: `Unspecified` (nikdo neřekl), `Assigned` (hodnotu dodá aplikace), `Auto` (mechanismus vybere framework podle dialektu), `Identity`, `Sequence`, `HiLo`, `Uuid` a `Increment`. Neutrální název volíme tam, kde tentýž záměr vyjadřuje víc ekosystémů, a slovo zdroje tam, kde mechanismus umí jediný framework – proto `Auto` pro `native` i `AUTO`, ale `Increment` pro generátor, který má jen NHibernate (rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md)).

Co se do výčtu nevejde, se zaznamená vedle něj, ne místo něj. `SourceStrategyName` nese, co zdroj napsal, když to slovník nezachytil – vlastní generátorovou třídu, `foreign`, ale i variantu `guid.comb` vedle rozpoznaného `Uuid`; kanonický název, který bychom stejně vypsali zpátky, se nekopíruje, jinak by každý klíč nesl opis vlastní strategie. `StrategyParameters` nese parametry generátoru jako dvojice klíč–hodnota, protože bez názvu sekvence nebo velikosti bloku vzniká mapování, které se přeloží a nepoběží. Klíče parametrů jsou dnes názvy zdroje; kanonický slovník napříč ekosystémy je otevřená položka.

Model je záměrně permisivní – dovolí i kombinaci, kterou konkrétní databáze nepřijme (například dvě `Identity` části); validace typu „tabulka smí mít jen jednu IDENTITY" patří cílovému builderu nebo databázi, ne abstraktnímu modelu.

Členy, které si kompozitní klíč v cílovém frameworku vynucuje – u NHibernate `Equals`, `GetHashCode` a `[Serializable]`, u JPA celá ID třída – v mezireprezentaci nejsou; generuje je builder (rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md)).

Klíčová třída zdroje se zaznamenává, ne převádí. Protože všechny cíle vykreslují klíč ploše, ztratil by se při překladu její název i forma; `SourceKeyClass` je proto nepovinný signál vedle klíče, obdobně jako `IsJunctionTable` u spojovací tabulky – klíč sám zůstává seřazeným seznamem částí. Formy jsou dvě a liší se cestou k části klíče: u `Embedded` vede přes vlastnost entity (`<composite-id name= class=>`, `@EmbeddedId`, `o.Id.OrderID`), u `Mirrored` klíčové vlastnosti zůstávají na entitě a třída je jen zrcadlí (`<composite-id class=>` bez `name`, `@IdClass`, `o.OrderID`). Dvojici formy a názvu vlastnosti kontroluje konstruktor, takže nekonzistentní záznam v mezireprezentaci nevznikne. Signál vyplňuje NHibernate XML parser z atributů `<composite-id>`: samotný `class` znamená `Mirrored`, `class` spolu s `name` znamená `Embedded`. U formy `Embedded` je tím přečtená jen mapovací strana – části klíče jsou vlastnostmi klíčové třídy, ne entity, a co z toho plyne pro entitu, je otevřená položka. Žádný .NET builder signál nečte a číst nemá; testy ověřují, že generovaný artefakt je s ním i bez něj shodný a že z výstupního `<composite-id>` název třídy zmizí. Prvním čtenářem bude JPA builder, který podle něj pojmenuje ID třídu místo odvození konvencí (F7–F10).

### 4.3 Vztahy

```csharp
public sealed class Relation
{
    public string? Name { get; set; }
    public required Cardinality Cardinality { get; set; }        // OneToOne, OneToMany, ManyToMany, ManyToOne
    public required RelationRole Role { get; set; }              // Owning, Inverse
    public required string SourceEntity { get; set; }            // název entity, ne reference
    public required string TargetEntity { get; set; }
    public IReadOnlyList<ColumnPair> ColumnPairs { get; set; } = [];
    public string? SourceNavigationProperty { get; set; }
    public string? InverseRelationName { get; set; }
}
```

Vztah žije na `EntityMap`, ne na `PropertyMap` – u 1:N a N:M neexistuje na straně „mnoho" žádný sloupec, na který by šlo vztah pověsit. Násobnost nese jedině `Cardinality`; samostatný příznak pro 1:1 model nemá, protože by říkal totéž jinými slovy a dvě místa pro jeden fakt se dřív nebo později rozejdou.

`Role` rozlišuje stranu s fyzickým cizím klíčem (`Owning`) od strany s navigační kolekcí nebo referencí bez sloupce (`Inverse`). Builder podle role generuje buď vlastnost s cizím klíčem, nebo navigaci — a role, ne kardinalita, rozhoduje i o tvaru značky: u NHibernate je vlastnící strana vztahu 1:1 `<many-to-one unique="true">`, kdežto `<one-to-one>` je strana bez sloupce, tedy buď protistrana, nebo vztah přes sdílený primární klíč, který se pozná podle generátoru `foreign` u vlastního klíče entity (rozhodnutí [012](./decisions/012-foreign-key-rendering.md)).

`ColumnPairs` je uspořádaný seznam dvojic sloupců, který zaručuje správné párování u kompozitního cizího klíče. `ColumnPair` je záměrně třída, ne tuple – System.Text.Json položky tuple neserializuje. **Prázdný seznam znamená, že sloupce zatím nejsou rozresolvované**; cílové sloupce nejdou určit z jedné translation unit a jejich doplnění závisí na metadatech z databáze (F4/F5). Pořadí párů je autoritativní a buildery je nepřeskládávají: rozpor s pořadím klíče, na který míří, je chyba úplnosti pro diagnostiku, ne něco, co se má potichu spravit při emisi.

Entity se ve vztahu odkazují jménem, ne referencí (rozhodnutí [001](./decisions/001-entity-reference-by-name.md)).

N:M nemá vlastní typ vztahu. Skládá se ze spojovací entity s `IsJunctionTable = true` a dvou `Owning` relací s kardinalitou `ManyToOne`, které z ní míří k oběma stranám:

```
StudentCourse (IsJunctionTable = true)
 ├─ Relation(Owning, ManyToOne) → Student   (ColumnPairs: [StudentId ↔ Id])
 └─ Relation(Owning, ManyToOne) → Course    (ColumnPairs: [CourseId ↔ Id])
```

„Bohatá" spojovací tabulka s vlastními sloupci navíc je z pohledu modelu prostě normální entita se dvěma cizími klíči. `IsJunctionTable` je volitelný signál, že tabulka je „čistá" – dnešní buildery ho k ničemu nepotřebují (rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md)).

### 4.4 Dotazové instrukce a podmínkový strom

Dotaz je seznam instrukcí (`FromInstruction`, `ProjectInstruction`, `SelectInstruction`, `JoinInstruction`, `GroupByInstruction`, `HavingInstruction`, `OrderByInstruction`, `SubQueryInstruction`, `SetOperationInstruction`), každá s metodou `Accept(IQueryVisitor)`.

Filtrační a spojovací podmínky jsou strom (`Model.QueryInstructions.Conditions`):

- `ComparisonCondition` – levá i pravá strana jako trojice tabulka/vlastnost/konstanta plus volitelná funkce (`COUNT`, `SUM`, …), mezi nimi `ComparisonOperator`
- `LogicalCondition` – `And` nebo `Or` nad seznamem operandů
- `NotCondition` – negace jednoho operandu

Test na `NULL` je operátor `IsNull`/`IsNotNull` s nevyužitou pravou stranou, ne samostatný uzel ani porovnání s konstantou (rozhodnutí [002](./decisions/002-is-null-as-comparison-operator.md)).

`SelectInstruction` i `HavingInstruction` nesou právě jeden `ConditionNode`; místo seznamu plochých instrukcí spojovaných builderem přes AND je celá podmínka včetně vnoření ve stromu. `JoinInstruction` má ON klauzuli rovněž jako `ConditionNode`, takže vícesloupcový equi-join je jen `And` několika rovností a nepotřebuje vlastní mechanismus.

`IQueryVisitor` má kromě metod pro instrukce i `Visit` pro všechny tři typy podmínkových uzlů. Implementace musí hlídat závorkování – vnořený `LogicalCondition` s odlišným operátorem (AND obsahující OR) se musí obalit, jinak vznikne sémanticky jiný dotaz.

## 5. Parsery a buildery – jak fungují dnes

- Všechny entity parsery používají Roslyn syntax analyzer. Dapper parser čte jen strukturu entity (Dapper mapování nepodporuje), EF Core parser navíc interpretuje atributové mapování, NHibernate má dva samostatné parsery – jeden pro entitu (stejně jako Dapper), druhý pro XML mapování (LINQ to XML).
- EF Core parser kromě atributů čte i konvenci primárního klíče: nemá-li entita `[Key]` ani třídní `[PrimaryKey]`, klíčem se stane skalární vlastnost `Id`, jinak `<název entity>Id`, a to bez ohledu na velikost písmen. Klíč z konvence je tvrzení zdroje, ne domněnka, takže má v prioritě zdrojů první stupeň (rozhodnutí [008](./decisions/008-database-as-metadata-source.md)) a v cílovém artefaktu se stává explicitním. Bez toho by entity s konvenčním klíčem — v EF Core běžné — vycházely z převodu jako bezklíčové. Touž logikou se čte strategie klíče: parser bere `[DatabaseGenerated]`, kde je, a jinde ji odvodí z typu klíče, protože EF Core generuje hodnotu sám u jednosloupcového celočíselného klíče a u `Guid`, jinde ne; části kompozitního klíče proto dostávají `Assigned`. Anotace `Identity` netvrdí sloupec IDENTITY, ale „hodnotu vytvoří úložiště při vložení", takže se mapuje na `Auto`. Opačným směrem builder anotaci vypíše jen tam, kde mění, co by EF Core udělalo samo: zopakovat konvenci cíle je šum, kdežto řetězcový klíč označený `Auto` by bez ní generování tiše ztratil. Mechanismy `Identity`, `Sequence`, `HiLo`, `Uuid` a `Increment` anotace nevyjádří vůbec — jsou dostupné jen fluent API, takže jde o zúžení pro diagnostiku.
- NHibernate XML parser čte u `<id>` nejen třídu generátoru, ale i jeho `<param>` prvky a název, který slovník strategií nezachytil; builder parametry vypisuje zpět jako vnořené prvky, protože jinak by vzniklo mapování odkazující na sekvenci, kterou si cíl zvolí sám. `<composite-id>` generátor nepřipouští, takže části kompozitního klíče jsou `Assigned` a per-part strategie je v tomto cíli nevyjádřitelný fakt. Strategii, kterou nikdo neuvedl, builder vypisuje jako `assigned` — konvence cíle, ne tvrzení zdroje (rozhodnutí [008](./decisions/008-database-as-metadata-source.md)).
- U `<composite-id>` čte parser navíc atributy `class` a `name` a ukládá je jako `SourceKeyClass`; do výstupu se nevracejí, protože vykreslení je ploché (rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md)). Části klíče přitom bere jen z prvků `<key-property>` — `<key-many-to-one>`, tedy část klíče, která je zároveň odkazem na jinou entitu, se přeskočí bez hlášení a klíč vyjde kratší, než jak ho zdroj popsal.
- NHibernate builder volí značku vztahu podle role a strategie klíče: `<many-to-one>` u N:1, `<many-to-one unique="true">` u vlastnící strany 1:1, `<one-to-one constrained="true">` u entity se sdíleným primárním klíčem a `<one-to-one>` bez atributů na straně bez cizího klíče. Sloupce bere z `ColumnPairs` — jeden atributem `column`, víc vnořenými `<column>` v uloženém pořadí. Bez známých párů atribut u `<many-to-one>` vynechá, protože NHibernate doplní sloupec podle názvu vlastnosti, ale u `<key>` uvnitř `<bag>` vypíše sloupec klíče vlastníka: tam je výchozí jméno pevné `id` (`Collection.DefaultKeyColumnName`), takže mlčení význam mění, místo aby ho jen neuvádělo.
- EF Core builder vypisuje `[ForeignKey]` na navigační vlastnosti, a to jen na vlastnící straně; anotace jmenuje vlastnosti třídy, ne sloupce. Chybí-li vlastnost pro sloupec cizího klíče, builder ji dogeneruje: jméno složí z navigace a názvu části cílového klíče (`OrderLineOrderID`), typ převezme z části klíče, na kterou pár míří, jméno sloupce vypíše jako `[Column]` a nullabilitu přebírá po navigaci. Bez známých párů anotaci nevypíše a odvození nechá konvenci EF Core.
- Parsování dotazů je implementované jen pro EF Core LINQ (`EFCoreLinqQueryParser` dědí z Roslyn `CSharpSyntaxWalker`, přepisuje `Visit*` metody pro analýzu řetězených volání).
- Generování dotazů je implementované jen pro Dapper SQL (přes `StringBuilder` + dedikovaný visitor `DapperSQLQueryVisitor`).
- Entity buildery fungují stejně napříč frameworky – skládají string šablony; NHibernate builder plní dva `StringBuilder`y najednou a vrací dva výstupy (C# entitu + XML mapování).
- Kompozitní klíč vykresluje EF Core builder atributem `[PrimaryKey]`, NHibernate builder elementem `<composite-id>` s `<key-property>` a k tomu na entitní třídu doplní `[Serializable]` a přepisy `Equals`/`GetHashCode`. Bez nich NHibernate shodí stavbu session factory hláškou `composite-id class must override Equals()`.
- Sloupec se v mapování NHibernate zapisuje atributem `column`, nebo vnořeným elementem `<column>`. Parser čte obojí a vnořený element má přednost; builder ho použije jen tehdy, když má co do něj dát. Není to volba stylu: `<property>` unese délku, přesnost i měřítko jako vlastní atributy, ale `<id>` a `<key-property>` ne, takže bez vnořeného elementu by klíčový sloupec tyto údaje ztratil. Nullabilita se u klíče nevypisuje, protože sloupec nesoucí identifikátor nullable není. Čtou se první `<column>` a jen ty atributy, které mají v modelu protějšek — `sql-type` patří k neutralizaci typového modelu.
- Dapper builder má kroky `BuildPrimaryKey` a `BuildForeignKey` prázdné, protože Dapper mapování klíčů nemá; prázdné tělo je tvrzení o frameworku, ne opomenutí. Dnes je zahazuje potichu; podle rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md) o nich má nést strukturované varování – zatím neimplementováno.
- Vlastnosti cílového frameworku jsou deklarované v deskriptoru (`TargetFrameworkDescriptor` v `AbstractWrappers.Descriptors`, konkrétní instance ve wrapperech). Deskriptor uvádí vynucené členy — co builder přidá ke generovanému artefaktu, ačkoli to není fakt o doméně — a vztah ke kategoriím mapovacích faktů ve stavech *vyžaduji*, *umím vyjádřit*, *neumím vyjádřit*. Dapper má všechny kategorie ve třetím stavu a žádné vynucené členy; jediná kategorie, ve které se plnohodnotné frameworky rozcházejí, je primární klíč — NHibernate ho vyžaduje, EF Core na jeho absenci reaguje bezklíčovým typem. Import se za vynucený člen nepovažuje: plyne z prvku, který se generuje, takže patří builderu. Společný test EnforcedMembersTest ověřuje každý generovaný artefakt proti deskriptoru jeho frameworku, včetně toho, že člen chybí, když jeho podmínka neplatí. Ověřuje se výskytem řetězce, což je vědomý strop: u modifikátoru virtual test odhalí, že vypadl úplně, ne že chybí u jedné vlastnosti. Přísnější kontrola by znamenala výstup parsovat, což je proti smyslu deskriptoru — ten popisuje, co framework vyžaduje, ne jak vypadá vygenerovaný kód. Deskriptor dnes čte jedině tento test. Buildery podmínku, za které člen platí, vyhodnocují samostatně — `NHibernateEntityBuilder` se ptá vlastní metody `HasCompositeKey`, ne deskriptoru. Je to vědomé: párovat emisi s deklarací by znamenalo hledat členy podle názvu, a překlep v něm by generování tiše vypnul, aniž by to test odhalil, protože by porovnával tentýž překlep na obou stranách. Dělba je tedy trojí — deskriptor deklaruje, builder implementuje, test je váže. Produkčního konzumenta deskriptor dostane s diagnostikou, která z něj bude číst kategorie ve stavu „neumím vyjádřit". Rozhodnutí [009](./decisions/009-target-framework-descriptor.md).
- Převod typů není mezi frameworky symetrický. Konvertor pro EF Core je u data a času bijekce (`datetime`, `datetime2`, `smalldatetime` tam i zpět), protože `HasColumnType` bere doslovný SQL typ. Konvertor pro NHibernate slévá `DatabaseType.DateTime`, `DateTime2` i `SmallDateTime` do jediného `type="DateTime"` a zpětný převod vrací všechny tři jako `DateTime2`. Round-trip přes NHibernate tedy z `datetime` i `smalldatetime` udělá `datetime2` – idempotence po první normalizaci platí, identita ne; u `smalldatetime` je to navíc změna přesnosti z minuty na 100 ns a rozsahu z 1900–2079 na 0001–9999. Příčinou není chyba v převodní tabulce, ale to, že `type` v mapování NHibernate je typ NHibernate, ne SQL typ – konkrétní SQL typ z něj odvozuje dialekt. Pod zafixovanými verzemi to odpovídá i skutečnosti: NHibernate 5.7.0 s `MsSql2012Dialect` vyrobí z typu `DateTime` sloupec `datetime2`, protože dialekty pro SQL Server 2008+ ho používají od verze 5.0.0, a typ `DateTime2` je od téže verze obsoletní. Rozdíl by šlo udržet přes `sql-type` na `<column>`, to ale vyžaduje znát cílový dialekt, který se dnes nikde nedeklaruje.

## 6. Spuštění a nasazení

**Přes Visual Studio:** otevřít `ORMConvertor.sln`, nastavit `ORMConvertorAPI` jako startup projekt, spustit (`F5` / `Ctrl+F5`). Otestovaný je jen `http` launch profil.

**Přes .NET CLI:**

```
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

Aplikace pak běží typicky na `http://localhost:5072/orm/`.

**Testy:**

```
dotnet test Tests/Tests.csproj --configuration Release
```

Testy běží i v GitHub Actions (konfigurace ve `.github`) – při push na `main` a u pull requestů, ale jen pokud se změnilo něco uvnitř `ORMConvertor/**`; změny v `docs/`, `benchmarks/` apod. workflow nespustí.

**Frontend (Angular):** je potřeba zkompilovat zvlášť a zkopírovat do `wwwroot`, odkud ho servíruje ASP.NET:

```
npm install
ng build --configuration "production" --base-href "/orm/" --deploy-url "/orm/"
# a zkopírovat dist/browser/* do ../wwwroot/
```

**Kontejnerizované nasazení:** v repozitáři je i `docker-compose.yml` (aplikace + SQL Server s WideWorldImporters přes `database.Dockerfile`) a vícestupňový `ORMConvertorAPI/Dockerfile`, který sestaví frontend (Node), nativní knihovnu Advisoru (`libadvisor.so`, gcc + GLPK) i .NET aplikaci. Soubor `ecosystem.config.js` je konfigurace pro proces manager PM2 (nasazení mimo Docker). Tahle cesta zatím není podrobněji zdokumentovaná – viz [`open-items.md`](./open-items.md).

## 7. Rozhraní parserů a builderů

`AbstractEntityBuilder` odděluje dvě sady metod. **Naplnění mezireprezentace** řeší veřejné metody implementované přímo v abstraktní třídě (framework-nezávislé): `BeginEntity` (začátek další entity – builder umí držet víc entit najednou v `EntityMaps`), `AddNamespace`, `AddClassHeader`, `AddSchema`, `AddTable`, `AddProperty`, `SetPropertyDatabaseMapping` (databázové detaily mapování – sloupec, typ, délka, precision/scale, nullabilita, ostatní klíč–hodnota), `AddPrimaryKey`, `SetKeyStrategyDetails`, `AddForeignKey` a `AddRelation`. `AddPrimaryKey` má dvě přetížení: seznam trojic `(PropertyName, Order, Strategy)` pro kompozitní klíč a pohodlnou zkratku pro jednoduchý klíč; kompozitní přetížení navíc bere nepovinný záznam o klíčové třídě zdroje. `SetKeyStrategyDetails` doplňuje k jedné části klíče název strategie ze zdroje a parametry generátoru a volá se až po definici klíče – ten vzniká jediným voláním, takže opakované `AddPrimaryKey` detaily zahodí spolu s klíčem, který nahrazuje.

**Generování výstupu** je šablonová metoda. Veřejná `Build()` je implementovaná v abstraktní třídě: iteruje přes `EntityMaps` a pro každou entitu volá v pevném pořadí `BuildImports` → `BuildTableSchema` → `BuildPrimaryKey` → `BuildProperties` → `BuildForeignKey` → `BuildEnforcedMembers` → `FinalizeBuild`. Všech sedm kroků je `protected abstract` a bere `(EntityMap, EntityArtifact)`; `FinalizeBuild` navíc vrací výstupy, protože počet artefaktů se liší podle frameworku. `EntityArtifact` drží dva `StringBuilder`y (`Code` a `Mapping`) a příznak `ClassOpened` — NHibernate plní oba a vrací dva výstupy, Dapper i EF Core plní jen `Code`. Primární klíč se generuje **před** vlastnostmi, protože části klíče se vypisují v pořadí, které určuje klíč, ne deklarace vlastností. Krok, ve kterém framework nic negeneruje, má prázdné tělo — to je tvrzení o frameworku, ne mrtvý kód: Dapper má prázdné `BuildPrimaryKey`, `BuildForeignKey` i `BuildEnforcedMembers`, EF Core prázdné `BuildEnforcedMembers`, protože jeho jediný vynucený prvek stojí před hlavičkou třídy a vypisuje se v `BuildTableSchema`. Každý builder má vlastnost `Descriptor` odkazující na deskriptor svého frameworku.

`AbstractQueryBuilder` přijímá podmínky hotové: `Where(ConditionNode)`, `Having(ConditionNode)` a `Join(JoinKind, left, right, ConditionNode, alias?)`. Parser si strom postaví sám rekurzivním průchodem zdrojové syntaxe a předá ho jedním voláním – to odpovídá tomu, jak `EFCoreLinqQueryParser` už dnes rekurzivně prochází syntaxi, a je to přirozenější než inkrementální mutace builderu.

`IParser` (entity/mapování) definuje jen dvě metody: `CanParse(contentType)` – zjistí, jestli parser umí daný vstupní formát (důležité tam, kde je mapování rozdělené do víc souborů, typicky NHibernate XML), a `Parse(source)`, která nemá návratovou hodnotu a místo toho naplňuje mezireprezentaci přes volání na `AbstractEntityBuilder`. `IQueryParser` je stejné, jen `Parse` navíc bere referenci na už naparsované mapování entit, protože dotaz sám o sobě často neobsahuje název tabulky/sloupce (typicky LINQ).

Původní návrh pro `AbstractQueryBuilder` (v `thesis/chapters/04_query_translation.tex` v původním repozitáři) počítá s `Push()`/`Pop()` metodami pro vstup/výstup z vnořeného poddotazu a se dvěma abstraktními výstupními metodami – `Build()` pro nativní syntaxi cílového frameworku (LINQ apod.) a `BuildSQL()` pro syrové SQL. Ověřeno proti kódu: `Push()`/`Pop()` implementované jsou (zásobník značek, `Pop()` obalí nasbírané instrukce do `SubQueryInstruction`; `SetOperation` je bez předchozího Push/Pop odmítnutá výjimkou). `BuildSQL()` ale **neexistuje** – je jen jediná abstraktní `Build()` vracející `List<ConversionSource>`; rozlišení nativní syntaxe vs. SQL bude potřeba dořešit při implementaci query builderů pro EF Core a NHibernate. Poddotazy jsou navíc ve visitor vrstvě dotažené jen napůl: `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` obsahuje `TODO` a vrací prázdný string (instrukce uvnitř poddotazu projde, ale výsledek se nikam neskládá).

## 8. Advisor – implementační detaily

ILP model je napsaný v C (`Advisor/ilp.c`) přímo přes GLPK C API (ne přes vyšší úroveň abstrakce): `glp_create_prob()` založí úlohu, `glp_add_cols()`/`glp_set_col_kind()` definují binární proměnné $x_{q,f}$ a $y_f$, `glp_set_obj_coef()` nastaví účelovou funkci, `glp_add_rows()`/`glp_set_row_bnds()` definují omezení, `glp_load_matrix()` nahraje řídkou matici koeficientů. Řešení spouští `glp_init_iocp()` (parametry solveru) a `glp_intopt()` (branch-and-bound). Výsledek se čte zpět přes `glp_mip_col_val()`.

C# strana (`Advisor.Solve`) volá tenhle wrapper přes P/Invoke – `[LibraryImport("libadvisor.so")]`. Knihovna `libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`: gcc + `libglpk-dev`); název je natvrdo linuxový, takže Advisor mimo Linux/Docker neběží – překladová část aplikace na tom nezávisí a funguje všude. Build krok pro Windows (`advisor.dll`) neexistuje, i když `ilp.c` má exportní makra připravená.

## 9. Co je záměrně mimo rozsah dnešní implementace

Výchozí stav při převzetí projektu popisuje [`baseline.md`](./baseline.md), otevřené položky a jejich pořadí [`open-items.md`](./open-items.md).

Stručně: podporované jsou tři .NET ORM, překlad dotazů je jednosměrný (jen EF Core → Dapper), Advisor pracuje jen s Dapperem a EF Core, napojení na databázi pro doplnění chybějících metadat neexistuje – a s ním ani rozresolvované `ColumnPairs`. Javový ekosystém a cross-ecosystem překlad zatím nezačaly; typový model je stále CLR-specifický.
