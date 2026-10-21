using System.Globalization;
using Common.Sql;
using DatabaseCatalog;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model.AbstractRepresentation.Enums;
using Tests.Database;

namespace Tests.Catalog;

/// <summary>
/// The verification criterion of F4 counted rather than asserted fact by fact: against the
/// test database at least 95 % of the tracked metadata retrieved correctly, deviations
/// recorded explicitly. The test database is what the fixture builds in it - the schema of
/// TestSchema.sql and the LDBC tables of decision 110 - and the scripts it builds them from
/// are the expected answer (decision 016).
/// <list type="bullet">
/// <item>The expected facts are read out of those very scripts by ScriptDom, not kept as a
/// list here. A list would be a second copy of the scripts that drifts from them; read this
/// way, a table added to a script joins the count by itself.</item>
/// <item>The two sides stay independent. The expected one is the script's text, its type
/// literals read by <see cref="SqlTypeSpelling"/> with T-SQL's defaults for an omitted
/// argument supplied here; the actual one is sys.* through <see cref="SqlServerCatalogReader"/>,
/// which computes the facets from sys.columns with a type table of its own (decision 086 kept
/// the two apart). So the share checks the arithmetic the reader does - characters out of the
/// byte length of unicode data, fractional seconds moved out of scale, MAX as no length.</item>
/// <item>One fact is one of the kinds F4 names: a table, a column, the column's type (family
/// and unicode facet), nullability, length (character and binary data), precision (decimal
/// and fractional seconds) and scale (decimal); a primary key, a foreign key and a unique
/// constraint are one fact each, taken whole and in order. Counted column by column a key
/// would inflate the share and pass a key in the wrong order, which is a wrong key for
/// F1-F3.</item>
/// <item>Both schemas count. The fixture's schema was written together with the reader and
/// its tests, which state most of its facts one by one already; the LDBC schema was written
/// for another purpose and is as much a part of the test database.</item>
/// <item>The share has to reach 95 % and the deviations have to be exactly the recorded
/// ones. The bound alone would let a regression of a single fact pass without a word.</item>
/// </list>
/// The report - per schema, per kind, and every deviation - goes to the test output.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class CatalogMetadataShareTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    private const double Criterion = 0.95;

    private const string None = "(none)";

    /// <summary>
    /// The deviations the test database is known to carry, keyed as the report spells them,
    /// each with its reason: the half of the criterion that asks for them to be recorded.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RecordedDeviations =
        new Dictionary<string, string>();

    private enum FactKind
    {
        Table,
        Column,
        Type,
        Nullability,
        Length,
        Precision,
        Scale,
        PrimaryKey,
        ForeignKey,
        UniqueConstraint,
    }

    [Fact]
    public void RetrievesTheTrackedMetadataOfTheTestDatabase()
    {
        fixture.SkipIfUnavailable();

        List<MetadataFact> facts =
        [
            .. Measure(
                "TestSchema.sql",
                fixture.SchemaName,
                [TestSchemaFixture.ReadScript(TestSchemaFixture.ScriptResourceName, fixture.SchemaName)]),
            .. Measure(
                "ldbc",
                fixture.LdbcSchemaName,
                [
                    TestSchemaFixture.ReadScript(TestSchemaFixture.LdbcSchemaResourceName, fixture.LdbcSchemaName),
                    TestSchemaFixture.ReadScript(TestSchemaFixture.LdbcConstraintsResourceName, fixture.LdbcSchemaName),
                ]),
        ];

        Report(facts);

        var unmeasured = Enum.GetValues<FactKind>().Where(kind => facts.All(f => f.Kind != kind)).ToList();
        Assert.True(
            unmeasured.Count == 0,
            $"The test database states no fact of {string.Join(", ", unmeasured)}, "
            + "so the share would cover a kind it never measured.");

        Assert.True(
            Share(facts) >= Criterion,
            $"{Spell(facts)} is below the {Criterion.ToString("P0", CultureInfo.InvariantCulture)} of F4.");

        var deviations = facts.Where(f => !f.IsCorrect).Select(f => f.Key).ToHashSet();
        var unrecorded = deviations.Where(d => !RecordedDeviations.ContainsKey(d)).ToList();
        var vanished = RecordedDeviations.Keys.Where(d => !deviations.Contains(d)).ToList();

        Assert.True(
            unrecorded.Count == 0 && vanished.Count == 0,
            "The deviations differ from the recorded ones:"
            + string.Concat(unrecorded.Select(d => $"{Environment.NewLine}  not recorded: {d}"))
            + string.Concat(vanished.Select(d => $"{Environment.NewLine}  recorded, no longer found: {d}")));
    }

    [Fact]
    public void ADeclarationTheCatalogDoesNotHoldIsADeviationOfItsKind()
    {
        fixture.SkipIfUnavailable();

        // The script is edited after the fixture built the schema from it, so each edit is a
        // fact the catalog does not hold - what a reader misreading that fact would return.
        // Exactly the edited facts must come out as deviations, each under its own kind.
        string[] edits =
        [
            "[Website]      NVARCHAR(200)", "[Website]      NVARCHAR(400)",
            "[CreditLimit]  DECIMAL(18,2)", "[CreditLimit]  DECIMAL(19,2)",
            "[Name]         NVARCHAR(100)   NOT NULL", "[Name]         NVARCHAR(100)   NULL",
            "[IsCancelled]  BIT", "[IsCancelled]  INT",
            "PRIMARY KEY ([CompanyId], [OrderId])", "PRIMARY KEY ([OrderId], [CompanyId])",
            "UNIQUE ([Sku])", "UNIQUE ([Sku], [ProductName])",
        ];

        var script = TestSchemaFixture.ReadScript(TestSchemaFixture.ScriptResourceName, fixture.SchemaName);
        for (var i = 0; i < edits.Length; i += 2)
        {
            Assert.Contains(edits[i], script, StringComparison.Ordinal);
            script = script.Replace(edits[i], edits[i + 1], StringComparison.Ordinal);
        }

        var deviations = Measure("TestSchema.sql", fixture.SchemaName, [script])
            .Where(f => !f.IsCorrect)
            .Select(f => f.Key)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "TestSchema.sql: Length CustomerProfiles.Website",
                "TestSchema.sql: Nullability Customers.Name",
                "TestSchema.sql: Precision CustomerProfiles.CreditLimit",
                "TestSchema.sql: PrimaryKey Orders",
                "TestSchema.sql: Type Orders.IsCancelled",
                "TestSchema.sql: UniqueConstraint Products.UQ_Products_Sku",
            },
            deviations);
    }

    private static List<MetadataFact> Measure(string source, string schema, IEnumerable<string> scripts)
    {
        var tables = ReadExpected(source, scripts);
        Assert.NotEmpty(tables);

        // A reader of its own, so that what is counted is what this reader reads and not an
        // answer the collection's cache took from an earlier test.
        using var reader = new SqlServerCatalogReader(TestDatabase.ConnectionString!);
        var lookups = reader.ReadTables([.. tables.Select(t => new TableRequest(t.Name, t.Schema, [t.Name]))]);

        return [.. tables.SelectMany(table => Compare(source, table, lookups[table.Name].Image, tables))];
    }

    private static IEnumerable<MetadataFact> Compare(
        string source, ExpectedTable table, TableImage? image, IReadOnlyList<ExpectedTable> tables)
    {
        MetadataFact Of(FactKind kind, string subject, string expected, string? actual)
            => new(source, kind, subject, expected, actual ?? None);

        yield return Of(FactKind.Table, table.Name, $"{table.Schema}.{table.Name}", image?.QualifiedName);

        foreach (var column in table.Columns)
        {
            var subject = $"{table.Name}.{column.Name}";
            var actual = image?.Columns.FirstOrDefault(c => c.Name == column.Name);
            var type = column.Type;

            // A column the script leaves without NULL or NOT NULL is NOT NULL in the key or
            // under IDENTITY, and nullable otherwise: SqlClient sessions run ANSI_NULL_DFLT_ON.
            var isNullable = column.IsNullable ?? !(column.IsIdentity || table.PrimaryKey.Contains(column.Name));

            yield return Of(FactKind.Column, subject, column.Name, actual?.Name);
            yield return Of(
                FactKind.Type, subject, SpellType(type.Type, type.IsUnicode), actual is null ? null : SpellType(actual.Type, actual.IsUnicode));
            yield return Of(
                FactKind.Nullability, subject, SpellNullability(isNullable), actual is null ? null : SpellNullability(actual.IsNullable));

            if (type.Type is DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Binary or DatabaseType.VarBinary)
            {
                yield return Of(FactKind.Length, subject, SpellLength(type.Length), actual is null ? null : SpellLength(actual.Length));
            }

            if (type.Type is DatabaseType.Decimal or DatabaseType.Time or DatabaseType.Timestamp or DatabaseType.TimestampWithTimeZone)
            {
                yield return Of(FactKind.Precision, subject, SpellNumber(type.Precision), actual is null ? null : SpellNumber(actual.Precision));
            }

            if (type.Type is DatabaseType.Decimal)
            {
                yield return Of(FactKind.Scale, subject, SpellNumber(type.Scale), actual is null ? null : SpellNumber(actual.Scale));
            }
        }

        if (table.PrimaryKey.Count > 0)
        {
            yield return Of(
                FactKind.PrimaryKey, table.Name, SpellColumns(table.PrimaryKey), image is null ? null : SpellColumns(image.PrimaryKeyColumns));
        }

        foreach (var foreignKey in table.ForeignKeys)
        {
            // A foreign key that names no referenced columns points at the primary key.
            var referencedColumns = foreignKey.ReferencedColumns.Count > 0
                ? foreignKey.ReferencedColumns
                : tables.Single(t => t.Schema == foreignKey.ReferencedSchema && t.Name == foreignKey.ReferencedTable).PrimaryKey;

            var expected = SpellForeignKey(
                foreignKey.ReferencedSchema,
                foreignKey.ReferencedTable,
                foreignKey.Columns.Zip(referencedColumns, (column, referenced) => new ForeignKeyColumn(column, referenced)));

            var actual = image?.ForeignKeys.FirstOrDefault(f => foreignKey.Name is null
                ? SpellForeignKey(f.ReferencedSchema, f.ReferencedTable, f.Columns) == expected
                : f.Name == foreignKey.Name);

            yield return Of(
                FactKind.ForeignKey,
                $"{table.Name}.{foreignKey.Name ?? expected}",
                expected,
                actual is null ? null : SpellForeignKey(actual.ReferencedSchema, actual.ReferencedTable, actual.Columns));
        }

        foreach (var unique in table.UniqueConstraints)
        {
            var expected = SpellColumns(unique.Columns);
            var actual = image?.UniqueConstraints.FirstOrDefault(u => unique.Name is null
                ? SpellColumns(u.Columns) == expected
                : u.Name == unique.Name);

            yield return Of(
                FactKind.UniqueConstraint, $"{table.Name}.{unique.Name ?? expected}", expected, actual is null ? null : SpellColumns(actual.Columns));
        }
    }

    /// <summary>
    /// The tables the scripts create, with the columns and constraints they declare. A
    /// statement that would change a tracked fact after it was declared fails the test
    /// instead of being skipped, so that the expected answer never silently stops being the
    /// schema the fixture built.
    /// </summary>
    private static List<ExpectedTable> ReadExpected(string source, IEnumerable<string> scripts)
    {
        var tables = new List<ExpectedTable>();

        foreach (var text in scripts)
        {
            var script = (TSqlScript)new TSql160Parser(initialQuotedIdentifiers: true)
                .Parse(new StringReader(text), out var errors);

            Assert.True(
                errors.Count == 0,
                $"A script of {source} does not parse: "
                + string.Join("; ", errors.Select(e => $"({e.Line},{e.Column}) {e.Message}")));

            foreach (var statement in script.Batches.SelectMany(b => b.Statements))
            {
                switch (statement)
                {
                    case CreateTableStatement create:
                        var created = new ExpectedTable(SchemaOf(create.SchemaObjectName), create.SchemaObjectName.BaseIdentifier.Value);
                        ReadDefinition(created, create.Definition);
                        tables.Add(created);
                        break;

                    case AlterTableAddTableElementStatement alter:
                        ReadDefinition(
                            tables.Single(t => t.Schema == SchemaOf(alter.SchemaObjectName)
                                && t.Name == alter.SchemaObjectName.BaseIdentifier.Value),
                            alter.Definition);
                        break;

                    case AlterTableStatement other:
                        Assert.Fail($"A script of {source} alters a table by {other.GetType().Name}, which the expected answer does not read.");
                        break;
                }
            }
        }

        return tables;
    }

    private static void ReadDefinition(ExpectedTable table, TableDefinition definition)
    {
        foreach (var column in definition.ColumnDefinitions)
        {
            var name = column.ColumnIdentifier.Value;

            table.Columns.Add(new ExpectedColumn(
                name,
                ExpectedType(column.DataType),
                column.Constraints.OfType<NullableConstraintDefinition>().Select(c => (bool?)c.Nullable).LastOrDefault(),
                column.IdentityOptions is not null));

            ReadConstraints(table, column.Constraints, name);
        }

        ReadConstraints(table, definition.TableConstraints, column: null);
    }

    /// <summary>
    /// Keys and constraints of a table. One written on a column itself names no columns and
    /// is over that column.
    /// </summary>
    private static void ReadConstraints(ExpectedTable table, IEnumerable<ConstraintDefinition> constraints, string? column)
    {
        foreach (var constraint in constraints)
        {
            switch (constraint)
            {
                case UniqueConstraintDefinition { IsPrimaryKey: true } primaryKey:
                    table.PrimaryKey.AddRange(ColumnsOf(primaryKey, column));
                    break;

                case UniqueConstraintDefinition unique:
                    table.UniqueConstraints.Add(new ExpectedUniqueConstraint(unique.ConstraintIdentifier?.Value, ColumnsOf(unique, column)));
                    break;

                case ForeignKeyConstraintDefinition foreignKey:
                    table.ForeignKeys.Add(new ExpectedForeignKey(
                        foreignKey.ConstraintIdentifier?.Value,
                        SchemaOf(foreignKey.ReferenceTableName),
                        foreignKey.ReferenceTableName.BaseIdentifier.Value,
                        foreignKey.Columns.Count > 0 ? [.. foreignKey.Columns.Select(c => c.Value)] : [column!],
                        [.. foreignKey.ReferencedTableColumns.Select(c => c.Value)]));
                    break;
            }
        }
    }

    private static List<string> ColumnsOf(UniqueConstraintDefinition constraint, string? column)
        => constraint.Columns.Count > 0
            ? [.. constraint.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value)]
            : [column!];

    private static string SchemaOf(SchemaObjectName name) => name.SchemaIdentifier?.Value ?? "dbo";

    /// <summary>
    /// The type a column definition states, as the neutral vocabulary reads its literal, with
    /// T-SQL's own defaults for an argument the script leaves out: one character or byte for
    /// character and binary data, decimal(18, 0), seven digits of fractional seconds.
    /// </summary>
    private static SqlTypeReading ExpectedType(DataTypeReference type)
    {
        var arguments = type is ParameterizedDataTypeReference { Parameters.Count: > 0 } parameterized
            ? $"({string.Join(",", parameterized.Parameters.Select(p => p.Value))})"
            : string.Empty;

        var reading = SqlTypeSpelling.Read(type.Name.BaseIdentifier.Value + arguments, declaredSourceDialect: null)!.Value;

        return reading.Type switch
        {
            DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Binary or DatabaseType.VarBinary when arguments.Length == 0
                => reading with { Length = reading.Length ?? 1 },
            DatabaseType.Decimal => reading with { Precision = reading.Precision ?? 18, Scale = reading.Scale ?? 0 },
            DatabaseType.Time or DatabaseType.Timestamp or DatabaseType.TimestampWithTimeZone
                => reading with { Precision = reading.Precision ?? 7 },
            _ => reading,
        };
    }

    private void Report(IReadOnlyCollection<MetadataFact> facts)
    {
        foreach (var source in facts.GroupBy(f => f.Source))
        {
            output.WriteLine($"{source.Key}: {Spell([.. source])}");

            foreach (var kind in source.GroupBy(f => f.Kind).OrderBy(g => g.Key))
            {
                output.WriteLine($"  {kind.Key}: {Spell([.. kind])}");
            }
        }

        output.WriteLine($"Test database: {Spell(facts)}");

        foreach (var deviation in facts.Where(f => !f.IsCorrect))
        {
            output.WriteLine($"Deviation {deviation.Key}: expected {deviation.Expected}, read {deviation.Actual}");
        }
    }

    private static double Share(IReadOnlyCollection<MetadataFact> facts)
        => (double)facts.Count(f => f.IsCorrect) / facts.Count;

    private static string Spell(IReadOnlyCollection<MetadataFact> facts)
        => $"{facts.Count(f => f.IsCorrect)} of {facts.Count} = {Share(facts).ToString("P2", CultureInfo.InvariantCulture)}";

    private static string SpellType(DatabaseType? type, bool? isUnicode)
        => (type?.ToString() ?? "(no family)") + isUnicode switch
        {
            true => " unicode",
            false => " non-unicode",
            null => string.Empty,
        };

    private static string SpellNullability(bool isNullable) => isNullable ? "NULL" : "NOT NULL";

    private static string SpellLength(int? length) => length?.ToString(CultureInfo.InvariantCulture) ?? "max";

    private static string SpellNumber(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? None;

    private static string SpellColumns(IEnumerable<string> columns)
        => string.Join(", ", columns) is { Length: > 0 } spelled ? spelled : None;

    private static string SpellForeignKey(string schema, string table, IEnumerable<ForeignKeyColumn> columns)
        => $"{schema}.{table} ({string.Join(", ", columns.Select(c => $"{c.Column} -> {c.ReferencedColumn}"))})";

    private sealed class ExpectedTable(string schema, string name)
    {
        public string Schema { get; } = schema;

        public string Name { get; } = name;

        public List<ExpectedColumn> Columns { get; } = [];

        public List<string> PrimaryKey { get; } = [];

        public List<ExpectedForeignKey> ForeignKeys { get; } = [];

        public List<ExpectedUniqueConstraint> UniqueConstraints { get; } = [];
    }

    private sealed record ExpectedColumn(string Name, SqlTypeReading Type, bool? IsNullable, bool IsIdentity);

    private sealed record ExpectedForeignKey(
        string? Name,
        string ReferencedSchema,
        string ReferencedTable,
        IReadOnlyList<string> Columns,
        IReadOnlyList<string> ReferencedColumns);

    private sealed record ExpectedUniqueConstraint(string? Name, IReadOnlyList<string> Columns);

    /// <summary>One tracked fact: what the script declares and what the reader returned, both spelled.</summary>
    private sealed record MetadataFact(string Source, FactKind Kind, string Subject, string Expected, string Actual)
    {
        public bool IsCorrect => string.Equals(Expected, Actual, StringComparison.Ordinal);

        public string Key => $"{Source}: {Kind} {Subject}";
    }
}
