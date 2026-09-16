# MyBatis od nuly — Docker, JDK 25, Maven, MS SQL Server

Šestý díl a třetí javový. Stejná doména jako ve všech předchozích (`Author` 1:N `Book`), stejný SQL Server, stejné číslování kroků.

MyBatis je **javový protějšek Dapperu**: SQL píše autor, framework se stará o parametrizaci a o materializaci výsledku. Jenže protějšek jen zhruba — a právě to je na tomhle dílu zajímavé. Dapper nemá pro mapování žádné deklarativní místo a aliasy v dotazu jsou jediná forma, jak název sloupce spojit s vlastností. MyBatis má `<resultMap>`: pojmenovatelné, znovupoužitelné mapování, které umí vnořené kolekce. Z hlediska mezireprezentace je proto MyBatis **mezi** Dapperem a plnohodnotným ORM, ne vedle Dapperu.

Druhá věc, kterou žádný z předchozích pěti dílů neměl: **dynamické SQL**. Jeden `<select>` není jeden příkaz, ale rodina příkazů — a která z nich se pošle do databáze, rozhodnou hodnoty parametrů. Krok 9 to ukazuje bez databáze.

Ověřené verze: MyBatis **3.5.19**, JDK **25** (Temurin 25.0.1+8), Maven **3.9.11**, `mssql-jdbc` **13.4.0.jre11**, MS SQL Server **2022**. Všechno běželo v obrazu `maven:3.9.11-eclipse-temurin-25-noble`, viz krok 0.

---

## Než začneš: dvě osy rozdílů

### Osa 1 — prostředí

Shodná s dílem k EclipseLinku: **žádné IDE, jen Docker.** Na hostiteli není potřeba JDK, Maven ani SQL Server. Kroky 0, 1 a 8 tedy vypadají jinak než v dílu k Hibernate, kroky 2 až 7 a 9 jsou o frameworku a prostředím dotčené nejsou. Zdůvodnění a tabulku rozdílů proti IntelliJ variantě nese [díl k EclipseLinku](eclipselink-getting-started.md), osa 1.

### Osa 2 — framework

Proti oběma implementacím JPA tady odpadá skoro všechno, co dělá ORM ORMem. Tabulka srovnává s Dapperem, protože ten je bližší příbuzný:

| | Dapper 2.1.79 | MyBatis 3.5.19 |
|---|---|---|
| SQL píše | autor | autor |
| Deklarativní místo pro mapování | **není** | **`<resultMap>` v XML mapperu** |
| Kde SQL žije | v řetězci v kódu | v XML mapperu, nebo v `@Select` na rozhraní |
| Volání dotazu | metoda rozšíření nad `IDbConnection` | **metoda vlastního rozhraní**, implementaci dodá proxy |
| Vnořené kolekce | ručně přes dictionary | `<collection>` s `<id>`, deduplikaci řeší framework |
| Dynamické SQL | skládáním řetězců | **`<if>`, `<where>`, `<foreach>` s výrazy OGNL** |
| Požadavky na doménovou třídu | žádné | žádné |
| Znalost primárního klíče | ne | jen jako příznak identity pro vnořování |
| Znalost relací | ne | jen v rámci jednoho `<resultMap>` |
| Tvorba schématu | ručně psané DDL | ručně psané DDL |
| Cílový dialekt řeší | autor | autor |

Dva řádky mění to, co může parser přečíst. **`<resultMap>` je skutečné mapování**, které nese dvojice sloupec–vlastnost i strukturu vztahu, takže parser MyBatis zdroje má z čeho stavět entitní mapu — na rozdíl od Dapperu, kde entitní mapa vzniká až doplněním z katalogu. A **dynamické SQL znamená, že dotazová jednotka není dotaz**, ale předpis, z něhož dotaz vznikne až za běhu.

---

## Krok 0 — Databáze, síť a přihlášení

Totožné s dílem k EclipseLinku, jen s jiným názvem databáze. Pokud už síť a kontejner z předchozího dílu běží, stačí vytvořit databázi (bod 0c).

### 0a — Síť

```sh
docker network create ormplayground
```

### 0b — SQL Server

```sh
docker run -d --name ormplayground-sql --network ormplayground -p 11433:1433 \
  -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=Playground.2026" -e MSSQL_PID=Developer \
  mcr.microsoft.com/mssql/server:2022-latest
```

### 0c — Databáze a přihlášení

```sh
docker exec ormplayground-sql bash -c "/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Playground.2026' -C -Q \"
    CREATE DATABASE OrmPlayground_MyBatis;
    CREATE LOGIN ormplayground WITH PASSWORD = 'Zmen.Si.Me.2026', CHECK_POLICY = OFF;\""

docker exec ormplayground-sql bash -c "/opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Playground.2026' -C -d OrmPlayground_MyBatis -Q \"
    CREATE USER ormplayground FOR LOGIN ormplayground;
    ALTER ROLE db_owner ADD MEMBER ormplayground;\""
```

`CREATE LOGIN` selže hláškou, že už existuje, pokud jsi dělal díl k EclipseLinku — to je v pořádku, `CREATE USER` se provede stejně.

**Tabulky tentokrát nevytvoří framework.** MyBatis DDL negeneruje vůbec; napíšeme si ho sami v kroku 7. V tom je stejný jako Dapper a nepodobný všem ostatním dílům.

---

## Krok 1 — Založení projektu

```
orm-playground-mybatis/
├─ pom.xml
└─ src/
   └─ main/
      ├─ java/cz/stochel/ormplayground/mybatis/
      │  ├─ domain/
      │  ├─ mapper/
      │  └─ Program.java
      └─ resources/
         ├─ mybatis-config.xml
         └─ cz/stochel/ormplayground/mybatis/mapper/
```

```sh
mkdir -p orm-playground-mybatis/src/main/java/cz/stochel/ormplayground/mybatis/{domain,mapper}
mkdir -p orm-playground-mybatis/src/main/resources/cz/stochel/ormplayground/mybatis/mapper
cd orm-playground-mybatis
```

**Té zdvojené cestě pod `resources` si všimni**, je to jediné strukturální specifikum tohoto dílu. XML mapper musí ležet na classpath ve **stejné cestě balíčku** jako rozhraní, ke kterému patří. Pak se najde sám a nemusí se nikde vyjmenovávat. V .NETu by ekvivalent byl `<EmbeddedResource>` u NHibernate — s tím rozdílem, že tady nejde o build akci, ale o umístění.

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
    <artifactId>orm-playground-mybatis</artifactId>
    <version>1.0-SNAPSHOT</version>

    <properties>
        <maven.compiler.release>25</maven.compiler.release>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
        <exec.mainClass>cz.stochel.ormplayground.mybatis.Program</exec.mainClass>
    </properties>

    <dependencies>
        <dependency>
            <groupId>org.mybatis</groupId>
            <artifactId>mybatis</artifactId>
            <version>3.5.19</version>
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
            </plugin>
        </plugins>
    </build>
</project>
```

**Dva balíčky a žádná tranzitivní závislost.** Tohle je v celé šestici unikátní: `mybatis` nepřitáhne vůbec nic. OGNL (vyhodnocování výrazů v dynamickém SQL) i Javassist (proxy pro líné načtení) jsou zabalené uvnitř jaru pod přejmenovanými balíčky `org.apache.ibatis.ognl` a `org.apache.ibatis.javassist`. Hibernate proti tomu táhne `byte-buddy`, `antlr4-runtime`, `jandex` a další; EclipseLink tři vlastní moduly.

Na logování nic nepotřebuje. Hibernate v kroku 9 svého dílu chtěl doinstalovat SLF4J backend, aby vypsal hodnoty parametrů; MyBatis má vestavěné `STDOUT_LOGGING`, které zapneme v kroku 6.

---

## Krok 3 — Doménové třídy

**`domain/Author.java`**

```java
package cz.stochel.ormplayground.mybatis.domain;

import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;

public class Author {

    private Integer id;
    private String name;
    private LocalDate bornOn;
    private List<Book> books = new ArrayList<>();

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

    public List<Book> getBooks() { return books; }
    public void setBooks(List<Book> books) { this.books = books; }
}
```

**`domain/Book.java`**

```java
package cz.stochel.ormplayground.mybatis.domain;

import java.math.BigDecimal;

public class Book {

    private Integer id;
    private String title;
    private int publishedYear;
    private BigDecimal price;
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

**Ani jeden import z frameworku.** Tohle je jediný ze šesti dílů, kde doménová třída nezávisí ani na frameworku, ani na specifikaci — u JPA je tam aspoň `jakarta.persistence`, u NHibernate `virtual` na každém členu, u EF Core aspoň konvence. Pro převodník je to nejčistší případ pravidla „požadavky na entitu jsou omezení cílového frameworku, ne fakta o doméně": tady jich je nula.

**`List` místo `Set`.** U JPA i NHibernate nese druh kolekce sémantiku (`Set` vylučuje duplicity, `List` bez `@OrderColumn` je bag). V MyBatisu **neznamená nic**: `<collection>` má vlastní `javaType` a ve výchozím stavu použije `ArrayList`. Druh kolekce v mezireprezentaci (`CollectionKind`, rozhodnutí [014](../../decisions/014-language-type-model.md)) tedy pro MyBatis cíl není fakt k vyjádření — je to fakt, který se nemá kam zapsat.

**`int publishedYear` je tady past.** Sloupec je `NOT NULL`, takže se nic nestane; kdyby ale byl nullable a přišlo `null`, MyBatis by setter **vůbec nezavolal** (`callSettersOnNulls` je ve výchozím stavu vypnuté) a vlastnost by tiše zůstala na `0`. Ani JPA, ani .NET trojice se takhle nechovají: tam by z `null` do primitivu byla výjimka. Je to nullabilita, kterou framework neřeší ani nehlásí.

---

## Krok 4 — Mapování

Tady je jádro rozdílu proti Dapperu. Mapování má vlastní deklarativní místo a to místo je **v XML mapperu vedle dotazů**, ne u třídy.

**`src/main/resources/cz/stochel/ormplayground/mybatis/mapper/AuthorMapper.xml`** — zatím jen mapovací část, příkazy přibudou v kroku 7:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN"
        "https://mybatis.org/dtd/mybatis-3-mapper.dtd">

<mapper namespace="cz.stochel.ormplayground.mybatis.mapper.AuthorMapper">

    <resultMap id="authorWithBooks" type="Author">
        <id     column="AuthorId"      property="id"/>
        <result column="Name"          property="name"/>
        <result column="BornOn"        property="bornOn"/>
        <collection property="books" ofType="Book">
            <id     column="BookId"        property="id"/>
            <result column="Title"         property="title"/>
            <result column="PublishedYear" property="publishedYear"/>
            <result column="Price"         property="price"/>
        </collection>
    </resultMap>

</mapper>
```

### Čeho si všimnout

**`namespace` je plné jméno rozhraní.** Tím je `<resultMap>` i každý příkaz adresovatelný jako `<namespace>.<id>` a MyBatis podle toho páruje XML s metodami rozhraní z kroku 5.

**`<id>` není primární klíč.** Je to příznak, podle kterého se při vnořování poznají řádky patřící témuž objektu. Bez něj by tři řádky výsledku daly tři autory místo jednoho s dvěma knihami. Tabulka žádný klíč mít nemusí a MyBatis se na něj nikdy nezeptá — z hlediska mezireprezentace je to **tvrzení o výsledku dotazu, ne o entitě**, a parser to musí rozlišit: `<id column="AuthorId">` neopravňuje zapsat `PrimaryKey`.

**`<collection>` nese vztah, ale jen v rámci tohoto dotazu.** Že `Author` má `Book`y, je napsané uvnitř `authorWithBooks`. Jiný `<resultMap>` nad týmiž třídami může vztah vynechat nebo popsat jinak — mapování je **per příkaz, ne per třída**. Tady je rozdíl proti všem pěti předchozím dílům největší: `Relation` v mezireprezentaci visí na `EntityMap` (rozhodnutí [001](../../decisions/001-entity-reference-by-name.md)), kdežto tady je vlastností jednoho dotazu. Parser MyBatis zdroje tedy musí rozhodnout, co dělat, když dva `<resultMap>` téže třídy tvrdí různé věci — a to je vstup rozhodnutí k F8.

**Vlastnictví vztahu nikde není.** `mappedBy` ani `inverse="true"` nemá MyBatis obdobu, protože FK sloupec se zapisuje ručně v `<insert>`. Role `Owning`/`Inverse` se tedy do MyBatis výstupu nepropíše nijak, a při čtení se z něj nedá zjistit.

**Chybí délky, nullabilita, typy.** `<result>` umí `javaType` a `jdbcType`, ale `jdbcType` je rodina JDBC, ne typ v DDL. Délka sloupce, nullabilita ani přesnost nemají v mapperu kam — jsou v DDL, které píše autor. Doplnění z katalogu (F6, rozhodnutí [015](../../decisions/015-mapping-fact-completion-from-the-catalog.md)) je tedy pro MyBatis zdroj stejně nutné jako pro Dapper.

**Párování názvů je necitlivé na velikost písmen** a `mapUnderscoreToCamelCase` (protějšek `MatchNamesWithUnderscores` z Dapperu) je ve výchozím stavu **vypnuté**. V kroku 6 ho vypneme explicitně, ať je to v konfiguraci vidět.

---

## Krok 5 — Registrace mapperu a zdroje na classpath

MyBatis nepracuje s `SqlSession.selectList("namespace.id")` napřímo, i když by to šlo; idiomatická cesta je **rozhraní**, jehož implementaci dodá proxy.

**`mapper/AuthorMapper.java`**

```java
package cz.stochel.ormplayground.mybatis.mapper;

import cz.stochel.ormplayground.mybatis.domain.Author;
import cz.stochel.ormplayground.mybatis.domain.Book;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface AuthorMapper {

    void createSchema();

    void dropSchema();

    void insertAuthor(Author author);

    void insertBook(@Param("book") Book book);

    List<Author> findAuthors(@Param("publishedBefore") Integer publishedBefore,
                             @Param("namePattern") String namePattern);
}
```

Každá metoda se páruje s příkazem stejného `id` v XML mapperu se `namespace` rovným plnému jménu rozhraní. Soubor `.xml` leží pod `resources` ve stejné cestě balíčku, takže se najde sám.

### Čeho si všimnout

**Tady žijí typy parametrů.** V XML mapperu je parametr jen `#{publishedBefore}` — bez typu. Typ je v signatuře metody, tedy v Javě, ne v mapování. Pro F8 je to podstatné: operand parametru v mezireprezentaci bude muset typ vzít **odjinud než z dotazu**, což je přesně to, co říká položka o operandu parametru v [`open-items.md`](../../open-items.md).

**`@Param` je nutné od druhého parametru.** S jedním parametrem by se jméno odvodilo; u dvou a víc bez `@Param` zbydou jen poziční názvy `arg0`, `arg1`. Anotace tedy není mapování, je to náhrada za informaci, kterou bytecode nenese.

**Kontrola při kompilaci žádná.** Překlep v `id` příkazu se pozná až při stavbě továrny, překlep v názvu sloupce nikdy — sloupec se proti databázi neověřuje vůbec. Názvy vlastností v `<resultMap>` MyBatis ověří proti třídě, to ano.

---

## Krok 6 — Konfigurace

**`src/main/resources/mybatis-config.xml`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE configuration PUBLIC "-//mybatis.org//DTD Config 3.0//EN"
        "https://mybatis.org/dtd/mybatis-3-config.dtd">

<configuration>

    <settings>
        <setting name="logImpl" value="STDOUT_LOGGING"/>
        <setting name="mapUnderscoreToCamelCase" value="false"/>
    </settings>

    <typeAliases>
        <package name="cz.stochel.ormplayground.mybatis.domain"/>
    </typeAliases>

    <environments default="playground">
        <environment id="playground">
            <transactionManager type="JDBC"/>
            <dataSource type="POOLED">
                <property name="driver" value="com.microsoft.sqlserver.jdbc.SQLServerDriver"/>
                <property name="url"
                          value="jdbc:sqlserver://ormplayground-sql:1433;databaseName=OrmPlayground_MyBatis;encrypt=true;trustServerCertificate=true"/>
                <property name="username" value="ormplayground"/>
                <property name="password" value="Zmen.Si.Me.2026"/>
            </dataSource>
        </environment>
    </environments>

    <mappers>
        <mapper class="cz.stochel.ormplayground.mybatis.mapper.AuthorMapper"/>
    </mappers>
</configuration>
```

### Čeho si všimnout

**Ovladač se tady jmenuje.** U Hibernate i EclipseLinku se JDBC ovladač registruje sám přes `ServiceLoader` a v konfiguraci není. Vestavěný `POOLED` zdroj MyBatisu ale třídu ovladače chce explicitně. S externím `DataSource` nebo přes JNDI by odpadl — je to vlastnost vestavěného zdroje, ne frameworku.

**Žádný dialekt, a to doslova.** Hibernate si dialekt odvodí, EclipseLink platformu taky, NHibernate ho chce v konfiguraci. MyBatis nemá **žádný pojem cílové databáze**; přenositelnost SQL je věcí autora. Pro převodník to znamená, že cílový databázový systém nelze z MyBatis artefaktu přečíst ani odhadnout — SQL uvnitř je jediná stopa, a jak se čte, říká položka o čtení SQL MyBatisu gramatikou T-SQL v [`open-items.md`](../../open-items.md).

**`<typeAliases>` nad balíčkem** dovolí psát `type="Author"` místo plného jména. Je to pohodlí, ale pro parser je to nepřímost navíc: `type="Author"` sám o sobě neurčuje třídu, tu určí až tenhle element v konfiguraci. Pořadí čtení artefaktů (rozhodnutí [068](../../decisions/068-source-framework-precedence-orders-the-reading.md)) tedy u MyBatisu musí začít konfigurací, ne mapperem.

**`mapUnderscoreToCamelCase` je uvedené, i když je vypnuté.** Je to výchozí hodnota a nemusí tam být; napsali jsme ji, protože je to přesně ten druh nastavení, který musí znát parser i builder, aby se názvy párovaly stejně — obdoba pojmenovací strategie u Hibernate. Kritérium rozhodnutí [067](../../decisions/067-a-derived-convention-is-a-statement-a-default-is-not.md) na ni sedí beze změny: je to nastavená konvence, tedy tvrzení, ne absenční default.

---

## Krok 7 — Vytvoření schématu a první data

Schéma i příkazy jsou v témže XML mapperu jako `<resultMap>` z kroku 4. Doplň do něj:

```xml
    <!-- Schema: MyBatis zadne DDL negeneruje, pise ho autor. -->
    <update id="dropSchema">
        DROP TABLE IF EXISTS Books;
        DROP TABLE IF EXISTS Authors;
    </update>

    <update id="createSchema">
        CREATE TABLE Authors (
            AuthorId int IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Name     nvarchar(200)     NOT NULL,
            BornOn   date              NULL
        );
        CREATE TABLE Books (
            BookId        int IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Title         nvarchar(300)     NOT NULL,
            PublishedYear int               NOT NULL,
            Price         decimal(18,2)     NOT NULL,
            AuthorId      int               NOT NULL
                CONSTRAINT FK_Books_Authors REFERENCES Authors (AuthorId)
        );
    </update>

    <insert id="insertAuthor" parameterType="Author"
            useGeneratedKeys="true" keyProperty="id" keyColumn="AuthorId">
        INSERT INTO Authors (Name, BornOn)
        VALUES (#{name}, #{bornOn})
    </insert>

    <insert id="insertBook" useGeneratedKeys="true" keyProperty="book.id" keyColumn="BookId">
        INSERT INTO Books (Title, PublishedYear, Price, AuthorId)
        VALUES (#{book.title}, #{book.publishedYear}, #{book.price}, #{book.author.id})
    </insert>

    <!-- Dynamicky dotaz: rodina prikazu, ne jeden prikaz. -->
    <select id="findAuthors" resultMap="authorWithBooks">
        SELECT a.AuthorId, a.Name, a.BornOn,
               b.BookId, b.Title, b.PublishedYear, b.Price
        FROM Authors a
        LEFT JOIN Books b ON b.AuthorId = a.AuthorId
        <where>
            <if test="publishedBefore != null">
                EXISTS (
                    SELECT 1 FROM Books x
                    WHERE x.AuthorId = a.AuthorId AND x.PublishedYear &lt; #{publishedBefore}
                )
            </if>
            <if test="namePattern != null">
                AND a.Name LIKE #{namePattern}
            </if>
        </where>
        ORDER BY a.AuthorId, b.PublishedYear
    </select>
```

**`Program.java`**

```java
package cz.stochel.ormplayground.mybatis;

import cz.stochel.ormplayground.mybatis.domain.Author;
import cz.stochel.ormplayground.mybatis.domain.Book;
import cz.stochel.ormplayground.mybatis.mapper.AuthorMapper;
import org.apache.ibatis.io.Resources;
import org.apache.ibatis.mapping.BoundSql;
import org.apache.ibatis.mapping.MappedStatement;
import org.apache.ibatis.session.SqlSession;
import org.apache.ibatis.session.SqlSessionFactory;
import org.apache.ibatis.session.SqlSessionFactoryBuilder;

import java.io.InputStream;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public final class Program {

    public static void main(String[] args) throws Exception {
        SqlSessionFactory factory;
        try (InputStream config = Resources.getResourceAsStream("mybatis-config.xml")) {
            factory = new SqlSessionFactoryBuilder().build(config);
        }

        // --- Schema a zapis ---
        try (SqlSession session = factory.openSession()) {
            AuthorMapper mapper = session.getMapper(AuthorMapper.class);
            mapper.dropSchema();
            mapper.createSchema();

            Author capek = new Author();
            capek.setName("Karel Capek");
            capek.setBornOn(LocalDate.of(1890, 1, 9));
            capek.addBook(book("R.U.R.", 1920, "249.00"));
            capek.addBook(book("War with the Newts", 1936, "329.00"));

            Author orwell = new Author();
            orwell.setName("George Orwell");
            orwell.setBornOn(LocalDate.of(1903, 6, 25));
            orwell.addBook(book("1984", 1949, "299.00"));

            for (Author author : List.of(capek, orwell)) {
                mapper.insertAuthor(author);
                for (Book b : author.getBooks()) {
                    mapper.insertBook(b);
                }
            }
            session.commit();
        }

        System.out.println("\n--- Query: authors with a book published before 1930 ---\n");

        try (SqlSession session = factory.openSession()) {
            AuthorMapper mapper = session.getMapper(AuthorMapper.class);
            List<Author> authors = mapper.findAuthors(1930, null);
            System.out.printf("%d radku v seznamu%n%n", authors.size());
            for (Author author : authors) {
                System.out.printf("%s (%d books)%n", author.getName(), author.getBooks().size());
                for (Book b : author.getBooks()) {
                    System.out.printf("  %d  %-25s %8.2f%n",
                            b.getPublishedYear(), b.getTitle(), b.getPrice());
                }
            }
        }

        // --- Krok 9: getBoundSql, SQL bez databaze ---
        System.out.println("\n--- getBoundSql: tyz prikaz, ctyri sady parametru, zadna databaze ---");
        String statementId = "cz.stochel.ormplayground.mybatis.mapper.AuthorMapper.findAuthors";
        MappedStatement statement = factory.getConfiguration().getMappedStatement(statementId);

        printBoundSql(statement, params(null, null), "oba parametry null");
        printBoundSql(statement, params(1930, null), "jen publishedBefore");
        printBoundSql(statement, params(null, "Karel%"), "jen namePattern");
        printBoundSql(statement, params(1930, "Karel%"), "oba parametry");
    }

    private static Map<String, Object> params(Integer publishedBefore, String namePattern) {
        Map<String, Object> map = new HashMap<>();
        map.put("publishedBefore", publishedBefore);
        map.put("namePattern", namePattern);
        return map;
    }

    private static void printBoundSql(MappedStatement statement, Map<String, Object> params, String label) {
        BoundSql boundSql = statement.getBoundSql(params);
        System.out.printf("%n[%s]%n%s%n", label, boundSql.getSql().replaceAll("(?m)^\\s+", "  ").trim());
        boundSql.getParameterMappings()
                .forEach(m -> System.out.printf("   parametr: %s (%s)%n",
                        m.getProperty(), m.getJavaType().getSimpleName()));
    }

    private static Book book(String title, int year, String price) {
        Book b = new Book();
        b.setTitle(title);
        b.setPublishedYear(year);
        b.setPrice(new BigDecimal(price));
        return b;
    }
}
```

### Čeho si všimnout

**Žádný dirty checking, žádný flush.** `insertAuthor` a `insertBook` jsou volání, ne důsledky změny stavu. Objekt po vložení není „managed" a jeho změna nikam nedojde. `session.commit()` je nutné, protože `openSession()` bez argumentu vypne autocommit — to je jediné, co ze správy jednotky práce zbylo.

**Klíč se plní `useGeneratedKeys`, a deklaruje se per příkaz.** `keyProperty="id"` u autora a `keyProperty="book.id"` u knihy, protože v druhém případě je parametr obalený `@Param("book")`. Že `AuthorId` je IDENTITY, tedy v mapování nestojí — stojí to v příkazu, který ho vkládá.

**Vztah se plní ručně.** `#{book.author.id}` je jediné místo, kde se cizí klíč naplní; knihy se musí vkládat po autorovi a v tomhle pořadí. Kaskáda neexistuje, `orphanRemoval` taky ne.

**Ruční DDL je v mapperu jako `<update>`.** Je to legitimní cesta a pro hřiště nejpohodlnější, protože se celý běh obejde bez `sqlcmd`. V produkci by tohle nikdo nedělal a schéma by spravoval Flyway, Liquibase nebo MyBatis Migrations — což jsou všechno samostatné nástroje mimo framework, přesně jako FluentMigrator v .NETu.

---

## Krok 8 — Spuštění

```sh
docker run --rm -v "$PWD":/app -w /app -v ormplayground-m2:/root/.m2 \
  --network ormplayground maven:3.9.11-eclipse-temurin-25-noble \
  mvn -B compile exec:java
```

V Git Bash na Windows předřaď `MSYS_NO_PATHCONV=1` a použij `$(pwd -W)` místo `$PWD`; vysvětlení je v kroku 8 [dílu k EclipseLinku](eclipselink-getting-started.md).

Ve výstupu bys měl vidět:

```
==>  Preparing: INSERT INTO Authors (Name, BornOn) VALUES (?, ?)
==> Parameters: Karel Capek(String), 1890-01-09(LocalDate)
...
==>  Preparing: SELECT a.AuthorId, a.Name, a.BornOn, b.BookId, b.Title, b.PublishedYear, b.Price FROM Authors a LEFT JOIN Books b ON b.AuthorId = a.AuthorId WHERE EXISTS ( SELECT 1 FROM Books x WHERE x.AuthorId = a.AuthorId AND x.PublishedYear < ? ) ORDER BY a.AuthorId, b.PublishedYear
==> Parameters: 1930(Integer)
<==      Total: 2
1 radku v seznamu

Karel Capek (2 books)
  1920  R.U.R.                      249.00
  1936  War with the Newts          329.00
```

### Co ve výstupu hledat

**`Preparing:` / `Parameters:` / `Total:` je nejlepší log ze všech šesti dílů.** Bez jediné závislosti navíc vypíše příkaz, hodnoty **i s javovými typy** a počet vrácených řádků. Hibernate na hodnoty potřebuje SLF4J backend, EF Core `EnableSensitiveDataLogging`, NHibernate je neumí vůbec.

**`Total: 2`, ale `1 radku v seznamu`.** Databáze vrátila dva řádky (Čapek má dvě knihy) a MyBatis z nich složil jednoho autora — deduplikaci zajistilo `<id>` ve `<resultMap>`. Postav si to vedle ostatních dílů, je to trojí různé chování nad týmž dotazem:

| | Deduplikace rodičů z JOINu |
|---|---|
| Hibernate 7.4.5 | automatická, framework |
| EclipseLink 5.0.0 | **žádná** — nutné `SELECT DISTINCT` |
| MyBatis 3.5.19 | automatická, podle `<id>` ve `<resultMap>` |
| Dapper 2.1.79 | ruční, přes dictionary v kódu |

**`nvarchar` v DDL, protože jsme ho tak napsali.** Obě implementace JPA by ze `String` udělaly `varchar`; tady je typ sloupce volba autora DDL a s doménovou třídou nemá nic společného. Parametr `String` navíc ovladač posílá jako `nvarchar` (`sendStringParametersAsUnicode=true` je výchozí), takže nationalizace je u MyBatisu rozdělená mezi dvě místa, z nichž ani jedno není mapování.

---

## Krok 9 — `getBoundSql`: SQL bez databáze

Nejdůležitější krok celého dílu a nejsilnější metodický nástroj, jaký kterýkoli z šesti frameworků nabízí.

`getBoundSql` vyhodnotí dynamické značky pro konkrétní sadu parametrů a vrátí hotový text SQL s otazníky — **bez připojení k databázi, bez transakce, bez session**. Je to protějšek `ToQueryString()` z EF Core, jenže bez běžící infrastruktury a pro libovolné parametry. Výstup našeho programu:

```
[oba parametry null]
SELECT a.AuthorId, a.Name, a.BornOn,
  b.BookId, b.Title, b.PublishedYear, b.Price
  FROM Authors a
  LEFT JOIN Books b ON b.AuthorId = a.AuthorId
  ORDER BY a.AuthorId, b.PublishedYear

[jen publishedBefore]
  ...
  WHERE EXISTS (
  SELECT 1 FROM Books x
  WHERE x.AuthorId = a.AuthorId AND x.PublishedYear < ?
  )
  ORDER BY a.AuthorId, b.PublishedYear
   parametr: publishedBefore (Object)

[jen namePattern]
  ...
  WHERE  a.Name LIKE ?
  ORDER BY a.AuthorId, b.PublishedYear
   parametr: namePattern (Object)

[oba parametry]
  ...
  WHERE EXISTS (
  SELECT 1 FROM Books x
  WHERE x.AuthorId = a.AuthorId AND x.PublishedYear < ?
  )
  AND a.Name LIKE ?
  ORDER BY a.AuthorId, b.PublishedYear
   parametr: publishedBefore (Object)
   parametr: namePattern (Object)
```

Čtyři pozorování, každé s důsledkem pro mezireprezentaci:

**1. Jeden `<select>` dal čtyři různé příkazy.** Bez `WHERE`, se dvěma různými `WHERE` a s oběma. Dotazová jednotka MyBatisu tedy **není dotaz, ale rodina dotazů**, a `getBoundSql` vrací jednoho člena. Mezireprezentace dnes žádný pojem pro takovou rodinu nemá — a co s ní, musí vyslovit rozhodnutí k F8. Samotné čtení jednoho členu je slepá ulička: `[oba parametry null]` a `[oba parametry]` se liší celou klauzulí `WHERE`, takže překlad postavený na jediné sadě parametrů by tiše zahodil zbytek rodiny.

**2. `<where>` není text, je to operace nad stromem podmínek.** Ve variantě `[jen namePattern]` je ve `WHERE  a.Name LIKE ?` — počáteční `AND`, které je v mapperu napsané, zmizelo. `<where>` klauzuli vynechá, když je prázdná, a odstraní úvodní `AND` nebo `OR`, když není. Je to přesně ta operace, kterou v mezireprezentaci dělá strom podmínek (invariant „podmínky jsou strom, ne plochý seznam"), jen vyjádřená v XML.

**3. Typ parametru v `BoundSql` není.** Vypsaný javový typ je `Object`, protože příkaz jsme volali mapou. Skutečný typ — `Integer` a `String` — je v signatuře metody `findAuthors` z kroku 5, tedy **v Javě, ne v mapování**. Operand parametru z rozhodnutí k F8 tedy typ nemůže brát z dotazové jednotky; musí ho vzít z podpisu metody, případně odvodit přes mapovací mezireprezentaci.

**4. `#{}` a `${}` se chovají opačně a je to bezpečnostní rozdíl.** `#{publishedBefore}` se stalo `?`, tedy vázaným parametrem `PreparedStatement`. `${…}` by se místo toho textově dosadilo do SQL — je to jediná cesta, jak dynamicky vložit název sloupce nebo směr řazení, a zároveň jediné místo, kde je MyBatis otevřený SQL injection. Pro parser je podstatné, že jedno je operand a druhé je změna textu dotazu, a nesmí se splést.

---

## Anotace a XML se nevrství

Tenhle nález má vlastní sekci, protože jde proti tomu, na co jsou zvyklé oba ostatní javové frameworky. U JPA je pořadí čtení dané specifikací — anotace první, `orm.xml` poslední a přebíjí je. MyBatis nic takového nemá: **stejné `id` v anotaci a v XML je chyba.**

Zkus přidat na metodu `findAuthors` anotaci:

```java
@Select("SELECT AuthorId, Name, BornOn FROM Authors")
List<Author> findAuthors(@Param("publishedBefore") Integer publishedBefore,
                         @Param("namePattern") String namePattern);
```

Aplikace pak neselže při volání dotazu, ale už **při stavbě továrny**:

```
org.apache.ibatis.builder.BuilderException: Error parsing SQL Mapper Configuration.
Cause: java.lang.IllegalArgumentException: Mapped Statements collection already contains key
cz.stochel.ormplayground.mybatis.mapper.AuthorMapper.findAuthors.
please check cz/stochel/ormplayground/mybatis/mapper/AuthorMapper.xml
and cz/stochel/ormplayground/mybatis/mapper/AuthorMapper.java (best guess)
```

Referenční dokumentace MyBatisu pravidlo pro souběh anotací a XML nevyslovuje; tenhle běh ho doložil. Pro rozhodnutí k F8 z toho plyne přesně to, co předjímá [srovnání javových frameworků](../java-orm-frameworks-comparison.md): souběh se zapíše jako `Failure` po vzoru rozhodnutí [063](../../decisions/063-stated-keylessness-as-a-carried-fact.md), **ne jako `Conflict`**. Konflikt předpokládá dvě tvrzení, mezi nimiž lze rozhodnout podle precedence; tady žádná precedence neexistuje a framework sám odmítne model postavit.

Rozhodnutí [068](../../decisions/068-source-framework-precedence-orders-the-reading.md) je tím pádem u MyBatisu aplikovatelné jen zpola: pořadí čtení mezi konfigurací a mapperem dává (konfigurace první kvůli `<typeAliases>`), pořadí mezi anotací a XML nedává, protože žádné není.

---

## Typické chyby

| Hláška | Příčina |
|---|---|
| `docker: executable file not found` u `-w /app` | Git Bash přepsal cestu; `MSYS_NO_PATHCONV=1` a `$(pwd -W)` (krok 8) |
| `The TCP/IP connection to the host ... has failed` | kontejnery nejsou na společné síti, nebo se aplikace připojuje na `localhost` |
| `PKIX path building failed` | chybí `trustServerCertificate=true` |
| `Cannot open database "OrmPlayground_MyBatis"` | databáze neexistuje; MyBatis netvoří ani databázi, ani tabulky (krok 0c) |
| `Invalid object name 'Authors'` | neproběhl `createSchema` — MyBatis DDL negeneruje |
| `Mapped Statements collection already contains key ...` | totéž `id` v anotaci i v XML (sekce výš) |
| `Type interface ... is not known to the MapperRegistry` | rozhraní není v `<mappers>` konfigurace |
| `Invalid bound statement (not found)` | XML mapper není na classpath ve stejné cestě balíčku jako rozhraní (krok 1), nebo nesedí `namespace` |
| `There is no getter for property named 'arg1'` | chybí `@Param` u metody s víc parametry (krok 5) |
| vlastnost zůstala `null` nebo `0` bez chyby | název sloupce nesedí a `<result>` pro něj není; automatické mapování sloupec bez protějšku tiše zahodí |
| tři autoři místo jednoho s dvěma knihami | chybí `<id>` ve `<resultMap>` (krok 4) |
| `ExecutorException: Executor was closed` | práce s líně načítanou kolekcí po zavření `SqlSession` — obdoba `LazyInitializationException` |

---

## Srovnání všech šesti

Rozšíření tabulky z dílu k EclipseLinku o sloupec MyBatis. Řádky, kde MyBatis a Dapper stojí na téže straně, ukazují, kde je javový protějšek přesný; řádky, kde se liší, jsou ty zajímavé.

| | NHibernate | EF Core | Dapper | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| Ekosystém | .NET | .NET | .NET | Java | Java | Java |
| Standardizační vrstva | — | — | — | **JPA 3.2** | **JPA 3.2** | — |
| Balíčky | 2 | 1 | 2 | 2 | 2 | 2 |
| Tranzitivní závislosti | ano | ano | ne | mnoho | několik | **žádné** |
| Požadavky na entity | `virtual`, ctor | žádné | žádné | ctor, ne `final` | totéž | **žádné** |
| Závislost domény na frameworku | ano | ne | ne | na specifikaci | na specifikaci | **žádná** |
| Deklarativní místo pro mapování | ano | ano | **není** | ano | ano | **ano, `<resultMap>`** |
| Mapování je vázané na | třídu | třídu | — | třídu | třídu | **příkaz** |
| Znalost primárního klíče | ano | ano | ne | ano | ano | **jen jako příznak identity** |
| Znalost relací | ano | ano | ne | ano | ano | **jen uvnitř `<resultMap>`** |
| Vlastnictví vztahu | `inverse` | `HasOne/WithMany` | — | `mappedBy` | `mappedBy` | **žádné** |
| SQL píše | framework | framework | autor | framework | framework | **autor** |
| Dynamické SQL | ne | ne | řetězce | ne | ne | **`<if>`, `<where>`, OGNL** |
| Dialekt | v konfiguraci | v provideru | žádný | odvozený | odvozený | **žádný** |
| Ovladač | v konfiguraci | v provideru | v kódu | sám | sám | **v konfiguraci** |
| Vytvoření tabulek | `SchemaExport` | `EnsureCreated` | ručně | JPA standard | JPA standard | **ručně** |
| Sledování změn | ano | ano | ne | ano | ano | **ne** |
| Explicitní transakce | nutná | volitelná | ruční | nutná | nutná | **`commit()` na session** |
| Deduplikace u JOINu | ruční | automatická | ruční | automatická | **ruční** | automatická |
| Identity map | ano | ano | ne | ano | ano | **ne** (cache klíčovaná dotazem) |
| Hodnoty parametrů v logu | ne | `EnableSensitiveData…` | — | SLF4J backend | ano | **ano, vestavěné** |
| SQL bez databáze | ne | `ToQueryString()` | triviálně | ne | jen po `prepareCall` | **`getBoundSql`** |
| Dotazovací jazyk | LINQ, HQL | LINQ | SQL | HQL ⊃ JPQL | EQL ⊃ JPQL | **SQL** |

---

## Co z toho plyne pro převodník

**1. MyBatis není Dapper, a je to dobrá zpráva.** `<resultMap>` nese dvojice sloupec–vlastnost i vnořený vztah, takže parser MyBatis zdroje **má z čeho postavit entitní mapu** bez katalogu — na rozdíl od Dapperu, kde entitní mapa vzniká celá doplněním (F6). Katalog bude pořád potřeba na délky, nullabilitu a typy, ale ne na základní tvar.

**2. Mapování vázané na příkaz je nový tvar.** `Relation` v mezireprezentaci visí na `EntityMap` (rozhodnutí [001](../../decisions/001-entity-reference-by-name.md)) a předpokládá jedno mapování na třídu. MyBatis dovolí dva `<resultMap>` nad touž třídou, které o vztahu tvrdí různé věci. Rozhodnutí k F8 musí říct, co je konflikt a co legitimní pohled — a je to tentýž typ otázky jako u aliasů Dapperu v položce o propsání aliasu do entitní mapy.

**3. Dynamický příkaz je rodina, ne dotaz.** Tohle je největší otevřená věc a `getBoundSql` ji doložil čtyřmi různými příkazy z jednoho `<select>`. Bez pojmu parametru v mezireprezentaci (rozhodnutí [024](../../decisions/024-typed-query-operand.md) ho vědomě odložilo) nelze F8 splnit vůbec; dnes takový dotaz parser odmítá záznamem `Failure` kategorie `QueryParameter` (rozhodnutí [070](../../decisions/070-a-parser-refuses-what-would-change-the-row-set.md)). Operand parametru proto dostane vlastní rozhodnutí s F8 jako prvním producentem, a tenhle běh mu dává čtyři konkrétní vstupy: typ se bere z podpisu metody, `#{}` je vázaná hodnota a `${}` textová substituce, `<where>` je operace nad stromem podmínek, a jediný vyhodnocený člen rodinu nereprezentuje.

**4. Souběh anotace a XML je `Failure`, ne `Conflict`.** Doloženo výjimkou při stavbě továrny. Precedence mezi oběma formami neexistuje, takže není mezi čím rozhodovat — a záznam má mít tvar podle rozhodnutí [063](../../decisions/063-stated-keylessness-as-a-carried-fact.md).

**5. Jednotka převodu je u MyBatisu mapování i dotaz zároveň.** Jeden `AuthorMapper.xml` nese `<resultMap>` i `<select>`. Orchestrace dnes dělí jednotky na entitní a dotazové podle typu obsahu a pouští je ve dvou průchodech; tenhle soubor patří do obou. Je to položka „Jednotka, která je mapování i dotaz zároveň" v [`open-items.md`](../../open-items.md) a tenhle díl je jejím nejkonkrétnějším dokladem: dělit takový soubor by musel klient, což jde proti F14.

---

## Kam pokračovat

**1. `<foreach>` a `IN` se seznamem hodnot.** Nejčastější důvod, proč se dynamické SQL vůbec používá, a pro mezireprezentaci zajímavý tím, že počet parametrů závisí na datech. Seznam hodnot jako čtvrtý tvar operandu už mezireprezentace zná; tady by se ukázalo, jestli to stačí.

**2. Vnořený `<select>` místo JOINu.** Druhá cesta, jak naplnit `<collection>`, a rovnou N+1 z definice. Stojí za vyzkoušení kvůli `fetchType="lazy"` a `lazyLoadingEnabled` — je to jediné místo, kde MyBatis proxy vůbec používá.

**3. `@SelectProvider`.** SQL skládané javovým kódem místo XML. Pro převodník je to hranice čitelnosti: co je ještě artefakt, který jde přečíst, a co už je program, který se musí spustit. Odpověď na to patří k hranici předávaného artefaktu (rozhodnutí [040](../../decisions/040-boundary-of-the-handed-over-artifact.md)).
