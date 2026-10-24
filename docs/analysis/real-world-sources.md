# Reálné zdroje: veřejný kód s netriviální vrstvou ORM

Katalog veřejných projektů, jejichž kód může být vstupem nástroje, a pět z nich upravených a převzatých jako příklady. Sledujeme dvě věci: **kód** — entity, mapování a čtecí dotazy, které nástroj přečte — a **schéma**, z něhož fáze katalogu doplní fakta, která zdroj neřekl (rozh. [015](../decisions/015-mapping-fact-completion-from-the-catalog.md)). Data v tabulkách nástroj nečte a katalog je nesleduje.

Účel: případová studie T1 (open-source aplikace, nejméně 15 entit, vztahy, složený klíč, 20 čtecích dotazů) a vstup, který jsme pro nástroj nepsali. Co se z projektu smí převzít a jak se změna vyznačí, určuje rozh. [138](../decisions/138-code-of-open-source-projects-enters-as-a-marked-excerpt-under-its-own-permissive-licence.md); převzaté výňatky leží v `ORMConvertor/SampleData/ThirdParty/`, každý s licencí a README, které vypíše všechny změny.

Stav k 2026-10-13: počty jsou z mělkých klonů výchozí větve (grep, ±10 %), čtení nástrojem z převodu vydání 2.2.0 bez katalogu. Tvrzení o frameworcích platí proti zafixovaným verzím ([`architecture.md`](../architecture.md), *Zafixované verze*).

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
| [HAPI FHIR JPA server](https://github.com/hapifhir/hapi-fhir) | P Apache-2.0 | 62 · 26 `@IdClass` a 9 `@EmbeddedId` · ~178 čtecích `@Query` | migrační úlohy, SQL Server podporovaný | s úpravou PD, DD (`@MappedSuperclass` u hlavních tabulek) |
| [Broadleaf Commerce CE](https://github.com/BroadleafCommerce/BroadleafCommerce) | C Fair Use License | 154 · 3 `@EmbeddedId` · 131 `<named-query>` v `orm.xml` | `hbm2ddl` | nevhodné: licence není open source; `@Inheritance` u skoro všech entit |
| [OpenMRS core](https://github.com/openmrs/openmrs-core) | C MPL-2.0 | 86 + 17 v `hbm.xml` · žádný · ~28 `createQuery`, jinak Criteria API | Liquibase, MySQL a PostgreSQL | nevhodné: bez složeného klíče, dotazy skládá Criteria API |
| [Shopizer](https://github.com/shopizer-ecommerce/shopizer) | P Apache-2.0 | 82 · žádný · 229 `@Query` s `join fetch` | z entit (H2, MySQL) | nevhodné pro T1: bez složeného klíče; `javax.persistence`, vývoj stojí od 2025-03 |

### EclipseLink

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [Apache Ambari](https://github.com/apache/ambari) — správa clusterů Hadoop | P Apache-2.0 | 98 · 12 `@IdClass` · ~185 čtecích `@NamedQuery` a 38 `createQuery` | `Ambari-DDL-SQLServer-CREATE.sql` | s úpravou ZP v anotacích; parametr proti výčtu mimo převod vypustit — **převzato** (upozornění), příklad `ambari-alerts` |
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
| [Flowable](https://github.com/flowable/flowable-engine) | P Apache-2.0 | engine BPMN, desítky tabulek · ano · 395 statických po dosazení `${prefix}` | `flowable.mssql.create.*.sql` | s úpravou: `${prefix}` dosadit, ZP, DS |
| [mall](https://github.com/macrozheng/mall) | P Apache-2.0 | 76 tabulek · žádný · 102 statických z 266 | dump MySQL | nevhodné pro T1: bez složeného klíče, většinou generované dynamické SQL |

### EF Core

| Zdroj | Licence | Rozsah | Schéma | Použití |
|---|---|---|---|---|
| [Chinook Web API](https://github.com/cwoodruff/ChinookASPNETWebAPI) — vrstva EF Core | P MIT | 11 · 1 (`PlaylistTrack`) · 35 čtecích LINQ | autorova varianta Chinooku pro SQL Server ([ChinookDatabase](https://github.com/cwoodruff/ChinookDatabase), klíče `Id`) | s úpravou ZP, navigace v projekci a `SelectMany` nad navigací na korelované `Any` — **převzato**, příklad `chinook-efcore` |
| [bitwarden/server](https://github.com/bitwarden/server) | C AGPL-3.0 | 63 tabulek · 5+ (`CollectionCipher`, `GroupUser`, …) · 56 tříd dotazů a ~180 LINQ v repozitářích | SSDT `src/Sql`, SQL Server | s úpravou ZP; nejlepší kandidát T1 v .NET, převzít ho nelze |
| [SimplCommerce](https://github.com/simplcommerce/SimplCommerce) | P Apache-2.0 | ~73 · 8 · ~330 přes `_repository.Query()` | migrace, SQL Server výchozí | s úpravou KR, DD (`EntityBase`) |
| [Piranha CMS](https://github.com/PiranhaCMS/piranha.core) | P MIT | 39 `DbSet` · 7 · ~180 kroků LINQ | migrace pro SQL Server | s úpravou DD (`ContentBase`, `BlockBase` …) |
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
| `keycloak/` | 22 entit, 10 složených klíčů, 47 čtecích dotazů v jedné třídě (18 přepsaných, 1 vypuštěný); 3 kolekce prvků typu mapa vypuštěné, 1 asociace mimo výňatek přepsaná na sloupec | Hibernate → NHibernate, bez `Failure`; 46 dotazů i s tvarem LINQ | Dapper, MyBatis, Hibernate bez `Failure`; EclipseLink 4× `Fallback`; EF Core odmítne 1 dotaz (`COUNT` bez seskupení s kolekčním parametrem nemá nativní tvar) |
| `ambari/` | 6 entit, 2 N:M přes `@JoinTable`, 39 dotazů (8 přepsaných, 2 vypuštěné); 6 výčtových sloupců přepsaných na `String`, 1 asociace mimo výňatek vypuštěná | EclipseLink → EF Core, bez `Failure`; N:M jako spojovací entity | všech šest cílů bez `Failure` |
| `chinook/` vrstva Dapper | 11 entit beze změny, 11 repozitářů jen se čtením, 38 dotazů | Dapper → EF Core, bez `Failure`; entity `[Keyless]` | MyBatis, Dapper bez `Failure`; NHibernate, Hibernate, EclipseLink odmítnou 11 entit pro chybějící klíč — doplní ho katalog |
| `chinook/` vrstva EF Core | tytéž entity, kontext s fluent konfigurací, 35 dotazů | EF Core → Hibernate, bez `Failure`; klíče a vztahy z fluent řetězů | všech šest cílů bez `Failure` |
| `jpetstore/` | 7 tříd, 7 rozhraní, 7 dokumentů mapperu, 14 selectů | MyBatis → Dapper, bez `Failure` | MyBatis bez `Failure`; EF Core odmítne 4 dotazy (holé sloupce joinu, které žádná třída nemapuje — `SUPPLIER`, `BILLADDR1` —, nemají v řádku `SqlQuery` typ) a entity odvozuje z tabulek bez třídy; NHibernate, Hibernate, EclipseLink píšou tytéž 4 dotazy nativním SQL (`Fallback`) a odmítnou 7 entit pro chybějící klíč — doplní ho katalog |

**T1:** výňatek Keycloaku jako jediný splní všechna čísla zadání (22 entit, vztahy, 10 složených klíčů, 47 čtecích dotazů); bitwarden/server by je splnil v .NET, ale licence převzetí nedovolí.

**Katalog:** u zdrojů Dapper a MyBatis rozhoduje o cílech NHibernate, Hibernate a EclipseLink, které klíč vyžadují. Schéma zveřejňují všechny převzaté projekty — Keycloak migracemi pro SQL Server, Ambari skriptem pro SQL Server, Chinook autorovou databází pro SQL Server, JPetStore skriptem HSQLDB, který T-SQL přečte beze změny —, v databázi katalogu instance ale není žádné z nich (otevřená položka *Schémata převzatých projektů v katalogu instance*).

## Nálezy

Co výňatky ukázaly o nástroji. **Vada** je tvar uvnitř nároku, který nástroj přečte a přeloží jinak, než znamená; vede ji [`open-items.md`](../open-items.md) jako práci a [`subset.md`](../subset.md) 2.13 jako tiché místo. Výňatky každou vadu obcházejí přepisem, který na ni ukazuje.

| | Nález | Kde | Co nástroj udělá |
|---|---|---|---|
| V1 | `Any` nad kolekční navigací (`c.InvoiceLines.Any(o => …)`) | Chinook, EF Core | **opraveno 2026-10-16:** zdrojem poddotazu je cílová entita vztahu a korelace FK = PK se odvodí z jeho párů jako u joinu po cestě; totéž `All`, `Count`, agregát a `Select(…).Contains`; co mapy nemají, `Failure` jmenovitě ([`architecture.md`](../architecture.md) §6.2) |
| V2 | `Find`, `FindAsync` | Chinook, EF Core | **opraveno 2026-10-16:** filtr přes části klíče z mapování + `Take(1)` s `Convention`; bez klíče či s jiným počtem hodnot `Failure` jmenovitě |
| V3 | `@Id` na `@ManyToOne` uvnitř `@IdClass` (odvozená identita JPA) | Keycloak | **opraveno 2026-10-16:** čte se jako `<key-many-to-one>` — sloupec `@JoinColumn` je část klíče pojmenovaná sloupcem, vztah stojí vedle jen pro čtení ([`subset.md`](../subset.md), část 2); entity výňatku se vrátily k textu projektu a dotazy, které srovnávají klíč, jmenují sloupec (`m.USER_ID`) |
| V4 | alias v uvozovkách s tečkou (`AS "product.productId"`) | JPetStore | **opraveno 2026-10-16:** SQL cíl `AS [product.productId]`, cíl nad členy řádku nativním SQL (`Fallback`), zdroj MyBatis hlásí ztracené vnoření ([`subset.md`](../subset.md) 2.8) |
| V5 | holý sloupec v dotazu přes víc tabulek | JPetStore | **opraveno 2026-10-16:** vlastníka dá mapa aliasu, kterou doplní i katalog; bez ní SQL cíl nechá sloupec holý a cíl nad členy řádku jde nativním SQL (2.6) |
| V6 | projekce celého řádku jedné tabulky joinu (`select r from R r join C c …`, `SELECT Track.* …`) | Keycloak, Ambari, Chinook | **opraveno 2026-10-16:** projekce nese alias celého řádku; SQL `r.*`, HQL a JPQL `select r`, LINQ `Select(x => x.r)`, typ výsledku entita za aliasem (2.6) |

Vyslovené meze, na které výňatky narazily ([`subset.md`](../subset.md), část 2): srovnání celé entity s parametrem, křížový join čárkou, parametr porovnaný s atributem výčtového typu mimo převod (parametr bez skaláru), zápis, Dapper.Contrib, uložené procedury, dynamické SQL MyBatisu, `@ElementCollection` a dědičnost (VO 2), koncový skalár `AnyAsync` a `CountAsync`, projekce navigace, asociace jako argument agregátu (`count(m.user)`) a `SelectMany` s jedním argumentem (`Loss`). Cesta přes asociaci mimo `join`, na které stojí velká část reálného kódu a která zprvu vedla otevřenou položku, se čte od rozh. [144](../decisions/144-a-path-through-an-association-outside-join-is-an-implicit-join-or-the-foreign-key-column.md): čtrnáct dotazů Keycloaku a Ambari, které ji nesly, stojí zase v textu projektu a `Combined/AssociationPathOutsideJoinTest` je převádí. Pojmenované dotazy JPA, které výňatky Keycloaku a Ambari zprvu musely vytáhnout do třídy s `em.createQuery` (úprava PD), se od rozh. [143](../decisions/143-a-named-query-of-jpa-is-read-where-it-is-defined.md) čtou z anotací; oba výňatky je nesou tak, jak je projekt napsal, s úpravami ZP a CE vyznačenými u každého dotazu.

Drobnost: vnořené klíčové třídy se stejným jménem (`Key` v jedenácti entitách Keycloaku) hlásily `Conflict`, jako by byly entitami; **opraveno 2026-10-16** rozh. [142](../decisions/142-a-key-class-claim-binds-first-to-the-class-nested-in-the-entity-that-names-it.md) — nárok klíčové třídy ji najde ve vlastním rozsahu entity.
