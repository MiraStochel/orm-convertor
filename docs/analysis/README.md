# Analýzy

Podklad o frameworcích samotných: čím se liší, co vyjádří a jak v nich vypadá minimální aplikace — vstup pro rozhodnutí a materiál pro analytickou část práce. O nástroji samém nejsou (ten popisuje [`architecture.md`](../architecture.md)). Tvrzení platí **jen proti zafixovaným verzím** (`architecture.md`, „Zafixované verze", rozh. [013](../decisions/013-target-framework-versions.md)).

| Dokument | Obsah |
|---|---|
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
