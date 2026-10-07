# Společná podmnožina a její hranice

Odpověď na „přeloží nástroj tohle?", konstrukce po konstrukci: **část 1** vymezuje společnou podmnožinu, kterou žádá [`specifikace.tex`](./specifikace.tex), **část 2** případy bez úplného nebo jednoznačného překladu, které slibuje [`zamer.tex`](./zamer.tex), **část 3** dvě sporná čtení zadání.

Žánr jako [`architecture.md`](./architecture.md) (rozh. [007]): mechanismus je tam (odkazy „§"), důvody v rozhodnutích, nárok v [§9] a kanonicky v *Guarantees* kořenového [`README.md`](../README.md). Při rozporu katalogu s §9 platí §9, u tabulek 1.2–1.5 deskriptory (`*Descriptor.cs`) a manifest kategorií.

**Záznamy** ([§5.1]); **strana**: **č** čtení (parser zdroje), **b** brána (šablona builderu pro všechny cíle, [§7]), **z** zápis (krok či visitor cíle).

| Záznam | Význam |
|---|---|
| `Failure` | artefakt nevznikne, záznam jmenuje konstrukci; dotaz, který by vrátil jiné řádky, se nevydá ([053], [070]) |
| `Loss` | artefakt vznikne bez konstrukce; řádky dotazu tytéž |
| `Fallback` | cíl napíše celý dotaz v SQL dialektu přes API svých nativních dotazů ([113]); řádky tytéž, vazba na SQL Server 2022, měří se 4. stupněm |
| nečte se | čtečka konstrukci nepřevezme: v podmínce `Failure`, v projekci a klíči řazení `Loss`, vždy jmenovitě ([070]) |
| `Convention` · `Incompleteness` · `Conflict` | výstup tvrdí, co zdroj neřekl · chybí fakt, generování pokračuje · dva zdroje faktu se rozcházejí |

**Druh hranice** (rozh. [030]):

| Druh | Význam |
|---|---|
| **VO n** vyňatá oblast | oblast bez záruky ([§9, *Hranice záruk*][§9h]); překladu se týkají VO 2 a 5, VO 1 a 6 (Advisor, experimenty) ne |
| **Z Fx** zúžení nároku | požadavek nárokovaný v užším čtení ([§9, *Co verze nárokuje*][§9n]) |
| **VM** vyslovená mez | konstrukce uvnitř nároku, kterou nástroj z rozhodnutého důvodu (model fakt nenese, jazyk cíle tvar nemá, dotaz by vrátil jiné řádky) odmítne, vypustí nebo napíše nativním SQL a ozve se záznamem |

**Tichá místa** (2.13) hranicí nejsou: fakt zmizí beze slova, ač má zaznít ([004], [048]); jsou to otevřené položky.

---

## 1. Kladná půlka: co se překládá

### 1.1 Šest frameworků

| Framework | Verze | Ekosystém | Entita a mapování | Dotazový jazyk | Jazyky zdrojové jednotky | Nativní SQL v cíli |
|---|---|---|---|---|---|---|
| Dapper | 2.1.79 | .NET | C# třída bez atributů | SQL dialektu (`SqlMapper`, holý skript) | `CSharp`, `SqlQuery` | — (jazykem je SQL) |
| EF Core | 10.0.10 | .NET | C# třída s datovými anotacemi | LINQ (`IQueryable`) | `CSharp` | `DatabaseFacade.SqlQuery`, `DbSet.FromSql` |
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
| druh joinu (vnitřní, levý, pravý, plný) | jazyk | jazyk | jazyk; plný složením `LeftJoin`, `Concat`, `RightJoin` ([065]) | jazyk; plný SQL | jazyk | jazyk; vnější s podmínkou na alias vnitřního joinu SQL |
| join uvnitř poddotazu | jazyk | jazyk | jazyk | jazyk | jazyk | SQL |
| množinová operace | jazyk | jazyk | jazyk | SQL | jazyk | jazyk |
| mezivýsledek (`WITH`, odvozená tabulka) | jazyk | jazyk | jazyk (proměnná metody) | SQL | jazyk | SQL |
| rekurze | jazyk | jazyk | SQL | SQL | jazyk; s limitem SQL | SQL |
| seskupení podle výrazu | jazyk | jazyk | jazyk | jazyk | jazyk | jazyk; klíč s literálem SQL |
| okenní funkce řazení | jazyk | jazyk | SQL | SQL | jazyk | SQL |
| agregace do seznamu | jazyk | jazyk | jazyk nad prvky skupiny a sloupcem bez `NULL`, jinak SQL | SQL | jazyk | SQL |
| funkce slovníku | všech 17 | všech 17 | všech 17 | bez `DateAdd`, `DateDiff` | všech 17 | bez `DateAdd`, `DateDiff`, `Cast` |

**Funkce** (`QueryFunction`): `Upper`, `Lower`, `Trim`, `Substring`, `Length`, `Coalesce`, `Abs`, `Year`, `Month`, `Day`, `CurrentTimestamp`, `EscapePattern` ([107]); `DateAdd`, `DateDiff` (jednotky rok … sekunda), `Round`, `Sqrt`, `Cast` do `Int`, `Long`, `Float`, `Double`, `String` ([113]). Funkci mimo deskriptor cíle píše cíl SQL. **Okenní funkce řazení:** `ROW_NUMBER`, `RANK`, `DENSE_RANK` nad oknem s povinným řazením, jen v projekci.

Strojově: deskriptory (`QuerySupport`, `Functions`, `NativeSqlApi`), `Combined/TargetFrameworkDescriptorTest`. Dapper a MyBatis nemají kam ustoupit (jazykem je SQL), odmítnou jen to, co brány odmítají všem cílům.

### 1.3 Kategorie, na kterých je podmnožina změřená

40 kategorií T2, každá dotazem nad sdílenou doménou sedmi entit v každém zdrojovém jazyce, který ji vysloví; manifest `Tests/Database/QueryShapes/categories.txt` čtou obě sady ([§6.2]). Každá jde každým směrem: 1. stupeň `Combined/QueryShapeMatrixTest`, 2. nad SQL a .NET cíli, 2. a 3. nad javovými (`shapes/QueryCategoryTest`), 4. diferenčně proti kanonickému výsledku (`Tests/Database/Differential/matrix.txt`, [089]). Cíle mimo sloupec *Fallback* píšou kategorii jazykem; odmítnutý směr (`refusedBy`) není žádný.

| Kategorie | Zdroje, které ji vysloví | `Fallback` (`fallbackBy`) |
|---|---|---|
| `Projection`, `Filtering`, `JoinOverTwoColumns`, `AggregationGroupingAndHaving`, `Ordering`, `SubqueryAsTheRightSideOfIn`, `CorrelatedExistsOverThreeColumns`, `ScalarSubquery`, `DistinctProjection`, `InOverAListOfValues`, `ConstantOfAMoment`, `LikeWithAnAnchoredPattern`, `CountOverDistinctValues`, `LikeWithAnEscapedWildcard`, `LikeWithABoundPrefix`, `ArithmeticInAProjection`, `FunctionInAFilter`, `CoalesceInAFilter`, `CaseInAProjection`, `OrderingByAnAggregate`, `ScalarSubqueryAgainstABoundValue`, `GroupingByAnExpression`, `RoundingAndSquareRoot`, `OuterJoinWithAFilterInOn`, `JoinBeyondEqualities` | všech šest | — |
| `ScalarParameter`, `CollectionParameter`, `InListWithABoundValue` | všech šest, Dapper jen s katalogem¹ | — |
| `PaginationWithBoundCounts` | bez NHibernatu a EclipseLinku | — |
| `SetOperation` | bez NHibernatu | NHibernate |
| `GroupingOverAGroupedResult`, `IntermediateResultReadTwice`, `DateArithmetic` | bez NHibernatu a EclipseLinku | NHibernate, EclipseLink |
| `ListAggregation` | bez NHibernatu a EclipseLinku | NHibernate, EclipseLink; ze zdroje MyBatis bez katalogu i EF Core² |
| `AggregateOverTheWholeResult` | bez EF Core | EF Core |
| `CastInAConcatenation` | bez EclipseLinku | EclipseLink |
| `NativeSqlInCode` | EF Core, NHibernate, Hibernate, EclipseLink | — |
| `RecursiveDescentOfAHierarchy`, `BestRowPerGroup` | Dapper, Hibernate, MyBatis | EF Core, NHibernate, EclipseLink |
| `RecursiveWalkOfACyclicGraph` | Dapper, MyBatis | EF Core, NHibernate, Hibernate, EclipseLink |

¹ `refusedWithoutCatalog`, jediné odmítnutí manifestu: bez katalogu nemá parametr zdroje Dapper skalár (2.4); s katalogem, který tabulku má, se typuje do všech cílů ([105], [106]).
² `fallbackWithoutCatalog`: zdroj MyBatis neříká, že sloupec nesmí mít `NULL`.

**Mimo kategorie.** *Záměrně špatný dotaz* (osm poddotazů do čtyř úrovní, joiny přes dva a tři sloupce, seskupení s `HAVING`, řazení, výřez) jde ze všech šesti zdrojů do všech šesti cílů (`Combined/DeeplyNestedQueryTest`, javově `shapes/DeeplyNestedQueryTest`). *Katalog LDBC* — 41 čtecích dotazů LDBC SNB ([110]) — měří úplnost: přeloženo všech 41, šest se zjednodušením, které katalog u dotazu vyslovuje; odmítá jen EclipseLink BI 12 (seznamový parametr v nativním SQL). `SampleData/LdbcSnbSample`, koncový bod `/ldbc`, `Combined/LdbcCatalogTest` (1. stupeň, artefakty EF Core a NHibernate i 2.). Stav „podle specifikace" drží u 18 dotazů Interactive validační sada LDBC na 4. stupni ([117], `LdbcJudge/LdbcValidationTest`, javové `ldbc/`); u 17 dotazů BI je tvrzením autora — validační sadu pro BI nad daty Interactive LDBC nevydává.

### 1.4 Proč u kategorie chybí zdroj

Chybějící zdroj v 1.3 je mez jazyka zdroje, ne nástroje:

| Zdroj | Co jeho jazyk nevysloví |
|---|---|
| HQL 5.7 (NHibernate) | výřez v textu (je na objektu dotazu, `SetFirstResult`, `SetMaxResults`, čte se z kódu), množinovou operaci, `WITH`, odvozenou tabulku, rekurzi, datumovou aritmetiku, okenní funkci, agregaci do seznamu; LINQ NHibernatu mezivýsledek nečte, provider ho do HQL nepřeloží |
| JPQL (EclipseLink) | totéž kromě množinové operace, kterou vysloví; výřez je na objektu dotazu, poddotaz ve `FROM` je kartézský součin za čárkou, který model nenese, `cast` předá EclipseLink 5.0 SQL Serveru jménem javového typu |
| HQL 7.4 (Hibernate) | limit rekurze |
| LINQ (EF Core) | agregát přes celý výsledek (jen voláním, které dotaz vykoná), rekurzi, okenní funkci |
| SQL (Dapper, MyBatis) | nativní SQL v kódu — jejich jazykem už je |

### 1.5 Slovník mapování

Entita ([§4.1]): třída s vlastnostmi a jejich jazykovými fakty (typ, nullabilita, modifikátory, inicializátor), databázový typ jako rodina s facetami, primární klíč o 1…*n* částech se strategií generování a klíčovou třídou ([§4.2]), vztahy na entitě se jménem cíle, rolí a páry sloupců, N:M jako spojovací entita ([§4.3]). Jak cíl fakt vyjádří (`MappingFactCategory`, `FactSupport`); prázdná buňka = nevyjádří, a nese-li fakt model, `Loss`; **vyžaduje** = bez faktu `Failure`:

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
| sloupec verze | | | `[Timestamp]`; číselná a datočasová `[ConcurrencyCheck]` — jako zdroj jen jediný token třídy nad `short`/`int`/`long`/`DateTime` ([116]) | `<version>` | `@Version` |
| unikátní omezení | | | `[Index(IsUnique = true)]` | `unique`, `unique-key` | `@UniqueConstraint`, `unique` |
| transientní vlastnost | | vynechání (`autoMapping="false"`) | `[NotMapped]` | vynechání | `@Transient` |

Co zdroj Dapper či MyBatis nevysloví, doplní katalog ([§5.2], [015]) nebo konvence cíle s `Convention`. **Vynucené členy** (`EnforcedMembers`, [009]): NHibernate `virtual`, u složeného klíče `[Serializable]`, `Equals`, `GetHashCode`; JPA nefinální třída a pole, bezparametrický konstruktor, u složeného klíče vnořená `@IdClass` se `Serializable`, `equals`, `hashCode`; MyBatis třída bez konstruktoru; EF Core `[Keyless]` u entity bez klíče; Dapper nic.

### 1.6 Po frameworcích

Co je frameworku vlastní ([§5]); odmítnutí, ztráty a nativní SQL jsou v části 2.

| Framework | Jako zdroj čte | Jako cíl vydá |
|---|---|---|
| **Dapper** | třídu jen jazykovými fakty (klíč, tabulku, sloupce, vztahy doplní katalog, F6); dotaz z holé jednotky SQL, kde je dotazem každý `SELECT` ([108]), a z každého volání `SqlMapper` (`Query…`, `QueryMultiple` jako skript, `Execute…`; [109]), sdílenou čtečkou T-SQL ([082]); `IN @ids` = kolekční parametr, `IN (@ids)` = jedna hodnota ([106]) | třídu bez atributů, klíče a vztahů s mechanickým `Loss` za každý fakt; metodu nad `connection.Query<T>` a holé SQL, projekci jako `List<dynamic>` ([104]) |
| **EF Core** | anotace `[Table]`, `[Key]`, `[PrimaryKey]`, `[Keyless]`, `[Column]`, `[MaxLength]`, `[StringLength]`, `[Unicode]`, `[Precision]`, `[Required]`, `[DatabaseGenerated]`, `[Timestamp]`, `[ConcurrencyCheck]` ([116]), `[NotMapped]`, `[Index]`, `[ForeignKey]`, `[InverseProperty]` a konvence jako tvrzení zdroje (klíč `Id`, navigace s dohledaným cizím klíčem, N:M; [067]); `DbContext`, `DbSet<T>` entitou nejsou. Řetěz LINQ nad kořenem ([026], [103]), o kořeni rozhoduje, co jednotka o jménu uvádí ([114]); explicitní načtení navigace jako dotaz nad cílovou entitou ([115]); nativní SQL v `SqlQuery`, `SqlQueryRaw`, `FromSql…` jako celý dotaz ([113]) | třídu s datovými anotacemi (fluent API ne); metodu vracející `IQueryable` nad `ctx.Set<T>()`, mezivýsledek jako proměnnou metody, join nad rámec rovností klíčů filtrem spojované posloupnosti či korelovaným `SelectMany`; nativně `ctx.Database.SqlQuery<Row>` s třídou řádku, `FromSql` u celé entity |
| **NHibernate** | C# třídu s odloženým `virtual`, po ní `hbm.xml` ([017], [068]) v ploché třídě: `<class>`, `<id>` s `<generator>`, `<composite-id>` s `<key-property>` a `<key-many-to-one>`, `<property>`, `<version>`, `<timestamp>`, `<many-to-one>`, `<one-to-one>`, `<set>`, `<bag>`, `<list>`. Holé HQL (vlastní parser, [062]), `<query>` a `<sql-query>` s typem z `<query-param>`, kód (LINQ nad `session.Query<T>()`, `CreateSQLQuery`, `CreateQuery`, výřez `SetFirstResult`, `SetMaxResults`) | třídu s `virtual`, kolekce jako `IList`, `ISet` ([035]), `hbm.xml` bez `assembly` ([028]), generátor podle [021]; metodu nad `session.CreateQuery` s výřezem na objektu dotazu a holé HQL; vedle nich druhý tvar téhož dotazu jako metodu vracející `IQueryable` nad `session.Query<T>()` (`CSharpLinqQuery`, [118]) — levý join `GroupJoin` s `DefaultIfEmpty()`, řazení pod `DISTINCT` před projekcí, `COUNT` nad sloupcem s `NULL` jako `Sum` nad podmínkou, `LIKE` s escapovaným jádrem přes `Like` z `NHibernate.Linq`; nativně `session.CreateSQLQuery` s `AddEntity` či `AddScalar` |
| **Hibernate** ([077]) | sdílenou vrstvu JPA: anotace a `orm.xml` (nad anotacemi, `metadata-complete` je vypne), přístup podle `@Access` či místa `@Id`, `@IdClass`, `@EmbeddedId`, generátory s kanonickými parametry ([020]), vztahy s `@JoinColumn` i bez (výchozí `<atribut>_<sloupec klíče>` nad jednodílným klíčem), `@MapsId`, `@JoinTable`; navíc `@Nationalized`. JPQL vlastním parserem včetně joinu po asociační cestě ([101]) a výřezu na objektu dotazu; z HQL 7.4 `limit`, `offset`, `with` i rekurzivní, poddotaz ve `from`, okenní funkce, `listagg`, `timestampadd`, `timestampdiff`; nativní SQL v `createNativeQuery` | anotovanou javovou třídu (`orm.xml` ne), `AUTO` jako sekvenci `<Entita>_SEQ`; metodu nad `em.createQuery` (`TypedQuery<E>` či `Query`) a holé JPQL |
| **EclipseLink** ([080]) | tutéž vrstvu bez rozšíření: `@Nationalized` nezná, unicode jen z doslovného `columnDefinition`; JPQL bez `limit`, `with`, oken, `listagg`, datumové aritmetiky | jako Hibernate s profilem EclipseLinku: každý název explicitně (implicitní píše velkými písmeny), `AUTO` jako tabulku čítače `SEQUENCE`, nationalizaci doslovným `columnDefinition`, žádný `cast` |
| **MyBatis** ([084]) | doménovou třídu; rozhraní mapperu (`@Results`, `@Result`, `@One`, `@Many`, `@Select`, `@Param`); XML mapper (`<resultMap>` s `extends`, `<id>`, `<result>`, `<association>`, `<collection>`; `<select>` po rozvinutí `<include>`, `<where>`, `<set>`, `<trim>`; kanonický `<foreach>` = kolekční parametr, `#{}` = parametr); text sdílenou čtečkou T-SQL; skalár parametru z podpisu metody, `parameterType` či `javaType` | POJO bez importu z frameworku a bez konstruktoru, mapper s `<resultMap autoMapping="false">`; hlavičku metody s `@Param` a `<select>`; kolekci jako kanonický `<foreach>` (jediná vydávaná dynamická značka) |

---

## 2. Záporná půlka: případy bez úplného nebo jednoznačného překladu

Řádek: *konstrukce → proč → co nástroj udělá (záznam, strana) → druh hranice → rozhodnutí*.

### 2.1 Vyňaté oblasti

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| dědičnost, komponenty, spojené tabulky: NHibernate `<subclass>`, `<joined-subclass>`, `<union-subclass>`, `<component>`, `<dynamic-component>`, `<join>`, `<natural-id>`, `<idbag>`, `<array>`, `<primitive-array>`, `<any>`, kolekce hodnot či komponent; bázový typ C# třídy, který jmenuje jinou entitu převodu (EF Core = table per hierarchy); JPA `@Inheritance`, `extends` mezi dvěma `@Entity`, `@MappedSuperclass`, `@Embedded`, `@ElementCollection`, `@SecondaryTable`; EF Core `[Owned]`, `[ComplexType]`, primitivní kolekce (`List<string>`: prvek skalár či klíčové slovo C#, jeden sloupec JSON) | model nese jen plochou třídu | `Loss` u každého prvku (č); hierarchie = nesouvisející entity; `@MappedSuperclass` s vlastním záznamem = samostatná entita, jejíž atributy dědící entity nedostanou | VO 2 | [030], [048] |
| dialekt: jediný je SQL Server 2022 z deskriptoru; zdroj s jiným deklarovaným dialektem; katalog u něj; `CHECK` a výchozí hodnota sloupce jako doslovný SQL výraz | verze nese jeden dialekt | cizí dialekt zastaví čtení doslovného SQL (`Failure`) i doslovného typu sloupce (`Loss`, `DatabaseType`); katalog fakta dodá s jediným `Conflict`; `CHECK`, `default` `Loss`; artefakt únikové cesty vázaný na dialekt | VO 5 | [086], [088], [091], [055] |

`CHECK` a výchozí hodnotu řadí do VO 5 §9; rozhodnutí 055 je za vyňatou oblast nemělo. Platí §9.

### 2.2 Mapování

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| líné načtení reference v EclipseLinku (`fetch = LAZY` na `@ManyToOne`, `@OneToOne`) | bez weavingu tiše eager; model strategii nenese | `Loss` (č, zdroj EclipseLink); artefakt `fetch` nevyslovuje | Z F9 | [076], [080] |
| načítání a kaskády (`fetch`, `cascade`, `orphanRemoval`, NHibernate `inverse` a `cascade` kolekce) | do mapovací IR nepatří (článek, §5.4) | `Loss` (č), `fetch` JPA mlčky; NHibernate píše `inverse` jen odvozené, `cascade` nikdy | VM | [014], [035], [077] |
| pořadí a tvar kolekce: `@OrderColumn`, indexový sloupec `<list>`, `<map>` | kolekce bez indexu; mapy mimo typový model | `Loss` (č); NHibernate píše jen `<set>`, `<bag>` | VM | [014], [035] |
| klíčová třída jako vnořený klíč (`@EmbeddedId`, `<composite-id class>`) | cíle píšou klíč ploše | jméno třídy se nese, změna formy `Loss`; člen, který mapování nejmenuje, `Loss`; třída s vlastním mapováním `Conflict` | VM | [006], [031] |
| `<key-many-to-one>` | klíč plochý, vztah vedle | plochý klíč a vlastnící N:1, změna formy `Loss`; bez sloupců `Incompleteness` | VM | [006] |
| strategie generování, kterou cíl nevyjádří: identita, sekvence, hi/lo, UUID, increment v anotacích EF Core; strategie části `<composite-id>` jiná než přiřazená či neuvedená; `Increment` v JPA; generátor či parametr bez protějšku | anotace EF Core řeknou jen „generuje databáze" nebo „nic"; jinde protějšek chybí | `Loss` (z); rozklad `AUTO` podle profilu a dosazené `assigned` jsou `Convention` | VM | [011], [020], [021], [064], [077], [080] |
| nullabilita části klíče (`int? Id`) | klíč nesmí být `NULL` | EF Core, NHibernate typ zploští s `Loss` (`Nullability`); JPA píše obalový typ vždy | VM | [054] |
| nullable sloupec za nenullovatelnou vlastností, cíl EF Core | `IsRequired(false)` je jen ve fluent API | `Loss` (z) | VM | [§5] |
| entita bez primárního klíče | NHibernate a JPA klíč vyžadují | `Failure` do NHibernatu, Hibernatu, EclipseLinku; EF Core `[Keyless]`; Dapper, MyBatis prostá třída | VM | [063] |
| neunikátní index (`index`, `[Index]` bez `IsUnique`) | výkonnostní artefakt | `Loss` (č); JPA `@Table(indexes)` mlčí (2.13) | VM | [055] |
| unikátní omezení nad vlastností, která v cíli není sloupcem (část klíče, navigace, verze v NHibernatu; transientní vlastnost) | `unique` na `<many-to-one>` = 1:1; transientní nemá sloupec | `Loss` (z); nad nedeklarovanou vlastností `Incompleteness`; vícesloupcové bez jména dostane jméno s `Convention` | VM | [055], [072] |
| druhý sloupec verze; verze na části klíče, navigaci či transientní vlastnosti (NHibernate) | `<version>` je jeden, nad obyčejnou vlastností | `Loss` (z) | VM | [030] |
| verze, kterou zvyšuje framework (JPA `@Version int`, NHibernate `<version>` nad `Int32`, `<timestamp>`), cíl EF Core | anotace inkrementaci nevyjádří; `[Timestamp]` = hodnota z databáze | `[ConcurrencyCheck]` a `Loss` (z), zvyšuje aplikace; týmž tvarem EF Core `[Timestamp]` nad `ulong` (typ mimo slovník) | VM | [004], [030], [075] |
| verze, kterou udržuje aplikace (EF Core `[ConcurrencyCheck]`), cíl NHibernate, Hibernate, EclipseLink | `<version>` a `@Version` zvyšuje framework sám | `<version>`/`@Version` a `Convention` (z): inkrementace přechází na framework; do EF Core doslovně beze záznamu | VM | [116] |
| `[ConcurrencyCheck]` mimo podmínky: druhý token třídy, vedle `[Timestamp]` na jiné vlastnosti, nad `Guid`, řetězcem, `decimal`, `byte[]` … | token chrání sloupec a verzí není; cíle vedou verzi jen nad celočíselným či datočasovým typem | `Loss` kategorie `VersionColumn` s jmenovanou podmínkou (č) | VM | [116] |
| druhý a další `<column>` vlastnosti | chtělo by kompozitní uživatelský typ | `Loss` u každého (č) | VM | [030] |
| hodnota `property-ref` | builder ji odvodí ze vztahu | `Loss` (č); inverzní 1:1 bez vlastnické protistrany `Incompleteness` | VM | [012] |
| N:M bez jména spojovací tabulky či sloupců, N:M entity na sebe, implicitní spojovací tabulka EF Core | spojovací entitu není z čeho postavit | zůstane N:M s `Incompleteness`; s katalogem se spojovací entita syntetizuje | VM | [005], [015], [067] |
| EF Core: stínový cizí klíč, kompozitní cizí klíč bez explicitního párování, vztah 1:1 | anotace páry ani 1:1 nevysloví | vztah bez sloupců, cíl je vynechá s `Convention`, katalog doplní; 1:1 se čte jako N:1 | VM | [012], [015], [067] |
| výchozí sloupec cizího klíče JPA bez `@JoinColumn` nad složeným klíčem, přes `@JoinTable`, s `referencedColumnName` mimo klíč či k cíli mimo převod (nad jednodílným klíčem se `<atribut>_<sloupec klíče>` čte jako tvrzení zdroje) | nad složeným klíčem jméno specifikace nedefinuje a implementace se liší; jinde chybí, z čeho odvodit | páry prázdné pro katalog; cíl je vynechá či odvodí s `Convention`; join po asociační cestě bez katalogu `Failure` | VM | [067], [101] |
| MyBatis `<association>`, `<collection>`, `<id>` | vztah nese tvar výsledku, ne cizí klíč; `<id>` je identita řádku výsledku | navigace bez sloupců (doplní katalog); `<id>` → `Incompleteness` (`PrimaryKey`), MyBatis → MyBatis bez katalogu klíč neudrží | VM | [084] |
| typ mimo slovník: neznámý databázový typ, NHibernate `Currency`, dvojice neregistrovaná v NHibernate 5.7 (`DateOnly` nad `datetime2`), neznámý jazykový typ (`Instant`, `java.util.Date`, mapa), přesnost u JPA pod jménem jiné rodiny | slovník typů je uzavřený | neznámý typ `Incompleteness` s doslovným typem na únikové cestě; `Currency` jako `Decimal(19,4)` s `Loss`; ostatní `Loss` | VM | [019], [052], [071], [075], [079] |
| nationalizace v EclipseLinku | `@Nationalized` nezná | ze zdroje `Loss`; cíl doslovný `columnDefinition`, nad rodinou bez národní varianty `Loss` | VM | [080] |
| modifikátory a inicializátory přes hranici ekosystémů | druhý jazyk modifikátor nemá, inicializátor píše jinak | `virtual`, `override`, `sealed`, `new`, `required` do Javy odpadnou mlčky, ostatní a javové `final`, `volatile` `Loss`; inicializátor jen literál psaný v obou stejně, jinak `Loss`; `sealed`, `partial`, `abstract` třídy model nenese | VM | [076], [077] |
| fakt, který cíl podle deskriptoru nevyjádří (Dapper 13, MyBatis 9) | tvrzení o frameworku | mechanický `Loss` za každý fakt modelu | VM | [004], [009], [084] |
| atributy NHibernate bez místa v modelu: `<property>` `formula`, `access`, `insert`, `update`, `lazy`, `generated`, `optimistic-lock`; `<class>` `discriminator-value`, `where`, `mutable`, `optimistic-lock`, `dynamic-insert`, `dynamic-update`, `batch-size`, `lazy` | model je nenese; `formula`, `where` mění, co entita čte | `Loss` (č); ostatní atributy mlčí (2.13) | VM | [048] |
| anotace mimo podmnožinu: neznámá EF Core, JPA `@Enumerated`, `@Lob`, `@NamedQuery`, anotace výrobce Hibernatu (kromě `@Nationalized`) a EclipseLinku | model fakt nenese | `Loss` se jménem anotace (č) | VM | [048], [077], [080] |
| fluent konfigurace EF Core (`OnModelCreating`) | nenárokovaná jednotka, otevřená otázka (*Zbytky*) | nečte se; kontext vyloučen z entit s `Loss`, který to jmenuje | VM | [111] |
| Dapper.Contrib (`[Table]`, `[Key]`, `[ExplicitKey]`), alias v SQL jako zdroj mapování Dapperu | rozsah nerozhodnut (*Zbytky*) | nečte se: atributy mlčky, alias se nepáruje, katalog páruje podle jména | VM | [015], [067] |

### 2.3 Jednotka a předání dotazu

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| jednotka deklarovaná rolí (`CSharpEntity`, `JavaQuery` …), v jazyce, který zdroj nečte, nebo z níž nic nevzešlo | jednotka deklaruje jen jazyk | `Failure` o jednotce (orchestrace) | VM | [045], [066], [111] |
| aplikační kód, který dotaz nepředává (služba volající repozitář, DTO projekce) | od entity se nerozezná | čte se jako entita | Z F14 | [111] |
| procházení repozitáře, archiv projektu; jméno jednotky na artefaktu; zobrazení mezireprezentace | vstup vybírá uživatel; sloučená entita nepatří jedné jednotce | nenárokuje se | Z F14 | [066], [094], [111] |
| příkaz, který není čtecí `SELECT` (`INSERT`, `UPDATE`, `DELETE`, `DECLARE`, `SET`, `USE`, `SELECT … INTO`, `SELECT @x = …`) | mění stav, ze kterého čtou ostatní dotazy | `Failure` celé holé jednotky, u vloženého textu toho dotazu | VM | [108] |
| druhý `SELECT` v textu vloženém do hostitele (Dapper mimo `QueryMultiple`, `<select>`, `<sql-query>`) | hostitel pošle jeden příkaz s jedním výsledkem | `Failure` dotazu | VM | [108], [109] |
| `FOR XML`, `FOR JSON`, `TABLESAMPLE`, `WITH XMLNAMESPACES` | dokument či vzorek, ne řádky | `Failure` (č) | VM | [§5] |
| proměnná dotazu přiřazená víckrát či podmíněně, kterou kód prodlužuje; objekt dotazu, se kterým kód pokračuje v jiném příkazu | dotaz se skládá za běhu | `Failure` (č); výsledek a objekt opouštějící jednotku se nesledují | VM | [109], [113] |
| text dotazu, který není literál (Dapper, JPA, NHibernate) | není co číst | `Incompleteness`, dotaz nevznikne (`Failure`: nenese instrukce) | VM | [026], [109] |
| dotaz skládaný za běhu: `QueryOver`, `CreateCriteria`; EF Core: díra interpolace spočtená na místě, argument `SqlQueryRaw`, který není jméno, `@x` v textu | nemá text, nebo hodnota vzniká za běhu | `QueryOver`, `CreateCriteria` `Failure`, zbytek `Incompleteness` | VM | [113] |
| předání jménem: `GetNamedQuery`, `createNamedQuery` | dotaz je v definici | `Incompleteness`; definici NHibernatu v `hbm.xml` čte, **JPA ne**: `@NamedQuery`, `<named-query>` jsou `Loss`, pojmenovaný dotaz JPA se nepřekládá nikde | VM | [081], [113] |
| volby pojmenovaného dotazu `hbm.xml` (`cacheable`, `cache-region`, `cache-mode`, `fetch-size`, `timeout`, `flush-mode`, `read-only`, `comment`, nativně `callable`, `<synchronize>`); `<query-param>` se skalárem mimo slovník (`XmlDoc`, uživatelský typ) | provedení, ne řádky; skalár mimo slovník model nenese | `Loss` (č); hodnota, kterou volba má i nevyslovená (`cacheable="false"`), se nehlásí; `callable` ztratí značku a text se čte, volání (`exec`) odmítne čtení | VM | [048], [083] |
| prvek v `<query>` či `<sql-query>`, který schéma `hbm.xml` nepřipouští; týž `<query-param>` dvakrát se dvěma skaláry | text potomka by mohl být kusem dotazu; parametr nemá dva typy | `Failure` (č) | VM | [053], [083] |
| kořen EF Core `x.M`, kde jednotka uvádí pro `x` typ mapovaný jako entita; člen typu `IQueryable<T>`, přes který se dotaz skládá | entita `DbSet` nemá; člen se nesleduje | `Failure` (č) | VM | [114] |
| navigace entity z jiného souboru bez mapovací jednotky; proměnná z volání, které jednotka nedeklaruje (`repository.Load(id)`) | jednotka o jménu nic neuvádí, rozhoduje místo | čte se podle místa jako dotaz; s mapovací jednotkou `Failure` | VM | [114] |
| explicitní načtení nad jménem, které jednotka neuvádí, s navigací deklarovanou víc entitami či žádnou; navigace, kterou text nefixuje; M:N; bez mapovací jednotky | filtr z mapování, nic se nehádá | `Failure` jmenovitě; builder EF Core explicitní načtení nepíše (řetěz nad `Set<T>()`); `NHibernateUtil.Initialize` se nečte | VM | [115] |
| dynamický příkaz MyBatisu (`<if>`, `<choose>`, `<when>`, `<otherwise>`, `<bind>`) | rodina příkazů s různými řádky (část 3) | `Failure` se jménem značky | Z F8 | [084] |
| `<foreach>` mimo kanonický tvar, `${}`, `#{a.b}`, `@` už ve zdrojovém SQL, `@SelectProvider`; týž příkaz v anotaci i v XML | substituce není hodnota; cestu signatura nevysloví; dvě definice odmítne MyBatis sám | `Failure` (č); `jdbcType`, `typeHandler`, `mode` v `#{}` `Loss` | VM | [084] |
| zápisový příkaz MyBatisu (`<insert>`, `<update>`, `<delete>`, `@Insert`, `@Update`, `@Delete`); escape JDBC `{call …}`; jazykový ovladač jiný než XML (`lang`, `@Lang`) | zápis ani volání procedury není čtecí dotaz; jiný ovladač čte text jinak | `Failure` (č) se jménem příkazu, escape či ovladače | VM | [053], [084], [109] |
| volby příkazu MyBatisu: atributy `<select>` mimo `id`, `parameterType`, `resultType`, `resultMap` (`statementType`, `fetchSize`, `timeout`, `useCache`, `flushCache`, `resultSetType`, `resultOrdered`, `resultSets`, `affectData`, `parameterMap`, `databaseId`), `@Options`, `@MapKey` | provedení či uspořádání výsledku, ne řádky | `Loss` (č); hodnota, kterou volba má i nevyslovená, se nehlásí; u `statementType="CALLABLE"` rozhoduje text: dotaz se přeloží, volání (`EXEC`, `{call …}`) odmítne čtení | VM | [048], [084] |
| volby předání v kódu: volání na objektu dotazu JPA mimo parametry, výřez a vykonání (`setHint`, `setFlushMode`, `setTimeout`, `setCacheStoreMode` …), i zřetězená a na proměnné; Dapper `buffered`, `commandTimeout` a argument bez místa v přetížení; NHibernate `UniqueResult`, `SetTimeout` a jiné neznámé volání na objektu dotazu; zámek (`setLockMode`, `setHibernateLockMode`, `eclipselink.pessimistic-lock`) | provedení, ne řádky; zámek řádky nemění | `Loss` (č) jmenovitě; navázání parametru, objekt parametrů, transakce a vykonávací volání (`getResultList`, `getSingleResult`, `list` …) nic | VM | [048], [109] |
| mapování výsledku v kódu: transformery Hibernate (`setTupleTransformer`, `setResultListTransformer`, `setResultTransformer`), `eclipselink.result-type`, multi-mapping Dapperu (`map`, `splitOn`, `types`) | dotaz mapování výsledku nenese, cíl řádek odvodí | jeden `Loss` (`Projection`) (č) | VM | [067], [109] |
| volání a nápovědy, které mění řádky: Hibernate `setPage`, `getKeyedResultList`, `getResultCount`; EclipseLink `eclipselink.jdbc.max-rows`, `eclipselink.jdbc.first-result`, `eclipselink.history.as-of` (i `.scn`), `eclipselink.query-type` | model je nenese, bez nich jiné řádky | `Failure` (č), i v pozdějším příkazu; pod Hibernatem je nápověda EclipseLinku `Loss` | VM | [070], [109] |
| `commandType` Dapperu `StoredProcedure`, `TableDirect` nebo spočtený za běhu | text jde jako jméno procedury či tabulky, nebo se rozhodne za běhu | `Failure` (č) se jménem typu | VM | [053], [109] |
| vstup zanořený hlouběji než strop (výchozí 128, nastavuje provozovatel) | sestup by mohl shodit proces | `Failure` s řádkem a sloupcem před sestupem; XML strop nemá | VM | [092] |
| nečitelný text jednotky | — | `Failure` s řádkem a sloupcem u SQL, Javy, HQL, JPQL, XML; u C# bez pozice (otevřené, *Zbytky*) | Z S7 | [093] |

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
| entity join HQL či JPQL bez `with`/`on`; nepřečtená podmínka joinu; poddotaz v `ON` | join filtruje i násobí; LINQ poddotaz do klíčových selektorů nevepíše | `Failure` (č/b) | VM | [070], [061] |
| `fetch` u joinu | mění načítání, ne řádky | `Loss` (č) | VM | [070], [101] |
| asociační cesta, kterou mapy převodu nerozresolvují (entita, vztah či cíl mimo převod, prázdné páry vztahu i vlastnící protistrany); cesta přes víc asociací či přes N:M | podmínka se odvozuje z mapování, nic se nehádá; mezičlánek by chtěl vymyšlený alias | `Failure` (`Join`) se jménem cesty (č: HQL, JPQL, LINQ) | VM | [101] |
| join po asociační cestě ve zdroji | builder cestu nevydává | entity join s odvozenou podmínkou, řádky tytéž | VM | [101] |
| LINQ `GroupJoin` (`join … into`), `Zip`; `Join` se selektory, které nejsou lambdy či se nespárují | model je nenese | `Failure` (`Join`) (č) | VM | [103], [113] |
| result selector LINQ joinu, který vynechá řádek, vrátí jen jednu stranu nebo celý řádek vedle sloupců; `SelectMany` s jedním argumentem | řádky tytéž, sloupce ne | `Loss` (`Projection`) (č) | VM | [070] |
| spojovaná posloupnost s jiným krokem než `Where` | je to dotaz sám o sobě | `Failure` (`IntermediateResult`) (č) | VM | [113] |
| cíl EF Core: pravý či plný vnější join s podmínkou nad rámec rovností klíčů; podmínka joinu se sloupcem bez tabulky | filtr posloupnosti by ubral zachované řádky; sloupec nejde přiřadit straně | `Fallback` (z) | VM | [113] |
| cíl NHibernate: plný vnější join | HQL 5.7 ho nemá ani množinové operace na složení | `Fallback` (z) | VM | [065], [113] |
| druhý tvar NHibernatu (LINQ): pravý a plný vnější join | provider 5.7.0 zná `Join` a levý join jako `GroupJoin` s `DefaultIfEmpty()`; `LeftJoin` a `RightJoin` .NET 10 nepřekládá | `Omitted` (z), HQL stojí samo | VM | [118] |
| sloupec cizího klíče bez skalární vlastnosti mimo podmínku joinu po asociaci: cíl EclipseLink vždy, cíl NHibernate u části složeného klíče | cesta přes referenci (`p.customer.id`) je v nich vnitřní join a ztratí řádky s `NULL` | `Fallback` (z) | VM | [113] |
| navigace v operandu: LINQ `o.Customer.Name`, `x.o.Customer.Name`, `o.Customer != null`; JPQL cesta přes referenci v projekci (`select o.customer.id`) | join po cestě se v LINQ píše druhým `from` nad kolekcí; model operand přes referenci nenese | nečte se (ve filtru `Failure`, v projekci a agregátu `Loss`) (č) | VM | [070], [101] |
| JPQL a HQL srovnání celé entity jinak než `asociace = řádek` (s parametrem, hodnotou, jiným řádkem, kolekcí, vztahem bez sloupců) | model operand entity nemá; `p.customer = c` se čte jako rovnosti klíče | nečte se (`Failure`) (č) | VM | [101] |
| JPQL a HQL: test celé entity na `NULL` jinde než nad vlastnící jednohodnotovou asociací se známými sloupci — kolekce, inverzní reference, `c is null` nad proměnnou, reference bez sloupců | model nenese nulovost řádku ani prázdnost kolekce; inverzní reference je nepřítomnost řádku na druhé straně | `Failure` (č) | VM | [101] |

### 2.7 Poddotazy, množinové operace a stránkování

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| poddotaz v operandu s tělem množinové operace; `IN` či skalární porovnání s poddotazem, který nemá právě jednu projekci | operand nese jeden `SELECT`; SQL by druhé odmítlo za běhu | `Failure` (b) | VM | [061] |
| řazení uvnitř poddotazu či těla definice bez výřezu | T-SQL ho tam nepřipouští, řádky nemění | `Loss` | VM | [061], [112] |
| výřez v poddotazu či operandu množinové operace do HQL a JPQL; literálový počet řádků nad `Int32` do EF Core, NHibernatu a JPA | text výřez nemá, je na objektu dotazu; API jsou 32bitová | `Fallback` (z) | VM | [060], [113] |
| cíl EF Core: skalární poddotaz, který není jediným neseskupeným agregátem; `IN` nad neseskupeným agregátem; množinová operace nad různými typy prvků | `First()` by tiše vybral řádek; LINQ různé typy nesloží | `Fallback` (z) | VM | [113] |
| LINQ metoda na jeden řádek v pozici operandu (`… == ctx.Orders.Select(…).FirstOrDefault()`) | skalární poddotaz s výřezem nemá v modelu výrobce | nečte se | VM | [103] |
| `INTERSECT ALL`; `EXCEPT ALL` do T-SQL a LINQ | slovník `INTERSECT ALL` nemá; `EXCEPT ALL` SQL Server odmítá a LINQ nemá | `INTERSECT ALL` `Failure` (č); `EXCEPT ALL` `Failure` (z) T-SQL i na únikové cestě, cíle JPA ho píšou | VM | [§4.4] |
| řazení či projekce za množinovou operací | model za operací nic nenese; řádky tytéž | `Loss` (č) | VM | [053], [070] |
| filtr, join, seskupení či výřez za množinovou operací; `OFFSET` v jejím operandu | bez nich jiné řádky; T-SQL výřez v operandu nezapíše | `Failure` (č/z) | VM | [053], [060], [113] |
| počet řádků, který není číslo ani parametr (`TOP (@n + 1)`, spočtený za běhu); `TOP … PERCENT`, `WITH TIES`, `TOP` vedle `OFFSET` | počet řádků je číslo, nebo parametr | `Failure` (`Pagination`) (č) | VM | [060], [085] |
| výřez z výřezu, `Skip` za `Take`, krok, který s výřezem nekomutuje; `Last`; výřez na objektu dotazu vedle `limit` | výřez je poslední operací rozsahu, model nese jeden | `Failure` (č/b); za výřezem projektovaných řádků se krok čte jako mezivýsledek | VM | [060], [103], [112] |
| `First`, `Single`, `ElementAt` a jejich `…OrDefault` | čtou se jako výřez jednoho řádku | `Convention`; artefakt vrací seznam o nejvýš jednom prvku, u `Single` druhý řádek nekontroluje | VM | [103] |

### 2.8 Výrazy a funkce

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| funkce mimo slovník: `CONVERT`, `REPLACE` mimo vzorek, `LEFT`, `RIGHT`, `SYSDATETIME()`, `DateTime.UtcNow`, HQL `str()`, `year()` v JPQL a cokoli mimo 17 `QueryFunction` | uzavřený slovník; funkce vstupuje se zápisem v T-SQL a ověřeným zápisem v každém cíli | nečte se (`Expression`) | VM | [107], [113] |
| převod do jiného typu než `INT`, `BIGINT`, `REAL`, `FLOAT`, `NVARCHAR(MAX)`, s délkou či do neunicode textu; `DATEADD`, `DATEDIFF` s týdnem, čtvrtletím či milisekundou; `ROUND` se třetím argumentem | mohly by změnit hodnotu; třetí argument mění zaokrouhlení na ořez | nečte se | VM | [113] |
| `+`, jehož ani jedna strana není typovaná | cíle ho hláskují různě (konkatenace, nebo sčítání) | `Failure` (`Expression`) (b) | VM | [107] |
| aritmetika, `COALESCE`, `CASE` nad skaláry, které se nesjednotí (`Decimal` s `Float`) | C# nepřeloží, T-SQL porovná jinou hodnotu | `Failure` (b) | VM | [107] |
| agregát nad agregátem (`SUM(COUNT(*))`), i do seznamu | žádný cíl ho nezapíše | `Failure`: přímo vnořený (`MAX(SUM(x))`) při čtení (č), uvnitř výrazu bránou (b); přes odvozenou tabulku se nese | VM | [107], [112] |
| výraz v projekci bez aliasu | sloupec bez jména nečte žádný cíl, jméno se nevymýšlí | `Failure` (b); konstanta bez aliasu `Loss` (č) | VM | [107], [028] |
| výraz jako prvek výčtu či počet řádků | ty mají vlastní uzavřené tvary | `Failure` (č) | VM | [074], [085], [107] |
| konkatenace nad sloupcem ve zdroji LINQ | C# bere `null` jako prázdný řetězec, SQL, HQL i JPQL vracejí `NULL` | `Loss` (č); artefakt má sémantiku SQL | VM | [107] |
| druhý tvar NHibernatu (LINQ): konkatenace v projekci nad textem, který smí být `NULL`; skalární poddotaz, který není jediným agregátem; výřez nad `Int32` | provider 5.7.0 projekci vyhodnotí na klientovi, kde `null` je prázdný řetězec; obecné meze LINQ jako u EF Core | `Omitted` (z), HQL stojí samo | VM | [118] |
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
| druhý tvar NHibernatu (LINQ): tytéž meze LINQ jako řádek výš; agregát nad distinktními hodnotami mimo `COUNT` (`SUM(DISTINCT …)`, `MAX(DISTINCT …)`) | provider 5.7.0 přeloží nad distinktními hodnotami jen `count` | `Omitted` (z), HQL stojí samo | VM | [118] |
| cíl EclipseLink: klíč seskupení s literálem | literál naváže jako parametr, seskupení se liší od projekce | `Fallback` (z) | VM | [113] |
| okenní agregát (`SUM(…) OVER`), rámec okna, okno bez řazení, jiná okenní funkce než tři řadicí | slovník nese jen řazení nad oknem s povinným řazením | nečte se (`WindowFunction`) | VM | [113] |
| okenní funkce mimo projekci (ve filtru, pod agregátem, v klíči) | SQL ji tam nepřipustí; filtr nad ní jde přes mezivýsledek | `Failure` (`WindowFunction`) (b) | VM | [113] |
| LINQ `GroupBy(…).Select(g => g.OrderBy(…).First())` | jiný tvar než okenní funkce | jako okenní funkce se nečte | VM | [113] |
| `STRING_AGG` s oddělovačem, který není literál; LINQ `string.Join` mimo prvky skupiny | — | nečte se | VM | [113] |
| LINQ `string.Join` nad sloupcem, který smí mít `NULL` | C# bere `NULL` jako prázdný řetězec, `STRING_AGG` ho vynechá | `Loss` (č) | VM | [113] |
| cíl EF Core: seznam mimo prvky skupiny, nad korelovaným poddotazem či nad sloupcem, který smí mít `NULL` | EF Core 10 spojí na klientovi nebo vloží prázdné řetězce | `Fallback` (z) | VM | [113] |

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
| mez hloubky, sledy místo vrcholů | rekurzivní člen SQL Serveru řádky neslučuje | obsah dotazu, ne mez nástroje; katalog LDBC ji u IC 13, IC 14, BI 15, BI 19, BI 20 vyslovuje jako zjednodušení | — | [110], [113] |

### 2.11 Úniková cesta a nativní SQL

| Konstrukce | Proč | Co nástroj udělá | Druh | Rozh. |
|---|---|---|---|---|
| jméno entity, které parser JPQL cíle jako jméno nebere (EclipseLink 5.0.0: `from`, `where`, `table`, `union`, `left`, `set` …, jako cíl joinu i `select`, `member`, `case` …; Hibernate 7.4.5: `true`, `false`, `null`) | JPQL nemá kvalifikované ani uvozené jméno entity | `Fallback` (b/z) | VM | [113] |
| jméno entity, které je klíčovým slovem HQL, bez jmenného prostoru v pozici, kde ho NHibernate 5.7.0 nebere | HQL ho přečte jen kvalifikované | `Fallback` (z) | VM | [113] |
| join uvnitř poddotazu; vnější join, jehož podmínka jmenuje alias vnitřního joinu — cíl EclipseLink | EclipseLink 5.0.0 join poddotazu z SQL vypustí i s podmínkou (dotaz by vrátil jiné řádky bez chyby) a vnitřní joiny píše za vnější (SQL Server podmínku odmítne); změřeno soudcem LDBC | `Fallback` (`Subquery`, `Join`) (z) | VM | [113], [117] |
| každý artefakt únikové cesty | je v SQL deklarovaného dialektu | vždy `Fallback` se jménem konstrukce a dialektu; platí jen pro SQL Server 2022; 3. stupeň = přijetí T-SQL, rozhoduje 4. | VO 5 | [086], [113] |
| kolekční parametr v nativním SQL EF Core a EclipseLinku | EF Core 10 naváže interpolovanou kolekci jako jednu hodnotu, nativní dotaz EclipseLinku 5.0 seznam nerozvine | `Failure` (`QueryParameter`) (z); Hibernate ho rozvine | VM | [113] |
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
| `OfType`, `SkipWhile`, `TakeWhile`, `DefaultIfEmpty` mimo korelovaný join, `GroupJoin`, `Zip`, koncové agregáty, `Any`, `All`, `Contains` (i asynchronně), temporální kroky EF Core | `Failure` (č) jmenovitě |
| krok, který parser nezná (`Include`, `Cast` …) | `Loss`; odmítnout každé neznámé volání by odmítlo i `Include` (VM, [070]) |

Úniková cesta se za překlad jazykem cíle nevydává nikdy: matice kategorií tvrdí `Fallback` u každého směru, který ho má mít (`AFallbackDirectionFallsBackAsStated` v obou sadách). Drží to `Combined/QueryFaithfulnessTest`.

### 2.13 Tichá místa

Fakt zmizí beze slova, ačkoli podle [004] a [048] zaznít má. Žádné rozhodnutí ticho neobhajuje, proto je vede [`open-items.md`](./open-items.md) jako práci.

| Tiché místo | Kategorie v `open-items.md` |
|---|---|
| čtení dotazů: žádné známé | — |
| EF Core: skalární forma `[ForeignKey("Navigace")]` na vlastnosti cizího klíče | *Užitečné, ne nutné* |
| atributy na třídách a vlastnostech zdroje Dapper a NHibernate | *Užitečné, ne nutné* |
| pole C# třídy | *Užitečné, ne nutné* |
| JPA `@Column(table, insertable, updatable)` | *Užitečné, ne nutné* |
| JPA `@Table(indexes, catalog)` a `<index>` v `orm.xml` | *Užitečné, ne nutné* |
| JPA `referencedColumnName` v `@JoinColumn` | *Užitečné, ne nutné* |
| MyBatis `<cache>`, `<cache-ref>`, `<parameterMap>`, `typeHandler` na `<result>` | *Užitečné, ne nutné* |
| bázový typ C# či Java třídy, který jmenuje třídu mimo převod | *Užitečné, ne nutné* |
| cíl JPA: kolekční vlastnost bez vztahu jako `@Column` — sloupec, který zdroj netvrdil (Hibernate ho přijme jako `xml`, EclipseLink jako serializovaný `IMAGE`) | *Užitečné, ne nutné* |
| zbylé atributy ostatních prvků NHibernate mapování | *Užitečné, ne nutné* (vlastní položka) |

---

## 3. Sporná čtení zadání

**F8: „dynamicky parametrizované read-only dotazy" čteme jako parametr, ne jako dynamické SQL** ([084]). Parametr je hodnota od volajícího za běhu (`#{}`, kanonický `<foreach>`) a má protějšek ve všech šesti frameworcích ([083]). Dynamický příkaz (`<select>` s `<if>`, `<choose>`, `<bind>`) je **rodina příkazů** s různými množinami řádků: překlad jednoho člena by vydal jiné řádky ([053]), nést rodinu v IR by znamenalo pojem, který pět cílů zahodí, a artefakt za každého člena dává exponenciální počet se jmény, která zdroj neřekl ([028]); MyBatis → MyBatis by přestal být převodem (S2). Opačné čtení je jazykově možné, proto ho vedeme jako zúžení **Z F8** ([§9, *Co verze nárokuje*][§9n]); k úkolům záměru ho vztahuje [`traceability.md`, *Úkoly záměru*](./traceability.md#úkoly-záměru).

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
[045]: ./decisions/045-a-conversion-that-produced-nothing-says-so.md
[048]: ./decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md
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
