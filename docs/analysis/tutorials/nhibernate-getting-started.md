# NHibernate od nuly — Visual Studio 2026, .NET 10, MS SQL Server

Tutoriál staví minimální konzolovou aplikaci s doménou `Author` 1:N `Book`, mapováním přes `hbm.xml`, konfigurací v kódu a automatickým vytvořením schématu přes `SchemaExport`.

Ověřené verze: NHibernate **5.7.0** (vydáno 2026-06-30, cílí .NET 10 přímo), .NET **10**, MS SQL Server.

---

## Krok 0 — Prázdná databáze

NHibernate umí vytvořit **tabulky**, ale ne **databázi**. Tu musíš založit sám, jinak dostaneš `Cannot open database "OrmPlayground" requested by the login`.

Ve Visual Studiu: **View → SQL Server Object Explorer** → rozbal svůj server → pravým na **Databases → Add New Database** → název `OrmPlayground`.

Nebo v novém dotazu:

```sql
CREATE DATABASE OrmPlayground;
```

Zároveň si tady ověř, jak se k serveru připojuješ — název instance z Object Exploreru potřebuješ v kroku 5.

---

## Krok 1 — Založení projektu

1. **File → New → Project**
2. Šablona **Console App** (C#), ne „Console App (.NET Framework)"
3. Project name: `OrmPlayground.NHibernate`
4. Solution name: `OrmPlayground`, zaškrtni **Place solution and project in the same directory** = *odškrtnuto* (chceš solution složku, budeš přidávat další projekty pro EF Core a Dapper)
5. Framework: **.NET 10.0**, **Do not use top-level statements** = *odškrtnuto* (top-level statements necháme, je to kratší)

---

## Krok 2 — NuGet balíčky

Pravým na projekt → **Manage NuGet Packages → Browse**. Nainstaluj:

| Balíček | Proč |
|---|---|
| `NHibernate` | samotný ORM, verze 5.7.0 nebo novější |
| `Microsoft.Data.SqlClient` | ADO.NET provider; NHibernate ho načítá reflexí, takže **musí být referencovaný v projektu**, i když ho v kódu přímo nepoužiješ |

Nejnovější stabilní verzi `Microsoft.Data.SqlClient` nech vybrat NuGet.

> **Proč ne `System.Data.SqlClient`:** starší tutoriály i `benchmarks/ORMComparison.sln` používají `SqlClientDriver`, který jede na `System.Data.SqlClient`. Ten je deprecated a nepodporuje novější funkce SQL Serveru. Pro nový kód `MicrosoftDataSqlClientDriver`.

---

## Krok 3 — Doménové třídy

Vytvoř složku `Domain` a v ní dva soubory.

**`Domain/Author.cs`**

```csharp
namespace OrmPlayground.NHibernate.Domain;

public class Author
{
    public virtual int Id { get; set; }
    public virtual string Name { get; set; } = string.Empty;
    public virtual DateTime? BornOn { get; set; }
    public virtual ISet<Book> Books { get; set; } = new HashSet<Book>();

    public virtual void AddBook(Book book)
    {
        book.Author = this;
        Books.Add(book);
    }
}
```

**`Domain/Book.cs`**

```csharp
namespace OrmPlayground.NHibernate.Domain;

public class Book
{
    public virtual int Id { get; set; }
    public virtual string Title { get; set; } = string.Empty;
    public virtual int PublishedYear { get; set; }
    public virtual decimal Price { get; set; }
    public virtual Author Author { get; set; } = null!;
}
```

### Dvě věci, které NHibernate vynucuje

**Všechny mapované členy musí být `virtual`.** NHibernate za běhu generuje proxy třídu, která z entity dědí a přepisuje property, aby mohla zachytit přístup a dotáhnout lazy data. Bez `virtual` to nejde a dostaneš `The following types may not be used as proxies: ... method get_Name should be virtual`.

**Musí existovat bezparametrický konstruktor**, aspoň `protected`. Tady ho máš implicitně, protože žádný jiný nedefinuješ. Jakmile přidáš konstruktor s parametry, musíš bezparametrický dopsat ručně.

Obojí si poznamenej do analýzy — je to přesně ten typ implicitního požadavku, který EF Core nemá a který se do IR nepřenáší.

---

## Krok 4 — Mapování

Vytvoř složku `Mappings`. Soubory přidávej přes **Add → New Item → XML File** s příponou `.hbm.xml`.

**`Mappings/Author.hbm.xml`**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2"
                   assembly="OrmPlayground.NHibernate"
                   namespace="OrmPlayground.NHibernate.Domain">
  <class name="Author" table="Authors">
    <id name="Id" column="AuthorId" type="int">
      <generator class="identity" />
    </id>
    <property name="Name" column="Name" type="string" length="200" not-null="true" />
    <property name="BornOn" column="BornOn" type="DateTime" />
    <set name="Books" cascade="all-delete-orphan" inverse="true">
      <key column="AuthorId" />
      <one-to-many class="Book" />
    </set>
  </class>
</hibernate-mapping>
```

**`Mappings/Book.hbm.xml`**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<hibernate-mapping xmlns="urn:nhibernate-mapping-2.2"
                   assembly="OrmPlayground.NHibernate"
                   namespace="OrmPlayground.NHibernate.Domain">
  <class name="Book" table="Books">
    <id name="Id" column="BookId" type="int">
      <generator class="identity" />
    </id>
    <property name="Title" column="Title" type="string" length="300" not-null="true" />
    <property name="PublishedYear" column="PublishedYear" type="int" not-null="true" />
    <property name="Price" column="Price" type="decimal" precision="18" scale="2" not-null="true" />
    <many-to-one name="Author" column="AuthorId" class="Author" not-null="true" />
  </class>
</hibernate-mapping>
```

### Co tady stojí za pozornost

`inverse="true"` na `<set>` říká, že vlastníkem vztahu je strana `Book` — tedy že FK sloupec zapisuje `Book`, ne kolekce. Bez toho by NHibernate po insertu knih poslal ještě zbytečný `UPDATE`, aby FK nastavil znovu. Tohle je NHibernate specifikum, které v EF Core nemá přímý protějšek.

`cascade="all-delete-orphan"` znamená, že uložení autora uloží i jeho knihy a odebrání knihy z kolekce ji smaže z databáze.

IntelliSense pro `hbm.xml` nemusí ve VS fungovat — schéma `urn:nhibernate-mapping-2.2` není registrované. Není to chyba a na běh to nemá vliv.

---

## Krok 5 — Embedded Resource (nejčastější důvod, proč tutoriály padají)

NHibernate hledá mapování **jako embedded resource v assembly**, ne jako soubory na disku. V SDK-style projektech se `.hbm.xml` embedded resource ve výchozím stavu **nestane**.

Pravým na projekt → **Edit Project File** a doplň:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Mappings\**\*.hbm.xml" />
  </ItemGroup>
```

Wildcard je lepší než vyjmenovávat soubory jednotlivě (jak to dělá `NHibernateEntities.csproj` v benchmarcích) — na nové mapování pak není nutné myslet.

Po tomhle kroku by celý `.csproj` měl vypadat zhruba takto:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>OrmPlayground.NHibernate</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="..." />
    <PackageReference Include="NHibernate" Version="5.7.0" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Mappings\**\*.hbm.xml" />
  </ItemGroup>

</Project>
```

---

## Krok 6 — Konfigurace

Vytvoř složku `Persistence`.

**`Persistence/SessionFactoryBuilder.cs`**

```csharp
using NHibernate.Cfg;
using NHibernate.Cfg.Loquacious;
using NHibernate.Dialect;
using NHibernate.Driver;
using OrmPlayground.NHibernate.Domain;

namespace OrmPlayground.NHibernate.Persistence;

public static class SessionFactoryBuilder
{
    // Uprav podle svojí instance ze SQL Server Object Exploreru.
    private const string ConnectionString =
        "Server=localhost;Database=OrmPlayground;Trusted_Connection=True;TrustServerCertificate=True;";

    public static Configuration BuildConfiguration()
    {
        return new Configuration()
            .DataBaseIntegration(db =>
            {
                db.ConnectionString = ConnectionString;
                db.Driver<MicrosoftDataSqlClientDriver>();
                db.Dialect<MsSql2012Dialect>();
                db.LogSqlInConsole = true;
                db.LogFormattedSql = true;
            })
            .AddAssembly(typeof(Author).Assembly);
    }
}
```

`AddAssembly` projde assembly a načte všechny embedded `.hbm.xml`.

`LogSqlInConsole` je pro účely analýzy nejdůležitější nastavení v celém tutoriálu — každý dotaz je vidět přesně tak, jak jde do databáze.

### Connection string

- `Server=localhost` — pokud máš pojmenovanou instanci, bude to `localhost\SQLEXPRESS` nebo podobně. Přesný název najdeš v SQL Server Object Exploreru.
- `Trusted_Connection=True` — Windows autentizace. Pro SQL autentizaci nahraď `User Id=...;Password=...`.
- `TrustServerCertificate=True` — **nutné**. `Microsoft.Data.SqlClient` má od verze 4.0 `Encrypt=true` jako výchozí a lokální SQL Server má self-signed certifikát. Bez tohohle dostaneš `A connection was successfully established with the server, but then an error occurred during the login process` nebo hlášku o nedůvěryhodném certifikátu. Tohle je jeden z hlavních důvodů, proč starší tutoriály nefungují — psaly se, když byl výchozí stav opačný.

---

## Krok 7 — Vytvoření schématu a první data

**`Program.cs`**

```csharp
using NHibernate;
using NHibernate.Linq;
using NHibernate.Tool.hbm2ddl;
using OrmPlayground.NHibernate.Domain;
using OrmPlayground.NHibernate.Persistence;

var configuration = SessionFactoryBuilder.BuildConfiguration();

// Dropne a znovu vytvoří tabulky. DDL se vypíše do konzole.
new SchemaExport(configuration).Create(useStdOut: true, execute: true);

using var sessionFactory = configuration.BuildSessionFactory();

// --- Zápis ---
using (var session = sessionFactory.OpenSession())
using (var transaction = session.BeginTransaction())
{
    var capek = new Author
    {
        Name = "Karel Capek",
        BornOn = new DateTime(1890, 1, 9)
    };
    capek.AddBook(new Book { Title = "R.U.R.", PublishedYear = 1920, Price = 249m });
    capek.AddBook(new Book { Title = "War with the Newts", PublishedYear = 1936, Price = 329m });

    var orwell = new Author { Name = "George Orwell", BornOn = new DateTime(1903, 6, 25) };
    orwell.AddBook(new Book { Title = "1984", PublishedYear = 1949, Price = 299m });

    session.Save(capek);
    session.Save(orwell);

    transaction.Commit();
}

Console.WriteLine("\n--- Query: authors with a book published before 1930 ---\n");

// --- Cteni ---
using (var session = sessionFactory.OpenSession())
{
    var authors = session.Query<Author>()
        .Where(a => a.Books.Any(b => b.PublishedYear < 1930))
        .FetchMany(a => a.Books)
        .ToList();

    foreach (var author in authors)
    {
        Console.WriteLine($"{author.Name} ({author.Books.Count} books)");
        foreach (var book in author.Books.OrderBy(b => b.PublishedYear))
        {
            Console.WriteLine($"  {book.PublishedYear}  {book.Title,-25} {book.Price,8:N2}");
        }
    }
}
```

`SchemaExport(...).Create(...)` napřed dropne existující tabulky, pak je vytvoří. Pro hřiště je to ideální — každé spuštění začíná načisto. V reálné aplikaci tohle samozřejmě nechceš.

---

## Krok 8 — Spuštění

**Ctrl+F5**. V konzoli bys měl vidět:

1. **DDL** vygenerované NHibernatem — `create table Authors (...)`, `create table Books (...)` včetně FK constraintu. Přečti si ho pozorně: uvidíš, jaké SQL typy si NHibernate odvodil z `type="string" length="200"` a z `type="decimal" precision="18" scale="2"`.
2. **INSERT příkazy** — všimni si pořadí (nejdřív autor, pak knihy) a toho, že díky `inverse="true"` nenásleduje žádný dodatečný `UPDATE`.
3. **SELECT** s `left outer join`, který vznikl z `FetchMany`.
4. Výpis obou autorů s knihami.

Když tohle vidíš, máš funkční NHibernate a můžeš začít experimentovat.

---

## Typické chyby

| Hláška | Příčina |
|---|---|
| `No persister for: OrmPlayground.NHibernate.Domain.Author` | mapování se nenačetlo — chybí `EmbeddedResource` v `.csproj` (krok 5) |
| `Could not compile the mapping document` + `Persistent class not found` | nesedí `assembly` nebo `namespace` atribut v `hibernate-mapping` |
| `method get_Name should be virtual` | chybí `virtual` na property |
| `Could not create the driver from NHibernate.Driver.MicrosoftDataSqlClientDriver` | není nainstalovaný NuGet `Microsoft.Data.SqlClient` |
| chyba o certifikátu při přihlášení | chybí `TrustServerCertificate=True` v connection stringu |
| `Cannot open database "OrmPlayground"` | databáze neexistuje — `SchemaExport` vytváří tabulky, ne databázi (krok 0) |
| `LazyInitializationException` / `Initializing[...] failed to lazily initialize` | přístup ke kolekci mimo otevřenou session |

---

## Kam pokračovat

Až tohle poběží, následují témata, kvůli kterým hřiště vzniklo. V pořadí podle užitečnosti pro analýzu:

**1. Kompozitní klíč.** Přidej `BookTranslation` s klíčem `(BookId, LanguageCode)` a namapuj přes `<composite-id>`. Narazíš na to, že NHibernate vyžaduje na klíčové třídě override `Equals` a `GetHashCode` — klíč s sebou táhne sémantiku identity, což je věc, kterou EF Core nevyžaduje vůbec. Přesně tenhle rozdíl je materiál pro rozhodnutí, jestli `PrimaryKeyPart` v IR stačí, nebo jestli potřebuješ koncept „třídy klíče".

**2. N:M.** Přidej `Category` a namapuj `Book` ↔ `Category` přes `<set>` s `<many-to-many>`. Junction tabulka nemá vlastní entitu — to je opak toho, co generuje IR podle rozhodnutí [005](../../decisions/005-many-to-many-as-explicit-junction-entity.md).

**3. DDL bez databáze.** Pro analýzu často nepotřebuješ nic spouštět, stačí vygenerovaný DDL:

```csharp
var ddl = new StringBuilder();
new SchemaExport(configuration).Create(s => ddl.AppendLine(s), execute: false);
File.WriteAllText("schema.sql", ddl.ToString());
```

S `execute: false` se k databázi vůbec nepřipojí. Výsledkem je čitelný výstup toho, jak NHibernate interpretoval mapování — vhodný podklad pro srovnávací tabulky a pro citace v textu práce.

**4. `<subclass>` a `<joined-subclass>`** pro dědičnost (table-per-hierarchy vs. table-per-subclass). V benchmarcích to úplně chybí, jde tedy o neprobádaný terén.
