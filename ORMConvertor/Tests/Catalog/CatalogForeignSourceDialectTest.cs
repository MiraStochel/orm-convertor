using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Catalog;

/// <summary>
/// Decision 091 over a fake catalog: a source that declared another database system is
/// completed from the catalog all the same - the catalog reader is SQL Server and the tool
/// has no other - and the run says once that the facts came from a system the source
/// disowned.
///
/// What is worth testing is not the condition but the three things around it. That the
/// completion still happens, because refusing it would take the conversion exactly what the
/// catalog is connected for (F6) and take it from the input that needs it most, the one
/// decision 088 already stripped of its literal column types. That the record is one per run
/// and an increment - the same input with no declaration, and with the dialect this version
/// does read, comes out with the very same records it did before. And that it is a finding
/// about this run rather than about the arrangement: a catalog that put nothing into the
/// mapping says nothing, and a catalog that only lost to the source says it anyway, because
/// that is the run in which a disagreement throughout most wants explaining.
/// </summary>
public class CatalogForeignSourceDialectTest
{
    private const string CustomerSource = """
        namespace DapperEntities;

        public class Customer
        {
            public int CustomerId { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? Notes { get; set; }
        }
        """;

    private static TableImage CustomersImage() => new()
    {
        Schema = "sales",
        Name = "Customers",
        Columns =
        [
            new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "Notes", Type = DatabaseType.VarChar, IsUnicode = true, Length = 400, IsNullable = true, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["CustomerId"],
        ForeignKeys = [],
    };

    private static NHibernateEntityBuilder ParseCustomer()
    {
        var builder = new NHibernateEntityBuilder();
        new DapperEntityParser(builder).Parse(CustomerSource);
        return builder;
    }

    /* ---- the completion still happens -------------------------------------- */

    /// <summary>
    /// The heart of the choice. A fact read from a live schema is exact - the doubt is which
    /// schema it came from, which is a different doubt from the one decision 088 refused to
    /// guess at - so the mapping comes out of the phase richer, not poorer, and the caveat
    /// rides beside the facts instead of replacing them.
    /// </summary>
    [Fact]
    public void AForeignSourceIsCompletedAllTheSame()
    {
        var builder = ParseCustomer();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()), SourceSqlDialect.AnotherSystem);

        var em = builder.EntityMaps.Single();
        Assert.Equal("Customers", em.Table);
        Assert.Equal("sales", em.Schema);
        Assert.Equal(100, em.PropertyMaps.Single(pm => pm.Property.Name == "Name").Length);
        Assert.Equal("CustomerId", Assert.Single(em.PrimaryKey!.Parts).PropertyMap.Property.Name);
    }

    /// <summary>
    /// One record for the run, and it names both halves of the disagreement: what the source
    /// declared and what was read from. Without an entity, without a property and without a
    /// category, because the doubt is no property of one fact (decision 048) - it covers
    /// every fact the phase reported, which is why it is not repeated at each of them.
    /// </summary>
    [Fact]
    public void OneRecordPerRunNamesTheDisagreement()
    {
        var builder = ParseCustomer();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()), SourceSqlDialect.AnotherSystem);

        var record = Assert.Single(ForeignCatalogRecords(builder));

        Assert.Equal(ConversionRecordKind.Conflict, record.Kind);
        Assert.Null(record.Category);
        Assert.Null(record.Entity);
        Assert.Null(record.Property);
        Assert.Contains("another database system", record.Reason);
        Assert.Contains("SQL Server catalog", record.Reason);

        // Beside facts it did supply, so the caveat qualifies something rather than standing
        // alone.
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Supplied);
    }

    /* ---- the field is an increment ----------------------------------------- */

    /// <summary>
    /// The test that holds the decision to being an increment. No declaration and the
    /// declaration of the dialect this version does read must give the very same records the
    /// phase gave before the field reached it, and the foreign declaration must give those
    /// same records plus exactly one. A record that moved would be a changed output as much
    /// as a supplied fact that did.
    /// </summary>
    [Fact]
    public void OnlyTheForeignDeclarationAddsAnything()
    {
        var undeclared = Reasons(CompleteCustomer(dialect: null));
        var ownDialect = Reasons(CompleteCustomer(SourceSqlDialect.SqlServer2022));
        var foreign = Reasons(CompleteCustomer(SourceSqlDialect.AnotherSystem));

        Assert.Equal(undeclared, ownDialect);
        Assert.Equal(undeclared, foreign[..^1]);
        Assert.Equal(undeclared.Count + 1, foreign.Count);
    }

    /* ---- a finding about the run, not about the arrangement ---------------- */

    /// <summary>
    /// A catalog that answered nothing put nothing of SQL Server's into the mapping, so
    /// there is nothing to warn about: a record there would describe the arrangement instead
    /// of this run (decision 028). The entity is reported as uncompletable, as it would be
    /// without any declaration at all.
    /// </summary>
    [Fact]
    public void ACatalogThatSuppliedNothingSaysNothing()
    {
        var builder = ParseCustomer();

        var unrelated = new TableImage
        {
            Schema = "dbo",
            Name = "Invoices",
            Columns = [new ColumnImage { Name = "InvoiceId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true }],
            PrimaryKeyColumns = ["InvoiceId"],
            ForeignKeys = [],
        };

        CatalogCompletion.Complete(builder, new FakeCatalogReader(unrelated), SourceSqlDialect.AnotherSystem);

        Assert.Empty(ForeignCatalogRecords(builder));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
    }

    /// <summary>
    /// A target that cannot express a single mapping fact asks the catalog nothing (Dapper,
    /// decision 015), so the connection is never even tried and the same silence follows -
    /// this time without the phase having read anything at all.
    /// </summary>
    [Fact]
    public void AnEmptyDemandAsksNothingAndSaysNothing()
    {
        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse(CustomerSource);

        var reader = new FakeCatalogReader(CustomersImage());
        var result = CatalogCompletion.Complete(builder, reader, SourceSqlDialect.AnotherSystem);

        Assert.Equal(CatalogConnectionState.Unused, result.ConnectionState);
        Assert.Equal(0, reader.Reads);
        Assert.Empty(ForeignCatalogRecords(builder));
    }

    /// <summary>
    /// A catalog fact that lost to the source met the mapping as much as one that was
    /// written, so it counts: the source outranks it (rule E9, decision 015) and the run
    /// still says where the losing claim came from. This is the run where the caveat is
    /// worth most - it explains why a catalog disagrees with the source throughout.
    /// </summary>
    [Fact]
    public void ACatalogThatOnlyLostToTheSourceIsStillSpokenOf()
    {
        var builder = CompleteFullyStatedEntity(SourceSqlDialect.AnotherSystem);

        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.Nullability);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Supplied);

        Assert.Single(ForeignCatalogRecords(builder));

        // And the same entity without the declaration says nothing, so the record is the
        // declaration's and not the conflict's.
        Assert.Empty(ForeignCatalogRecords(CompleteFullyStatedEntity(dialect: null)));
    }

    /* ---- helpers ----------------------------------------------------------- */

    private static AbstractEntityBuilder CompleteCustomer(SourceSqlDialect? dialect)
    {
        var builder = ParseCustomer();
        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()), dialect);
        return builder;
    }

    /// <summary>
    /// An entity that states every fact the catalog could supply, so the phase has nothing
    /// left to write and its one disagreement - the source calls the column nullable, the
    /// schema does not - is the only fact of the catalog that meets the mapping. The key
    /// column is deliberately neither IDENTITY nor free of a default, so its strategy stays
    /// unspecified with a record of incompleteness rather than a supplied one (decision 064).
    /// </summary>
    private static AbstractEntityBuilder CompleteFullyStatedEntity(SourceSqlDialect? dialect)
    {
        var builder = new NHibernateEntityBuilder();

        new EFCoreEntityParser(builder).Parse("""
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace EFCoreEntities;

            [Table("Customers", Schema = "sales")]
            public class Customer
            {
                [Key]
                [Column(TypeName = "int")]
                public int CustomerId { get; set; }

                [Column(TypeName = "nvarchar(100)")]
                public string? Name { get; set; }
            }
            """);

        var image = new TableImage
        {
            Schema = "sales",
            Name = "Customers",
            Columns =
            [
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false, HasDefault = true },
                new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["CustomerId"],
            ForeignKeys = [],
        };

        CatalogCompletion.Complete(builder, new FakeCatalogReader(image), dialect);

        return builder;
    }

    /// <summary>
    /// The record of decision 091, matched on its reason rather than on its kind: a conflict
    /// made for some other cause must not be mistaken for it.
    /// </summary>
    private static List<ConversionRecord> ForeignCatalogRecords(AbstractEntityBuilder builder)
        => [.. builder.Records.Where(r =>
            r.Kind == ConversionRecordKind.Conflict
            && r.Reason.Contains("completed from a SQL Server catalog"))];

    private static List<string> Reasons(AbstractEntityBuilder builder)
        => [.. builder.Records.Select(r => r.Reason)];
}
