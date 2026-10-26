# The prompt template web-zero-shot-1 of the web channel (decision 189), variant zero-shot of
# decision 175. The lines before the first section are this note and are not part of the
# prompt. A template that a recorded run used is never changed: a change is a new file with a
# new identifier, so that the record names a text that still exists.
#
# Sections: "=== prompt ===" is the text of the prompt, "=== shape <framework> ===" what the
# answer holds for that target - the units the suite expects of the target, the shape of the
# hand-written artifacts in Tests/Database/Foreign, in words. Slots, filled by WebPrompt:
# {{SOURCE}}, {{SOURCE_VERSION}}, {{TARGET}}, {{TARGET_VERSION}}, {{DIALECT}}, {{SHAPE}},
# {{CALL}}, {{ROLES}}, {{INPUT}}. {{schema}} in lower case is no slot: it is the placeholder of
# the inputs and stays where it stands.
=== prompt ===
You are given the data-access code of an application written for {{SOURCE}} {{SOURCE_VERSION}}. Translate it into {{TARGET}} {{TARGET_VERSION}}, so that the translated code maps the same tables and its queries return the same rows from the same database.

## Rules

1. Work only from this message and from what you already know. Do not search the web, do not open links, do not run code, do not read or write files and do not use any other tool. Do not try to find out where this task comes from. If your interface searched or used a tool by itself, say so in the report.
2. Answer in this one reply. There will be no follow-up message and no answer to a question: where something is unclear, decide, and write the assumption into the report.
3. The database is {{DIALECT}}. Its tables already exist and stay as they are; the translated code creates, alters and drops nothing.
4. Where the input writes the schema of a table as `{{schema}}`, write `{{schema}}` in the same place of the translation - in a table annotation, a mapping attribute or an SQL text. It is replaced with the real schema before the code is compiled.
5. Translate every input file. Keep the names of the classes, of their properties, of the queries and of their parameters as the input has them, unless {{TARGET}} requires another spelling.

## What the answer contains

{{SHAPE}}

## How the query is called

{{CALL}}

## The input

{{INPUT}}

## The form of the answer

Write every file of the translation into the reply itself: no attachment, no file to download, no canvas or artifact panel. For each file write one line

FILE: <file name> | <role>

and directly below it one fenced code block with the whole content of the file. The roles are {{ROLES}}. Nothing outside these blocks is read as code.

After the last file write the line

REPORT:

and directly below it one fenced `json` block in this form:

```json
{
  "model": "<the name and version of the model you are>",
  "secondsWorked": null,
  "secondsBasis": "measured | estimated | unknown",
  "tokensUsed": null,
  "tokensBasis": "measured | estimated | unknown",
  "credits": null,
  "creditsBasis": "measured | estimated | unknown",
  "toolsUsed": [],
  "sourcesConsulted": [],
  "assumptions": []
}
```

In the report, state how long you worked on this task, how many tokens it used and how many credits it cost, as far as you can tell, and for each of the three whether you measured it, estimated it or do not know it: write null where you do not know, and never give an estimate as a measurement. List every tool you or your interface used and every source you consulted, and leave both lists empty if there were none. List the assumptions you made.
=== shape Dapper ===
- **Entity classes**, role `CSharpEntity`: plain public classes with public properties that have a getter and a setter, named and typed after the columns they read, in a file-scoped namespace (`namespace Shop;` - the namespace of the input where it has one).
- **One query method per query**, role `CSharpQuery`, one file each, holding the method alone - no class, namespace or using directive around it: `public static List<TRow> MethodName(IDbConnection connection, …)`, which runs the SQL text with Dapper (`connection.Query<TRow>(sql, new { … })`) and returns the list - of the entity class, or `List<dynamic>` where the query returns a projection. The caller puts the method into a static class and supplies `using Dapper;`, `using System.Data;`, `using System;`, `using System.Collections.Generic;`, `using System.Linq;` and a using directive for the namespace of the entity classes.
- Nothing else: no repository class, no configuration, no connection string.
=== shape EFCore ===
- **Entity classes**, role `CSharpEntity`: public classes in a file-scoped namespace (`namespace Shop;` - the namespace of the input where it has one), mapped with data annotations alone (`[Table]`, `[Key]`, `[PrimaryKey]`, `[Column]`, `[MaxLength]`, `[Required]`, `[DatabaseGenerated]`, …). No `DbContext` class, no `OnModelCreating`, no fluent configuration: the caller builds the context itself and registers every public class of these files as an entity type, so these files hold no other public class.
- **One query method per query**, role `CSharpQuery`, one file each, holding the method alone - no class, namespace or using directive around it: `public static IQueryable<TEntity> MethodName(DbContext ctx, …)`, or `IQueryable` where the query returns a projection, built from `ctx.Set<TEntity>()`. The method returns the query and does not run it. The caller puts the method into a static class and supplies `using Microsoft.EntityFrameworkCore;`, `using System;`, `using System.Collections.Generic;`, `using System.Linq;` and a using directive for the namespace of the entity classes.
- Where you write a query as SQL through EF Core (`FromSqlRaw`, `SqlQueryRaw`) rather than in LINQ, add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no context class, no configuration, no connection string.
=== shape NHibernate ===
- **Entity classes**, role `CSharpEntity`: public classes in a file-scoped namespace (`namespace Shop;` - the namespace of the input where it has one) whose mapped properties are `public virtual`, with no attributes.
- **Mapping documents**, role `XML`: `hbm.xml` documents (`<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" …>`), one per entity class or one for all of them, the file name ending in `.hbm.xml`.
- **One query method per query**, role `CSharpQuery`, one file each, holding the method alone - no class, namespace or using directive around it: `public static IQuery MethodName(ISession session, …)`, which returns `session.CreateQuery(hql)` with every parameter set and does not list it; beside it the HQL text alone as a file of role `HqlQuery` (`MethodName.hql`). Where you write the query with NHibernate's LINQ provider instead, the method is `public static IQueryable<TEntity> MethodName(ISession session, …)` over `session.Query<TEntity>()`, its role is `CSharpLinqQuery`, and there is no HQL file. The caller puts the method into a static class and supplies `using NHibernate;` (and `using NHibernate.Linq;` for the LINQ form), `using System;`, `using System.Collections.Generic;`, `using System.Linq;` and a using directive for the namespace of the entity classes.
- Where you write a query as native SQL (`session.CreateSQLQuery`), add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no session factory, no configuration, no connection string.
=== shape Hibernate ===
- **Entity classes**, role `JavaEntity`: one public class per file, the file named after the class, all classes in one package (`package Shop;` - the namespace of the input where it has one), mapped with `jakarta.persistence` annotations (`@Entity`, `@Table`, `@Id`, `@IdClass`, `@Column`, …), with a getter and a setter for every mapped field; each file imports what it uses.
- **One query method per query**, role `JavaQuery`, one file each, holding the method alone - no class, package or import around it: `public static TypedQuery<TEntity> methodName(EntityManager em, …)`, or `Query` where the query returns a projection, which returns `em.createQuery(jpql, TEntity.class)` (or `em.createQuery(jpql)`) with every parameter set and does not run it; beside it the JPQL text alone as a file of role `JpqlQuery` (`MethodName.jpql`). The caller puts the method into a public class in the package of the entity classes and imports `jakarta.persistence.EntityManager`, `jakarta.persistence.Query`, `jakarta.persistence.TypedQuery`, `java.math.BigDecimal`, `java.time.Duration`, `java.time.LocalDate`, `java.time.LocalDateTime`, `java.time.LocalTime`, `java.time.OffsetDateTime`, `java.util.Collection` and `java.util.UUID`; any other type is written with its full name.
- Where you write a query as native SQL (`em.createNativeQuery`), add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no `persistence.xml`, no configuration, no connection string.
=== shape EclipseLink ===
- **Entity classes**, role `JavaEntity`: one public class per file, the file named after the class, all classes in one package (`package Shop;` - the namespace of the input where it has one), mapped with `jakarta.persistence` annotations (`@Entity`, `@Table`, `@Id`, `@IdClass`, `@Column`, …), with a getter and a setter for every mapped field; each file imports what it uses.
- **One query method per query**, role `JavaQuery`, one file each, holding the method alone - no class, package or import around it: `public static TypedQuery<TEntity> methodName(EntityManager em, …)`, or `Query` where the query returns a projection, which returns `em.createQuery(jpql, TEntity.class)` (or `em.createQuery(jpql)`) with every parameter set and does not run it; beside it the JPQL text alone as a file of role `JpqlQuery` (`MethodName.jpql`). The caller puts the method into a public class in the package of the entity classes and imports `jakarta.persistence.EntityManager`, `jakarta.persistence.Query`, `jakarta.persistence.TypedQuery`, `java.math.BigDecimal`, `java.time.Duration`, `java.time.LocalDate`, `java.time.LocalDateTime`, `java.time.LocalTime`, `java.time.OffsetDateTime`, `java.util.Collection` and `java.util.UUID`; any other type is written with its full name.
- Where you write a query as native SQL (`em.createNativeQuery`), add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no `persistence.xml`, no configuration, no connection string.
=== shape MyBatis ===
- **Domain classes**, role `JavaEntity`: one public class per file, the file named after the class, all classes in one package (`package Shop;` - the namespace of the input where it has one), with private fields and a getter and a setter for each, and no annotations.
- **One mapper document per query**, role `XML`: `<mapper namespace="<package>.<MapperName>">` with one `<select id="<methodName>" …>` holding the SQL text, its parameters written as `#{name}`, and `resultType` the full name of the domain class - or `resultType="map"` where the query returns a projection.
- **One mapper method per query**, role `JavaQuery`, one file each, holding the method declaration alone - no interface, package or import around it: `List<TRow> methodName(@Param("name") Type name, …);`. The caller wraps it in `public interface <MapperName>` in the package of the domain classes, named after the namespace of the mapper document, and imports `java.util.List`, `java.util.Map`, `org.apache.ibatis.annotations.Param`, `java.math.BigDecimal`, `java.time.Duration`, `java.time.LocalDate`, `java.time.LocalDateTime`, `java.time.LocalTime`, `java.time.OffsetDateTime`, `java.util.Collection` and `java.util.UUID`.
- Nothing else: no `mybatis-config.xml`, no configuration, no connection string.
