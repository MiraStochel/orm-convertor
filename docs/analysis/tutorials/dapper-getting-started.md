# Dapper od nuly — Visual Studio 2026, .NET 10, MS SQL Server

Třetí díl. Stejná doména (`Author` 1:N `Book`), stejný SQL Server, stejné číslování kroků jako u NHibernate a EF Core.

Ověřené verze: Dapper **2.1.79**, Microsoft.Data.SqlClient **7.0.2**, .NET **10**, MS SQL Server.

---

## Než začneš: co Dapper vlastně je

NHibernate i EF Core si drží **model** — vnitřní reprezentaci toho, jaké máš entity, jaké mají klíče, jak se mapují na tabulky a jak spolu souvisejí. Z toho modelu pak generují SQL, generují DDL, hlídají identitu objektů a sledují změny.

Dapper žádný model nemá. Je to sada rozšiřujících metod nad `IDbConnection`, které umí dvě věci: navázat parametry na SQL příkaz a namapovat sloupce výsledku na vlastnosti objektu. Nic víc. Neví, že `Authors` je tabulka. Neví, že `AuthorId` je klíč. Neví, že mezi `Author` a `Book` je nějaký vztah.

Praktický důsledek: **v Dapperu neexistuje místo, kde by mapování bylo deklarované.** U NHibernate to je `hbm.xml`, u EF Core `OnModelCreating` plus konvence. U Dapperu je mapování rozptýlené v SQL řetězcích napříč kódem. Tohle je hlavní důvod, proč je jakýkoli převodní cyklus přes Dapper ztrátový — a proč `BuildPrimaryKey` v Dapper builderu nemá co vygenerovat.

Za to dostáváš úplnou kontrolu nad SQL, nulovou abstrakci mezi sebou a databází a nejmenší režii ze všech tří.

---

## Krok 0 — Prázdná databáze **a schéma**

Ze všech tří tutoriálů je tenhle krok tady nejpracnější. Dapper nemá `SchemaExport` ani `EnsureCreated` — DDL musíš napsat sám.

Databázi založ stejně jako u NHibernate (**SQL Server Object Explorer** → **Add New Database**), pojmenuj ji `OrmPlayground_Dapper`:

```sql
CREATE DATABASE OrmPlayground_Dapper;
```

Tabulky vytvoří kód v kroku 7, ale DDL si projdi už teď — je to poprvé, co schéma navrhuješ ty a ne framework:

```sql
CREATE TABLE Authors (
    AuthorId      INT            IDENTITY(1,1) NOT NULL,
    Name          NVARCHAR(200)  NOT NULL,
    BornOn        DATETIME2      NULL,
    CONSTRAINT PK_Authors PRIMARY KEY (AuthorId)
);

CREATE TABLE Books (
    BookId        INT            IDENTITY(1,1) NOT NULL,
    Title         NVARCHAR(300)  NOT NULL,
    PublishedYear INT            NOT NULL,
    Price         DECIMAL(18,2)  NOT NULL,
    AuthorId      INT            NOT NULL,
    CONSTRAINT PK_Books PRIMARY KEY (BookId),
    CONSTRAINT FK_Books_Authors FOREIGN KEY (AuthorId)
        REFERENCES Authors (AuthorId) ON DELETE CASCADE
);
```

Porovnej to s DDL, které ti vygeneroval NHibernate a EF Core. Rozdíly v pojmenování constraintů a v typech (`DATETIME2` vs. co zvolily frameworky) jsou první konkrétní data pro kapitolu o mapování typů.

---

## Krok 1 — Založení projektu

Do solution `OrmPlayground` přidej třetí projekt:

1. Pravým na solution → **Add → New Project**
2. Šablona **Console App** (C#)
3. Project name: `OrmPlayground.Dapper`
4. Framework: **.NET 10.0**

---

## Krok 2 — NuGet balíčky

| Balíček | Proč |
|---|---|
| `Dapper` | mapování výsledků a parametrů, verze 2.1.79 nebo novější |
| `Microsoft.Data.SqlClient` | ADO.NET provider — tady ho referencuješ přímo, protože `SqlConnection` používáš v kódu |

Dva balíčky, stejně jako u NHibernate, ale z jiného důvodu: u NHibernate byl provider potřeba kvůli reflexi driveru, tady ho instancuješ ručně.

---

## Krok 3 — Doménové třídy

Vytvoř složku `Domain`.

**`Domain/Author.cs`**

```csharp
namespace OrmPlayground.Dapper.Domain;

public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? BornOn { get; set; }
    public List<Book> Books { get; set; } = [];
}
```

**`Domain/Book.cs`**

```csharp
namespace OrmPlayground.Dapper.Domain;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PublishedYear { get; set; }
    public decimal Price { get; set; }
    public int AuthorId { get; set; }
}
```

### Čeho si všimnout

**Žádné požadavky.** Ani `virtual`, ani bezparametrický konstruktor, ani atributy, ani `DbSet`. Doslova jakékoli POCO. V tvých benchmarcích to je vidět elegantně: `DapperEntities.csproj` nemá **žádný** `PackageReference` — entity ani nevědí, že Dapper existuje.

**Chybí `Book.Author`.** Zpětná navigace by nikam nevedla, protože Dapper vztahy nezná a nikdy by ji nenaplnil. Nechal jsem jen `AuthorId`, tedy holý FK sloupec.

**`Author.Books` je jen obyčejná kolekce.** Není to navigační vlastnost v žádném smyslu — Dapper ji nikdy nenaplní sám. Naplníš ji ty, ručně, v kroku 9.

---

## Krok 4 — Mapování

**Neexistuje jako samostatný artefakt.** Jediné pravidlo, které Dapper uplatňuje, je: *název sloupce ve výsledku = název vlastnosti* (case-insensitive, podtržítka se ignorují).

Když se neshodují — a v naší doméně se neshodují, protože property je `Id`, ale sloupec `AuthorId` — máš dvě možnosti.

**Alias v SQL**, což je idiomatická cesta:

```sql
SELECT AuthorId AS Id, Name, BornOn FROM Authors
```

**Globální type map**, jak to řešíš v benchmarcích pro `PurchaseOrderUpdate`:

```csharp
SqlMapper.SetTypeMap(
    typeof(Author),
    new CustomPropertyTypeMap(
        typeof(Author),
        (type, columnName) => type.GetProperty(columnName == "AuthorId" ? "Id" : columnName)!));
```

Zastav se u toho na chvíli, protože je to jádro věci pro tvoji analýzu. U EF Core je totéž jeden řádek `HasColumnName("AuthorId")` na jednom místě. U NHibernate `column="AuthorId"` v mapovacím souboru. U Dapperu buď **opakuješ alias v každém dotazu**, nebo saháš po globální reflexní registraci, která žije úplně jinde než dotazy, kterých se týká.

To znamená, že parser pro Dapper nemá odkud číst mapování jinak než z SQL řetězců — a co v SQL řetězci není, to prostě neexistuje.

Tenhle tutoriál používá alias, protože to je běžnější a je vidět přímo v dotazu.

---

## Krok 5 — Embedded Resource

**Odpadá.** Není co načítat.

---

## Krok 6 — Konfigurace

**Zredukovala se na connection string.** Žádná session factory, žádný `DbContext`, žádný dialekt, žádný driver. Vytvoř složku `Persistence`.

**`Persistence/Db.cs`**

```csharp
using Microsoft.Data.SqlClient;

namespace OrmPlayground.Dapper.Persistence;

public static class Db
{
    // Uprav podle svojí instance ze SQL Server Object Exploreru.
    private const string ConnectionString =
        "Server=localhost;Database=OrmPlayground_Dapper;Trusted_Connection=True;TrustServerCertificate=True;";

    public static SqlConnection Open() => new(ConnectionString);
}
```

`TrustServerCertificate=True` platí i tady — je to vlastnost `Microsoft.Data.SqlClient`, ne ORM.

Za povšimnutí stojí, co v konfiguraci **není**: dialekt. NHibernate i EF Core potřebují vědět, proti jaké databázi generují SQL. Dapper nic negeneruje, takže se ptát nemusí. Přenositelnost mezi databázemi je tím pádem plně na tobě.

Logování SQL taky nikde není. Nepotřebuješ ho — SQL máš doslova v kódu.

---

## Krok 7 — Vytvoření schématu a první data

**`Program.cs`**

```csharp
using Dapper;
using OrmPlayground.Dapper.Domain;
using OrmPlayground.Dapper.Persistence;

using var connection = Db.Open();

// --- Schema ---
connection.Execute("""
    DROP TABLE IF EXISTS Books;
    DROP TABLE IF EXISTS Authors;

    CREATE TABLE Authors (
        AuthorId      INT            IDENTITY(1,1) NOT NULL,
        Name          NVARCHAR(200)  NOT NULL,
        BornOn        DATETIME2      NULL,
        CONSTRAINT PK_Authors PRIMARY KEY (AuthorId)
    );

    CREATE TABLE Books (
        BookId        INT            IDENTITY(1,1) NOT NULL,
        Title         NVARCHAR(300)  NOT NULL,
        PublishedYear INT            NOT NULL,
        Price         DECIMAL(18,2)  NOT NULL,
        AuthorId      INT            NOT NULL,
        CONSTRAINT PK_Books PRIMARY KEY (BookId),
        CONSTRAINT FK_Books_Authors FOREIGN KEY (AuthorId)
            REFERENCES Authors (AuthorId) ON DELETE CASCADE
    );
    """);

// --- Zapis ---
var capek = new Author { Name = "Karel Capek", BornOn = new DateTime(1890, 1, 9) };
var orwell = new Author { Name = "George Orwell", BornOn = new DateTime(1903, 6, 25) };

foreach (var author in new[] { capek, orwell })
{
    author.Id = connection.QuerySingle<int>("""
        INSERT INTO Authors (Name, BornOn) VALUES (@Name, @BornOn);
        SELECT CAST(SCOPE_IDENTITY() AS INT);
        """,
        new { author.Name, author.BornOn });
}

var books = new[]
{
    new Book { Title = "R.U.R.", PublishedYear = 1920, Price = 249m, AuthorId = capek.Id },
    new Book { Title = "War with the Newts", PublishedYear = 1936, Price = 329m, AuthorId = capek.Id },
    new Book { Title = "1984", PublishedYear = 1949, Price = 299m, AuthorId = orwell.Id },
};

connection.Execute("""
    INSERT INTO Books (Title, PublishedYear, Price, AuthorId)
    VALUES (@Title, @PublishedYear, @Price, @AuthorId);
    """,
    books);
```

Čtyři věci, které jinde nebyly:

**Klíč si musíš vyzvednout sám.** `SCOPE_IDENTITY()` a přiřazení do `author.Id`. NHibernate i EF Core to udělají za tebe, protože vědí, který sloupec je klíč a že je generovaný. Dapper to neví.

**Pořadí zápisu řešíš ty.** Autoři před knihami, protože FK. Ostatní dva frameworky si pořadí odvodí ze vztahů v modelu.

**FK naplňuješ ručně** přes `AuthorId = capek.Id`. Nikde není nic jako `AddBook`.

**Žádná transakce a žádný change tracking.** Každé volání jde rovnou do databáze. Když chceš transakci, otevřeš ji explicitně přes `connection.BeginTransaction()` a předáš ji každému volání.

Zato poslední `Execute` s polem `books` udělá to, co bys čekal — Dapper příkaz zopakuje pro každý prvek.

---

## Krok 8 — Spuštění

**Ctrl+F5**. Konzole zůstane prázdná — a to je samo o sobě zjištění. Není co logovat, protože nic negeneruje. SQL, které jde do databáze, je přesně to, co máš v uvozovkách.

Ověř si data v **SQL Server Object Exploreru**, nebo pokračuj krokem 9.

---

## Krok 9 — Relace (multi-mapping)

Tohle je protějšek `Include` z EF Core a `FetchMany` z NHibernate. Připoj na konec `Program.cs`:

```csharp
Console.WriteLine("\n--- Query: authors with a book published before 1930 ---\n");

const string sql = """
    SELECT a.AuthorId AS Id, a.Name, a.BornOn,
           b.BookId AS Id, b.Title, b.PublishedYear, b.Price, b.AuthorId
    FROM Authors a
    LEFT JOIN Books b ON b.AuthorId = a.AuthorId
    WHERE EXISTS (
        SELECT 1 FROM Books x
        WHERE x.AuthorId = a.AuthorId AND x.PublishedYear < 1930
    )
    ORDER BY a.AuthorId, b.PublishedYear;
    """;

var authorsById = new Dictionary<int, Author>();

connection.Query<Author, Book, Author>(
    sql,
    (author, book) =>
    {
        if (!authorsById.TryGetValue(author.Id, out var existing))
        {
            existing = author;
            authorsById.Add(existing.Id, existing);
        }

        if (book is not null)
        {
            existing.Books.Add(book);
        }

        return existing;
    },
    splitOn: "Id");

foreach (var author in authorsById.Values)
{
    Console.WriteLine($"{author.Name} ({author.Books.Count} books)");
    foreach (var book in author.Books)
    {
        Console.WriteLine($"  {book.PublishedYear}  {book.Title,-25} {book.Price,8:N2}");
    }
}
```

**Jak funguje `splitOn`.** Dapper dostane jeden plochý řádek a musí ho rozříznout na dva objekty. `splitOn: "Id"` znamená „druhý objekt začíná u dalšího sloupce jménem `Id`". Proto je pořadí sloupců v `SELECT` významné a proto tam jsou aliasy. Když se řez netrefí, dostaneš buď výjimku o chybějícím konstruktoru, nebo — hůř — tiše špatně naplněné objekty.

**Deduplikaci děláš ty.** JOIN vrátí autora tolikrát, kolik má knih. Dictionary zajistí, že vznikne jedna instance. Tohle je ten kód, u kterého máš v benchmarcích komentář, že Dapper nic takového neumí automaticky.

**Identita objektů neexistuje.** Kdybys spustil dotaz dvakrát, dostaneš dvě různé instance téhož autora. NHibernate i EF Core mají identity map v rámci session a vrátí tutéž instanci.

---

## Co v Dapperu nelze vyjádřit

Tohle je pro tvoji práci nejdůležitější sekce v celém dokumentu. Následující fakta nemají v Dapperu **žádnou** deklarativní reprezentaci — existují nanejvýš v DDL, které je mimo dosah kódu:

| Fakt | Kde žije místo toho |
|---|---|
| Primární klíč | v DDL; kód o něm ví jen tím, že píše `SCOPE_IDENTITY()` |
| Kompozitní klíč | v DDL a ve `WHERE` klauzulích, které ho ručně skládají |
| Generování klíče (IDENTITY) | v DDL |
| Relace 1:N, N:1 | v `JOIN` klauzulích jednotlivých dotazů |
| Relace N:M | v `JOIN` přes junction tabulku, ručně |
| Kaskádové mazání | v DDL |
| Nullabilita, délka, přesnost | v DDL |
| Mapování sloupec ↔ vlastnost | v `AS` aliasech, rozptýleně |
| Identita entity | nikde |
| Dědičnost | nikde |

Když tohle položíš vedle tabulek z předchozích dvou tutoriálů, máš přesně tu matici „nelze vyjádřit", o které jsme mluvili — a zároveň seznam případů, pro které musí Dapper builder podle rozhodnutí [004](../../decisions/004-unexpressible-facts-as-warnings.md) emitovat strukturovaná varování. Každý řádek téhle tabulky je jedno varování.

Stojí za zmínku, že to není chyba návrhu Dapperu. Dapper záměrně nespravuje schéma; předpokládá, že databáze existuje a někdo jiný ji spravuje. Ztrátovost převodu je důsledek téhle volby, ne nedostatku.

---

## Typické chyby

| Hláška / projev | Příčina |
|---|---|
| vlastnosti zůstanou na výchozích hodnotách | nesedí název sloupce a vlastnosti — chybí alias |
| `When using the multi-mapping APIs ensure you set the splitOn param` | Dapper nenašel dělicí sloupec; zkontroluj pořadí sloupců a hodnotu `splitOn` |
| druhý objekt má vyplněné vlastnosti prvního | řez `splitOn` padl na špatné místo |
| `Must declare the scalar variable '@X'` | parametr v SQL nemá protějšek v anonymním objektu |
| autor se opakuje v kolekci | chybí deduplikace přes dictionary |
| chyba o certifikátu při přihlášení | `TrustServerCertificate=True` |
| `Invalid column name` | ručně psané SQL nesedí se schématem — nikdo to nekontroluje |

Poslední řádek je vlastnost, ne chyba: v Dapperu jsou SQL řetězce mimo dosah kompilátoru. Přejmenování vlastnosti v C# ti nerozbije build, rozbije ti běh.

---

## Srovnání všech tří

| | NHibernate | EF Core | Dapper |
|---|---|---|---|
| Balíčky | 2 | 1 | 2 |
| Požadavky na entity | `virtual`, bezparam. ctor | žádné | žádné |
| Mapování | `hbm.xml` | konvence + annotations + fluent | aliasy v SQL |
| Deklarativní místo pro mapování | ano, jedno | ano, jedno | **není** |
| Vytvoření databáze | ne | ano | ne |
| Vytvoření tabulek | `SchemaExport` | `EnsureCreated` | ručně psané DDL |
| Znalost primárního klíče | ano | ano | **ne** |
| Znalost relací | ano | ano | **ne** |
| Dialekt v konfiguraci | nutný | nutný | žádný |
| Psaní SQL | generované | generované | ruční |
| Change tracking | ano | ano | ne |
| Identity map | ano (session) | ano (context) | ne |
| Eager loading | `FetchMany` | `Include` | ruční JOIN + dictionary |
| SQL bez připojení k DB | ne | `ToQueryString()` | triviálně (je v kódu) |
| Introspekce modelu | omezená | `context.Model` | není co introspektovat |
| Kontrola nad SQL | nepřímá | nepřímá | úplná |

---

## Kam pokračovat

**1. Kompozitní klíč.** Přidej `BookTranslation` s klíčem `(BookId, LanguageCode)`. Pro Dapper to bude znamenat jen to, že v `WHERE` píšeš dvě podmínky místo jedné — na úrovni kódu se nezmění vůbec nic. Ten nulový rozdíl je ale právě ten výsledek: srovnej ho s `<composite-id>` a s `HasKey(t => new { ... })`.

**2. N:M.** `Book` ↔ `Category`. Znamená to JOIN přes junction tabulku a dictionary. Zajímavé je, že tady se Dapper naopak shoduje s IR podle rozhodnutí [005](../../decisions/005-many-to-many-as-explicit-junction-entity.md) — junction tabulka je v SQL viditelná explicitně, protože jinak než explicitně o ní mluvit nejde.

**3. Znovu si přečti `DapperFeatures/FeatureTests.cs` v benchmarcích.** Po tomhle tutoriálu ti bude dávat mnohem větší smysl, hlavně testy `A3_MultipleEntitiesFromOneResult` a `A4_StoredProcedureToEntity`.

**4. Sepiš první tematickou stránku analýzy.** Máš teď tři implementace identické domény. Než se pustíš do dalších frameworků, zkus na tomhle materiálu napsat sekci o mapování sloupců podle tří os (expresivita, syntax, implicitní defaulty) — zjistíš, jestli ti ta struktura sedí, dokud je levné ji změnit.
