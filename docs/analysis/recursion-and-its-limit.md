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

## 6. Kudy dál

| Cesta | Které dotazy | Přesná? | Jeden `SELECT`? | Překlad (jazykem kde) | Co to stojí v nástroji | Naráží na |
|---|---|---|---|---|---|---|
| [6.1](#61-mez-podle-dat-s-důkazem) mez podle dat s důkazem | IC 13, IC 14 | jen nad daty, jejichž průměr se změří | ano | beze změny | nic; měření průměru | stav zůstane *zjednodušený* |
| [6.2](#62-prohledání-do-šířky-rozepsané-do-nerekurzivních-definic) BFS rozepsané do nerekurzivních definic | IC 13, IC 14; vážené jako Bellman–Ford | do zvolené hloubky přesná, s hloubkou ≥ průměr úplně | ano | Dapper, MyBatis, Hibernate; EF Core podle konce dotazu | nic nového ve slovníku | zamítnutí „rekurze rozepsané do pevného počtu joinů" (113, 122) |
| [6.3](#63-iterativní-hledání-v-uložené-proceduře-či-funkci) procedura / tabulková funkce s cyklem | všech pět | ano, bez meze | ne (`EXEC`, funkce ve `FROM`) | volání procedury umí všech šest, tělo nepřekládá nikdo | nová konstrukce slovníku | rozh. [108](../decisions/108-a-sql-unit-carries-a-query-per-select-numbered-by-position.md) (jen čtecí `SELECT`), argument 022/113 proti obalu kolem textu |
| [6.4](#64-graf-sql-serveru-match-a-shortest_path) graf SQL Serveru, `SHORTEST_PATH` | IC 13 | ano (ověřeno) | ano | nikde, jen nativní SQL | nová konstrukce slovníku a tabulky uzlů a hran | jen nevážená cesta a jen jedna cesta |
| [6.5](#65-předvýpočet) předvýpočet | BI 19, BI 20 (váhy hran); IC 13 (vzdálenosti) | ano | ano | beze změny | loader, údržba při aktualizacích | soudce Interactive přehrává aktualizace (117) |
| [6.6](#66-jiná-data) jiná data | všech pět | nad nimi ano | ano | beze změny | vlastní orákulum výsledků | validační sada LDBC neplatí |
| [6.7](#67-jiná-databázová-platforma) jiná platforma | všech pět | podle platformy | ano | podle ORM a dialektu | druhý dialekt | rozh. [129](../decisions/129-sql-server-2022-stays-the-only-database-dialect.md), schválený záměr |

### 6.1 Mez podle dat s důkazem

Dnešní mez (dosah 4) nechat, nebo zvednout na 3 + 3 (dosah 6; z příkladové osoby 2 × 919 376 řádků), a **změřit průměr** komponenty `knows` ve SF 1 prohledáním do šířky ze všech 9 163 osob (skript v [oddílu 7](#7-jak-měření-zopakovat), jen bez výběru vzorku). Je-li průměr nejvýš dosah, je IC 13 nad SF 1 přesný a dá se to doložit jednou větou s číslem. Text zůstává textem s mezí, takže podle rozh. 110 dál *zjednodušený*; mění se jen to, co o něm umíme říct. Pro IC 14 (jedna procházka, nese cesty) je 3 + 3 dražší: musí se nést cesta z obou stran a spojit.

### 6.2 Prohledání do šířky rozepsané do nerekurzivních definic

To, co rekurzivní člen nesmí — sloučit úroveň (`DISTINCT`) a vyřadit dřív dosažené (`NOT EXISTS`) —, nerekurzivní definice smí. Prohledání do šířky se tak dá napsat jako řetěz definic `L0 … Ld`, kde úroveň *k* + 1 jsou sousedé úrovně *k*, kteří nejsou v úrovních *k* a *k* − 1 (v neorientovaném grafu nemůže soused ležet jinde):

```sql
WITH L0 AS (
    SELECT p.Id AS PersonId
    FROM Person AS p
    WHERE p.Id = 4398046513938),
L1 AS (
    SELECT DISTINCT k.Person2Id AS PersonId
    FROM L0 AS f
    JOIN Person_knows_Person AS k ON k.Person1Id = f.PersonId
    WHERE NOT EXISTS (SELECT s.PersonId FROM L0 AS s WHERE s.PersonId = k.Person2Id)),
L2 AS (
    SELECT DISTINCT k.Person2Id AS PersonId
    FROM L1 AS f
    JOIN Person_knows_Person AS k ON k.Person1Id = f.PersonId
    WHERE NOT EXISTS (SELECT s.PersonId FROM L0 AS s WHERE s.PersonId = k.Person2Id)
      AND NOT EXISTS (SELECT s.PersonId FROM L1 AS s WHERE s.PersonId = k.Person2Id)),
L3 AS (
    SELECT DISTINCT k.Person2Id AS PersonId
    FROM L2 AS f
    JOIN Person_knows_Person AS k ON k.Person1Id = f.PersonId
    WHERE NOT EXISTS (SELECT s.PersonId FROM L1 AS s WHERE s.PersonId = k.Person2Id)
      AND NOT EXISTS (SELECT s.PersonId FROM L2 AS s WHERE s.PersonId = k.Person2Id)),
L4 AS (
    SELECT DISTINCT k.Person2Id AS PersonId
    FROM L3 AS f
    JOIN Person_knows_Person AS k ON k.Person1Id = f.PersonId
    WHERE NOT EXISTS (SELECT s.PersonId FROM L2 AS s WHERE s.PersonId = k.Person2Id)
      AND NOT EXISTS (SELECT s.PersonId FROM L3 AS s WHERE s.PersonId = k.Person2Id)),
Reached AS (
    SELECT l.PersonId AS PersonId, 0 AS Steps FROM L0 AS l
    UNION ALL
    SELECT l.PersonId AS PersonId, 1 AS Steps FROM L1 AS l
    UNION ALL
    SELECT l.PersonId AS PersonId, 2 AS Steps FROM L2 AS l
    UNION ALL
    SELECT l.PersonId AS PersonId, 3 AS Steps FROM L3 AS l
    UNION ALL
    SELECT l.PersonId AS PersonId, 4 AS Steps FROM L4 AS l)
SELECT COALESCE(MIN(r.Steps), -1) AS ShortestPathLength
FROM Reached AS r
WHERE r.PersonId = 96
```

- **Změřeno nad SF 1:** dvojice příkladu IC 13 (4398046513938 → 96) dá 3; počty úrovní 1 / 80 / 4 476 / 4 580 / 26 se shodují s prohledáním do šířky výš i s `SHORTEST_PATH` (6.4). Úroveň má nejvýš tolik řádků, kolik je osob, a join úrovně s hranami nejvýš tolik, kolik je hran — žádný exponent.
- **Přeloženo nástrojem** (Dapper do šesti cílů): Dapper, MyBatis a Hibernate jazykem; EF Core nativním SQL kvůli koncovému `MIN` bez seskupení (`Fallback` `Aggregation`, kategorie `AggregateOverTheWholeResult`), NHibernate a EclipseLink nativním SQL kvůli mezivýsledkům. Limit rekurze tu není co nést.
- **Vážené dotazy** jdou týmž tvarem jako Bellman–Ford: úroveň *k* nese pro každou osobu nejlevnější cenu sledu o nejvýš *k* hranách, `MIN` se seskupením mezi úrovněmi — to, co rekurzivní člen nesmí. Přesné, je-li *k* aspoň počet hran nejlevnější cesty. Nezměřeno.
- **Hloubka je pořád pevná** a text roste s ní (dvě definice na krok, z obou konců polovina). Rozdíl proti dnešku: mez neomezuje přesnost na vzorku dat, ale jen počet kroků — s hloubkou ≥ průměr (6.1) je výsledek úplný a cena lineární.
- **Naráží na rozhodnutí.** Rozh. 113 a 122 zamítají jako text katalogu „rekurzi rozepsanou do pevného počtu joinů". Tohle není rekurze rozepsaná do joinů, ale jiný algoritmus (BFS místo výčtu sledů), přesto by text katalogu změnil nové rozhodnutí, ne oprava. Je třeba zodpovědět, jestli kritérium 122 (b) — tvar, jaký by referenční implementace mohla napsat — takový text unese.
- **Vada nalezená cestou.** Konstanta bez aliasu v dalším operandu množinové operace (`SELECT l.PersonId, 1 FROM L1`) se zahodí se záznamem `Loss` a vyjde příkaz s různým počtem sloupců v operandech, který SQL Server odmítne. Text výš proto píše alias u každé konstanty; vada je v [`open-items.md`](../open-items.md).

### 6.3 Iterativní hledání v uložené proceduře či funkci

Smyčka `WHILE` nad dočasnou tabulkou navštívených osob je prohledání do šířky bez meze a s cenou úměrnou hranám (tak jsou změřena čísla v oddílu 5). Do jednoho čtecího `SELECT` se nevejde: žije v uložené proceduře (`EXEC dbo.ShortestPath @a, @b`) nebo ve vícepříkazové tabulkové funkci, kterou dotaz čte ve `FROM`. Procedura by patřila k fyzickému návrhu `LdbcSnb` stejně jako indexy (rozh. [121](../decisions/121-the-text-of-a-catalog-query-takes-the-shape-its-planner-needs-and-ldbcsnb-carries-the-indexes-its-reads-need.md)).

| Framework | Volání procedury | Tabulková funkce ve `FROM` |
|---|---|---|
| Dapper | `CommandType.StoredProcedure` | v textu SQL |
| MyBatis | `statementType="CALLABLE"` | v textu SQL |
| EF Core | `SqlQuery($"EXEC …")` | mapovaná funkce (`HasDbFunction`) skládatelná v LINQ |
| NHibernate | `CreateSQLQuery("EXEC …")` | v nativním SQL |
| Hibernate, EclipseLink | `createStoredProcedureQuery` (JPA) | v nativním SQL; HQL 7 a funkce vracející množinu — neověřeno |

Podle dokumentace, neověřeno. Pro nástroj je to nová konstrukce slovníku (volání procedury či funkce jako zdroj řádků) a dnes ji čtečka T-SQL odmítne (108 bere jen čtecí `SELECT`, laterální join na funkci odmítá `subset.md` 2.6). Hlavní námitka: tělo procedury se nepřekládá, takže by matice u těchto dotazů měřila obal kolem jednoho volání — tentýž argument, kterým rozh. 022 a 113 odmítly nativní SQL tam, kde jazyk cíle konstrukci má.

### 6.4 Graf SQL Serveru: `MATCH` a `SHORTEST_PATH`

SQL Server 2019 a novější hledá nejkratší cestu ve vlastním grafovém modelu. Data musejí být v tabulkách uzlů a hran (`AS NODE`, `AS EDGE`) vedle relačních:

```sql
CREATE TABLE dbo.PersonNode (Id BIGINT PRIMARY KEY) AS NODE;
CREATE TABLE dbo.Knows AS EDGE;
INSERT dbo.PersonNode (Id) SELECT Id FROM dbo.Person;
INSERT dbo.Knows ($from_id, $to_id)
SELECT a.$node_id, b.$node_id
FROM dbo.Person_knows_Person AS k
JOIN dbo.PersonNode AS a ON a.Id = k.Person1Id
JOIN dbo.PersonNode AS b ON b.Id = k.Person2Id;

SELECT q.EndId, q.Hops
FROM (
    SELECT LAST_VALUE(p2.Id) WITHIN GROUP (GRAPH PATH) AS EndId,
           COUNT(p2.Id) WITHIN GROUP (GRAPH PATH) AS Hops
    FROM dbo.PersonNode AS p1, dbo.Knows FOR PATH AS k, dbo.PersonNode FOR PATH AS p2
    WHERE MATCH(SHORTEST_PATH(p1(-(k)->p2)+)) AND p1.Id = 4398046513938) AS q
WHERE q.EndId = 96
```

| Změřeno 2026-10-15 nad kopií `knows` SF 1 | Výsledek |
|---|---|
| IC 13 pro dvojici příkladu | 3 kroky, bez meze |
| vzdálenosti od příkladové osoby | 80 / 4 477 / 4 580 / 26 — totéž co BFS; o jednu víc ve druhém kroku, protože `SHORTEST_PATH` počítá i návrat k výchozí osobě |
| cesta (`STRING_AGG … WITHIN GROUP (GRAPH PATH)`) | jedna: `4047,13194139537478,96` |
| funkce grafu ve `WHERE` téže úrovně | odmítnuto (chyba 13961), filtruje se o úroveň výš |

Pro IC 13 přesné. IC 14 chce **všechny** nejkratší cesty a `SHORTEST_PATH` vrací jednu na koncovou osobu; BI 15, BI 19 a BI 20 chtějí cestu váženou a `SHORTEST_PATH` váhy nezná. `MATCH` nevysloví žádný z šesti ORM, takže by šel všude nativním SQL a mezireprezentace by potřebovala novou konstrukci; jestli `MATCH` přečte ScriptDom, ověřené není.

### 6.5 Předvýpočet

Katalog už předpočítává tam, kde to dělá referenční implementace: `RootPostId` a `ContainerForumId` (IS 2, IS 6, BI 3) jsou sloupce naplněné při načtení. Stejně by šlo:

- **váhy hran BI 19 a BI 20** jako tabulka načtená s daty. Poznámka BI 19 říká, že dotaz doběhne jen nad hranou materializovanou předem (969 tisíc dvojic autorů); procházka pak čte tabulku místo definice. Indexovaný pohled to nezastoupí — podle dokumentace nesmí obsahovat self-join, a interakce je join `Message` se sebou.
- **vzdálenosti** pro IC 13: všechny dvojice komponenty jsou přes 80 milionů řádků; do dvou kroků desítky milionů. Prakticky jen jako pomocná struktura procházky z obou konců.

Interactive má aktualizace (IU 8 přidává přátelství) a soudce je přehrává (rozh. [117](../decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md)), takže předvýpočet nad `knows` by musel loader udržovat při každém vložení. BI aktualizace v katalogu nemá.

### 6.6 Jiná data

| Data | Co umožní | Cena |
|---|---|---|
| **malý vlastní graf** (desítky osob, řídký) | text bez meze s hlídáním cyklu cestou doběhne — počet jednoduchých cest je malý — a jde napsat dotaz tak, jak ho specifikace myslí | orákulum výsledků v testu (BFS, Dijkstra) místo validační sady; měří správnost textu, ne LDBC |
| **SF 0,1** (už v `test_db`) | menší komponenta, menší stupně; validační sada LDBC pro SF 0,1 vychází v témže archivu | sledy rostou s mocninou stupně i tak; stupeň SF 0,1 změřený není |
| **prořídlé `knows`** (hrany vybrané pravidlem) | nižší stupeň, procházky hlouběji | mění odpovědi, validační sada neplatí; vlastní orákulum |
| **generátor LDBC s jiným rozdělením stupňů** | totéž bez ručního zásahu | jestli generátor parametr nabízí, ověřené není |

Malý graf zapadá do diferenčního ověření (rozh. [089](../decisions/089-differential-verification-as-the-fourth-level-over-a-query.md)): kanonický výsledek by dal algoritmus v testu, ne zdrojová varianta dotazu. Dokazoval by, že text dotazu bez meze je správný; nad SF 1 by dál neběžel.

### 6.7 Jiná databázová platforma

Všechno podle dokumentace platforem, nic ověřené sondou.

| Platforma | Slučování mezi úrovněmi | Obdoba `MAXRECURSION` | Nejkratší cesta |
|---|---|---|---|
| PostgreSQL | `UNION` (bez `ALL`) zahodí řádek, který už rekurze vydala, takže dosažitelnost bez počítadla skončí sama; `SEARCH` a `CYCLE` (od 14) | žádný limit | rekurzí; `CYCLE` hlídá cyklus po cestě |
| MySQL 8 | `UNION DISTINCT` totéž | `cte_max_recursion_depth` (výchozí 1000), i jako hint `SET_VAR` | rekurzí |
| Oracle | `CONNECT BY NOCYCLE`, rekurzivní `WITH` se `SEARCH` a `CYCLE` | — | SQL/PGQ `GRAPH_TABLE` (23ai) |
| DuckDB | rekurze `USING KEY` přepisuje řádek podle klíče, takže úroveň nese jen nejlepší cenu ke každému vrcholu | — | rozšíření DuckPGQ |

Referenční implementace LDBC, ze kterých schéma `LdbcSnb` vychází, stojí na PostgreSQL (Interactive v1) a Umbra (BI). Druhý dialekt ale rozh. 129 zamítá jako vymezení předmětu práce a schválený záměr jmenuje SQL Server jedinou větou. Platforma se sloučením mezi úrovněmi by IC 13 napsala přesně jedním rekurzivním dotazem; pro nástroj by to znamenalo druhý dialekt ve všech čtyřech stupních ověření. Jako argument do textu práce — proč SQL Server zjednodušení vynucuje — se hodí bez toho.

### Je třeba zodpovědět

- Smí text katalogu být prohledáním do šířky rozepsaným do nerekurzivních definic (6.2)? Je to nové rozhodnutí proti 113 a 122; pro IC 13 dává přesnost i lineární cenu jedním `SELECT`, a ve třech cílech jazykem.
- Jaký je průměr komponenty `knows` ve SF 1 (6.1)? Je to jediné číslo, které 6.1 i 6.2 potřebují.
- Má katalog nést váhy hran BI 19 a BI 20 jako předpočítanou tabulku (6.5), jako to dělá s `RootPostId`?

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
