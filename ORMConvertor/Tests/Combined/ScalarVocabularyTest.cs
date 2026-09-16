using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using NHibernateWrappers;
using NHibernateWrappers.Convertors;

namespace Tests.Combined;

/// <summary>
/// The five scalars decision 071 added to the closed list: they read from C# and write
/// back under their own names in every .NET target, the NHibernate type name follows the
/// pair of column family and property scalar, every family of the database vocabulary
/// infers a language scalar, and the query visitors write the temporal literals the way
/// each target reads them.
/// </summary>
public class ScalarVocabularyTest
{
    private const string Source = """
        public class Event
        {
            [Key]
            public int EventId { get; set; }

            public DateOnly Day { get; set; }

            public TimeOnly? StartsAt { get; set; }

            public DateTimeOffset RecordedAt { get; set; }

            public TimeSpan Elapsed { get; set; }

            public byte[] Payload { get; set; }
        }
        """;

    [Theory]
    [InlineData("Day", ScalarType.Date, false)]
    [InlineData("StartsAt", ScalarType.TimeOfDay, true)]
    [InlineData("RecordedAt", ScalarType.DateTimeOffset, false)]
    [InlineData("Elapsed", ScalarType.Duration, false)]
    [InlineData("Payload", ScalarType.ByteArray, false)]
    public void TheFiveTypesReadAsScalarsOfTheVocabulary(string property, ScalarType expected, bool nullable)
    {
        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse(Source);

        var type = builder.EntityMap.Entity.Properties.Single(p => p.Name == property).Type!;
        Assert.Equal(LangTypeCategory.Scalar, type.Category);
        Assert.Equal(expected, type.ScalarType);
        Assert.Equal(nullable, type.IsNullable);
    }

    [Fact]
    public void EveryDotNetTargetWritesTheTypesBackUnderTheirCSharpNames()
    {
        foreach (AbstractEntityBuilder builder in new AbstractEntityBuilder[]
                 {
                     new DapperEntityBuilder(), new EFCoreEntityBuilder(), new NHibernateEntityBuilder(),
                 })
        {
            new EFCoreEntityParser(builder).Parse(Source);
            var code = builder.Build().Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;

            Assert.Contains("DateOnly Day { get; set; }", code);
            Assert.Contains("TimeOnly? StartsAt { get; set; }", code);
            Assert.Contains("DateTimeOffset RecordedAt { get; set; }", code);
            Assert.Contains("TimeSpan Elapsed { get; set; }", code);
            Assert.Contains("byte[] Payload { get; set; }", code);
        }
    }

    [Theory]
    [InlineData(ScalarType.Date, "DateOnlyAsDate")]
    [InlineData(ScalarType.TimeOfDay, "TimeOnlyAsTime")]
    [InlineData(ScalarType.DateTimeOffset, "DateTimeOffset")]
    [InlineData(ScalarType.Duration, "TimeSpan")]
    [InlineData(ScalarType.ByteArray, "binary")]
    public void NHibernateGuessesItsOwnDefaultTypeForEachScalar(ScalarType scalar, string expected)
    {
        // NHibernateUtil.GuessType of 5.7.0, read at runtime: DateOnly and TimeOnly have
        // their own registered names, a TimeSpan is ticks in a 64-bit integer column.
        Assert.Equal(expected, DatabaseTypeConvertor.GuessFromScalarType(scalar));
    }

    [Fact]
    public void TheGuessReachesTheIdentifierElement()
    {
        // The identifier always carries a type attribute; a plain property without a
        // stated database type does not, and NHibernate infers the same default itself.
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Calendar
            {
                [Key]
                public DateOnly Day { get; set; }

                public TimeSpan Elapsed { get; set; }
            }
            """);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<id name=\"Day\"", xml);
        Assert.Contains("type=\"DateOnlyAsDate\"", xml);
        Assert.Contains("<property name=\"Elapsed\"", xml);
        Assert.DoesNotContain("type=\"TimeSpan\"", xml);
    }

    [Theory]
    [InlineData(DatabaseType.Date, ScalarType.Date, "DateOnlyAsDate")]
    [InlineData(DatabaseType.Date, ScalarType.DateTime, "Date")]
    [InlineData(DatabaseType.Time, ScalarType.TimeOfDay, "TimeOnlyAsTime")]
    [InlineData(DatabaseType.Time, ScalarType.Duration, "TimeAsTimeSpan")]
    [InlineData(DatabaseType.Time, ScalarType.DateTime, "Time")]
    [InlineData(DatabaseType.Timestamp, ScalarType.DateTime, "DateTime")]
    [InlineData(DatabaseType.Timestamp, ScalarType.TimeOfDay, "TimeOnlyAsDateTime")]
    [InlineData(DatabaseType.TimestampWithTimeZone, ScalarType.DateTimeOffset, "DateTimeOffset")]
    [InlineData(DatabaseType.BigInt, ScalarType.Duration, "TimeSpan")]
    [InlineData(DatabaseType.BigInt, ScalarType.TimeOfDay, "TimeOnlyAsTicks")]
    public void NHibernateNamesATemporalTypeByTheColumnFamilyAndThePropertyScalar(
        DatabaseType family, ScalarType scalar, string expected)
    {
        var naming = DatabaseTypeConvertor.ToNHibernate(family, scalar: scalar);

        Assert.Equal(expected, naming.Name);
        Assert.Null(naming.Narrowing);
    }

    [Fact]
    public void NHibernateKeepsTheFamilyOnlyNamesWhenNoScalarIsKnown()
    {
        // The pre-071 answers stay for a property whose type is a reference, unknown or
        // missing altogether - nothing about the CLR side is claimed.
        Assert.Equal("TimeAsTimeSpan", DatabaseTypeConvertor.ToNHibernate(DatabaseType.Time).Name);
        Assert.Equal("Date", DatabaseTypeConvertor.ToNHibernate(DatabaseType.Date).Name);
        Assert.Equal("Int64", DatabaseTypeConvertor.ToNHibernate(DatabaseType.BigInt).Name);
        Assert.Equal("Int64", DatabaseTypeConvertor.ToNHibernate(DatabaseType.BigInt, scalar: ScalarType.Long).Name);
    }

    [Fact]
    public void NHibernateWritesThePropertysOwnTypeWhereNoTypeReadsThePairAndReportsTheChange()
    {
        // A DateOnly property over a datetime2 column: NHibernate 5.7.0 has no type for
        // that pair. The name that can read the property is written - anything else
        // would fail at the first hydration - and the changed column claim is reported.
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Event
            {
                [Key]
                public int EventId { get; set; }

                [Column(TypeName = "datetime2")]
                public DateOnly Day { get; set; }
            }
            """);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("type=\"DateOnlyAsDate\"", xml);
        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Day");
        Assert.Contains("DateOnlyAsDate", loss.Reason);
    }

    [Fact]
    public void AStatedTimeColumnMakesATimeSpanTimeAsTimeSpan()
    {
        // EF Core maps a TimeSpan to a time column; once the column is stated, NHibernate
        // must read it through the TimeSpan-valued time type, not through its own ticks
        // default (decision 071).
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Event
            {
                [Key]
                public int EventId { get; set; }

                [Column(TypeName = "time")]
                public TimeSpan Elapsed { get; set; }
            }
            """);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<property name=\"Elapsed\"", xml);
        Assert.Contains("type=\"TimeAsTimeSpan\"", xml);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    [Theory]
    [InlineData("DateOnlyAsDate", DatabaseType.Date)]
    [InlineData("TimeOnlyAsTime", DatabaseType.Time)]
    [InlineData("TimeSpan", DatabaseType.BigInt)]
    [InlineData("TimeOnlyAsTicks", DatabaseType.BigInt)]
    [InlineData("TimeOnlyAsDateTime", DatabaseType.Timestamp)]
    [InlineData("LocalDateTime", DatabaseType.Timestamp)]
    public void NHibernateReadsTheRegisteredTemporalNamesIntoTheirColumnFamily(string typeName, DatabaseType expected)
    {
        var reading = DatabaseTypeConvertor.FromNHibernate(typeName);

        Assert.Equal(expected, reading.Type);
        Assert.Null(reading.SourceType);
    }

    [Fact]
    public void NHibernateMappingRoundTripsATimeSpanStoredAsTicks()
    {
        // The mapping says ticks in a bigint, the class says TimeSpan; both survive and
        // the same registered name comes back out.
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Job
            {
                public virtual int Id { get; set; }

                public virtual TimeSpan Elapsed { get; set; }
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Job" table="Jobs">
                    <id name="Id" type="Int32">
                        <generator class="identity" />
                    </id>
                    <property name="Elapsed" type="TimeSpan" />
                </class>
            </hibernate-mapping>
            """);

        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Elapsed");
        Assert.Equal(DatabaseType.BigInt, map.Type);
        Assert.Equal(ScalarType.Duration, map.Property.Type?.ScalarType);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;
        Assert.Contains("<property name=\"Elapsed\"", xml);
        Assert.Contains("type=\"TimeSpan\"", xml);
        Assert.DoesNotContain("Int64", xml);
    }

    [Fact]
    public void EveryDatabaseFamilyInfersALanguageScalar()
    {
        foreach (var family in Enum.GetValues<DatabaseType>())
        {
            Assert.NotNull(LanguageTypeInference.FromDatabaseType(family));
        }

        Assert.Equal(ScalarType.Date, LanguageTypeInference.FromDatabaseType(DatabaseType.Date));
        Assert.Equal(ScalarType.TimeOfDay, LanguageTypeInference.FromDatabaseType(DatabaseType.Time));
        Assert.Equal(ScalarType.DateTimeOffset, LanguageTypeInference.FromDatabaseType(DatabaseType.TimestampWithTimeZone));
        Assert.Equal(ScalarType.ByteArray, LanguageTypeInference.FromDatabaseType(DatabaseType.VarBinary));
        Assert.Equal(ScalarType.ByteArray, LanguageTypeInference.FromDatabaseType(DatabaseType.Blob));
    }

    [Fact]
    public void APropertyKnownOnlyToTheMappingOverATimeColumnIsNoLongerRefused()
    {
        // Dapper as the target: no key is required, so the only thing standing between
        // the mapping-only properties and an artifact is a language type.
        var builder = new DapperEntityBuilder();
        builder.BeginEntity();
        builder.AddClassHeader("public", "Shift");
        builder.SetPropertyDatabaseType("StartsAt", DatabaseType.Time);
        builder.SetPropertyDatabaseType("Since", DatabaseType.Date);

        CatalogCompletion.Complete(builder, reader: null);

        var maps = builder.EntityMaps.Single().PropertyMaps;
        Assert.Equal(ScalarType.TimeOfDay, maps.Single(pm => pm.Property.Name == "StartsAt").Property.Type?.ScalarType);
        Assert.Equal(ScalarType.Date, maps.Single(pm => pm.Property.Name == "Since").Property.Type?.ScalarType);

        var code = builder.Build().Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;
        Assert.Contains("TimeOnly StartsAt", code);
        Assert.Contains("DateOnly Since", code);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    private static string Emit(AbstractQueryBuilder builder, ConversionContentType type, ScalarType scalar, string text)
    {
        var day = new Property { Name = "Day", Type = LangType.Scalar(scalar) };
        builder.EntityMaps =
        [
            new EntityMap
            {
                Entity = new Entity { Name = "Event", Properties = [day] },
                Table = "Events",
                PropertyMaps = [new PropertyMap { Property = day, ColumnName = "Day" }],
            },
        ];

        builder.Push();
        builder.From("Events", alias: "e");
        builder.Project("e", "Day", "Day");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("e", "Day"),
            ComparisonOperator.GreaterThan,
            QueryOperand.Value(QueryConstant.Of(text, scalar))));
        builder.Pop();

        return builder.Build().Single(s => s.ContentType == type).Content;
    }

    [Theory]
    [InlineData(ScalarType.Date, "2024-01-31", "DateOnly.Parse(\"2024-01-31\")")]
    [InlineData(ScalarType.TimeOfDay, "08:30:00", "TimeOnly.Parse(\"08:30:00\")")]
    [InlineData(ScalarType.DateTimeOffset, "2024-01-31T08:30:00+01:00", "DateTimeOffset.Parse(\"2024-01-31T08:30:00+01:00\")")]
    [InlineData(ScalarType.Duration, "01:30:00", "TimeSpan.Parse(\"01:30:00\")")]
    public void TheVisitorsWriteATemporalLiteralTheWayEachTargetReadsIt(ScalarType scalar, string text, string linq)
    {
        Assert.Contains($"> '{text}'", Emit(new DapperSqlQueryBuilder(), ConversionContentType.SqlQuery, scalar, text));
        Assert.Contains($"> '{text}'", Emit(new NHibernateHqlQueryBuilder(), ConversionContentType.HqlQuery, scalar, text));
        Assert.Contains($"> {linq}", Emit(new EFCoreLinqQueryBuilder(), ConversionContentType.CSharpQuery, scalar, text));
    }
}
