# Návrh přepracování abstraktního modelu ORMorpheru

**Rozsah:** podmínkový strom (WHERE/HAVING/JOIN), kompozitní primární klíče, vícesloupcové vztahy (1:1, 1:N, N:M)
**Stav:** návrh k implementaci, kód zatím neměním
**Kontext:** navazuje na `Model`, `AbstractWrappers`, `DapperWrappers`, `EFCoreWrappers`, `NHibernateWrappers` v mém repozitáři [`MiraStochel/orm-convertor`](https://github.com/MiraStochel/orm-convertor)
**Pokrývá požadavky:** F1–F3 z [docs/requirements.md](../requirements.md)

---

## 1. Proč to dělám

Tři nezávislé části současného modelu jsou ploché tam, kde realita plochá není:

| Co | Dnešní stav | Problém |
|---|---|---|
| `SelectInstruction` / `HavingInstruction` | jeden pár `(left, op, right)` na instrukci, více instrukcí se spojuje jen přes AND | nejde vyjádřit OR, závorky, vnořené podmínky |
| `JoinInstruction` | jeden pár `LeftProperty`/`RightProperty` | nejde vyjádřit JOIN přes vícesloupcový klíč |
| primární klíč | řetězcové příznaky `IsPrimaryKey`/`PrimaryKeyStrategy` v `PropertyMap.OtherDatabaseProperties`, vždy jedna vlastnost | nejde vyjádřit kompozitní klíč, natož jeho pořadí |
| `Relation` | visí na jednom `PropertyMap`, `Source`/`Target` jsou řetězce | nejde vyjádřit vícesloupcový FK; 1:N/N:M nemají na "mnoho" straně žádný reálný sloupec, na který by se dalo navěsit |

Všechny čtyři body spolu úzce souvisí – řešení je ve všech případech totéž: nahradit plochou dvojici/n-tici hodnot uspořádanou strukturou (strom u podmínek, uspořádaný seznam párů sloupců u klíčů/vztahů/joinů).

---

## 2. Podmínkový strom (WHERE / HAVING / JOIN ON)

### 2.1 Nové typy (`Model.QueryInstructions.Conditions`)

```csharp
public enum ComparisonOperator
{
    Equal, NotEqual,
    GreaterThan, GreaterThanOrEqual,
    LessThan, LessThanOrEqual,
    Like, In,
    IsNull, IsNotNull        // right operand se ignoruje
}

public enum LogicalOperator { And, Or }

public abstract record ConditionNode
{
    public abstract string Accept(IQueryVisitor visitor);
}

public sealed record ComparisonCondition(
    string? LeftTable, string? LeftProperty, string? LeftConstant, string? LeftFunction,
    ComparisonOperator Operator,
    string? RightTable, string? RightProperty, string? RightConstant, string? RightFunction
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}

public sealed record LogicalCondition(
    LogicalOperator Operator,
    IReadOnlyList<ConditionNode> Operands
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}

public sealed record NotCondition(ConditionNode Operand) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
```

`LeftFunction`/`RightFunction` sjednocuje dnešní `SelectInstruction` (bez funkcí) a `HavingInstruction` (s funkcemi `COUNT`/`SUM`/…) do jednoho typu uzlu – `HavingInstruction` pak přestává potřebovat vlastní pole a jen obaluje `ConditionNode`.

### 2.2 Změna `SelectInstruction` / `HavingInstruction`

```csharp
public sealed record SelectInstruction(ConditionNode Condition) : QueryInstruction { ... }
public sealed record HavingInstruction(ConditionNode Condition) : QueryInstruction { ... }
```

Místo dnešního seznamu plochých instrukcí, které builder ručně spojuje přes AND, bude existovat nejvýš jedna `SelectInstruction`/`HavingInstruction` na úroveň dotazu a celá podmínka (včetně vnořeného AND/OR) bude v jejím stromu.

### 2.3 Změna `AbstractQueryBuilder`

Dnešní `Select(left...op...right...)` nahradím:

```csharp
public void Where(ConditionNode condition) => instructions.Add(new SelectInstruction(condition));
public void Having(ConditionNode condition) => instructions.Add(new HavingInstruction(condition));
```

Parser si strom postaví sám (rekurzivním průchodem zdrojového kódu) a předá ho builderu hotový jedním voláním – to je přirozenější než dnešní inkrementální mutace builderu a odpovídá tomu, jak `EFCoreLinqQueryParser` už dnes rekurzivně prochází syntaxi.

### 2.4 `IQueryVisitor` – nové metody

```csharp
string Visit(ComparisonCondition cond);
string Visit(LogicalCondition cond);
string Visit(NotCondition cond);
```

Implementace (např. `DapperSQLQueryVisitor`) musí při vykreslování hlídat závorkování – vnořený `LogicalCondition` uvnitř jiného s odlišným operátorem (AND obsahující OR) se musí obalit do závorek, jinak vznikne sémanticky odlišný SQL dotaz.

### 2.5 Dopad na `JoinInstruction` (navazuje na kompozitní FK, kapitola 4)

Původní návrh v kapitole 04_query_translation.tex původní diplomky [`thesis/chapters v původním repozitáři`](https://github.com/milan252525/orm-convertor/blob/main/thesis/chapters/04_query_translation.tex) počítal s JOIN instrukcí ve tvaru `JOIN table1, table2, joinType, (conditionTree)` – tedy ON klauzule měla být od začátku celý podmínkový strom, ne jen jeden pár sloupců. Držím se toho a `JoinInstruction` navrhuji rovnou s `ConditionNode`, ne se seznamem párů – je to obecnější (multi-column equi-join je jen `AND` několika `ComparisonCondition` rovností) a znovu použije přesně stejný mechanismus jako WHERE/HAVING, žádný nový typ navíc.

```csharp
public sealed record JoinInstruction(
    JoinKind Kind,
    string LeftTable,
    string RightTable,
    string? RightTableAlias,
    ConditionNode OnCondition
) : QueryInstruction { ... }
```

Builder podmínku vyrenderuje stejným visitorem jako WHERE/HAVING. Jednosloupcový JOIN je jen degenerovaný případ s jedním `ComparisonCondition` – žádná ztráta zpětné kompatibility ve významu, jen ve volací signatuře.

### 2.6 Zpětná kompatibilita

Jednoduchý dotaz s jedinou podmínkou (dnešní běžný případ) se vejde jako `LogicalCondition(And, [cmp1])` nebo dokonce jako samotný `ComparisonCondition` – degenerovaný strom o jednom uzlu. Žádná ztráta vyjadřovací síly oproti dnešku, jen navíc možnost stromu.

---

## 3. Kompozitní primární klíče

### 3.1 Nové typy (`Model.AbstractRepresentation`)

```csharp
public sealed class PrimaryKey
{
    public required IReadOnlyList<PrimaryKeyPart> Parts { get; init; }
}

public sealed class PrimaryKeyPart
{
    public required PropertyMap PropertyMap { get; init; }   // typ, název sloupce, nullabilita atd. se berou z Property/PropertyMap
    public required int Order { get; init; }                 // explicitní pořadí (1-based), NE pořadí v listu
    public PrimaryKeyStrategy Strategy { get; init; } = PrimaryKeyStrategy.None;   // per-part, ne pro celý klíč
}
```

`EntityMap` dostane nové pole:

```csharp
public PrimaryKey? PrimaryKey { get; set; }
```

### 3.2 Proč explicitní `Order`, ne pořadí v `List<>`

EF Core řeší tenhle přesný problém atributem `[Column(Order = N)]`, protože pořadí deklarace vlastností ve třídě není zaručeně to zamýšlené pořadí klíče. Když už to řeší samotný framework, na který cílím, dává smysl mít pořadí jako první-třídní hodnotu v modelu, ne ho odvozovat z pozice v seznamu (obzvlášť u NHibernate XML `<composite-id>`, kde by se pořadí prvků XML dalo teoreticky splést s pořadím parsování).

### 3.3 `AbstractEntityBuilder.AddPrimaryKey`

```csharp
public void AddPrimaryKey(IReadOnlyList<(string PropertyName, int Order, PrimaryKeyStrategy Strategy)> parts)
```

Pro jednoduchý (nekompozitní) případ necháme pohodlný přetížený overload:

```csharp
public void AddPrimaryKey(PrimaryKeyStrategy strategy, string propertyName)
    => AddPrimaryKey([(propertyName, 1, strategy)]);
```

– tím zůstane beze změny stávající volání v `EFCoreEntityParser.cs` (řádek ~202).

### 3.4 Strategie generování per-part

Rozhodli jsme se nechat model záměrně permisivní (dovolí i teoreticky nevalidní kombinaci, např. dvě `Identity` části) a validaci typu "smí mít tabulka jen jednu IDENTITY" necháme na cílovém builderu/databázi, ne na abstraktním modelu. Model má reprezentovat záměr, ne vynucovat pravidla konkrétního SQL enginu.

---

## 4. Vícesloupcové vztahy (1:1, 1:N, N:M)

### 4.1 Přesun `Relation` z `PropertyMap` na `EntityMap`

```csharp
public sealed class EntityMap
{
    // ... stávající pole ...
    public List<Relation> Relations { get; set; } = new();
    public bool IsJunctionTable { get; set; } = false;   // viz 4.5
}
```

`PropertyMap.Relation` ruším (nebo dočasně `[Obsolete]`, viz otázky níže). Důvod jsem si všiml sám u sebe v kódu – 1:N/N:M nemá na "mnoho" straně žádný reálný sloupec, na který by šlo vztah pověsit, a `NHibernateEntityBuilder.cs` to dokonce sám přiznává komentářem `// single relation per FK property`.

### 4.2 Nový `Relation`

```csharp
public enum RelationRole { Owning, Inverse }

public sealed class Relation
{
    public string? Name { get; set; }                 // rozlišení víc vztahů mezi stejnou dvojicí entit
    public required Cardinality Cardinality { get; set; }
    public required RelationRole Role { get; set; }
    public required string SourceEntity { get; set; }  // viz otázka níže (string vs. reference)
    public required string TargetEntity { get; set; }
    public required IReadOnlyList<(PropertyMap Source, PropertyMap Target)> ColumnPairs { get; set; }
    public bool IsUnique { get; set; }                 // true = 1:1 (unikátní omezení na FK sloupcích)
    public string? InverseRelationName { get; set; }   // párování Owning <-> Inverse
}
```

`ColumnPairs` řeším stejně jako u `JoinInstruction` – uspořádaný seznam dvojic zaručuje správné párování sloupců u kompozitního FK (source.Col1↔target.Col1, source.Col2↔target.Col2), ne dvě nezávislé množiny.

### 4.3 Owning vs. Inverse strana

- **1:N** – strana s cizím klíčem (fyzickými sloupci) = `Owning`; strana s kolekcí = `Inverse`, `ColumnPairs` stejné, jen se dívá z druhé strany.
- **1:1** – jedna strana zvolena jako `Owning` (nese FK + `IsUnique = true`), druhá `Inverse`.
- **M:1** – zrcadlový případ k 1:N, `Owning` je vždy strana s fyzickým FK.

Builder pak podle role generuje buď vlastnost s cizím klíčem (Owning), nebo navigační kolekci/referenci bez sloupce (Inverse) – to je přesně to, co dnešní kód dělá napůl a nekonzistentně (`NHibernateEntityBuilder.cs` dnes hledá `OneToMany`/`ManyToMany` přes `PropertyMap.Relation`, což pro tyto kardinality nedává úplně smysl).

### 4.4 Unikátnost pro 1:1

`IsUnique` na `Relation` – bez něj je 1:1 v databázi nerozeznatelné od 1:N, které náhodou nikdo neporušil. Builder pro cílový framework/DB pak ví, že má vygenerovat unikátní index/omezení na sloupcích `ColumnPairs`.

### 4.5 N:M přes spojovací tabulku

Zvažovali jsme dvě varianty: zavést speciální typ vztahu `ManyToManyRelation`, nebo N:M poskládat ze stávajících stavebních bloků. Rozhodli jsme se pro druhou variantu – žádný nový typ vztahu nezavádíme. N:M reprezentujeme jako dvě `Owning` `ManyToOne` relace, obě směřující ze spojovací entity (`EntityMap` s `IsJunctionTable = true`) k oběma stranám. Výhoda: znovupoužijeme stejný mechanismus `ColumnPairs`, nic nového se nemusí učit builder ani visitor.

```
StudentCourse (IsJunctionTable = true)
 ├─ Relation(Owning, ManyToOne) → Student   (ColumnPairs: [StudentId↔Id])
 └─ Relation(Owning, ManyToOne) → Course    (ColumnPairs: [CourseId↔Id])
```

Pro "bohatou" spojovací tabulku (má vlastní sloupce navíc, např. datum zápisu) se nic neděje navíc – je to prostě normální entita se dvěma FK. Pro "čistou" spojovací tabulku (jen dva FK sloupce) dává `IsJunctionTable = true` builderu signál, že při generování do frameworku, který umí implicitní M:N bez explicitní třídy (typicky NHibernate `<many-to-many>` mapping, nebo EF Core skip navigation), může syntetickou entitu "zploštit" a negenerovat pro ni samostatnou třídu.

---

## 5. Další postřehy nad rámec původního zadání

Při návrhu jsem si všiml ještě těchto věcí:

1. **`Relation` musí žít na entitě, ne na sloupci** – jinak 1:N/N:M nemají kam se pověsit (doloženo komentářem `// single relation per FK property` přímo v mém `NHibernateEntityBuilder.cs`).
2. **`JoinInstruction` má stejný jednosloupcový problém jako WHERE** – bez opravy nepůjde vygenerovat JOIN přes kompozitní FK, i kdyby byl FK v modelu správně.
3. **Explicitní `Order`** u částí klíče – zdůvodněno chováním EF Core (`[Column(Order = N)]`).
4. **Strategie generování per-part**, ne pro celý composite key.
5. **`IsUnique`** na vztahu pro skutečné 1:1.
6. **`Name`/identita vztahu** kvůli self-referencím a víc FK na stejnou cílovou entitu.
7. Dapper builder dnes vůbec nečte `IsPrimaryKey`/`IsForeignKey` – Dapper jako micro-ORM zřejmě generuje jen POCO bez klíčových metadat. Stojí za ověření, jestli to má zůstat tak, nebo jestli i Dapper výstup má klíče/vztahy nějak reflektovat (např. jako komentáře, nebo pro potřeby query JOIN generování).

---

## 6. Dotčená místa v současném kódu (checklist k migraci)

| Soubor | Co se týká |
|---|---|
| `AbstractWrappers/AbstractEntityBuilder.cs` | `AddPrimaryKey`, `AddForeignKey` – signatura i tělo |
| `AbstractWrappers/AbstractQueryBuilder.cs` | `Select`, `Having`, `Join` – signatura i tělo |
| `Model/AbstractRepresentation/EntityMap.cs` | přidat `PrimaryKey`, `Relations`, `IsJunctionTable` |
| `Model/AbstractRepresentation/PropertyMap.cs` | zrušit/odstranit `Relation`, přestat spoléhat na `OtherDatabaseProperties` flagy |
| `Model/AbstractRepresentation/Relation.cs` | kompletní redesign dle kap. 4.2 |
| `Model/QueryInstructions/SelectInstruction.cs`, `HavingInstruction.cs`, `JoinInstruction.cs`, `IQueryVisitor.cs` | dle kap. 2 |
| `EFCoreWrappers/EFCoreEntityBuilder.cs` (řádky ~46, 51, 90, 122, 127) | čtení `IsForeignKey`/`IsPrimaryKey`/`Relation` |
| `EFCoreWrappers/EFCoreEntityParser.cs` (řádek ~202) | volání `AddPrimaryKey` |
| `NHibernateWrappers/NHibernateEntityBuilder.cs` (řádky ~107, 129, 153, 158, 179, 181, 202, 204, 318) | nejtěžší migrace, dnes nejvíc závislý na starém tvaru |
| `NHibernateWrappers/NHibernateXMLMappingParser.cs` (řádek ~170) | rozšířit o `<composite-id>` |
| `DapperWrappers/DapperSQLQueryBuilder.cs` + `DapperSQLQueryVisitor.cs` | jediný dnešní fungující query builder – musí umět nový `ConditionNode` strom |

---

## 7. Otázky, které je třeba zodpovědět před implementací

1. **`Relation.SourceEntity`/`TargetEntity`** – je třeba rozhodnout mezi řetězcem (jednoduché, ale křehké vyhledávání podle jména) a přímou referencí na `EntityMap` (robustnější, ale je nutné ošetřit cykly při serializaci do API DTO).

   **Rozhodnutí: řetězec s názvem entity.** Vede k tomu víc důvodů. Konzistence: celá mezireprezentace identifikuje věci jmény (tabulky, sloupce, vlastnosti), řetězcový odkaz na entitu z toho nevybočuje. Serializace: IR putuje přes API DTO do frontendu a přímá reference by vytvořila cyklus `EntityMap → Relation → EntityMap`, který System.Text.Json bez speciálního zacházení (`ReferenceHandler.Preserve`, vlastní konvertory) neserializuje – a stejné komplikace by se přenesly i do Angularu. Neutralita do budoucna: řetězcová identifikace zůstane funkční i pro plánovaný Java ekosystém (F10), kde objektové reference přes hranici serializace stejně sdílet nejde. Známou slabinu řetězce – křehké vyhledávání podle jména, které rozbije překlep nebo přejmenování – neřešíme robustnějším typem, ale validací: před generováním se všechny názvy entit ve vztazích rozresolvují proti množině `EntityMaps` a nenalezený název je chyba úplnosti IR se strukturovanou diagnostikou, v duchu F11. Kdyby se později hodilo traverzovat graf vztahů přes reference, lze nad IR postavit rozresolvovanou vrstvu, aniž by se měnil serializovaný tvar modelu.

2. **`IS NULL`** – je třeba zvolit mezi speciálním `ComparisonOperator.IsNull/IsNotNull` (navrženo výše) a samostatným uzlem `IsNullCondition`.

   **Rozhodnutí: operátory `ComparisonOperator.IsNull`/`IsNotNull`.** Ve hře byla i třetí varianta – `NULL` jako konstantní operand běžného `==`/`!=`, tak to znázorňuje doprovodný článek (obr. 4, uzel `==` s operandy `NULL` a `c.CreditLimit`). Ta vypadá nejjednotněji, ale je to sémantická past: naivně vygenerované `= NULL` v SQL není `IS NULL` – porovnání s NULL vrací UNKNOWN a dotaz by potichu nevracel řádky, což je přesně druh tiché chyby, které se chceme vyhnout. Každý builder by proto NULL operand stejně musel detekovat a ošetřit zvláštní větví, čímž se domnělá jednotnost jen přesouvá z modelu do všech builderů. Samostatný uzel `IsNullCondition` je sémanticky čistý, ale rozšiřuje `IQueryVisitor` o další metodu, kterou musí implementovat každý současný i budoucí builder (včetně plánovaných Java builderů), přestože jde pořád o porovnání, jen unární. Zvolená varianta drží tvar stromu uniformní: `ComparisonCondition` s operátorem `IsNull`/`IsNotNull` a nevyužitou pravou stranou. Parser do ní normalizuje jak `== null`/`!= null` z LINQ, tak `IS [NOT] NULL` ze SQL, a builder má pro tyto operátory povinnou explicitní větev – omylem vygenerovat `= NULL` tak nejde.

3. **Migrace** – je třeba rozhodnout, zda proběhne postupně (staré API dočasně `[Obsolete]`, běží vedle nového), nebo jednorázovým přepisem všech tří wrapperů najednou.

   **Rozhodnutí: jednorázový přepis v rámci každého kroku z §8, žádné `[Obsolete]`.** Kontext: na projektu pracuje jeden člověk, IR API nemá žádné konzumenty mimo tento repozitář a aplikace smí být během přestavby dočasně nefunkční. Souběh starého plochého a nového stromového modelu by za těchto podmínek nepřinášel hodnotu, jen náklady: adaptéry mezi oběma reprezentacemi, dvojí testovací matici (každý scénář proti starému i novému API) a mrtvý kód s `[Obsolete]` atributy, který by se po pár týdnech stejně mazal. Roli záchranné sítě přebírá git a testy: každý krok z §8 se dokončí včetně úprav testů a commitne jako funkční milník, CI na `main` dá zpětnou vazbu po každém pushi a případný červený stav mezi milníky znamená „rozpracováno", ne problém. Kroky v §8 jsou záměrně řezané tak, aby byl každý dokončitelný jako jeden ohraničený přepis.

4. **Dapper a klíče/vztahy** – je třeba ujasnit, má-li je builder začít nějak reflektovat, i když dnes negeneruje explicitní metadata.

   **Rozhodnutí: negenerovat, ale nahlásit strukturovaným varováním.** Dapper mechanismus explicitního mapování klíčů a vztahů nemá – ve srovnání z článku (tab. 1) je specifikace primárního klíče „implicit/manual" a cizí klíče se řeší ručními joiny. Není tedy do čeho generovat: pseudo-atributy nebo metadata v komentářích by vytvářely kód, který žádné Dapper API nečte, a budily by falešný dojem, že se informace přenesla. Dnešní builder ale klíče a vztahy zahazuje potichu (kroky `BuildPrimaryKey`/`BuildForeignKey` vůbec nevolá), což je přesně chování, které F11 zakazuje: „Nepodporované konstrukce nesmí být potichu vynechány." Builder proto klíče/vztahy nadále generovat nebude, ale výsledek konverze ponese strukturované varování za každý nevyjádřený fakt – např. „primární klíč (CustomerID) je zachován jen v IR; Dapper ho nevyjadřuje" – aby uživatel v UI i volající API viděli, co se do cílového kódu nepropsalo. Varování se napojí na diagnostickou infrastrukturu z F11, jakmile vznikne; do té doby stačí jednoduchý seznam varování ve výsledku konverze. Podstatné je, že IR zůstává úplná – při překladu Dapper → EF Core/NHibernate se metadata neztrácejí, protože zdrojem pravdy je pivot přes IR, ne vygenerovaný Dapper kód.

---

## 8. Pořadí implementace

Rozhodli jsme se postupovat v tomto pořadí:

1. **Podmínkový strom** (`ConditionNode` + `IQueryVisitor`) – nejmenší dopad, týká se jen `Select`/`Having` a jediného fungujícího builderu (Dapper SQL).
2. **`JoinInstruction` → vícesloupcový** – přímo navazuje, stejný mechanismus párů sloupců.
3. **`PrimaryKey`/`PrimaryKeyPart`** – týká se `EntityMap` + `AbstractEntityBuilder` + EFCore/NHibernate builderů.
4. **`Relation` na úrovni entity s `ColumnPairs`** – největší dopad, staví na kroku 3 (cílová strana vztahu se odkazuje na klíč cílové entity).
5. **N:M přes junction entitu** – staví na kroku 4.

Každý krok je nezávisle otestovatelný a nevyžaduje, aby byly hotové kroky za ním.
