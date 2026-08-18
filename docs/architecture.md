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
- **Testovací projekt (xUnit v3)** – `Tests`, testy parserů a builderů pro všechny tři ORM (převod do mezireprezentace a z ní, identity testy) a kombinované end-to-end testy pro dvojice EF Core ↔ NHibernate a EF Core → Dapper (dotazy). Podsložka `Database/` drží prostředí s testovací databází (viz §6.1), podsložka `Catalog/` testy čtečky katalogu a fáze doplnění (viz §5.2), podsložka `Verification/` 2. a 3. stupeň ověření generovaných artefaktů (viz §6.2).
- **Class library projekty** – zbytek, nejsou samostatně spustitelné, používají se jen jako reference.

### Knihovní projekty a jejich zodpovědnost

| Projekt | Zodpovědnost |
|---|---|
| `Model` | centrální doménové modely – entity/mapování mezireprezentace i instrukce pro dotazy; referencovaný téměř všude |
| `Common` | sdílené konvertory nezávislé na frameworku – `AccessModifierConvertor`, `CSharpTypeConvertor` (název typu v C# ↔ `LangType`); používají je všechny buildery. Tabulka jazykový typ ↔ databázový typ sem záměrně nepatří — je to výchozí předpoklad konkrétního frameworku a žije ve wrapperech (rozhodnutí [014](./decisions/014-language-type-model.md)). Dále `Common.Compilation` – jediný krok kompilace přes Roslyn v řešení (`CSharpSourceCompiler` vrací diagnostiky a sestavení, nezavádí a nevyhazuje) a `MetadataReferenceProvider` pro skládání referencí; spotřebiteli jsou ověření artefaktů v testech a benchmarking (rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md)) |
| `AbstractWrappers` | rozhraní pro parsery a buildery, deskriptor cílového frameworku a typy diagnostických záznamů (`Diagnostics/ConversionRecord`, viz §5.1); buildery mají společnou funkcionalitu řešenou jako abstraktní třídy, parsery jsou čistá rozhraní |
| `DapperWrappers` / `EFCoreWrappers` / `NHibernateWrappers` | konkrétní implementace parserů/builderů pro jednotlivé ORM, každý framework izolovaný ve vlastním projektu |
| `OrmConvertor` | orchestrace – třída `ConversionHandler` přijímá zdrojový vstup a nepovinný připojovací řetězec ke katalogu, přes factory třídy najde správný parser/builder, mezi parsováním a generováním spustí fázi doplnění z katalogu (viz §5.2) a vrátí `ConversionResult`, tedy artefakty spolu s diagnostickými záznamy (viz §5.1) a časem čtení katalogu (S3); s konkrétními implementacemi pracuje jen přes rozhraní |
| `SampleData` | ukázkové vstupy (entity, mapování, dotazy) pro frontend – servírované přes endpointy `/samples` a `/samples-advisor`; zároveň se používají v testech |
| `Advisor` | ILP optimalizátor (GLPK), vybírá framework/kombinaci frameworků podle naměřených nákladů – detaily v §8 |
| `AdvisorBenchmarking` | spuštění vygenerovaného kódu pro reálné změření běhu a paměti; kompilaci bere ze společného kroku v `Common.Compilation` a přidává k ní zavedení do kolektibilního `AssemblyLoadContext`; kvalifikaci názvů tabulek bere z jediné čtečky katalogu v `DatabaseCatalog` a selhání spojení nechá propadnout (dřívější prázdný `catch` zmizel s rozhodnutím [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)) |
| `DatabaseCatalog` | jediné místo, které čte databázová metadata (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)): `SqlServerCatalogReader` (mechanismus – dotazy do katalogu, dávkování), `CatalogCompletion` (řízení – fáze doplnění mezireprezentace podle poptávky deskriptoru, viz §5.2) a `LanguageTypeInference` (odvození jazykového skaláru z databázového typu). Wrappery na něm nezávisejí (S1) |

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

Jazykový typ vlastnosti nese ekosystémově neutrální `LangType` (rozhodnutí [014](./decisions/014-language-type-model.md)) se čtyřmi kategoriemi: `Scalar` s hodnotou uzavřeného výčtu `ScalarType` (`Bool`, `Byte`, `Short`, `Int`, `Long`, `Float`, `Double`, `Decimal`, `Char`, `String`, `DateTime`, `Guid`, `Object`), `Reference` s názvem cílové entity (jménem, ne referencí — rozhodnutí [001](./decisions/001-entity-reference-by-name.md)), `Collection` s rekurzivním `LangType` prvku a druhem `CollectionKind` (`Unspecified`, `List`, `Set`; mapy jsou mimo rozsah, potřebovaly by i typ klíče) a `Unknown` s názvem přesně tak, jak ho napsal zdroj. Instance vznikají výhradně továrními metodami `LangType.Scalar/Reference/Collection/Unknown`, takže neplatná kombinace polí není zapsatelná. `IsNullable` sedí na `LangType`, tedy na jazykové straně vedle databázové na `PropertyMap` (pravidlo E4 má co porovnávat), a protože je na typu, nese ji i prvek kolekce. `Property.Type` je nullable: `null` znamená, že jazykový typ nikdo neuvedl — vlastnost známá jen z mapovacího deskriptoru.

`Unknown` se nikdy neprojeví výjimkou: parser ho zaznamená a builder vypíše jménem ze zdroje — mlčení by znamenalo neúplný artefakt místo neúplného tvrzení. `Reference` vzniká, když to zdroj tvrdí: `AddForeignKey` při registraci vztahu povýší typ navigační vlastnosti na `Reference` (u kolekcí na `Collection` s referenčním prvkem), přičemž zachová nullabilitu i druh kolekce, který deklarovala jazyková strana. Druhá polovina pravidla — rozresolvování názvu typu proti entitám téhož převodu — čeká na rozresolvování jmen entit (otevřená položka). `Object` je skalár a znamená, že zdroj napsal `object`; je to jiné tvrzení než `Unknown`.

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

`ColumnPairs` je uspořádaný seznam dvojic sloupců, který zaručuje správné párování u kompozitního cizího klíče. `ColumnPair` je záměrně třída, ne tuple – System.Text.Json položky tuple neserializuje. Strany páru sledují klíč, ne směr vztahu: **`Source` je sloupec na straně, která fyzický cizí klíč drží** (u `Owning` vztahu zdrojová entita, u `Inverse` cílová), **`Target` je odkazovaná část klíče** — u `Owning` primární klíč cílové entity, u `Inverse` vlastní klíč zdrojové. Tak to čtou oba buildery: `<many-to-one>` i `<key>` vypisují `Source`, EF Core builder bere z `Target` typ a jméno dogenerované vlastnosti. **Prázdný seznam znamená, že sloupce nejsou rozresolvované.** Pořadí párů je autoritativní a buildery je nepřeskládávají: rozpor s pořadím klíče, na který míří, hlásí fáze rozresolvování záznamem o neúplnosti (viz §5.1) a páry se vypíšou tak, jak jsou uložené — potichu spravovat je při emisi by zakrylo vadu producenta.

Páry plní fáze rozresolvování jmen entit, kterou `Build()` spouští po naparsování všech entit převodu a před generováním (`ResolveEntityNames` v `AbstractEntityBuilder`; slíbena v důsledcích rozhodnutí [001](./decisions/001-entity-reference-by-name.md)). Parsery předávají přes `AddForeignKey` sloupce cizího klíče tak, jak je zdroj uvedl, a fáze je spáruje s odkazovaným klíčem, jakmile se název cílové entity rozresolvuje proti `EntityMaps` — párování je tedy jen mezi entitami téhož převodu; odkaz mimo převod je případ databázového katalogu (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)). Sloupec, za kterým nestojí žádná deklarovaná vlastnost, dostane odpojenou `PropertyMap` jen uvnitř páru — do `PropertyMaps` entity nepatří, sloupec není vlastnost. Nespáruje se nic, kde počet sloupců nesouhlasí s počtem částí klíče nebo kde se cíl nenajde — obojí fáze hlásí záznamem o neúplnosti podle rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md) (viz §5.1) — a N:M vůbec — jeho sloupce patří spojovací tabulce (rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md)); N:M vztah bez spojovací entity v převodu dostává rovněž záznam. Táž fáze dokončuje druhou polovinu pravidla o referencích z rozhodnutí [014](./decisions/014-language-type-model.md): typ `Unknown`, jehož jméno je přesně jménem entity téhož převodu, se povýší na `Reference`, u kolekcí totéž pro typ prvku.

Entity se ve vztahu odkazují jménem, ne referencí (rozhodnutí [001](./decisions/001-entity-reference-by-name.md)).

N:M nemá vlastní typ vztahu. Skládá se ze spojovací entity s `IsJunctionTable = true` a dvou `Owning` relací s kardinalitou `ManyToOne`, které z ní míří k oběma stranám:

```
StudentCourse (IsJunctionTable = true)
 ├─ Relation(Owning, ManyToOne) → Student   (ColumnPairs: [StudentId ↔ Id])
 └─ Relation(Owning, ManyToOne) → Course    (ColumnPairs: [CourseId ↔ Id])
```

„Bohatá" spojovací tabulka s vlastními sloupci navíc je z pohledu modelu prostě normální entita se dvěma cizími klíči. `IsJunctionTable` je volitelný signál, že tabulka je „čistá" – dnešní buildery ho k ničemu nepotřebují (rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md)).

Do tohoto tvaru převádí N:M vztahy zdroje **fáze syntézy junction entit** (`SynthesizeJunctionEntities` v `AbstractEntityBuilder`), kterou `Build()` spouští před rozresolvováním. Z faktů, které zdroj o spojovací tabulce uvedl — u NHibernate `table` a `schema` atribut kolekce, sloupce jejího `<key>` a sloupce `<many-to-many>` prvku, předané parserem jako `JunctionFacts` — postaví spojovací entitu: vlastnosti pojmenované po sloupcích s typy zkopírovanými z odkazovaných částí klíčů, složený klíč přes všechny tyto sloupce (`Assigned`), dvě `Owning` relace N:1 s navigacemi pojmenovanými po cílových entitách a `IsJunctionTable = true`. Obě strany popisují touž tabulku z opačných konců, takže si fakta doplní navzájem a jednosměrné N:M stačí popsat z jedné strany. Kolekce obou stran se přesměrují na junction entitu jako inverzní 1:N — typ prvku kolekce se změní na junction entitu, což je tvar varianty B z rozhodnutí 005. Jediné, co zdroj netvrdí, je název třídy: odvozuje se z názvu tabulky (heuristika koncového „s") a hlásí se záznamem o konvenci. Chybí-li název tabulky nebo sloupce některé strany, syntéza se zdrží, vztah zůstane N:M — NHibernate builder ho pak vypíše jako `<many-to-many>` kolekci bez sloupců — a chybějící spojovací entitu ohlásí fáze rozresolvování jako dosud; nesoulad počtu sloupců s klíčem nebo kolize jmen dostanou vlastní záznam o neúplnosti. Sebereference (N:M entity na sebe samu) se nesyntetizuje.

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
- EF Core parser kromě atributů čte i konvenci primárního klíče: nemá-li entita `[Key]` ani třídní `[PrimaryKey]`, klíčem se stane skalární vlastnost `Id`, jinak `<název entity>Id`, a to bez ohledu na velikost písmen. Klíč z konvence je tvrzení zdroje, ne domněnka, takže má v prioritě zdrojů první stupeň (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)) a v cílovém artefaktu se stává explicitním. Bez toho by entity s konvenčním klíčem — v EF Core běžné — vycházely z převodu jako bezklíčové. Touž logikou se čte strategie klíče: parser bere `[DatabaseGenerated]`, kde je, a jinde ji odvodí z typu klíče, protože EF Core generuje hodnotu sám u jednosloupcového celočíselného klíče a u `Guid`, jinde ne; části kompozitního klíče proto dostávají `Assigned`. Anotace `Identity` netvrdí sloupec IDENTITY, ale „hodnotu vytvoří úložiště při vložení", takže se mapuje na `Auto`. Opačným směrem builder anotaci vypíše jen tam, kde mění, co by EF Core udělalo samo: zopakovat konvenci cíle je šum, kdežto řetězcový klíč označený `Auto` by bez ní generování tiše ztratil. Mechanismy `Identity`, `Sequence`, `HiLo`, `Uuid` a `Increment` anotace nevyjádří vůbec — jsou dostupné jen fluent API, takže je builder zahazuje a hlásí záznamem o ztrátě (viz §5.1).
- NHibernate XML parser čte u `<id>` nejen třídu generátoru, ale i jeho `<param>` prvky a název, který slovník strategií nezachytil; builder parametry vypisuje zpět jako vnořené prvky, protože jinak by vzniklo mapování odkazující na sekvenci, kterou si cíl zvolí sám. `<composite-id>` generátor nepřipouští, takže části kompozitního klíče jsou `Assigned` a per-part strategii jinou než `Assigned`/`Unspecified` builder zahazuje a hlásí záznamem o ztrátě. Strategii, kterou nikdo neuvedl, builder vypisuje jako `assigned` — konvence cíle, ne tvrzení zdroje (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)) — a hlásí ji záznamem o konvenci; název generátoru, který se do kanonického zápisu nevrátí (`seqhilo`, `guid.comb`, `foreign`), hlásí záznamem o ztrátě (viz §5.1). Hodnotu `property-ref`, kterou model neudrží, hlásí už XML parser při čtení.
- U `<composite-id>` čte parser navíc atributy `class` a `name` a ukládá je jako `SourceKeyClass`; do výstupu se nevracejí, protože vykreslení je ploché (rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md)). Části klíče přitom bere jen z prvků `<key-property>` — `<key-many-to-one>`, tedy část klíče, která je zároveň odkazem na jinou entitu, se přeskočí bez hlášení a klíč vyjde kratší, než jak ho zdroj popsal.
- Sloupce cizího klíče čtou parsery a předávají je fázi rozresolvování (viz §4.3): NHibernate XML parser bere `column` atribut nebo vnořené `<column>` prvky z `<many-to-one>` i z `<key>` uvnitř kolekce (vnořená forma má přednost, stejně jako u vlastností); u `<many-to-many>` navíc čte `table` a `schema` atribut kolekce a sloupce `<many-to-many>` prvku a předává je jako `JunctionFacts` fázi syntézy junction entit (viz §4.3). EF Core parser čte `[ForeignKey("A,B")]` na navigační vlastnosti — jména dělí čárkou a pořadí zachovává, protože odpovídá pořadí klíče, na který anotace míří. Vlastnost s `[ForeignKey]`, jejíž typ není skalár, je tím prohlášená za navigaci (N:1, `Owning`; 1:1 anotace vyjádřit neumí). Skalární typ vedle `[ForeignKey]` znamená druhou zákonnou formu anotace — na klíčové vlastnosti jmenuje navigaci — a ta se zatím nečte.
- NHibernate builder volí značku vztahu podle role a strategie klíče: `<many-to-one>` u N:1, `<many-to-one unique="true">` u vlastnící strany 1:1, `<one-to-one constrained="true">` u entity se sdíleným primárním klíčem a `<one-to-one>` bez atributů na straně bez cizího klíče. Sloupce bere z `ColumnPairs` — jeden atributem `column`, víc vnořenými `<column>` v uloženém pořadí. Bez známých párů atribut u `<many-to-one>` vynechá, protože NHibernate doplní sloupec podle názvu vlastnosti, ale u `<key>` uvnitř `<bag>` vypíše sloupec klíče vlastníka: tam je výchozí jméno pevné `id` (`Collection.DefaultKeyColumnName`), takže mlčení význam mění, místo aby ho jen neuvádělo. Sloupec smí být v mapování zapisovatelný jen jednou, jinak NHibernate mapování odmítne jako opakovaný sloupec; kde tentýž sloupec nese skalární vlastnost i vztah — typický tvar Dapper třídy se sloupcem cizího klíče vedle navigace — dostane `<property>` atributy `insert="false" update="false"` a zápis vlastní vztah, a kde jsou sloupce vztahu částmi primárního klíče, dostane tytéž atributy `<many-to-one>`, protože zápis vlastní identifikátor.
- EF Core builder vypisuje `[ForeignKey]` na navigační vlastnosti, a to jen na vlastnící straně; anotace jmenuje vlastnosti třídy, ne sloupce. Chybí-li vlastnost pro sloupec cizího klíče, builder ji dogeneruje: jméno složí z navigace a názvu části cílového klíče (`OrderLineOrderID`), typ převezme z části klíče, na kterou pár míří, jméno sloupce vypíše jako `[Column]` a nullabilitu přebírá po navigaci. Bez známých párů anotaci nevypíše a odvození nechá konvenci EF Core.
- Parsování dotazů je implementované jen pro EF Core LINQ (`EFCoreLinqQueryParser` dědí z Roslyn `CSharpSyntaxWalker`, přepisuje `Visit*` metody pro analýzu řetězených volání).
- Generování dotazů je implementované jen pro Dapper SQL (přes `StringBuilder` + dedikovaný visitor `DapperSQLQueryVisitor`).
- Entity buildery fungují stejně napříč frameworky – skládají string šablony; NHibernate builder plní dva `StringBuilder`y najednou a vrací dva výstupy (C# entitu + XML mapování).
- Kompozitní klíč vykresluje EF Core builder atributem `[PrimaryKey]`, NHibernate builder elementem `<composite-id>` s `<key-property>` a k tomu na entitní třídu doplní `[Serializable]` a přepisy `Equals`/`GetHashCode`. Bez nich NHibernate shodí stavbu session factory hláškou `composite-id class must override Equals()`.
- Sloupec se v mapování NHibernate zapisuje atributem `column`, nebo vnořeným elementem `<column>`. Parser čte obojí a vnořený element má přednost; builder ho použije jen tehdy, když má co do něj dát. Není to volba stylu: `<property>` unese délku, přesnost i měřítko jako vlastní atributy, ale `<id>` a `<key-property>` ne, takže bez vnořeného elementu by klíčový sloupec tyto údaje ztratil. Nullabilita se u klíče nevypisuje, protože sloupec nesoucí identifikátor nullable není. Čtou se první `<column>` a jen ty atributy, které mají v modelu protějšek — `sql-type` patří k neutralizaci typového modelu. Precision menší než 1 builder nevypisuje vůbec: XSD mapování NHibernate připouští jen kladnou hodnotu, takže `[Precision(0)]` u datetime sloupce se zahazuje se záznamem o ztrátě (odhalil to 3. stupeň ověření, viz §6.2); udržet by ji uměl až `sql-type`.
- Dapper builder má kroky `BuildPrimaryKey` a `BuildForeignKey` prázdné, protože Dapper mapování klíčů nemá; prázdné tělo je tvrzení o frameworku, ne opomenutí. Zahozené fakty hlásí mechanicky vzniklé záznamy o ztrátě podle rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md) — builder je nevypisuje ručně, plynou z deskriptoru (viz §5.1).
- Vlastnosti cílového frameworku jsou deklarované v deskriptoru (`TargetFrameworkDescriptor` v `AbstractWrappers.Descriptors`, konkrétní instance ve wrapperech). Deskriptor uvádí vynucené členy — co builder přidá ke generovanému artefaktu, ačkoli to není fakt o doméně — a vztah ke kategoriím mapovacích faktů ve stavech *vyžaduji*, *umím vyjádřit*, *neumím vyjádřit*. Dapper má všechny kategorie ve třetím stavu a žádné vynucené členy; jediná kategorie, ve které se plnohodnotné frameworky rozcházejí, je primární klíč — NHibernate ho vyžaduje, EF Core na jeho absenci reaguje bezklíčovým typem. Import se za vynucený člen nepovažuje: plyne z prvku, který se generuje, takže patří builderu. Společný test EnforcedMembersTest ověřuje každý generovaný artefakt proti deskriptoru jeho frameworku, včetně toho, že člen chybí, když jeho podmínka neplatí. Ověřuje se výskytem řetězce, což je vědomý strop: u modifikátoru virtual test odhalí, že vypadl úplně, ne že chybí u jedné vlastnosti. Přísnější kontrola by znamenala výstup parsovat, což je proti smyslu deskriptoru — ten popisuje, co framework vyžaduje, ne jak vypadá vygenerovaný kód. Kromě testu čte deskriptor i diagnostika: brána úplnosti bere kategorie ve stavu *vyžaduji* a mechanické záznamy o ztrátě kategorie ve stavu *neumím vyjádřit* (viz §5.1) — deskriptor tím má prvního konzumenta v produkčním kódu. Buildery podmínku, za které člen platí, vyhodnocují samostatně — `NHibernateEntityBuilder` se ptá vlastní metody `HasCompositeKey`, ne deskriptoru. Je to vědomé: párovat emisi s deklarací by znamenalo hledat členy podle názvu, a překlep v něm by generování tiše vypnul, aniž by to test odhalil, protože by porovnával tentýž překlep na obou stranách. Dělba je tedy trojí — deskriptor deklaruje, builder implementuje, test je váže. Rozhodnutí [009](./decisions/009-target-framework-descriptor.md).
- Atribut `type` u `<id>` a `<key-property>` bere NHibernate builder z databázového typu v `PropertyMap`; chybí-li, odhadne ho z jazykového skaláru tabulkou `GuessFromScalarType` ve wrapperu (výchozí předpoklad NHibernate, např. `String` → `nvarchar`). U reference, kolekce, neznámého jména nebo `Object` netvrdí nic a atribut vynechá — NHibernate si typ odvodí z perzistentní třídy sám.
- Převod typů není mezi frameworky symetrický. Konvertor pro EF Core je u data a času bijekce (`datetime`, `datetime2`, `smalldatetime` tam i zpět), protože `HasColumnType` bere doslovný SQL typ. Konvertor pro NHibernate slévá `DatabaseType.DateTime`, `DateTime2` i `SmallDateTime` do jediného `type="DateTime"` a zpětný převod vrací všechny tři jako `DateTime2`. Round-trip přes NHibernate tedy z `datetime` i `smalldatetime` udělá `datetime2` – idempotence po první normalizaci platí, identita ne; u `smalldatetime` je to navíc změna přesnosti z minuty na 100 ns a rozsahu z 1900–2079 na 0001–9999. Příčinou není chyba v převodní tabulce, ale to, že `type` v mapování NHibernate je typ NHibernate, ne SQL typ – konkrétní SQL typ z něj odvozuje dialekt. Pod zafixovanými verzemi to odpovídá i skutečnosti: NHibernate 5.7.0 s `MsSql2012Dialect` vyrobí z typu `DateTime` sloupec `datetime2`, protože dialekty pro SQL Server 2008+ ho používají od verze 5.0.0, a typ `DateTime2` je od téže verze obsoletní. Rozdíl by šlo udržet přes `sql-type` na `<column>`, to ale vyžaduje znát cílový dialekt, který se dnes nikde nedeklaruje.

### 5.1 Diagnostika převodu

Převod vrací vedle artefaktů i diagnostické záznamy (rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md)): `ConversionHandler.Convert` vrací `ConversionResult` se seznamy `Sources` a `Records` a časem `CatalogReadTime` (viz §5.2) a REST endpoint `/convert` všechno propisuje do `ConvertResponse` (čas jako `CatalogReadMilliseconds`). Frontend záznamy zatím nezobrazuje — jeho úpravy jsou vedené zvlášť v [`open-items.md`](./open-items.md). Výjimky zůstávají vyhrazené chybám programu (nepodporovaný framework, neparsovatelný vstup); stav, se kterým návrh počítá, končí záznamem, ne pádem.

Záznam je `ConversionRecord` (`AbstractWrappers.Diagnostics`) a nese, co žádá F11: druh, cílový framework, artefakt, entitu, vlastnost, kategorii mapovacího faktu a důvod. Druh (`ConversionRecordKind`) není stupnice závažnosti, ale výčet definovaných událostí:

- **`Failure`** — kategorie, kterou deskriptor uvádí jako *vyžaduji*, chybí, nebo vlastnost nemá jazykový typ (zná ji jen mapování, ne entitní třída). Artefakty entity se negenerují; dřívější pád `NotSupportedException` uprostřed generování se tím stal záznamem.
- **`Loss`** — fakt, který zdroj nesl a cíl nevyjádří: kategorie ve stavu *neumím vyjádřit* (celý Dapper), fluent-only strategie klíče u EF Core, strategie části `<composite-id>`, název generátoru, který kanonický zápis nevrátí, hodnota `property-ref`. Artefakt vzniká a je platný, jen chudší než vstup.
- **`Convention`** — výstup tvrdí něco, co zdroj neřekl: `assigned` za neuvedenou strategii, vynechaný `column`/`[ForeignKey]` s odvozením ponechaným konvenci cíle, sloupec klíče vlastníka dosazený do `<key>` (konvence třetího stupně podle rozhodnutí [012](./decisions/012-foreign-key-rendering.md)), jazykový typ odvozený z databázového typu i název třídy syntetizované junction entity odvozený z názvu tabulky (viz §4.3 a §5.2).
- **`Incompleteness`** — mezireprezentaci chybí fakt, který by výstup potřeboval: nenalezené jméno cílové entity (překlep nerozlišitelný od legitimního odkazu mimo převod — rozhodne až katalog, rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)), počet sloupců neodpovídající klíči, pořadí párů neodpovídající klíči, N:M bez spojovací entity. Sem patří i stavy fáze doplnění (viz §5.2): nenakonfigurované nebo nedostupné připojení ke katalogu, entita bez odpovídající tabulky, vlastnost bez odpovídajícího sloupce, cizí klíč bez navigační vlastnosti. Generuje se z toho, co je.
- **`Supplied`** — fakt, který zdroj neuvedl, dodal databázový katalog. Záznam je původem faktu — model ho nenese (rozhodnutí 010 a 015).
- **`Conflict`** — zdroj a katalog se neshodují. Zdroj má přednost (pravidlo E9, rozhodnutí 015), překlad pokračuje se zdrojovou hodnotou a záznam říká, co tvrdil katalog.

Záznamy vznikají ve dvou okamžicích, jak je má rozhodnutí 010: **kontrola úplnosti proti deskriptoru běží před generováním** (`CheckCompleteness` v `AbstractEntityBuilder` — brána, která entitu bez vyžadované kategorie odmítne dřív, než se napíše půlka artefaktu; sem patří i hlášení fáze rozresolvování), **záznamy o ztrátě vznikají při emisi** (`ReportLosses` mechanicky jako průnik faktů modelu s kategoriemi *neumím vyjádřit* deskriptoru; specifická zúžení hlásí buildery v místě emise). Kanálem je kolekce `Records` na `AbstractEntityBuilder` s veřejnou metodou `Report` — veřejnou proto, že ztráta může nastat i cestou do modelu: XML parser NHibernate tak hlásí zahozenou hodnotu `property-ref`. Čtečka katalogu k tomu později přidá vlastní druhy záznamů (původ doplněného faktu, konflikt se zdrojem).

Vyžadovanou kategorii má dnes jediný framework: NHibernate primární klíč. Entita bez klíče se tedy do NHibernate odmítne se záznamem `Failure`, kdežto do EF Core projde jako `[Keyless]` typ.

### 5.2 Doplňování mapovacích faktů z katalogu

Implementace rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md) žije v projektu `DatabaseCatalog` a dělí mechanismus od řízení:

- **Mechanismus** je `SqlServerCatalogReader` za rozhraním `ICatalogReader`. Celá dávka požadavků se obslouží jedním připojením a třemi dotazy do `sys.*` pohledů (sloupce, primární klíče, cizí klíče) bez ohledu na počet tabulek — `INFORMATION_SCHEMA` nestačí, protože IDENTITY a pořadí částí klíče nese jen `sys`. Čtečka vrací celý sloupcový obraz tabulky (`TableImage`): sloupce s typem, délkou, precision/scale, nullabilitou a příznakem IDENTITY (délka znakových typů v znacích, ne bajtech; `MAX` je bez délky; u datetime2/time/datetimeoffset se jako precision vrací zlomkové vteřiny), části primárního klíče v pořadí klíče a cizí klíče s páry sloupců. SQL typ mimo slovník `DatabaseType` znamená, že se typ prostě nedodá. Tabulku hledá podle kandidátů, které formuluje volající: uvedený název tabulky je jediný přesný kandidát, entita bez tabulky nabízí své jméno a variantu s/bez koncového `s` (`TableNameCandidates` — jediné místo té heuristiky, používá ho i benchmarking). Shoda ve víc schématech preferuje `dbo`; jiná víceznačnost se vrací jako seznam a hlásí, nehádá se.
- **Řízení** je `CatalogCompletion.Complete`, fáze mezi parsováním a generováním v `ConversionHandler.Convert`. Poptávka je sjednocení kategorií deskriptoru ve stavech *vyžaduji* a *umím vyjádřit*; prázdná poptávka (Dapper jako cíl) znamená nulový dotaz. Zápis je přírůstkový a idempotentní: vyplní se jen prázdný fakt a nese záznam `Supplied`, obsazený fakt se s katalogem porovná a rozpor skončí záznamem `Conflict` se zachovanou zdrojovou hodnotou. Název sloupce shodný se jménem vlastnosti se nezapisuje — každý builder ho doplní sám, takže by zápis nic netvrdil.

Primární klíč se z katalogu dodá jen tam, kde každý sloupec klíče má vlastnost, na kterou se dá pověsit — vymyslet člen třídy fáze nesmí, takže jinak vznikne záznam a klíč se nedodá. IDENTITY sloupec dostane strategii `Identity`; ne-IDENTITY zůstává `Unspecified`, protože „databáze hodnotu negeneruje" neříká, kdo ji přiřazuje. U klíče ze zdroje se doplní jen strategie `Unspecified` část po části a hlásí se rozpor tvrzené generovanosti s IDENTITY (`Auto` se s IDENTITY nevylučuje — je to týž fakt viděný ze strany zdroje).

Cizí klíče se řeší až po klíčích všech entit, aby odkazovaný klíč už stál. Klíč katalogu, jehož odkazovaná tabulka patří entitě téhož převodu, buď doplní páry existujícímu vztahu bez sloupců, nebo — když vztah neexistuje — syntetizuje nový přes navigační vlastnost (vlastnost typu reference nebo `Unknown` se jménem cílové entity) voláním `AddForeignKey`, takže párování proběhne standardní fází rozresolvování; pokrývá-li cizí klíč celý primární klíč entity, je to 1:1, jinak N:1. Bez navigační vlastnosti se vztah nevymýšlí a vznikne záznam — to je běžný tvar Dapper třídy jen se sloupcem cizího klíče. Odkaz na tabulku mimo převod se tiše přeskočí; zrcadlový případ (navigace bez cíle) hlásí už fáze rozresolvování. Sloupce se řadí podle odkazovaného klíče (rozhodnutí [012](./decisions/012-foreign-key-rendering.md)).

Součástí fáze je i **odvození jazykového typu** vlastnosti, kterou zná jen mapování: `LanguageTypeInference` převede databázový typ na skalár uzavřeného seznamu (typ bez protějšku — `time`, `datetimeoffset`, binární typy — nic netvrdí a vlastnost odmítne až brána úplnosti), vlastnost dostane `public` a přístupové metody, protože bez nich by nešla deklarovat, a vznikne záznam `Convention` — konvence třetího stupně (rozhodnutí 015). Odvození běží i bez připojené databáze, protože databázový typ může nést už zdroj.

**Připojení je nepovinný vstup překladu.** REST endpoint `/convert` ho čte z konfigurace `ConnectionStrings:CatalogDatabase`; bez něj překlad proběhne na konvencích a nese záznam `Incompleteness`, že katalog nebyl k dispozici, a totéž platí pro nakonfigurované, ale nedostupné připojení — výjimka se nepropaguje (rozhodnutí 015). Fáze se měří samostatně (S3): `ConversionResult.CatalogReadTime`, v odpovědi API `CatalogReadMilliseconds`; null znamená, že fáze neměla co dělat.

Kvalifikace názvů tabulek pro benchmarking (`HarnessGenerationUtilities.QualifyEntityTableNames`) používá touž čtečku jednou dávkou; dřívější druhá odpověď s dotazem na entitu a prázdným `catch` zmizela — selhání spojení se tam propaguje, protože benchmark databázi stejně potřebuje. Heuristika `EFCoreLinqQueryParser.ResolveQualifiedTableName` zůstává, ale už není druhou odpovědí na touž otázku: čte `EntityMaps`, do kterých fáze doplnění schéma a tabulku propsala před parsováním dotazů, takže priorita zdrojů platí i pro ni a parser sám na databázi nesahá (S1).

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

### 6.1 Testovací databáze

Databázově závislé testy se podle rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md) připojují k **lokální instanci SQL Serveru a schéma si vytvářejí samy**. Všechno k tomu potřebné je v `Tests/Database/`.

**Připojení je konfigurace, ne kód.** `TestDatabase` skládá konfiguraci ze dvou zdrojů – user secrets testovacího projektu (`UserSecretsId` je v `Tests.csproj`) a proměnných prostředí – a čte z nich připojovací řetězec pod jménem `TestDatabase`, tedy klíč `ConnectionStrings:TestDatabase` v user secrets nebo proměnná `ConnectionStrings__TestDatabase`. V repozitáři řetězec není (S4); pojmenování je stejné jako u `ConnectionStrings__AdvisorDatabase` v `docker-compose.yml`. Název schématu je natvrdo `ormconvertor_test`, aby se běh dal prohlédnout ručně; proměnná `ORMCONVERTOR_TEST_SCHEMA` ho přebije (musí to být prostý SQL identifikátor, jinak se odmítne – dosazuje se přímo do DDL).

**Schéma je zároveň očekávanou odpovědí.** Popisuje ho SQL skript `Database/TestSchema.sql`, vložený do sestavení jako embedded resource; místo názvu schématu je v něm zástupný symbol `{{schema}}`. Skriptem, ne kódem, protože se dá spustit i ručně proti lokální instanci a je to čitelný zápis toho, proti čemu se podle F4 měří podíl správně získaných metadat. Obsahuje právě ty případy, o které jde: jednosložkový klíč generovaný `IDENTITY` (`Customers`, `Suppliers`) i přiřazený aplikací (`Products`), složené klíče o dvou (`Orders`, `ProductSuppliers`), třech (`OrderLines`) a čtyřech částech (`OrderLineAllocations`), jedno-, dvou- a třísloupcové cizí klíče, vztah 1:1 přes sdílený primární klíč (`CustomerProfiles`), spojovací tabulku (`ProductSuppliers`) a sadu typů s délkou, precision/scale i nullabilitou.

**Životní cyklus a hranice transakce.** `TestSchemaFixture` je collection fixture: schéma vznikne jednou pro celou kolekci `TestDatabaseSchema` a po jejím doběhnutí se zahodí. Jednou za kolekci proto, že čtečka katalogu čte metadata a ta se zapisují jednou a čtou mnohokrát. Před vytvořením se schéma nejdřív zahodí, takže zbytek po spadlém běhu další běh neblokuje; úklid je psaný proti systémovému katalogu (zruší všechny cizí klíče, pak všechny tabulky, pak schéma), takže přidání tabulky do skriptu ho nerozbije. Fixture sama žádnou transakci nedrží – DDL je sdílené a pro 2. a 3. stupeň ověření jen ke čtení. Test 4. stupně, který zapisuje řádky, si otevře vlastní transakci nad `OpenConnection()` a vrátí ji zpět, aby žádný test nezávisel na tom, co po sobě nechal jiný.

**Chybějící databáze test přeskočí, ne shodí.** `SkipIfUnavailable()` na fixture volá dynamické přeskočení xUnit v3 s uvedeným důvodem. Rozlišují se dva: buď není nakonfigurovaný připojovací řetězec (důvod říká, kam ho zapsat), nebo nakonfigurovaný je, ale schéma se nepodařilo připravit (důvod nese chybu spojení). Ani jeden případ testy neshodí – to je záměr, ne tolerance: sada nemá tvrdit opak toho, co tvrdí nástroj, u kterého překlad bez připojené databáze selhat nesmí. Přeskočení je ale samo tvrzením o pokrytí, takže kritéria F4 a F6 platí jen tam, kde běh s databází skutečně proběhl.

**Co fixture hlídá sama.** `TestSchemaFixtureTest` ověřuje, že schéma opravdu obsahuje, co slibuje – existenci tabulek, pořadí částí u všech čtyř velikostí klíče, sloupcové páry vícesloupcových cizích klíčů, spojovací tabulku (celý klíč je složený z cizích klíčů) a u vybraných sloupců typ, nullabilitu, délku, precision/scale a příznak `IDENTITY`. Bez toho by fixture mohla tiše přestat obsahovat případ, o který jde, a každý pozdější verdikt o čtečce katalogu by tím ztratil smysl.

Testovací projekt kvůli tomu referencuje `Microsoft.Data.SqlClient` a konfigurační balíky; **S1 to neporušuje** – wrappery zůstávají bez závislosti na frameworku, pro který generují. CI zatím databázi nemá, takže tyhle testy tam přeskakují; kontejnerová konfigurace podle S5 je otevřená položka.

### 6.2 Ověření generovaných artefaktů

Rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md) uznává čtyři stupně ověření. Implementované jsou první tři; čtvrtý (běh proti databázi) zůstává otevřený. Testy 2. a 3. stupně jsou v `Tests/Verification/` a na převodu EF Core ↔ NHibernate běží nasucho — žádné spojení se nekonfiguruje ani nepokouší; nasucho běží i ověření syntézy junction entity (`ManyToManyJunctionVerificationTest`), protože zdroj o spojovací tabulce tvrdí všechno sám.

**1. stupeň — tvar** jsou dosavadní řetězcové aserce v testech parserů a builderů. Zůstávají, protože jako jediné umí tvrdit, *jak* je artefakt zapsán (S2).

**2. stupeň — překlad.** Generované C# kompiluje `CSharpSourceCompiler` v `Common.Compilation` — jediné místo v řešení, kde se přes Roslyn kompiluje (viz §2): vrací diagnostiky a sestavení, nic nezavádí a nevyhazuje; `AdvisorBenchmarking` na témž kroku staví a přidává zavedení do `AssemblyLoadContext`. Kompilační prostředí zrcadlí konzumentský projekt, pro který artefakt vzniká: implicitní usings SDK a reference balíku cílového frameworku dodává testovací harness (`GeneratedEntityCompiler`), protože jsou příspěvkem projektu, ne artefaktu. Mapování NHibernate se validuje proti XSD, které NHibernate nese jako embedded resource — je to schéma přesně té verze, proti které běží 3. stupeň, takže se s balíkem nemůže tiše rozejít.

**3. stupeň — přijetí frameworkem.** NHibernate: `BuildSessionFactory` nad konfigurací s dialektem `MsSql2012Dialect`, explicitně uvedeným driverem `MicrosoftDataSqlClientDriver` (výchozí driver dialektu reflektuje System.Data.SqlClient, který v řešení záměrně není) a vypnutým `hbm2ddl.keywords` — jediným krokem stavby factory, který by jinak otevíral spojení. Jméno assembly, ve které perzistentní třída žije, je fakt konzumentského projektu, ne převodu; harness ho proto dodává atributem `assembly` na `<hibernate-mapping>` tam, kde builder název nekvalifikoval sám. EF Core: sestavení modelu z `DbContext`u, který zaregistruje všechny veřejné třídy zkompilovaného artefaktu, s `UseSqlServer()` bez připojovacího řetězce a s vypnutou cache interního service provideru (`EnableServiceProviderCaching(false)`) — EF Core cachuje model podle typu kontextu, a protože harness používá jediný typ kontextu pro všechna ověření, sdílená cache by každému dalšímu volání vrátila model prvního bez sestavení i bez validace. Přístup k `Model` tak pokaždé spustí validaci modelu včetně relačních pravidel a test se pak modelu ptá zpět na fakty zdroje (tabulka, schéma, části klíče, cizí klíč). K oběma směrům patří negativní kontroly: každý stupeň je předveden, jak vadný artefakt odmítá — mapování na neexistující vlastnost, `<composite-id>` třída bez `Equals`/`GetHashCode` (třída chyb z rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md)), entita bez klíče u EF Core. Stupeň, který nikdy neřekne ne, by nedokazoval nic.

První úlovek: XSD i `BuildSessionFactory` odmítly `precision="0"`, které builder NHibernate vypisoval z `[Precision(0)]` u datetime sloupce — schéma mapování připouští jen kladnou precision. Builder hodnotu menší než 1 nyní zahazuje se záznamem o ztrátě (viz §5).

Tvrzení „framework to přijme" je tvrzením o verzi balíku v `Tests.csproj` (NHibernate, `Microsoft.EntityFrameworkCore.SqlServer`); dokud deskriptor verzi nenese (rozhodnutí [013](./decisions/013-target-framework-versions.md), otevřená položka), musí souhlasit s tabulkou verzí v §1 a shoda se hlídá ručně.

**Scénáře se zdrojem v Dapperu** — doslovné kritérium F6 — existují se čtečkou katalogu a běží proti testovací databázi v kolekci `TestDatabaseSchema` (`DapperToNHibernateVerificationTest`, `DapperToEFCoreVerificationTest`): celá pipeline přes `ConversionHandler.Convert` s připojením, pak 2. stupeň (kompilace, XSD) a 3. stupeň (session factory; u EF Core se finalizovaný model ptá zpět na tabulku, schéma, pořadí částí klíče a cizí klíče, které mohly přijít jedině z katalogu). Bez nakonfigurované databáze se přeskakují s uvedeným důvodem (§6.1). Mechanismus a řízení fáze doplnění mají vedle toho vlastní testy: `SqlServerCatalogReaderTest` proti schématu fixture (F4 — podíl správně získaných metadat) a `CatalogCompletionTest` nad falešnou čtečkou bez databáze (priorita zdrojů, idempotence, prázdná poptávka, syntéza vztahů, odvození jazykového typu).

**Frontend (Angular):** je potřeba zkompilovat zvlášť a zkopírovat do `wwwroot`, odkud ho servíruje ASP.NET:

```
npm install
ng build --configuration "production" --base-href "/orm/" --deploy-url "/orm/"
# a zkopírovat dist/browser/* do ../wwwroot/
```

**Kontejnerizované nasazení:** v repozitáři je i `docker-compose.yml` (aplikace + SQL Server s WideWorldImporters přes `database.Dockerfile`) a vícestupňový `ORMConvertorAPI/Dockerfile`, který sestaví frontend (Node), nativní knihovnu Advisoru (`libadvisor.so`, gcc + GLPK) i .NET aplikaci. Soubor `ecosystem.config.js` je konfigurace pro proces manager PM2 (nasazení mimo Docker). Tahle cesta zatím není podrobněji zdokumentovaná – viz [`open-items.md`](./open-items.md).

## 7. Rozhraní parserů a builderů

`AbstractEntityBuilder` odděluje dvě sady metod. **Naplnění mezireprezentace** řeší veřejné metody implementované přímo v abstraktní třídě (framework-nezávislé): `BeginEntity` (začátek další entity – builder umí držet víc entit najednou v `EntityMaps`), `AddNamespace`, `AddClassHeader`, `AddSchema`, `AddTable`, `AddProperty`, `SetPropertyDatabaseMapping` (databázové detaily mapování – sloupec, typ, délka, precision/scale, nullabilita, ostatní klíč–hodnota), `AddPrimaryKey`, `SetKeyStrategyDetails`, `AddForeignKey` a `AddRelation`. `AddPrimaryKey` má dvě přetížení: seznam trojic `(PropertyName, Order, Strategy)` pro kompozitní klíč a pohodlnou zkratku pro jednoduchý klíč; kompozitní přetížení navíc bere nepovinný záznam o klíčové třídě zdroje. `SetKeyStrategyDetails` doplňuje k jedné části klíče název strategie ze zdroje a parametry generátoru a volá se až po definici klíče – ten vzniká jediným voláním, takže opakované `AddPrimaryKey` detaily zahodí spolu s klíčem, který nahrazuje. `AddForeignKey` kromě registrace vztahu povýší jazykový typ navigační vlastnosti na referenci, u kolekcí na kolekci s referenčním prvkem (viz §4.1); nepovinným parametrem bere sloupce cizího klíče, jak je uvedl zdroj — builder je drží stranou modelu a spáruje je s klíčem cílové entity až ve fázi rozresolvování (viz §4.3).

**Generování výstupu** je šablonová metoda. Veřejná `Build()` je implementovaná v abstraktní třídě: nejdřív spustí fázi syntézy junction entit a fázi rozresolvování jmen entit (viz §4.3), protože obě potřebují kompletní množinu entit převodu, a pak iteruje přes `EntityMaps` — pro každou entitu nejdřív brána úplnosti `CheckCompleteness` (entita, které chybí vyžadovaná kategorie nebo jazykový typ vlastnosti, se odmítne se záznamem `Failure`, viz §5.1), pak mechanické záznamy o ztrátě `ReportLosses`, a teprve potom v pevném pořadí `BuildImports` → `BuildTableSchema` → `BuildPrimaryKey` → `BuildProperties` → `BuildForeignKey` → `BuildEnforcedMembers` → `FinalizeBuild`. Všech sedm kroků je `protected abstract` a bere `(EntityMap, EntityArtifact)`; `FinalizeBuild` navíc vrací výstupy, protože počet artefaktů se liší podle frameworku. `EntityArtifact` drží dva `StringBuilder`y (`Code` a `Mapping`) a příznak `ClassOpened` — NHibernate plní oba a vrací dva výstupy, Dapper i EF Core plní jen `Code`. Primární klíč se generuje **před** vlastnostmi, protože části klíče se vypisují v pořadí, které určuje klíč, ne deklarace vlastností. Krok, ve kterém framework nic negeneruje, má prázdné tělo — to je tvrzení o frameworku, ne mrtvý kód: Dapper má prázdné `BuildPrimaryKey`, `BuildForeignKey` i `BuildEnforcedMembers`, EF Core prázdné `BuildEnforcedMembers`, protože jeho jediný vynucený prvek stojí před hlavičkou třídy a vypisuje se v `BuildTableSchema`. Každý builder má vlastnost `Descriptor` odkazující na deskriptor svého frameworku a kolekci `Records` s metodou `Report`, kterou plní diagnostika (viz §5.1).

`AbstractQueryBuilder` přijímá podmínky hotové: `Where(ConditionNode)`, `Having(ConditionNode)` a `Join(JoinKind, left, right, ConditionNode, alias?)`. Parser si strom postaví sám rekurzivním průchodem zdrojové syntaxe a předá ho jedním voláním – to odpovídá tomu, jak `EFCoreLinqQueryParser` už dnes rekurzivně prochází syntaxi, a je to přirozenější než inkrementální mutace builderu.

`IParser` (entity/mapování) definuje jen dvě metody: `CanParse(contentType)` – zjistí, jestli parser umí daný vstupní formát (důležité tam, kde je mapování rozdělené do víc souborů, typicky NHibernate XML), a `Parse(source)`, která nemá návratovou hodnotu a místo toho naplňuje mezireprezentaci přes volání na `AbstractEntityBuilder`. `IQueryParser` je stejné, jen `Parse` navíc bere referenci na už naparsované mapování entit, protože dotaz sám o sobě často neobsahuje název tabulky/sloupce (typicky LINQ).

Původní návrh pro `AbstractQueryBuilder` (v `thesis/chapters/04_query_translation.tex` v původním repozitáři) počítá s `Push()`/`Pop()` metodami pro vstup/výstup z vnořeného poddotazu a se dvěma abstraktními výstupními metodami – `Build()` pro nativní syntaxi cílového frameworku (LINQ apod.) a `BuildSQL()` pro syrové SQL. Ověřeno proti kódu: `Push()`/`Pop()` implementované jsou (zásobník značek, `Pop()` obalí nasbírané instrukce do `SubQueryInstruction`; `SetOperation` je bez předchozího Push/Pop odmítnutá výjimkou). `BuildSQL()` ale **neexistuje** – je jen jediná abstraktní `Build()` vracející `List<ConversionSource>`; rozlišení nativní syntaxe vs. SQL bude potřeba dořešit při implementaci query builderů pro EF Core a NHibernate. Poddotazy jsou navíc ve visitor vrstvě dotažené jen napůl: `IQueryVisitor` nemá `Visit(SubQueryInstruction)` a `SubQueryInstruction.Accept` obsahuje `TODO` a vrací prázdný string (instrukce uvnitř poddotazu projde, ale výsledek se nikam neskládá).

## 8. Advisor – implementační detaily

ILP model je napsaný v C (`Advisor/ilp.c`) přímo přes GLPK C API (ne přes vyšší úroveň abstrakce): `glp_create_prob()` založí úlohu, `glp_add_cols()`/`glp_set_col_kind()` definují binární proměnné $x_{q,f}$ a $y_f$, `glp_set_obj_coef()` nastaví účelovou funkci, `glp_add_rows()`/`glp_set_row_bnds()` definují omezení, `glp_load_matrix()` nahraje řídkou matici koeficientů. Řešení spouští `glp_init_iocp()` (parametry solveru) a `glp_intopt()` (branch-and-bound). Výsledek se čte zpět přes `glp_mip_col_val()`.

C# strana (`Advisor.Solve`) volá tenhle wrapper přes P/Invoke – `[LibraryImport("libadvisor.so")]`. Knihovna `libadvisor.so` se kompiluje jen v Docker buildu (stage `advisor-native`: gcc + `libglpk-dev`); název je natvrdo linuxový, takže Advisor mimo Linux/Docker neběží – překladová část aplikace na tom nezávisí a funguje všude. Build krok pro Windows (`advisor.dll`) neexistuje, i když `ilp.c` má exportní makra připravená.

## 9. Co je záměrně mimo rozsah dnešní implementace

Výchozí stav při převzetí projektu popisuje [`baseline.md`](./baseline.md), otevřené položky a jejich pořadí [`open-items.md`](./open-items.md).

Stručně: podporované jsou tři .NET ORM, překlad dotazů je jednosměrný (jen EF Core → Dapper), Advisor pracuje jen s Dapperem a EF Core a napojení na databázi pro doplnění chybějících metadat neexistuje — `ColumnPairs` se rozresolvují jen mezi entitami téhož převodu, odkaz mimo převod na katalog teprve čeká. Javový ekosystém a cross-ecosystem překlad zatím nezačaly; jazyková strana typového modelu už je neutrální (`LangType`, rozhodnutí [014](./decisions/014-language-type-model.md)), databázová strana zůstává slovníkem T-SQL a její neutralizace je otevřené rozhodnutí.
