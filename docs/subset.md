# Společná podmnožina a její hranice

**Účel:** odpověď na otázku „přeloží nástroj tohle?", konstrukce po konstrukci a na jednom místě. Specifikace omezuje překlad na „explicitně vymezenou společnou podmnožinu konstrukcí, kterou dokáže zachytit mezireprezentace a pro kterou lze ověřit odpovídající chování napříč .NET a Java ekosystémem" ([`specifikace.tex`](./specifikace.tex)) a schválený záměr slibuje, že „případy, ve kterých není možný úplný nebo jednoznačný překlad, budou v průběhu projektu identifikovány a zdokumentovány" ([`zamer.tex`](./zamer.tex)). Tenhle dokument je obojí: **kladná půlka** (část 1) vymezuje podmnožinu, **záporná půlka** (část 2) vyjmenovává případy bez úplného nebo jednoznačného překladu a část 3 jmenuje dvě místa, kde je sporné už samo čtení zadání.

**Žánr.** Popis současného stavu, tedy týž žánr jako [`architecture.md`](./architecture.md) (rozhodnutí [007](./decisions/007-documentation-structure.md)); samostatný soubor je proto, že §5 architektury má přes dvě stě kilobajtů a seznam by se v něm ztratil. Katalog neopisuje, ale ukazuje: jak konstrukce funguje, říká architektura (odkazy „§" míří do ní), proč je hranice tam, kde je, rozhodnutí v [`decisions/`](./decisions/README.md), a co verze nárokuje, [§9](./architecture.md#9-co-tahle-verze-nárokuje-a-co-je-vyňaté-ze-záruk) a kanonicky sekce *Guarantees* kořenového [`README.md`](../README.md). Když se katalog s §9 rozejde, platí §9 a chyba je tady, stejně jako u [`traceability.md`](./traceability.md). Tabulky 1.2 až 1.5 jsou čitelný opis strojových zdrojů — deskriptorů cílových frameworků a manifestu kategorií —, a na rozpor s nimi platí ty.

**Jak se čte.** U každé konstrukce stojí, co s ní nástroj udělá (druh záznamu podle [§5.1](./architecture.md#51-diagnostika-převodu)) a na které straně:

- **odmítne** — `Failure`: artefakt nevznikne a záznam jmenuje konstrukci; u dotazu platí, že dotaz, který by vrátil jiné řádky, se nevydá (rozhodnutí [053](./decisions/053-a-query-that-would-return-other-rows-is-not-emitted.md) a [070](./decisions/070-a-parser-refuses-what-would-change-the-row-set.md));
- **vypustí** — `Loss`: artefakt vznikne bez ní; u dotazu jsou řádky tytéž a výstup je jen chudší;
- **nativním SQL** — `Fallback`: cíl napíše celý dotaz v SQL deklarovaného dialektu přes API svého frameworku pro nativní dotazy (rozhodnutí [113](./decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md)); řádky jsou tytéž, artefakt je tím vázaný na SQL Server 2022 a měří se na 4. stupni jako každý překlad;
- **nečte se** — čtečka konstrukci nepřevezme: v podmínce je z toho `Failure`, v projekci a klíči řazení `Loss`, vždy jmenovitě (rozhodnutí 070);
- `Convention` — výstup tvrdí něco, co zdroj neřekl; `Incompleteness` — modelu chybí fakt a generování pokračuje; `Conflict` — dva zdroje faktu se rozcházejí.

Strana je **čtení** (parser zdroje), **brána** (šablona builderu, platí pro všechny cíle, [§7](./architecture.md#7-rozhraní-parserů-a-builderů)) nebo **zápis** (krok či visitor jednoho cíle).

**Tři druhy hranice.** Rozhodnutí [030](./decisions/030-scope-of-version-1-0.md) zavedlo pojem *vyňaté oblasti*, a ten tenhle seznam není — je jeho částí. Katalog vede tři druhy hranice a u každé položky říká, o který jde:

- **vyňatá oblast** (*VO n*) — celá oblast, na kterou verze neslibuje spoleh; vyslovuje se jednou a vcelku ([§9, *Hranice záruk*](./architecture.md#hranice-záruk)). Překladu se týkají oblasti 2 a 5, oblasti 1 a 6 (Advisor a experimenty) jsou mimo překlad;
- **zúžení nároku** (*Z Fx*) — požadavek je nárokovaný, ale v užším čtení, než jak zní ([§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje));
- **vyslovená mez** (*VM*) — konstrukce uvnitř nárokované oblasti, kterou nástroj z rozhodnutého důvodu odmítne, vypustí nebo napíše nativním SQL. Rozhodnutí 030 zakázalo, aby verze byla „slepencem jednotlivě omluvených polotovarů", a vyslovená mez takovou omluvou není: za každou stojí rozhodnutí, které argumentuje, proč jiná odpověď neexistuje — model fakt nenese, jazyk cíle tvar nemá, nebo by dotaz vrátil jiné řádky —, a záznam za běhu je způsob, jakým se ozve tomu, kdo katalog nečte.

Vedle toho jmenuje část 2.13 **tichá místa**: kde nástroj fakt zahodí beze slova, ačkoli podle rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md) a [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md) mluvit má. Ta hranicí nejsou, jsou to otevřené položky.

---

## 1. Kladná půlka: co se překládá

### 1.1 Šest frameworků

| Framework | Verze | Ekosystém | Zdroj — jazyk jednotky | Cíl — artefakty | API nativního SQL |
|---|---|---|---|---|---|
| Dapper | 2.1.79 | .NET | `CSharp` (třída, volání `SqlMapper`), `SqlQuery` (skript) | C# třída bez atributů; metoda nad `connection.Query<T>` a holé SQL | žádné — jazykem je SQL dialektu |
| EF Core | 10.0.10 | .NET | `CSharp` (třída s anotacemi, LINQ, nativní SQL v kódu) | C# třída s datovými anotacemi; metoda vracející `IQueryable` | `DatabaseFacade.SqlQuery`, `DbSet.FromSql` |
| NHibernate | 5.7.0 | .NET | `CSharp` (třída, LINQ, `CreateSQLQuery`, `CreateQuery`), `XML` (`hbm.xml` i s `<query>` a `<sql-query>`), `HqlQuery` | C# třída s `virtual` a `hbm.xml`; metoda nad `session.CreateQuery` a holé HQL | `ISession.CreateSQLQuery` |
| Hibernate | 7.4.5.Final | Java | `Java` (třída s anotacemi JPA, kód s `createQuery` a `createNativeQuery`), `XML` (`orm.xml`), `JpqlQuery` (JPQL a podmnožina HQL) | javová třída s anotacemi `jakarta.persistence`; metoda nad `em.createQuery` a holé JPQL | `EntityManager.createNativeQuery` |
| EclipseLink | 5.0.0 | Java | totéž co Hibernate, JPQL bez rozšíření HQL | totéž co Hibernate, s profilem EclipseLinku | `EntityManager.createNativeQuery` |
| MyBatis | 3.5.19 | Java | `Java` (doménová třída, rozhraní mapperu s `@Select` a `@Results`), `XML` (mapper) | POJO bez importu z frameworku a mapper s `<resultMap>`; hlavička metody rozhraní a `<select>` | žádné — jazykem je SQL dialektu |

Verze kanonicky vede tabulka *Zafixované verze* v [§1](./architecture.md#zafixované-verze), strojově deskriptor každého frameworku (`*Descriptor.cs`). Dialekt všech šesti je SQL Server 2022 a deskriptor ho deklaruje (rozhodnutí [086](./decisions/086-target-database-dialect-declared-by-the-descriptor.md)). Jednotka vstupu je jeden zdrojový soubor v jednom jazyce, deklaruje jen ten jazyk a třídy v ní rozdělí wrapper zdrojového frameworku (rozhodnutí [111](./decisions/111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md)); dokument, který je mapováním i dotazem zároveň, čtou oba průchody (rozhodnutí [081](./decisions/081-a-unit-may-be-a-mapping-and-a-query-at-once.md)). Mezi každými dvěma frameworky jde překlad oběma směry, tedy v šestatřiceti směrech včetně osmnácti napříč ekosystémy (`Combined/QueryMatrixTest`, `Combined/CrossEcosystemMatrixTest`).

### 1.2 Slovník dotazu

Dotaz mezireprezentace ([§4.4](./architecture.md#44-dotazové-instrukce-a-podmínkový-strom)) je seznam instrukcí — zdroj řádků, projekce, filtr, join, seskupení, `HAVING`, řazení, stránkování, `DISTINCT`, poddotaz, množinová operace — a vedle nich seznam pojmenovaných mezivýsledků (`WITH`) s limitem rekurze. Podmínka je strom (`AND`, `OR`, `NOT` nad porovnáním). Operand má šest tvarů: sloupec (i pod agregační funkcí s jejím `DISTINCT`), konstanta, poddotaz, výčet hodnot, parametr a výraz. Výraz je binární operátor (konkatenace a aritmetika), funkce uzavřeného slovníku, `CASE`, okenní funkce řazení nebo agregace do seznamu.

Jak cíl jednotlivou konstrukci napíše — *jazyk* znamená svým dotazovým jazykem, *nativní SQL* celý dotaz únikovou cestou se záznamem `Fallback`. Řádky odpovídají kategoriím `QueryFeature` deskriptoru; kde cíl kategorii vyjádří jen v některém tvaru, stojí to v buňce a podrobnosti v části 2.

| Konstrukce | Dapper | MyBatis | EF Core | NHibernate | Hibernate | EclipseLink |
|---|---|---|---|---|---|---|
| projekce, filtr, join, agregace, seskupení, `HAVING`, řazení, stránkování, poddotaz, parametr, výraz | jazyk | jazyk | jazyk | jazyk | jazyk | jazyk |
| druh joinu (vnitřní, levý, pravý, plný) | jazyk | jazyk | jazyk; plný složením `LeftJoin`, `Concat`, `RightJoin` ([065](./decisions/065-row-set-as-the-boundary-of-rule-053.md)) | jazyk; plný nativní SQL | jazyk | jazyk |
| množinová operace | jazyk | jazyk | jazyk | nativní SQL | jazyk | jazyk |
| mezivýsledek (`WITH`, odvozená tabulka) | jazyk | jazyk | jazyk (proměnná metody) | nativní SQL | jazyk | nativní SQL |
| rekurze | jazyk | jazyk | nativní SQL | nativní SQL | jazyk; s limitem nativní SQL | nativní SQL |
| seskupení podle výrazu | jazyk | jazyk | jazyk | jazyk | jazyk | jazyk; klíč s literálem nativní SQL |
| okenní funkce řazení | jazyk | jazyk | nativní SQL | nativní SQL | jazyk | nativní SQL |
| agregace do seznamu | jazyk | jazyk | jazyk nad prvky skupiny a sloupcem bez `NULL`, jinak nativní SQL | nativní SQL | jazyk | nativní SQL |
| funkce slovníku | všech 17 | všech 17 | všech 17 | bez `DateAdd`, `DateDiff` | všech 17 | bez `DateAdd`, `DateDiff`, `Cast` |

Funkcí je sedmnáct (`QueryFunction`): `Upper`, `Lower`, `Trim`, `Substring`, `Length`, `Coalesce`, `Abs`, `Year`, `Month`, `Day`, `CurrentTimestamp`, `EscapePattern` (rozhodnutí [107](./decisions/107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md)) a `DateAdd`, `DateDiff` s jednotkou od roku po sekundu, `Round`, `Sqrt` a `Cast` do `Int`, `Long`, `Float`, `Double` a `String` (rozhodnutí 113). Funkci, kterou deskriptor cíle neuvádí, píše cíl nativním SQL. Okenní funkce řazení jsou `ROW_NUMBER`, `RANK` a `DENSE_RANK` nad oknem s povinným řazením a stojí jen v projekci.

Strojově to drží deskriptory (`QuerySupport`, `Functions`, `NativeSqlApi`) a `Combined/TargetFrameworkDescriptorTest`. Dapper a MyBatis nemají kam ustoupit, protože jejich jazykem už je SQL dialektu — co z dotazu nevezmou, odmítnou, a nevezmou nic, co by nebylo v bránách šablony odmítnuté všem cílům.

### 1.3 Kategorie, na kterých je podmnožina změřená

Kategorií požadavku T2 je čtyřicet a každá je dotazem nad sdílenou doménou sedmi entit, napsaným v každém zdrojovém jazyce, který ji vysloví. Seznam a to, kdo kategorii vysloví a kdo ji píše nativním SQL, nese manifest `Tests/Database/QueryShapes/categories.txt`, ze kterého čtou obě testovací sady ([§6.2](./architecture.md#62-ověření-generovaných-artefaktů)). Každá kategorie jde každým směrem: 1. stupeň v matici `Combined/QueryShapeMatrixTest`, 2. stupeň nad SQL a .NET cíli, 2. a 3. stupeň nad javovými cíli v javové sadě (`shapes/QueryCategoryTest`) a 4. stupeň — diferenční ověření proti jednomu kanonickému výsledku — v `Tests/Database/Differential/matrix.txt` (rozhodnutí [089](./decisions/089-differential-verification-as-the-fourth-level-over-a-query.md)).

✓ znamená, že zdroj kategorii vysloví a nástroj ji z něj přečte; — že ji jazyk zdroje nevysloví (důvody v 1.4). Sloupec *Nativní SQL* jmenuje cíle, které kategorii píšou únikovou cestou; ostatní ji píšou svým jazykem. Odmítnutý směr v matici není žádný.

| Kategorie | Co měří | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis | Nativní SQL |
|---|---|---|---|---|---|---|---|---|
| `Projection` | projekce sloupců | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `Filtering` | filtr | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `JoinOverTwoColumns` | join přes dva sloupce | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `AggregationGroupingAndHaving` | agregace, seskupení, `HAVING` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `Ordering` | řazení | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `PaginationWithBoundCounts` | stránkování s vázanými počty | ✓ | ✓ | — | ✓ | — | ✓ | |
| `SubqueryAsTheRightSideOfIn` | `IN (SELECT …)` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `CorrelatedExistsOverThreeColumns` | korelované `EXISTS` přes tři sloupce | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `ScalarSubquery` | skalární poddotaz proti sloupci | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `SetOperation` | množinová operace | ✓ | ✓ | — | ✓ | ✓ | ✓ | NHibernate |
| `DistinctProjection` | `DISTINCT` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `InOverAListOfValues` | `IN` s výčtem hodnot | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `ScalarParameter` | skalární parametr | ✓¹ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `CollectionParameter` | kolekční parametr | ✓¹ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `ConstantOfAMoment` | konstanta okamžiku | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `LikeWithAnAnchoredPattern` | `LIKE` s ukotveným vzorkem | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `CountOverDistinctValues` | `COUNT(DISTINCT …)` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `LikeWithAnEscapedWildcard` | `LIKE` s únikovým znakem | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `InListWithABoundValue` | parametr mezi hodnotami výčtu | ✓¹ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `LikeWithABoundPrefix` | `LIKE` nad vázaným prefixem | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `ArithmeticInAProjection` | aritmetika v projekci | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `FunctionInAFilter` | funkce ve filtru | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `CoalesceInAFilter` | `COALESCE` ve filtru | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `CaseInAProjection` | `CASE` v projekci | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `OrderingByAnAggregate` | řazení podle agregátu | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `ScalarSubqueryAgainstABoundValue` | parametr proti skalárnímu poddotazu | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `GroupingOverAGroupedResult` | seskupení nad seskupeným výsledkem | ✓ | ✓ | — | ✓ | — | ✓ | NHibernate, EclipseLink |
| `IntermediateResultReadTwice` | `WITH` čtený dvakrát | ✓ | ✓ | — | ✓ | — | ✓ | NHibernate, EclipseLink |
| `AggregateOverTheWholeResult` | agregát přes celý výsledek | ✓ | — | ✓ | ✓ | ✓ | ✓ | EF Core |
| `NativeSqlInCode` | nativní SQL předané v kódu | — | ✓ | ✓ | ✓ | ✓ | — | |
| `RecursiveDescentOfAHierarchy` | rekurzivní sestup hierarchií | ✓ | — | — | ✓ | — | ✓ | EF Core, NHibernate, EclipseLink |
| `RecursiveWalkOfACyclicGraph` | procházka grafem s cyklem, s limitem | ✓ | — | — | — | — | ✓ | EF Core, NHibernate, Hibernate, EclipseLink |
| `GroupingByAnExpression` | seskupení podle výrazu | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `DateArithmetic` | `DATEADD` a `DATEDIFF` | ✓ | ✓ | — | ✓ | — | ✓ | NHibernate, EclipseLink |
| `RoundingAndSquareRoot` | `ROUND` a `SQRT` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `CastInAConcatenation` | převod typu v konkatenaci | ✓ | ✓ | ✓ | ✓ | — | ✓ | EclipseLink |
| `BestRowPerGroup` | nejlepší řádek skupiny oknem | ✓ | — | — | ✓ | — | ✓ | EF Core, NHibernate, EclipseLink |
| `ListAggregation` | agregace do seznamu | ✓ | ✓ | — | ✓ | — | ✓ | NHibernate, EclipseLink² |
| `OuterJoinWithAFilterInOn` | vnější join s filtrem v `ON` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `JoinBeyondEqualities` | join nad rámec rovností klíčů | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |

¹ Bez katalogu nástroj parametr zdroje Dapper odmítne, protože skalár nemá z čeho vzít (2.4); s katalogem, který tabulku má, se typuje do každého cíle (rozhodnutí [105](./decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md) a [106](./decisions/106-a-bare-parameter-after-in-is-dappers-collection-parameter.md)). Manifest to vede klíčem `refusedWithoutCatalog` a je to jediné odmítnutí v něm.
² Ze zdroje MyBatis bez katalogu píše nativním SQL i EF Core, protože o sloupci neví, že nesmí mít `NULL` (klíč `fallbackWithoutCatalog`).

Vedle kategorií stojí dvě sady, které kategorie nepíšeme sami. **Záměrně špatný dotaz** — osm poddotazů do čtyř úrovní, joiny přes dva a tři sloupce, seskupení s `HAVING`, řazení a výřez — jde ze všech šesti zdrojů do všech šesti cílů (`Combined/DeeplyNestedQueryTest`, v javové sadě `shapes/DeeplyNestedQueryTest`). A **katalog LDBC** — 41 čtecích dotazů LDBC Social Network Benchmark nad jeho schématem (rozhodnutí [110](./decisions/110-ldbc-snb-as-a-second-reference-domain.md)) — je měřítkem úplnosti: přeložených je všech 41, šest se zjednodušením, které katalog u dotazu vyslovuje, a odmítá jen EclipseLink BI 12 kvůli seznamovému parametru v nativním SQL (`SampleData/LdbcSnbSample`, koncový bod `/ldbc`, `Combined/LdbcCatalogTest`).

### 1.4 Proč u kategorie chybí zdroj

Pomlčka v tabulce 1.3 není mezera nástroje, ale jazyka zdroje — konstrukci, kterou zdroj nevysloví, nemá nástroj z čeho číst:

- **HQL 5.7** (NHibernate) nemá výřez v textu — ten žije na objektu dotazu (`SetFirstResult`, `SetMaxResults`) a z kódu se čte —, množinovou operaci, `WITH` ani odvozenou tabulku, rekurzi, datumovou aritmetiku, okenní funkci ani agregaci do seznamu; LINQ NHibernatu mezivýsledek nečte, protože ho provider do HQL nepřeloží;
- **JPQL EclipseLinku** nemá totéž, jen seskupuje podle výrazu (`extract`); výřez má také jen na objektu dotazu, jeho poddotaz ve `FROM` je kartézský součin za čárkou, který model nenese, a `cast` předá EclipseLink 5.0 SQL Serveru jménem javového typu, takže zdrojem převodu není;
- **HQL 7.4** (Hibernate) rekurzi má, limit rekurze ne;
- **LINQ** (EF Core) vysloví agregát přes celý výsledek jen voláním, které dotaz vykoná, a nemá rekurzi ani okenní funkci;
- **SQL Dapperu a MyBatisu** není zdrojem nativního SQL v kódu, protože jejich jazykem už nativní SQL je.

### 1.5 Slovník mapování

Entita mezireprezentace ([§4.1](./architecture.md#41-entity-a-mapování)) je třída s vlastnostmi a jejich jazykovými fakty (typ, nullabilita, modifikátory, inicializátor), s databázovým typem jako rodinou s facetami, s primárním klíčem o jedné až *n* částech se strategií generování a s klíčovou třídou ([§4.2](./architecture.md#42-primární-klíč)), se vztahy na entitě, které ukazují jménem a nesou roli a páry sloupců, a s N:M jako spojovací entitou ([§4.3](./architecture.md#43-vztahy)). Jak který cíl mapovací fakt vyjádří (`MappingFactCategory` a `FactSupport` deskriptoru) — prázdná buňka znamená, že ho cíl nevyjádří a nese-li ho model, vydá záznam `Loss`:

| Fakt | Dapper | MyBatis | EF Core | NHibernate | Hibernate, EclipseLink |
|---|---|---|---|---|---|
| tabulka | | | `[Table]` | `<class table>` | `@Table(name)` |
| schéma | | | `[Table(Schema)]` | `schema` | `@Table(schema)` |
| sloupec | | `<result column>` | `[Column]` | `column` | `@Column(name)` |
| databázový typ | | `jdbcType` | `[Column(TypeName)]` | `type`, `sql-type` | `columnDefinition`; `@Nationalized` (Hibernate) |
| délka | | | `[MaxLength]` | `length` | `@Column(length)` |
| přesnost a měřítko | | | `[Precision]` | `precision`, `scale` | `precision`, `scale`, `secondPrecision` |
| nullabilita | | | `[Required]` | `not-null` | `@Column(nullable)` |
| primární klíč | | `<id>` | `[Key]`, `[PrimaryKey]` | **vyžaduje**: `<id>`, `<composite-id>` | **vyžaduje**: `@Id`, `@IdClass` |
| strategie klíče | | | `[DatabaseGenerated]` | `<generator>` | `@GeneratedValue` |
| sloupce cizího klíče | | | `[ForeignKey]` | `column` vztahu | `@JoinColumn` |
| sloupec verze | | | `[Timestamp]` | `<version>` | `@Version` |
| unikátní omezení | | | `[Index(IsUnique = true)]` | `unique`, `unique-key` | `@UniqueConstraint`, `unique` |
| transientní vlastnost | | vynechání (`autoMapping="false"`) | `[NotMapped]` | vynechání | `@Transient` |

*Vyžaduje* znamená, že bez faktu cíl artefakt nevydá (`Failure`). Co zdroj Dapper nebo MyBatis nevysloví, doplní katalog databáze ([§5.2](./architecture.md#52-doplňování-mapovacích-faktů-z-katalogu), rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)) nebo konvence cíle se záznamem `Convention`. Členy, které cíl vyžaduje nad rámec domény, deklaruje deskriptor (`EnforcedMembers`, rozhodnutí [009](./decisions/009-target-framework-descriptor.md)): NHibernate `virtual` a u složeného klíče `[Serializable]`, `Equals` a `GetHashCode`, oba cíle JPA nefinální třídu a pole, bezparametrický konstruktor a u složeného klíče vnořenou `@IdClass`, která implementuje `Serializable`, `equals` a `hashCode`, MyBatis třídu bez konstruktoru, EF Core `[Keyless]` u entity bez klíče; Dapper nic.

### 1.6 Po frameworcích

Co každý framework jako zdroj přečte a jako cíl vydá. Co odmítne, vypustí nebo napíše nativním SQL, je v části 2 u konstrukce, které se to týká; tady stojí jen to, co je pro framework vlastní.

**Dapper** ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes)). *Zdroj:* třída jen svými jazykovými fakty — klíč, tabulku, sloupce ani vztahy Dapper nemá, takže je doplní katalog (F6) —; dotaz z holé jednotky SQL, kde je každý `SELECT` dotazem (rozhodnutí [108](./decisions/108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md)), a z každého volání `SqlMapper` v kódu (`Query…`, `QueryMultiple` jako skript, `Execute…`; rozhodnutí [109](./decisions/109-a-code-unit-carries-every-query-it-hands-over.md)), čtený sdílenou čtečkou T-SQL (rozhodnutí [082](./decisions/082-t-sql-read-and-written-by-a-shared-project.md)); holé `IN @ids` je kolekční parametr, `IN (@ids)` jedna hodnota (rozhodnutí 106). *Cíl:* třída bez atributů, klíče a vztahů, s mechanickým `Loss` za každý mapovací fakt modelu; dotaz jako metoda nad `connection.Query<T>` a vedle ní holé SQL, projekce jako `List<dynamic>` (rozhodnutí [104](./decisions/104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md)).

**EF Core** ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes)). *Zdroj:* anotace `[Table]`, `[Key]`, `[PrimaryKey]`, `[Keyless]`, `[Column]`, `[MaxLength]`, `[StringLength]`, `[Unicode]`, `[Precision]`, `[Required]`, `[DatabaseGenerated]`, `[Timestamp]`, `[NotMapped]`, `[Index]`, `[ForeignKey]` a `[InverseProperty]` a konvence EF Core, které parser vysloví jako tvrzení zdroje (klíč `Id`, navigace s dohledaným cizím klíčem, N:M; rozhodnutí [067](./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md)); kontext (`DbContext`, `DbSet<T>`) entitou není. Dotaz je každý řetěz LINQ nad kořenem (sdílené čtení LINQ, rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md) a [103](./decisions/103-a-query-expression-is-read-as-its-rewrite-and-a-single-row-terminal-as-a-slice.md)), kořen rozhoduje to, co jednotka o jménu uvádí (rozhodnutí [114](./decisions/114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md)), explicitní načtení navigace je dotaz nad cílovou entitou (rozhodnutí [115](./decisions/115-explicit-loading-is-the-query-the-provider-composes.md)) a nativní SQL v `SqlQuery`, `SqlQueryRaw` a `FromSql…` se čte jako celý dotaz (rozhodnutí 113). *Cíl:* třída s datovými anotacemi — fluent API nástroj nevydává; dotaz jako metoda vracející `IQueryable` nad `ctx.Set<T>()`, mezivýsledek jako proměnná metody, join nad rámec rovností klíčů filtrem spojované posloupnosti nebo korelovaným `SelectMany`; nativně `ctx.Database.SqlQuery<Row>` s třídou řádku, nebo `FromSql` u celé entity.

**NHibernate** ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes)). *Zdroj:* třída C# s odloženým `virtual`, `hbm.xml` v ploché třídě (`<class>`, `<id>` a `<generator>`, `<composite-id>` s `<key-property>` a `<key-many-to-one>`, `<property>`, `<version>` a `<timestamp>`, `<many-to-one>`, `<one-to-one>`, `<set>`, `<bag>`, `<list>`), čtený až po třídě (rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md) a [068](./decisions/068-source-framework-precedence-orders-the-reading.md)); dotazy z holého HQL (vlastní sestupný parser, rozhodnutí [062](./decisions/062-hql-read-by-a-hand-written-parser.md)), z `<query>` a `<sql-query>` v `hbm.xml` i s typem parametru, který deklaruje `<query-param>`, a z kódu (LINQ nad `session.Query<T>()`, `CreateSQLQuery`, HQL v `CreateQuery`, výřez `SetFirstResult` a `SetMaxResults`). *Cíl:* třída s `virtual` a kolekcemi jako `IList` a `ISet` (rozhodnutí [035](./decisions/035-nhibernate-collections-declared-by-interface.md)) a `hbm.xml` bez `assembly` (rozhodnutí [028](./decisions/028-assembly-name-is-not-ours-to-invent.md)), s generátorem vybraným podle rozhodnutí [021](./decisions/021-generator-name-selection.md); dotaz jako metoda nad `session.CreateQuery` s výřezem na objektu dotazu a vedle ní holé HQL; nativně `session.CreateSQLQuery` s `AddEntity` nebo `AddScalar`.

**Hibernate** ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes), rozhodnutí [077](./decisions/077-hibernate-wrapper-over-the-shared-jpa-layer.md)). *Zdroj:* sdílená vrstva JPA — anotace a `orm.xml` (ten nad anotacemi, `metadata-complete` je vypne), přístup podle `@Access` nebo místa `@Id`, `@IdClass` a `@EmbeddedId`, generátory s kanonickými parametry (rozhodnutí [020](./decisions/020-canonical-generator-parameter-vocabulary.md)), vztahy s `@JoinColumn`, `@MapsId` a `@JoinTable` —, k tomu `@Nationalized`; JPQL vlastním parserem včetně joinu po asociační cestě (rozhodnutí [101](./decisions/101-a-join-along-an-association-path-is-derived-from-the-relation.md)) a výřezu na objektu dotazu a z HQL 7.4 `limit` a `offset`, `with` i rekurzivní, poddotaz ve `from`, okenní funkce, `listagg` a `timestampadd`/`timestampdiff`; nativní SQL v `createNativeQuery`. *Cíl:* anotovaná javová třída (`orm.xml` se nevydává), `AUTO` jako sekvence `<Entita>_SEQ`; dotaz jako metoda nad `em.createQuery` vracející `TypedQuery<E>` nebo `Query` a vedle ní holé JPQL.

**EclipseLink** ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes), rozhodnutí [080](./decisions/080-eclipselink-as-the-second-profile-over-the-jpa-layer.md)). *Zdroj:* táž vrstva JPA bez rozšíření — `@Nationalized` nezná a unicode čte jen z doslovného `columnDefinition`, JPQL bez `limit`, `with`, oken, `listagg` a datumové aritmetiky. *Cíl:* totéž co Hibernate s jiným profilem implementace: každý název vypsaný explicitně (implicitní by EclipseLink psal velkými písmeny), `AUTO` jako tabulka čítače `SEQUENCE`, nationalizace doslovným `columnDefinition`, žádný `cast`.

**MyBatis** ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes), rozhodnutí [084](./decisions/084-mybatis-wrapper-over-the-shared-sql-reading.md)). *Zdroj:* doménová třída, rozhraní mapperu (`@Results`, `@Result`, `@One`, `@Many`, `@Select`, podpisy metod s `@Param`) a XML mapper (`<resultMap>` s `extends`, `<id>`, `<result>`, `<association>`, `<collection>`; `<select>` po rozvinutí statických značek `<include>`, `<where>`, `<set>` a `<trim>`, kanonický `<foreach>` jako kolekční parametr, `#{}` jako parametr), text dotazu sdílenou čtečkou T-SQL; skalár parametru z podpisu metody, `parameterType` nebo `javaType`. *Cíl:* POJO bez importu z frameworku a bez konstruktoru a mapper s uzavřeným `<resultMap autoMapping="false">`; dotaz jako hlavička metody rozhraní s `@Param` a `<select>` v mapperu, kolekce jako kanonický `<foreach>`, jediná dynamická značka, kterou builder vydává.

---

## 2. Záporná půlka: případy bez úplného nebo jednoznačného překladu

Řádek má tvar *konstrukce → proč nemá úplný nebo jednoznačný překlad → co nástroj dělá → kde je to rozhodnuto*. Druh hranice je ve sloupci *Co nástroj dělá*, není-li to vyslovená mez (VM), která je pravidlem.

### 2.1 Vyňaté oblasti

| Oblast | Co do ní patří | Co nástroj dělá | Kde |
|---|---|---|---|
| **VO 2** — dědičnost, komponenty a spojené tabulky | NHibernate `<subclass>`, `<joined-subclass>`, `<union-subclass>`, `<component>`, `<dynamic-component>`, `<join>`, `<natural-id>`, `<idbag>`, `<array>`, `<primitive-array>`, `<any>` a kolekce hodnot či komponent; bázový typ C# třídy, který jmenuje jinou entitu převodu (EF Core ho konvencí mapuje jako table per hierarchy); JPA `@Inheritance`, holé `extends` mezi dvěma `@Entity`, `@MappedSuperclass`, `@Embedded`, `@ElementCollection`, `@SecondaryTable`; EF Core `[Owned]` a `[ComplexType]`. Mezireprezentace nese jen plochou třídu a rozšířit ji znamená otevřít novou část, kterou by musely umět i ostatní cíle. | `Loss` u každého prvku, čtení; hierarchie vyjde jako nesouvisející entity, třída `@MappedSuperclass` s vlastním záznamem jako samostatná entita, jejíž atributy dědící entity nedostanou | 030, [048](./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md); [§9, *Hranice záruk*](./architecture.md#hranice-záruk), oblast 2; `NHibernateUnreadElementTest`, `EntityBaseTypeTest` |
| **VO 5** — databázový dialekt | jediný dialekt, SQL Server 2022, deklarovaný deskriptorem; zdroj, který deklaruje dialekt jiného systému; doplnění z katalogu u takového zdroje; `CHECK` a výchozí hodnota sloupce, které jsou doslovným SQL výrazem dialektu | cizí dialekt zastaví čtení doslovného SQL (`Failure`) i doslovného typu sloupce (`Loss`, `DatabaseType`); katalog takovému zdroji fakta dodá a běh nese jediný `Conflict`; `CHECK` a `default` dostanou `Loss`; artefakt únikové cesty je vázaný na deklarovaný dialekt | 086, [088](./decisions/088-a-declared-foreign-source-dialect-is-not-read.md), [091](./decisions/091-the-catalog-completes-a-foreign-source-and-says-so.md), [055](./decisions/055-unique-constraint-as-a-carried-mapping-fact.md); [§9, *Hranice záruk*](./architecture.md#hranice-záruk), oblast 5; `DeclaredSourceDialectTest`, `CatalogForeignSourceDialectTest` |

Rozhodnutí 055 `CHECK` a výchozí hodnoty za vyňatou oblast nepovažovalo; §9 je do oblasti 5 řadí, protože jde o tvrzení o dialektu, a katalog se drží §9.

### 2.2 Mapování

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| líné načtení reference v EclipseLinku (`fetch = LAZY` na `@ManyToOne`, `@OneToOne`) | bez weavingu je tiše eager a model strategii načítání nenese | `Loss`, čtení zdroje EclipseLink; artefakt `fetch` nevyslovuje. **Z F9** | [076](./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), 080; [§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje); `EclipseLinkEntityParserTest` |
| strategie načítání a kaskády (`fetch`, `cascade`, `orphanRemoval`, NHibernate `inverse` a `cascade` kolekce) | do mapovací mezireprezentace nepatří (článek, §5.4) | `Loss`, čtení (`fetch` u JPA mlčky); cíl NHibernate píše `inverse` jen odvozené a `cascade` nikdy | [014](./decisions/014-language-type-model.md), 035, 077; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| pořadí a tvar kolekce: `@OrderColumn`, indexový sloupec `<list>`, `<map>` | model nese kolekci bez indexu a mapy jsou mimo jazykový typový model | `Loss`, čtení; cíl NHibernate píše jen `<set>` a `<bag>` | 014, 035; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes); `NHibernateCollectionTest` |
| klíčová třída ve formě vnořeného klíče (`@EmbeddedId`, `<composite-id class>`) | všechny cíle vykreslují klíč ploše | jméno třídy se nese, změna formy je `Loss`; člen třídy, který mapování nejmenuje, `Loss`; třída s vlastním mapováním `Conflict` | [006](./decisions/006-flat-composite-key-rendering.md), [031](./decisions/031-key-class-as-declaration-of-key-parts.md); [§4.2](./architecture.md#42-primární-klíč); `NHibernateEmbeddedKeyClassTest` |
| `<key-many-to-one>` | klíč se nese ploše, vztah vedle něj | plochý klíč a vlastnící N:1, změna formy `Loss`; bez sloupců `Incompleteness` | 006; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes); `NHibernateKeyManyToOneTest` |
| strategie generování, kterou cíl nevyjádří: identita, sekvence, hi/lo, UUID a increment v anotacích EF Core, strategie části `<composite-id>` jiná než přiřazená, `Increment` v JPA, generátor nebo parametr bez protějšku | anotace EF Core řeknou jen „generuje databáze" nebo „nic", mechanismus je ve fluent API; ostatní cíle protějšek nemají | `Loss`, zápis; rozklad `AUTO` podle profilu implementace a dosazené `assigned` jsou `Convention` | [011](./decisions/011-key-generation-strategy-vocabulary.md), 020, 021, [064](./decisions/064-absence-of-generation-as-a-catalog-fact.md), 077, 080; [§4.2](./architecture.md#42-primární-klíč) |
| nullabilita části klíče (`int? Id`) | klíč nesmí být `NULL` | EF Core a NHibernate typ zploští s `Loss` (`Nullability`); JPA píše část klíče vždy obalovým typem | [054](./decisions/054-nullable-key-part-is-a-reported-loss.md); `NullableKeyPartTest` |
| nullable sloupec za nenullovatelnou vlastností, cíl EF Core | `IsRequired(false)` je jen ve fluent API | `Loss`, zápis | [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| entita bez primárního klíče | NHibernate a JPA klíč vyžadují | `Failure` do NHibernatu, Hibernatu a EclipseLinku; do EF Core `[Keyless]`, do Dapperu a MyBatisu prostá třída | [063](./decisions/063-stated-keylessness-as-a-carried-fact.md); [§5.1](./architecture.md#51-diagnostika-převodu) |
| neunikátní index (`index`, `[Index]` bez `IsUnique`) | výkonnostní artefakt, ne mapovací fakt | `Loss`, čtení (JPA `@Table(indexes)` mlčí, viz 2.13) | 055; [§4.1](./architecture.md#41-entity-a-mapování) |
| unikátní omezení nad vlastností, která v cíli není sloupcem (část klíče, navigace, verze v NHibernatu; transientní vlastnost) | `unique` na `<many-to-one>` znamená vztah 1:1; transientní vlastnost sloupec nemá | `Loss`, zápis; nad nedeklarovanou vlastností `Incompleteness`; vícesloupcové bez jména dostane jméno se záznamem `Convention` | 055, [072](./decisions/072-a-transient-property-is-a-carried-mapping-fact.md); `UniqueConstraintTest` |
| druhý sloupec verze, verze na části klíče, navigaci nebo transientní vlastnosti (NHibernate) | `<version>` je jeden a nad obyčejnou vlastností | `Loss`, zápis | 030; `VersionColumnTest` |
| druhý a další `<column>` jedné vlastnosti | vlastnost by potřebovala kompozitní uživatelský typ | `Loss` u každého, čtení | 030; `NHibernateColumnElementTest` |
| hodnota `property-ref` | builder ji odvozuje ze vztahu znovu | `Loss`, čtení; inverzní 1:1 bez vlastnické protistrany `Incompleteness` | [012](./decisions/012-foreign-key-rendering.md); [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| N:M bez jména spojovací tabulky nebo sloupců, N:M entity na sebe samu, implicitní spojovací tabulka EF Core | spojovací entitu nemá z čeho postavit a nástroj ji nevymýšlí | vztah zůstane N:M a fáze rozresolvování vydá `Incompleteness`; s katalogem se spojovací entita syntetizuje | [005](./decisions/005-many-to-many-as-explicit-junction-entity.md), 015, 067; [§4.3](./architecture.md#43-vztahy); `JunctionEntitySynthesisTest` |
| cizí klíč EF Core bez vlastnosti (stínová vlastnost), kompozitní cizí klíč bez explicitního párování, vztah 1:1 | anotace páry sloupců nevysloví; 1:1 anotace nevyjádří | vztah bez sloupců, cíl je vynechá se záznamem `Convention`, katalog je doplní; 1:1 se čte jako N:1 | 012, 015, 067; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| výchozí sloupec cizího klíče JPA (`<vlastnost>_<sloupec>` bez `@JoinColumn`) | konvence se nematerializuje | páry sloupců zůstanou prázdné; join po asociační cestě bez katalogu `Failure` (otevřená položka v *Užitečné, ne nutné*) | 067, 101 |
| vztahy MyBatisu (`<association>`, `<collection>`) a jeho `<id>` | vztah nese tvar výsledku, ne cizí klíč; `<id>` je identita řádku výsledku, ne klíč tabulky | navigace bez sloupců, které doplní katalog; `<id>` dá `Incompleteness` (`PrimaryKey`), takže MyBatis → MyBatis bez katalogu klíč neudrží | 084 |
| typ mimo slovník: neznámý databázový typ, `Currency` NHibernate, dvojice, kterou NHibernate 5.7 neregistruje (`DateOnly` nad `datetime2`), neznámý jazykový typ (`Instant`, `java.util.Date`, mapa), přesnost u JPA pod jménem jiné rodiny | slovník typů je uzavřený a cíl dvojici nezná | neznámý typ `Incompleteness` s doslovným typem na únikové cestě; `Currency` jako `Decimal(19,4)` s `Loss`; ostatní `Loss` | [019](./decisions/019-neutral-database-type-vocabulary.md), [052](./decisions/052-literal-sql-type-reaches-the-ef-core-annotation.md), [071](./decisions/071-five-scalars-with-a-counterpart-in-both-ecosystems.md), [075](./decisions/075-unknown-language-type-is-a-reported-incompleteness.md), [079](./decisions/079-fractional-second-precision-as-second-precision.md); [§4.1](./architecture.md#41-entity-a-mapování) |
| nationalizace v EclipseLinku | EclipseLink `@Nationalized` nezná | ze zdroje `Loss`; cíl píše doslovný `columnDefinition`, nad rodinou bez národní varianty `Loss` | 080; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| modifikátory a inicializátory přes hranici ekosystémů | druhý jazyk modifikátor nemá nebo inicializátor píše jinak | `virtual`, `override`, `sealed`, `new` a `required` odpadnou do Javy mlčky, ostatní a javové `final` a `volatile` `Loss`; inicializátor jen jako literál, který obě řeči píšou stejně, jinak `Loss`; `sealed`, `partial` a `abstract` třídy model nenese | 076, 077; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| fakt, který cíl podle deskriptoru nevyjádří — u Dapperu všech třináct, u MyBatisu devět | je to tvrzení o frameworku, ne o nástroji | mechanický `Loss` u každého faktu, který model nese | 004, 009, 084; tabulka 1.5 |
| atributy NHibernate mapování bez místa v modelu: `formula`, `access`, `insert`, `update`, `lazy`, `generated`, `optimistic-lock` na `<property>`; `discriminator-value`, `where`, `mutable`, `optimistic-lock`, `dynamic-insert`, `dynamic-update`, `batch-size`, `lazy` na `<class>` | model je nenese; `formula` a `where` mění, co entita čte | `Loss`, čtení (ostatní atributy mlčí, viz 2.13) | 048; [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes); `NHibernateUnmodelledAttributeTest` |
| anotace mimo podmnožinu: neznámá anotace EF Core, JPA `@Enumerated`, `@Lob` a `@NamedQuery`, anotace výrobce Hibernatu (kromě `@Nationalized`) a EclipseLinku | model fakt nenese | `Loss` se jménem anotace, čtení | 048, 077, 080 |
| fluent konfigurace EF Core (`OnModelCreating`) | není nárokovaná jednotka; otevřená otázka v *Zbytcích* | nečte se; kontext je z entit vyloučený s `Loss`, který to jmenuje | 111 |
| Dapper.Contrib (`[Table]`, `[Key]`, `[ExplicitKey]`) a alias v SQL jako zdroj mapování Dapperu | v rozsahu, nebo mimo něj, není rozhodnuté (*Zbytky*) | nečte se; atributy mlčky, alias se nepáruje a katalog páruje podle jména | 015, 067 |

Jedno místo mapování je **vadou**, ne mezí, a vede ho kategorie *Vady* v [`open-items.md`](./open-items.md): číselná verze se v EF Core vypíše jako `[Timestamp]`. Druhá vada, jednosloupcový `<key>` inverzní kolekce nad složeným cizím klíčem v NHibernatu, je od 2026-10-02 opravená ([§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes)).

### 2.3 Jednotka a předání dotazu

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| jednotka deklarovaná rolí (`CSharpEntity`, `JavaQuery` …), v jazyce, který zdrojový framework nečte, nebo jednotka, ze které nic nevzešlo | jednotka deklaruje jen jazyk | `Failure` o jednotce, orchestrace | [045](./decisions/045-a-conversion-that-produced-nothing-says-so.md), [066](./decisions/066-records-attributed-to-the-input-unit.md), 111; [§5.1](./architecture.md#51-diagnostika-převodu) |
| aplikační kód, který dotaz sám nepředává (služba volající repozitář, DTO projekce) | od entity se nerozezná | čte se jako entita. **Z F14** | 111; [§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje); `WholeSourceFileTest` |
| procházení repozitáře, archiv projektu; jméno jednotky na výstupním artefaktu; zobrazení mezireprezentace | vstup vybírá uživatel; záznam o sloučené entitě žádné jedné jednotce nepatří | nenárokuje se. **Z F14** | 066, [094](./decisions/094-entity-identity-inside-a-conversion.md), 111; [§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje) |
| příkaz, který není čtecí `SELECT` (`INSERT`, `UPDATE`, `DELETE`, `DECLARE`, `SET`, `USE`, `SELECT … INTO`, `SELECT @x = …`) | zápis mění stav, ze kterého čtou ostatní dotazy | `Failure` celé holé jednotky, u vloženého textu toho dotazu | 108; `SqlScriptTest` |
| druhý `SELECT` v textu vloženém do hostitele (volání Dapperu mimo `QueryMultiple`, `<select>`, `<sql-query>`) | hostitel ho pošle jako jeden příkaz s jedním výsledkem | `Failure` toho dotazu | 108, 109 |
| `FOR XML`, `FOR JSON`, `TABLESAMPLE`, `WITH XMLNAMESPACES` | vrací dokument nebo vzorek, ne řádky | `Failure`, čtení | [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| proměnná dotazu přiřazená víckrát nebo podmíněně, kterou kód dál prodlužuje; objekt dotazu, se kterým kód pokračuje v jiném příkazu | dotaz se skládá za běhu | `Failure`, čtení; co kód dělá s výsledkem a s objektem dotazu, který jednotku opustí, se nesleduje | 109, 113; `CodeQueryUnitTest`, `QueryObjectPaginationTest` |
| text dotazu, který není literál (Dapper, JPA, NHibernate) | nástroj nemá co číst | `Incompleteness` a dotaz nevznikne (`Failure`, že nenese instrukce) | 026, 109 |
| dotaz skládaný za běhu: `QueryOver`, `CreateCriteria`; v EF Core díra interpolace spočtená na místě, argument `SqlQueryRaw`, který není jméno, `@x` psané v textu | dotaz nemá text, nebo hodnota vzniká až za běhu | `QueryOver` a `CreateCriteria` `Failure`, zbytek `Incompleteness` | 113; `NativeSqlTest` |
| předání jménem: `GetNamedQuery`, `createNamedQuery` | dotaz stojí tam, kde je definovaný | `Incompleteness`; u NHibernatu se definice v `hbm.xml` (`<query>`, `<sql-query>`) čte, **u JPA ne**: `@NamedQuery` a `<named-query>` jsou `Loss`, takže pojmenovaný dotaz JPA se nepřekládá nikde | 081, 113; `NativeSqlTest`, `HibernateOrmXmlTest` |
| volby pojmenovaného dotazu `hbm.xml` (`cacheable`, `cache-region`, `cache-mode`, `fetch-size`, `timeout`, `flush-mode`, `read-only`, `comment`, u nativního `callable` a `<synchronize>`); `<query-param>`, jehož typ nemá skalár slovníku (`XmlDoc`, uživatelský typ) | říkají, jak NHibernate dotaz provede, ne které řádky čte; skalár mimo slovník model nenese | `Loss`, čtení; hodnota, kterou volba má i nevyslovená (`cacheable="false"`), se nehlásí; `callable` přijde o značku a text se čte, co vysloví — volání (`exec`) odmítne čtení | 048, 083; `NHibernateNamedQueryDeclarationTest` |
| prvek uvnitř `<query>` nebo `<sql-query>`, který schéma `hbm.xml` nepřipouští; tentýž `<query-param>` dvakrát se dvěma skaláry | text potomka se do dotazu nečte a mohl by být jeho kusem; jeden parametr nemá dva typy | `Failure`, čtení | 053, 083; `NHibernateNamedQueryDeclarationTest` |
| kořen EF Core tvaru `x.M`, kde jednotka uvádí pro `x` typ, který převod mapuje jako entitu; člen typu `IQueryable<T>`, přes který se dotaz skládá | entita `DbSet` nemá; člen se nesleduje | `Failure`, čtení | 114; `LoadedEntityNavigationTest` |
| navigace entity z jiného souboru bez mapovací jednotky; proměnná naplněná voláním, které jednotka nedeklaruje (`repository.Load(id)`) | jednotka o jménu nic neuvádí, takže rozhoduje místo | čte se podle místa jako dotaz; s mapovací jednotkou `Failure` | 114 |
| explicitní načtení nad jménem, které jednotka neuvádí, s navigací, kterou deklaruje víc entit nebo žádná; navigace, kterou text nefixuje; M:N; načtení bez mapovací jednotky | filtr se odvozuje z mapování a nic se nehádá | `Failure` jmenovitě; builder EF Core explicitní načtení nepíše, vydá řetěz nad `Set<T>()`; `NHibernateUtil.Initialize` se nečte | 115; `ExplicitLoadingTest` |
| dynamický příkaz MyBatisu (`<if>`, `<choose>`, `<when>`, `<otherwise>`, `<bind>`) | jeden `<select>` je rodina příkazů, jejíž členové vracejí různé řádky (část 3) | `Failure`, který značku jmenuje. **Z F8** | 084; [§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje); `MyBatisQueryTest` |
| `<foreach>` mimo kanonický tvar, `${}`, `#{a.b}`, `@` psané už ve zdrojovém SQL, `@SelectProvider`; týž příkaz v anotaci i v XML | textová substituce není hodnota; parametr s cestou signatura nevysloví; dvě definice téhož příkazu MyBatis sám odmítne | `Failure`, čtení; atributy `jdbcType`, `typeHandler`, `mode` v `#{}` `Loss` | 084; `MyBatisQueryTest` |
| zápisový příkaz MyBatisu v obou formách (`<insert>`, `<update>`, `<delete>`, `@Insert`, `@Update`, `@Delete`); text v escape JDBC `{call …}`; jazykový ovladač jiný než XML (`lang`, `@Lang`) | zápis ani volání procedury není čtecí dotaz; jiný ovladač čte text jiným jazykem a přečtený jako XML by mohl být jiným příkazem | `Failure`, čtení, který příkaz, escape nebo ovladač jmenuje | 053, 084, 109; `MyBatisQueryTest` |
| volby příkazu MyBatisu: atributy `<select>` mimo `id`, `parameterType`, `resultType` a `resultMap` (`statementType`, `fetchSize`, `timeout`, `useCache`, `flushCache`, `resultSetType`, `resultOrdered`, `resultSets`, `affectData`, `parameterMap`, `databaseId`), prvky `@Options` a `@MapKey` | říkají, jak MyBatis příkaz provede nebo výsledek uspořádá, ne které řádky čte | `Loss`, čtení; hodnota, kterou volba má i nevyslovená, se nehlásí; u `statementType="CALLABLE"` rozhoduje text — dotaz se přeloží, volání (`EXEC`, `{call …}`) odmítne čtení | 048, 084; `MyBatisQueryTest` |
| volby předání v kódu: volání na objektu dotazu JPA mimo parametry, výřez a vykonání (`setHint`, `setFlushMode`, `setTimeout`, `setCacheStoreMode` …), zřetězená i na proměnné, která objekt drží; argumenty volání Dapperu `buffered` a `commandTimeout` a argument, pro který přetížení nemá místo; zámek (`setLockMode`, `setHibernateLockMode`, nápověda `eclipselink.pessimistic-lock`) | říkají, jak framework dotaz provede, ne které řádky čte; zámek řádky nemění, mění, co s nimi dotaz udělá | `Loss`, čtení, který volání nebo argument jmenuje; navázání parametru, objekt parametrů, transakce a volání, které dotaz vykoná (`getResultList`, `getSingleResult`, `list` …), nic | 048, 109; `QueryObjectCallTest`, `DapperCallArgumentTest` |
| mapování výsledku při předání v kódu: transformery Hibernate (`setTupleTransformer`, `setResultListTransformer`, `setResultTransformer`), nápověda `eclipselink.result-type`, multi-mapping Dapperu (`map`, `splitOn`, `types`) | dotaz mapování výsledku nenese a cíl řádek odvodí znovu | jeden `Loss` (`Projection`), čtení | 067, 109; `QueryObjectCallTest`, `DapperCallArgumentTest` |
| volání a nápovědy na objektu dotazu, které mění řádky: `setPage`, `getKeyedResultList` a `getResultCount` Hibernatu, `eclipselink.jdbc.max-rows`, `eclipselink.jdbc.first-result`, `eclipselink.history.as-of` (i `.scn`) a `eclipselink.query-type` EclipseLinku | model je nenese a bez nich by dotaz vrátil jiné řádky | `Failure`, čtení, i v pozdějším příkazu; pod Hibernatem je nápověda EclipseLinku `Loss`, protože ji Hibernate nečte | 070, 109; `QueryObjectCallTest` |
| `commandType` volání Dapperu se `StoredProcedure`, s `TableDirect` nebo spočtený za běhu | Dapper text pošle jako jméno procedury nebo tabulky, ne jako dotaz, nebo se to rozhodne až za běhu | `Failure`, čtení, který typ příkazu jmenuje | 053, 109; `DapperCallArgumentTest` |
| vstup zanořený hlouběji než strop (výchozí 128, nastavuje provozovatel) | rekurzivní sestup by mohl shodit proces | `Failure` s řádkem a sloupcem před sestupem; XML strop nemá | [092](./decisions/092-input-nesting-depth-capped-before-the-descent.md); `NestingDepthCapTest` |
| nečitelný text jednotky | — | `Failure` s řádkem a sloupcem u SQL, Javy, HQL, JPQL a XML; u C# bez pozice (**Z S7**, otevřená položka v *Zbytcích*) | [093](./decisions/093-unreadable-input-is-a-unit-failure.md); [§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje) |

### 2.4 Parametry

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| parametr, jehož skalár se neodvodí — porovnaný s jiným parametrem, se sloupcem mimo mapování nebo s vlastností bez jazykového typu | model skalár parametru nenese a bere ho z druhé strany; cíle potřebují typovanou hlavičku metody | `Failure` (`QueryParameter`), brána | [083](./decisions/083-parameter-as-the-fifth-operand-shape.md), 107; [§7](./architecture.md#7-rozhraní-parserů-a-builderů) |
| parametr zdroje Dapper v běhu bez katalogu, nebo když katalog tabulku nenajde či ji najde ve dvou schématech | Dapper tabulku ani sloupce netvrdí a vazbu tabulky dá jen katalog | `Failure`, brána; s katalogem, který tabulku má, se typuje do všech cílů | 083, 105, 106; [§5.2](./architecture.md#52-doplňování-mapovacích-faktů-z-katalogu) |
| týž název se dvěma skaláry; jednou jako kolekce a jednou jako hodnota (u Dapperu `IN @ids` vedle `IN (@ids)`) | jeden parametr metody nemá dva typy | `Failure`, brána (u Dapperu už wrapper) | 083, 106; `DapperCollectionParameterTest` |
| pojmenovaný a poziční parametr v jednom dotazu; jméno, které není prostý identifikátor | JPQL souběh nepřipouští; cestu k vlastnosti signatura nevysloví | `Failure`, brána | 083 |
| kolekční parametr mimo pravou stranu `IN`, uvnitř výčtu (`IN (1, :ids)`) nebo jako počet řádků; skalár jako pravá strana `IN` | kolekce stojí jen na místě seznamu, výčet bere jednotlivé hodnoty | `Failure` (`QueryParameter`), čtení nebo brána | 083, [085](./decisions/085-a-row-count-is-a-number-or-a-parameter.md), [102](./decisions/102-aggregate-distinct-like-escape-and-a-parameter-among-listed-values-are-carried.md) |
| celočíselný skalár počtu řádků širší než `Int` (MyBatis `Long`) | API stránkování cílů jsou 32bitová | `Loss`, metoda bere `int` | 085 |
| poziční parametr do jiného cíle než JPQL | ostatní cíle poziční tvar nemají | vyjde pojmenovaný `p1`, `p2` se záznamem `Convention` | 083 |
| `${…}` MyBatisu | je to textová substituce, ne hodnota | `Failure`, čtení | 082, 084 |

### 2.5 `LIKE`, `DISTINCT` a výčet hodnot

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| únikový znak, který není literál, je delší než jeden znak nebo stojí u jiného operátoru než `LIKE` | je to fakt o čtení vzorku, ne hodnota volajícího; T-SQL i JPQL berou jeden znak | `Failure` (`Filtering`), čtení nebo brána | 102; `LikeEscapeTest` |
| řetězcová metoda LINQ s přetížením `StringComparison` | provider ji na vzorek nepřeloží | nečte se | [051](./decisions/051-like-pattern-translated-not-carried-over.md); [§5](./architecture.md#5-parsery-a-buildery--jak-fungují-dnes) |
| agregát nad různými celými řádky — `count(distinct c)` v JPQL, `COUNT(DISTINCT *)`, LINQ `Distinct().Count()`, `.Sum()`, `.Average()` nad celou entitou | žádné SQL nemá tvar agregátu nad různými řádky | `Failure`, čtení nebo brána; `Max` a `Min` nad zhroucenými řádky značku vypustí se záznamem `Convention` | [073](./decisions/073-distinct-as-a-flag-of-the-query-scope.md), 102; `AggregateDistinctTest` |
| krok LINQ za `Distinct()` celé entity (`Select`, `GroupBy`, `Join`) | model nese `DISTINCT` nad koncovou projekcí | `Failure`, čtení; po projekci se čte jako mezivýsledek | 073, [112](./decisions/112-a-query-as-a-row-source-is-a-named-intermediate-result.md) |
| pod `DISTINCT` klíč řazení mimo projekci | T-SQL ho odmítá a LINQ ho nepojmenuje | `Failure` (`Ordering`), brána | 073 |
| `DISTINCT` nad `EXCEPT ALL` | výsledek není `EXCEPT` | `Failure`, brána; nad `UNION ALL` přepis na `UNION` a nad ostatními operacemi vypuštění, obojí `Convention` | 073; `DistinctQueryTest` |
| prvek výčtu `NULL`, sloupec, funkce nebo výraz; prázdný výčet | výčet nese literály a skalární parametry; `NULL` v `NOT IN` vyhodnotí cíle různě; `IN ()` nemá tvar | `Failure` (`Filtering`), čtení | [074](./decisions/074-a-list-of-values-as-the-fourth-operand-shape.md), 102, 107 |
| výčet se skaláry mimo jednu číselnou rodinu (`IN (1, 'a')`, `IN (0.5, 1E0)`) | C# by pole nepřeložil a T-SQL by porovnával jinou hodnotu | `Failure`, brána | 074; `InValueListTest` |

### 2.6 Joiny

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| křížový join čárkou; LINQ `SelectMany` nad druhým zdrojem bez filtru | model křížový join nenese a bez něj by vyšly jiné řádky | `Failure` (`Join`), čtení | 070, 101, 113 |
| laterální join (`CROSS APPLY`, `OUTER APPLY`, HQL `join lateral`), join na tabulkovou funkci nebo `VALUES` | slovník laterální odkaz nemá | `Failure`, čtení, v každém cíli | 112, 113 |
| entity join HQL nebo JPQL bez `with` či `on`; nepřečtená podmínka joinu; poddotaz v `ON` | join filtruje i násobí; LINQ poddotaz do klíčových selektorů nevepíše | `Failure`, čtení nebo brána | 070, [061](./decisions/061-subquery-as-a-condition-operand.md) |
| `fetch` u joinu | mění načítání, ne řádky | `Loss`, čtení | 070, 101 |
| asociační cesta, kterou mapy převodu nerozresolvují (entita, vztah nebo cíl mimo převod, prázdné páry sloupců), cesta přes víc asociací nebo přes N:M | podmínka se odvozuje z mapování a nic se nehádá; mezičlánek by potřeboval vymyšlený alias | `Failure` (`Join`) se jménem cesty, čtení HQL, JPQL i LINQ | 101; `LinqAssociationPathJoinTest` |
| join po cestě ve zdroji | builder cestu nevydává | vyjde entity join s odvozenou podmínkou, řádky jsou tytéž | 101 |
| LINQ `GroupJoin` (`join … into`), `Zip`; `Join` se selektory, které nejsou lambdy nebo se nespárují | model je nenese | `Failure` (`Join`), čtení | 103, 113 |
| result selector LINQ joinu, který vynechá řádek, vrátí jen jednu stranu nebo celý řádek vedle sloupců; `SelectMany` s jedním argumentem | řádky jsou tytéž, sloupce ne | `Loss` (`Projection`), čtení | 070; `LinqJoinResultSelectorTest` |
| spojovaná posloupnost s jiným krokem než `Where` | je to dotaz sám o sobě | `Failure` (`IntermediateResult`), čtení | 113; `LinqJoinConditionTest` |
| cíl EF Core: pravý nebo plný vnější join s podmínkou nad rámec rovností klíčů; podmínka joinu se sloupcem bez tabulky | filtr posloupnosti by ubral zachované řádky; sloupec nejde přiřadit straně | nativní SQL, zápis | 113 |
| cíl NHibernate: plný vnější join | HQL 5.7 ho nemá a nemá ani množinové operace, ze kterých by šel složit | nativní SQL, zápis | 065, 113 |

### 2.7 Poddotazy, množinové operace a stránkování

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| poddotaz v operandu, jehož tělo je množinová operace; `IN` nebo skalární porovnání s poddotazem, který nemá právě jednu projekci | operand nese jeden `SELECT`; SQL by druhé odmítlo za běhu | `Failure`, brána | 061; `SubQueryConditionTest` |
| řazení uvnitř poddotazu nebo těla definice bez výřezu | T-SQL ho tam nepřipouští a řádky vnějšího dotazu nemění | `Loss` | 061, 112 |
| výřez uvnitř poddotazu nebo operandu množinové operace do HQL a JPQL; literálový počet řádků nad `Int32` do EF Core, NHibernatu a obou JPA | text výřez nemá, ten žije na objektu dotazu; API jsou 32bitová | nativní SQL, zápis | 060, 113 |
| cíl EF Core: skalární poddotaz, který není jediným neseskupeným agregátem; `IN` nad neseskupeným agregátem; množinová operace nad různými typy prvků | `First()` by tiše vybral jeden řádek; LINQ různé typy nesloží | nativní SQL, zápis | 113 |
| LINQ metoda na jeden řádek v pozici operandu (`… == ctx.Orders.Select(…).FirstOrDefault()`) | skalární poddotaz s výřezem nemá v modelu výrobce | nečte se | 103 |
| `INTERSECT ALL`; `EXCEPT ALL` do T-SQL a LINQ | slovník `INTERSECT ALL` nemá; `EXCEPT ALL` SQL Server za běhu odmítá a LINQ ho nemá | `INTERSECT ALL` `Failure` při čtení; `EXCEPT ALL` `Failure` zápisu T-SQL (i na únikové cestě), cíle JPA ho píšou | [§4.4](./architecture.md#44-dotazové-instrukce-a-podmínkový-strom) |
| řazení nebo projekce za množinovou operací | model za operací nic nenese; řádky jsou tytéž | `Loss`, čtení | 053, 070 |
| filtr, join, seskupení nebo výřez za množinovou operací; `OFFSET` uvnitř jejího operandu | bez nich by vyšly jiné řádky; T-SQL výřez v operandu nezapíše | `Failure`, čtení nebo zápis | 053, [060](./decisions/060-pagination-as-a-query-instruction.md), 113 |
| počet řádků, který není číslo ani parametr (`TOP (@n + 1)`, hodnota spočtená za běhu); `TOP … PERCENT`, `WITH TIES`, `TOP` vedle `OFFSET` | počet řádků je číslo, nebo parametr | `Failure` (`Pagination`), čtení | 060, 085 |
| výřez z výřezu, `Skip` za `Take`, krok, který s výřezem nekomutuje; `Last`; výřez na objektu dotazu vedle `limit` | výřez je poslední operací rozsahu a model nese jeden | `Failure`, čtení nebo brána; za výřezem projektovaných řádků se krok čte jako mezivýsledek | 060, 103, 112; `PaginationQueryTest` |
| `First`, `Single`, `ElementAt` a jejich `…OrDefault` | čtou se jako výřez jednoho řádku | `Convention`; artefakt vrací seznam o nejvýš jednom prvku a u `Single` nekontroluje druhý řádek | 103; `LinqSingleRowTerminalTest` |

### 2.8 Výrazy a funkce

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| funkce mimo slovník — `CONVERT`, `REPLACE` mimo vzorek, `LEFT`, `RIGHT`, `SYSDATETIME()`, `DateTime.UtcNow`, HQL `str()`, `year()` v JPQL a cokoli dalšího mimo sedmnáct funkcí `QueryFunction` | slovník je uzavřený; funkce do něj vstupuje až se zápisem v T-SQL a ověřeným zápisem v každém cíli | nečte se (`Expression`) | 107, 113; [§9, *Rozsah implementace ve zkratce*](./architecture.md#rozsah-implementace-ve-zkratce) |
| převod do jiného typu než `INT`, `BIGINT`, `REAL`, `FLOAT` a `NVARCHAR(MAX)`, převod s délkou nebo do neunicode textu; `DATEADD` a `DATEDIFF` s jednotkou týden, čtvrtletí nebo milisekunda; `ROUND` se třetím argumentem | mohly by hodnotu změnit; model nese pět skalárů převodu a jednotky od roku po sekundu; třetí argument mění zaokrouhlení na ořez | nečte se | 113 |
| `+`, jehož ani jedna strana není typovaná | dva cíle ho hláskují různě (konkatenace, nebo sčítání) | `Failure` (`Expression`), brána | 107 |
| aritmetika, `COALESCE` nebo `CASE` nad skaláry, které se nesjednotí (`Decimal` s `Float`) | C# by to nepřeložil a T-SQL by porovnával jinou hodnotu | `Failure`, brána | 107 |
| agregát nad agregátem (`SUM(COUNT(*))`), i do seznamu | žádný cíl ho nezapíše | `Failure`, brána; přes odvozenou tabulku se nese | 107, 112 |
| výraz v projekci bez aliasu | sloupec bez jména nepřečte žádný cíl a jméno nástroj nevymýšlí | `Failure`, brána; konstanta bez aliasu `Loss` při čtení | 107, 028 |
| výraz jako prvek výčtu nebo jako počet řádků | výčet a počet řádků mají vlastní uzavřené tvary | `Failure`, čtení | 074, 085, 107 |
| konkatenace nad sloupcem ve zdroji LINQ | C# bere `null` jako prázdný řetězec, SQL, HQL i JPQL vracejí `NULL`, a model ten rozdíl nenese | `Loss`, čtení; artefakt má sémantiku SQL | 107 |
| funkce, kterou deskriptor cíle neuvádí — `DateAdd` a `DateDiff` u NHibernatu, k nim `Cast` u EclipseLinku | sonda proti připnuté verzi zápis nepotvrdila | nativní SQL, brána | 113; deskriptory |
| převod textu do textu do NHibernatu (`NVARCHAR(4000)`) a Hibernatu (`varchar(max)`); do EF Core `Math.Round` nad celým číslem, hodina a menší jednotka přičtená k datu bez času, převod textu na číslo | jazyk cíle tvar má, ale s jinou hodnotou | nativní SQL, zápis | 113 |
| `CASE` bez `ELSE` do LINQ, kde brána skalár větví neodvodí | C# nemá čím otypovat `null` | `Failure`, zápis | 107 |
| konstruktor `DateTime` s počítaným argumentem nebo neexistujícím datem; řetězec okamžiku mimo ISO 8601 porovnaný s časovým sloupcem | vyhodnotit ho by znamenalo spustit program; databáze by ho odmítla až za běhu | konstruktor nečte se, řetězec `Failure` (`Filtering`) v bráně | [024](./decisions/024-typed-query-operand.md), 070; [§7](./architecture.md#7-rozhraní-parserů-a-builderů) |
| alias projekce, který je vyhrazeným slovem cíle (EclipseLink `Size`) | alias je slovo zdroje a nepřepisuje se | nehlásí se; odmítne ho až framework | 028 |

### 2.9 Seskupení, okenní funkce a agregace do seznamu

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| projekce, `HAVING` nebo řazení seskupeného dotazu, které jmenují sloupec mimo klíč a mimo agregát; klíč seskupení, který agreguje nebo je okenní funkcí | pravidlo seskupení SQL | `Failure` (`Grouping`), brána | 113; [§7](./architecture.md#7-rozhraní-parserů-a-builderů) |
| agregát vedle prostých sloupců bez seskupení | neplatný už zdrojový dotaz | `Incompleteness`; artefakt vznikne a odmítne ho databáze | [§7](./architecture.md#7-rozhraní-parserů-a-builderů) |
| `GROUP BY ROLLUP`, `CUBE`, `GROUPING SETS` | model je nenese | `Failure`, čtení | 070 |
| LINQ `GroupBy` s druhým argumentem (selektor prvků, result selector) | čte se jen klíč | `Loss` (`Grouping`), čtení | 103 |
| cíl EF Core: klíč-výraz, který žádná projekce nepojmenuje; agregát přes celý výsledek bez seskupení | anonymní klíč by potřeboval vymyšlené jméno; LINQ agregát přes všechno vysloví jen voláním, které dotaz vykoná | nativní SQL, zápis | 028, 113 |
| cíl EclipseLink: klíč seskupení s literálem | EclipseLink literál naváže jako parametr a seskupení se pak liší od projekce | nativní SQL, zápis | 113 |
| okenní agregát (`SUM(…) OVER`), rámec okna, okno bez řazení, jiná okenní funkce než tři řadicí | slovník nese jen řazení nad oknem, a to s povinným řazením | nečte se (`WindowFunction`) | 113 |
| okenní funkce mimo projekci (ve filtru, pod agregátem, v klíči) | SQL ji tam nepřipustí; filtr nad ní jde přes mezivýsledek | `Failure` (`WindowFunction`), brána | 113 |
| LINQ obrat `GroupBy(…).Select(g => g.OrderBy(…).First())` | je to jiný tvar než okenní funkce | jako okenní funkce se nečte | 113 |
| `STRING_AGG` s oddělovačem, který není literál; LINQ `string.Join` mimo prvky skupiny | — | nečte se | 113 |
| LINQ `string.Join` nad sloupcem, který smí mít `NULL` | C# bere `NULL` jako prázdný řetězec, `STRING_AGG` ho vynechá | `Loss`, čtení | 113 |
| cíl EF Core: seznam mimo prvky skupiny, nad korelovaným poddotazem nebo nad sloupcem, který smí mít `NULL` | EF Core 10 seznam spojí na klientovi nebo vloží prázdné řetězce | nativní SQL, zápis | 113 |

### 2.10 Mezivýsledek a rekurze

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| definice, která čte dotaz kolem sebe (laterální odkaz, korelovaná odvozená tabulka) | slovník laterální odkaz nemá | `Failure`, čtení nebo brána, v každém cíli | 112 |
| definice nad celou entitou, se sloupcem bez jména nebo se dvěma sloupci jednoho jména | z definice by se stala druhá entita; T-SQL i Hibernate to odmítají | `Failure` (`IntermediateResult`), brána | 112 |
| dvě definice téhož jména; alias odvozené tabulky shodný se jménem čtené tabulky nebo jiné definice; dopředný odkaz | vyzdvižená definice by tabulku zastínila; pořadí definic je pořadím závislostí | `Failure`, čtení nebo brána | 112 |
| HQL seznam sloupců `with x(a, b)`; `with` a poddotaz ve `from` v jednotce EclipseLinku; skládání nad projekcí v LINQ NHibernatu | jazyk ho nemá nebo ho provider nepřeloží | `Failure`, čtení | 112 |
| HQL nápověda `materialized` | mění plán, ne řádky | `Loss`, čtení | 112 |
| odvozená tabulka ve zdroji | model si syntaxi nepamatuje | cíl SQL ji píše jako `WITH`, řádky jsou tytéž | 112 |
| porušené pravidlo rekurzivního `WITH` SQL Serveru — jiná operace než `UNION ALL` před rekurzivním členem, kotva jmenuje sebe, rekurzivní člen jmenuje definici jinak než jednou ve `FROM` nebo vnitřním joinu, v rekurzivním členu `DISTINCT`, seskupení, agregát, výřez, vnější join nebo poddotaz, jiný počet nebo skalár sloupců než v kotvě, vzájemná rekurze | SQL Server takový dotaz nespustí | `Failure` (`IntermediateResult`), brána, v každém cíli včetně únikové cesty | 113; `RecursionTest` |
| HQL `search` a `cycle` | SQL Server je nemá a slovník je nenese | `Failure`, čtení | 113 |
| rekurzivní člen, který přičítá `COUNT` ke kotvě začínající konstantou | model typuje `COUNT` jako `Long`, T-SQL jako `int` | `Failure`, brána — ačkoli by ho SQL Server přijal; vědomě bezpečnější strana | 113; [§9, *Rozsah implementace ve zkratce*](./architecture.md#rozsah-implementace-ve-zkratce) |
| délka řetězce a přesnost desetinného čísla mezi kotvou a rekurzivním členem | model je nenese | brána je nehlídá; dotaz musí srovnat text převodem, jinak ho odmítne SQL Server | 113 |
| limit rekurze bez rekurzivní definice | nic neohraničuje | vypustí se se záznamem `Convention` | [§7](./architecture.md#7-rozhraní-parserů-a-builderů) |
| mez hloubky a sledy místo vrcholů | rekurzivní člen SQL Serveru řádky neslučuje | není mez nástroje, ale obsah dotazu; katalog LDBC ji u IC 13, IC 14, BI 15, BI 19 a BI 20 vyslovuje jako zjednodušení | 110, 113 |

### 2.11 Úniková cesta a nativní SQL

| Konstrukce | Proč | Co nástroj dělá | Kde |
|---|---|---|---|
| každý artefakt únikové cesty | je napsaný v SQL deklarovaného dialektu | vždy záznam `Fallback`, který jmenuje konstrukci a dialekt; platí jen pro SQL Server 2022 (VO 5); 3. stupeň nad ním je přijetí T-SQL, rozhoduje 4. | 086, 113 |
| kolekční parametr v nativním SQL EF Core a EclipseLinku | EF Core 10 interpolovanou kolekci naváže jako jednu hodnotu, nativní dotaz EclipseLinku 5.0 seznam nerozvine | `Failure` (`QueryParameter`), zápis; Hibernate ho rozvine | 113 |
| množinová operace nad dvěma různými entitami tam, kde API materializuje jednu (`FromSql`, `AddEntity`, `createNativeQuery` s třídou) | řádky jedné strany by se vrátily jako entita, kterou nejsou | `Failure`, zápis | 113 |
| sloupec výsledku bez jména nebo bez typu | EF Core plní třídu řádku podle jmen; NHibernate typ nevysloví | EF Core `Failure`; NHibernate ho čte bez `AddScalar` v typu ovladače s `Incompleteness` | 113 |
| odmítnutí, která nejsou o jazyku cíle — laterální odkaz, nepojmenovaný sloupec definice, parametr bez skaláru, agregát nad agregátem, pravidla rekurze, `EXCEPT ALL`, `OFFSET` v operandu | neunese je ani SQL Server, nebo je nenese model | `Failure` i s únikovou cestou | 113 |
| LINQ složený nad nativním SQL (`FromSql…`, `SqlQuery` s krokem nad sebou); `Database.ExecuteSql…` | EF Core takový dotaz spouští jako vlastní poddotaz; `ExecuteSql` je příkaz | `Failure` (`Filtering`), čtení | 070, 113 |
| zástupné symboly NHibernatu (`{alias}`, `{alias.*}`) v nativním SQL | rozřeší je jen NHibernate | `Failure`, čtení | 082, 113 |
| mapování výsledku nativního dotazu (`AddScalar`, `AddEntity`, `AddJoin`, `SetResultTransformer`, `<return>`, druhý argument `createNativeQuery`, `addScalar`, `addEntity` a příbuzné nativního dotazu Hibernate) | dotaz mapování výsledku nenese a cíl ho odvodí z tabulky | `Loss`, čtení | 067, 113 |

### 2.12 Obecné pravidlo: dotaz, který by vrátil jiné řádky, se nevydá

Pod všemi řádky 2.4 až 2.11 stojí jedno pravidlo (rozhodnutí 053, 065 a 070): **nepřečtená nebo nevyjádřená konstrukce, bez níž by dotaz vrátil jinou množinu řádků, odmítne artefakt**, a parser čte dál, aby jmenoval všechny důvody; konstrukce, bez níž jsou řádky tytéž a výstup jen chudší, je `Loss`. Hranicí je množina řádků, ne podmínka (065). Kroky LINQ, o kterých parser ví, že řádky mění — `OfType`, `SkipWhile`, `TakeWhile`, `DefaultIfEmpty` mimo korelovaný join, `GroupJoin`, `Zip`, koncové agregáty a `Any`, `All`, `Contains` i v asynchronní podobě a temporální kroky EF Core —, odmítá čtení jmenovitě; **krok, který parser nezná** (`Include`, `Cast` …), vypustí se záznamem `Loss`, protože odmítnout každé neznámé volání by odmítlo i `Include` (vyslovená mez 070). Úniková cesta se za překlad jazykem cíle nevydává nikdy: matice kategorií tvrdí záznam `Fallback` u každého směru, který ho má mít (`AFallbackDirectionFallsBackAsStated` v obou sadách). Drží to `Combined/QueryFaithfulnessTest`.

### 2.13 Tichá místa

Místa, kde nástroj fakt zahodí beze slova, ačkoli podle rozhodnutí 004 a 048 o něm mluvit má. Hranicí nejsou — za žádným nestojí rozhodnutí, které by ticho obhájilo —, a proto je vede [`open-items.md`](./open-items.md) jako práci:

- **Čtení dotazů** (kategorie *Dotazy*): žádné, o kterém víme; poslední známá, volby předání dotazu v kódu u JPA a Dapperu, mají odpověď v části 2.3.
- **Čtení mapování** (kategorie *Užitečné, ne nutné*): skalární forma `[ForeignKey("Navigace")]` na vlastnosti cizího klíče v EF Core; atributy na třídách a vlastnostech zdroje Dapper a NHibernate; pole C# třídy; JPA `@Column(table, insertable, updatable)`, `@Table(indexes, catalog)`, `<index>` v `orm.xml` a `referencedColumnName` v `@JoinColumn`; MyBatis `<cache>`, `<cache-ref>`, `<parameterMap>` a `typeHandler` na `<result>`; a zbylé atributy NHibernate mapování, které už mají položku vlastní.

---

## 3. Sporná čtení zadání

Dvě místa, kde katalog stojí na čtení slov zadání, které by šlo vést i jinak. Obě čtení argumentuje rozhodnutí; tady stojí, proč je volíme a co by znamenalo opačné.

**„Dynamicky parametrizované read-only dotazy" v F8 čteme jako parametr, ne jako dynamické SQL** (rozhodnutí 084). Parametr je hodnota, kterou dodá volající za běhu — `#{}` a kanonický `<foreach>` —, a ten se v MyBatisu čte i píše a má protějšek ve všech šesti frameworcích (rozhodnutí 083). Dynamický příkaz — `<select>` se značkou `<if>`, `<choose>` nebo `<bind>` — je naproti tomu **rodina příkazů**, jejíž členové vracejí různé množiny řádků: týž `<select>` dal v tutoriálu čtyři různé příkazy pro čtyři sady parametrů. Překlad jednoho člena by vydal dotaz s jinými řádky, než jaké zdroj napsal, což zakazuje rozhodnutí 053; nést rodinu v mezireprezentaci by znamenalo pojem, který pět ze šesti cílů vždycky zahodí, protože dynamické SQL v šestici protějšek nemá; a vydat artefakt za každého člena dává počet exponenciální v počtu značek, se jmény, která zdroj neřekl (rozhodnutí 028), a převod MyBatis → MyBatis by přestal být převodem (S2). Opačné čtení — „dynamicky" jako dynamické SQL, tedy to, kvůli čemu se MyBatis v praxi používá — je jazykově možné, a proto ho zapisujeme jako zúžení nároku F8 ([§9, *Co verze nárokuje*](./architecture.md#co-verze-nárokuje)) a potvrzení proti zadání i záměru žádá položka *Doložení, že úkoly 1–4 záměru jsou splněné* v [`open-items.md`](./open-items.md).

**„Společnou podmnožinu" ze specifikace čteme jako to, co zachytí mezireprezentace a co jde ověřit napříč ekosystémy, ne jako průnik toho, co vysloví všech šest dotazových jazyků.** Specifikace sama podmnožinu vymezuje dvěma podmínkami — „kterou dokáže zachytit mezireprezentace a pro kterou lze ověřit odpovídající chování" — a od rozhodnutí 113 nese slovník i konstrukce, které vyjádří jen část cílů: množinovou operaci, mezivýsledek, rekurzi, okenní funkci a agregaci do seznamu. Cíl, jehož jazyk je nevysloví, píše dotaz nativním SQL svého dialektu se záznamem `Fallback`, a takový směr se měří na 4. stupni proti témuž kanonickému výsledku jako překlad jazykem cíle, takže druhá podmínka platí i pro něj. Průnik by podmnožinu stáhl na to, co umí HQL 5.7 — bez množinových operací, mezivýsledků a výřezu v textu —, a katalog LDBC (rozhodnutí 110) by zůstal z větší části nepřeložený. Cenou našeho čtení je vazba artefaktu únikové cesty na SQL Server 2022, kterou záznam vyslovuje a která patří do vyňaté oblasti 5.

---

## Jak se katalog udržuje

Katalog je popis současného stavu a mění se se stavem: **změna chování, která posune hranici podmnožiny** — přidá konstrukci, vysloví novou mez, mez zruší nebo změní, co nástroj s konstrukcí dělá —, upraví katalog týmž krokem jako [`architecture.md`](./architecture.md) a řádek [`traceability.md`](./traceability.md). Zrušená mez z katalogu zmizí; kdy a proč, říká git historie a rozhodnutí, které ji zrušilo. Tichá místa z 2.13 mizí s položkou, která je odbaví. Tabulky 1.2 až 1.5 se při změně deskriptoru nebo manifestu kategorií opraví podle nich, ne naopak.
