# The prompt template web-schema-1 of the web channel (decision 189), variant schema of
# decision 207. The lines before the first section are this note and are not part of the
# prompt. A template that a recorded run used is never changed: a change is a new file with a
# new identifier, so that the record names a text that still exists.
#
# The text is web-zero-shot-4 with one more section, "The database", between the call and the
# input: the DDL of the catalog the tool reads for the key - the schema.sql the export writes
# beside input.json, CREATE TABLE statements with keys and foreign keys and no rows, the schema
# as {{schema}} - as one block of sql. A cell of a key the export writes no schema.sql for has
# no prompt of this variant. The attempt is judged as it came, like zero-shot: the two variants
# differ in the DDL and in nothing else.
#
# Sections: "=== prompt ===" is the text of the prompt, "=== shape <framework> ===" what the
# answer holds for that target - the units the suite expects of the target, the shape of the
# hand-written artifacts in Tests/Database/Foreign, in words. Slots, filled by WebPrompt:
# {{SOURCE}}, {{SOURCE_VERSION}}, {{TARGET}}, {{TARGET_VERSION}}, {{DIALECT}}, {{SHAPE}},
# {{CALL}}, {{ROLES}}, {{DATABASE}}, {{INPUT}}. {{schema}} in lower case is no slot: it is the
# placeholder of the inputs and stays where it stands.
=== prompt ===
You are given the data-access code of an application written for {{SOURCE}} {{SOURCE_VERSION}}. Translate it into {{TARGET}} {{TARGET_VERSION}}, so that the translated code maps the same tables and its queries return the same rows from the same database.

## Rules

1. Work only from this message and from what you already know. Do not search the web, do not open links, do not open any repository on GitHub or on any other code host, do not run code, do not read or write files on any computer - the one your interface runs on included - and do not use any other tool. Do not try to find out where this task comes from. If your interface searched or used a tool by itself, say so in the report.
2. Use nothing from outside this conversation: no memory of other conversations, no saved facts about the user, no custom instructions, no project, no attached or uploaded file. If your interface supplied any such context, ignore it and name it in the report.
3. Answer in this one reply. There will be no follow-up message and no answer to a question: where something is unclear, decide, and write the assumption into the report.
4. The database is {{DIALECT}}. Its tables already exist and stay as they are; the translated code creates, alters and drops nothing. Every table of the input lives in the schema `{{schema}}` of that database: write `{{schema}}` as the schema of every table - in a table annotation, a mapping attribute or an SQL text -, whether or not the input writes it. It is replaced with the real schema before the code is compiled.
5. Translate every input file. Keep the names of the classes, of their properties, of the queries and of their parameters as the input has them, unless {{TARGET}} requires another spelling.
6. Your reply is one document in the form given at the end of this message, and nothing else: no greeting, no explanation, no summary, no question, no text before the first `FILE:` line and none after the report. Everything you want to say goes into the report.
7. Each file carries every using directive or import it needs; the caller supplies only the implicit usings its shape names below, and nothing else.

## What the answer contains

{{SHAPE}}

## How the query is called

{{CALL}}

## The database

The tables of the input are these, in the schema `{{schema}}`; the translation maps them and creates none of them:

{{DATABASE}}

## The input

{{INPUT}}

## The form of the answer

The whole reply is the document below and nothing else - it begins with the first `FILE:` line and ends with the closing fence of the report; a reader takes every character outside the blocks as a breach of the form. Write every file of the translation into the reply itself: no attachment, no file to download, no canvas or artifact panel. For each file write one line

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
  "contextUsed": [],
  "assumptions": []
}
```

In the report, state how long you worked on this task, how many tokens it used and how many credits it cost, as far as you can tell, and for each of the three whether you measured it, estimated it or do not know it: write null where you do not know, and never give an estimate as a measurement. List every tool you or your interface used, every source you consulted and every piece of context your interface supplied beyond this message - a memory, a saved fact about the user, a custom instruction, a project, a file -, and leave all three lists empty if there were none. List the assumptions you made.
=== shape Dapper ===
- **Entity classes**, role `CSharpEntity`: plain public classes with public properties that have a getter and a setter, named and typed after the columns they read, in a file-scoped namespace (`namespace Shop;` - the namespace of the input where it has one).
- **One query file per query**, role `CSharpQuery`: a complete C# source file - its using directives (`using Dapper;`, `using System.Data;`, …), the namespace of the entity classes (`namespace Shop;`) and one public static class holding one public static method `public static List<TRow> MethodName(IDbConnection connection, …)`, which runs the SQL text with Dapper (`connection.Query<TRow>(sql, new { … })`) and returns the list - of the entity class, or `List<dynamic>` where the query returns a projection. The caller compiles the file as it is, together with the entity files, in a project that references Dapper and has the implicit usings `System`, `System.Collections.Generic` and `System.Linq`; every other using directive stands in the file.
- Nothing else: no repository class, no configuration, no connection string.
=== shape EFCore ===
- **Entity classes**, role `CSharpEntity`: public classes in a file-scoped namespace (`namespace Shop;` - the namespace of the input where it has one), mapped with attributes alone: the data annotations of `System.ComponentModel.DataAnnotations` and `System.ComponentModel.DataAnnotations.Schema` (`[Table]`, `[Key]`, `[Column]`, `[MaxLength]`, `[Required]`, `[DatabaseGenerated]`, …) and, for a composite key, `[PrimaryKey]` of `Microsoft.EntityFrameworkCore`. Each entity file carries its own using directives for the attributes it uses. No `DbContext` class, no `OnModelCreating`, no fluent configuration: the caller builds the context itself and registers every public class of these files as an entity type, so these files hold no other public class.
- **One query file per query**, role `CSharpQuery`: a complete C# source file - its using directives (`using Microsoft.EntityFrameworkCore;`, …), the namespace of the entity classes (`namespace Shop;`) and one public static class holding one public static method `public static IQueryable<TEntity> MethodName(DbContext ctx, …)`, or `IQueryable` where the query returns a projection, built from `ctx.Set<TEntity>()`. The method returns the query and does not run it. The caller compiles the file as it is, together with the entity files, in a project that references Entity Framework Core with its SQL Server provider and has the implicit usings `System`, `System.Collections.Generic` and `System.Linq`; every other using directive stands in the file. The class around the method is no entity type: only the public classes of the entity files are registered.
- Where you write a query as SQL through EF Core (`FromSqlRaw`, `SqlQueryRaw`) rather than in LINQ, add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no context class, no configuration, no connection string.
=== shape NHibernate ===
- **Entity classes**, role `CSharpEntity`: public classes in a file-scoped namespace (`namespace Shop;` - the namespace of the input where it has one) whose mapped properties are `public virtual`, with no attributes.
- **Mapping documents**, role `XML`: `hbm.xml` documents (`<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" …>`), one per entity class or one for all of them, the file name ending in `.hbm.xml`. The root element carries no `assembly` attribute: the caller's build supplies the name of its own assembly, and a name written into the document is replaced by it.
- **One query file per query**, role `CSharpQuery`: a complete C# source file - its using directives (`using NHibernate;`, …), the namespace of the entity classes (`namespace Shop;`) and one public static class holding one public static method `public static IQuery MethodName(ISession session, …)`, which returns `session.CreateQuery(hql)` with every parameter set and does not list it; beside it the HQL text alone as a file of role `HqlQuery` (`MethodName.hql`). Where you write the query with NHibernate's LINQ provider instead, the method is `public static IQueryable<TEntity> MethodName(ISession session, …)` over `session.Query<TEntity>()` (`using NHibernate.Linq;`), the role of the file is `CSharpLinqQuery`, and there is no HQL file. The caller compiles the file as it is, together with the entity files, in a project that references NHibernate and has the implicit usings `System`, `System.Collections.Generic` and `System.Linq`; every other using directive stands in the file.
- Where you write a query as native SQL (`session.CreateSQLQuery`), add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no session factory, no configuration, no connection string.
=== shape Hibernate ===
- **Entity classes**, role `JavaEntity`: one public class per file, the file named after the class, all classes in one package (`package Shop;` - the namespace of the input where it has one), mapped with `jakarta.persistence` annotations (`@Entity`, `@Table`, `@Id`, `@IdClass`, `@Column`, …), with a getter and a setter for every mapped field; each file imports what it uses.
- **One query file per query**, role `JavaQuery`: a complete Java source file - the package of the entity classes (`package Shop;`), its imports (`jakarta.persistence.EntityManager`, `jakarta.persistence.TypedQuery`, …) and one public class, named after the file, holding one public static method `public static TypedQuery<TEntity> methodName(EntityManager em, …)`, or `Query` where the query returns a projection, which returns `em.createQuery(jpql, TEntity.class)` (or `em.createQuery(jpql)`) with every parameter set and does not run it; beside it the JPQL text alone as a file of role `JpqlQuery` (`MethodName.jpql`). The caller compiles the file as it is, together with the entity classes, on a class path that holds Hibernate ORM and Jakarta Persistence; every import stands in the file, and any type outside the package is imported or written with its full name.
- Where you write a query as native SQL (`em.createNativeQuery`), add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no `persistence.xml`, no configuration, no connection string.
=== shape EclipseLink ===
- **Entity classes**, role `JavaEntity`: one public class per file, the file named after the class, all classes in one package (`package Shop;` - the namespace of the input where it has one), mapped with `jakarta.persistence` annotations (`@Entity`, `@Table`, `@Id`, `@IdClass`, `@Column`, …), with a getter and a setter for every mapped field; each file imports what it uses.
- **One query file per query**, role `JavaQuery`: a complete Java source file - the package of the entity classes (`package Shop;`), its imports (`jakarta.persistence.EntityManager`, `jakarta.persistence.TypedQuery`, …) and one public class, named after the file, holding one public static method `public static TypedQuery<TEntity> methodName(EntityManager em, …)`, or `Query` where the query returns a projection, which returns `em.createQuery(jpql, TEntity.class)` (or `em.createQuery(jpql)`) with every parameter set and does not run it; beside it the JPQL text alone as a file of role `JpqlQuery` (`MethodName.jpql`). The caller compiles the file as it is, together with the entity classes, on a class path that holds EclipseLink and Jakarta Persistence; every import stands in the file, and any type outside the package is imported or written with its full name.
- Where you write a query as native SQL (`em.createNativeQuery`), add its SQL statement as one more file of role `SqlQuery`.
- Nothing else: no `persistence.xml`, no configuration, no connection string.
=== shape MyBatis ===
- **Domain classes**, role `JavaEntity`: one public class per file, the file named after the class, all classes in one package (`package Shop;` - the namespace of the input where it has one), with private fields and a getter and a setter for each, and no annotations.
- **One mapper document per query**, role `XML`: `<mapper namespace="<package>.<MapperName>">` with one `<select id="<methodName>" …>` holding the SQL text, its parameters written as `#{name}`, and `resultType` the full name of the domain class - or `resultType="map"` where the query returns a projection.
- **One mapper interface per query**, role `JavaQuery`: a complete Java source file - the package of the domain classes (`package Shop;`), its imports (`java.util.List`, `org.apache.ibatis.annotations.Param`, …) and `public interface <MapperName>`, named after the file and as the namespace of its mapper document names it, holding the one method `List<TRow> methodName(@Param("name") Type name, …);`. The caller compiles the file as it is, together with the domain classes, on a class path that holds MyBatis; every import stands in the file.
- Nothing else: no `mybatis-config.xml`, no configuration, no connection string.
