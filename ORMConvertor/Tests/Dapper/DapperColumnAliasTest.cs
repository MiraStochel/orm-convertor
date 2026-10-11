using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using Tests.Catalog;

namespace Tests.Dapper;

/// <summary>
/// The alias in the text of a Dapper call that maps its rows onto an entity is the source's
/// statement of the column of a property (decision 131). The domain is the one of the Dapper
/// tutorial: the property Id over the column AuthorId, which the catalog alone pairs by name
/// and therefore never paired.
/// </summary>
public class DapperColumnAliasTest
{
    private const string Repository = "repository";

    private const string Author = """
        namespace Library;

        public class Author
        {
            public int Id { get; set; }

            public int Code { get; set; }

            public string Name { get; set; } = string.Empty;

            public DateTime BornOn { get; set; }

            public List<Book> Books { get; set; } = [];
        }

        public class Book
        {
            public int BookId { get; set; }

            public int AuthorId { get; set; }
        }
        """;

    private const string Aliased = "SELECT a.AuthorId AS Id, a.Name, a.BornOn FROM Authors AS a";

    private static TableImage AuthorsImage() => new()
    {
        Schema = "dbo",
        Name = "Authors",
        Columns =
        [
            new ColumnImage { Name = "AuthorId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "BornOn", Type = DatabaseType.Date, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["AuthorId"],
        ForeignKeys = [],
    };

    private static ConversionResult Convert(ORMEnum target, string calls, ICatalogReader? catalog = null) =>
        ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                new() { ContentType = ConversionContentType.CSharp, Name = "entities", Content = Author },
                new()
                {
                    ContentType = ConversionContentType.CSharp,
                    Name = Repository,
                    Content = $$"""
                        public class AuthorRepository(IDbConnection connection)
                        {
                        {{calls}}
                        }
                        """,
                },
            ],
            catalog);

    private static string Call(string sql, string method = "Query<Author>") => $"    public object Load() => connection.{method}(\"{sql}\");";

    private static string AuthorClass(ConversionResult result) =>
        result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpEntity && s.Content.Contains("class Author")).Content;

    private static List<ConversionRecord> Records(ConversionResult result, ConversionRecordKind kind, string property) =>
        [.. result.Records.Where(r => r.Kind == kind && r.Entity == "Author" && r.Property == property)];

    /// <summary>
    /// The tutorial with a catalog: the alias pairs the property with its column, so the key
    /// the catalog knows of AuthorId lands on Id, generator and all - where the property used
    /// to have no column and the entity no key, and NHibernate refused it.
    /// </summary>
    [Fact]
    public void TheAliasPairsThePropertyWithTheKeyColumnOfTheCatalog()
    {
        var result = Convert(ORMEnum.NHibernate, Call(Aliased), new FakeCatalogReader(AuthorsImage()));

        var mapping = result.Sources.Single(s => s.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<id name=\"Id\" column=\"AuthorId\"", mapping);
        Assert.Contains("<generator class=\"identity\"", mapping);
        Assert.Empty(Records(result, ConversionRecordKind.Incompleteness, "Id"));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Entity == "Author");
    }

    /// <summary>
    /// Without a catalog the target carries the column all the same, and the translated query
    /// names the property over it, because the builders map a column back through the maps.
    /// </summary>
    [Fact]
    public void WithoutACatalogTheTargetCarriesTheColumnAndTheQueryNamesTheProperty()
    {
        var result = Convert(ORMEnum.EFCore, Call(Aliased));

        Assert.Contains("[Column(\"AuthorId\")]", AuthorClass(result));

        var query = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        Assert.Contains(".Id", query);
        Assert.DoesNotContain("AuthorId", query);
    }

    /// <summary>
    /// The statement is the source's, so the ledger gives it to the unit that holds the call, as
    /// a fact the source stated rather than one the catalog supplied.
    /// </summary>
    [Fact]
    public void TheFactBelongsToTheUnitWithTheCall()
    {
        var result = Convert(ORMEnum.EFCore, Call(Aliased), new FakeCatalogReader(AuthorsImage()));

        var fact = Assert.Single(result.Facts, f => f.Entity == "Author" && f.Property == "Id" && f.Category == MappingFactCategory.ColumnName);
        Assert.Equal(Repository, fact.Unit);
        Assert.Equal(FactOrigin.Source, fact.Origin);
        Assert.Equal("AuthorId", fact.Value);
    }

    /// <summary>
    /// Every way a call states the mapping: the type argument of any method that maps rows onto
    /// one type, a Type ahead of the SQL, a qualified type, an unqualified column of the one
    /// table, the T-SQL form of the alias, and a name Dapper matches regardless of case.
    /// </summary>
    [Theory]
    [InlineData("SELECT a.AuthorId AS Id FROM Authors AS a", "QueryFirstOrDefault<Author>")]
    [InlineData("SELECT a.AuthorId AS Id FROM Authors AS a", "QuerySingleAsync<Author>")]
    [InlineData("SELECT AuthorId AS Id FROM dbo.Authors", "Query<Author>")]
    [InlineData("SELECT Id = AuthorId FROM Authors", "Query<Author>")]
    [InlineData("SELECT Authors.AuthorId AS id FROM Authors", "Query<Library.Author>")]
    [InlineData("SELECT a.AuthorId AS Id FROM Author AS a INNER JOIN Books AS b ON b.AuthorId = a.AuthorId", "Query<Author>")]
    public void EveryCallThatMapsRowsOntoTheEntityStatesTheColumn(string sql, string method)
    {
        var result = Convert(ORMEnum.EFCore, Call(sql, method));

        Assert.Contains("[Column(\"AuthorId\")]\n    public required int Id", AuthorClass(result).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ATypeAheadOfTheSqlNamesTheEntity()
    {
        var result = Convert(ORMEnum.EFCore, "    public object Load() => connection.Query(typeof(Author), \"SELECT AuthorId AS Id FROM Authors\");");

        Assert.Contains("[Column(\"AuthorId\")]", AuthorClass(result));
    }

    /// <summary>
    /// What states nothing about a column of the entity (decision 131): a text no call maps
    /// onto it, a column of another table or of no table, an expression, the column's own name,
    /// a name the projection gives twice, a name that is no scalar property, and a call whose
    /// text Dapper does not send as a query.
    /// </summary>
    [Theory]
    [InlineData("connection.Query<Book>(\"SELECT a.AuthorId AS Id FROM Authors AS a\")")]
    [InlineData("connection.Query<int>(\"SELECT a.AuthorId AS Id FROM Authors AS a\")")]
    [InlineData("connection.ExecuteScalar<Author>(\"SELECT a.AuthorId AS Id FROM Authors AS a\")")]
    [InlineData("connection.Query<Author>(\"SELECT b.BookId AS Id FROM Authors AS a INNER JOIN Books AS b ON b.AuthorId = a.AuthorId\")")]
    [InlineData("connection.Query<Author>(\"SELECT BookId AS Id FROM Authors AS a INNER JOIN Books AS b ON b.AuthorId = a.AuthorId\")")]
    [InlineData("connection.Query<Author>(\"SELECT t.AuthorId AS Id FROM tblAuthor AS t\")")]
    [InlineData("connection.Query<Author>(\"SELECT AuthorId AS Id FROM Authors UNION SELECT AuthorId FROM Authors\")")]
    [InlineData("connection.Query<Author>(\"WITH Authors AS (SELECT 1 AS AuthorId) SELECT AuthorId AS Id FROM Authors\")")]
    [InlineData("connection.Query<Author>(\"SELECT d.AuthorId AS Id FROM (SELECT AuthorId FROM Authors) AS d\")")]
    [InlineData("connection.Query<Author>(\"SELECT a.AuthorId + 0 AS Id FROM Authors AS a\")")]
    [InlineData("connection.Query<Author>(\"SELECT a.name AS Name FROM Authors AS a\")")]
    [InlineData("connection.Query<Author>(\"SELECT a.AuthorId AS Id, a.Name AS Id FROM Authors AS a\")")]
    [InlineData("connection.Query<Author>(\"SELECT a.AuthorId AS Books FROM Authors AS a\")")]
    [InlineData("connection.Query<Author>(\"SELECT a.AuthorId AS AuthorKey FROM Authors AS a\")")]
    [InlineData("connection.Query<Author>(\"SELECT a.AuthorId AS Id FROM Authors AS a\", commandType: CommandType.StoredProcedure)")]
    [InlineData("connection.Query<Author, Book, Author>(\"SELECT a.AuthorId AS Id, b.BookId FROM Authors AS a INNER JOIN Books AS b ON b.AuthorId = a.AuthorId\", (a, b) => a, splitOn: \"BookId\")")]
    [InlineData("connection.QueryMultiple(\"SELECT a.AuthorId AS Id FROM Authors AS a\")")]
    public void WhatStatesNoColumnOfTheEntityStatesNothing(string call)
    {
        var result = Convert(ORMEnum.EFCore, $"    public object Load() => {call};");

        Assert.DoesNotContain("[Column(", AuthorClass(result));
    }

    /// <summary>
    /// Dapper's other way to rename a column - a type map registered in code, or the switch
    /// that drops underscores for every type - is not read, and says so (decision 048).
    /// </summary>
    [Fact]
    public void ATypeMapRegisteredInCodeIsALoss()
    {
        var result = Convert(
            ORMEnum.EFCore,
            Call(Aliased) + "\n"
                + "    public static void Map() => SqlMapper.SetTypeMap(typeof(Author), new CustomPropertyTypeMap(typeof(Author), "
                + "(type, column) => type.GetProperty(column == \"AuthorId\" ? \"Id\" : column)!));\n"
                + "    public static void Underscores() => DefaultTypeMap.MatchNamesWithUnderscores = true;");

        var losses = result.Records
            .Where(r => r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.ColumnName && r.Unit == Repository)
            .ToList();

        Assert.Equal(2, losses.Count);
        Assert.Contains(losses, loss => loss.Entity == "Author" && loss.Reason.Contains("SetTypeMap"));
        Assert.Contains(losses, loss => loss.Entity is null && loss.Reason.Contains("MatchNamesWithUnderscores"));
    }

    [Fact]
    public void ABareSqlUnitMapsOntoNoEntity()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.EFCore,
            [
                new() { ContentType = ConversionContentType.CSharp, Content = Author },
                new() { ContentType = ConversionContentType.SqlQuery, Content = Aliased },
            ]);

        Assert.DoesNotContain("[Column(", AuthorClass(result));
    }

    /// <summary>
    /// Two columns for one property is the conflict of decision 017: the first call read keeps
    /// the property, and the record names the column the later one stated.
    /// </summary>
    [Fact]
    public void TwoColumnsForOnePropertyAreAConflictAndTheFirstStays()
    {
        var result = Convert(
            ORMEnum.EFCore,
            Call("SELECT a.AuthorId AS Id FROM Authors AS a") + "\n" + Call("SELECT a.LegacyId AS Id FROM Authors AS a").Replace("Load()", "Legacy()"));

        Assert.Contains("[Column(\"AuthorId\")]", AuthorClass(result));
        Assert.DoesNotContain("LegacyId", AuthorClass(result));

        var conflict = Assert.Single(Records(result, ConversionRecordKind.Conflict, "Id"));
        Assert.Equal(MappingFactCategory.ColumnName, conflict.Category);
        Assert.Contains("LegacyId", conflict.Reason);
    }

    /// <summary>
    /// One column under two names is two statements about two properties and contradicts
    /// nothing: both are written, and whether a target takes two properties over one column is
    /// its builder's to say.
    /// </summary>
    [Fact]
    public void OneColumnUnderTwoNamesIsNoConflict()
    {
        var result = Convert(
            ORMEnum.EFCore,
            Call("SELECT a.AuthorId AS Id FROM Authors AS a") + "\n" + Call("SELECT a.AuthorId AS Code FROM Authors AS a").Replace("Load()", "Codes()"));

        var entity = AuthorClass(result).ReplaceLineEndings("\n");

        Assert.Contains("[Column(\"AuthorId\")]\n    public required int Id", entity);
        Assert.Contains("[Column(\"AuthorId\")]\n    public required int Code", entity);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }
}
