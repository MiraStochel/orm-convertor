# EclipseLink od nuly — Docker, JDK 25, Maven, MS SQL Server

Pátý díl a druhý javový. Stejná doména jako ve všech předchozích (`Author` 1:N `Book`), stejný SQL Server, stejné číslování kroků.

Proti dílu k Hibernate se mění **jen framework**, ne ekosystém — a to je na tomhle dílu to cenné. Hibernate i EclipseLink jsou implementace téže Jakarta Persistence 3.2, takže kroky 3 a 4 jsou **znak po znaku shodné**: tytéž třídy, tytéž anotace. Všechno, v čem se oba díly liší, je tedy rozdíl implementací, ne rozdíl jazyka ani specifikace. Pro požadavky F7 a F9 je to přesně ta hranice, kterou potřebujeme znát: co smí vyslovit sdílená JPA vrstva a co musí zůstat v nadstavbě.

Ověřené verze: EclipseLink **5.0.0** (build `5.0.0.v202603230926-bc4f4eb36eb1dcce223533d87890ba458628a2f9`), Jakarta Persistence **3.2**, JDK **25** (Temurin 25.0.1+8), Maven **3.9.11**, `mssql-jdbc` **13.4.0.jre11**, MS SQL Server **2022**. Všechno běželo v obrazu `maven:3.9.11-eclipse-temurin-25-noble`, viz krok 0.

---

## Než začneš: dvě osy rozdílů

### Osa 1 — prostředí

**Tenhle díl neběží v IDE, ale v kontejneru.** Díl k Hibernate předpokládal IntelliJ IDEA s vlastním JDK a zabudovaným Mavenem; tady je jediný předpoklad Docker a na hostiteli není potřeba ani JDK, ani Maven, ani SQL Server.

Důvod je metodický, ne pohodlnost: javová strana řešení zatím neexistuje, kudy vstoupí, říká rozhodnutí [076](../../decisions/076-java-wrappers-in-csharp-jvm-in-containers.md), a kontejnerová konfigurace prostředí je rozhodnutí [039](../../decisions/039-container-configuration-of-the-environment.md). Běh v obrazu s pevnou verzí je doložitelný na cizím stroji přesně tak, jak je zapsaný, což u „nainstaluj si JDK a klikni na zelenou šipku" neplatí.

| Díl k Hibernate | Tenhle díl |
|---|---|
| IntelliJ IDEA stáhne JDK | JDK je v obrazu |
| Maven zabudovaný v IDE | Maven je v obrazu |
| SQL Server Express na hostiteli | SQL Server v kontejneru |
| TCP/IP se musí zapnout v Configuration Manageru | v obraze je zapnuté |
| smíšený režim přihlášení se musí zapnout | v obraze je zapnutý |
| spuštění zelenou šipkou | `docker run … mvn compile exec:java` |

Kroky 3 až 7 — tedy všechno, co je o frameworku — tím **nejsou dotčené**. Liší se jen kroky 0, 1 a 8.

### Osa 2 — framework

Obě implementace stojí na Jakarta Persistence 3.2, takže shod je drtivá většina. Zajímavé je, kde se rozcházejí, a všech šest řádků níž je v tomhle dílu doložených během:

| | Hibernate 7.4.5 | EclipseLink 5.0.0 |
|---|---|---|
| Bootstrap | typicky v kódu (`StandardServiceRegistryBuilder`) | typicky `META-INF/persistence.xml` |
| `@GeneratedValue` bez strategie | sekvence `<Entita>_SEQ`, krok 50 | tabulka `SEQUENCE` se sloupci `SEQ_NAME`, `SEQ_COUNT` |
| Název tabulky a sloupce bez `@Table`/`@Column` | název entity a atributu, velikost písmen zachovaná | **velkými písmeny** |
| Duplicitní rodiče z `join fetch` kolekce | odfiltruje framework | **musí odfiltrovat autor** přes `distinct` |
| `fetch = LAZY` na `@ManyToOne` | proxy, funguje samo | **funguje jen s weavingem**, jinak se tiše načte eager |
| DDL do skriptu bez připojení | žádá `hibernate.dialect` | žádá `jakarta.persistence.database-product-name` |

Poslední řádek je drobnost, ale stojí za zapamatování: **generování DDL bez databáze je standardní vlastnost JPA, kdežto to, čím se implementaci řekne cílová databáze, standardní není.** Jedna půlka mechanismu je přenositelná a druhá ne.

---

## Krok 0 — Databáze, síť a přihlášení

Nejpracnější krok dílu k Hibernate se tady scvrkl na tři příkazy, protože obraz SQL Serveru má TCP/IP i smíšený režim zapnuté od začátku. Past z kroku 0b dílu k Hibernate — JDBC ovladač je Type 4 a mluví výhradně TCP, kdežto `Microsoft.Data.SqlClient` se na lokální instanci dostane i sdílenou pamětí — v kontejneru nevzniká.

### 0a — Síť

Aplikace i databáze poběží každá ve svém kontejneru, takže potřebují společnou síť. Na ní se kontejnery adresují jménem, ne přes `localhost`:

```sh
docker network create ormplayground
```

### 0b — SQL Server

```sh
docker run -d --name ormplayground-sql --network ormplayground -p 11433:1433 \
  -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Playground.2026" -e MSSQL_PID=Developer \
  mcr.microsoft.com/mssql/server:2022-latest
```

Je to týž obraz, jaký používá compose konfigurace řešení (rozhodnutí [039](../../decisions/039-container-configuration-of-the-environment.md)). Port `11433` na hostiteli je jen proto, aby sis mohl do databáze kouknout klientem z Windows; kontejnery mezi sebou jedou po `1433`.

### 0c — Databáze a přihlášení

EclipseLink stejně jako Hibernate umí vytvořit tabulky, ne databázi — v tom se chovají všechny tři javové frameworky stejně a opačně než EF Core.

```sh
docker exec ormplayground-sql bash -c "/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Playground.2026' -C -Q \"
    CREATE DATABASE OrmPlayground_EclipseLink;
    CREATE LOGIN ormplayground WITH PASSWORD = 'Zmen.Si.Me.2026', CHECK_POLICY = OFF;\""

docker exec ormplayground-sql bash -c "/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Playground.2026' -C -d OrmPlayground_EclipseLink -Q \"
    CREATE USER ormplayground FOR LOGIN ormplayground;
    ALTER ROLE db_owner ADD MEMBER ormplayground;\""
```

`sqlcmd` je v obrazu v `/opt/mssql-tools18/bin` a přepínač `-C` u něj znamená totéž co `trustServerCertificate=true` v connection stringu.

---

## Krok 1 — Založení projektu

Žádné IDE, jen adresářová struktura, kterou Maven vyžaduje konvencí:

```
orm-playground-eclipselink/
├─ pom.xml
└─ src/
   └─ main/
      ├─ java/cz/stochel/ormplayground/eclipselink/
      │  ├─ domain/
      │  └─ Program.java
      └─ resources/
         └─ META-INF/
```

```sh
mkdir -p orm-playground-eclipselink/src/main/java/cz/stochel/ormplayground/eclipselink/domain
mkdir -p orm-playground-eclipselink/src/main/resources/META-INF
cd orm-playground-eclipselink
```

Platí všechno, co o javové struktuře říká krok 1 dílu k Hibernate: `package` musí odpovídat adresářové cestě, jeden soubor nese jednu veřejnou třídu stejného názvu a `src/main/resources` je automaticky na classpath.

---

## Krok 2 — Závislosti

**`pom.xml`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd">
    <modelVersion>4.0.0</modelVersion>

    <groupId>cz.stochel.ormplayground</groupId>
    <artifactId>orm-playground-eclipselink</artifactId>
    <version>1.0-SNAPSHOT</version>

    <properties>
        <maven.compiler.release>25</maven.compiler.release>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
        <exec.mainClass>cz.stochel.ormplayground.eclipselink.Program</exec.mainClass>
    </properties>

    <dependencies>
        <dependency>
            <groupId>org.eclipse.persistence</groupId>
            <artifactId>org.eclipse.persistence.jpa</artifactId>
            <version>5.0.0</version>
        </dependency>
        <dependency>
            <groupId>com.microsoft.sqlserver</groupId>
            <artifactId>mssql-jdbc</artifactId>
            <version>13.4.0.jre11</version>
        </dependency>
    </dependencies>

    <build>
        <plugins>
            <plugin>
                <groupId>org.codehaus.mojo</groupId>
                <artifactId>exec-maven-plugin</artifactId>
                <version>3.5.0</version>
                <executions>
                    <execution>
                        <id>weaving</id>
                        <goals>
                            <goal>exec</goal>
                        </goals>
                        <configuration>
                            <executable>java</executable>
                            <arguments>
                                <argument>-javaagent:${settings.localRepository}/org/eclipse/persistence/org.eclipse.persistence.jpa/5.0.0/org.eclipse.persistence.jpa-5.0.0.jar</argument>
                                <argument>-classpath</argument>
                                <classpath/>
                                <argument>${exec.mainClass}</argument>
                            </arguments>
                        </configuration>
                    </execution>
                </executions>
            </plugin>
        </plugins>
    </build>
</project>
```

**Dva balíčky, stejně jako u Hibernate, a ze stejného důvodu.** `org.eclipse.persistence.jpa` přitáhne `jakarta.persistence-api` 3.2 tranzitivně, JDBC ovladač se registruje sám přes `ServiceLoader` a nikde v konfiguraci se nejmenuje.

Blok `exec-maven-plugin` zatím jen zaregistruj; k čemu je `-javaagent`, řekne krok 9. **Že konfigurace sedí uvnitř pojmenovaného `<execution>`, a ne přímo pod `<plugin>`, je podstatné:** na úrovni pluginu by ji četl i cíl `exec:java` z kroku 8, který `<classpath/>` neumí, a spadl by na `Unable to parse configuration of mojo … for parameter arguments`. Takhle si ji vyzvedne jen `exec:exec@weaving`.

---

## Krok 3 — Doménové třídy

**Tady se nic nemění.** Obě třídy jsou znak po znaku shodné s dílem k Hibernate, jen v jiném package — proto je zkrácené i to, co si na nich všímat; celý rozbor `Integer` proti `int`, `BigDecimal`, `LocalDate` a `Set` proti `List` platí beze změny a stojí v kroku 3 dílu k Hibernate.

**`domain/Author.java`**

```java
package cz.stochel.ormplayground.eclipselink.domain;

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

**`domain/Book.java`**

```java
package cz.stochel.ormplayground.eclipselink.domain;

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

### Čeho si všimnout

**Žádný import z `org.eclipse.persistence`.** Doména závisí na `jakarta.persistence`, tedy na API specifikace, ne na implementaci — tytéž soubory se zkompilují a poběží pod Hibernate. To je pro F9 nejdůležitější věta celého dílu.

**Řádek `@ManyToOne(fetch = FetchType.LAZY)` tady lže.** Pod Hibernate dělá, co slibuje; pod EclipseLinkem bez weavingu ne. Nepozná to ani kompilátor, ani start aplikace — dokazuje se to až sondou v kroku 9.

---

## Krok 4 — Mapování

**Taky beze změny.** Anotace jsou tytéž, protože jsou standardní: `@Entity`, `@Table`, `@Id`, `@GeneratedValue`, `@Column`, `@OneToMany(mappedBy)`, `@ManyToOne`, `@JoinColumn`. Vrstvy jsou tři jako u Hibernate (konvence → anotace → `orm.xml`) a `orm.xml` má EclipseLink v téže standardní podobě, plus vlastní rozšířené `eclipselink-orm.xml`.

Rozdíl proti Hibernate je celý schovaný v tom, **co doplní konvence tam, kde anotace mlčí**. Naše doména mlčet nenechává — každá tabulka i sloupec má jméno explicitně —, takže se rozdíl v tomhle mapování vůbec neprojeví. Proto ho dokládáme zvlášť, na druhé sadě entit bez jediného názvu, v sekci „Rozdíl DDL" níž.

Pro převodník je to podstatnější, než to zní: **absenční default je v každé implementaci jiný, ale přítomná anotace je v obou stejná.** Builder, který vždycky vypíše `@Table` i `@Column` se jménem, je tím pádem přenositelný mezi oběma implementacemi; parser, který z jejich nepřítomnosti chce odvodit název, přenositelný není a potřebuje profil implementace. To je přesně kritérium rozhodnutí [067](../../decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md), jen doložené ze strany cíle.

---

## Krok 5 — Registrace entit a zdroje na classpath

Zdroje jsou na classpath samy, jako u Hibernate. Entity se registrují elementem `<class>` v `META-INF/persistence.xml`; tomu se věnuje krok 6, protože u EclipseLinku je registrace a konfigurace tentýž soubor.

Za zmínku stojí `<exclude-unlisted-classes>true</exclude-unlisted-classes>`: bez něj by EclipseLink prohledal classpath a našel i entity, které do dané jednotky nepatří. Protože v tomhle projektu budou jednotky dvě — hlavní a pomocná na doložení defaultů —, je vypnuté skenování nutné, ne kosmetické.

---

## Krok 6 — Konfigurace a bootstrap

Tady je první opravdový rozdíl proti Hibernate. Hibernate jsme bootstrapovali v kódu přes `StandardServiceRegistryBuilder`, protože to bylo nejblíž `SessionFactoryBuilder.cs` z dílu k NHibernate. EclipseLink nativní obdobu takového builderu nemá; standardní a jediná pohodlná cesta je **`META-INF/persistence.xml`** a `Persistence.createEntityManagerFactory`.

**`src/main/resources/META-INF/persistence.xml`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<persistence xmlns="https://jakarta.ee/xml/ns/persistence"
             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
             xsi:schemaLocation="https://jakarta.ee/xml/ns/persistence
                                 https://jakarta.ee/xml/ns/persistence/persistence_3_2.xsd"
             version="3.2">

    <persistence-unit name="playground" transaction-type="RESOURCE_LOCAL">
        <class>cz.stochel.ormplayground.eclipselink.domain.Author</class>
        <class>cz.stochel.ormplayground.eclipselink.domain.Book</class>
        <exclude-unlisted-classes>true</exclude-unlisted-classes>

        <properties>
            <property name="jakarta.persistence.jdbc.url"
                      value="jdbc:sqlserver://ormplayground-sql:1433;databaseName=OrmPlayground_EclipseLink;encrypt=true;trustServerCertificate=true"/>
            <property name="jakarta.persistence.jdbc.user" value="ormplayground"/>
            <property name="jakarta.persistence.jdbc.password" value="Zmen.Si.Me.2026"/>

            <property name="jakarta.persistence.schema-generation.database.action" value="drop-and-create"/>
            <property name="jakarta.persistence.schema-generation.scripts.action" value="create"/>
            <property name="jakarta.persistence.schema-generation.scripts.create-source" value="metadata"/>
            <property name="jakarta.persistence.schema-generation.scripts.create-target" value="schema.sql"/>

            <property name="eclipselink.logging.level.sql" value="FINE"/>
            <property name="eclipselink.logging.parameters" value="true"/>
            <property name="eclipselink.logging.timestamp" value="false"/>
            <property name="eclipselink.logging.thread" value="false"/>
            <property name="eclipselink.logging.session" value="false"/>
        </properties>
    </persistence-unit>

    <persistence-unit name="defaults" transaction-type="RESOURCE_LOCAL">
        <class>cz.stochel.ormplayground.eclipselink.domain.Note</class>
        <class>cz.stochel.ormplayground.eclipselink.domain.Tag</class>
        <exclude-unlisted-classes>true</exclude-unlisted-classes>

        <properties>
            <property name="eclipselink.target-database" value="SQLServer"/>
            <property name="jakarta.persistence.database-product-name" value="Microsoft SQL Server"/>
            <property name="jakarta.persistence.schema-generation.database.action" value="none"/>
            <property name="jakarta.persistence.schema-generation.scripts.action" value="create"/>
            <property name="jakarta.persistence.schema-generation.scripts.create-source" value="metadata"/>
            <property name="jakarta.persistence.schema-generation.scripts.create-target" value="schema-defaults.sql"/>
        </properties>
    </persistence-unit>
</persistence>
```

Druhá jednotka `defaults` potřebuje dvě entity bez jediného názvu; ty patří do stejného package:

**`domain/Note.java`** a **`domain/Tag.java`**

```java
@Entity
public class Note {
    @Id @GeneratedValue private Integer id;
    private String text;
    private LocalDate createdOn;
    @ManyToOne private Tag tag;
    // gettery a settery
}

@Entity
public class Tag {
    @Id @GeneratedValue private Integer id;
    private String label;
    // gettery a settery
}
```

### Čeho si všimnout

**Připojení je popsané standardními vlastnostmi.** `jakarta.persistence.jdbc.url`, `.user`, `.password` jsou tytéž klíče, jaké jsme použili u Hibernate. Díl k Hibernate radil držet se jich a odchylky evidovat — teď je ověřeno, že to skutečně vychází: konfigurace připojení je mezi oběma implementacemi přenositelná beze zbytku.

**Chybí dialekt, a přesto ne úplně.** `eclipselink.target-database` má výchozí hodnotu `Auto` a platforma se odvodí z JDBC metadat, přesně jako dialekt u Hibernate. Jednotka `defaults` ale žádné připojení nemá, takže odvodit není z čeho a platformu je nutné říct — a **k tomu nestačí `eclipselink.target-database`**. Samotné `target-database=SQLServer` skončí na:

```
Exception [EclipseLink-4021]: Unable to acquire a connection from driver [null],
user [null] and URL [null].
```

Teprve standardní `jakarta.persistence.database-product-name` EclipseLink přesvědčí, že se připojovat nemá. Hibernate se v téže situaci chová **opačně**: `database-product-name` ignoruje a chce `hibernate.dialect`, jinak zahlásí `Unable to determine Dialect without JDBC metadata`. Generování skriptu je tedy standardní, ale jeho konfigurace ne — a je to typ rozdílu, který se v dokumentaci obou implementací nedohledá, protože každá popisuje jen svou půlku.

**Weaving se nekonfiguruje tady.** Vlastnost `eclipselink.weaving` existuje, ale sama o sobě nic nezapne: dynamický weaving vyžaduje javového agenta na příkazové řádce JVM. Proto je v `pom.xml` konfigurace `exec:exec` a proto je to samostatný krok 9.

**Sdílená cache je zapnutá.** `eclipselink.cache.shared.default` je ve výchozím stavu `true`, kdežto Hibernate má cache druhé úrovně vypnutou. Na tomhle hřišti to nepoznáš, protože do databáze nikdo jiný nepíše; v provozu je to nejznámější rozdíl obou implementací a do profilu cíle patří.

---

## Krok 7 — Vytvoření schématu a první data

**`Program.java`**

```java
package cz.stochel.ormplayground.eclipselink;

import cz.stochel.ormplayground.eclipselink.domain.Author;
import cz.stochel.ormplayground.eclipselink.domain.Book;
import jakarta.persistence.*;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

public final class Program {

    private static final String QUERY = """
            select a
            from Author a
            left join fetch a.books
            where exists (
                select b from Book b
                where b.author = a and b.publishedYear < 1930
            )
            """;

    public static void main(String[] args) {
        try (EntityManagerFactory emf = Persistence.createEntityManagerFactory("playground")) {

            // --- Zapis ---
            try (EntityManager em = emf.createEntityManager()) {
                em.getTransaction().begin();

                Author capek = new Author();
                capek.setName("Karel Capek");
                capek.setBornOn(LocalDate.of(1890, 1, 9));
                capek.addBook(book("R.U.R.", 1920, "249.00"));
                capek.addBook(book("War with the Newts", 1936, "329.00"));

                Author orwell = new Author();
                orwell.setName("George Orwell");
                orwell.setBornOn(LocalDate.of(1903, 6, 25));
                orwell.addBook(book("1984", 1949, "299.00"));

                em.persist(capek);
                em.persist(orwell);

                em.getTransaction().commit();
            }

            System.out.println("\n--- Query: authors with a book published before 1930 ---\n");

            // --- Cteni: bez distinct ---
            try (EntityManager em = emf.createEntityManager()) {
                List<Author> authors = em.createQuery(QUERY, Author.class).getResultList();
                System.out.printf("BEZ distinct: %d radku v seznamu%n", authors.size());
            }

            // --- Cteni: s distinct ---
            try (EntityManager em = emf.createEntityManager()) {
                List<Author> authors = em.createQuery(
                        QUERY.replace("select a", "select distinct a"), Author.class).getResultList();
                System.out.printf("S distinct:   %d radku v seznamu%n%n", authors.size());

                for (Author author : authors) {
                    System.out.printf("%s (%d books)%n", author.getName(), author.getBooks().size());
                    author.getBooks().stream()
                            .sorted(Comparator.comparingInt(Book::getPublishedYear))
                            .forEach(b -> System.out.printf("  %d  %-25s %8.2f%n",
                                    b.getPublishedYear(), b.getTitle(), b.getPrice()));
                }
            }

            // --- Sonda na weaving ---
            System.out.println("\n--- Weaving probe: je @ManyToOne(fetch = LAZY) opravdu line? ---\n");
            try (EntityManager em = emf.createEntityManager()) {
                Integer bookId = em.createQuery("select min(b.id) from Book b", Integer.class)
                        .getSingleResult();
                em.clear();

                Book book = em.find(Book.class, bookId);
                PersistenceUnitUtil util = emf.getPersistenceUnitUtil();
                System.out.printf("isLoaded(book, \"author\") = %s%n", util.isLoaded(book, "author"));
                System.out.printf("trida atributu author    = %s%n",
                        book.getAuthor().getClass().getName());
            }
        }

        // --- DDL z holych anotaci, bez pripojeni k databazi ---
        System.out.println("\n--- generateSchema(\"defaults\") ---\n");
        Persistence.generateSchema("defaults", Map.of());
        System.out.println("schema-defaults.sql zapsan");
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

**`EntityManager` místo `Session`.** U Hibernate jsme volali `sessionFactory.inTransaction(…)`, což je hibernátovský obal. Tady je to holé JPA: `em.getTransaction().begin()` a `commit()`. Rozdíl je jen v pohodlí — `Session` je v Hibernate potomkem `EntityManager`, takže totéž jde napsat i tam a přenositelná varianta je tahle.

**`persist` je tentýž.** Model práce s kontextem — transient / managed / detached / removed, dirty checking, flush při commitu — je standardní a shodný.

**`try (EntityManager em = …)`** funguje, protože `EntityManager` je od JPA 3.2 `AutoCloseable`. Na JPA 3.1 by to nešlo a psal by se `finally`.

**Dotaz je týž řetězec jako u Hibernate**, včetně schválně napsaného `select a`. Díl k Hibernate to zdůvodnil tím, že JPQL vynechání nedovolí, kdežto HQL ano; tenhle dotaz to potvrzuje, protože prošel oběma beze změny.

**`distinct` je tady nutné.** Zatímco Hibernate od verze 6 duplicitní kořeny z fetch joinu odfiltruje sám, EclipseLink je vrátí. Proto je dotaz v programu pouštěný dvakrát — jednou tak, jak by ho napsal člověk zvyklý na Hibernate, a jednou správně.

---

## Krok 8 — Spuštění

```sh
docker run --rm -v "$PWD":/app -w /app -v ormplayground-m2:/root/.m2 \
  --network ormplayground maven:3.9.11-eclipse-temurin-25-noble \
  mvn -B compile exec:java
```

Co jednotlivé přepínače dělají:

| Přepínač | Proč |
|---|---|
| `--rm` | kontejner po doběhnutí zmizí; projekt je na hostiteli, ne v něm |
| `-v "$PWD":/app -w /app` | projekt z aktuálního adresáře se připojí dovnitř a stane se pracovním adresářem |
| `-v ormplayground-m2:/root/.m2` | pojmenovaný svazek pro `~/.m2/repository`, aby se závislosti stahovaly jen napoprvé |
| `--network ormplayground` | aby byl hostitel `ormplayground-sql` z kroku 0 rozeznatelný jménem |

**V Git Bash na Windows** musíš předřadit `MSYS_NO_PATHCONV=1` a použít `$(pwd -W)` místo `$PWD`, jinak Git Bash přepíše `/app` na windowsovou cestu a `docker` ohlásí, že nenašel spustitelný soubor:

```sh
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W)":/app -w /app -v ormplayground-m2:/root/.m2 \
  --network ormplayground maven:3.9.11-eclipse-temurin-25-noble \
  mvn -B compile exec:java
```

V PowerShellu ani v `cmd.exe` tenhle problém není a platí první varianta, jen s `${PWD}`.

Ve výstupu bys měl vidět:

1. **Opakované `DROP TABLE`**, každé třikrát a některé neúspěšné — `drop-and-create` se pokouší zahodit tabulky, které ještě nemusí existovat, a chyby jen zaloguje. Hibernate se chová stejně.
2. **DDL** — hlavní výstup celého dílu, rozebraný níž.
3. **`INSERT`y s vypsanými hodnotami parametrů** na řádcích `bind => [...]`. Proti Hibernate je to znatelně lepší výchozí stav: EclipseLink má vlastní logovací backend nad `java.util.logging`, takže `eclipselink.logging.parameters=true` stačí a nic se nedoinstalovává. U Hibernate jsme na totéž potřebovali přidat SLF4J backend (krok 9 dílu k Hibernate).
4. **`BEZ distinct: 2` a `S distinct: 1`.**
5. **Sondu na weaving**, zatím s `isLoaded(book, "author") = true`.

### Co v DDL hledat

Vygenerovaný `schema.sql` (a totéž, co proběhlo proti databázi):

```sql
CREATE TABLE Authors (AuthorId INTEGER IDENTITY NOT NULL, BornOn DATE NULL, Name VARCHAR(200) NOT NULL, PRIMARY KEY (AuthorId))
CREATE TABLE Books (BookId INTEGER IDENTITY NOT NULL, Price NUMERIC(18,2) NOT NULL, PublishedYear INTEGER NOT NULL, Title VARCHAR(300) NOT NULL, AuthorId INTEGER NOT NULL, PRIMARY KEY (BookId))
ALTER TABLE Books ADD CONSTRAINT FK_Books_AuthorId FOREIGN KEY (AuthorId) REFERENCES Authors (AuthorId)
```

**`VARCHAR`, ne `nvarchar`.** Tentýž nález jako u Hibernate a ze stejného důvodu: `String` je v obou implementacích JPA nenationalizovaný. EclipseLink k tomu ale **nemá žádný přepínač** — Hibernate umí `@Nationalized` na atributu, od 7.4 i na balíčku, a globální `hibernate.use_nationalized_character_data`; EclipseLink zná jedinou cestu, a tou je doslovný `columnDefinition = "nvarchar(200)"` na každém sloupci zvlášť. Pro builder to znamená, že nationalizační režim profilu cíle se do EclipseLink výstupu propíše **doslovným typem**, ne anotací.

**`NULL` je napsané explicitně.** Hibernate nullable sloupec nechá bez klauzule, EclipseLink napíše `NULL`. Sémanticky totéž, textově ne — a pro porovnávání vygenerovaných artefaktů (ověřovací stupně, rozhodnutí [016](../../decisions/016-generated-artifact-verification-levels.md)) je to přesně ten druh rozdílu, kvůli kterému nelze artefakty srovnávat jako text.

**`FK_Books_AuthorId` proti hibernátovskému `FKl3n8im5b0nbrnd07xq39516ia`.** EclipseLink pojmenovává cizí klíče čitelně a odvozeně od tabulky a sloupce, Hibernate hashem. Ani jedno není v anotacích, ani jedno není ve specifikaci.

**Velká písmena v názvech tady nejsou vidět**, protože každý název jsme dali explicitně. Že by tam jinak byla, ukazuje následující sekce.

---

## Krok 9 — Weaving a líné načtení (nepovinné)

Krok 9 dílu k Hibernate doplňoval logování hodnot parametrů; to tady máme od kroku 6 zadarmo. Místo toho doložíme jediný rozdíl, který mění chování aplikace, a ne jen text DDL.

Sonda z kroku 7 se ptá na jednu věc: je po `em.find(Book.class, id)` atribut `author` načtený, nebo ne? `@ManyToOne(fetch = FetchType.LAZY)` slibuje, že ne.

**Bez agenta** (`mvn compile exec:java`):

```
isLoaded(book, "author") = true
trida atributu author    = cz.stochel.ormplayground.eclipselink.domain.Author
```

**S agentem** (`mvn compile exec:exec@weaving`, tedy execution z kroku 2):

```
isLoaded(book, "author") = false
trida atributu author    = cz.stochel.ormplayground.eclipselink.domain.Author
```

```sh
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W)":/app -w /app -v ormplayground-m2:/root/.m2 \
  --network ormplayground maven:3.9.11-eclipse-temurin-25-noble \
  mvn -B compile exec:exec@weaving
```

Tři věci, které z toho plynou:

**1. Bez weavingu se `fetch = LAZY` na `@ManyToOne` tiše ignoruje.** Žádné varování, žádná výjimka — jen se to načte eager. Je to jediný nález celého dílu, kde se vygenerovaný kód chová jinak, než jeho anotace slibují, a přitom se to nedá poznat z textu artefaktu. Do profilu cíle patří jako varování; do mezireprezentace nepatří vůbec, protože strategii načítání článek (§5.4) vylučuje.

**2. Třída atributu zůstává `Author`, ne proxy potomek.** Hibernate by tu ukázal vygenerovaného potomka. EclipseLink místo dědění přepisuje bytecode entity samotné a stav drží ve value holderech — proto mu na rozdíl od Hibernate nevadí ani `final` metody, zato mu nestačí mít třídu na classpath, potřebuje ji upravit při načtení.

**3. Agent musí běžet ve vlastním JVM.** `MAVEN_OPTS=-javaagent:…` nefunguje: agent se načítá systémovým classloaderem, ve kterém Maven `jakarta.persistence` nemá, a JVM na tom spadne ještě před `premain` (`java.lang.instrument ASSERTION FAILED`). Proto konfigurace v `pom.xml` používá `exec:exec@weaving` s `<classpath/>`, které rozvětví nový proces — kdežto `exec:java` běží uvnitř mavenovského JVM. V provozu se týž efekt řeší buď kontejnerem (aplikační server agenta nasadí sám), nebo statickým weavingem při buildu (`StaticWeave`).

---

## Rozdíl DDL: Hibernate × EclipseLink nad týmiž anotacemi

Tohle je metodicky nejcennější část dílu. Generování DDL bez připojení k databázi je standardní vlastnost JPA, takže se dá obě implementace požádat o totéž a rozdíl přečíst jako text. Entity `Note` a `Tag` z kroku 6 jsou schválně bez jediného názvu a s `@GeneratedValue` bez strategie — tedy přesně tam, kde mluví konvence.

**EclipseLink 5.0.0** (`schema-defaults.sql`):

```sql
CREATE TABLE NOTE (ID INTEGER NOT NULL, CREATEDON DATE NULL, TEXT VARCHAR(255) NULL, TAG_ID INTEGER NULL, PRIMARY KEY (ID))
CREATE TABLE TAG (ID INTEGER NOT NULL, LABEL VARCHAR(255) NULL, PRIMARY KEY (ID))
ALTER TABLE NOTE ADD CONSTRAINT FK_NOTE_TAG_ID FOREIGN KEY (TAG_ID) REFERENCES TAG (ID)
CREATE TABLE SEQUENCE (SEQ_NAME VARCHAR(50) NOT NULL, SEQ_COUNT NUMERIC(28) NULL, PRIMARY KEY (SEQ_NAME))
INSERT INTO SEQUENCE(SEQ_NAME, SEQ_COUNT) values ('SEQ_GEN', 0)
```

**Hibernate 7.4.5** nad znak po znaku týmž zdrojem:

```sql
create sequence Note_SEQ start with 1 increment by 50;
create sequence Tag_SEQ start with 1 increment by 50;
create table Note (id int not null, createdOn date, text varchar(255), tag_id int, primary key (id));
create table Tag (id int not null, label varchar(255), primary key (id));
alter table Note add constraint FKmjgf94k6xbe2an5ylxr634d1w foreign key (tag_id) references Tag;
```

| Co | Hibernate 7.4.5 | EclipseLink 5.0.0 |
|---|---|---|
| Název tabulky | `Note` | `NOTE` |
| Název sloupce | `createdOn` | `CREATEDON` |
| Název FK sloupce | `tag_id` | `TAG_ID` |
| `@GeneratedValue` bez strategie | dvě sekvence `Note_SEQ`, `Tag_SEQ`, krok 50 | jedna tabulka `SEQUENCE` s řádkem `SEQ_GEN` |
| Název FK constraintu | hash `FKmjgf94k6…` | `FK_NOTE_TAG_ID` |
| Nullable sloupec | bez klauzule | `NULL` |
| Typ celého čísla | `int` | `INTEGER` |
| Oddělovač příkazů | `;` | žádný |
| Velikost klíčových slov | malá | velká |

Dvě z devíti řádek nesou **sémantický** rozdíl, zbytek je kosmetika:

**Velká písmena v názvech.** EclipseLink je zapíná vlastností `eclipselink.jpa.uppercase-column-names`, která je ve výchozím stavu zapnutá a má simulovat necitlivost na velikost písmen. Na SQL Serveru s výchozí kolací to databáze sama nerozliší, ale **nástroj ano**: párování přečteného názvu s katalogem (F6) i porovnání dvou mezireprezentací by na tom ztroskotalo. Párování na straně EclipseLinku proto musí být necitlivé na velikost písmen.

**`AUTO` znamená v každé implementaci jinou věc.** Hibernate vyrobí sekvenci, EclipseLink tabulku `SEQUENCE` — a s ní i řádek `INSERT INTO SEQUENCE…`, tedy DML uprostřed DDL skriptu. Překlad Hibernate → EclipseLink, který by `@GeneratedValue` bez strategie jen opsal, by tiše vyměnil generátor za jiný. Proto rozhodnutí k F7 a F9 musí vyslovit dvojí: že **profil cíle jmenuje implementaci, ne jen úroveň specifikace**, a že builder `AUTO` nikdy nevypisuje — kanonický parametr generátoru (rozhodnutí [020](../../decisions/020-canonical-generator-parameter-vocabulary.md)) se do výstupu propisuje vždy konkrétní strategií.

---

## Typické chyby

| Hláška | Příčina |
|---|---|
| `docker: executable file not found` u `-w /app` | Git Bash přepsal cestu; předřaď `MSYS_NO_PATHCONV=1` a použij `$(pwd -W)` (krok 8) |
| `The TCP/IP connection to the host ... has failed` | kontejnery nejsou na společné síti, nebo se aplikace připojuje na `localhost` místo `ormplayground-sql` |
| `PKIX path building failed` | chybí `trustServerCertificate=true` |
| `Login failed for user 'ormplayground'` | nespuštěný krok 0c, nebo jiná databáze v URL |
| `Cannot open database "OrmPlayground_EclipseLink"` | databáze neexistuje; EclipseLink tvoří tabulky, ne databázi (krok 0c) |
| `EclipseLink-4021: Unable to acquire a connection from driver [null]` | generování skriptu bez připojení; chybí `jakarta.persistence.database-product-name` (krok 6) |
| `No Persistence provider for EntityManager named playground` | překlep v názvu jednotky, nebo `persistence.xml` není v `src/main/resources/META-INF` |
| `Unable to determine Dialect without JDBC metadata` | tatáž situace, ale pod Hibernate — ten chce `hibernate.dialect` |
| `java.lang.instrument ASSERTION FAILED` a pád JVM | `-javaagent` předaný přes `MAVEN_OPTS`; musí jít do rozvětveného JVM přes `exec:exec@weaving` (krok 9) |
| `Unable to parse configuration of mojo … for parameter arguments` | konfigurace `exec:exec` visí přímo pod `<plugin>`, takže ji čte i `exec:java`; patří do pojmenovaného `<execution>` (krok 2) |
| entity se nenačetly do správné jednotky | chybí `<exclude-unlisted-classes>true</exclude-unlisted-classes>` (krok 5) |
| dotaz vrací víc rodičů, než by měl | `join fetch` kolekce bez `distinct` — EclipseLink neduplikuje sám (krok 7) |
| `LazyInitializationException` **nepřijde**, ač bys ji čekal | EclipseLink načte i na odpojené entitě, dokud žije továrna; to není chyba, to je rozdíl proti Hibernate |

---

## Srovnání všech pěti

Rozšíření tabulky z dílu k Hibernate o sloupec EclipseLink. Řádky označené **(JPA)** jsou určené specifikací, takže se u obou javových implementací shodují z principu, ne shodou okolností.

| | NHibernate | EF Core | Dapper | Hibernate | EclipseLink |
|---|---|---|---|---|---|
| Ekosystém | .NET | .NET | .NET | Java | Java |
| Standardizační vrstva | — | — | — | **Jakarta Persistence 3.2** | **Jakarta Persistence 3.2** |
| Balíčky | 2 | 1 | 2 | 2 | 2 |
| Zásah do build souboru | nutný | žádný | žádný | žádný | žádný |
| Požadavky na entity | `virtual`, bezparam. ctor | žádné | žádné | **(JPA)** bezparam. ctor, ne `final` | **(JPA)** totéž |
| Mapování | `hbm.xml` | konvence + annotations + fluent | aliasy v SQL | **(JPA)** konvence + anotace + `orm.xml` | **(JPA)** totéž |
| Typický bootstrap | kód | kód (`DbContext`) | kód | kód | **`persistence.xml`** |
| Dialekt | nutný v konfiguraci | nutný | žádný | odvozený za běhu | odvozený za běhu |
| Vytvoření tabulek | `SchemaExport` | `EnsureCreated` | ručně | **(JPA)** `schema-generation.database.action` | **(JPA)** totéž |
| DDL bez provedení | `SchemaExport(cfg).Create(a, false)` | `GenerateCreateScript()` | triviálně | **(JPA)** `scripts.action` + `hibernate.dialect` | **(JPA)** `scripts.action` + `database-product-name` |
| Tabulka bez `@Table` | — | pluralizace | — | `Note` | **`NOTE`** |
| Generátor bez strategie | — | identity | — | **sekvence**, krok 50 | **tabulka `SEQUENCE`** |
| Název FK constraintu | odvozený | odvozený | ruční | hash | odvozený |
| `string` → SQL typ | `nvarchar` | `nvarchar` | dle DDL | `varchar` + `@Nationalized` | `varchar`, jen `columnDefinition` |
| Explicitní transakce | nutná | volitelná | ruční | nutná | **(JPA)** nutná |
| Eager loading | `FetchMany` | `Include` | ruční JOIN | **(JPA)** `join fetch` | **(JPA)** `join fetch` |
| Deduplikace u fetch joinu | ruční `distinct` | automatická | ruční dictionary | automatická od v6 | **ruční `distinct`** |
| Líné `@ManyToOne` | proxy | proxy | — | proxy | **jen s weavingem** |
| Cache 2. úrovně | vypnutá | není | není | vypnutá | **zapnutá** |
| Lazy mimo kontext | `LazyInitializationException` | výjimka | — | `LazyInitializationException` | **načte se** |
| Dotazovací jazyk | LINQ, HQL | LINQ | SQL | HQL ⊃ JPQL | EQL ⊃ JPQL |

---

## Co z toho plyne pro převodník

**1. Sdílená JPA vrstva je reálná a její hranice je ostrá.** Kroky 3, 4 a 7 se mezi oběma implementacemi nezměnily ani o znak: doména, anotace, dotaz i model jednotky práce jsou standardní. Liší se krok 6 (bootstrap) a defaulty. Pro F7 a F9 to znamená, že sdílený „JPA builder" s tenkými nadstavbami je doložitelně schůdný — a že nadstavba nese hlavně konfiguraci a volbu strategií, ne tvar entit. Tohle je páté zjištění dílu k Hibernate, teď ověřené z druhé strany.

**2. Profil cíle musí jmenovat implementaci.** `@GeneratedValue` bez strategie a `@Entity` bez `@Table` znamenají v každé implementaci něco jiného. Rozhodnutí [013](../../decisions/013-target-framework-versions.md) dalo deskriptoru jediné číslo verze; javový profil potřebuje **verzi specifikace, verzi implementace a implementaci samu** jako klíč k defaultům, a k tomu pojmenovací strategii a nationalizační režim z dílu k Hibernate.

**3. Builder nesmí spoléhat na defaulty ani u cíle.** Nejbezpečnější výstup je ten, kde žádný název ani strategie nezůstane na konvenci — pak je artefakt přenositelný mezi implementacemi. To není nová volba, je to důsledek rozhodnutí [067](../../decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) čtený ze strany zápisu: co parser nesmí materializovat z mlčení, to builder nesmí do mlčení schovat.

**4. Párování s katalogem musí být na straně EclipseLinku necitlivé na velikost písmen.** `NOTE` proti `Note` není překlep, je to výchozí chování. Doplňování z katalogu (F6, rozhodnutí [015](../../decisions/015-mapping-fact-completion-from-the-catalog.md)) na tom jinak ztroskotá.

**5. Existuje fakt o cíli, který není v artefaktu vidět.** Weaving mění chování vygenerovaného kódu, aniž by se změnil jediný znak zdroje. Ověřovací stupně (rozhodnutí [016](../../decisions/016-generated-artifact-verification-levels.md)) na to nedosáhnou — 2. stupeň by zkompiloval a 3. stupeň by předložil artefakt frameworku, a ani jeden by nic nenašel. Patří to do profilu cíle jako varování a do hranice záruk jako věta o tom, co nástroj netvrdí.

---

## Kam pokračovat

**1. `orm.xml` místo anotací.** Standardní deskriptor, který obě implementace čtou se stejnou precedencí — anotace první, XML poslední, `<xml-mapping-metadata-complete/>` anotace vypíná. Je to přesně tvar, se kterým počítá rozhodnutí [068](../../decisions/068-source-framework-precedence-orders-the-reading.md), a nejlevnější způsob, jak si ho ověřit na běžícím kódu dřív, než se podle něj napíše parser.

**2. Kompozitní klíč přes `@IdClass`.** Tentýž experiment jako v dílu k Hibernate, ale pod EclipseLinkem — ověří, že požadavky na klíčovou třídu (veřejná, bezparametrický konstruktor, `Serializable`, `equals`, `hashCode`) jsou opravdu věcí specifikace, a ne jedné implementace.

**3. Statický weaving při buildu.** `StaticWeave` místo agenta. Pro řešení je to zajímavé tím, že mění artefakt, ne jen běh — a je to tedy jediná varianta weavingu, kterou by generovaný projekt mohl nést sám v `pom.xml`.
