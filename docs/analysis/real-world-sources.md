# Reálné zdroje: veřejný kód s netriviální vrstvou ORM

Katalog veřejných projektů, jejichž kód může být vstupem nástroje, a pět z nich upravených a převzatých jako příklady. Sledujeme dvě věci: **kód** — entity, mapování a čtecí dotazy, které nástroj přečte — a **schéma**, z něhož fáze katalogu doplní fakta, která zdroj neřekl (rozh. [015](../decisions/015-mapping-fact-completion-from-the-catalog.md)). Data v tabulkách nástroj nečte a katalog je nesleduje.

Účel: případová studie T1 (open-source aplikace, nejméně 15 entit, vztahy, složený klíč, 20 čtecích dotazů) a vstup, který jsme pro nástroj nepsali. Co se z projektu smí převzít a jak se změna vyznačí, určuje rozh. [138](../decisions/138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md); převzaté výňatky leží v `ORMConvertor/SampleData/ThirdParty/`, každý s licencí a README, které vypíše všechny změny.

Stav k 2026-10-13: počty jsou z mělkých klonů výchozí větve (grep, ±10 %), čtení nástrojem z převodu vydání 2.2.0 bez katalogu. Doplněno 2026-10-25 při hledání druhé aplikace případové studie (rozh. [184](../decisions/184-t1-leaves-the-exempt-area-in-a-narrower-sense-the-case-study-meets-the-criteria-counted-from-the-excerpt-and-cofoundry-becomes-its-second-application.md)): řádky s commitem jsou z klonu toho commitu, licence přečtené ze souboru licence; převodem je změřené jen Cofoundry. Tvrzení o frameworcích platí proti zafixovaným verzím ([`architecture.md`](../architecture.md), *Zafixované verze*).

## Jak číst tabulky

| Sloupec | Význam |
|---|---|
| Licence | **P** permisivní (MIT, Apache-2.0, BSD) — lze převzít; **C** copyleft (LGPL, MPL, EPL, GPL, AGPL) či jiná — jen odkazem |
| Rozsah | entity · složené klíče · čtecí dotazy v kódu, který nástroj čte nebo který lze do čitelného tvaru převést |
| Schéma | kde projekt své schéma zveřejňuje (DDL, migrace) a zda pro SQL Server; to je vstup katalogu |
| Použití | **beze změny** · **s úpravou** (jakou) · **nevhodné** (proč) |

Úpravy se opakují, proto mají zkratky; řádek podmnožiny, který mez vyslovuje, je v [`subset.md`](../subset.md):

| Zkr. | Úprava | Proč |
|---|---|---|
| **PD** | `@Query` Spring Data vytáhnout do `em.createQuery("…")` | anotace doplňkového balíčku nad frameworkem, kterou nástroj nečte (rozh. [133](../decisions/133-an-add-on-package-over-a-framework-is-not-that-framework.md)); pojmenovaný dotaz JPA (`@NamedQuery`, `<named-query>`) se čte tam, kde stojí (rozh. [143](../decisions/143-a-named-query-of-jpa-is-read-where-it-is-defined.md)), a úpravu nepotřebuje |
| **ZP** | vypustit zápis (`<insert>`, `Execute`, Dapper.Contrib, `SaveChanges`, zápisový `@NamedQuery`) a kód, který slouží jen jemu | překládají se čtecí dotazy (rozh. [053](../decisions/053-a-query-that-would-return-other-rows-is-not-emitted.md), [133](../decisions/133-an-add-on-package-over-a-framework-is-not-that-framework.md)) |
| **DS** | dynamické SQL MyBatisu (`<if>`, `<choose>`, `${}`) vypustit či rozepsat | zúžení Z F8 (subset 2.3, 2.4) |
| **CE** | srovnání s celou entitou (`m.user = :user`) na srovnání klíče | parametr entitou model nenese (subset 2.6) |
| **CP** | cesta přes asociaci v podmínce (`c.user.id = :id`) na korelované `exists` | úprava zrušená: cestu mimo `join` nástroj čte od rozh. [144](../decisions/144-a-path-through-an-association-outside-join-is-an-implicit-join-or-the-foreign-key-column.md) a projekci jedné strany joinu (`select r … join …`) od 2026-10-16, všechny přepisy se vrátily k textu projektu; zkratka zůstává, protože ji jmenují starší záznamy |
| **DD** | dědičnost a `@MappedSuperclass`: sloupce předka přenést do entity | VO 2 (subset 2.1) |
| **KR** | kořen dotazu přes vlastní repozitář (`_repository.Query()`, `Repository.Table`) na `DbSet`/`Set<T>()` | rozh. [114](../decisions/114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md) |
| **SQ** | SQL jiného dialektu přepsat do T-SQL | čtečka SQL je T-SQL nad ScriptDom (rozh. [082](../decisions/082-t-sql-read-and-written-by-a-shared-project.md)) |

## Katalog po frameworcích

### Hibernate a JPA

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [Keycloak](https://github.com/keycloak/keycloak) — identity a přístup | P Apache-2.0 | 47 entit v `model/jpa/…/entities` · 26 `@IdClass` · 48 čtecích `@NamedQuery` u 21 jádrových entit | Liquibase, SQL Server podporovaný | s úpravou ZP, CE v anotacích — **převzato**, příklad `keycloak-identity` |
| [HAPI FHIR JPA server](https://github.com/hapifhir/hapi-fhir) | P Apache-2.0 | 62 · 26 `@IdClass` a 9 `@EmbeddedId` · ~178 čtecích `@Query` | migrační úlohy, SQL Server podporovaný | s úpravou PD, DD (`@MappedSuperclass` u hlavních tabulek); oblast terminologie (`2970ec4`): 13 entit `Term*` (15 jen s `ResourceTable` a jejími předky), část klíče `@IdClass` v `@MappedSuperclass`, 61 čtecích `@Query` — PD u všech, DD v každé entitě, DDL vznikne jen spuštěním javového migrátoru |
| [OpenOLAT](https://github.com/OpenOLAT/OpenOLAT) | P Apache-2.0 | `d073ad4`, oblast identity a organizací: 27 · 1 (`@EmbeddedId`, bez asociace) · 24 čtecích `@NamedQuery`, ostatní dotazy skládané za běhu | PostgreSQL, MySQL, Oracle | nevhodné: asociace typované rozhraním přes `targetEntity`, `@Embedded`, schéma bez SQL Serveru |
| [DSpace](https://github.com/DSpace/DSpace) | P BSD-3 | `DSpaceObject` s dědičností JOINED · jediný nestandardní dvojitý `@Id` · HQL přes pomocnou metodu, Criteria API | PostgreSQL | nevhodné: dědičnost (VO 2), dotazy |
| [jBPM](https://github.com/kiegroup/jbpm) | P Apache-2.0 | JPA, `javax.persistence` · žádný · — | DDL i pro SQL Server | nevhodné pro T1: bez složeného klíče |
| [Apache Airavata](https://github.com/apache/airavata), tag v0.21 (`b304610`) | P Apache-2.0 | oblast `expcatalog`: 28 · 19 `@IdClass` · ~17 dotazů z řetězených konstant | MySQL, Derby | nevhodné: `javax.persistence`, OpenJPA, pod 20 čitelných dotazů; `master` je dnes v Go |
| [Apache Syncope](https://github.com/apache/syncope), [Apache OpenMeetings](https://github.com/apache/openmeetings) | P Apache-2.0 | OpenMeetings 176 `@NamedQuery` · žádný | — | nevhodné pro T1: bez složeného klíče |
| [ThingsBoard](https://github.com/thingsboard/thingsboard) | C Business Source License (dnešní `LICENSE`) | mapování JPA bez asociací | — | nevhodné: licence, bez vztahů |
| [Broadleaf Commerce CE](https://github.com/BroadleafCommerce/BroadleafCommerce) | C Fair Use License | 154 · 3 `@EmbeddedId` · 131 `<named-query>` v `orm.xml` | `hbm2ddl` | nevhodné: licence není open source; `@Inheritance` u skoro všech entit |
| [OpenMRS core](https://github.com/openmrs/openmrs-core) | C MPL-2.0 | 86 + 17 v `hbm.xml` · žádný · ~28 `createQuery`, jinak Criteria API | Liquibase, MySQL a PostgreSQL | nevhodné: bez složeného klíče, dotazy skládá Criteria API |
| [Shopizer](https://github.com/shopizer-ecommerce/shopizer) | P Apache-2.0 | 82 · žádný · 229 `@Query` s `join fetch` | z entit (H2, MySQL) | nevhodné pro T1: bez složeného klíče; `javax.persistence`, vývoj stojí od 2025-03 |

### EclipseLink

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [Apache Ambari](https://github.com/apache/ambari) — správa clusterů Hadoop | P Apache-2.0 | 98 · 20 `@IdClass` (osm psaných plně kvalifikovaně, `@jakarta.persistence.IdClass`) · 221 čtecích z 240 `@NamedQuery` a 38 `createQuery` | `Ambari-DDL-SQLServer-CREATE.sql` | s úpravou ZP v anotacích; parametr proti výčtu mimo převod vypustit — **převzato** (upozornění), příklad `ambari-alerts`. Rozšíření výňatku o topologii a stav clusteru (13 entit, mj. `ClusterServiceEntity` a `ServiceDesiredStateEntity` se složeným klíčem) by dalo 19 entit a 44 nových čtecích dotazů, za cenu 16 výčtových polí na `String`, 8 asociací mimo výňatek a 7 dotazů po cestě přes N:M; jako aplikace T1 ho rozh. 184 zamítlo |
| [Dataverse](https://github.com/IQSS/dataverse) | P Apache-2.0 | 199 `@NamedQuery` · žádný (jediný `@IdClass` je v komentáři) | — | nevhodné pro T1: bez složeného klíče; velký korpus JPQL |
| [Eclipse Cargo Tracker](https://github.com/eclipse-ee4j/cargotracker) | P MIT | 7 entit, většinou `@Embeddable` · žádný | generované | nevhodné: malé, VO 2 |
| [Apache Ranger](https://github.com/apache/ranger) | P Apache-2.0 | 95 bez asociací · žádný · 375 čtecích v `jpa_named_queries.xml` | `security-admin/db/sqlserver` | bez úpravy dotazů (`<named-query>` se čte); nesplní T1 (bez vztahů a složeného klíče), velký korpus JPQL |
| [Apache Roller](https://github.com/apache/roller) | P Apache-2.0 | 29 jen v `orm.xml` · žádný · 87 čtecích `<named-query>` | šablona `createdb.vm` s profilem SQL Serveru | bez úpravy dotazů (`<named-query>` se čte); mapování čistě v `orm.xml` |
| [Apache Fineract](https://github.com/apache/fineract) | P Apache-2.0 | 251 · 1 · ~175 `@Query` | Liquibase, MySQL a PostgreSQL | nevhodné: 165 entit bere klíč z `@MappedSuperclass` (DD u většiny modelu) |
| [Eclipse hawkBit](https://github.com/eclipse-hawkbit/hawkbit) | C EPL-2.0 | 21 · 2 · ~23 `@Query`, jinak Specifications | Flyway bez SQL Serveru | nevhodné: malé, dotazy skládá za běhu |
| [Jakarta EE Tutorial, *order*](https://github.com/eclipse-ee4j/jakartaee-examples) | P | 5 · 2 `@IdClass` · ~10 | generované | jen kalibrace |

### MyBatis

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [MyBatis JPetStore 6](https://github.com/mybatis/jpetstore-6) — referenční obchod | P Apache-2.0 | 7 tříd, 13 tabulek · 2 · 14 selectů, všechny statické | `jpetstore-hsqldb-schema.sql` (HSQLDB, do T-SQL přímo) | s úpravou ZP, čárkový join na `INNER JOIN` — **převzato**, příklad `jpetstore-mybatis` |
| [SonarQube](https://github.com/SonarSource/sonarqube) | C LGPL-3.0 | 176 tabulek · 24 · 448 statických selectů z 626 | `schema-sq.ddl`, SQL Server podporovaný | s úpravou ZP, DS; vztahy v mapperech řídké |
| [Apache Guacamole JDBC](https://github.com/apache/guacamole-client/tree/main/extensions/guacamole-auth-jdbc) | P Apache-2.0 | 23 tabulek · 3 · 40 statických z 62 | `001-create-schema.sql` v T-SQL | s velkou úpravou ZP, DS, `<include>` napříč dokumenty, `<foreach>` s `jdbcType`, model s předky |
| [RuoYi](https://github.com/yangzongzhuan/RuoYi) | P MIT | 21 tabulek · 4 · 65 statických z 87 | dump MySQL | s úpravou ZP, DS, SQ |
| [Alfresco Community Repo](https://github.com/Alfresco/alfresco-community-repo) | C LGPL-3.0 | 45 tabulek · 3+ · 169 statických | PostgreSQL a MySQL | s úpravou SQ, DS |
| [Flowable](https://github.com/flowable/flowable-engine) | P Apache-2.0 | engine BPMN, desítky tabulek · 1 · 395 statických po dosazení `${prefix}` | `flowable.mssql.create.*.sql` | s úpravou: `${prefix}` dosadit, ZP, DS; nevhodné pro T1: jediný složený klíč (`ACT_ID_MEMBERSHIP`) stojí v ostrově tabulek identity, jehož mapper nemá select, a rozhraní mapperů chybí |
| [Camunda 7](https://github.com/camunda/camunda-bpm-platform) | P Apache-2.0 | tytéž tabulky identity · 1 | skripty i pro SQL Server | nevhodné pro T1 jako Flowable; vývoj skončil 2025-11 |
| [mall](https://github.com/macrozheng/mall) | P Apache-2.0 | 76 tabulek · žádný · 102 statických z 266 | dump MySQL | nevhodné pro T1: bez složeného klíče, většinou generované dynamické SQL |

### EF Core

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [Chinook Web API](https://github.com/cwoodruff/ChinookASPNETWebAPI) — vrstva EF Core | P MIT | 11 · 1 (`PlaylistTrack`) · 35 čtecích LINQ | autorova varianta Chinooku pro SQL Server ([ChinookDatabase](https://github.com/cwoodruff/ChinookDatabase), klíče `Id`) | s úpravou ZP, navigace v projekci a `SelectMany` nad navigací na korelované `Any` — **převzato**, příklad `chinook-efcore` |
| [bitwarden/server](https://github.com/bitwarden/server) | C AGPL-3.0 | 63 tabulek · 5+ (`CollectionCipher`, `GroupUser`, …) · 56 tříd dotazů a ~180 LINQ v repozitářích | SSDT `src/Sql`, SQL Server | s úpravou ZP; nejlepší kandidát T1 v .NET, převzít ho nelze |
| [Cofoundry](https://github.com/cofoundry-cms/cofoundry) — CMS | P MIT | `d3b30b1`: 47 `DbSet` · 11 · obslužné třídy dotazů přímo nad `DbSet` kontextu; oblast stránek, adresářů, šablon, uživatelů, rolí, štítků, jazyků a obrázků převodem bez úprav: 31 entit, 7 složených klíčů, 82 přečtených dotazů, 60 bez `Failure`, z nich 19 s vlastním filtrem projektu, který parser vypustí jako neznámý krok (2.12) | instalační skripty T-SQL `Install/Db/Schema`, jen SQL Server | s úpravou RF (vlastní filtr nad `IQueryable` za `Where`, který obaluje), ZP — **převzato**, druhá aplikace T1 (rozh. 184), příklad `cofoundry-pages` |
| [allReady](https://github.com/HTBox/allReady) | P MIT, archiv | `b3d35a1` (2019), EF Core 2.0: 27 `DbSet` · 9 · ~75 čitelných LINQ v obslužných třídách | jen migrace (SQL Server) | s úpravou: konfigurace z pomocných metod `Map(…)` do konfiguračních tříd, osm navigací na `IdentityUser`, výčty; záloha za Cofoundry |
| [SimplCommerce](https://github.com/simplcommerce/SimplCommerce) | P Apache-2.0 | ~73 · 8 · ~330 přes `_repository.Query()` | migrace, SQL Server výchozí | s úpravou KR, DD (`EntityBase`); `3472ba0`: entity registruje reflexe, `DbSet` nejsou, takže výňatek katalogu (19 tříd, 1 složený klíč, 68 dotazů) by chtěl i nový kontext — kolem 110 míst úprav |
| [Piranha CMS](https://github.com/PiranhaCMS/piranha.core) | P MIT | 39 `DbSet` · 7 · ~180 kroků LINQ | migrace pro SQL Server | s úpravou DD (`ContentBase`, `BlockBase` …); `90f9636`: ~28 z 30 tříd má předka, několik generického, kontext je generický `Db<T>` za rozhraním |
| [Oqtane](https://github.com/oqtane/oqtane.framework), [Raytha](https://github.com/RaythaHQ/raytha) | P MIT | žádný složený klíč; Raytha má předka u každé entity | migrace | nevhodné pro T1 |
| [BTCPay Server](https://github.com/btcpayserver/btcpayserver) | P MIT | 15 složených klíčů | jen PostgreSQL (`JSONB`) | nevhodné: dialekt, konfigurace ve statických pomocných metodách |
| [Skoruba IdentityServer4 Admin](https://github.com/skoruba/IdentityServer4.Admin) | P MIT | entity z balíčků NuGet, složené klíče jen v generických předcích ASP.NET Identity, 36 `WhereIf` | migrace | nevhodné |
| [Smartstore](https://github.com/smartstore/Smartstore) | C AGPL-3.0 | ~113 · jen implicitní N:M · stovky, skládané za běhu | SQL Server ve více providerech | nevhodné: dotazy skládá kód |
| [Jellyfin](https://github.com/jellyfin/jellyfin) | C GPL-2.0 | 30 · 12 (i tříčlenné) · skládané za běhu | migrace jen SQLite | nevhodné: dotazy, dialekt |
| [ABP, moduly](https://github.com/abpframework/abp/tree/dev/modules) | C LGPL-3.0 | ~50 tabulek · 6+ · `WhereIf`, `GetQueryableAsync()` | SQL Server | nevhodné: jména tabulek skládá kód |
| [NorthwindTraders](https://github.com/jasontaylordev/NorthwindTraders) | P MIT, archiv | 11 · 2 · 8 přes AutoMapper `ProjectTo` | SQL Server | nevhodné: dotazů málo a projekci nástroj nevidí |
| [EF Core, testy Northwind](https://github.com/dotnet/efcore/tree/main/test/EFCore.Specification.Tests/TestModels/Northwind) | P MIT | 5 s klíčem, 5 bez · 1 · ~1 860 dotazů v testech s baseline T-SQL | `Northwind.sql` | s úpravou: dotaz je lambda testu (`ss.Set<T>()` v `AssertQuery`); sada dotazů, ne aplikace |
| [nopCommerce](https://github.com/nopSolutions/nopCommerce) | C | — | — | nevhodné: od 4.30 linq2db; EF Core jen v tagu `release-4.20` |

### Dapper

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [Chinook Web API](https://github.com/cwoodruff/ChinookASPNETWebAPI) — vrstva Dapper | P MIT | 11 · klíče ne (Dapper je nevysloví) · 38 literálů T-SQL | jako u vrstvy EF Core | s úpravou ZP — **převzato**, příklad `chinook-dapper` |
| [StackExchange.DataExplorer](https://github.com/StackExchange/StackExchange.DataExplorer) | P MIT | ~17 tabulek · žádný · ~45 literálů | `Migrations/*.sql`, SQL Server | beze změny až na `Dapper.SqlBuilder`; nesplní T1 |
| [Hangfire.SqlServer](https://github.com/HangfireIO/Hangfire/tree/main/src/Hangfire.SqlServer) | C LGPL-3.0 | 11 tabulek · 6 · literály se schématem interpolovaným do textu | `Install.sql`, SQL Server | s úpravou: interpolace `[{schemaName}]` |
| [bitwarden/server](https://github.com/bitwarden/server) — vrstva Dapper | C AGPL-3.0 | 376 volání | jako výš | nevhodné: každé volání je uložená procedura (subset 2.3) |

### NHibernate

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [NHibernate, testy Northwind](https://github.com/nhibernate/nhibernate-core/tree/master/src/NHibernate.DomainModel/Northwind) | C LGPL-2.1 | ~25 tříd v 21 `hbm.xml` · 2 `composite-id` · ~1 100 testů LINQ, HQL | `MsSql2012DialectLinqReadonlyCreateScript.sql` | s úpravou: kořen `db.Customers` na `session.Query<T>()`, `<component>` a `joined-subclass` (VO 2); jediný bohatý zdroj `hbm.xml`, převzít ho nelze |
| [RawDataAccessBencher](https://github.com/FransBouma/RawDataAccessBencher) | P MIT | 68 `hbm.xml` nad AdventureWorks · 20 `composite-id` · 3 dotazy | AdventureWorks 2008 (Microsoft) | s velkou úpravou: třídy generuje LLBLGen — `System.Nullable<T>` nástroj za skalár nepřijme, vlastnosti jen v mapování odmítne; dotazy chybí |
| [ClearCanvas](https://github.com/ClearCanvas/ClearCanvas) | C GPL-3.0 | ~120 `hbm.xml` · žádný · HQL skládané za běhu | vlastní generátor DDL | nevhodné: dědičnost, dotazy za běhu |
| [Socioboard](https://github.com/Socioboard-developers/socioboard-core) | bez licence | 118 `hbm.xml` bez vztahů · žádný · ~548 HQL | MariaDB | nevhodné: licence |
| [DotNet-ORM-Cookbook](https://github.com/TortugaResearch/DotNet-ORM-Cookbook) | P Unlicense | 6 `hbm.xml` · — · dotazy NHibernatu většinou `QueryOver` | SSDT, 8 tabulek | nevhodné: malé, `QueryOver` nečteme |
| [Cuyahoga](https://github.com/cuyahogaproject/cuyahoga-1) | P BSD-3 | `96c0e01` (2014): 24 `<class>` · žádný `composite-id` · ~45 HQL v `CreateQuery`, část řetězená | — | nevhodné pro T1: bez složeného klíče; jediná nalezená permisivní aplikace s `hbm.xml` |
| [ShokoServer](https://github.com/ShokoAnime/ShokoServer) | P MIT | 76 Fluent `ClassMap` · žádný | — | nevhodné: Fluent NHibernate nečteme |

### Schémata a sady dotazů bez aplikace

Kód aplikace nemají, ale dávají katalogu schéma a zdrojům Dapper a MyBatis text dotazů.

| Zdroj | Licence | Schéma | Kód nad ním |
|---|---|---|---|
| [Northwind](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/northwind-pubs) `instnwnd.sql` | P MIT | 13 tabulek, 3 složené klíče, SQL Server | testy EF Core a NHibernatu (výš), každá nad svou variantou schématu |
| [AdventureWorks](https://github.com/microsoft/sql-server-samples/tree/master/samples/databases/adventure-works) | P MIT | 71 tabulek, 23 složených klíčů, SQL Server; `hierarchyid`, `xml`, aliasové typy | mapování NHibernate a EF Core v RawDataAccessBencher, model EF Core v benchmarcích dotnet/efcore |
| [Sakila pro SQL Server](https://github.com/jOOQ/sakila) | P BSD-2 | 16 tabulek, 2 složené klíče | sady entit JPA ([jinahya/sakila-entities](https://github.com/jinahya/sakila-entities), Apache-2.0) |
| [Chinook](https://github.com/lerocha/chinook-database) | P MIT | 11 tabulek, 1 složený klíč | Chinook Web API (výš) nad autorovou variantou |
| [classicmodels pro SQL Server](https://github.com/PacktPublishing/jOOQ-Masterclass/tree/master/db/migration/ddl/mssql) | P MIT | 20 tabulek, složené klíče | jen jOOQ |
| [SEDE](https://github.com/hirupert/sede) nad schématem Stack Overflow | repozitář Apache-2.0 | 9 tabulek, žádný složený klíč | ~4 400 prostých dotazů T-SQL psaných lidmi — vstup Dapperu |
| TPC-H, TPC-DS | licence TPC | 8 a 24 tabulek | 22 a 99 šablon; do podmnožiny se vejde menšina |

## Převzaté výňatky

Co se změnilo, vypisuje README složky výňatku a poznámka na začátku každého souboru; tady je, co výňatek dává a jak dopadne v cílech. Převod bez katalogu, jako na výkladové stránce. Co z toho se v cílech .NET zkompiluje a co cíl přijme, měří experimentální běh; první kontrola a úpravy vstupů, které z ní plynou, jsou v [záznamech běhů](../runs/README.md).

| Výňatek | Vstup | Ukázaný směr | Ostatní cíle |
|---|---|---|---|
| `keycloak/` | 22 entit, 11 složených klíčů, 47 čtecích dotazů v anotacích entit (13 přepsaných, 1 vypuštěný); 3 kolekce prvků typu mapa vypuštěné, 1 asociace mimo výňatek přepsaná na sloupec | Hibernate → NHibernate, bez `Failure`; 46 dotazů i s tvarem LINQ | Dapper, MyBatis, Hibernate bez `Failure`; EclipseLink 4× `Fallback`; EF Core odmítne 1 dotaz (`COUNT` bez seskupení s kolekčním parametrem nemá nativní tvar) |
| `ambari/` | 6 entit, 2 N:M přes `@JoinTable`, 39 dotazů (8 přepsaných, 2 vypuštěné); 6 výčtových sloupců přepsaných na `String`, 1 asociace mimo výňatek vypuštěná | EclipseLink → EF Core, bez `Failure`; N:M jako spojovací entity | všech šest cílů bez `Failure` |
| `chinook/` vrstva Dapper | 11 entit beze změny, 11 repozitářů jen se čtením, 38 dotazů | Dapper → EF Core, bez `Failure`; entity `[Keyless]` | MyBatis, Dapper bez `Failure`; NHibernate, Hibernate, EclipseLink odmítnou 11 entit pro chybějící klíč — doplní ho katalog |
| `chinook/` vrstva EF Core | tytéž entity, kontext s fluent konfigurací, 35 dotazů | EF Core → Hibernate, bez `Failure`; klíče a vztahy z fluent řetězů | všech šest cílů bez `Failure` |
| `jpetstore/` | 7 tříd, 7 rozhraní, 7 dokumentů mapperu, 14 selectů | MyBatis → Dapper, bez `Failure` | MyBatis bez `Failure`; EF Core odmítne 4 dotazy (holé sloupce joinu, které žádná třída nemapuje — `SUPPLIER`, `BILLADDR1` —, nemají v řádku `SqlQuery` typ) a entity odvozuje z tabulek bez třídy; NHibernate, Hibernate, EclipseLink píšou tytéž 4 dotazy nativním SQL (`Fallback`) a odmítnou 7 entit pro chybějící klíč — doplní ho katalog |
| `cofoundry/` | 29 entit s konfiguračními třídami, kontext zúžený na jejich `DbSet`, 38 z 53 obslužných tříd, které čtou kontext; vlastní filtry projektu za `Where`, který obalují, dotazy čtené jen v jiném tvaru přepsané a nepřeložitelné vypuštěné, navigace a výčtový typ mimo výňatek vypuštěné, chyba projektu (sloupec `ParentPageGroupId`) opravená — výčet v README výňatku | EF Core → MyBatis, bez `Failure` | všech šest cílů bez `Failure` (bez katalogu) |

**T1:** všechna čísla zadání splní výňatky Keycloaku a Cofoundry, druhotné výňatky ne; drží to `Combined/CaseStudyCriteriaTest`, který je spočítá z výňatku tak, jak ho přečte převod, a počty vypíše (rozh. [184](../decisions/184-t1-leaves-the-exempt-area-in-a-narrower-sense-the-case-study-meets-the-criteria-counted-from-the-excerpt-and-cofoundry-becomes-its-second-application.md)). bitwarden/server by je splnil v .NET, ale licence převzetí nedovolí; aplikací se zdrojem v .NET je proto Cofoundry. Zdroj NHibernate, který by čísla splnil, pod permisivní licencí není.

**Katalog:** u zdrojů Dapper a MyBatis rozhoduje o cílech NHibernate, Hibernate a EclipseLink, které klíč vyžadují. Schéma zveřejňují všechny převzaté projekty — Keycloak migracemi Liquibase, Ambari skriptem pro SQL Server, Chinook autorovou databází pro SQL Server, JPetStore skriptem HSQLDB, který T-SQL přečte beze změny, Cofoundry instalačními skripty T-SQL, které schéma po verzích mění — a každý výňatek nese jeho výňatek `schema.sql` (rozh. [176](../decisions/176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md)): tabulky, které výňatek jmenuje, s vyznačenou změnou (Chinook klíče `Id`, Keycloak přehraný řetěz migrací, Cofoundry tabulky z databáze, kterou skripty postaví). Obě testovací sady je vytvářejí prázdné v databázi případových studií, každý projekt ve vlastním schématu, a experimentální běh překládá výňatky s touto databází jako katalogem a jejich dotazy nad ní spouští; výkladová stránka běží dál bez katalogu (rozh. [099](../decisions/099-examples-are-content-not-a-choice.md)).

## Nálezy

Co výňatky ukázaly o nástroji. **Vada** je tvar uvnitř nároku, který nástroj přečte a přeloží jinak, než znamená; vede ji [`open-items.md`](../open-items.md) jako práci, a je-li tichá, i [`subset.md`](../subset.md) 2.13 jako tiché místo. Výňatky každou vadu obcházejí přepisem, který na ni ukazuje.

| | Nález | Kde | Co nástroj udělá |
|---|---|---|---|
| V1 | `Any` nad kolekční navigací (`c.InvoiceLines.Any(o => …)`) | Chinook, EF Core | **opraveno 2026-10-16:** zdrojem poddotazu je cílová entita vztahu a korelace FK = PK se odvodí z jeho párů jako u joinu po cestě; totéž `All`, `Count`, agregát a `Select(…).Contains`; co mapy nemají, `Failure` jmenovitě ([`architecture.md`](../architecture.md) §6.2) |
| V2 | `Find`, `FindAsync` | Chinook, EF Core | **opraveno 2026-10-16:** filtr přes části klíče z mapování + `Take(1)` s `Convention`; bez klíče či s jiným počtem hodnot `Failure` jmenovitě |
| V3 | `@Id` na `@ManyToOne` uvnitř `@IdClass` (odvozená identita JPA) | Keycloak | **opraveno 2026-10-16:** čte se jako `<key-many-to-one>` — sloupec `@JoinColumn` je část klíče pojmenovaná sloupcem, vztah stojí vedle jen pro čtení ([`subset.md`](../subset.md), část 2); entity výňatku se vrátily k textu projektu a dotazy, které srovnávají klíč, jmenují sloupec (`m.USER_ID`) |
| V4 | alias v uvozovkách s tečkou (`AS "product.productId"`) | JPetStore | **opraveno 2026-10-16:** SQL cíl `AS [product.productId]`, cíl nad členy řádku nativním SQL (`Fallback`), zdroj MyBatis hlásí ztracené vnoření ([`subset.md`](../subset.md) 2.8) |
| V5 | holý sloupec v dotazu přes víc tabulek | JPetStore | **opraveno 2026-10-16:** vlastníka dá mapa aliasu, kterou doplní i katalog; bez ní SQL cíl nechá sloupec holý a cíl nad členy řádku jde nativním SQL (2.6) |
| V6 | projekce celého řádku jedné tabulky joinu (`select r from R r join C c …`, `SELECT Track.* …`) | Keycloak, Ambari, Chinook | **opraveno 2026-10-16:** projekce nese alias celého řádku; SQL `r.*`, HQL a JPQL `select r`, LINQ `Select(x => x.r)`, typ výsledku entita za aliasem (2.6) |
| V7 | kořen EF Core jménem sady, které není jménem entity s „s" (`ctx.PageDirectories` nad `DbSet<PageDirectory>`, tabulka `PageDirectory`) | Cofoundry, EF Core | **opraveno 2026-10-25** rozh. [185](../decisions/185-the-name-of-an-ef-core-set-names-the-entity-the-context-of-the-conversion-declares-through-one-reading-context-of-both-passes.md) — oba průchody čtou přes jeden kontext a jméno sady, kterou kontext převodu deklaruje, je sadou její entity; výňatek nese `.PageDirectories` a `.PagePublishStatusQueries`, jak je projekt napsal (`Combined/SetNamedUnlikeItsEntityTest`). Dotaz dřív vyšel nad tabulkou `PageDirectories` s typem výsledku `PageDirectorie`, jen se záznamem `Convention` |

Vyslovené meze, na které výňatky narazily ([`subset.md`](../subset.md), část 2): srovnání celé entity s parametrem, křížový join čárkou, parametr porovnaný s atributem výčtového typu mimo převod (parametr bez skaláru), zápis, Dapper.Contrib, uložené procedury, dynamické SQL MyBatisu, `@ElementCollection` a dědičnost (VO 2), koncový skalár `AnyAsync` a `CountAsync`, projekce navigace, asociace jako argument agregátu (`count(m.user)`) a `SelectMany` s jedním argumentem (`Loss`). Výňatek Cofoundry k nim přidal vlastní rozšiřující metodu nad `IQueryable` (neznámý krok, `Loss`, 2.12), filtr, který rozšiřující metoda vybírá podmínkou za běhu, LINQ nad objekty v paměti přes člen typu mimo převod (čtený jako dotaz, rozh. [114](../decisions/114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md)), soukromou třídu výsledku v jednotce (čtenou jako entita), cestu do kolekce v podmínce, join přes dvě asociace, pojmenovanou projekci (`Loss`, materializuje entitu) a dvě odmítnutí, která katalog do té doby nejmenoval: člen `Nullable<T>` v podmínce a hodnotu z okolí hlubší než člen jména, s převodem výčtu na číslo (2.4, 2.8). Cesta přes asociaci mimo `join`, na které stojí velká část reálného kódu a která zprvu vedla otevřenou položku, se čte od rozh. [144](../decisions/144-a-path-through-an-association-outside-join-is-an-implicit-join-or-the-foreign-key-column.md): čtrnáct dotazů Keycloaku a Ambari, které ji nesly, stojí zase v textu projektu a `Combined/AssociationPathOutsideJoinTest` je převádí. Pojmenované dotazy JPA, které výňatky Keycloaku a Ambari zprvu musely vytáhnout do třídy s `em.createQuery` (úprava PD), se od rozh. [143](../decisions/143-a-named-query-of-jpa-is-read-where-it-is-defined.md) čtou z anotací; oba výňatky je nesou tak, jak je projekt napsal, s úpravami ZP a CE vyznačenými u každého dotazu.

Drobnost: vnořené klíčové třídy se stejným jménem (`Key` v jedenácti entitách Keycloaku) hlásily `Conflict`, jako by byly entitami; **opraveno 2026-10-16** rozh. [142](../decisions/142-a-key-class-claim-binds-first-to-the-class-nested-in-the-entity-that-names-it.md) — nárok klíčové třídy ji najde ve vlastním rozsahu entity.
