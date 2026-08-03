# Hibernate od nuly — IntelliJ IDEA, JDK 25, Maven, MS SQL Server

Čtvrtý díl a první javový. Stejná doména jako v předchozích třech (`Author` 1:N `Book`), stejný SQL Server, stejné číslování kroků. Cílem je zase to, aby šly dokumenty položit vedle sebe.

Proti předchozím dílům se ale mění dvě věci najednou — framework **i** ekosystém. Tutoriál je proto od začátku odděluje: co je jinak kvůli Javě a co kvůli Hibernate. Pro požadavek F10 (cross-ecosystem překlad) je tohle rozlišení podstatnější než kterýkoli jednotlivý rozdíl.

Ověřené verze: Hibernate ORM **7.4.5.Final** (vydáno 2026-07-12, série 7.4 = latest stable), Jakarta Persistence **3.2**, JDK **25** (LTS), `mssql-jdbc` **13.4.0.jre11**, MS SQL Server **2022**, IntelliJ IDEA (sjednocená distribuce, viz krok 1).

---

## Než začneš: dvě osy rozdílů

### Osa 1 — ekosystém

Javový svět má na skoro každý .NET pojem protějšek, jen jinak pojmenovaný. Tahle tabulka je jediné, co je potřeba si zapamatovat dopředu:

| .NET | Java | Poznámka |
|---|---|---|
| .NET SDK | JDK | IntelliJ ho umí stáhnout sám, viz krok 1 |
| solution (`.sln`) | **neexistuje** | víc projektů pohromadě řeší Maven multi-module, ale pro hřiště to nepotřebuješ |
| projekt (`.csproj`) | modul s `pom.xml` | |
| MSBuild / `dotnet build` | Maven | IntelliJ má vlastní zabudovaný Maven, nemusíš nic instalovat |
| NuGet balíček | Maven artefakt | souřadnice jsou trojice `groupId:artifactId:version` |
| reference na assembly | classpath | |
| `namespace` | `package` | **musí** odpovídat adresářové struktuře, jinak se to nezkompiluje |
| jeden soubor = víc tříd | jeden soubor = jedna veřejná třída | název souboru se musí shodovat s názvem třídy |
| `Embedded Resource` v `.csproj` | `src/main/resources` | na classpath se dostane automaticky, žádný zásah do build souboru |
| `bin/Debug/net10.0` | `target/classes` | |
| Visual Studio | IntelliJ IDEA | |

Struktura projektu je v Mavenu daná konvencí a nedá se z ní vybočit bez konfigurace:

```
orm-playground-hibernate/
├─ pom.xml
└─ src/
   └─ main/
      ├─ java/          ← zdrojáky, adresáře kopírují package
      └─ resources/     ← všechno ostatní, končí na classpath
```

### Osa 2 — framework

NHibernate je port Hibernate, takže shod je víc než rozdílů: session, session factory, dirty checking, flush při commitu, lazy loading, HQL, cascade, `inverse`/`mappedBy`, generátory klíčů. Tuhle část znáš.

Zajímavé je, kde se obě větve za dvacet let rozešly:

| | NHibernate 5.7.0 | Hibernate 7.4.5 |
|---|---|---|
| Primární forma mapování | `hbm.xml` | **anotace** |
| `hbm.xml` | jediná plně podporovaná forma | zavržené, transformace do `mapping.xml`, odstranění v 8.0 |
| Standardizační vrstva | žádná | **Jakarta Persistence 3.2** — Hibernate je její implementace |
| Dialekt | nutné zvolit ručně | odvozený z JDBC metadat, ručně jen výjimečně |
| Ovladač databáze | `db.Driver<...>()` | JDBC 4 se registruje sám, nikde se neuvádí |
| `virtual` na mapovaných členech | povinné | odpadá — v Javě jsou metody virtuální implicitně |
| Jak se určí přístup k hodnotě | `access` na jednotlivé vlastnosti | podle umístění `@Id` pro celou třídu |

Poslední tři řádky nejsou rozdíly frameworků, ale jazyků. Držet je oddělené se vyplatí — do IR patří jinak.

**Nejdůležitější strukturální rozdíl je ale ta standardizační vrstva.** V Javě existují dvě úrovně: co říká Jakarta Persistence a co k tomu přidává konkrétní implementace. `@Entity`, `@Table`, `@Column`, `@Id` jsou standard a přenesou se do EclipseLinku beze změny; `@Nationalized`, `hibernate.*` nastavení a HQL nad rámec JPQL jsou hibernátovské. V .NETu nic takového není — NHibernate ani EF Core nemají společnou specifikaci. Pro F7 a F9 to znamená, že Hibernate a EclipseLink budou mít velkou společnou část, a je otázka rozhodnutí, jestli ji vyjádřit jako sdílený „JPA builder" s tenkými nadstavbami.

---

## Krok 0 — Databáze, přihlášení a TCP

Nejpracnější krok celého tutoriálu a jediný, kde jde o infrastrukturu. Hibernate umí vytvořit tabulky, ne databázi — v tom se chová stejně jako NHibernate a opačně než EF Core.

### 0a — Databáze

Ve Visual Studiu (**View → SQL Server Object Explorer**) nebo v jakémkoli klientovi:

```sql
CREATE DATABASE OrmPlayground_Hibernate;
```

### 0b — TCP/IP

**Tohle je past, kterou v .NETu nikdy nepotkáš.** `Microsoft.Data.SqlClient` se na lokální instanci připojí přes sdílenou paměť i tehdy, když je TCP/IP vypnuté. JDBC ovladač je Type 4 — čistě javový, mluví výhradně TCP. Na SQL Server Express je přitom TCP/IP ve výchozím stavu **vypnuté**.

1. Spusť **SQL Server Configuration Manager** (`SQLServerManager16.msc` pro SQL Server 2022).
2. **SQL Server Network Configuration → Protocols for \<instance\>** → **TCP/IP** → **Enable**.
3. Dvojklik na **TCP/IP → IP Addresses → IPAll**: pokud je vyplněné **TCP Dynamic Ports**, poznamenej si to číslo; jinak nastav **TCP Port** na `1433`.
4. Restartuj službu (PowerShell jako správce): `Restart-Service MSSQLSERVER`, u pojmenované instance `Restart-Service 'MSSQL$SQLEXPRESS'`.

U pojmenované instance se navíc musí připojit služba **SQL Server Browser**, jinak klient nezjistí port. Jednodušší je port v connection stringu uvést natvrdo a Browser neřešit.

### 0c — Přihlášení

Windows autentizace přes JDBC funguje, ale potřebuje nativní knihovnu `mssql-jdbc_auth-<verze>-<arch>.dll` z distribuce ovladače, umístěnou v adresáři z `PATH` nebo v `java.library.path`. Bez ní skončíš na `Unable to load authentication DLL`. Pro hřiště je jednodušší SQL autentizace.

Zapni smíšený režim (jde to i dotazem, SSMS není potřeba):

```sql
EXEC xp_instance_regwrite N'HKEY_LOCAL_MACHINE',
     N'Software\Microsoft\MSSQLServer\MSSQLServer',
     N'LoginMode', REG_DWORD, 2;
```

Restartuj službu (znovu `Restart-Service`) a založ přihlášení:

```sql
CREATE LOGIN ormplayground WITH PASSWORD = N'Zmen.Si.Me.2026', CHECK_POLICY = ON;
GO
USE OrmPlayground_Hibernate;
GO
CREATE USER ormplayground FOR LOGIN ormplayground;
ALTER ROLE db_owner ADD MEMBER ormplayground;
```

> Heslo drž mimo repozitář. Požadavek S4 zakazuje přihlašovací údaje ve zdrojích a logu a jedno takové natvrdo zapsané heslo už v repozitáři jednou bylo. Tenhle projekt je hřiště a do `orm-convertor` nepatří.

---

## Krok 1 — Založení projektu

### IntelliJ IDEA

Od verze 2025.3 je IntelliJ IDEA **jedna distribuce**; Community Edition a Ultimate jako samostatné produkty skončily. Volná úroveň obsahuje všechno, co tenhle tutoriál potřebuje, a proti bývalé Community navíc i prohlížeč databáze a SQL nástroje. Ultimate funkce se dají 30 dní zkoušet a pak zůstane volná úroveň — z IDE tě nikdo nevyhodí.

### Nový projekt

**File → New → Project**, vlevo **Java**:

| Položka | Hodnota |
|---|---|
| Name | `orm-playground-hibernate` |
| Location | mimo repozitář `orm-convertor` |
| Create Git repository | odškrtnuto |
| JDK | **Download JDK…** → verze **25**, vendor Eclipse Temurin (nebo jiný OpenJDK build) |
| Add sample code | odškrtnuto |
| Build system | **Maven** |
| Advanced Settings → GroupId | `cz.stochel.ormplayground` |
| Advanced Settings → ArtifactId | `orm-playground-hibernate` |

JDK není nutné instalovat zvlášť — IntelliJ ho stáhne a zaregistruje sám. Hibernate 7.4 běží na Javě 17, 21, 25 nebo 26; 25 je aktuální LTS.

`groupId` je zhruba to, čemu bys v .NETu říkal kořenový namespace organizace, `artifactId` název assembly. Dvojice `groupId:artifactId` je v Maven Central unikátní.

---

## Krok 2 — Závislosti

Místo NuGetu se edituje `pom.xml` ručně. Nahraď vygenerovaný obsah tímto:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd">
    <modelVersion>4.0.0</modelVersion>

    <groupId>cz.stochel.ormplayground</groupId>
    <artifactId>orm-playground-hibernate</artifactId>
    <version>1.0-SNAPSHOT</version>

    <properties>
        <maven.compiler.release>25</maven.compiler.release>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
    </properties>

    <dependencies>
        <dependency>
            <groupId>org.hibernate.orm</groupId>
            <artifactId>hibernate-core</artifactId>
            <version>7.4.5.Final</version>
        </dependency>
        <dependency>
            <groupId>com.microsoft.sqlserver</groupId>
            <artifactId>mssql-jdbc</artifactId>
            <version>13.4.0.jre11</version>
        </dependency>
    </dependencies>
</project>
```

Po uložení klikni v panelu **Maven** vpravo na ikonu obnovení (nebo potvrď nabídku „Load Maven Changes"). Tím se závislosti stáhnou do lokálního repozitáře `~/.m2/repository` — obdoba globální NuGet cache.

**Klasifikátor `jre11`** neznamená, že potřebuješ Javu 11. Ovladač se vydává ve dvou variantách, `jre8` pro Javu 8 a `jre11` pro Javu 11 a novější; na Javě 25 je správně `jre11`.

**Dva balíčky, stejně jako u NHibernate a Dapperu, ale zase z jiného důvodu.** U NHibernate byl ADO.NET provider potřeba kvůli reflexi driveru, u Dapperu proto, že `SqlConnection` instancuješ sám. Tady je to JDBC ovladač, který se registruje přes `ServiceLoader` — stačí, že je na classpath, a v kódu se o něm nikde nemluví. Ani v konfiguraci se neuvádí jeho třída, na rozdíl od NHibernate, kde jsi psal `db.Driver<MicrosoftDataSqlClientDriver>()`.

`hibernate-core` je jen jádro; celý ekosystém je rozdělený na moduly (`hibernate-envers` pro audit, `hibernate-spatial`, `hibernate-vector`, `hibernate-jcache`, poolery jako `hibernate-hikaricp`). Pro srovnávací tabulku „doplňkové balíčky v ekosystému" je to bohatší škála než u NHibernate.

---

## Krok 3 — Doménové třídy

Vytvoř package `cz.stochel.ormplayground.hibernate.domain` (pravým na `src/main/java` → **New → Package**) a v něm dvě třídy. Anotace řeší krok 4, tady si všímej tvaru tříd.

**`Author.java`**

```java
package cz.stochel.ormplayground.hibernate.domain;

import jakarta.persistence.*;
import java.time.LocalDate;
import java.util.LinkedHashSet;
import java.util.Set;

@Entity
@Table(name = "Authors")
public class Author {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @Column(name = "AuthorId")
    private Integer id;

    @Column(name = "Name", length = 200, nullable = false)
    private String name;

    @Column(name = "BornOn")
    private LocalDate bornOn;

    @OneToMany(mappedBy = "author", cascade = CascadeType.ALL, orphanRemoval = true)
    private Set<Book> books = new LinkedHashSet<>();

    public void addBook(Book book) {
        book.setAuthor(this);
        books.add(book);
    }

    public Integer getId() { return id; }
    public void setId(Integer id) { this.id = id; }

    public String getName() { return name; }
    public void setName(String name) { this.name = name; }

    public LocalDate getBornOn() { return bornOn; }
    public void setBornOn(LocalDate bornOn) { this.bornOn = bornOn; }

    public Set<Book> getBooks() { return books; }
}
```

**`Book.java`**

```java
package cz.stochel.ormplayground.hibernate.domain;

import jakarta.persistence.*;
import java.math.BigDecimal;

@Entity
@Table(name = "Books")
public class Book {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @Column(name = "BookId")
    private Integer id;

    @Column(name = "Title", length = 300, nullable = false)
    private String title;

    @Column(name = "PublishedYear", nullable = false)
    private int publishedYear;

    @Column(name = "Price", precision = 18, scale = 2, nullable = false)
    private BigDecimal price;

    @ManyToOne(optional = false, fetch = FetchType.LAZY)
    @JoinColumn(name = "AuthorId", nullable = false)
    private Author author;

    public Integer getId() { return id; }
    public void setId(Integer id) { this.id = id; }

    public String getTitle() { return title; }
    public void setTitle(String title) { this.title = title; }

    public int getPublishedYear() { return publishedYear; }
    public void setPublishedYear(int publishedYear) { this.publishedYear = publishedYear; }

    public BigDecimal getPrice() { return price; }
    public void setPrice(BigDecimal price) { this.price = price; }

    public Author getAuthor() { return author; }
    public void setAuthor(Author author) { this.author = author; }
}
```

Gettery a settery ti IntelliJ vygeneruje přes **Alt+Insert → Getter and Setter**. Java nemá vlastnosti jako jazykový prvek, takže tenhle boilerplate je normální stav, ne opomenutí.

### Čeho si všimnout

**`virtual` odpadlo.** Hibernate stejně jako NHibernate generuje proxy potomka, ale v Javě jsou metody virtuální implicitně. Požadavek se tím obrátil: nikoli „označ členy jako `virtual`", nýbrž „neoznačuj třídu ani metody jako `final`". Pro převodník je podstatné, že jde o **jazykový**, ne frameworkový rozdíl — do kategorie „členy vynucené frameworkem" tedy patří, ale jako položka závislá na cílovém jazyce.

**Bezparametrický konstruktor je pořád povinný**, přesně jako u NHibernate. Tady je implicitní, protože žádný jiný nedefinuješ.

**`Integer` místo `int` u klíče.** Java nemá nullable hodnotové typy; nullabilita se vyjadřuje volbou mezi primitivem a obalovým typem. Klíč musí být `Integer`, aby šel před insertem rozeznat nepřiřazený stav. Naopak `publishedYear` je `int`, protože je vždycky vyplněný. **Tohle je třetí mechanismus nullability ve čtyřech tutoriálech**: NHibernate ji čte z `not-null` v XML, EF Core z nullable reference types a `?`, JPA z atributu `nullable` — a Java k tomu přidává volbu primitiv/obal na jazykové úrovni.

**`BigDecimal` místo `decimal`.** V C# je `decimal` hodnotový typ zabudovaný v jazyce, v Javě jde o třídu z knihovny. Sémanticky si odpovídají, reprezentací ne.

**`LocalDate` místo `DateTime?`.** Tohle je jediné místo, kde se doména proti .NET tutoriálům schválně liší. C# `DateTime` má v Javě dva protějšky — `LocalDate` (jen datum) a `LocalDateTime` (datum a čas) — a v DDL se to projeví: `date` proti `datetime2`. Až budeš mít vygenerované DDL, zkus si přepsat typ na `LocalDateTime` a rozdíl si prohlédnout. Je to nejlevnější dostupný důkaz, že typový model IR nemůže zůstat CLR-specifický.

**`Set` proti `List`.** Typ kolekce má v JPA sémantický význam úplně stejně jako u NHibernate: `Set` vylučuje duplicity, `List` je bez `@OrderColumn` vlastně bag a s ním nese pořadí. Otevřená položka „NHibernate builder — kolekce jen jako `<bag>`" tedy není specifikum NHibernate; volbu tvaru kolekce bude potřeba nést v IR i kvůli Javě.

**`fetch = FetchType.LAZY` u `@ManyToOne`.** Výchozí hodnota je v JPA u `@ManyToOne` a `@OneToOne` `EAGER`, u `@OneToMany` a `@ManyToMany` `LAZY`. Ta asymetrie je notoricky známý zdroj problémů a je dobré ji do srovnání zaznamenat — ale do IR nepatří, článek strategie načítání explicitně vylučuje (§5.4).

---

## Krok 4 — Mapování

Mapování je v anotacích na entitě, tedy tvarem nejblíž data annotations z EF Core. Rozdíl je v tom, že u EF Core jsou anotace jednou ze tří vrstev a fluent API je nadřazené, kdežto v JPA jsou anotace hlavní forma.

### Vrstvy

| | NHibernate | EF Core | Hibernate / JPA |
|---|---|---|---|
| Vrstvy | jedna (XML) | tři (konvence → annotations → fluent) | dvě a půl (konvence → anotace → `orm.xml`) |
| Poslední slovo má | XML | fluent API | `orm.xml`, pokud existuje |
| Model bez konfigurace | nefunguje | funguje | funguje |

`orm.xml` je standardní JPA deskriptor, kterým se dají anotace přebít nebo nahradit. V praxi se používá málo, ale existuje — a pro převodník je důležitý, protože je to standardní, na implementaci nezávislá cesta, jak mapování vyjádřit mimo kód.

### Konvence

Bez anotací by platily tyto defaulty:

| Co | Default |
|---|---|
| Tabulka | název entity, tedy `Author` (ne `Authors` — žádná pluralizace) |
| Sloupec | název atributu, tedy `id`, `bornOn` |
| Primární klíč | **žádný default**, `@Id` je povinné |
| FK sloupec u `@ManyToOne` | `<názevAtributu>_<názevPKSloupce>`, tedy `author_AuthorId` |
| Přístup k hodnotě | podle umístění `@Id` — na poli přístup přes pole, na getteru přes vlastnost |

Poslední řádek nemá v .NETu obdobu a stojí za zapamatování: **umístěním jediné anotace určuješ způsob přístupu pro celou třídu.** Když dáš `@Id` na pole a `@Column` na getter, druhá anotace se tiše ignoruje. Parser musí tenhle implicitní default materializovat stejně jako názvy tabulek.

Pojmenovací konvence jsou navíc v Hibernate **vyměnitelné** přes `hibernate.implicit_naming_strategy` a `hibernate.physical_naming_strategy`. Většina javových tutoriálů na internetu předpokládá Spring Boot, který dosazuje strategii převádějící `bornOn` na `born_on` — proto v nich DDL vypadá jinak než tady. Holý Hibernate nic takového nedělá.

Pro převodník je to konkrétní zjištění: **znát cílovou verzi frameworku nestačí, parser i builder musí znát i nastavenou pojmenovací strategii**, jinak defaulty materializuje špatně. Patří to k otevřenému rozhodnutí o deklaraci cílových verzí.

### Vlastnictví vztahu

`mappedBy = "author"` na `@OneToMany` je přesný protějšek `inverse="true"` z NHibernate: říká, že vlastníkem vztahu je strana `Book` a FK sloupec zapisuje ona. `@JoinColumn` na druhé straně pojmenovává sloupec.

Tady se to konečně sešlo — `Relation` s rolemi `Owning`/`Inverse` v IR má ve všech třech plnohodnotných frameworcích přímý protějšek a JSS článek (§5.2) vlastnictví vyžaduje z téhož důvodu: generovat kód umí jen jedna autoritativní strana. Jediný, kdo vlastnictví nezná, je Dapper, protože nezná vztahy.

`cascade = CascadeType.ALL` s `orphanRemoval = true` odpovídá NHibernátovskému `cascade="all-delete-orphan"`.

---

## Krok 5 — Registrace entit a zdroje na classpath

Protějšek kroku, na kterém padají NHibernate tutoriály. Rozpadá se na dvě části a **obě jsou jednodušší**.

**Zdroje na classpath.** Cokoli v `src/main/resources` se do výsledného artefaktu dostane samo. Žádný `<EmbeddedResource>`, žádný zásah do `pom.xml`. Ekvivalent chyby „mapování se nenačetlo, protože chybí build action" v Javě nevzniká.

**Registrace entit.** Hibernate musí vědět, které třídy jsou entity. Máš tři možnosti:

1. `addAnnotatedClass(Author.class)` při bootstrapu — použijeme ji v kroku 6, je nejexplicitnější a IntelliJ ji kontroluje;
2. `<class>` v `META-INF/persistence.xml`, pokud jedeš přes JPA bootstrap;
3. automatické skenování classpath, které si v Hibernate 7 navíc vyžádá modul `hibernate-scan-jandex`.

Proti NHibernate, kde `AddAssembly` prohledá celou assembly, je varianta 1 upovídanější, zato se překlep projeví při kompilaci a ne až za běhu.

---

## Krok 6 — Konfigurace a bootstrap

Hibernate má konfiguraci **na třech místech**: v kódu, v souboru `hibernate.properties` na classpath, nebo v `META-INF/persistence.xml`. Zvolíme kód, protože je nejblíž `SessionFactoryBuilder.cs` z NHibernate tutoriálu a jde do něj nejlíp koukat.

Vytvoř package `cz.stochel.ormplayground.hibernate.persistence`.

**`SessionFactoryBuilder.java`**

```java
package cz.stochel.ormplayground.hibernate.persistence;

import cz.stochel.ormplayground.hibernate.domain.Author;
import cz.stochel.ormplayground.hibernate.domain.Book;
import org.hibernate.SessionFactory;
import org.hibernate.boot.MetadataSources;
import org.hibernate.boot.registry.StandardServiceRegistry;
import org.hibernate.boot.registry.StandardServiceRegistryBuilder;

public final class SessionFactoryBuilder {

    // Uprav podle svojí instance. U pojmenovane instance pouzij port
    // z Configuration Manageru, napr. localhost:52698.
    private static final String JDBC_URL =
            "jdbc:sqlserver://localhost:1433"
                    + ";databaseName=OrmPlayground_Hibernate"
                    + ";encrypt=true"
                    + ";trustServerCertificate=true";

    private SessionFactoryBuilder() {
    }

    public static SessionFactory build() {
        StandardServiceRegistry registry = new StandardServiceRegistryBuilder()
                .applySetting("jakarta.persistence.jdbc.url", JDBC_URL)
                .applySetting("jakarta.persistence.jdbc.user", "ormplayground")
                .applySetting("jakarta.persistence.jdbc.password", "Zmen.Si.Me.2026")

                // Vypis SQL do konzole.
                .applySetting("hibernate.show_sql", "true")
                .applySetting("hibernate.format_sql", "true")
                .applySetting("hibernate.highlight_sql", "true")

                // Dropne a znovu vytvori tabulky.
                .applySetting("jakarta.persistence.schema-generation.database.action",
                              "drop-and-create")

                // Zaroven zapise DDL do souboru, bez ohledu na to,
                // jestli se provede proti databazi.
                .applySetting("jakarta.persistence.schema-generation.scripts.action",
                              "create")
                .applySetting("jakarta.persistence.schema-generation.scripts.create-source",
                              "metadata")
                .applySetting("jakarta.persistence.schema-generation.scripts.create-target",
                              "schema.sql")
                .build();

        try {
            return new MetadataSources(registry)
                    .addAnnotatedClass(Author.class)
                    .addAnnotatedClass(Book.class)
                    .buildMetadata()
                    .buildSessionFactory();
        } catch (Exception e) {
            StandardServiceRegistryBuilder.destroy(registry);
            throw e;
        }
    }
}
```

### Čeho si všimnout

**Chybí dialekt.** Od Hibernate 6 reprezentuje jedna třída dialektu všechny verze daného produktu a Hibernate si ji vybere z JDBC metadat, včetně verze serveru. Nastavení `hibernate.dialect` je pro podporované databáze zbytečné. Proti `db.Dialect<MsSql2012Dialect>()` u NHibernate je to podstatný rozdíl: **cílovou verzi databáze si framework zjistí sám za běhu, místo aby ji čekal v konfiguraci.**

Pro převodník je to nepříjemné zjištění: informace, kterou u NHibernate parser přečte z konfigurace, u Hibernate v kódu vůbec není a bez připojení k databázi ji nelze doplnit. To je další argument pro F4–F6.

**Chybí ovladač.** JDBC ovladače se registrují samy přes `ServiceLoader`, jakmile jsou na classpath.

**Názvy nastavení mají dvě podoby.** `jakarta.persistence.jdbc.url` je standard JPA, `hibernate.connection.url` hibernátovský ekvivalent. Standardní varianty fungují i v EclipseLinku — pro F9 se vyplatí držet se jich a odchylky evidovat.

**Generování schématu je standardní.** Vlastnosti `jakarta.persistence.schema-generation.*` definuje specifikace, takže stejný mechanismus bude fungovat i pro EclipseLink. Hibernátovská zkratka `hibernate.hbm2ddl.auto=create-drop` dělá totéž, ale jen tady.

**`schema.sql` vzniká i bez připojení.** S `scripts.create-source=metadata` se DDL odvodí z mapování, ne z databáze. Je to protějšek `GenerateCreateScript()` z EF Core a `SchemaExport(cfg).Create(action, false)` z NHibernate — s tím rozdílem, že tady je to standardní vlastnost JPA, ne API konkrétního frameworku. Soubor se objeví v pracovním adresáři, což je u spuštění z IntelliJ kořen projektu.

**Connection string.** `trustServerCertificate=true` platí ze stejného důvodu jako `TrustServerCertificate=True` v .NETu — lokální server má self-signed certifikát. Hláška je ale jiná: Java certifikát ověřuje proti vlastnímu truststoru, takže dostaneš `PKIX path building failed` místo .NETové zprávy o selhání přihlášení. Stejná příčina, jiný projev; do srovnávací tabulky diagnostiky se to hodí.

---

## Krok 7 — Vytvoření schématu a první data

**`Program.java`** v package `cz.stochel.ormplayground.hibernate`:

```java
package cz.stochel.ormplayground.hibernate;

import cz.stochel.ormplayground.hibernate.domain.Author;
import cz.stochel.ormplayground.hibernate.domain.Book;
import cz.stochel.ormplayground.hibernate.persistence.SessionFactoryBuilder;
import org.hibernate.SessionFactory;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.Comparator;
import java.util.List;

public final class Program {

    public static void main(String[] args) {
        try (SessionFactory sessionFactory = SessionFactoryBuilder.build()) {

            // --- Zapis ---
            sessionFactory.inTransaction(session -> {
                Author capek = new Author();
                capek.setName("Karel Capek");
                capek.setBornOn(LocalDate.of(1890, 1, 9));
                capek.addBook(book("R.U.R.", 1920, "249.00"));
                capek.addBook(book("War with the Newts", 1936, "329.00"));

                Author orwell = new Author();
                orwell.setName("George Orwell");
                orwell.setBornOn(LocalDate.of(1903, 6, 25));
                orwell.addBook(book("1984", 1949, "299.00"));

                session.persist(capek);
                session.persist(orwell);
            });

            System.out.println("\n--- Query: authors with a book published before 1930 ---\n");

            // --- Cteni ---
            sessionFactory.inTransaction(session -> {
                List<Author> authors = session.createSelectionQuery("""
                        select a
                        from Author a
                        left join fetch a.books
                        where exists (
                            select b from Book b
                            where b.author = a and b.publishedYear < 1930
                        )
                        """, Author.class).getResultList();

                for (Author author : authors) {
                    System.out.printf("%s (%d books)%n", author.getName(), author.getBooks().size());
                    author.getBooks().stream()
                            .sorted(Comparator.comparingInt(Book::getPublishedYear))
                            .forEach(b -> System.out.printf("  %d  %-25s %8.2f%n",
                                    b.getPublishedYear(), b.getTitle(), b.getPrice()));
                }
            });
        }
    }

    private static Book book(String title, int year, String price) {
        Book book = new Book();
        book.setTitle(title);
        book.setPublishedYear(year);
        book.setPrice(new BigDecimal(price));
        return book;
    }
}
```

### Čeho si všimnout

**`persist` místo `Save`.** Jinak je model práce se session shodný s NHibernate: entita se předá session, změny se hlídají dirty checkingem a zapíšou při commitu. Explicitní transakce je nutná stejně jako u NHibernate a na rozdíl od EF Core; `inTransaction` je jen pohodlný obal, který ji otevře a zavře.

**Žádný `SaveChanges`.** V tomhle je Hibernate blíž NHibernate než EF Core: neexistuje jeden okamžik „ulož změny", flush proběhne při commitu sám.

**`join fetch` místo `FetchMany` a `Include`.** Sémanticky totéž, potřetí jiné jméno.

**Žádné `distinct`.** Od Hibernate 6 se duplicitní kořenové entity z fetch joinu filtrují vždycky a `distinct` se naopak propisuje až do SQL, takže by tam přidalo zbytečné `DISTINCT`. Postav si to vedle Dapperu, kde deduplikaci děláš ručně přes dictionary — je to dobrý příklad toho, že „stejný dotaz" znamená v každém frameworku jinou míru ruční práce.

**`select a` je napsané schválně.** HQL by ho nechalo vynechat, JPQL ne. Držet se přenositelné podmnožiny se pro F9 vyplatí a stojí to jedno slovo.

---

## Krok 8 — Spuštění

Zelená šipka u `main`, nebo **Shift+F10**. V konzoli bys měl vidět:

1. **Varování o zabudovaném poolu připojení** (`not intended for production use`) — neškodné, Hibernate na hřišti používá vlastní primitivní pool.
2. **Chyby při dropování tabulek** při prvním spuštění — taky neškodné, `drop-and-create` se pokusí zahodit tabulky, které ještě neexistují. Hibernate je jen zaloguje a pokračuje.
3. **DDL** — a tohle si přečti pozorně, je to hlavní výstup celého tutoriálu. Porovnej ho s výstupem NHibernate, EF Core a s ručním DDL z Dapper tutoriálu. Nejzajímavější místa: typ sloupce `Name`, typ `BornOn` a pojmenování FK constraintu.
4. **INSERT příkazy** — všimni si pořadí a toho, že se díky `mappedBy` neposílá dodatečný `UPDATE`, přesně jako u `inverse="true"`.
5. **SELECT** s `left outer join`, který vznikl z `join fetch`.
6. Výpis obou autorů s knihami.

### Co v DDL hledat

**`varchar` proti `nvarchar`.** Hibernate mapuje `String` ve výchozím stavu na `varchar`. Nationalizované typy se vyžádají anotací `@Nationalized` na atributu nebo globálně nastavením `hibernate.use_nationalized_character_data=true`. Oba .NET frameworky přitom generují `nvarchar` bez ptaní.

Je to nejostřejší zatím doložený doklad pro rozhodnutí o neutralizaci typového modelu: **stejný jazykový typ na stejné databázi dá v .NETu a v Javě jiný sloupec.** `DatabaseType` v dnešní podobě, tedy fakticky výčet typů T-SQL, tenhle rozdíl neumí zaznamenat jako volbu — vypadá v něm jako dvě různé hodnoty bez vztahu, přestože jde o jeden typ ve dvou nationalizačních režimech.

Mimochodem, tady je i konkrétní odpověď na otevřenou otázku z auditu, proč `CLRType.Char` nejde namapovat: `DatabaseType` nemá hodnotu pro jednotlivý unicode znak. Hibernate stejný problém řeší tím, že nationalizaci drží jako vlastnost mapování, ne jako jiný typ.

---

## Krok 9 — Bind parametry v logu (nepovinné)

`hibernate.show_sql` vypisuje SQL s otazníky, ne s hodnotami — je to obdoba `LogSqlInConsole` z NHibernate a chybí mu to, co u EF Core umí `EnableSensitiveDataLogging`. Hodnoty parametrů se logují zvlášť a potřebují k tomu logovací backend, protože Hibernate sám o sobě žádný nemá.

Přidej do `pom.xml` `org.slf4j:slf4j-simple` (nejnovější 2.x; verzi nech doplnit IntelliJ přes **Alt+Insert → Add dependency**, do zafixovaných verzí projektu tenhle balíček nepatří) a do `src/main/resources` soubor `simplelogger.properties`:

```properties
org.slf4j.simpleLogger.log.org.hibernate.SQL=debug
org.slf4j.simpleLogger.log.org.hibernate.orm.jdbc.bind=trace
```

Kategorie `org.hibernate.orm.jdbc.bind` platí od Hibernate 6; starší návody uvádějí `org.hibernate.type.descriptor.sql`, což je varianta pro Hibernate 5 a tady nefunguje.

---

## Typické chyby

| Hláška | Příčina |
|---|---|
| `java: invalid source release: 25` | `maven.compiler.release` neodpovídá zvolenému JDK; sjednoť v **File → Project Structure** |
| `package jakarta.persistence does not exist` | závislosti se nestáhly — obnov Maven projekt v panelu vpravo |
| `The TCP/IP connection to the host ... has failed` | vypnuté TCP/IP nebo špatný port (krok 0b) |
| `PKIX path building failed` / `Could not establish secure connection` | chybí `trustServerCertificate=true` |
| `Login failed for user 'ormplayground'` | nezapnutý smíšený režim, nerestartovaná služba, nebo špatné heslo (krok 0c) |
| `Unable to load authentication DLL mssql-jdbc_auth` | `integratedSecurity=true` bez nativní knihovny — použij SQL autentizaci |
| `Cannot open database "OrmPlayground_Hibernate"` | databáze neexistuje; Hibernate tvoří tabulky, ne databázi (krok 0a) |
| `Unable to determine Dialect without JDBC metadata` | spojení selhalo dřív, než mohl dialekt vzniknout — skutečná příčina je vždy o řádek výš |
| `AnnotationException: No identifier specified for entity` | chybí `@Id` |
| `Unknown entity: ...Author` | třída se nezaregistrovala přes `addAnnotatedClass` |
| `HHH90000028: Support for <hibernate-mappings/> is deprecated` | zkoušel jsi `hbm.xml` — v 7.x zavržené, v 8.0 odstraněné |
| `LazyInitializationException` | přístup ke kolekci mimo otevřenou session — stejná chyba i stejný název jako u NHibernate |
| `Invalid object name 'Authors'` | schéma nevzniklo, nebo běžíš proti jiné databázi |
| `NullPointerException` nad `BigDecimal` | `null` v primitivu; zkontroluj, jestli nemáš `int` tam, kde má být `Integer` |

---

## Srovnání všech čtyř

| | NHibernate | EF Core | Dapper | Hibernate |
|---|---|---|---|---|
| Ekosystém | .NET | .NET | .NET | Java |
| Standardizační vrstva | — | — | — | **Jakarta Persistence 3.2** |
| Balíčky | 2 | 1 | 2 | 2 |
| Zásah do build souboru | nutný (`EmbeddedResource`) | žádný | žádný | žádný |
| Požadavky na entity | `virtual`, bezparam. ctor | žádné | žádné | bezparam. ctor, ne `final` |
| Mapování | `hbm.xml` | konvence + annotations + fluent | aliasy v SQL | konvence + anotace + `orm.xml` |
| Deklarativní místo pro mapování | ano, jedno | ano, jedno | **není** | ano, jedno |
| Vyměnitelná pojmenovací strategie | ne | částečně (vlastní konvence) | — | **ano** (`*_naming_strategy`) |
| Určení přístupu k hodnotě | `access` per vlastnost | vždy vlastnost/pole podle konvence | vlastnost | **umístěním `@Id` pro celou třídu** |
| Dialekt | nutný v konfiguraci | nutný (volba provideru) | žádný | **odvozený za běhu** |
| Ovladač databáze | v konfiguraci | v provideru | v kódu | sám přes classpath |
| Vytvoření databáze | ne | ano | ne | ne |
| Vytvoření tabulek | `SchemaExport` | `EnsureCreated` | ručně psané DDL | `schema-generation.database.action` |
| DDL bez provedení | `SchemaExport(cfg).Create(a, false)` | `GenerateCreateScript()` | triviálně | `schema-generation.scripts.action` (**standard JPA**) |
| Znalost primárního klíče | ano | ano | **ne** | ano |
| Znalost relací | ano | ano | **ne** | ano |
| Vlastnictví vztahu | `inverse="true"` | `HasOne/WithMany` | — | `mappedBy` |
| Ukládání | `Save`/`Update`/`Merge` | `SaveChanges` nad grafem | ruční SQL | `persist`, flush při commitu |
| Explicitní transakce | nutná | volitelná | ruční | nutná |
| Eager loading | `FetchMany` | `Include` | ruční JOIN + dictionary | `join fetch` |
| Deduplikace u fetch joinu | ruční `distinct` | automatická | ruční dictionary | **automatická od v6** |
| `string` → SQL typ | `nvarchar` | `nvarchar` | dle DDL | **`varchar`** |
| Nullabilita se bere z | `not-null` v XML | NRT a `?` | DDL | `nullable` + primitiv/obal |
| Dotazovací jazyk | LINQ, HQL | LINQ | SQL | HQL, JPQL, Criteria |

---

## Co z toho plyne pro převodník

Pět zjištění, která mají přímý dopad na otevřené položky. Žádné z nich není překvapivé, ale všechna jsou teď doložená na běžícím kódu, ne odvozená z dokumentace.

**1. Typový model musí být neutrální, a je to horší, než vypadalo.** Neutralizace se netýká jen jazykové strany (`CLRType` → `LangType`), ale i databázové. Jeden a týž `String`/`string` dá na stejném SQL Serveru `varchar` z Hibernate a `nvarchar` z obou .NET frameworků. `DatabaseType` jako výčet typů T-SQL to zachytí jen jako dvě nesouvisející hodnoty, přestože jde o jeden typ ve dvou režimech. Otevřená položka o neutralizaci tím dostává druhou, konkrétnější polovinu.

**2. Klíčová třída je v JPA povinná, nikoli volitelná.** Rozhodnutí 006 zvolilo ploché vykreslení a `@IdClass` jako javový cíl. Ověření: u `@IdClass` jsou klíčové atributy skutečně přímo na entitě, takže cesty k vlastnostem v dotazech zůstávají ploché — v tomhle rozhodnutí obstálo. Zároveň ale JPA **vždycky** vyžaduje samostatnou třídu klíče, i u ploché varianty; není to volba jako u NHibernate, kde `<composite-id>` s `<key-property>` žádnou třídu nepotřebuje. Builder ji tedy bude muset syntetizovat vždy, ne jen někdy, a to potvrzuje směr otevřeného rozhodnutí o členech vynucených frameworkem.

**3. Vynucené členy závisí na cílovém jazyce, ne jen na frameworku.** `virtual` u NHibernate a „ne `final`" u Hibernate je tentýž požadavek proxy mechanismu, vyjádřený opačně jen proto, že C# a Java mají opačný default. Kategorie „členy vynucené frameworkem" tedy potřebuje osu jazyka.

**4. Cílová verze frameworku nestačí.** K deterministickému generování je u Hibernate potřeba znát i pojmenovací strategii a nationalizační režim. Otevřené rozhodnutí o deklaraci cílových verzí by mělo počítat s tím, že jde spíš o **profil cílového frameworku** než o jedno číslo verze.

**5. JPA je společný jmenovatel dvou ze tří javových frameworků.** Hibernate a EclipseLink jsou implementace téže specifikace. Kde to jde, vyplatí se generovat standardní JPA (`jakarta.persistence.*` anotace i nastavení) a implementačně specifické věci držet jako tenkou nadstavbu. To může výrazně zlevnit F9 proti F7 — a je to samostatné rozhodnutí, které je namístě sepsat dřív, než se začne psát první javový builder. MyBatis (F8) tuhle výhodu nemá; ten je javovým protějškem Dapperu.

---

## Kam pokračovat

**1. Kompozitní klíč.** `BookTranslation` s klíčem `(BookId, LanguageCode)` v obou formách:

```java
@Entity
@IdClass(BookTranslationId.class)
public class BookTranslation {
    @Id private Integer bookId;
    @Id private String languageCode;
    // ...
}
```

Klíčová třída musí být veřejná, mít bezparametrický konstruktor, implementovat `Serializable` a přepsat `equals` i `hashCode` — tedy stejná sada požadavků, jakou vynucuje NHibernate na entitě u `<composite-id>`, jen umístěná jinam. Zkus si pak i `@EmbeddedId` a všimni si, jak se změní cesta k vlastnosti v HQL (`bt.bookId` proti `bt.id.bookId`). Přesně tenhle rozdíl je důvod, proč rozhodnutí 006 zvolilo `@IdClass`; teď si ho můžeš ověřit na běžícím kódu.

**2. N:M.** `Book` ↔ `Category` přes `@ManyToMany` s `@JoinTable`. Junction tabulka nemá vlastní entitu — stejně jako u NHibernate a EF Core a opačně, než co generuje IR podle rozhodnutí [005](../../decisions/005-many-to-many-as-explicit-junction-entity.md). Tři ze čtyř frameworků tedy junction entitu nemají, což stojí za to v textu práce zmínit: rozhodnutí 005 je vědomá volba za cenu odchylky od zvyklostí, ne pohodlnější cesta.

**3. Dědičnost.** `@Inheritance(strategy = ...)` se `SINGLE_TABLE`, `JOINED` a `TABLE_PER_CLASS` odpovídá `<subclass>`, `<joined-subclass>` a `<union-subclass>` z NHibernate a TPH/TPT/TPC z EF Core skoro 1:1. Ze všech probraných témat je tohle nejlepší kandidát na kapitolu, kde se ukáže, že jednotná IR je realistická.

**4. Zkus JPA bootstrap.** Přepiš krok 6 na `META-INF/persistence.xml` a `Persistence.createEntityManagerFactory(...)`. Uvidíš, kolik z tutoriálu je hibernátovské a kolik standardní — a je to nutný předstupeň, než se sáhne na EclipseLink.

**5. Sepiš srovnání typů.** Máš teď čtyři implementace téže domény a čtyři DDL skripty. Tabulka „jazykový typ → typ v mapování → SQL typ" pro všechny čtyři frameworky je nejpřímější podklad pro rozhodnutí o `LangType` a zároveň text, který půjde do práce skoro beze změny.
