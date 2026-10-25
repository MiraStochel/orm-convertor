# Společná podmnožina a její hranice

Odpověď na „přeloží nástroj tohle?", konstrukce po konstrukci: **část 1** vymezuje společnou podmnožinu, kterou žádá [`specifikace.tex`](./specifikace.tex), **část 2** případy bez úplného nebo jednoznačného překladu, které slibuje [`zamer.tex`](./zamer.tex), **část 3** dvě sporná čtení zadání.

Žánr jako [`architecture.md`](./architecture.md) (rozh. [007]): mechanismus je tam (odkazy „§"), důvody v rozhodnutích, nárok v [§9] a kanonicky v *Guarantees* kořenového [`README.md`](../README.md). Při rozporu katalogu s §9 platí §9, u tabulek 1.2–1.5 deskriptory (`*Descriptor.cs`) a manifest kategorií.

**Záznamy** ([§5.1]); **strana**: **č** čtení (parser zdroje), **b** brána (šablona builderu pro všechny cíle, [§7]), **z** zápis (krok či visitor cíle).

| Záznam | Význam |
|---|---|
| `Failure` | artefakt nevznikne, záznam jmenuje konstrukci; dotaz, který by vrátil jiné řádky, se nevydá ([053], [070]) |
| `Loss` | artefakt vznikne bez konstrukce; řádky dotazu tytéž |
| `Fallback` | cíl napíše celý dotaz v SQL dialektu přes API svých nativních dotazů ([113]); řádky tytéž, vazba na SQL Server 2022, měří se 4. stupněm |
| `Omitted` | druhý tvar dotazu (LINQ vedle HQL NHibernatu) nevznikne, závazný tvar stojí sám; řádky tytéž ([118]) |
| nečte se | čtečka konstrukci nepřevezme: v podmínce `Failure`, v projekci a klíči řazení `Loss`, vždy jmenovitě ([070]) |
| `Convention` · `Incompleteness` · `Conflict` | výstup tvrdí, co zdroj neřekl · chybí fakt, generování pokračuje · dva zdroje faktu se rozcházejí |

**Druh hranice** (rozh. [030]):

| Druh | Význam |
|---|---|
| **VO n** vyňatá oblast | oblast bez záruky ([§9, *Hranice záruk*][§9h]); překladu se týkají VO 2 a 5, VO 1 a 6 (Advisor, experimenty) ne |
| **Z Fx** zúžení nároku | požadavek nárokovaný v užším čtení ([§9, *Co verze nárokuje*][§9n]) |
| **VM** vyslovená mez | konstrukce uvnitř nároku, kterou nástroj z rozhodnutého důvodu (model fakt nenese, jazyk cíle tvar nemá, dotaz by vrátil jiné řádky) odmítne, vypustí nebo napíše nativním SQL a ozve se záznamem — nebo, kde to řádek říká, výstup odmítne až framework či databáze, či hodnotu změní nastavení konzumentského projektu ([040]) |

**Tichá místa** (2.13) hranicí nejsou: fakt zmizí beze slova, ač má zaznít ([004], [048]); jsou to otevřené položky.

---

## 1. Kladná půlka: co se překládá

### 1.1 Šest frameworků

| Framework | Verze | Ekosystém | Entita a mapování | Dotazový jazyk | Jazyky zdrojové jednotky | Nativní SQL v cíli |
|---|---|---|---|---|---|---|
| Dapper | 2.1.79 | .NET | C# třída bez atributů | SQL dialektu (`SqlMapper`, holý skript) | `CSharp`, `SqlQuery` | — (jazykem je SQL) |
| EF Core | 10.0.10 | .NET | C# třída s datovými anotacemi; zdroj i fluent konfigurace (`OnModelCreating`, `IEntityTypeConfiguration<T>`) jako podmnožina ([132]) | LINQ (`IQueryable`) | `CSharp` | `DatabaseFacade.SqlQuery`, `DbSet.FromSql` |
| NHibernate | 5.7.0 | .NET | C# třída s `virtual` a `hbm.xml` | HQL 5.7 závazně; LINQ nad `session.Query<T>()` jako zdroj i jako druhý tvar cíle ([118]) | `CSharp`, `XML`, `HqlQuery` | `ISession.CreateSQLQuery` |
| Hibernate | 7.4.5.Final | Java | třída s anotacemi `jakarta.persistence`; zdroj i `orm.xml` | JPQL a podmnožina HQL 7.4 | `Java`, `XML`, `JpqlQuery` | `EntityManager.createNativeQuery` |
| EclipseLink | 5.0.0 | Java | totéž s profilem EclipseLinku | JPQL | `Java`, `XML`, `JpqlQuery` | `EntityManager.createNativeQuery` |
| MyBatis | 3.5.19 | Java | POJO a XML mapper s `<resultMap>`; zdroj i anotace mapperu | SQL dialektu (`<select>`, `@Select`) | `Java`, `XML` | — (jazykem je SQL) |

Každý je zdrojem i cílem: 36 směrů, 18 napříč ekosystémy (`Combined/QueryMatrixTest`, `Combined/CrossEcosystemMatrixTest`). Verze kanonicky v [§1, *Zafixované verze*][§1v], strojově v deskriptoru. Dialekt všech šesti: SQL Server 2022, deklarovaný deskriptorem ([086]). Jednotka je jeden zdrojový soubor, deklaruje jen jazyk, třídy v ní rozdělí wrapper zdroje ([111]); dokument, který je mapováním i dotazem, čtou oba průchody ([081]).

### 1.2 Slovník dotazu

Dotaz ([§4.4]): instrukce (zdroj řádků, projekce, filtr, join, seskupení, `HAVING`, řazení, stránkování, `DISTINCT`, poddotaz, množinová operace) a pojmenované mezivýsledky (`WITH`) s limitem rekurze. Podmínka je strom `AND`/`OR`/`NOT` nad porovnáním. **Operand:** sloupec (i pod agregací s jejím `DISTINCT`), konstanta, poddotaz, výčet hodnot, parametr, výraz. **Výraz:** binární operátor (konkatenace, aritmetika), funkce slovníku, `CASE`, okenní funkce řazení, agregace do seznamu.

Jak cíl konstrukci píše (*jazyk* = svým dotazovým jazykem, *SQL* = `Fallback`); řádky odpovídají `QueryFeature`:

| Konstrukce | Dapper | MyBatis | EF Core | NHibernate | Hibernate | EclipseLink |
|---|---|---|---|---|---|---|
| projekce, filtr, join, agregace, seskupení, `HAVING`, řazení, stránkování, poddotaz, parametr, výraz | jazyk | jazyk | jazyk | jazyk | jazyk | jazyk |
| agregát přes celý výsledek bez seskupení | jazyk | jazyk | SQL | jazyk | jazyk | jazyk |
| druh joinu (vnitřní, levý, pravý, plný) | jazyk | jazyk | jazyk; plný složením `LeftJoin`, `Concat`, `RightJoin` ([065]); pravý a plný s podmínkou nad rámec rovností SQL | jazyk; plný SQL | jazyk | jazyk; plný SQL ([180]); vnější s podmínkou na alias vnitřního joinu SQL |
| join uvnitř poddotazu | jazyk | jazyk | jazyk | jazyk | jazyk | jazyk; vnitřní jako proměnná rozsahu s podmínkou ve `where`, vnější SQL |
| množinová operace | jazyk | jazyk | jazyk | SQL | jazyk | jazyk; v poddotazu SQL |
| mezivýsledek (`WITH`, odvozená tabulka) | jazyk | jazyk | jazyk (proměnná metody) | SQL | jazyk | SQL |
| rekurze | jazyk | jazyk | SQL | SQL | jazyk; limit hintem na objektu dotazu ([139]) | SQL |
| seskupení podle výrazu | jazyk | jazyk | jazyk | jazyk | jazyk | jazyk; klíč s literálem s hintem `eclipselink.jdbc.bind-parameters` = `false` na objektu dotazu |
| okenní funkce (pořadová i agregát) | jazyk | jazyk | SQL | SQL | jazyk | SQL |
| agregace do seznamu | jazyk | jazyk | jazyk nad prvky skupiny i nad korelovaným poddotazem (řetěz seskupený konstantou), nad hodnotou textu bez `NULL`, jinak SQL | SQL | jazyk | SQL |
| funkce slovníku | celý slovník | celý slovník | celý slovník | bez `DateAdd`, `DateDiff` | celý slovník | bez `DateAdd`, `DateDiff`, `Cast` |

**Funkce** (`QueryFunction`): `Upper`, `Lower`, `Trim`, `TrimEnd` (`RTRIM`, JPQL a HQL `trim(trailing from x)`, LINQ `TrimEnd()`; [169]), `Substring`, `Length`, `Coalesce`, `Abs`, `Year`, `Month`, `Day`, `CurrentTimestamp`, `EscapePattern` ([107]); `DateAdd`, `DateDiff` (jednotky rok … sekunda), `Round`, `Sqrt`, `Cast` do `Int`, `Long`, `Float`, `Double`, `String` ([113]). Funkci mimo deskriptor cíle píše cíl SQL. **Okenní funkce:** `ROW_NUMBER`, `RANK`, `DENSE_RANK` nad oknem s povinným řazením ([113]) a `COUNT`, `SUM`, `MIN`, `MAX`, `AVG` nad oknem s řazením volitelným ([161]), jen v projekci. **Poddotaz** je operand ve `WHERE`, `HAVING` i v podmínce joinu ([061], [162]).

Strojově: deskriptory (`QuerySupport`, `Functions`, `NativeSqlApi`), `Combined/TargetFrameworkDescriptorTest`. Jemnější případy, kdy cíl jinak psaný svým jazykem napíše dotaz nativním SQL — výřez v poddotazu do HQL a JPQL, skalární poddotaz EF Core, který není jediným agregátem, převod textu na text do NHibernatu a Hibernatu —, vede část 2. Dapper a MyBatis nemají kam ustoupit (jazykem je SQL): odmítnou, co brány odmítají všem cílům, a co T-SQL nezapíše (`EXCEPT ALL`, výřez v operandu množinové operace; 2.7).

### 1.3 Kategorie, na kterých je podmnožina změřená

Kategorie T2, každá dotazem nad sdílenou doménou sedmi entit v každém zdrojovém jazyce, který ji vysloví; jejich výčet je manifest `Tests/Database/QueryShapes/categories.txt`, který čtou obě sady ([§6.2]). Každá jde každým směrem: 1. stupeň `Combined/QueryShapeMatrixTest`, 2. nad SQL a .NET cíli, 2. a 3. nad javovými (`shapes/QueryCategoryTest`), 4. diferenčně proti kanonickému výsledku (`Tests/Database/Differential/matrix.txt`, [089]). Cíle mimo sloupec *Fallback* píšou kategorii jazykem; odmítnutý směr (`refusedBy`) není žádný.

| Kategorie | Zdroje, které ji vysloví | `Fallback` (`fallbackBy`) |
|---|---|---|
| `Projection`, `Filtering`, `JoinOverTwoColumns`, `AggregationGroupingAndHaving`, `Ordering`, `SubqueryAsTheRightSideOfIn`, `CorrelatedExistsOverThreeColumns`, `SubqueryWithAJoinAsTheRightSideOfIn`, `CorrelatedExistsWithAJoin`, `NestedInUnderADisjunctionAsTheRightSideOfIn` (okruh osob IC 5, [121]), `ScalarSubquery`, `DistinctProjection`, `InOverAListOfValues`, `ConstantOfAMoment`, `LikeWithAnAnchoredPattern`, `CountOverDistinctValues`, `LikeWithAnEscapedWildcard`, `LikeWithABoundPrefix`, `ArithmeticInAProjection`, `FunctionInAFilter`, `CoalesceInAFilter`, `CaseInAProjection`, `CaseOverExistsInAProjection` (příznak IS 7, [122]), `OrderingByAnAggregate`, `ScalarSubqueryAgainstABoundValue`, `GroupingByAnExpression`, `RoundingAndSquareRoot`, `OuterJoinWithAFilterInOn`, `JoinBeyondEqualities`, `SubqueryInAJoinCondition`, `QuantifiedComparisonOverAll`, `QuantifiedComparisonOverAny`, `AverageInAProjection` ([161]) | všech šest | — |
| `ScalarParameter`, `CollectionParameter`, `InListWithABoundValue` | všech šest, Dapper jen s katalogem¹ | — |
| `PaginationWithBoundCounts`, `OrderingByAnUnprojectedKeyUnderASlice` | bez NHibernatu a EclipseLinku | — |
| `SetOperation` | bez NHibernatu | NHibernate |
| `InOverASetOperation` | bez NHibernatu | NHibernate, EclipseLink (parser 5.0.0 `union` v poddotazu odmítne) |
| `GroupingOverAGroupedResult`, `IntermediateResultReadTwice`, `DateArithmetic` | bez NHibernatu a EclipseLinku | NHibernate, EclipseLink |
| `CollectionParameterOverAnIntermediateResult` | bez NHibernatu a EclipseLinku, Dapper jen s katalogem¹ | NHibernate, EclipseLink (kolekci rozvine metoda, [123]) |
| `ListAggregation` | bez NHibernatu a EclipseLinku | NHibernate, EclipseLink; ze zdroje MyBatis bez katalogu i EF Core² |
| `ListAggregationOverACorrelatedSubquery` | bez NHibernatu a EclipseLinku | NHibernate, EclipseLink; ze zdroje MyBatis bez katalogu i EF Core² |
| `AggregateOverTheWholeResult` | bez EF Core | EF Core |
| `CastInAConcatenation`, `AverageInFloatingPoint` ([161]) | bez EclipseLinku | EclipseLink |
| `ConcatenationOverANullableColumn` | všech šest | — |
| `NativeSqlInCode` | EF Core, NHibernate, Hibernate, EclipseLink | — |
| `RecursiveDescentOfAHierarchy`, `RecursiveWalkOfACyclicGraph`, `BestRowPerGroup`, `WindowAggregate` | Dapper, Hibernate, MyBatis | EF Core, NHibernate, EclipseLink |

¹ `refusedWithoutCatalog`, jediné odmítnutí manifestu: bez katalogu nemá parametr zdroje Dapper skalár (2.4); s katalogem, který tabulku má, se typuje do všech cílů ([105], [106]).
² `fallbackWithoutCatalog`: zdroj MyBatis neříká, že sloupec nesmí mít `NULL`.

**Mimo kategorie.** *Záměrně špatný dotaz* (osm poddotazů do čtyř úrovní, joiny přes dva a tři sloupce, seskupení s `HAVING`, řazení, výřez) jde ze všech šesti zdrojů do všech šesti cílů (`Combined/DeeplyNestedQueryTest`, javově `shapes/DeeplyNestedQueryTest`). *Katalog LDBC* — 41 čtecích dotazů LDBC SNB ([110]) — měří úplnost: přeloženo všech 41 podle specifikace, pět dotazů na cesty s hloubkou změřenou nad SF 1 ([140], [141]); žádný cíl žádný neodmítá. Čtyři texty mají tvar, který unese implementace EclipseLinku (vnější join nejmenuje alias vnitřního), v mezích [122]. `SampleData/LdbcSnbSample`, koncový bod `/ldbc`, `Combined/LdbcCatalogTest` (1. stupeň, artefakty EF Core a NHibernate i 2.), javová `ldbc/LdbcBusinessIntelligenceAcceptanceTest` (BI do javových cílů, 2. a 3. stupeň). Stav „podle specifikace" drží u dotazů Interactive (IS 1–7, IC 1–14) validační sada LDBC na 4. stupni ([117], `LdbcJudge/LdbcValidationTest`, javové `ldbc/`) — nad dnešními texty IC 13 a IC 14 celou sadou SF 1 zatím jen prefix ([`open-items.md`](./open-items.md)); u dotazů BI je tvrzením autora — validační sadu pro BI nad daty Interactive LDBC nevydává —, tři BI na cesty navíc drží srovnání s algoritmem (`LdbcJudge/LdbcPathQueryTest`). *Katalog FIBEN* — 237 dotazů benchmarku FIBEN převedených z Db2 do T-SQL ([149]) — měří totéž do hloubky zanoření (170 vnořených ve čtyřech Kimových typech), ne při sestavení, ale během na vyžádání (`FibenJudge/FibenRunTest`, javové `fiben/FibenRunTest`; záznamy v [`runs/`](./runs/README.md#popsané-běhy)); dotazy s okenním agregátem uvnitř odvozené tabulky (2.9) a se skalárním poddotazem v podmínce `ON` vnějšího joinu (2.6) jsou `NotTranslated`, ostatní stojí jako texty benchmarku — výřez, který by své řádky neurčoval, převod doplnil o rozhodující klíč ([170]) — a čtvrtý stupeň nad nimi soudí regresní soudce z prvního doloženého běhu, ne nezávislá sada.

### 1.4 Proč u kategorie chybí zdroj

Chybějící zdroj v 1.3 je mez jazyka zdroje, ne nástroje:

| Zdroj | Co jeho jazyk nevysloví |
|---|---|
| HQL 5.7 (NHibernate) | výřez v textu (je na objektu dotazu, `SetFirstResult`, `SetMaxResults`, čte se z kódu), množinovou operaci, `WITH`, odvozenou tabulku, rekurzi, datumovou aritmetiku, okenní funkci, agregaci do seznamu; LINQ NHibernatu mezivýsledek nečte, provider ho do HQL nepřeloží |
| JPQL (EclipseLink) | totéž kromě množinové operace, kterou vysloví; výřez je na objektu dotazu, poddotaz ve `FROM` je kartézský součin za čárkou, který model nenese, `cast` předá EclipseLink 5.0 SQL Serveru jménem javového typu |
| LINQ (EF Core) | agregát přes celý výsledek (jen voláním, které dotaz vykoná), rekurzi, okenní funkci |
| SQL (Dapper, MyBatis) | nativní SQL v kódu — jejich jazykem už je |

### 1.5 Slovník mapování

Entita ([§4.1]): třída s vlastnostmi a jejich jazykovými fakty (typ, nullabilita, modifikátory, inicializátor), databázový typ jako rodina s facetami, primární klíč o 1…*n* částech se strategií generování a klíčovou třídou ([§4.2]), vztahy na entitě se jménem cíle, rolí a páry sloupců, N:M jako spojovací entita ([§4.3]). Jak cíl fakt vyjádří (`MappingFactCategory`, `FactSupport`); prázdná buňka = nevyjádří, a nese-li fakt model, `Loss`; **vyžaduje** = bez faktu `Failure`:

| Fakt | Dapper | MyBatis | EF Core | NHibernate | Hibernate, EclipseLink |
|---|---|---|---|---|---|
| tabulka | | | `[Table]` | `<class table>` | `@Table(name)` |
| schéma | | | `[Table(Schema)]` | `schema` | `@Table(schema)` |
| sloupec | alias ve volání `Query<T>` ([131]) | `<result column>`; alias ve výběru s `resultType` ([181]) | `[Column]` | `column` | `@Column(name)` |
| databázový typ | | `jdbcType` | `[Column(TypeName)]` | `type`, `sql-type` | `columnDefinition`; `@Nationalized` (Hibernate) |
| délka | | | `[MaxLength]` | `length` | `@Column(length)` |
| přesnost a měřítko | | | `[Precision]` | `precision`, `scale` | `precision`, `scale`, `secondPrecision` |
| nullabilita | | | `[Required]` | `not-null` | `@Column(nullable)` |
| primární klíč | | `<id>` | `[Key]`, `[PrimaryKey]` | **vyžaduje**: `<id>`, `<composite-id>` | **vyžaduje**: `@Id`, `@IdClass` |
| strategie klíče | | | `[DatabaseGenerated]` | `<generator>` | `@GeneratedValue` |
| sloupce cizího klíče | | | `[ForeignKey]` | `column` vztahu | `@JoinColumn` |
| sloupec verze | | | `[Timestamp]`; číselná a datočasová `[ConcurrencyCheck]` — jako zdroj jen jediný token třídy nad `short`/`int`/`long`/`DateTime` ([116]) | `<version>` | `@Version`; EclipseLink ne nad binárním typem ([179]) |
| unikátní omezení, i nad cizím klíčem ([130]) | | | `[Index(IsUnique = true)]`, nad FK přes jeho vlastnosti | `unique`, `unique-key`; nad FK `unique-key` na `<many-to-one>` | `@UniqueConstraint`, `unique` na `@Column` i `@JoinColumn` |
| transientní vlastnost | | vynechání (`autoMapping="false"`) | `[NotMapped]` | vynechání | `@Transient` |

Co zdroj Dapper či MyBatis nevysloví, doplní katalog ([§5.2], [015]) nebo konvence cíle s `Convention`. **Vynucené členy** (`EnforcedMembers`, [009]): NHibernate `virtual`, nezapečetěná třída bez deklarovaného konstruktoru, kolekce přes `IList`, `ISet` ([035]), u složeného klíče `[Serializable]`, `Equals`, `GetHashCode`; JPA nefinální třída a pole, bezparametrický konstruktor, u složeného klíče vnořená `@IdClass` se `Serializable`, `equals`, `hashCode`; MyBatis třída bez konstruktoru; EF Core `[Keyless]` u entity bez klíče; Dapper nic. Vlastnosti bez setteru dá builder nejužší setter, přes který jeho framework zapisuje — NHibernate `protected set` mapované vlastnosti, EF Core `private set` persistované vlastnosti mimo kolekční navigaci ([134]); je to pravidlo builderu, ne vynucený člen deskriptoru.

### 1.6 Po frameworcích

Co je frameworku vlastní ([§5]); odmítnutí, ztráty a nativní SQL jsou v části 2.

| Framework | Jako zdroj čte | Jako cíl vydá |
|---|---|---|
| **Dapper** | třídu jen jazykovými fakty; sloupec vlastnosti z aliasu ve volání, které mapuje řádky na entitu (`Query<T>`, [131]); klíč, tabulku, ostatní sloupce a vztahy doplní katalog (F6); Dapper.Contrib ne ([133]); dotaz z holé jednotky SQL, kde je dotazem každý `SELECT` ([108]), a z každého volání `SqlMapper` (`Query…`, `QueryMultiple` jako skript, `Execute…`; [109]), sdílenou čtečkou T-SQL ([082]); `IN @ids` = kolekční parametr, `IN (@ids)` = jedna hodnota ([106]) | třídu bez atributů, klíče a vztahů s mechanickým `Loss` za každý fakt; metodu nad `connection.Query<T>` a holé SQL, projekci jako `List<dynamic>` ([104]) |
| **EF Core** | anotace `[Table]`, `[Key]`, `[PrimaryKey]`, `[Keyless]`, `[Column]`, `[MaxLength]`, `[StringLength]`, `[Unicode]`, `[Precision]`, `[Required]`, `[DatabaseGenerated]`, `[Timestamp]`, `[ConcurrencyCheck]` ([116]), `[NotMapped]`, `[Index]`, `[ForeignKey]`, `[InverseProperty]` a konvence jako tvrzení zdroje (klíč `Id`, navigace s dohledaným cizím klíčem, N:M, tabulka podle `DbSet` vlastnosti; [067]); před nimi fluent konfiguraci v `OnModelCreating` a `IEntityTypeConfiguration<T>` jako podmnožinu: řetěz nad `Entity<T>()` s literály a lambdami členů — `ToTable`, `HasKey`, `HasNoKey`, `HasAlternateKey`, unikátní `HasIndex`, `Ignore`, u `Property` sloupec, délka, přesnost, typ, `IsRequired`, `IsUnicode`, `IsRowVersion`, `IsConcurrencyToken`, `ValueGenerated…`, vztahy `HasOne`/`HasMany` s `With…` a `HasForeignKey` (1:1 přes `HasForeignKey<T>`), `HasDefaultSchema` ([132]); `DbContext`, `DbSet<T>`, `IEntityTypeConfiguration<T>` entitou nejsou; vlastnost `DbSet<T>` v každém zápisu (`{ get; set; }`, `=> Set<T>()`, getter, líné pole, přiřazení v konstruktoru) jako deklarace sady, ne dotaz ([158]). Řetěz LINQ nad kořenem ([026], [103]), o kořeni rozhoduje, co jednotka o jménu uvádí ([114]); jméno deklarované `DbSet<T>` (parametr, lokální proměnná, pole) jako sadu své entity, tedy jako `Set<T>()` ([114], [158]); člen jména jménem sady, kterou kontext převodu deklaruje `DbSet<T>` (`ctx.PageDirectories` nad `DbSet<PageDirectory>`), jako sadu její entity, i jako vnitřní posloupnost joinu ([185]); volání, které nad sadou jen sleduje entitu (`Add`, `AddRange`, `Attach`, `Update`, `Remove`, `Entry`), dotazem není; explicitní načtení navigace jako dotaz nad cílovou entitou ([115]); kompilovaný dotaz `EF.CompileQuery`, `EF.CompileAsyncQuery` jako řetěz nad kontextem z prvního parametru lambdy ([109]); `Find` a `FindAsync` jako filtr klíče z mapování s výřezem jednoho řádku ([103]); poddotaz nad kolekční navigací řádku (`c.Orders.Any(…)`, `All`, `Count`, agregát, `Select(…).Contains`) korelovaný vztahem jako join po cestě ([101]); nativní SQL v `SqlQuery`, `SqlQueryRaw`, `FromSql…` jako celý dotaz ([113]); predikát a `Sum` tak, jak je přeloží EF Core 10 — `==`, `!=`, negaci a `Contains` s kompenzací `NULL`, `Sum` jako `COALESCE(SUM(x), 0)` ([155], [156]) | třídu s datovými anotacemi (fluent API ne); metodu vracející `IQueryable` nad `ctx.Set<T>()`, mezivýsledek jako proměnnou metody, join nad rámec rovností klíčů filtrem spojované posloupnosti či korelovaným `SelectMany`; `SUM` se sémantikou SQL — ve skalárním poddotazu projekcí nad jedinou skupinou, nad hodnotou, která smí být `NULL`, pod stráží počtu hodnot, `COALESCE(SUM(x), 0)` holým `Sum` ([155]); podmínku tak, aby platila, kde platí v SQL — pod `NOT` obráceně, s testy na `NULL` ([156]); nativně `ctx.Database.SqlQuery<Row>` s třídou řádku, `FromSql` u celé entity |
| **NHibernate** | C# třídu s odloženým `virtual`, po ní `hbm.xml` ([017], [068]) v ploché třídě: `<class>`, `<id>` s `<generator>`, `<composite-id>` s `<key-property>` a `<key-many-to-one>`, `<property>`, `<version>`, `<timestamp>`, `<many-to-one>`, `<one-to-one>`, `<set>`, `<bag>`, `<list>`. Holé HQL (vlastní parser, [062]), `<query>` a `<sql-query>` s typem z `<query-param>`, kód (LINQ nad `session.Query<T>()`, `CreateSQLQuery`, `CreateQuery`, výřez `SetFirstResult`, `SetMaxResults`) | třídu s `virtual`, kolekce jako `IList`, `ISet` ([035]), `hbm.xml` bez `assembly` ([028]), generátor podle [021]; metodu nad `session.CreateQuery` s výřezem na objektu dotazu a holé HQL; vedle nich druhý tvar téhož dotazu jako metodu vracející `IQueryable` nad `session.Query<T>()` (`CSharpLinqQuery`, [118]) — levý join `GroupJoin` s `DefaultIfEmpty()`, řazení pod `DISTINCT` před projekcí, `COUNT` nad sloupcem s `NULL` jako `Sum` nad podmínkou, `LIKE` s escapovaným jádrem přes `Like` z `NHibernate.Linq`, podmínku se sémantikou SQL jako cíl EF Core ([156]); nativně `session.CreateSQLQuery` s `AddEntity` či `AddScalar` |
| **Hibernate** ([077]) | sdílenou vrstvu JPA: anotace a `orm.xml` (nad anotacemi, `metadata-complete` je vypne), přístup podle `@Access` či místa `@Id`, `@IdClass`, `@EmbeddedId`, `@Id` na vztahu (odvozená identita, část 2), generátory s kanonickými parametry ([020]), vztahy s `@JoinColumn` i bez (výchozí `<atribut>_<sloupec klíče>` nad jednodílným klíčem), `@MapsId`, `@JoinTable`; navíc `@Nationalized`. JPQL vlastním parserem včetně joinu po asociační cestě ([101]), výřezu a limitu rekurze (`addQueryHint`, [139]) na objektu dotazu; z HQL 7.4 `limit`, `offset`, `with` i rekurzivní, poddotaz ve `from`, okenní funkce, `listagg`, `timestampadd`, `timestampdiff`; nativní SQL v `createNativeQuery`; pojmenované dotazy `@NamedQuery`/`@NamedNativeQuery` na třídě a `<named-query>`/`<named-native-query>` v `orm.xml` ([143]) | anotovanou javovou třídu (`orm.xml` ne), `AUTO` jako sekvenci `<Entita>_SEQ`; metodu nad `em.createQuery` (`TypedQuery<E>` či `Query`) a holé JPQL |
| **EclipseLink** ([080]) | tutéž vrstvu bez rozšíření: `@Nationalized` nezná, unicode jen z doslovného `columnDefinition`; JPQL bez `limit`, `with`, oken, `listagg`, datumové aritmetiky | jako Hibernate s profilem EclipseLinku: každý název explicitně (implicitní píše velkými písmeny), `AUTO` jako tabulku čítače `SEQUENCE`, nationalizaci doslovným `columnDefinition`, žádný `cast`; hodnotu pevné šířky v projekci jako `concat(x, '')` a v entitě s vnořeným konvertorem doplnění ([169], 2.2) |
| **MyBatis** ([084]) | doménovou třídu; rozhraní mapperu (`@Results`, `@Result`, `@One`, `@Many`, `@Select`, `@Param`); XML mapper (`<resultMap>` s `extends`, `<id>`, `<result>`, `<association>`, `<collection>`; `<select>` po rozvinutí `<include>`, `<where>`, `<set>`, `<trim>`; kanonický `<foreach>` = kolekční parametr, `#{}` = parametr); sloupec vlastnosti z aliasu ve výběru, jehož `resultType` (nebo návratový typ metody s `@Select` bez `@Results`) jmenuje entitu převodu ([181]); text sdílenou čtečkou T-SQL; skalár parametru z podpisu metody, `parameterType` či `javaType`; MyBatis-Plus ne ([133]) | POJO bez importu z frameworku a bez konstruktoru, mapper s `<resultMap autoMapping="false">`; hlavičku metody s `@Param` a `<select>`; kolekci jako kanonický `<foreach>` (jediná vydávaná dynamická značka) |

---

## 2. Záporná půlka: případy bez úplného nebo jednoznačného překladu

Řádek: *konstrukce → proč → co nástroj udělá (záznam, strana) → druh hranice → rozhodnutí*.

### 2.1 Vyňaté oblasti

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| dědičnost, komponenty, spojené tabulky: NHibernate `<subclass>`, `<joined-subclass>`, `<union-subclass>`, `<component>`, `<dynamic-component>`, `<join>`, `<natural-id>`, `<idbag>`, `<array>`, `<primitive-array>`, `<any>`, kolekce hodnot či komponent; bázový typ C# třídy, který jmenuje jinou entitu převodu (EF Core = table per hierarchy), i třídu mimo převod (první položka bázového seznamu C#, `extends` v Javě: co by entita zdědila, převod nevidí); JPA `@Inheritance`, `extends` mezi dvěma `@Entity`, `@MappedSuperclass`, `@Embedded`, `@ElementCollection`, `@SecondaryTable` a `@Column(table)`; EF Core `[Owned]`, `[ComplexType]`, primitivní kolekce (`List<string>`: prvek skalár či klíčové slovo C#, jeden sloupec JSON) | model nese jen plochou třídu | `Loss` u každého prvku (č); vlastnost, kterou prvek mapuje (komponenta, kolekce hodnot či komponent, `<idbag>`, `<array>`, `<primitive-array>`, `<any>`, vlastnost pod `<join>`, `@Embedded`, `@ElementCollection`, `@Column(table)`, primitivní kolekce), z modelu vypadne s touž jedinou ztrátou; hodnotový typ (`[Owned]`, `[ComplexType]`, `@Embeddable` mimo klíč, třída komponenty bez vlastního mapování) není entita a vypadne i s vlastnostmi jím typovanými, třída i každá vlastnost se ztrátou; hierarchie = nesouvisející entity; `@MappedSuperclass` s vlastním záznamem = samostatná entita, jejíž atributy dědící entity nedostanou | VO 2 | [030], [048], [177] |
| dialekt: jediný je SQL Server 2022 z deskriptoru; zdroj s jiným deklarovaným dialektem; katalog u něj; `CHECK` a výchozí hodnota sloupce jako doslovný SQL výraz | jeden dialekt vymezuje předmět práce a drží měření jednoznačná; druhý se nepřidává | cizí dialekt zastaví čtení doslovného SQL (`Failure`) i doslovného typu sloupce (`Loss`, `DatabaseType`); katalog fakta dodá s jediným `Conflict`; `CHECK`, `default` `Loss`; artefakt únikové cesty vázaný na dialekt | VO 5 | [086], [088], [091], [055], [129] |

### 2.2 Mapování

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| líné načtení reference v EclipseLinku (`fetch = LAZY` na `@ManyToOne`, `@OneToOne`) | bez weavingu tiše eager; model strategii nenese | `Loss` (č, zdroj EclipseLink); artefakt `fetch` nevyslovuje | Z F9 | [076], [080] |
| načítání a kaskády (`fetch`, `cascade`, `orphanRemoval`, NHibernate `inverse` a `cascade` kolekce) | do mapovací IR nepatří (článek, §5.4) | `Loss` (č), `fetch` JPA mlčky; NHibernate píše `inverse` jen odvozené, `cascade` nikdy | VM | [014], [035], [077] |
| pořadí a tvar kolekce: `@OrderColumn`, indexový sloupec `<list>`, `<map>` | kolekce bez indexu; mapy mimo typový model | `Loss` (č); NHibernate píše jen `<set>`, `<bag>` | VM | [014], [035] |
| klíčová třída jako vnořený klíč (`@EmbeddedId`, `<composite-id class>`) | cíle píšou klíč ploše | jméno třídy se nese, změna formy `Loss`; člen, který mapování nejmenuje, `Loss`; třída s vlastním mapováním `Conflict` | VM | [006], [031] |
| `<key-many-to-one>` | klíč plochý, vztah vedle | plochý klíč a vlastnící N:1, změna formy `Loss`; bez sloupců `Incompleteness` | VM | [006] |
| JPA `@Id` na `@ManyToOne` / `@OneToOne` (odvozená identita; `orm.xml` `id="true"`) | klíč plochý, vztah vedle | části klíče ze sloupců `@JoinColumn` pojmenované sloupcem (`m.USER_ID`), `Assigned`, vztah nad nimi jen pro čtení, změna formy `Loss`; bez `@JoinColumn` `Incompleteness` a klíč kratší; na kolekci `Loss` | VM | [006], [077] |
| strategie generování, kterou cíl nevyjádří: identita, sekvence, hi/lo, UUID, increment v anotacích EF Core; strategie části `<composite-id>` jiná než přiřazená či neuvedená; `Increment` v JPA; generátor či parametr bez protějšku | anotace EF Core řeknou jen „generuje databáze" nebo „nic"; jinde protějšek chybí | `Loss` (z); rozklad `AUTO` podle profilu a dosazené `assigned` jsou `Convention` | VM | [011], [020], [021], [064], [077], [080] |
| nullabilita části klíče (`int? Id`) | klíč nesmí být `NULL` | EF Core, NHibernate typ zploští s `Loss` (`Nullability`); JPA píše obalový typ vždy | VM | [054] |
| nullable sloupec za nenullovatelnou vlastností, cíl EF Core | `IsRequired(false)` je jen ve fluent API | `Loss` (z) | VM | [§5] |
| entita bez primárního klíče | NHibernate a JPA klíč vyžadují | `Failure` do NHibernatu, Hibernatu, EclipseLinku; EF Core `[Keyless]` a navigace, kterou EF Core bez klíče hlavní strany nezmapuje (na entitu bez klíče; kolekce či inverzní reference entity bez klíče), `[NotMapped]` s vlastnostmi cizího klíče jako sloupci + `Incompleteness`, nad vztahem `Loss` (z); Dapper, MyBatis prostá třída | VM | [063], [177] |
| neunikátní index (`index`, `[Index]` bez `IsUnique`, JPA `@Table(indexes)`, `<index>`) | výkonnostní artefakt | `Loss` (č) | VM | [055] |
| unikátní omezení nad vlastností, která v cíli není sloupcem (část klíče, verze v NHibernatu; transientní vlastnost) | `<id>` ani `<version>` atribut omezení nemají; transientní nemá sloupec | `Loss` (z); nad nedeklarovanou vlastností `Incompleteness`; bez jména tam, kde NHibernate potřebuje `unique-key`, dostane jméno s `Convention` | VM | [130], [072] |
| unikátní omezení nad částí vícesloupcového cizího klíče; nad sloupcem jiné tabulky (`@JoinTable`, jednosměrné `@OneToMany` s `@JoinColumn`); JPA `@UniqueConstraint` nad spojovacím sloupcem, jehož jméno zdroj nevyslovil | navigace zastupuje celý FK a sloupec jiné tabulky patří jiné entitě; výchozí jméno spojovacího sloupce vzniká až z klíče cíle | `Incompleteness` (č, katalog); cíl, který sloupce navigace nezná (EF Core bez párů, JPA bez párů i klíče cíle), také `Incompleteness` (z) | VM | [130], [012] |
| druhý sloupec verze; verze na části klíče, navigaci či transientní vlastnosti (NHibernate) | `<version>` je jeden, nad obyčejnou vlastností | `Loss` (z) | VM | [030] |
| verze, kterou zvyšuje framework (JPA `@Version int`, NHibernate `<version>` nad `Int32`, `<timestamp>`), cíl EF Core | anotace inkrementaci nevyjádří; `[Timestamp]` = hodnota z databáze | `[ConcurrencyCheck]` a `Loss` (z), zvyšuje aplikace; týmž tvarem EF Core `[Timestamp]` nad `ulong` (typ mimo slovník) | VM | [004], [030], [075] |
| verze, kterou udržuje aplikace (EF Core `[ConcurrencyCheck]`), cíl NHibernate, Hibernate, EclipseLink | `<version>` a `@Version` zvyšuje framework sám | `<version>`/`@Version` a `Convention` (z): inkrementace přechází na framework; do EF Core doslovně beze záznamu | VM | [116] |
| `[ConcurrencyCheck]` mimo podmínky: druhý token třídy, vedle `[Timestamp]` na jiné vlastnosti, nad `Guid`, řetězcem, `decimal`, `byte[]` … | token chrání sloupec a verzí není; cíle vedou verzi jen nad celočíselným či datočasovým typem | `Loss` kategorie `VersionColumn` s jmenovanou podmínkou (č) | VM | [116] |
| verze nad binárním sloupcem, kterou vyrábí databáze (EF Core `[Timestamp] byte[]`, NHibernate `<version generated="always">` nad `BinaryBlob`, `rowversion` z katalogu), cíl EclipseLink | EclipseLink 5.0.0 nese `@Version` jen nad `int`, `short`, `long`, jejich obaly a `Timestamp` (`EclipseLink-7168`, změřeno); Hibernate 7.4.5 `@Version byte[]` přijme | `@Column(insertable = false, updatable = false)` bez `@Version` a `Loss` `VersionColumn` (z); do Hibernatu `@Version` beze záznamu | VM | [179] |
| druhý a další `<column>` vlastnosti | chtělo by kompozitní uživatelský typ | `Loss` u každého (č) | VM | [030] |
| hodnota `property-ref`; JPA `referencedColumnName`, které jmenuje jiný sloupec než klíč odkazované entity | model páruje cizí klíč s klíčem | `Loss` (č); `referencedColumnName` se posoudí až nad celou sadou entit, shoda s klíčem mlčí, cíl mimo převod se neposoudí; inverzní 1:1 bez vlastnické protistrany `Incompleteness` | VM | [012] |
| N:M bez jména spojovací tabulky či sloupců, N:M entity na sebe, implicitní spojovací tabulka EF Core | spojovací entitu není z čeho postavit | zůstane N:M s `Incompleteness`; s katalogem se spojovací entita syntetizuje | VM | [005], [015], [067] |
| EF Core bez fluent konfigurace vztahu: stínový cizí klíč, kompozitní cizí klíč bez explicitního párování, vztah 1:1; skalární forma `[ForeignKey("Navigace")]` na vlastnosti cizího klíče | anotace páry ani 1:1 nevysloví; čtení páruje sloupec s navigací jen ze strany navigace | vztah bez sloupců, cíl je vynechá s `Convention`, katalog doplní; 1:1 se čte jako N:1; skalární forma `Loss` (č), ledaže se vlastnost jmenuje `{Navigace}Id` a konvence ji najde sama | VM | [012], [015], [067], [132] |
| výchozí sloupec cizího klíče JPA bez `@JoinColumn` nad složeným klíčem, přes `@JoinTable`, s `referencedColumnName` mimo klíč či k cíli mimo převod (nad jednodílným klíčem se `<atribut>_<sloupec klíče>` čte jako tvrzení zdroje) | nad složeným klíčem jméno specifikace nedefinuje a implementace se liší; jinde chybí, z čeho odvodit | páry prázdné pro katalog; cíl je vynechá či odvodí s `Convention`; join po asociační cestě bez katalogu `Failure` | VM | [067], [101] |
| navigace bez vztahu — kolekce nebo reference na entitu převodu bez mapování (zdroj Dapper, prostá třída MyBatisu, holé pole JPA) — v cíli NHibernate, MyBatis, Hibernate, EclipseLink; v MyBatisu i s katalogem, který pro tento cíl cizí klíč nepoptá | mapování není z čeho postavit; sloupec pojmenovaný po navigaci žádný zdroj nevyslovil a MyBatis ho odmítne; holé pole by JPA provider četl jako základní sloupec s celou kolekcí | člen na třídě zůstává bez mapování (JPA `@Transient`) + `Incompleteness` (z); EF Core a Dapper vlastnost zapíšou beze záznamu | VM | [048], [178] |
| MyBatis `<association>`, `<collection>`, `<id>` | vztah nese tvar výsledku, ne cizí klíč; `<id>` je identita řádku výsledku | navigace bez sloupců (doplní katalog); `<id>` → `Incompleteness` (`PrimaryKey`), MyBatis → MyBatis bez katalogu klíč neudrží | VM | [084] |
| typ mimo slovník: neznámý databázový typ, NHibernate `Currency`, dvojice neregistrovaná v NHibernate 5.7 (`DateOnly` nad `datetime2`), neznámý jazykový typ (`Instant`, `java.util.Date`, mapa, výčet projektu, třída mimo převod), přesnost u JPA pod jménem jiné rodiny | slovník typů je uzavřený | neznámý typ `Incompleteness` s doslovným typem na únikové cestě — cíl v druhém ekosystému ho nezkompiluje (`Map<,>`, `AlertState` v C#), což měří 2. stupeň experimentálního běhu ([záznamy](./runs/README.md)); `Currency` jako `Decimal(19,4)` s `Loss`; ostatní `Loss` | VM | [019], [052], [071], [075], [079] |
| nationalizace v EclipseLinku | `@Nationalized` nezná | ze zdroje `Loss`; cíl doslovný `columnDefinition`, nad rodinou bez národní varianty `Loss` | VM | [080] |
| hodnota sloupce pevné šířky (`CHAR(n)`, `NCHAR(n)`) mezi EclipseLinkem a ostatními frameworky | SQL Server ji doplní mezerami na šířku a pět frameworků ji tak předá; EclipseLink 5.0.0 ve výchozí relaci (`shouldTrimStrings`) odřízne koncové znaky do U+0020 z každého sloupce výsledku typu `CHAR` a `NCHAR` — sloupec, `MIN`, `MAX`, `COALESCE`, `CASE` —, v JPQL, v nativním SQL i při čtení entity; hodnotu proměnné šířky a text, který databáze spočítá, nechá (katalog FIBEN q63: `"YES"` za `"YES  "`) | měřítkem je hodnota SQL. Do EclipseLinku: projekce nejvnějšího seznamu výběru (i členů množinové operace) jako konkatenace s prázdným textem, JPQL `concat(x, '')`, nativní `x + ''`; entita `columnDefinition` `char(n)`/`nchar(n)` a vnořený konvertor `<Entita>.FixedWidth<n>`, který doplnění vrátí a konzument ho uvádí v persistence unit. Ze zdroje EclipseLink (čtečka značí textové vlastnosti `TrimmedOnRead`): projekce sloupce a `MIN`/`MAX` nad ním `TrimEnd`, entita Hibernatu vnořený konvertor `<Entita>.TrimmedFixedWidth` se stejným pravidlem jako EclipseLink. Všude `Convention` (z); řádky se nemění | VM | [169] |
| sloupec pevné šířky, o kterém nástroj neví: chybí katalog a zdroj typ nevysloví (Dapper nikdy) | pevnou šířku nese jen typ sloupce | nic; hodnota se mezi EclipseLinkem a ostatními liší oběma směry | VM | [169], [015] |
| čtení entity v cíli EclipseLink: hodnota, která před doplněním končí znakem do U+0020 (tabulátor, řídicí znak), a `CHAR(n)` v kolaci s vícebajtovou kódovou stránkou (`_UTF8`, dvoubajtové) | EclipseLink znak odřízne s mezerami a konvertor vrací jen mezery; šířka `CHAR(n)` se v takové kolaci počítá v bajtech, konvertor doplňuje znaky | znak se vrátí jako mezera; ve vícebajtové kolaci se doplní na `n` znaků (plyne z pravidla, neměřeno); `NCHAR(n)` i projekce jsou přesné, relace s vypnutým ořezem také | VM | [169] |
| sloupec pevné šířky bez délky v cíli EclipseLink; klíčový sloupec pevné šířky v cíli JPA | konvertor doplnění potřebuje šířku; JPA na identifikátor konvertor nepoužije | `Loss` (z): EclipseLink vrátí hodnotu ořezanou, Hibernate ze zdroje EclipseLink doplněnou | VM | [169] |
| projekce sloupce pevné šířky pod `DISTINCT`, podle níž se i řadí | SQL Server páruje klíč řazení se seznamem výběru podle textu: holý sloupec vedle konkatenace odmítne a konkatenaci také, protože EclipseLink váže prázdný text na každém místě jako jiný parametr (změřeno, JPQL i nativní SQL) | nepřepíše se, `Loss` (z) | VM | [169], [073] |
| hodnota pevné šířky ze skalárního poddotazu v projekci a ze sloupce mezivýsledku | přepis stojí nad sloupcem, který typuje mapování | nepřepíše se: z EclipseLinku ořezaná, ze zdroje EclipseLink doplněná | VM | [169] |
| zdroj EclipseLink: `COALESCE` a `CASE` nad hodnotami pevné šířky | EclipseLink je ořezal, jen byl-li výsledným typem `CHAR`, a ten nástroj neodvozuje | neořeže se, cíl vrátí hodnotu doplněnou | VM | [169] |
| zdroj EclipseLink: ořez v projekci ostatních cílů | `RTRIM` SQL Serveru odřízne jen mezery (výčet znaků bere podle dokumentace až od úrovně kompatibility 160), EclipseLink i řídicí znaky do U+0020 | hodnota, která před doplněním končí řídicím znakem, ho v cíli podrží; konvertor Hibernatu je přesný | VM | [169] |
| zdroj EclipseLink: čtení celé entity v cíli EF Core, NHibernate, Dapper a MyBatis | EF Core 10.0.10 nemá datovou anotaci konverze hodnoty (jen konfigurační třídu přes `[EntityTypeConfiguration]`), sloupec NHibernatu 5.7.0 nemá čtecí výraz, Dapper a MyBatis čtou celý řádek bez místa pro funkci; konfigurační třída, uživatelský typ a obsluha typu by byly nové druhy artefaktu | `Loss` (z) za každý sloupec pevné šířky; entita dostane hodnotu doplněnou, projekce sloupce ořezanou | VM | [169], [040] |
| ořez v LINQ a HQL | `string.TrimEnd()` v C# odřízne v paměti každý bílý znak, EF Core 10.0.10 i NHibernate 5.7.0 ho ale překládají na `RTRIM`, takže hodnotu počítá databáze; `TrimEnd(' ')` EF Core nepřeloží a ořízne na klientovi; HQL `rtrim(x)` NHibernate odmítá | builder píše `TrimEnd()` bez argumentu a HQL `trim(trailing from x)`; čtečky čtou jen tyto tvary | VM | [169] |
| relace konzumenta, která ořez EclipseLinku vypne (`shouldTrimStrings`, vlastnost persistence unit `eclipselink.target-database-properties`) | relace je fakt konzumentského projektu, artefakt ji nevydává | cíl EclipseLink: konkatenace i konvertor nemají co dělat a hodnota je přesná i v okrajových případech; zdroj EclipseLink: nástroj předpokládá výchozí relaci a ostatní cíle ořezávají dál | VM | [040], [169] |
| nastavení databáze a dialekt: `CONCAT_NULL_YIELDS_NULL OFF`, jiný systém než SQL Server | `x + ''` dává z `NULL` opět `NULL` jen pod `CONCAT_NULL_YIELDS_NULL ON` (výchozí, vypnutí je podle dokumentace zastaralé); jiný dialekt doplnění, konkatenaci i ořez mění (PostgreSQL podle své dokumentace převádí `char(n)` na text bez koncových mezer) | nic: deklarovaný dialekt je SQL Server 2022 s výchozím nastavením ([086]); pod `CONCAT_NULL_YIELDS_NULL OFF` dá cíl EclipseLink za `NULL` prázdný text, jiný dialekt by se měřil celý znovu | VM | [086], [169] |
| přístupnost setteru: javové pole jen s getterem, C# setter s vlastním modifikátorem (`private set`, `protected set`), `init`; javový `setX` užší než getter | model nese jen to, zda třída setter volajícím nabízí; .NET framework vlastnost bez setteru nezapíše (EF Core ji konvencí nemapuje, NHibernate ji odmítne) | cíl napíše nejužší setter, kterým ji framework zapíše: EF Core `private set` (kolekční navigace nic), NHibernate `protected set`, Dapper a Java nic; setter s modifikátorem se čte jako žádný a vrátí se v podobě cíle; `init` jako `set;` s `Loss` (č); javový neveřejný `setX` vyjde jako veřejný `set;` (mez) | VM | [134], [077] |
| get-only auto-vlastnost zdroje EF Core | EF Core ji konvencí nemapuje, ať na ní stojí cokoli | nepersistovaná jako `[NotMapped]` beze záznamu, `[Key]` na ní `Failure`; kolekční navigace a člen, který jmenuje fluent konfigurace, zůstávají mapované | VM | [134], [072] |
| modifikátory a inicializátory přes hranici ekosystémů | druhý jazyk modifikátor nemá, inicializátor píše jinak | `virtual`, `override`, `sealed`, `new`, `required` do Javy odpadnou mlčky, ostatní a javové `final`, `volatile` `Loss`; inicializátor jen literál psaný v obou stejně, jinak `Loss`; `sealed`, `partial`, `abstract` třídy model nenese | VM | [076], [077] |
| fakt, který cíl podle deskriptoru nevyjádří (Dapper 13, MyBatis 9) | tvrzení o frameworku | mechanický `Loss` za každý fakt modelu | VM | [004], [009], [084] |
| atributy NHibernate bez místa v modelu: `<class>` (vedle `name`, `table`, `schema` všechny kromě `node`), `<id>` `access`, `unsaved-value`; `<property>` `formula`, `access`, `insert`, `update`, `lazy`, `generated`, `optimistic-lock`; `<version>`/`<timestamp>` `generated`, `source`, `insert`, `unsaved-value`, `access`; `<many-to-one>`/`<one-to-one>` `formula`, `not-null`, `not-found`, `cascade`, `insert`, `update`, `entity-name`, `foreign-key`, `index`, `lazy`, `fetch`, `outer-join`, `optimistic-lock`, `access`; `<composite-id>` `access`, `mapped`, `unsaved-value`, klíčové části `access`, `entity-name`, `foreign-key`, `lazy`, `not-found`; kolekce `where`, `subselect`, `catalog`, `order-by`, `sort`, `mutable`, `check`, `collection-type`, `persister`, `generic`, `lazy`, `fetch`, `outer-join`, `batch-size`, `optimistic-lock`, `access`; `<key>` `property-ref`, `on-delete`, `not-null`, `unique`, `update`, `foreign-key`; `<one-to-many>` `not-found`, `entity-name`; `<many-to-many>` `where`, `formula`, `property-ref`, `order-by`, `unique`, `not-found`, `entity-name`, `foreign-key`, `lazy`, `fetch`, `outer-join`; facety vnořeného `<column>` vztahu; potomci čtených prvků `<formula>`, `<type>`, u kolekce `<filter>`, `<subselect>`, `<loader>`, `<sql-*>`, `<synchronize>`, `<cache>`, u `<many-to-many>` `<filter>`, `<comment>`, `<meta>` | model je nenese; `formula`, `where`, `subselect`, `catalog`, `<filter>` mění, co entita či kolekce čte, `order-by` a `sort` pořadí | `Loss` (č), u prvku každý výskyt; co builder z modelu napíše sám (`generated="always"` binární verze, `insert`/`update="false"` nad sloupcem klíče či reference) a `mapped="true"` zrcadlené klíčové třídy záznam nemá | VM | [048], [067] |
| anotace mimo podmnožinu: neznámá EF Core, JPA `@Enumerated`, `@Lob`, `@SqlResultSetMapping`, `@NamedEntityGraph`, anotace výrobce Hibernatu (kromě `@Nationalized`) a EclipseLinku (i `org.hibernate.annotations.NamedQuery`); atribut na třídě či vlastnosti zdroje Dapper a NHibernate a anotace na doménové třídě MyBatisu (framework v třídě mapování nenese) | model fakt nenese | `Loss` se jménem anotace (č); `@NamedQuery` specifikace není ztráta, čte ji dotazový průchod (2.3) | VM | [048], [077], [080], [143] |
| pole C# třídy | model nese jen vlastnosti | `Loss` se jménem (č) a větou frameworku: Dapper by pole mapoval jménem, NHibernate přes `access="field"`, EF Core konvencí nemapuje; `static` a `const` mlčí | VM | [048] |
| prvky JPA mimo podmnožinu: `@Table(catalog, indexes)`, `@JoinColumn(table, columnDefinition)`, `insertable = false` a `updatable = false` na `@Column` i `@JoinColumn`; MyBatis `<cache>`, `<cache-ref>`, `<parameterMap>`, `typeHandler` na `<id>`/`<result>` | model fakt nenese (katalog, index, vedlejší tabulka, zápisové příkazy, cache, konverze hodnoty) | `Loss` se jménem (č); zápisové příznaky mlčí nad sloupcem, který v téže třídě mapuje i vztah, resp. i skalár — idiom jen pro čtení, který cíle odvodí znovu (rozh. 012) | VM | [048], [084] |
| fluent konfigurace EF Core mimo podmnožinu: příkaz, který není řetězcem nad tvůrcem entity (cyklus, podmínka, pomocná metoda, `Entity(typeof(…))`); argument, který není literálem, `nameof` ani lambdou členu; `Property("P")` řetězcem (stínová vlastnost); vztah bez navigace; `HasDefaultValue…`, `HasComputedColumnSql`, `HasConversion`, `HasComment`, `HasData`, `HasQueryFilter`, `OnDelete`, `HasPrincipalKey`, `HasConstraintName`, `UsingEntity`, filtrovaný a neunikátní `HasIndex`, `OwnsOne`, `OwnsMany`, `HasDiscriminator`, `UseIdentityColumn`, `UseHiLo`; 1:1 bez `HasForeignKey<T>` | podmnožina je zápis, ne program; model fakt nenese | `Loss` u příkazu či volání (č); volání se známým týmž tvůrcem řetěz nechá pokračovat, jiné ho ukončí i se zbytkem; 1:1 bez závislé strany `Incompleteness` a čte se konvencí; konfigurační třída se čte i bez `ApplyConfiguration`; `HasDefaultSchema` dopadá na každou entitu zdroje; entita, kterou konfigurace jmenuje a jejíž třídu žádná jednotka nedeklaruje, `Failure` (z) | VM | [132] |
| doplňkový balíček nad frameworkem: atribut Dapper.Contrib (`[Table]`, `[Key]`, `[ExplicitKey]`, `[Write]`, `[Computed]`) na zdroji Dapper, anotace MyBatis-Plus (`@TableName`, `@TableId`, `@TableField`, `@Version`, `@TableLogic`, `@KeySequence`, `@EnumValue`, `@OrderBy`) na zdroji MyBatis; konvence Contribu (tabulka jménem třídy s `s`, klíč `Id`) | balíček tím frameworkem není: konvence platí jen v projektu, který ho používá, a to zdroj nevysloví | nečte se: `Loss` (č) se jménem balíčku a s tím, co anotace tvrdila (kategorie tabulka, klíč, sloupec, verze, strategie), tabulku a klíč doplní katalog; konvence nic. Balíček pozná import v jednotce či kvalifikované jméno, `[Table]`, `[Key]`, `@Version`, `@OrderBy` jen bez importu jmenovce z DataAnnotations či JPA; jinak obecný `Loss` anotace | VM | [133], [067] |
| alias Dapperu, který sloupec entity netvrdí: v holé jednotce SQL, `QueryMultiple`, multi-mappingu, `Execute…`; nad sloupcem jiné tabulky (join, mezivýsledek, odvozená tabulka, tabulka se jménem mimo kandidáty [050]), nad výrazem, v množinové operaci, nad navigací | sloupec vlastnosti tvrdí jen volání, které řádky mapuje na entitu, nad tabulkou entity | zůstane aliasem projekce dotazu; sloupec doplní katalog podle jména | VM | [131] |
| alias MyBatisu, který sloupec entity netvrdí: ve výběru s `resultMap` či `@Results` (zbytek mapuje `autoMappingBehavior` konfigurace, která se nečte), s `resultType="map"` či skalárem, nad nekvalifikovaným sloupcem více tabulek `FROM`, nad sloupcem jiné tabulky, nad výrazem, tečkovaný (`"product.productId"`); `mapUnderscoreToCamelCase` | sloupec vlastnosti tvrdí jen výběr, který řádky mapuje na entitu automatickým mapováním, nad tabulkou entity; přepínač je nastavení konfigurace | zůstane aliasem projekce dotazu; sloupec doplní katalog podle jména; `Account` JPetStore (klíč `userid` čtený ze spojené `SIGNON.USERNAME`) proto zůstává bez klíče | VM | [181] |
| globální mapa typu Dapperu (`SqlMapper.SetTypeMap`, `DefaultTypeMap.MatchNamesWithUnderscores`) | mapa je funkce, přepínač nastavení všech typů; zápis mapování to není | `Loss` `ColumnName` u každého výskytu (č), u mapy se jménem typu | VM | [131] |
| dva aliasy dávají jedné vlastnosti dva sloupce | model nese jeden sloupec na vlastnost; Dapper mapuje dotaz po dotazu | `Conflict` `ColumnName` (č), platí první v pořadí čtení | VM | [017], [131] |

### 2.3 Jednotka a předání dotazu

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| jednotka deklarovaná rolí (`CSharpEntity`, `JavaQuery` …), v jazyce, který zdroj nečte, nebo z níž nic nevzešlo | jednotka deklaruje jen jazyk | `Failure` o jednotce (orchestrace) | VM | [045], [066], [111] |
| aplikační kód, který dotaz nepředává (služba volající repozitář, DTO projekce) | od entity se nerozezná | čte se jako entita | Z F14 | [111] |
| pojmenovaný dotaz JPA na entitě, kterou `orm.xml` označí `metadata-complete`; jméno definované v anotaci i v `orm.xml` | čtení dotazů nedostává kontext mapování, z něhož pravidlo přednosti `orm.xml` plyne | čte se každá definice tam, kde stojí; dvě definice jednoho jména dají dva artefakty (č) | VM | [143] |
| procházení repozitáře; archiv jiný než ZIP; jméno jednotky na artefaktu | vstup vybírá uživatel; sloučená entita nepatří jedné jednotce | nenárokuje se | Z F14 | [066], [094], [111], [136] |
| zobrazení mezireprezentace | uživateli je zbytečná a ruší: co z ní potřebuje, nese kniha tvrzení a záznamy slovy vstupu a výstupu | `/convert` ji nevrací, obrazovka ji nemá; nenárokuje se ani neplánuje | Z F14 | [154] |
| soubor archivu ZIP, jehož přípona nejmenuje jazyk, který zdroj čte; prázdný soubor, `__MACOSX/`, šifrovaná položka, komprese jiná než *stored* a deflate; archiv rozdělený do více souborů | jazyk určuje přípona a zdroj; prohlížeč rozbaluje jen deflate | jednotkou se nestane, obrazovka ho jmenuje s důvodem v seznamu nad okny | VM | [136] |
| příkaz, který není čtecí `SELECT` (`INSERT`, `UPDATE`, `DELETE`, `DECLARE`, `SET`, `USE`, `SELECT … INTO`, `SELECT @x = …`) | mění stav, ze kterého čtou ostatní dotazy | `Failure` celé holé jednotky, u vloženého textu toho dotazu | VM | [108] |
| druhý `SELECT` v textu vloženém do hostitele (Dapper mimo `QueryMultiple`, `<select>`, `<sql-query>`) | hostitel pošle jeden příkaz s jedním výsledkem | `Failure` dotazu | VM | [108], [109] |
| `FOR XML`, `FOR JSON`, `TABLESAMPLE`, `WITH XMLNAMESPACES` | dokument či vzorek, ne řádky | `Failure` (č) | VM | [§5] |
| proměnná dotazu přiřazená víckrát či podmíněně, kterou kód prodlužuje, i s holou sadou (`IQueryable<Order> q = ctx.Orders`; sada známá jen podle místa jen s krokem LINQ v hodnotě či pokračování); objekt dotazu, se kterým kód pokračuje v jiném příkazu | dotaz se skládá za běhu | `Failure` (č); výsledek a objekt opouštějící jednotku se nesledují | VM | [109], [113] |
| text dotazu, který není literál (Dapper, JPA, NHibernate) | není co číst | `Incompleteness`, dotaz nevznikne (`Failure`: nenese instrukce) | VM | [026], [109] |
| dotaz skládaný za běhu: `QueryOver`, `CreateCriteria`; EF Core: díra interpolace spočtená na místě, argument `SqlQueryRaw`, který není jméno, `@x` v textu | nemá text, nebo hodnota vzniká za běhu | `QueryOver`, `CreateCriteria` `Failure`, zbytek `Incompleteness` | VM | [113] |
| předání jménem: `GetNamedQuery`, `createNamedQuery` | dotaz je v definici | `Incompleteness`; definici čte tam, kde stojí — `<query>`/`<sql-query>` v `hbm.xml`, `@NamedQuery`/`@NamedNativeQuery` (i opakované a v kontejneru) na třídě a `<named-query>`/`<named-native-query>` na kořeni `orm.xml` i v `<entity>` — jako dotaz se jménem ze zdroje; volání na objektu odkazu se nečtou | VM | [081], [113], [143] |
| pojmenovaný dotaz JPA, který zapisuje (`update`, `delete`, `insert`; i v `createQuery`); definice bez jména; definice bez textu | mění stav, ze kterého čtou ostatní; jméno každá forma vyžaduje | `Failure` se jménem dotazu a příkazu | VM | [053], [081], [143] |
| volby definice pojmenovaného dotazu JPA: `hints`/`<hint>` (slovníkem profilu: nápověda měnící řádky `Failure`, zámek či materializace `Loss`, jiná `Loss`), `lockMode`/`<lock-mode>` mimo `NONE`, `resultSetMapping`/`result-set-mapping`, jiný člen anotace (`entities`, `classes`, `columns` nativního dotazu), cizí potomek `<named-query>`; `resultClass`/`result-class` mlčí; vazba `setParameter` v kódu, který dotaz volá, skalár parametru netypuje | provedení, ne řádky; řádek odvodí každý cíl; typ vazby je výraz aplikačního kódu v jiné jednotce | `Loss` (č); skalár dává brána z porovnání jako u `createQuery` | VM | [048], [083], [143] |
| volby pojmenovaného dotazu `hbm.xml` (`cacheable`, `cache-region`, `cache-mode`, `fetch-size`, `timeout`, `flush-mode`, `read-only`, `comment`, nativně `callable`, `<synchronize>`); `<query-param>` se skalárem mimo slovník (`XmlDoc`, uživatelský typ) | provedení, ne řádky; skalár mimo slovník model nenese | `Loss` (č); hodnota, kterou volba má i nevyslovená (`cacheable="false"`), se nehlásí; `callable` ztratí značku a text se čte, volání (`exec`) odmítne čtení | VM | [048], [083] |
| prvek v `<query>` či `<sql-query>`, který schéma `hbm.xml` nepřipouští; týž `<query-param>` dvakrát se dvěma skaláry | text potomka by mohl být kusem dotazu; parametr nemá dva typy | `Failure` (č) | VM | [053], [083] |
| kořen EF Core `x.M`, kde jednotka uvádí pro `x` typ mapovaný jako entita; člen typu `IQueryable<T>`, přes který se dotaz skládá; řetěz či vyčíslení nad parametrem, polem či vlastností, které jednotka deklaruje `IQueryable<T>`, a nad lokální proměnnou tak deklarovanou, jejíž hodnotu čtení nesleduje (EF Core i NHibernate) | entita `DbSet` nemá; strom poskytovatele začíná tím, co předá volající nebo co jméno drží, a to text neříká | `Failure` (č); vrácené či předané beze změny místem není | VM | [114] |
| kořen, jehož entitou je typový parametr: `ctx.Set<T>()` a `session.Query<T>()` generického repozitáře, jméno deklarované `DbSet<T>`, v kořeni dotazu i v poddotazu či joinu | tabulku určí typ, který dosadí volající, a ten text neříká (`EFCore/EFCoreDbSetParameterProviderTest`) | `Failure` (č) se jménem typového parametru; volání, které jen sleduje entitu (`Add`, `Remove` …), místem není | VM | [053], [114] |
| sada EF Core známá jen podle místa vrácená či předaná tam, kde jednotka typ dotazu neuvádí (`IEnumerable<Order> All() => ctx.Orders;`, `object`, `new List<Order>(ctx.Orders)`, argument metody mimo jednotku, lambda bez návratového typu) | text ji neodliší od `customer.Name` ani od navigace entity z jiného souboru: `IEnumerable<T>` přijme i seznam v paměti, `IQueryable<T>` a `DbSet<T>` ne (`EFCore/EFCoreSetHandedOverProviderTest`) | nečte se; třída kolem zůstává entitou | VM | [109], [114] |
| kořen EF Core jménem sady, kterou žádný kontext převodu nedeklaruje (fragment nad `ctx`, projekt poslaný bez kontextu) nebo kterou dva kontexty deklarují nad různými entitami, a jméno není jménem tabulky, entity ani jejím druhým číslem (`ctx.Categories` nad tabulkou `Category`) | převod sadu nezná; co jméno vystavuje, říká jen kontext | tabulka jména sady, typ výsledku odvozený s `Convention` (Q2, [050]); s kontextem v převodu sada své entity | VM | [050], [185] |
| dotaz předaný kroku LINQ ve statickém tvaru, přímo i přes proměnnou (`Queryable.Where(q, f)`, `Enumerable.ToList(q)`, `EntityFrameworkQueryableExtensions.CountAsync(q)`, NHibernate `LinqExtensionMethods`, `EagerFetchingExtensionMethods`, `DmlExtensionMethods`) | statický tvar čtení nečte, a bez něj by dotaz vrátil jiné řádky | `Failure` (č) jmenovitě | VM | [070], [109] |
| hromadný příkaz nad řetězem LINQ: EF Core `ExecuteDelete`, `ExecuteUpdate`; NHibernate `Delete`, `Update`, `UpdateVersioned`, `InsertInto`, `UpdateBuilder`, `InsertBuilder` (i asynchronně, přes proměnnou a ve statickém tvaru); HQL `update`, `delete`, `insert` (holá jednotka, `<query>`, `CreateQuery`) | zapisuje řádky, ze kterých čtou ostatní dotazy, a vrací jejich počet, ne řádky (`EFCore/EFCoreBulkCommandProviderTest`, `NHibernate/NHibernateBulkCommandProviderTest`) | `Failure` (č) se jménem volání či příkazu; třída kolem je kódem kolem dotazů | VM | [053], [108], [113], [143] |
| navigace entity z jiného souboru bez mapovací jednotky, i vyčíslená ve `foreach`; proměnná z volání, které jednotka nedeklaruje (`repository.Load(id)`) | jednotka o jménu nic neuvádí, rozhoduje místo | čte se podle místa jako dotaz; s mapovací jednotkou `Failure` | VM | [114] |
| explicitní načtení nad jménem, které jednotka neuvádí, s navigací deklarovanou víc entitami či žádnou; navigace, kterou text nefixuje; M:N; bez mapovací jednotky | filtr z mapování, nic se nehádá | `Failure` jmenovitě; builder EF Core explicitní načtení nepíše (řetěz nad `Set<T>()`); `NHibernateUtil.Initialize` se nečte | VM | [115] |
| dynamický příkaz MyBatisu (`<if>`, `<choose>`, `<when>`, `<otherwise>`, `<bind>`) | rodina příkazů s různými řádky (část 3) | `Failure` se jménem značky | Z F8 | [084] |
| `<foreach>` mimo kanonický tvar, `#{a.b}`, `@` už ve zdrojovém SQL, `@SelectProvider`; týž příkaz v anotaci i v XML (`${}` v 2.4) | cestu signatura nevysloví; dvě definice odmítne MyBatis sám | `Failure` (č); `jdbcType`, `typeHandler`, `mode` v `#{}` `Loss` | VM | [084] |
| zápisový příkaz MyBatisu (`<insert>`, `<update>`, `<delete>`, `@Insert`, `@Update`, `@Delete`); escape JDBC `{call …}`; jazykový ovladač jiný než XML (`lang`, `@Lang`) | zápis ani volání procedury není čtecí dotaz; jiný ovladač čte text jinak | `Failure` (č) se jménem příkazu, escape či ovladače | VM | [053], [084], [109] |
| volby příkazu MyBatisu: atributy `<select>` mimo `id`, `parameterType`, `resultType`, `resultMap` (`statementType`, `fetchSize`, `timeout`, `useCache`, `flushCache`, `resultSetType`, `resultOrdered`, `resultSets`, `affectData`, `parameterMap`, `databaseId`), `@Options`, `@MapKey` | provedení či uspořádání výsledku, ne řádky | `Loss` (č); hodnota, kterou volba má i nevyslovená, se nehlásí; u `statementType="CALLABLE"` rozhoduje text: dotaz se přeloží, volání (`EXEC`, `{call …}`) odmítne čtení | VM | [048], [084] |
| volby předání v kódu: volání na objektu dotazu JPA mimo parametry, výřez a vykonání (`setHint`, `setFlushMode`, `setTimeout`, `setCacheStoreMode` …), i zřetězená a na proměnné; Dapper `buffered`, `commandTimeout` a argument bez místa v přetížení; NHibernate `UniqueResult`, `SetTimeout` a jiné neznámé volání na objektu dotazu; zámek (`setLockMode`, `setHibernateLockMode`, `eclipselink.pessimistic-lock`) | provedení, ne řádky; zámek řádky nemění | `Loss` (č) jmenovitě; navázání parametru, objekt parametrů, transakce a vykonávací volání (`getResultList`, `getSingleResult`, `list` …) nic | VM | [048], [109] |
| mapování výsledku v kódu: transformery Hibernate (`setTupleTransformer`, `setResultListTransformer`, `setResultTransformer`), `eclipselink.result-type`, multi-mapping Dapperu (`map`, `splitOn`, `types`) | dotaz mapování výsledku nenese, cíl řádek odvodí | jeden `Loss` (`Projection`) (č) | VM | [067], [109] |
| volání a nápovědy, které mění řádky: Hibernate `setPage`, `getKeyedResultList`, `getResultCount`; EclipseLink `eclipselink.jdbc.max-rows`, `eclipselink.jdbc.first-result`, `eclipselink.history.as-of` (i `.scn`), `eclipselink.query-type` | model je nenese, bez nich jiné řádky | `Failure` (č), i v pozdějším příkazu; pod Hibernatem je nápověda EclipseLinku `Loss` | VM | [070], [109] |
| `commandType` Dapperu `StoredProcedure`, `TableDirect` nebo spočtený za běhu | text jde jako jméno procedury či tabulky, nebo se rozhodne za běhu | `Failure` (č) se jménem typu | VM | [053], [109] |
| volání Dapper.Contrib v jednotce, která ho importuje: `Get<T>`, `GetAll<T>`, `DeleteAll<T>`, `Insert`, `Update`, `Delete`, i `…Async`; rozhraní mapperu, které dědí `BaseMapper<T>` MyBatis-Plus | balíček mimo rozsah; SQL skládá za běhu, nebo zapisuje | volání Contribu je předání s vlastním číslem, `Failure` (č) se jménem volání, třída kolem něj je kódem kolem dotazů; `BaseMapper` jeden `Loss` u rozhraní, vlastní příkazy se čtou. Bez importu (`global using`) se volání nepozná a třída se čte podle ostatních předání | VM | [133], [109], [111] |
| vstup zanořený hlouběji než strop (výchozí 128, nastavuje provozovatel) | sestup by mohl shodit proces | `Failure` s řádkem a sloupcem před sestupem; XML strop nemá | VM | [092] |
| nečitelný text jednotky, u C# i text, který Roslyn přečte jen zotavením | co Roslyn dopsal, klient nenapsal | `Failure` s řádkem a sloupcem, kde čtení skončilo, ve všech šesti jazycích | VM | [093], [128] |

### 2.4 Parametry

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| parametr, jehož skalár se neodvodí (porovnaný s jiným parametrem, se sloupcem mimo mapování, s vlastností bez jazykového typu) | skalár se bere z druhé strany; cíl potřebuje typovanou hlavičku | `Failure` (`QueryParameter`) (b) | VM | [083], [107] |
| parametr zdroje Dapper bez katalogu, nebo katalog tabulku nenajde či najde ve dvou schématech | Dapper tabulku ani sloupce netvrdí | `Failure` (b); s katalogem, který tabulku má, typovaný do všech cílů | VM | [083], [105], [106] |
| týž název se dvěma skaláry; jednou kolekce, jednou hodnota (Dapper `IN @ids` vedle `IN (@ids)`) | parametr nemá dva typy | `Failure` (b; u Dapperu už wrapper) | VM | [083], [106] |
| pojmenovaný a poziční parametr v jednom dotazu; jméno, které není prostý identifikátor | JPQL souběh nepřipouští; cestu signatura nevysloví | `Failure` (b) | VM | [083] |
| kolekční parametr mimo pravou stranu `IN`, uvnitř výčtu (`IN (1, :ids)`) či jako počet řádků; skalár jako pravá strana `IN` | kolekce jen na místě seznamu, výčet bere jednotlivé hodnoty | `Failure` (`QueryParameter`) (č/b) | VM | [083], [085], [102] |
| celočíselný počet řádků širší než `Int` (MyBatis `Long`) | API stránkování jsou 32bitová | `Loss`, metoda bere `int` | VM | [085] |
| poziční parametr do jiného cíle než JPQL | jinde poziční tvar není | pojmenovaný `p1`, `p2` s `Convention` | VM | [083] |
| `${…}` MyBatisu | textová substituce, ne hodnota | `Failure` (č) | VM | [082], [084] |
| hodnota z okolí v LINQ hlubší než jméno nebo člen jména (`tokenResult.Data.UserId`, `query.PageVersionId.Value`); převod výčtu na číslo (`(short)query.Status`, `(short)Status.Latest`) | parametr LINQ je zachycené jméno a jeho člen (rozh. 083), delší řetěz čtení za hodnotu nebere; slovník převodů nese čísla, ne výčet | `Failure` (č); hodnota vyčíslená do lokální proměnné před dotazem se čte | VM | [083], [113] |

### 2.5 `LIKE`, `DISTINCT` a výčet hodnot

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| únikový znak, který není literál, je delší než jeden znak nebo stojí u jiného operátoru než `LIKE` | fakt o čtení vzorku; T-SQL i JPQL berou jeden znak | `Failure` (`Filtering`) (č/b) | VM | [102] |
| řetězcová metoda LINQ s přetížením `StringComparison` | provider ji na vzorek nepřeloží | nečte se | VM | [051] |
| agregát nad různými celými řádky: JPQL `count(distinct c)`, `COUNT(DISTINCT *)`, LINQ `Distinct().Count()`, `.Sum()`, `.Average()` nad celou entitou | SQL ten tvar nemá | `Failure` (č/b); `Max`, `Min` nad zhroucenými řádky značku vypustí s `Convention` | VM | [073], [102] |
| krok LINQ za `Distinct()` celé entity (`Select`, `GroupBy`, `Join`) | `DISTINCT` stojí nad koncovou projekcí | `Failure` (č); po projekci se čte jako mezivýsledek | VM | [073], [112] |
| pod `DISTINCT` klíč řazení mimo projekci | T-SQL ho odmítá, LINQ ho nepojmenuje | `Failure` (`Ordering`) (b) | VM | [073] |
| `DISTINCT` nad `EXCEPT ALL` | výsledek není `EXCEPT` | `Failure` (b); nad `UNION ALL` přepis na `UNION`, nad ostatními vypuštění, obojí `Convention` | VM | [073] |
| prvek výčtu `NULL`, sloupec, funkce, výraz; prázdný výčet | výčet nese literály a skalární parametry; `NULL` v `NOT IN` cíle vyhodnotí různě; `IN ()` nemá tvar | `Failure` (`Filtering`) (č) | VM | [074], [102], [107] |
| výčet se skaláry mimo jednu číselnou rodinu (`IN (1, 'a')`, `IN (0.5, 1E0)`) | C# pole nepřeloží, T-SQL porovná jinou hodnotu | `Failure` (b) | VM | [074] |

### 2.6 Joiny

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| křížový join čárkou; LINQ `SelectMany` nad druhým zdrojem bez filtru | model ho nenese, bez něj jiné řádky | `Failure` (`Join`) (č) | VM | [070], [101], [113] |
| laterální join (`CROSS APPLY`, `OUTER APPLY`, HQL `join lateral`), join na tabulkovou funkci či `VALUES` | slovník laterální odkaz nemá | `Failure` (č) v každém cíli | VM | [112], [113] |
| entity join HQL či JPQL bez `with`/`on`; nepřečtená podmínka joinu | join filtruje i násobí | `Failure` (č/b) | VM | [070], [061] |
| poddotaz v podmínce joinu (`ON … AND x IN (SELECT …)`, `EXISTS`, korelace) | — | nese se jako operand podmínky: T-SQL jak stál, HQL 7.4 a JPQL za `on`, HQL 5.7 za `with`, LINQ korelovaný `SelectMany` s `Contains`/`Any` v predikátu (EF Core 10 `CROSS`/`OUTER APPLY`, tytéž řádky); mechanická pravidla poddotazu (2.7) platí i tu | VM | [162] |
| `fetch` u joinu | mění načítání, ne řádky | `Loss` (č) | VM | [070], [101] |
| asociační cesta, kterou mapy převodu nerozresolvují (entita, vztah či cíl mimo převod, prázdné páry vztahu i vlastnící protistrany); cesta přes N:M; LINQ `SelectMany` přes víc asociací; cesta přes kolekci mimo `join` (HQL a JPQL `c.orders.total`, LINQ `c.Orders.Count`) a cesta, která by v podmínce joinu (`on`/`with`, klíčové selektory či filtry spojované posloupnosti `Join`) přinesla implicitní join | podmínka se odvozuje z mapování, nic se nehádá; cesta přes N:M by chtěla dva joiny přes junction entitu; kolekci jazyky navigují jen joinem, druhým `from` či agregátem; join z podmínky joinu by musel stát před joinem, který ho jmenuje | `Failure` (`Join`) se jménem cesty (č: HQL, JPQL, LINQ), v HQL a JPQL v každé klauzuli včetně projekce a řazení — cesta je vnitřní join a dotaz bez ní by vrátil jiné řádky | VM | [101], [144], [148], [152] |
| join po asociační cestě ve zdroji; cesta přes referenci mimo `join` (HQL a JPQL `alert.alertHistory.clusterId`, LINQ `o.Customer.Name` ve filtru, projekci, klíči seskupení, řazení, predikátu agregátu; i řetěz) | builder cestu nevydává | entity join s odvozenou podmínkou, řádky tytéž; cesta mimo `join` je join pod aliasem `alias_navigace`, jeden na asociaci a tělo dotazu — v HQL a JPQL vnitřní, v LINQ `LEFT`, smí-li řádek reference chybět (nulovatelný cizí klíč, nebo řádek, z něhož se reference kříží, sám smí chybět), jinak `INNER`, u EF Core i NHibernatu | VM | [101], [144], [148], [151] |
| LINQ `GroupJoin` (`join … into`), `Zip`; `Join` se selektory, které nejsou lambdy či se nespárují | model je nenese | `Failure` (`Join`) (č) | VM | [103], [113] |
| result selector či `Select` LINQ joinu, který ze složeného řádku vynechá řádek, nebo dá celý řádek vedle sloupců; `select r, c.x` v HQL a JPQL; `SELECT t.*, c.x` v T-SQL | řádky tytéž, sloupce ne; řádek vedle sloupců model nenese | `Loss` (`Projection`) (č); jedna strana sama (`(ol, o) => o`, `select r … join`, `SELECT t.*`, `SelectMany` s jedním argumentem) je celý řádek té strany a čte se bez záznamu; celý řádek *mezivýsledku* vedle sloupců (`SELECT d.*, …` nad definicí) jsou sloupce té definice v jejím pořadí, přesně z konstrukce (2.10) | VM | [070], [161] |
| holý sloupec dotazu T-SQL přes víc tabulek, který nemapuje entita žádného aliasu (tabulka mimo převod, sloupec mimo mapu) | komu patří, ví jen schéma; cíl, který sloupec píše jako člen řádku, ho bez řádku nenapíše | SQL cíl ho píše holý, jak stál; EF Core, NHibernate, Hibernate, EclipseLink `Fallback` (z) s kategorií klauzule; mapu doplní katalog (F6) | VM | [015], [113] |
| spojovaná posloupnost s jiným krokem než `Where` | je to dotaz sám o sobě | `Failure` (`IntermediateResult`) (č) | VM | [113] |
| cíl EF Core: pravý či plný vnější join s podmínkou nad rámec rovností klíčů; podmínka joinu se sloupcem bez tabulky | filtr posloupnosti by ubral zachované řádky; sloupec nejde přiřadit straně | `Fallback` (z) | VM | [113] |
| cíl NHibernate: plný vnější join | HQL 5.7 ho nemá ani množinové operace na složení | `Fallback` (z) | VM | [065], [113] |
| druhý tvar NHibernatu (LINQ): pravý vnější join (plný píše už HQL nativně, řádek výš) | provider 5.7.0 zná `Join` a levý join jako `GroupJoin` s `DefaultIfEmpty()`; `LeftJoin` a `RightJoin` .NET 10 nepřekládá | `Omitted` (z), HQL stojí samo | VM | [118] |
| sloupec cizího klíče bez skalární vlastnosti mimo podmínku joinu po asociaci: cíl EclipseLink vždy, cíl NHibernate u části složeného klíče | cesta přes referenci (`p.customer.id`) je v nich vnitřní join a ztratí řádky s `NULL` | `Fallback` (z) | VM | [113] |
| reference sama jako operand LINQ (`Select(o => o.Customer)`, `Count(o.Customer)`, i na konci delší cesty `o.Customer.Region`, pro niž join nevznikne); podmíněný `Count` nad testem reference se složeným cizím klíčem; LINQ test kolekce či inverzní reference na `null` | model operand řádku nemá; počet jednoho sloupce složeného klíče není počet řádků s celou referencí; kolekce není nikdy `null`, inverzní reference je nepřítomnost řádku na druhé straně | nečte se (v projekci a agregátu `Loss`, ve filtru `Failure`) jmenovitě (č) | VM | [070], [101], [148] |
| cesta ke klíči vlastnící reference (HQL a JPQL `c.user.id`, LINQ `o.Customer.CustomerID`) nad řádkem s `NULL` cizím klíčem v projekci či v testu na `NULL` | čte se jako sloupec cizího klíče bez joinu (čtení Hibernatu, NHibernatu i EF Core, inverze toho, jak buildery sloupec bez skalární vlastnosti píšou); specifikace JPA a EclipseLink ji vykládají vnitřním joinem, který řádek s `NULL` klíčem vypustí | sloupec; řádek s `NULL` cizím klíčem zůstává, kde by ho vnitřní join vypustil — u porovnání s hodnotou rozdíl není (č) | VM | [144], [148] |
| JPQL a HQL srovnání celé entity jinak než `asociace = řádek` (s parametrem, hodnotou, jiným řádkem, kolekcí, vztahem bez sloupců) | model operand entity nemá; `p.customer = c` se čte jako rovnosti klíče | nečte se (`Failure`) (č) | VM | [101] |
| JPQL a HQL: test celé entity na `NULL` jinde než nad vlastnící jednohodnotovou asociací se známými sloupci — kolekce, inverzní reference, `c is null` nad proměnnou, reference bez sloupců | model nenese nulovost řádku ani prázdnost kolekce; inverzní reference je nepřítomnost řádku na druhé straně | `Failure` (č) | VM | [101] |

### 2.7 Poddotazy, množinové operace a stránkování

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| `IN`, kvantifikované či skalární porovnání s poddotazem, který nemá právě jednu projekci (u těla množinové operace rozhoduje nejlevější člen) | SQL by ho odmítlo za běhu | `Failure` (b) | VM | [061], [120] |
| poddotaz v operandu s tělem množinové operace do NHibernatu a EclipseLinku; skalární porovnání s takovým tělem do LINQ | HQL 5.7 množinovou operaci nemá, parser EclipseLinku 5.0.0 ji v poddotazu odmítne; složené řádky nemají jeden koncový agregát, který by LINQ vzal | `Fallback` (z) | VM | [113], [120] |
| množinová operace, jejíž člen má entitní join, do EclipseLinku | EclipseLink 5.0.0 vypustí joiny člena z SQL a vrátí řádky jeho kořene (změřeno katalogem FIBEN: 49 870 řádků za 189) | `Fallback` (z) | VM | [113], [149] |
| řazení uvnitř poddotazu či těla definice bez výřezu | T-SQL ho tam nepřipouští, řádky nemění | `Loss` | VM | [061], [112] |
| výřez v poddotazu či operandu množinové operace do HQL a JPQL; literálový počet řádků nad `Int32` do EF Core, NHibernatu a JPA | text výřez nemá, je na objektu dotazu; API jsou 32bitová | `Fallback` (z) | VM | [060], [113] |
| cíl EF Core: skalární poddotaz, který není jediným neseskupeným agregátem; `IN` nad neseskupeným agregátem; množinová operace nad různými typy prvků | `First()` by tiše vybral řádek; LINQ různé typy nesloží | `Fallback` (z) | VM | [113] |
| LINQ metoda na jeden řádek v pozici operandu (`… == ctx.Orders.Select(…).FirstOrDefault()`) | skalární poddotaz s výřezem nemá v modelu výrobce | nečte se | VM | [103] |
| LINQ `All(predikát)`, který není jedním relačním porovnáním s parametrem lambdy právě na jedné straně (`All(o => o.A > 1 && …)`, `All(o => o.A > o.B)`, `All(v => x > v.Value)` nad projekcí) | jediný přepis je `NOT EXISTS` nad negovaným predikátem a negace nad `NULL` vybírá v SQL jiné řádky než v C# | `Failure` (`Subquery`) jmenovitě (č) | VM | [119] |
| kvantifikované porovnání (`ALL`, `ANY`, `SOME`) nad poddotazem s neseskupeným agregátem (`x > ALL (SELECT MAX(v) …)`) do LINQ | `Select` LINQ neseskupený agregát nenese; kvantifikátor dědí pozici pravé strany `IN` | `Fallback` (z); textové cíle píší | VM | [061], [119] |
| `= ANY`, `<> ALL`; `SOME` | tytéž řádky jako `IN`, `NOT IN`, `ANY`; model nese jeden zápis | přepis na `IN`/`NOT IN` s `Convention`; `SOME` → `ANY` beze záznamu | VM | [119] |
| zdroj LINQ: kvantifikátor `All`/`Any` s `==` či `!=` nad hodnotou, která smí být `NULL`; v EF Core negace kvantifikátoru nad takovou hodnotou; `Contains` nad poddotazem, jehož hodnota smí být `NULL` a který seskupuje, stránkuje či skládá řádky (EF Core) | pod lambdou páruje provider `NULL` s `NULL` a jeho negace je pravdivá tam, kde je `NOT` SQL *unknown*; mezireprezentace kvantifikátor s takovou shodou nenese a filtr shody v takovém poddotazu nemá kde stát | `Failure` (`Subquery`) (č) | VM | [156], [070] |
| `INTERSECT ALL`; `EXCEPT ALL` do T-SQL a LINQ | slovník `INTERSECT ALL` nemá; `EXCEPT ALL` SQL Server odmítá a LINQ nemá | `INTERSECT ALL` `Failure` (č); `EXCEPT ALL` `Failure` (z) T-SQL i na únikové cestě, cíle JPA ho píšou | VM | [§4.4] |
| řazení či projekce za množinovou operací | model za operací nic nenese; řádky tytéž | `Loss` (č) | VM | [053], [070] |
| filtr, join, seskupení či výřez za množinovou operací; `OFFSET` v jejím operandu | bez nich jiné řádky; T-SQL výřez v operandu nezapíše | `Failure` (č/z) | VM | [053], [060], [113] |
| počet řádků, který není číslo ani parametr (`TOP (@n + 1)`, spočtený za běhu); `TOP … PERCENT`, `WITH TIES`, `TOP` vedle `OFFSET` | počet řádků je číslo, nebo parametr | `Failure` (`Pagination`) (č) | VM | [060], [085] |
| výřez z výřezu, `Skip` za `Take`, krok, který s výřezem nekomutuje; `Last`; výřez na objektu dotazu vedle `limit` | výřez je poslední operací rozsahu, model nese jeden | `Failure` (č/b); za výřezem projektovaných řádků se krok čte jako mezivýsledek | VM | [060], [103], [112] |
| `First`, `Single`, `ElementAt` a jejich `…OrDefault` | čtou se jako výřez jednoho řádku | `Convention`; artefakt vrací seznam o nejvýš jednom prvku, u `Single` druhý řádek nekontroluje | VM | [103] |

### 2.8 Výrazy a funkce

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| funkce mimo slovník: `CONVERT`, `REPLACE` mimo vzorek, `LEFT`, `RIGHT`, `LTRIM`, `RTRIM` se dvěma argumenty, `trim(leading …)` a `trim` jiného znaku než mezery, HQL `rtrim()`, `TrimEnd(znaky)`, `SYSDATETIME()`, `DateTime.UtcNow`, HQL `str()`, `year()` v JPQL a cokoli mimo slovník `QueryFunction` | uzavřený slovník; funkce vstupuje se zápisem v T-SQL a ověřeným zápisem v každém cíli | nečte se (`Expression`) | VM | [107], [113] |
| převod do jiného typu než `INT`, `BIGINT`, `REAL`, `FLOAT`, `NVARCHAR(MAX)`, s délkou či do neunicode textu; `DATEADD`, `DATEDIFF` s týdnem, čtvrtletím či milisekundou; `ROUND` se třetím argumentem | mohly by změnit hodnotu; třetí argument mění zaokrouhlení na ořez | nečte se | VM | [113] |
| `+`, jehož ani jedna strana není typovaná | cíle ho hláskují různě (konkatenace, nebo sčítání) | `Failure` (`Expression`) (b) | VM | [107] |
| klauzule `let` dotazového výrazu LINQ | zavádí spočtenou proměnnou rozsahu, pro kterou model nemá místo | `Failure` (`Projection`) (č) | VM | [103] |
| člen `Nullable<T>` v podmínce LINQ: `x.HasValue`, `!x.HasValue`, `x.Value` | slovník výrazů ho nenese; test na `NULL` je porovnání s `null` | `Failure` (č); `x == null` a `x != null` se čtou | VM | [107] |
| aritmetika, `COALESCE`, `CASE` nad skaláry, které se nesjednotí (`Decimal` s `Float`) | C# nepřeloží, T-SQL porovná jinou hodnotu | `Failure` (b) | VM | [107] |
| agregát nad agregátem (`SUM(COUNT(*))`), i do seznamu | žádný cíl ho nezapíše | `Failure`: přímo vnořený (`MAX(SUM(x))`) při čtení (č), uvnitř výrazu bránou (b); přes odvozenou tabulku se nese | VM | [107], [112] |
| alias projekce, který zdroj oddělil, protože není identifikátorem (`AS "product.productId"`); u MyBatisu zároveň pokyn automatickému mapování do vnořené vlastnosti | anonymní typ C#, výsledková proměnná JPQL a HQL takové jméno nemají; vnoření model nenese | SQL cíl `AS [product.productId]`; EF Core, NHibernate, Hibernate, EclipseLink `Fallback` (z) — třída řádku `SqlQuery` EF Core sloupec odmítne (`Failure`); zdroj MyBatis `Loss` o vnoření (č) | VM | [082], [084], [113] |
| výraz v projekci bez aliasu | sloupec bez jména nečte žádný cíl, jméno se nevymýšlí | `Failure` (b); konstanta bez aliasu `Loss` (č); v dalším členu množinové operace dostane výraz i konstanta jméno levého členu podle pozice, jak ho dává SQL ([112]) — v definici, nahoře v dotazu i v poddotazu | VM | [107], [028], [112] |
| výraz jako prvek výčtu či počet řádků | ty mají vlastní uzavřené tvary | `Failure` (č) | VM | [074], [085], [107] |
| konkatenace nad sloupcem ve zdroji LINQ mimo stráž `x == null ? null : …` nad hodnotami konkatenace | C# bere `null` jako prázdný řetězec, SQL, HQL i JPQL vracejí `NULL`; stráž říká totéž co SQL a čte se jako holá konkatenace | `Loss` (č); artefakt má sémantiku SQL a cíl EF Core ji píše pod stráží ([§5]) | VM | [107] |
| druhý tvar NHibernatu (LINQ): konkatenace v projekci nad textem, který smí být `NULL`; skalární poddotaz, který není jediným agregátem | provider 5.7.0 projekci vyhodnotí na klientovi, kde `null` je prázdný řetězec; obecné meze LINQ jako u EF Core | `Omitted` (z), HQL stojí samo | VM | [118] |
| funkce, kterou deskriptor cíle neuvádí: `DateAdd`, `DateDiff` u NHibernatu, k nim `Cast` u EclipseLinku | sonda proti připnuté verzi zápis nepotvrdila | `Fallback` (b) | VM | [113] |
| převod textu na text do NHibernatu (`NVARCHAR(4000)`) a Hibernatu (`varchar(max)`); do EF Core `Math.Round` nad celým číslem, hodina a menší jednotka k datu bez času, převod textu na číslo, rozdíl dat nad datem a okamžikem | jazyk cíle tvar má, ale s jinou hodnotou, nebo bez přetížení pro dva typy | `Fallback` (z) | VM | [113] |
| `CASE` bez `ELSE` do LINQ, kde brána skalár větví neodvodí | C# nemá čím otypovat `null` | `Failure` (z) | VM | [107] |
| konstruktor `DateTime` s počítaným argumentem či neexistujícím datem; řetězec okamžiku mimo ISO 8601 proti časovému sloupci | vyhodnotit ho = spustit program; databáze by ho odmítla až za běhu | konstruktor nečte se; řetězec `Failure` (`Filtering`) (b) | VM | [024], [070] |
| alias projekce, který je klíčovým slovem HQL (`ascending`, `descending`); jméno mezivýsledku (`WITH`, odvozená tabulka) psané jako klíčové slovo | alias a jméno jsou slova zdroje, nepřepisují se (alias řádku, který je klíčovým slovem HQL, a v JPQL každá proměnná řádku i výsledková proměnná, která je vyhrazeným identifikátorem — např. EclipseLink `size` —, se naopak píše s podtržítkem na konci, §5) | nehlásí se; odmítne ho až framework | VM | [028] |

### 2.9 Seskupení, okenní funkce a agregace do seznamu

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| projekce, `HAVING` či řazení seskupeného dotazu se sloupcem mimo klíč a agregát; klíč seskupení, který agreguje či je okenní funkcí | pravidlo seskupení SQL | `Failure` (`Grouping`) (b) | VM | [113] |
| agregát vedle prostých sloupců bez seskupení | neplatný už zdroj | `Incompleteness`; artefakt vznikne a odmítne ho databáze | VM | [§7] |
| `GROUP BY ROLLUP`, `CUBE`, `GROUPING SETS` | model je nenese | `Failure` (č) | VM | [070] |
| LINQ selektor prvků `GroupBy` jiného tvaru než strana spojeného řádku či hodnota (anonymní objekt …); result selector | model je nenese | `Loss` (`Grouping`) (č); krok, který prvky takového selektoru čte (lambda nad nimi, agregát bez argumentu), `Failure` (č) | VM | [103] |
| cíl EF Core: klíč-výraz, který žádná projekce nepojmenuje; dva sloupce klíče s vlastností téhož jména, které žádná projekce nepojmenuje; agregát přes celý výsledek bez seskupení | anonymní klíč by chtěl vymyšlené jméno (C# jméno člena dvakrát nevezme); LINQ agregát přes vše vysloví jen vykonávacím voláním | `Fallback` (z) | VM | [028], [113] |
| cíl LINQ (EF Core, druhý tvar NHibernatu): řazení podle aliasu projekce vedle klíče, který projekce nenese, v *neseskupeném* dotazu | řazení LINQ za projekcí zahodí to před ní; seskupený dotaz řadí nad skupinou před `Select` (alias rozepsaný na hodnotu projekce, klíče seskupení a agregáty skupiny), což EF Core 10.0.10 i provider 5.7.0 přeloží i s výřezem | s výřezem `Fallback` (z), u druhého tvaru NHibernatu `Omitted` (řádek níž); bez výřezu `Loss` (jen pořadí shod) | VM | [053], [113] |
| druhý tvar NHibernatu (LINQ): tytéž meze LINQ jako řádek výš; agregát nad distinktními hodnotami mimo `COUNT` (`SUM(DISTINCT …)`, `MAX(DISTINCT …)`) | provider 5.7.0 přeloží nad distinktními hodnotami jen `count` | `Omitted` (z), HQL stojí samo | VM | [118] |
| cíl EF Core: `SUM` ve skalárním poddotazu či nad hodnotou, která smí být `NULL`, jejíž typ součtu (`int`, `long`, `decimal`, `double`, `float`) mapování neurčí | `Sum` EF Core je nad žádnou hodnotou `0`; tvar, který tam dá `NULL` jako SQL (projekce nad jedinou skupinou, stráž počtu hodnot), potřebuje nullovatelný typ součtu | `Fallback` (`Aggregation`) (z); s typem se `SUM` nese se sémantikou SQL ([§5]) | VM | [155] |
| cíl EclipseLink: klíč seskupení s literálem | EclipseLink 5.0 váže každý literál jako parametr, klíč by se lišil od projekce (chyba 8120) | JPQL beze změny; metoda nastaví hint `eclipselink.jdbc.bind-parameters` = `false`, pod kterým ten jediný dotaz píše literály *i parametry* doslova (plán na hodnotu); holé JPQL hint nenese (z) | VM | [113] |
| `HAVING`, jehož první operand s proměnnou (čteno zleva; konstanta a parametr ji nemají) je cesta — holá, pod agregátem, operací či funkcí — přes jinou proměnnou než kořen bloku (entitní join, proměnná rozsahu, vnější blok), do EclipseLinku | EclipseLink 5.0.0 pak celou klauzuli přestaví na kořen a každou její cestu čte jako atribut kořene toho jména: kde ho kořen nemá, dotaz odmítne až za běhu (`Invalid query key`), kde ho má, vrátí jiné řádky bez chyby. Otevře-li klauzuli cesta přes kořen, `CASE` (LDBC IC 3, BI 16) či poddotaz, čte všechny cesty, jak stojí; projekci, `GROUP BY` a `ORDER BY` nepřestaví nikdy (změřeno katalogem FIBEN a javovou sadou) | `Fallback` (`PostAggregationFiltering`) (z); jinak JPQL | VM | [113], [149] |
| týž `HAVING` ve zdroji EclipseLink | implementace zdroje tu vrací řádky přestavěné klauzule, ne ty, které text říká | čte se podle textu a specifikace; cíl vrátí řádky, které text říká, ne ty, které vrátil zdroj (č) | VM | [113], [149] |
| rámec okna (`ROWS`, `RANGE`, `GROUPS`), pořadová funkce bez řazení, `DISTINCT` v okenním agregátu, jiná okenní funkce než tři pořadové a pět agregátů (`LAG`, `NTILE` …) | slovník nese jen pořadovou funkci s povinným řazením a agregát s volitelným; `DISTINCT` v okně SQL Server nepočítá | nečte se (`WindowFunction`): v projekci dotazu `Loss` (HQL `Failure`, 2.13), uvnitř odvozené tabulky či `WITH` `Failure`, protože dotaz kolem čte sloupec jménem | VM | [113], [161] |
| agregát nad agregátem jako argument okna (`AVG(COUNT(*)) OVER ()`) | žádný cíl ho nenapíše jinak než přes mezivýsledek | `Failure` (`Expression`) (č) | VM | [107], [161] |
| okenní agregát (`AVG(x) OVER (PARTITION BY …)`, s řazením běžící) do EF Core, NHibernatu a EclipseLinku | LINQ, HQL 5.7 a JPQL okno nemají | `Fallback` (`WindowFunction`) (z), jako u pořadových funkcí; T-SQL a HQL 7.4 píší | VM | [161] |
| průměr (`AVG`, s oknem i bez) do LINQ, HQL a JPQL | průměr modelu je `AVG` SQL (skalár argumentu, nad celým číslem useknutý podíl); LINQ a HQL Hibernatu průměrují celé číslo v plovoucí čárce, HQL a JPQL vydávají každý průměr jako `Double` | cíl převede svůj průměr do skaláru SQL — `(int)…Average`, `cast(… as int)`, `Integer`, `INTEGER`, desetinné do přesnosti 38 a měřítka `max(s, 6)` ([§5]) —, všude, kde průměr stojí; bez skaláru argumentu `Fallback` (`Aggregation`) (z); měřítko, které mapování neuvede, se bere jako nejvýš šest míst | VM | [161] |
| typ, v němž zdroj HQL či JPQL průměr vydává: holý průměr desetinného čísla (`Double`), holý průměr celého čísla v HQL NHibernatu a JPQL EclipseLinku (useknutý, `Double`) | model typ vydání nenese, jen výpočet SQL | čte se jako `AVG(x)`: cíl vydá tutéž hodnotu v typu SQL (`decimal`, `int`), jako šířku `COUNT` (č) | VM | [161] |
| průměr ve zdroji LINQ či HQL Hibernatu nad hodnotou, jejíž číslo mapování ani definice neuvede | hodnota průměru na typu závisí (celé číslo v plovoucí čárce, jiné jako SQL) | `Failure` (`Aggregation`) (č) | VM | [053], [161] |
| převod průměru, který průměr SQL neobnoví: do desetinného čísla s menším měřítkem, než má průměr SQL, do `BIGINT` či desetinného nad jiným typem | zaokrouhlil by průměr; převod do desetinného čísla slovník nenese | nečte se (`Expression`) | VM | [113], [161] |
| okenní funkce mimo projekci (ve filtru, pod agregátem, v klíči) | SQL ji tam nepřipustí; filtr nad ní jde přes mezivýsledek | `Failure` (`WindowFunction`) (b) | VM | [113] |
| LINQ `GroupBy(…).Select(g => g.OrderBy(…).First())` | jiný tvar než okenní funkce | jako okenní funkce se nečte | VM | [113] |
| `STRING_AGG` s oddělovačem, který není literál; LINQ `string.Join` mimo prvky skupiny | — | nečte se | VM | [113] |
| LINQ `string.Join` nad sloupcem, který smí mít `NULL` | C# bere `NULL` jako prázdný řetězec, `STRING_AGG` ho vynechá | `Loss` (č) | VM | [113] |
| cíl EF Core: seznam mimo prvky skupiny a mimo korelovaný poddotaz (řetěz z kořene dotazu), či nad hodnotou, která smí mít `NULL` (sloupec, výraz s takovým sloupcem, parametrem či poddotazem, `CASE` bez `ELSE`) | EF Core 10 spojí na klientovi nebo vloží prázdné řetězce; korelovaný poddotaz jde jako řetěz seskupený konstantou ([§6.2]) | `Fallback` (z) | VM | [113] |

### 2.10 Mezivýsledek a rekurze

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| definice, která čte dotaz kolem sebe (laterální odkaz, korelovaná odvozená tabulka) | slovník laterální odkaz nemá | `Failure` (č/b) v každém cíli | VM | [112] |
| definice nad celou entitou, se sloupcem bez jména či se dvěma sloupci jednoho jména | z definice by byla druhá entita; T-SQL i Hibernate to odmítají | `Failure` (`IntermediateResult`) (b) | VM | [112] |
| dvě definice téhož jména; alias odvozené tabulky shodný se čtenou tabulkou či jinou definicí; dopředný odkaz | vyzdvižená definice by tabulku zastínila; pořadí definic = pořadí závislostí | `Failure` (č/b) | VM | [112] |
| HQL `with x(a, b)`; `with` a poddotaz ve `from` v jednotce EclipseLinku; skládání nad projekcí v LINQ NHibernatu | jazyk ho nemá či provider nepřeloží | `Failure` (č) | VM | [112] |
| HQL nápověda `materialized` | mění plán, ne řádky | `Loss` (č) | VM | [112] |
| odvozená tabulka ve zdroji | model syntaxi nepamatuje | cíl SQL ji píše jako `WITH`, řádky tytéž | VM | [112] |
| porušené pravidlo rekurzivního `WITH` SQL Serveru: jiná operace než `UNION ALL` před rekurzivním členem, kotva jmenuje sebe, rekurzivní člen jmenuje definici jinak než jednou ve `FROM` či vnitřním joinu, v rekurzivním členu `DISTINCT`, seskupení, agregát, výřez, vnější join či poddotaz, jiný počet či skalár sloupců než kotva, vzájemná rekurze | SQL Server ho nespustí | `Failure` (`IntermediateResult`) (b) v každém cíli i na únikové cestě | VM | [113] |
| HQL `search` a `cycle` | SQL Server je nemá, slovník nenese | `Failure` (č) | VM | [113] |
| rekurzivní člen, který přičítá `COUNT` ke kotvě začínající konstantou | model typuje `COUNT` jako `Long`, T-SQL jako `int` | `Failure` (b), ač by ho SQL Server přijal (vědomě bezpečnější strana) | VM | [113] |
| délka řetězce a přesnost desetinného čísla mezi kotvou a rekurzivním členem | model je nenese | brána nehlídá; dotaz musí text srovnat převodem, jinak ho odmítne SQL Server | VM | [113] |
| limit rekurze bez rekurzivní definice | nic neohraničuje | vypustí se s `Convention` | VM | [§7] |
| mez hloubky, sledy místo vrcholů | rekurzivní člen SQL Serveru řádky neslučuje | obsah dotazu, ne mez nástroje; katalog LDBC proto nejkratší a nejlevnější cestu po `knows` (IC 13, IC 14, BI 15, BI 19, BI 20) nepíše rekurzí, ale řetězem nerekurzivních definic, jednou na úroveň prohledání do šířky, s hloubkou změřenou nad data setem SF 1 z každé osoby (5 / 5 / 11 / 5 / 7) — bez zjednodušení, drží `LdbcJudge/LdbcPathQueryTest` | — | [110], [113], [140], [141] |

### 2.11 Úniková cesta a nativní SQL

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| jméno entity, které parser JPQL cíle jako jméno nebere (EclipseLink 5.0.0: `from`, `where`, `table`, `union`, `left`, `set` …, jako cíl joinu i `select`, `member`, `case` …; Hibernate 7.4.5: `true`, `false`, `null`) | JPQL nemá kvalifikované ani uvozené jméno entity | `Fallback` (b/z) | VM | [113] |
| jméno entity, které je klíčovým slovem HQL, bez jmenného prostoru v pozici, kde ho NHibernate 5.7.0 nebere | HQL ho přečte jen kvalifikované | `Fallback` (z) | VM | [113] |
| vnější join uvnitř poddotazu; vnější join, jehož podmínka jmenuje alias vnitřního joinu — cíl EclipseLink | EclipseLink 5.0.0 entitní join poddotazu z SQL vypustí i s podmínkou (dotaz by vrátil jiné řádky bez chyby) — vnitřní join se proto píše jako proměnná rozsahu s podmínkou ve `where`, vnější join takový tvar nemá; vnitřní joiny píše za vnější (SQL Server podmínku odmítne); změřeno soudcem LDBC. Texty katalogu LDBC takový join nepíšou ([122]) | `Fallback` (`Subquery`, `Join`) (z) | VM | [113], [117], [122] |
| každý artefakt únikové cesty | je v SQL deklarovaného dialektu | vždy `Fallback` se jménem konstrukce a dialektu; platí jen pro SQL Server 2022; 3. stupeň = přijetí T-SQL, rozhoduje 4. | VO 5 | [086], [113] |
| kolekční parametr v nativním SQL EF Core | EF Core 10 naváže interpolovanou kolekci jako jednu hodnotu | `Failure` (`QueryParameter`) (z); Hibernate ho rozvine sám, NHibernate `SetParameterList`, metoda EclipseLinku ho rozvine za běhu na symbol za prvek ([123]); prázdná kolekce dá `IN ()`, které SQL Server odmítne, jako u Dapperu | VM | [113], [123] |
| množinová operace nad dvěma entitami, kde API materializuje jednu (`FromSql`, `AddEntity`, `createNativeQuery` s třídou) | řádky jedné strany by se vrátily jako cizí entita | `Failure` (z) | VM | [113] |
| sloupec výsledku bez jména či typu; u EF Core i dva sloupce téhož jména (bez ohledu na velikost písmen) a jméno, které není identifikátorem C# | EF Core plní třídu řádku podle jmen; NHibernate typ nevysloví | EF Core `Failure`; NHibernate čte bez `AddScalar` v typu ovladače s `Incompleteness` | VM | [113] |
| odmítnutí, která nejsou o jazyku cíle: laterální odkaz, nepojmenovaný sloupec definice, parametr bez skaláru, agregát nad agregátem, pravidla rekurze, `EXCEPT ALL`, `OFFSET` v operandu | neunese je SQL Server, nebo je nenese model | `Failure` i s únikovou cestou | VM | [113] |
| LINQ složený nad nativním SQL (`FromSql…`, `SqlQuery` s krokem nad sebou); `Database.ExecuteSql…` | EF Core ho spouští jako vlastní poddotaz; `ExecuteSql` je příkaz | `Failure` (`Filtering`) (č) | VM | [070], [113] |
| zástupné symboly NHibernatu (`{alias}`, `{alias.*}`) v nativním SQL | rozřeší je jen NHibernate | `Failure` (č) | VM | [082], [113] |
| mapování výsledku nativního dotazu (`AddScalar`, `AddEntity`, `AddJoin`, `SetResultTransformer`, `<return>`, druhý argument `createNativeQuery`, `addScalar`, `addEntity` a příbuzné Hibernatu) | dotaz mapování výsledku nenese, cíl ho odvodí z tabulky | `Loss` (č) | VM | [067], [113] |

### 2.12 Obecné pravidlo: dotaz, který by vrátil jiné řádky, se nevydá

Pod 2.4–2.11 stojí jedno pravidlo ([053], [065], [070]): **nepřečtená či nevyjádřená konstrukce, bez níž by dotaz vrátil jinou množinu řádků, odmítne artefakt**; parser čte dál, aby jmenoval všechny důvody. Bez níž jsou řádky tytéž, je `Loss`. Hranicí je množina řádků, ne podmínka ([065]).

| Krok LINQ | Co nástroj udělá |
|---|---|
| `OfType`, `SkipWhile`, `TakeWhile`, `DefaultIfEmpty` mimo korelovaný join, `GroupJoin`, `Zip`, koncové agregáty, `Any`, `All`, `Contains` (i asynchronně), temporální kroky EF Core; `Find` bez mapování entity, bez klíče v něm, s jiným počtem hodnot než částí klíče nebo mimo kořen | `Failure` (č) jmenovitě |
| krok, který parser nezná (`Include`, `Cast` …) | `Loss`; odmítnout každé neznámé volání by odmítlo i `Include` (VM, [070]) |

Úniková cesta se za překlad jazykem cíle nevydává nikdy: matice kategorií tvrdí `Fallback` u každého směru, který ho má mít (`AFallbackDirectionFallsBackAsStated` v obou sadách). Drží to `Combined/QueryFaithfulnessTest`.

### 2.13 Tichá místa

Fakt zmizí beze slova, ačkoli podle [004] a [048] zaznít má. Žádné rozhodnutí ticho neobhajuje, proto je vede [`open-items.md`](./open-items.md) jako práci.

Známé není žádné.

---

## 3. Sporná čtení zadání

**F8: „dynamicky parametrizované read-only dotazy" čteme jako parametr, ne jako dynamické SQL** ([084]). Parametr je hodnota od volajícího za běhu (`#{}`, kanonický `<foreach>`) a má protějšek ve všech šesti frameworcích ([083]). Dynamický příkaz (`<select>` s `<if>`, `<choose>`, `<bind>`) je **rodina příkazů** s různými množinami řádků: překlad jednoho člena by vydal jiné řádky ([053]), nést rodinu v IR by znamenalo pojem, který pět cílů zahodí, a artefakt za každého člena dává exponenciální počet se jmény, která zdroj neřekl ([028]); MyBatis → MyBatis by přestal být převodem (S2). Opačné čtení je jazykově možné, proto ho vedeme jako zúžení **Z F8** ([§9, *Co verze nárokuje*][§9n]); k úkolům záměru ho vztahuje [`traceability.md`, *Úkoly záměru*](./traceability.md#úkoly-záměru).

**T1 čteme v obou zněních najednou** ([174]): zmrazené zadání žádá open-source *aplikaci*, schválená specifikace „případovou studii **nebo testovací sadu**". Testovací sadou jsou katalogy LDBC a FIBEN nad svými databázemi se soudci ([110], [149]), případovou studií výňatky Keycloaku a Cofoundry ([138], [184]), které do experimentálního běhu ([173]) vstupují jako příklady celého projektu s měřením úplnosti mapování; jejich schémata — a schémata druhotných studií Ambari, Chinooku a JPetStore — vytvářejí sady prázdné v databázi případových studií jako katalog překladu, a projekt studie prochází čtvrtým stupněm jako *spustitelný* bez měřítka ekvivalence ([176]); řádky by dala jen data, která projekty nezveřejňují. Čísla zadání (≥ 15 entit, vztahy, složený klíč, ≥ 20 čtecích dotazů) splní z výňatků jen tyto dva a nárokuje se vstup, ne výsledek nad ním: test je spočítá z výňatku tak, jak ho přečte převod ([184]). „Lépe dvě" čteme jako druhou aplikaci, ne jako druhotné studie, které jsou menší a nesou zdrojové frameworky; druhá aplikace, Cofoundry nad EF Core, dává reálnému kódu zdroj i na straně .NET. WideWorldImporters, kterou záměr jmenuje jako referenční databázi, je databází systému a ukázek, ne experimentů.

**„Společnou podmnožinu" čteme jako to, co zachytí mezireprezentace a co jde ověřit napříč ekosystémy, ne jako průnik šesti dotazových jazyků.** Specifikace ji vymezuje dvěma podmínkami („kterou dokáže zachytit mezireprezentace a pro kterou lze ověřit odpovídající chování"). Slovník nese i konstrukce, které vyjádří jen část cílů — množinovou operaci, mezivýsledek, rekurzi, okenní funkci, agregaci do seznamu ([113]); cíl bez nich píše `Fallback` a směr se měří 4. stupněm proti témuž kanonickému výsledku, takže platí i druhá podmínka. Průnik by podmnožinu stáhl na HQL 5.7 (bez množinových operací, mezivýsledků a výřezu v textu) a katalog LDBC ([110]) by zůstal z větší části nepřeložený. Cenou je vazba artefaktu únikové cesty na SQL Server 2022 (VO 5).

---

## Jak se katalog udržuje

**Změna chování, která posune hranici** (přidá konstrukci, vysloví, zruší či změní mez), upraví katalog týmž krokem jako [`architecture.md`](./architecture.md) a řádek [`traceability.md`](./traceability.md). Zrušená mez zmizí (kdy a proč říká git a rozhodnutí); tiché místo zmizí s položkou, která ho odbaví. Tabulky 1.2–1.5 se opravují podle deskriptorů a manifestu kategorií, ne naopak.

[004]: ./decisions/004-unexpressible-facts-as-warnings.md
[005]: ./decisions/005-many-to-many-as-explicit-junction-entity.md
[006]: ./decisions/006-flat-composite-key-rendering.md
[007]: ./decisions/007-documentation-structure.md
[009]: ./decisions/009-target-framework-descriptor.md
[011]: ./decisions/011-key-generation-strategy-vocabulary.md
[012]: ./decisions/012-foreign-key-rendering.md
[014]: ./decisions/014-language-type-model.md
[015]: ./decisions/015-mapping-fact-completion-from-the-catalog.md
[017]: ./decisions/017-source-precedence-for-mapping-facts.md
[019]: ./decisions/019-neutral-database-type-vocabulary.md
[020]: ./decisions/020-canonical-generator-parameter-vocabulary.md
[021]: ./decisions/021-generator-name-selection.md
[024]: ./decisions/024-typed-query-operand.md
[026]: ./decisions/026-home-of-shared-query-reading.md
[028]: ./decisions/028-assembly-name-is-not-ours-to-invent.md
[030]: ./decisions/030-scope-of-version-1-0.md
[031]: ./decisions/031-key-class-as-declaration-of-key-parts.md
[035]: ./decisions/035-nhibernate-collections-declared-by-interface.md
[040]: ./decisions/040-boundary-of-the-handed-over-artifact.md
[045]: ./decisions/045-a-conversion-that-produced-nothing-says-so.md
[048]: ./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md
[050]: ./decisions/050-one-home-for-the-singular-plural-heuristic.md
[051]: ./decisions/051-like-pattern-translated-not-carried-over.md
[052]: ./decisions/052-literal-sql-type-reaches-the-ef-core-annotation.md
[053]: ./decisions/053-a-query-that-would-return-other-rows-is-not-emitted.md
[054]: ./decisions/054-nullable-key-part-is-a-reported-loss.md
[055]: ./decisions/055-unique-constraint-as-a-carried-mapping-fact.md
[060]: ./decisions/060-pagination-as-a-query-instruction.md
[061]: ./decisions/061-subquery-as-a-condition-operand.md
[062]: ./decisions/062-hql-read-by-a-hand-written-parser.md
[063]: ./decisions/063-stated-keylessness-as-a-carried-fact.md
[064]: ./decisions/064-absence-of-generation-as-a-catalog-fact.md
[065]: ./decisions/065-row-set-as-the-boundary-of-rule-053.md
[066]: ./decisions/066-records-attributed-to-the-input-unit.md
[067]: ./decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md
[068]: ./decisions/068-source-framework-precedence-orders-the-reading.md
[070]: ./decisions/070-a-parser-refuses-what-would-change-the-row-set.md
[071]: ./decisions/071-five-scalars-with-a-counterpart-in-both-ecosystems.md
[072]: ./decisions/072-a-transient-property-is-a-carried-mapping-fact.md
[073]: ./decisions/073-distinct-as-a-flag-of-the-query-scope.md
[074]: ./decisions/074-a-list-of-values-as-the-fourth-operand-shape.md
[075]: ./decisions/075-unknown-language-type-is-a-reported-incompleteness.md
[076]: ./decisions/076-java-wrappers-in-csharp-jvm-in-containers.md
[077]: ./decisions/077-hibernate-wrapper-over-the-shared-jpa-layer.md
[079]: ./decisions/079-fractional-second-precision-as-second-precision.md
[080]: ./decisions/080-eclipselink-as-the-second-profile-over-the-jpa-layer.md
[081]: ./decisions/081-a-unit-may-be-a-mapping-and-a-query-at-once.md
[082]: ./decisions/082-t-sql-read-and-written-by-a-shared-project.md
[083]: ./decisions/083-parameter-as-the-fifth-operand-shape.md
[084]: ./decisions/084-mybatis-wrapper-over-the-shared-sql-reading.md
[085]: ./decisions/085-a-row-count-is-a-number-or-a-parameter.md
[086]: ./decisions/086-target-database-dialect-declared-by-the-descriptor.md
[088]: ./decisions/088-a-declared-foreign-source-dialect-is-not-read.md
[089]: ./decisions/089-differential-verification-as-the-fourth-level-over-a-query.md
[091]: ./decisions/091-the-catalog-completes-a-foreign-source-and-says-so.md
[092]: ./decisions/092-input-nesting-depth-capped-before-the-descent.md
[093]: ./decisions/093-unreadable-input-is-a-unit-failure.md
[094]: ./decisions/094-entity-identity-inside-a-conversion.md
[101]: ./decisions/101-a-join-along-an-association-path-is-derived-from-the-relation.md
[102]: ./decisions/102-aggregate-distinct-like-escape-and-a-parameter-among-listed-values-are-carried.md
[103]: ./decisions/103-a-query-expression-is-read-as-its-rewrite-and-a-single-row-terminal-as-a-slice.md
[104]: ./decisions/104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md
[105]: ./decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md
[106]: ./decisions/106-a-bare-parameter-after-in-is-dappers-collection-parameter.md
[107]: ./decisions/107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md
[108]: ./decisions/108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md
[109]: ./decisions/109-a-code-unit-carries-every-query-it-hands-over.md
[110]: ./decisions/110-ldbc-snb-as-a-second-reference-domain.md
[111]: ./decisions/111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md
[112]: ./decisions/112-a-query-as-a-row-source-is-a-named-intermediate-result.md
[113]: ./decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md
[114]: ./decisions/114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md
[115]: ./decisions/115-explicit-loading-is-the-query-the-provider-composes.md
[116]: ./decisions/116-concurrency-check-is-the-version-the-application-keeps.md
[117]: ./decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md
[118]: ./decisions/118-nhibernate-writes-a-linq-form-beside-its-hql.md
[119]: ./decisions/119-quantified-comparison-as-a-comparison-with-a-quantifier.md
[120]: ./decisions/120-a-set-operation-as-the-body-of-a-subquery-operand.md
[121]: ./decisions/121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md
[122]: ./decisions/122-the-text-of-a-catalog-query-takes-the-shape-a-targets-implementation-needs-under-the-same-criterion.md
[123]: ./decisions/123-a-native-query-of-eclipselink-expands-a-collection-parameter-at-run-time.md
[128]: ./decisions/128-a-csharp-unit-with-a-syntax-error-is-refused-with-a-position.md
[129]: ./decisions/129-sql-server-2022-stays-the-only-database-dialect.md
[130]: ./decisions/130-a-unique-constraint-over-a-foreign-key-names-the-navigation.md
[131]: ./decisions/131-a-column-alias-in-a-typed-dapper-call-states-the-column-of-the-property.md
[132]: ./decisions/132-ef-core-fluent-configuration-is-read-as-a-bounded-syntactic-subset.md
[133]: ./decisions/133-an-add-on-package-over-a-framework-is-not-that-framework.md
[134]: ./decisions/134-a-property-without-a-setter-gets-the-narrowest-setter-its-framework-writes-through.md
[136]: ./decisions/136-a-project-archive-is-unpacked-by-the-browser-with-own-code.md
[138]: ./decisions/138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md
[139]: ./decisions/139-hibernate-states-the-limit-of-recursion-as-a-hint-on-the-query-object.md
[140]: ./decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md
[141]: ./decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md
[143]: ./decisions/143-a-named-query-of-jpa-is-read-where-it-is-defined.md
[144]: ./decisions/144-a-path-through-an-association-outside-join-is-an-implicit-join-or-the-foreign-key-column.md
[148]: ./decisions/148-a-reference-in-a-linq-operand-is-an-implicit-join-whose-kind-the-provider-takes-from-the-nullability-of-the-foreign-key.md
[149]: ./decisions/149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md
[151]: ./decisions/151-a-reference-in-a-linq-operand-joins-left-where-its-row-may-be-absent-under-both-providers.md
[154]: ./decisions/154-the-intermediate-representation-is-not-shown-to-the-user.md
[152]: ./decisions/152-an-association-path-that-cannot-be-derived-refuses-the-query-in-every-clause.md
[155]: ./decisions/155-sql-sum-in-ef-core-linq-is-the-projection-of-one-group-and-ef-core-sum-is-coalesce.md
[156]: ./decisions/156-a-linq-predicate-carries-sql-three-valued-logic-in-both-directions.md
[158]: ./decisions/158-a-property-declared-dbset-declares-a-set-whatever-supplies-its-value.md
[161]: ./decisions/161-a-window-aggregate-is-a-window-of-the-vocabulary-and-the-whole-row-of-a-definition-beside-columns-is-its-columns.md
[162]: ./decisions/162-a-subquery-stands-in-a-join-condition-as-in-any-condition.md
[169]: ./decisions/169-a-fixed-width-value-is-the-value-sql-returns-in-every-target.md
[170]: ./decisions/170-rows-tied-on-the-ordering-key-compare-as-a-set-and-a-fiben-slice-gets-a-deciding-key.md
[173]: ./decisions/173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md
[174]: ./decisions/174-the-reference-databases-of-the-experiments-are-three-by-role-and-the-case-study-of-t1-is-the-keycloak-excerpt-judged-to-the-third-level.md
[176]: ./decisions/176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md
[177]: ./decisions/177-a-property-of-an-excluded-construct-leaves-the-model-and-ef-core-does-not-map-a-navigation-without-a-principal-key.md
[178]: ./decisions/178-a-reference-navigation-without-a-relation-stays-an-unmapped-member-of-the-class-and-is-reported.md
[179]: ./decisions/179-eclipselink-carries-no-version-over-a-binary-column-so-the-store-generated-version-is-a-read-only-column-with-a-loss.md
[180]: ./decisions/180-a-full-outer-join-into-eclipselink-goes-out-in-native-sql.md
[181]: ./decisions/181-an-alias-in-a-mybatis-select-with-a-result-type-states-the-column-of-the-property.md
[184]: ./decisions/184-t1-leaves-the-exempt-area-in-a-narrower-sense-the-case-study-meets-the-criteria-counted-from-the-excerpt-and-cofoundry-becomes-its-second-application.md
[185]: ./decisions/185-the-name-of-an-ef-core-set-names-the-entity-the-context-of-the-conversion-declares-through-one-reading-context-of-both-passes.md
[§1v]: ./architecture.md#zafixované-verze
[§4.1]: ./architecture.md#41-entity-a-mapování
[§4.2]: ./architecture.md#42-primární-klíč
[§4.3]: ./architecture.md#43-vztahy
[§4.4]: ./architecture.md#44-dotazové-instrukce-a-podmínkový-strom
[§5]: ./architecture.md#5-parsery-a-buildery--jak-fungují-dnes
[§5.1]: ./architecture.md#51-diagnostika-převodu
[§5.2]: ./architecture.md#52-doplňování-mapovacích-faktů-z-katalogu
[§6.2]: ./architecture.md#62-ověření-generovaných-artefaktů
[§7]: ./architecture.md#7-rozhraní-parserů-a-builderů
[§9]: ./architecture.md#9-co-tahle-verze-nárokuje-a-co-je-vyňaté-ze-záruk
[§9h]: ./architecture.md#hranice-záruk
[§9n]: ./architecture.md#co-verze-nárokuje
