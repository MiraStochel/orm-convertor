# Analýzy

Podklad o frameworcích samotných — čím se liší, co vyjádří a jak v nich vypadá minimální aplikace — a o veřejném kódu, který na nich stojí: vstup pro rozhodnutí a materiál pro analytickou část práce. O nástroji samém nejsou (ten popisuje [`architecture.md`](../architecture.md)); katalog zdrojů jen shrnuje, co jeho vstupy na nástroji ukázaly. Tvrzení platí **jen proti zafixovaným verzím** (`architecture.md`, „Zafixované verze", rozh. [013](../decisions/013-target-framework-versions.md)).

| Dokument | Obsah |
|---|---|
| [`real-world-sources.md`](real-world-sources.md) | veřejné projekty s netriviální vrstvou ORM po frameworcích: licence, rozsah, schéma pro katalog, použití beze změny či s úpravou; pět převzatých výňatků (rozh. [138](../decisions/138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md)) a co ukázaly |
| [`recursion-and-its-limit.md`](recursion-and-its-limit.md) | rekurzivní `WITH` a `MAXRECURSION` v SQL Serveru, jeden příklad nad `LdbcSnb` ve všech šesti zdrojích k vložení na stránku překladu a změřený překlad do 36 směrů, proč katalog LDBC zjednodušuje pět dotazů (sledy proti osobám, rozdělení vzdáleností SF 1), co s časem udělají větší data a podklad rozh. [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md): prohledání do šířky v nerekurzivních definicích, hloubka z dat, zamítnuté cesty |
| [`orm-frameworks-comparison.md`](orm-frameworks-comparison.md) | NHibernate, EF Core, Dapper tematicky: expresivita, syntaxe, implicitní defaulty pro parser |
| [`java-orm-frameworks-comparison.md`](java-orm-frameworks-comparison.md) | Hibernate (F7), EclipseLink (F9), MyBatis (F8) se stejným číslováním; značí shody z Jakarta Persistence 3.2; doloženo během |
| [`tutorials/nhibernate-getting-started.md`](tutorials/nhibernate-getting-started.md) | `Author` 1:N `Book`, `hbm.xml`, konfigurace v kódu, `SchemaExport` |
| [`tutorials/efcore-getting-started.md`](tutorials/efcore-getting-started.md) | EF Core od nuly |
| [`tutorials/dapper-getting-started.md`](tutorials/dapper-getting-started.md) | Dapper od nuly |
| [`tutorials/hibernate-getting-started.md`](tutorials/hibernate-getting-started.md) | odděluje rozdíly kvůli Javě od rozdílů kvůli Hibernate (F10) |
| [`tutorials/eclipselink-getting-started.md`](tutorials/eclipselink-getting-started.md) | kroky 3–4 shodné s Hibernate; rozdíl DDL obou implementací JPA |
| [`tutorials/mybatis-getting-started.md`](tutorials/mybatis-getting-started.md) | `<resultMap>` jako mapování; krok 9 `getBoundSql` a dynamický příkaz |

- Šest tutoriálů má **shodnou doménu i číslování kroků**, aby šly číst vedle sebe.
- .NET díly a Hibernate předpokládají IDE na hostiteli; EclipseLink a MyBatis běží v kontejneru `maven:3.9.11-eclipse-temurin-25-noble` (rozh. [039](../decisions/039-container-configuration-of-the-environment.md)) — liší se jen kroky 0, 1 a 8.
- Starší rešerše před převzetím je v `notes/` v kořeni — zmražená, proti zafixovaným verzím neověřená.
