# EF Core od nuly — Visual Studio 2026, .NET 10, MS SQL Server

Stejná doména jako v NHibernate tutoriálu (`Author` 1:N `Book`), stejný SQL Server, stejné číslování kroků. Cílem je, abys mohl oba dokumenty položit vedle sebe a číst rozdíly řádek po řádku.

Ověřené verze: EF Core **10.0.10**, .NET **10**, MS SQL Server.

---

## Krok 0 — Prázdná databáze

**Odpadá.** EF Core přes `EnsureCreated()` vytvoří databázi *i* tabulky. NHibernate přes `SchemaExport` vytvoří jen tabulky a databáze už musí existovat.

První rozdíl do analýzy, a není kosmetický: `SchemaExport` operuje na úrovni DDL objektů uvnitř databáze, zatímco `EnsureCreated` zahrnuje i provisioning samotné databáze.

---

## Krok 1 — Založení projektu

Přidej do existující solution `OrmPlayground` druhý projekt, ať máš obojí vedle sebe:

1. Pravým na solution → **Add → New Project**
2. Šablona **Console App** (C#)
3. Project name: `OrmPlayground.EFCore`
4. Framework: **.NET 10.0**

Startovací projekt přepínáš pravým na projekt → **Set as Startup Project**.

---

## Krok 2 — NuGet balíčky

Pravým na projekt → **Manage NuGet Packages → Browse**:

| Balíček | Proč |
|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | provider pro SQL Server; verze 10.0.10 nebo novější |

To je vše. `Microsoft.Data.SqlClient` i `Microsoft.EntityFrameworkCore.Relational` přijdou jako tranzitivní závislosti — na rozdíl od NHibernate, kde jsi musel ADO.NET provider referencovat ručně, protože si ho driver načítá reflexí.

---

## Krok 3 — Doménové třídy

Vytvoř složku `Domain`.

**`Domain/Author.cs`**

```csharp
namespace OrmPlayground.EFCore.Domain;

public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? BornOn { get; set; }
    public ICollection<Book> Books { get; set; } = new List<Book>();

    public void AddBook(Book book)
    {
        book.Author = this;
        Books.Add(book);
    }
}
```

**`Domain/Book.cs`**

```csharp
namespace OrmPlayground.EFCore.Domain;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PublishedYear { get; set; }
    public decimal Price { get; set; }

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;
}
```

### Čeho si všimnout proti NHibernate

**Žádné `virtual`.** EF Core nepoužívá proxy pro běžný provoz — change tracking řeší snapshotem stavu entity, ne interceptem property. `virtual` bys potřeboval jen kdybys explicitně zapnul lazy-loading proxies (samostatný balíček, opt-in).

**Žádný požadavek na bezparametrický konstruktor.** EF Core umí materializovat i přes konstruktor s parametry.

**Přibyla `AuthorId`.** Tohle je EF Core idiom — explicitní FK property vedle navigační property. Není povinná (EF Core by si vytvořil stínovou vlastnost), ale bez ní nemáš k FK z kódu přístup. NHibernate mapping tuhle dvojkolejnost nemá: buď mapuješ `<many-to-one>`, nebo `<property>`, ne obojí na stejný sloupec.

Všechno tři jsou implicitní požadavky, kterými se frameworky liší a které se do IR nepřenášejí.

---

## Krok 4 — Mapování

Tady je největší koncepční rozdíl. NHibernate bez mapovacího souboru neudělá nic. EF Core má **konvence**: kdybys nenapsal ani řádek konfigurace, model už funguje.

Konvence, které se uplatní na doméně výše:

| Co | Jak to konvence odvodí |
|---|---|
| Tabulky | z názvu `DbSet` property — `Authors`, `Books` |
| Primární klíč | property jménem `Id` nebo `<Typ>Id` |
| Generování klíče | `int` PK → `IDENTITY` |
| Sloupce | název = název property |
| Nullabilita | z nullable reference types a `?` u hodnotových typů |
| Vztah 1:N | z dvojice `Author.Books` + `Book.Author` |
| FK sloupec | `AuthorId` podle vzoru `<Navigace>Id` |
| Mazání | FK je non-nullable → `DeleteBehavior.Cascade` |

Vytvoř složku `Persistence`.

**`Persistence/PlaygroundContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrmPlayground.EFCore.Domain;

namespace OrmPlayground.EFCore.Persistence;

public class PlaygroundContext : DbContext
{
    // Uprav podle svojí instance ze SQL Server Object Exploreru.
    private const string ConnectionString =
        "Server=localhost;Database=OrmPlayground_EFCore;Trusted_Connection=True;TrustServerCertificate=True;";

    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder
            .UseSqlServer(ConnectionString)
            .LogTo(Console.WriteLine,
                   [DbLoggerCategory.Database.Command.Name],
                   LogLevel.Information)
            .EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Author>(entity =>
        {
            entity.ToTable("Authors");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasColumnName("AuthorId");
            entity.Property(a => a.Name).HasMaxLength(200).IsRequired();
            entity.Property(a => a.BornOn);
        });

        modelBuilder.Entity<Book>(entity =>
        {
            entity.ToTable("Books");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Id).HasColumnName("BookId");
            entity.Property(b => b.Title).HasMaxLength(300).IsRequired();
            entity.Property(b => b.PublishedYear).IsRequired();
            entity.Property(b => b.Price).HasPrecision(18, 2);

            entity.HasOne(b => b.Author)
                  .WithMany(a => a.Books)
                  .HasForeignKey(b => b.AuthorId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

Většina těch řádků jen potvrzuje to, co by konvence odvodila sama — `ToTable`, `HasKey`, `HasOne/WithMany`. Napsané jsou schválně, aby bylo vidět, co je explicitní a co ne. Řádky, které konvence *nedokáže* nahradit, jsou tři: `HasColumnName` (jinak by sloupec byl `Id`), `HasMaxLength` (jinak `nvarchar(max)`) a `HasPrecision` (jinak výchozí `decimal(18,2)`, ale spolehnout se na to nechceš).

`EnableSensitiveDataLogging` zapne výpis hodnot parametrů. V produkci se nepoužívá, na hřišti ho chceš.

### Data annotations jako alternativa

EF Core umí totéž atributy přímo na entitě, jak to máš v `EFCoreEntities` v benchmarcích:

```csharp
[Table("Books")]
public class Book
{
    [Key]
    [Column("BookId")]
    public int Id { get; set; }

    [MaxLength(300)]
    public required string Title { get; set; }
}
```

Fluent API je mocnější (annotations neumí například `OnDelete` nebo kompozitní klíč přes `HasKey` s více property) a drží mapování mimo doménovou třídu. Pro analýzu je podstatné, že **EF Core má tři vrstvy, které se skládají**: konvence → data annotations → fluent API, přičemž pozdější přebíjí dřívější. NHibernate má vrstvu jednu.

---

## Krok 5 — Embedded Resource

**Odpadá.** Mapování je kód, kompiluje se do assembly. Žádné XML, žádné build actions, žádná runtime chyba typu „mapování se nenačetlo".

---

## Krok 6 — Konfigurace

**Splynulo s krokem 4.** Connection string, provider i logování jsou v `OnConfiguring` uvnitř téhož `DbContext`. NHibernate má konfiguraci (`Configuration`) a mapování (`hbm.xml`) oddělené.

Alternativa je předat `DbContextOptions<T>` konstruktorem — to je varianta pro dependency injection a máš ji v benchmarcích. Pro konzolovku je `OnConfiguring` kratší.

Connection string má stejná pravidla jako u NHibernate, včetně `TrustServerCertificate=True` — EF Core SqlServer provider stojí na stejném `Microsoft.Data.SqlClient`, takže i stejná past s výchozím `Encrypt=true`.

---

## Krok 7 — Vytvoření schématu a první data

**`Program.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using OrmPlayground.EFCore.Domain;
using OrmPlayground.EFCore.Persistence;

using (var context = new PlaygroundContext())
{
    // Vypis DDL bez jeho provedeni - analogie SchemaExport(cfg).Create(action, false)
    Console.WriteLine("--- Generated DDL ---");
    Console.WriteLine(context.Database.GenerateCreateScript());

    context.Database.EnsureDeleted();
    context.Database.EnsureCreated();
}

// --- Zapis ---
using (var context = new PlaygroundContext())
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

    context.Authors.AddRange(capek, orwell);
    context.SaveChanges();
}

Console.WriteLine("\n--- Query: authors with a book published before 1930 ---\n");

// --- Cteni ---
using (var context = new PlaygroundContext())
{
    var query = context.Authors
        .Where(a => a.Books.Any(b => b.PublishedYear < 1930))
        .Include(a => a.Books);

    // SQL jeste pred spustenim dotazu
    Console.WriteLine(query.ToQueryString());
    Console.WriteLine();

    foreach (var author in query.ToList())
    {
        Console.WriteLine($"{author.Name} ({author.Books.Count} books)");
        foreach (var book in author.Books.OrderBy(b => b.PublishedYear))
        {
            Console.WriteLine($"  {book.PublishedYear}  {book.Title,-25} {book.Price,8:N2}");
        }
    }
}
```

Tři věci, které se liší od NHibernate verze:

**`SaveChanges()` místo `Save(entity)`.** EF Core sleduje změny v celém grafu a při `SaveChanges` je vyhodnotí najednou. NHibernate má `Save`, `Update`, `Merge` jako samostatné operace nad konkrétní entitou. Rozdíl uvidíš i v tom, že tady nikde není explicitní transakce — `SaveChanges` si ji otevře sám.

**`Include` místo `FetchMany`.** Sémanticky totéž, jiné jméno.

**`ToQueryString()`.** Tohle NHibernate nemá a pro tvoji práci je to nejcennější věc v celém tutoriálu — vrátí SQL jako string, aniž by se dotaz spustil.

---

## Krok 8 — Spuštění

**Ctrl+F5**. V konzoli uvidíš:

1. **DDL** z `GenerateCreateScript()` — srovnání s výstupem NHibernate. Pozor: pro `BornOn` vygenerují oba `datetime2` — NHibernate používá pro dialekty SQL Server 2008+ `datetime2` od verze 5.0.0, takže rozdíl tady není. Rozdíly hledat u délek řetězců a u pojmenování FK constraintů.
2. **INSERT příkazy** s parametry (díky `EnableSensitiveDataLogging`). Všimni si, že EF Core je batchuje — všechny knihy jednoho autora jdou v jednom příkazu, případně přes `MERGE`.
3. **SQL dotazu** vypsané přes `ToQueryString()`, ještě než se spustí.
4. Výpis autorů s knihami.

---

## Krok 9 — Introspekce modelu (bonus, který NHibernate nemá)

Tohle si zapamatuj, protože pro analýzu implicitních defaultů je to nejpřímější nástroj, jaký máš. EF Core drží kompletní model jako dotazovatelnou strukturu, takže se ho můžeš zeptat, co si z tvého kódu odvodil:

```csharp
using (var context = new PlaygroundContext())
{
    foreach (var entityType in context.Model.GetEntityTypes())
    {
        Console.WriteLine($"\n{entityType.ClrType.Name}  ->  {entityType.GetTableName()}");

        foreach (var property in entityType.GetProperties())
        {
            var column = property.GetColumnName();
            var type = property.GetColumnType() ?? "(convention)";
            var nullable = property.IsNullable ? "NULL" : "NOT NULL";
            var generated = property.ValueGenerated.ToString();
            Console.WriteLine($"   {property.Name,-15} -> {column,-15} {type,-15} {nullable,-8} {generated}");
        }

        foreach (var key in entityType.GetKeys())
        {
            var cols = string.Join(", ", key.Properties.Select(p => p.Name));
            Console.WriteLine($"   KEY ({(key.IsPrimaryKey() ? "PK" : "alt")}): {cols}");
        }

        foreach (var fk in entityType.GetForeignKeys())
        {
            var cols = string.Join(", ", fk.Properties.Select(p => p.Name));
            Console.WriteLine($"   FK: {cols} -> {fk.PrincipalEntityType.ClrType.Name} [{fk.DeleteBehavior}]");
        }
    }
}
```

Zkus si potom zakomentovat kus `OnModelCreating` a spustit znovu. Rozdíl ve výstupu je přesně ta množina faktů, kterou EF Core doplnil sám — tedy to, co musí parser umět materializovat, aby IR neslo úplnou informaci.

---

## Typické chyby

| Hláška | Příčina |
|---|---|
| chyba o certifikátu při přihlášení | chybí `TrustServerCertificate=True` (stejné jako u NHibernate) |
| `The entity type 'X' requires a primary key to be defined` | property se nejmenuje `Id` ani `<Typ>Id` a chybí `HasKey` |
| `Unable to determine the relationship represented by navigation 'X'` | dvě navigace mezi stejnými typy, konvence si neví rady — dořeš `HasOne/WithMany` |
| `No value was provided for the required property` | `required` property bez hodnoty |
| `The database is not empty` při `EnsureCreated` | v databázi už jsou tabulky — `EnsureDeleted()` napřed |
| `Introducing FOREIGN KEY constraint ... may cause cycles` | více cascade cest do jedné tabulky, řeš přes `OnDelete(DeleteBehavior.Restrict)` |
| `SqlException: Invalid object name 'Authors'` | běžíš proti databázi vytvořené jiným modelem |

---

## Srovnání obou tutoriálů

První materiál do `docs/analysis/`:

| | NHibernate | EF Core |
|---|---|---|
| Balíčky | ORM + ADO.NET provider ručně | jen provider, zbytek tranzitivně |
| `virtual` na členech | povinné | nepotřebné (jen pro lazy proxies) |
| Bezparametrický ctor | povinný | nepovinný |
| Mapování bez konfigurace | nefunguje vůbec | funguje přes konvence |
| Vrstvy mapování | jedna (XML) | tři (konvence → annotations → fluent) |
| Umístění mapování | externí soubor, embedded resource | kód v assembly |
| Konfigurace vs. mapování | oddělené | v jedné třídě |
| FK jako property entity | ne (jen `<many-to-one>`) | volitelně obojí |
| Vytvoření databáze | ne, jen tabulky | ano (`EnsureCreated`) |
| Ukládání | `Save`/`Update`/`Merge` na entitu | `SaveChanges` nad grafem |
| Explicitní transakce | nutná | volitelná |
| Eager loading | `FetchMany` | `Include` |
| SQL bez připojení k DB | ne | `ToQueryString()` |
| DDL bez provedení | `SchemaExport(cfg).Create(action, false)` | `GenerateCreateScript()` |
| Introspekce modelu | omezená | `context.Model`, plně dotazovatelný |

---

## Kam pokračovat

Stejné body jako u NHibernate, aby vznikly srovnatelné dvojice:

**1. Kompozitní klíč.** `BookTranslation` s klíčem `(BookId, LanguageCode)`:

```csharp
entity.HasKey(t => new { t.BookId, t.LanguageCode });
```

Jeden řádek, žádná klíčová třída, žádný `Equals`/`GetHashCode`. Postav to vedle `<composite-id>` z NHibernate a máš první pořádně podloženou stránku analýzy. Od EF Core 7 existuje i atribut `[PrimaryKey(nameof(BookId), nameof(LanguageCode))]` — tedy další doklad té třívrstvosti.

**2. N:M.** `Book` ↔ `Category` přes `HasMany().WithMany()`. EF Core junction tabulku vytvoří sám a bez entity — stejně jako NHibernate `<many-to-many>` a opačně, než co generuje IR podle design docu 002.

**3. Dědičnost.** EF Core má TPH jako výchozí (`HasDiscriminator`), TPT přes `UseTptMappingStrategy()`, TPC přes `UseTpcMappingStrategy()`. Proti NHibernate `<subclass>` / `<joined-subclass>` / `<union-subclass>` je to skoro 1:1 mapování konceptů — dobrý kandidát na kapitolu, kde se ukáže, že IR může být jednotné.

**4. Migrations.** `EnsureCreated` je pro hřiště, ne pro reálný vývoj. Migrations jsou koncept, který NHibernate v jádru nemá vůbec, takže do analýzy patří spíš jako poznámka o rozsahu frameworku než jako řádek ve srovnávací tabulce.
