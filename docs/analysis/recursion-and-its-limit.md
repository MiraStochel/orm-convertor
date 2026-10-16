# Rekurze a limit rekurze

Podklad k jedné konstrukci napříč šesti frameworky a ke katalogu LDBC: co rekurzivní `WITH` v SQL Serveru umí a neumí, co dělá `OPTION (MAXRECURSION n)`, jak rekurzi a limit vysloví každý zdrojový framework a co z toho nástroj přeloží, proč katalog LDBC pět dotazů zjednodušuje a kudy by se dalo dojít blíž specifikaci. Popis nástroje samého je v [`architecture.md`](../architecture.md) (§ *Rekurze*), hranice podmnožiny v [`subset.md`](../subset.md) 2.10, volby v rozhodnutích [113](../decisions/113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) a [139](../decisions/139-hibernate-states-the-limit-of-recursion-as-a-hint-on-the-query-object.md); tady je jen to, co k nim slouží jako podklad.

**Odkud čísla.** Překlady jsou změřené nástrojem ve stavu po rozhodnutí 139, data nad `LdbcSnb` ve scale factoru 1 (rozh. [110](../decisions/110-ldbc-snb-as-a-second-reference-domain.md)) v lokální instanci SQL Serveru 2025 Express, 2026-10-15. Jsou to **počty** — řádky, osoby, vzdálenosti —, které na stroji nezávisí; časy tu nejsou, protože se instance Express pro měření T nehodí. Co je převzaté z dokumentace a neověřené sondou, je tak označené.

---

## 1. Rekurze v SQL Serveru

Rekurzivní definice je `WITH x AS (kotva UNION ALL rekurzivní člen)`, kde rekurzivní člen čte `x`. SQL Server opakuje rekurzivní člen nad řádky, které vydala předchozí úroveň, dokud nevydá žádný. Všechna pravidla, která brána šablony hlídá, vede [`subset.md`](../subset.md) 2.10; pro tenhle dokument jsou podstatná tři:

| Pravidlo rekurzivního členu | Důsledek |
|---|---|
| žádné `DISTINCT`, `GROUP BY`, agregát, `TOP`, vnější join, poddotaz | řádky jedné úrovně nejde sloučit ani porovnat s dřívějšími úrovněmi |
| definici čte právě jednou | úroveň vidí jen svůj řádek předchůdce, ne celou množinu dosažených vrcholů |
| sloupce mají přesně typ kotvy | počítadlo kroků i cesta skládaná jako text se v obou členech převádějí stejně |

Z prvních dvou plyne to podstatné: **rekurzivní `WITH` SQL Serveru nevyjmenovává vrcholy, ale sledy.** Hledání v grafu nemůže vynechat osobu, kterou už potkalo, a hlídání cyklu textem cesty (kategorie `RecursiveWalkOfACyclicGraph`) zabrání jen tomu, aby se do osoby vrátil týž sled — ne tomu, aby do ní dorazil jiný.

### Limit rekurze

| Fakt | Hodnota | Odkud |
|---|---|---|
| zápis | `OPTION (MAXRECURSION n)` na konci příkazu, jeden na příkaz, ne na definici | dokumentace; čtečka i zapisovač T-SQL (113) |
| výchozí limit | 100 úrovní | sonda 2026-10-15: řetěz 150 hran bez limitu skončí chybou 530 po 100 |
| bez limitu | `MAXRECURSION 0` | sonda: týž řetěz vrátí 150 řádků |
| nejvyšší hodnota | 32 767; větší odmítne už překlad příkazu (chyba 310) | sonda |
| co `n` dovolí | právě `n` rekurzivních úrovní: hledání omezené na `Steps < 2` doběhne pod `MAXRECURSION 2`, pod `MAXRECURSION 1` skončí chybou 530 | sonda nad `LdbcSnb` |
| překročení | chyba 530 a **celý příkaz skončí bez výsledku**; nic se neořízne | sonda |

Limit rekurze proto **není mez hloubky**. Mez hloubky je podmínka v textu dotazu (`WHERE w.Steps < 2`), která úroveň ukončí tím, že nevydá řádky; limit je pojistka, která dotaz zastaví chybou, když ji podmínka nezastaví dřív. Dotaz, který se má zastavit sám, potřebuje mez hloubky; limit mu jen dovolí jít hlouběji než sto úrovní (0, 200 …), nebo ho pojistí přísněji. Proto ho nástroj nese jako fakt dotazu a nikde ho nezahodí (113).

---

## 2. Příklad: kdo je od osoby nejvýš dva kroky

Jádro IC 13 a IC 14 nad skutečnými tabulkami `LdbcSnb`: procházka po `Person_knows_Person` od jedné osoby s počítadlem kroků, mezí hloubky dva a limitem deset. Nejmenší počet kroků ke každé osobě se počítá **mimo** rekurzi, protože uvnitř seskupovat nejde.

```sql
WITH Walk AS (
    SELECT p.Id AS PersonId, 0 AS Steps
    FROM Person AS p
    WHERE p.Id = 4398046513938
    UNION ALL
    SELECT k.Person2Id, w.Steps + 1
    FROM Walk AS w
    JOIN Person_knows_Person AS k ON k.Person1Id = w.PersonId
    WHERE w.Steps < 2)
SELECT w.PersonId AS PersonId, MIN(w.Steps) AS Distance
FROM Walk AS w
GROUP BY w.PersonId
OPTION (MAXRECURSION 10)
```

Nad SF 1 vrátí **4 557 osob** z **9 312 řádků** definice (1 kotva + 80 sledů délky 1 + 9 231 délky 2). Osoba 4398046513938 je příkladová osoba katalogu, s osmdesáti přáteli. Identifikátor stojí v textu jako literál, ne jako parametr: parametr zdroje Dapper dostane skalár jen z katalogu (rozh. [105](../decisions/105-a-query-formulates-its-own-demand-on-the-catalog.md)), takže by text bez katalogu nešel přeložit — s katalogem nad `LdbcSnb` stačí napsat `@personId`.

---

## 3. Jak rekurzi a limit vysloví zdrojové frameworky

| Zdroj | Rekurze | Limit rekurze | Kde je potíž |
|---|---|---|---|
| **Dapper** | jazykem: T-SQL je jazyk zdroje | `OPTION (MAXRECURSION n)` v textu | žádná; parametr bez katalogu nemá typ (105) |
| **MyBatis** | jazykem: T-SQL v `<select>` | `OPTION (MAXRECURSION n)` v textu | `<` se v XML píše `&lt;` (nebo `CDATA`) |
| **EF Core 10** | **LINQ ji nemá**: definice je proměnná metody a proměnná nemůže jmenovat sebe; jen nativní SQL přes `Database.SqlQuery<T>` | v textu SQL | nad `FromSql` s `WITH` nejde skládat LINQ — EF Core by text obalil poddotazem a SQL Server `WITH` v poddotazu nepřijme; `SqlQuery` bez skládání projde beze změny (`RecursionTest.EFCoreTakesTheRecursiveQueryThroughSqlQuery`, ověřeno proti 10.0.10) |
| **NHibernate 5.7** | **HQL nemá ani `WITH`**, LINQ provider také ne; jen `CreateSQLQuery` | v textu SQL | typy sloupců výsledku nese až `AddScalar` (cíl je píše) |
| **Hibernate 7.4** | jazykem: HQL `with x as (… union all …)`; klauzule `search` a `cycle` nástroj odmítá jmenovitě, protože je SQL Server nemá (113) | **HQL ho v textu nemá**; `.unwrap(org.hibernate.query.Query.class).addQueryHint("MAXRECURSION n")` na objektu dotazu, který dialekt SQL Serveru napíše jako tutéž klauzuli (139, změřeno proti 7.4.5) | holé HQL (jednotka „JPQL query") limit nenese; nese ho jen kód kolem dotazu |
| **EclipseLink 5.0** | **JPQL nemá `WITH`**; jen `createNativeQuery` | v textu SQL | hierarchický dotaz EclipseLinku (`ReadAllQuery.setHierarchicalQueryClause`, `CONNECT BY`) je podle dokumentace jen pro platformu Oracle — neověřeno, pro SQL Server nepoužitelné |

Mimo tabulku, jako alternativa k datům, ne k dotazu: EF Core na SQL Serveru mapuje typ `hierarchyid` (balíček `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`), nad kterým jde hierarchii — ne obecný graf — dotazovat metodami `IsDescendantOf`, `GetLevel` bez rekurze. Vyžaduje to sloupec `hierarchyid` ve schématu, který LDBC nemá; podle dokumentace, neověřeno.

### Texty pro stránku překladu

Na stránce *Translation* se zvolí zdrojový framework, tlačítkem *Add empty unit* se přidá jednotka za každý blok níž, do pole jména se napíše název souboru a v rozbalovacím seznamu jazyk uvedený u bloku. Cíl je libovolný. Bez katalogu nemají entity zdrojů Dapper a MyBatis klíč, takže cíle, které klíč vyžadují, vydají místo entit záznam `Failure`; dotaz se přeloží i tak. S katalogem nad `LdbcSnb` (`ConnectionStrings__CatalogDatabase` na tuto databázi) se klíče doplní.

#### Dapper

`Person.cs` — *C# source*

```csharp
namespace LdbcSnb;

public class Person
{
    public long Id { get; set; }

    public string FirstName { get; set; } = "";
}
```

`Person_knows_Person.cs` — *C# source*

```csharp
namespace LdbcSnb;

public class Person_knows_Person
{
    public long Person1Id { get; set; }

    public long Person2Id { get; set; }
}
```

`Walk.sql` — *SQL query*: text z [oddílu 2](#2-příklad-kdo-je-od-osoby-nejvýš-dva-kroky).

#### MyBatis

`Person.java` — *Java source*

```java
package LdbcSnb;

public class Person {
    private Long Id;
    private String FirstName;
}
```

`Person_knows_Person.java` — *Java source*

```java
package LdbcSnb;

public class Person_knows_Person {
    private Long Person1Id;
    private Long Person2Id;
}
```

`WalkMapper.xml` — *XML document*

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
        "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
<mapper namespace="LdbcSnb.WalkMapper">
  <resultMap id="person" type="Person">
    <id     column="Id"        property="Id"/>
    <result column="FirstName" property="FirstName"/>
  </resultMap>
  <resultMap id="knows" type="Person_knows_Person">
    <result column="Person1Id" property="Person1Id"/>
    <result column="Person2Id" property="Person2Id"/>
  </resultMap>
  <select id="walk" resultType="map">
    WITH Walk AS (
        SELECT p.Id AS PersonId, 0 AS Steps
        FROM Person AS p
        WHERE p.Id = 4398046513938
        UNION ALL
        SELECT k.Person2Id, w.Steps + 1
        FROM Walk AS w
        JOIN Person_knows_Person AS k ON k.Person1Id = w.PersonId
        WHERE w.Steps &lt; 2)
    SELECT w.PersonId AS PersonId, MIN(w.Steps) AS Distance
    FROM Walk AS w
    GROUP BY w.PersonId
    OPTION (MAXRECURSION 10)
  </select>
</mapper>
```

`resultType="map"` čtení zahodí se záznamem `Loss` (způsob materializace řádku mezireprezentace nenese, rozh. [048](../decisions/048-a-fact-with-no-place-in-the-model-is-a-loss.md)).

#### EF Core

`Ldbc.cs` — *C# source*

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace LdbcSnb;

[Table("Person")]
public class Person
{
    [Key]
    public long Id { get; set; }

    public string FirstName { get; set; } = "";
}

[Table("Person_knows_Person")]
[PrimaryKey(nameof(Person1Id), nameof(Person2Id))]
public class Person_knows_Person
{
    public long Person1Id { get; set; }

    public long Person2Id { get; set; }
}
```

`Walk.query.cs` — *C# source*

```csharp
public IQueryable<WalkRow> Walk(LdbcContext ctx)
{
    return ctx.Database.SqlQuery<WalkRow>(
        $"WITH Walk AS (SELECT p.Id AS PersonId, 0 AS Steps FROM Person AS p WHERE p.Id = 4398046513938 UNION ALL SELECT k.Person2Id, w.Steps + 1 FROM Walk AS w JOIN Person_knows_Person AS k ON k.Person1Id = w.PersonId WHERE w.Steps < 2) SELECT w.PersonId AS PersonId, MIN(w.Steps) AS Distance FROM Walk AS w GROUP BY w.PersonId OPTION (MAXRECURSION 10)");
}
```

#### NHibernate

`Ldbc.cs` — *C# source*

```csharp
namespace LdbcSnb;

public class Person
{
    public virtual long Id { get; set; }

    public virtual string FirstName { get; set; } = "";
}

public class Person_knows_Person
{
    public virtual long Person1Id { get; set; }

    public virtual long Person2Id { get; set; }

    public override bool Equals(object? obj)
        => obj is Person_knows_Person other && other.Person1Id == Person1Id && other.Person2Id == Person2Id;

    public override int GetHashCode() => HashCode.Combine(Person1Id, Person2Id);
}
```

`Ldbc.hbm.xml` — *XML document*

```xml
<?xml version="1.0" encoding="utf-8"?>
<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="LdbcSnb">
  <class name="Person" table="Person">
    <id name="Id" column="Id" type="Int64">
      <generator class="assigned" />
    </id>
    <property name="FirstName" column="FirstName" type="String" not-null="true" />
  </class>
  <class name="Person_knows_Person" table="Person_knows_Person">
    <composite-id>
      <key-property name="Person1Id" column="Person1Id" type="Int64" />
      <key-property name="Person2Id" column="Person2Id" type="Int64" />
    </composite-id>
  </class>
</hibernate-mapping>
```

`Walk.query.cs` — *C# source*

```csharp
public IQuery Walk(ISession session)
{
    return session.CreateSQLQuery(
        "WITH Walk AS (SELECT p.Id AS PersonId, 0 AS Steps FROM Person AS p WHERE p.Id = 4398046513938 UNION ALL SELECT k.Person2Id, w.Steps + 1 FROM Walk AS w JOIN Person_knows_Person AS k ON k.Person1Id = w.PersonId WHERE w.Steps < 2) SELECT w.PersonId AS PersonId, MIN(w.Steps) AS Distance FROM Walk AS w GROUP BY w.PersonId OPTION (MAXRECURSION 10)");
}
```

#### Hibernate

`Person.java` — *Java source*

```java
package LdbcSnb;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

@Entity
@Table(name = "Person")
public class Person {
    @Id
    @Column(name = "Id")
    private Long Id;

    @Column(name = "FirstName", nullable = false)
    private String FirstName;
}
```

`Person_knows_Person.java` — *Java source*

```java
package LdbcSnb;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.util.Objects;

@Entity
@Table(name = "Person_knows_Person")
@IdClass(Person_knows_Person.Key.class)
public class Person_knows_Person {
    @Id
    @Column(name = "Person1Id")
    private Long Person1Id;

    @Id
    @Column(name = "Person2Id")
    private Long Person2Id;

    public static class Key implements Serializable {
        private Long Person1Id;
        private Long Person2Id;

        public Key() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof Key other
                && Objects.equals(Person1Id, other.Person1Id)
                && Objects.equals(Person2Id, other.Person2Id);
        }

        @Override
        public int hashCode() {
            return Objects.hash(Person1Id, Person2Id);
        }
    }
}
```

`Walk.query.java` — *Java source*

```java
public static Query walk(EntityManager em) {
    return em.createQuery("""
            with Walk as (
                select p.Id as PersonId, 0 as Steps
                from Person p
                where p.Id = 4398046513938
                union all
                select k.Person2Id as PersonId, w.Steps + 1 as Steps
                from Walk w
                join Person_knows_Person k on k.Person1Id = w.PersonId
                where w.Steps < 2)
            select w.PersonId as PersonId, min(w.Steps) as Distance
            from Walk w
            group by w.PersonId
            """)
        .unwrap(org.hibernate.query.Query.class)
        .addQueryHint("MAXRECURSION 10");
}
```

#### EclipseLink

`Person.java` a `Person_knows_Person.java` — *Java source*: tytéž dvě třídy jako u Hibernatu.

`Walk.query.java` — *Java source*

```java
public static Query walk(EntityManager em) {
    return em.createNativeQuery(
            "WITH Walk AS (SELECT p.Id AS PersonId, 0 AS Steps FROM Person AS p WHERE p.Id = 4398046513938 UNION ALL SELECT k.Person2Id, w.Steps + 1 FROM Walk AS w JOIN Person_knows_Person AS k ON k.Person1Id = w.PersonId WHERE w.Steps < 2) SELECT w.PersonId AS PersonId, MIN(w.Steps) AS Distance FROM Walk AS w GROUP BY w.PersonId OPTION (MAXRECURSION 10)");
}
```

---

## 4. Co z příkladu nástroj přeloží

Všech šest zdrojů výš, každý do všech šesti cílů, bez katalogu. **J** = jazykem cíle, **S** = nativním SQL přes API frameworku se záznamem `Fallback`.

| Zdroj ↓ / cíl → | Dapper | MyBatis | EF Core | NHibernate | Hibernate | EclipseLink |
|---|---|---|---|---|---|---|
| Dapper | J | J | S | S | J | S |
| MyBatis | J | J | S | S | J | S |
| EF Core | J | J | S | S | J | S |
| NHibernate | J | J | S | S | J | S |
| Hibernate | J | J | S | S | J | S |
| EclipseLink | J | J | S | S | J | S |

- **Limit se neztratí v žádném z 36 směrů.** T-SQL cílů je na konci textu, v nativním SQL taky, u Hibernatu jako `addQueryHint` na objektu dotazu (139).
- **Záznamy `Fallback`:** EF Core jeden (`Recursion`); NHibernate a EclipseLink dva (`IntermediateResult` a `Recursion`), protože jejich jazyk nemá ani nerekurzivní `WITH`.
- **Sloupec bez aliasu** v rekurzivním členu (`k.Person2Id`, `w.Steps + 1`) dostane jméno sloupce kotvy na stejné pozici, jak to dělá SQL.
- **Řádky dotazu** v cílech, které píšou nativní SQL, typuje cíl sám: EF Core třídou `QueryRow`, NHibernate `AddScalar`.

Tabulka je totéž, co pro kategorie T2 tvrdí matice (`RecursiveDescentOfAHierarchy`, `RecursiveWalkOfACyclicGraph`, [`subset.md`](../subset.md) 1.2 a 1.3); příklad je jen nad doménou LDBC a s jednotkami tak, jak je napíše člověk.

---

## 5. Rekurze v katalogu LDBC

| Dotaz | Co chce specifikace | Co píše katalog | Stav | Kontrola |
|---|---|---|---|---|
| IS 2, IS 6, BI 3 | kořen vlákna zpráv po `replyOf` | sloupce `RootPostId`, `ContainerForumId` naplněné při načtení, jako tabulka `Message` referenční implementace BI | podle specifikace | IS soudcem (117) |
| IC 1, IC 9, BI 10 | osoby do tří, do dvou, ve třech až čtyřech krocích | sjednocení pevného počtu kroků; mez dává sama specifikace, ne katalog | podle specifikace | IC soudcem |
| IC 12 | třída tagu a všechny pod ní | rekurzivní `WITH` sestupem po `SubclassOfTagClassId` (71 tříd, hloubka nejvýš 5) | podle specifikace | soudcem |
| **IC 13** | délka nejkratší cesty po `knows`, bez meze | dvě procházky z obou konců po **dvou** krocích, sloučené mimo rekurzi; dosah 4 | **zjednodušený** | soudcem: všech 6 818 čtení SF 1 se shoduje |
| **IC 14** | všechny nejkratší cesty s vahou | jedna procházka do **tří** kroků nesoucí osoby cesty | **zjednodušený** | soudcem: všech 6 818 čtení SF 1 se shoduje |
| **BI 15** | nejlevnější vážená cesta, váhy z odpovědí ve fórech období | váhy jako definice, procházky z obou konců po dvou krocích | **zjednodušený** | soudce BI nemá; jedno čtení nad SF 1 asi 49 s (rozh. [122](../decisions/122-the-text-of-a-catalog-query-takes-the-shape-a-targets-implementation-needs-under-the-same-criterion.md)) |
| **BI 19** | nejlevnější vážená cesta mezi osobami dvou měst | totéž | **zjednodušený** | nad SF 1 nedoběhne do 500 s (122) |
| **BI 20** | nejlevnější vážená cesta od zaměstnanců firmy | totéž | **zjednodušený** | soudce BI nemá |

### Proč zjednodušené: sledy rostou s mocninou stupně

Graf `knows` ve SF 1: **9 892 osob, 361 246 orientovaných hran** (každé přátelství oběma směry), osob s aspoň jedním přítelem 9 163, **průměrný stupeň 39,4**, nejvyšší 920. Od příkladové osoby (80 přátel):

| Krok *k* | Sledů délky *k* = řádků úrovně *k* rekurzivní definice | Nových osob ve vzdálenosti *k* | Dosaženo celkem |
|---|---|---|---|
| 1 | 80 | 80 | 81 |
| 2 | 9 231 | 4 476 | 4 557 |
| 3 | 910 065 | 4 580 | 9 137 |
| 4 | 94 695 181 | 26 | 9 163 |
| 5 | 9 551 857 428 | 0 | 9 163 |
| 6 | 974 859 951 869 | 0 | 9 163 |

Rekurzivní definice vydá tolik řádků, kolik je sledů, ne osob. Ve čtvrtém kroku přibude 26 osob a rekurze kvůli nim vyrobí 95 milionů řádků; celá komponenta je dosažená po čtyřech krocích, ale rekurze bez meze by pokračovala, dokud ji nezastaví limit (chybou) nebo mez v textu. Hlídání cyklu cestou (text navštívených identifikátorů) by vyřadilo jen sledy, které se vracejí do vlastní stopy; jednoduchých cest je řádově tolik co sledů.

Hledání z obou konců (IC 13, BI 15, BI 19, BI 20) proto vede dvě procházky po dvou krocích: od příkladové osoby 1 + 80 + 9 231 řádků místo 919 376 pro tři kroky z jednoho konce, a dosah je přesto čtyři. Limit rekurze tu nic neřeší — omezuje počet úrovní, ne řádků, a při překročení dotaz zastaví chybou, ne ořízne.

### Kolik dvojic mez čtyř kroků mine

Prohledání do šířky z 199 osob (každá 46. podle identifikátoru) do vyčerpání komponenty:

| Vzdálenost | Dvojic | Podíl |
|---|---|---|
| 1 | 7 674 | 0,42 % |
| 2 | 587 139 | 32,2 % |
| 3 | 1 171 809 | 64,3 % |
| 4 | 56 561 | 3,1 % |
| 5 | 55 | 0,003 % |

Excentricita: 187 z 199 osob má nejvzdálenější osobu ve vzdálenosti 4, dvanáct ve vzdálenosti 5. **Dvojice ve vzdálenosti 5 existují**, takže IC 13 s dosahem 4 může nad SF 1 odpovědět −1 tam, kde cesta je; validační sada LDBC žádnou takovou dvojici nemá, proto se s ní shoduje všech 6 818 čtení. Shoda se soudcem je tedy vlastností dat a sady, ne textu dotazu — a proto katalog stav *zjednodušený* drží i u dotazů, které soudce bere. Průměr komponenty (největší vzdálenost ze všech osob) změřený není; vzorek dává dolní mez 5.

U vážených dotazů (BI 15, BI 19, BI 20) je mez v krocích ještě slabší: nejlevnější cesta může mít víc kroků než nejkratší, a ta se mezí ztratí, i když oba konce leží blízko. Kolik takových dvojic je, změřené není.

---

## 6. Normalizace běhů a cesta bez zjednodušení

Cíl pro katalog: **každý dotaz má na MIS3 běžet mezi 0,2 s a 300 s**, a dosáhnout toho má volba dat, ne ohýbání textu. Pět dotazů na cesty k tomu přidává druhou otázku, jestli zjednodušení zůstane. Rozhodnutí [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) volí **jeden tvar textu** pro všech pět a **jeden parametr z dat**; tenhle oddíl drží podklad, ze kterého vychází, a to, co ještě změřené není.

### 6.1 Co s časem udělají větší data

Časy všech dotazů nad SF 1 nese [`ORMConvertor/README.md`](../../ORMConvertor/README.md), *Catalog query times* (2026-10-15): všech 41 pod 300 s, dvanáct pod 0,2 s. Rozhodnutí [141](../decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md) z toho volí jeden data set.

Dva tvary dotazu reagují na větší data opačně:

| Tvar | Jak roste čas s daty | Dotazy (odhad do změření) | Páka pro okno 0,2–300 s |
|---|---|---|---|
| **průchod** — join a seskupení nad velkou částí tabulky | zhruba lineárně s počtem řádků | většina IC, všechna BI | scale factor |
| **vyhledání** — klíč nebo cizí klíč přes index z rozh. [121](../decisions/121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md) | s logaritmem; SF 10 přidá k 5 ms jednotky procent | IS 1–7, patrně IC 2 a IC 8 | **data ne**; počet opakování v měření (Advisor iteruje, položka *Iterační politika benchmarku*) |

Spodní mez tedy nedosáhne žádný SF u dotazu, který je vyhledáním; horní mez je věcí SF jen u dotazu, který je průchodem. Rozdělení do těchto dvou tříd je výstup měření, ne předpoklad.

**Rekurze zvlášť.** Dnešní procházky vydávají sledy a sledů je stupeň<sup>*k*</sup>: roste-li průměrný stupeň `knows` se scale factorem (generátor LDBC — neověřeno), IC 14 s 919 tisíci řádky nad SF 1 roste s třetí mocninou stupně. Tvar z 6.3 je lineární v hranách a stupeň mu je lhostejný.

### 6.2 Žádná data nespustí neomezený rekurzivní text

Na cyklickém grafu rekurzivní `WITH` bez meze hloubky buď běží, dokud ho limit nezastaví chybou, nebo — s hlídáním cyklu cestou — vyjmenuje všechny jednoduché cesty, a těch není omezeně mnoho s průměrem grafu: jednoduchá cesta se smí vinout celou komponentou. Menší SF nebo prořídlé hrany mění konstantu, ne tvar. Volbou dat lze tedy jen:

- udělat z meze **prokazatelně přesnou** hodnotu — je-li změřený průměr dat nejvýš mez, odpovídá dotaz s mezí správně na každou dvojici těch dat;
- udělat prohledání do šířky (6.3) **levné** pro hloubku aspoň rovnou průměru.

Text „podle specifikace" bez meze se daty nevrátí; zjednodušení je vlastnost rekurzivního členu SQL Serveru, ne dat.

### 6.3 Zvolený tvar: prohledání do šířky v nerekurzivních definicích

To, co rekurzivní člen nesmí — sloučit řádky úrovně a podívat se na dřívější —, nerekurzivní definice smí. Tvar rozh. 140 (po revizi) je řetěz definic `L0 … Ld` jednoho tvaru pro nevážené i vážené hledání: definice hran `Step` dává každé osobě i ji samu jako souseda s nulovou cenou, a úroveň *k* + 1 nese pro každou osobu nejlevnější cenu přes úroveň *k* plus jednu hranu — tedy nejlevnější sled o nejvýš *k* + 1 hranách (Bellman–Ford; s jednotkovou cenou prohledání do šířky). Každá úroveň odkazuje na předchozí právě jednou; SQL Server definici při každém odkazu vyhodnotí znovu, takže tvar, který odkazuje dvakrát (`DISTINCT` sousedé bez `NOT EXISTS` dvou předchozích úrovní), roste s mocninou hloubky — IC 13 se čtyřmi takovými úrovněmi 0,65 s, s pěti úrovněmi tohoto tvaru 0,17 s.

```sql
WITH Step AS (
    SELECT k.Person1Id AS FromPersonId, k.Person2Id AS ToPersonId, 1 AS Steps
    FROM Person_knows_Person AS k
    UNION ALL
    SELECT p.Id AS FromPersonId, p.Id AS ToPersonId, 0 AS Steps
    FROM Person AS p),
L0 AS (
    SELECT p.Id AS PersonId, 0 AS Steps
    FROM Person AS p
    WHERE p.Id = @person1Id),
L1 AS (
    SELECT s.ToPersonId AS PersonId, MIN(l.Steps + s.Steps) AS Steps
    FROM L0 AS l
    JOIN Step AS s ON s.FromPersonId = l.PersonId
    GROUP BY s.ToPersonId),
-- L2 … L5 stejně, každá z předchozí
SELECT COALESCE(l.Steps, -1) AS ShortestPathLength
FROM Person AS p
LEFT JOIN L5 AS l ON l.PersonId = p.Id
WHERE p.Id = @person2Id
```

| | |
|---|---|
| **změřeno nad SF 1** (2026-10-15) | IC 13 0,17 s, IC 14 2,2 s, BI 15 5,3 s (11 úrovní), BI 19 6,6 s (z více než 500 s), BI 20 0,13 s; úroveň má nejvýš tolik řádků, kolik je osob, join s hranami nejvýš tolik, kolik je hran, a definici vah čte každá úroveň jednou |
| **přeloženo nástrojem** (Dapper do pěti cílů) | Dapper, MyBatis, Hibernate jazykem, EF Core LINQem (mezivýsledek jako proměnná), NHibernate a EclipseLink nativním SQL kvůli mezivýsledkům; rekurze ani její limit v textu nejsou |
| **hloubka** *d* | vlastnost dvojice data set × definice hran, z každé osoby: průměr pro nevážené (5), nejvíc hran nejlevnější cesty pro vážené (BI 19 5, BI 20 7, BI 15 pro příkladové období 11); zapsaná u dotazu, držená testem (rozh. 141) |
| **IC 14** (všechny nejkratší cesty) | úrovně od druhé osoby dají vzdálenost; osoba leží na nejkratší cestě na pozici *m* − vzdálenost; druhý řetěz od první osoby prodlužuje cestu jen k sousedovi na další pozici, váhu kroku přičte při kroku a hotovou cestu nese smyčkou přes zbylé úrovně |
| **IC 12** | zůstává rekurzivní: hierarchie tříd tagů je strom, 71 tříd, hloubka nejvýš 5, žádné sledy navíc |

**Vada nalezená cestou.** Konstanta bez aliasu v dalším operandu množinové operace (`SELECT p.Id, p.Id, 0 FROM Person`) se zahazovala se záznamem `Loss` a vycházel příkaz s různým počtem sloupců v operandech; opravena 2026-10-15 — dostává jméno levého operandu podle pozice jako výraz bez aliasu, v definici, nahoře v dotazu i v poddotazu.

### 6.4 Data jako parametr

Co o datech platí před rozhodnutím, které data set zvolí:

| Volba | Co umožní | Co stojí |
|---|---|---|
| **oficiální scale factor** (0,1 / 0,3 / 1 / 3 / 10) | validační sada Interactive je vydaná pro každý z nich v témže archivu, soudce (rozh. [117](../decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md)) platí dál; průměr a stupeň se změří skriptem | čas vyhledávacích dotazů skoro nemění (6.1) |
| **upravená data** (prořídlé `knows`, vlastní malý graf) | nižší stupeň; nad malým grafem doběhne i text bez meze s hlídáním cyklu, takže jde ověřit správnost textu, jak ho specifikace myslí | **soudce odpadá pro všech 19 dotazů Interactive**, ne jen pro rekurzivní — data sdílejí; orákulum výsledků by dal algoritmus v testu (BFS, Dijkstra) v duchu rozh. [089](../decisions/089-differential-verification-as-the-fourth-level-over-a-query.md). Smysl má jen pro databázi, nad kterou běží jen BI |
| **odvozená tabulka vah hran** pro BI 19 a BI 20 | jejich váhy nemají parametr dotazu (počty odpovědí; společná univerzita a ročník), takže tabulka naplněná při načtení je datová volba jako `RootPostId`; BI 19 jinak nedoběhne (122) | údržba loaderem; aktualizace Interactive (IU 6, IU 7 vkládají zprávy) by ji měnily, ale BI v katalogu aktualizace nemá. BI 15 to nejde: jeho váhy závisejí na parametrech období a zůstávají v textu |

Stupeň `knows` ve SF 0,1 a SF 0,3 změřený není; sledy rostou se stupněm, prohledání do šířky ne, takže pro zvolený tvar rozhoduje průměr, pro dnešní texty stupeň.

### 6.5 Zamítnuté cesty

Drží se tu kvůli textu práce; žádnou z nich 140 nevolí.

| Cesta | Proč ne | Co je o ní změřené |
|---|---|---|
| **uložená procedura či tabulková funkce** se smyčkou `WHILE` | tělo se nepřekládá — matice by měřila obal kolem jednoho volání, tentýž argument, kterým rozh. [022](../decisions/022-native-query-syntax-in-builders.md) a 113 odmítly nativní SQL tam, kde jazyk cíle konstrukci má; nová konstrukce slovníku (volání jako zdroj řádků), čtečka T-SQL bere jen čtecí `SELECT` (rozh. [108](../decisions/108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md)) | nic; volání procedury umí všech šest frameworků podle dokumentace (`CommandType.StoredProcedure`, `statementType="CALLABLE"`, `SqlQuery($"EXEC …")`, `CreateSQLQuery`, `createStoredProcedureQuery`) |
| **graf SQL Serveru**, `MATCH(SHORTEST_PATH(…))` | jen nevážená cesta a jen jedna na koncovou osobu — IC 14 chce všechny, BI 15/19/20 váhy; žádný ORM `MATCH` nevysloví, čtení ScriptDomem neověřené; vyžaduje tabulky `AS NODE` / `AS EDGE` vedle relačních | 2026-10-15 nad kopií `knows` SF 1: dvojice IC 13 dá 3 kroky bez meze; vzdálenosti od příkladové osoby 80 / 4 477 / 4 580 / 26 (o jednu víc ve druhém kroku: počítá i návrat k výchozí osobě); cesta `4047,13194139537478,96`; funkce grafu ve `WHERE` téže úrovně odmítne (chyba 13961), filtruje se o úroveň výš |
| **předpočítané vzdálenosti** pro IC 13 | přes 80 milionů dvojic komponenty; aktualizace IU 8 přidává přátelství a soudce ji přehrává | nic |
| **jiná databázová platforma** | druhý dialekt zamítá rozh. [129](../decisions/129-sql-server-2022-stays-the-only-database-dialect.md) jako vymezení předmětu práce; referenční implementace LDBC stojí na PostgreSQL a Umbře | podle dokumentace: PostgreSQL `UNION` bez `ALL` zahodí řádek, který rekurze už vydala, a `SEARCH`/`CYCLE` (od 14); MySQL 8 `UNION DISTINCT` a `cte_max_recursion_depth` (výchozí 1000); Oracle `CONNECT BY NOCYCLE` a SQL/PGQ `GRAPH_TABLE` (23ai); DuckDB rekurze `USING KEY`. Platforma se sloučením mezi úrovněmi napíše IC 13 přesně jedním rekurzivním dotazem — argument, proč SQL Server zjednodušení vynucuje |
| **`hierarchyid` EF Core**, hierarchický dotaz EclipseLinku (`CONNECT BY`) | první chce sloupec `hierarchyid` ve schématu a jen hierarchii, ne graf; druhý je podle dokumentace jen pro Oracle | nic, neověřeno |

### Co z toho rozhodlo 141

Jeden data set (SF 1), žádná upravená data ani tabulka vah; hloubka je vlastnost dvojice data set × definice hran dotazu, měřená z každé osoby a zapsaná u dotazu (`LdbcSearchDepth`), držená `LdbcJudge/LdbcPathQueryTest`.

---

## 7. Jak měření zopakovat

Počty sledů a nových osob po krocích (oddíl 5), nad `LdbcSnb`:

```sql
DECLARE @start BIGINT = 4398046513938;
CREATE TABLE #w (PersonId BIGINT PRIMARY KEY, Walks DECIMAL(38, 0));
CREATE TABLE #seen (PersonId BIGINT PRIMARY KEY, Dist INT);
CREATE TABLE #res (Lvl INT, Walks DECIMAL(38, 0), NewPersons INT, ReachedTotal INT);
INSERT #w VALUES (@start, 1);
INSERT #seen VALUES (@start, 0);
DECLARE @k INT = 0;
WHILE @k < 6
BEGIN
    SET @k += 1;
    SELECT k.Person2Id AS PersonId, SUM(w.Walks) AS Walks INTO #n
    FROM #w AS w JOIN Person_knows_Person AS k ON k.Person1Id = w.PersonId
    GROUP BY k.Person2Id;
    INSERT #seen SELECT n.PersonId, @k FROM #n AS n
    WHERE NOT EXISTS (SELECT 1 FROM #seen AS s WHERE s.PersonId = n.PersonId);
    INSERT #res SELECT @k, (SELECT SUM(Walks) FROM #n),
        (SELECT COUNT(*) FROM #seen WHERE Dist = @k), (SELECT COUNT(*) FROM #seen);
    TRUNCATE TABLE #w;
    INSERT #w SELECT PersonId, Walks FROM #n;
    DROP TABLE #n;
END
SELECT Lvl, Walks, NewPersons, ReachedTotal FROM #res ORDER BY Lvl;
```

Rozdělení vzdáleností ze vzorku (oddíl 5); bez podmínky `rn % 46 = 0` je to průměr celé komponenty (6.1):

```sql
CREATE TABLE #src (Id BIGINT PRIMARY KEY);
INSERT #src SELECT Id FROM (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn
    FROM (SELECT DISTINCT Person1Id AS Id FROM Person_knows_Person) AS a) AS b
WHERE rn % 46 = 0;
CREATE TABLE #seen (Src BIGINT, PersonId BIGINT, Dist INT, PRIMARY KEY (Src, PersonId));
INSERT #seen SELECT Id, Id, 0 FROM #src;
DECLARE @k INT = 0, @added INT = 1;
WHILE @added > 0
BEGIN
    SET @k += 1;
    INSERT #seen SELECT DISTINCT s.Src, k.Person2Id, @k
    FROM #seen AS s JOIN Person_knows_Person AS k ON k.Person1Id = s.PersonId
    WHERE s.Dist = @k - 1
      AND NOT EXISTS (SELECT 1 FROM #seen AS t WHERE t.Src = s.Src AND t.PersonId = k.Person2Id);
    SET @added = @@ROWCOUNT;
END
SELECT Dist, COUNT(*) AS Pairs FROM #seen WHERE Dist > 0 GROUP BY Dist ORDER BY Dist;
SELECT Ecc, COUNT(*) AS Sources FROM (SELECT Src, MAX(Dist) AS Ecc FROM #seen GROUP BY Src) AS e
GROUP BY Ecc ORDER BY Ecc;
```

Překladovou tabulku oddílu 4 dá stránka *Translation* s jednotkami z oddílu 3, nebo dočasný test, který jednotky předá `ConversionHandler.Convert` pro každou dvojici zdroje a cíle.
