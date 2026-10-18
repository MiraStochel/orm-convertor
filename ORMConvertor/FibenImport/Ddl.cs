using System.Text.RegularExpressions;

namespace FibenImport;

/// <summary>One column of a FIBEN table as FIBEN.sql declares it, in the Db2 dialect.</summary>
public sealed record Column(string Name, string Db2Type, bool NotNull)
{
    /// <summary>The T-SQL type the column takes in the Fiben database. Text compares binary, as Db2 does.</summary>
    public string SqlType => Db2Type switch
    {
        "BIGINT" => "BIGINT",
        "INTEGER" => "INT",
        "DOUBLE PRECISION" => "FLOAT",
        "DATE" => "DATE",
        "TIMESTAMP" => "DATETIME2(6)",
        "CHARACTER(5)" => "CHAR(5) COLLATE Latin1_General_100_BIN2",
        "VARCHAR(1024)" => "NVARCHAR(1024) COLLATE Latin1_General_100_BIN2",
        _ => throw new NotSupportedException($"{Name}: the Db2 type {Db2Type} has no mapping."),
    };

    /// <summary>The C# type of the Dapper property; nullable where the column is.</summary>
    public string CSharpType => Db2Type switch
    {
        "BIGINT" => "long",
        "INTEGER" => "int",
        "DOUBLE PRECISION" => "double",
        "DATE" => "DateOnly",
        "TIMESTAMP" => "DateTime",
        "CHARACTER(5)" or "VARCHAR(1024)" => "string",
        _ => throw new NotSupportedException($"{Name}: the Db2 type {Db2Type} has no mapping."),
    } + (NotNull ? string.Empty : "?");

    public bool IsNumeric => Db2Type is "BIGINT" or "INTEGER" or "DOUBLE PRECISION";

    public bool IsText => Db2Type is "CHARACTER(5)" or "VARCHAR(1024)";

    /// <summary>
    /// How the staging text of the loader becomes the typed value: Db2 exports a date as
    /// yyyymmdd, a timestamp as yyyy-mm-dd-hh.mm.ss.ffffff and a double with a sign and an
    /// exponent; an empty field is NULL.
    /// </summary>
    public string Conversion(string staged) => Db2Type switch
    {
        "BIGINT" => $"CAST(NULLIF({staged}, '') AS BIGINT)",
        "INTEGER" => $"CAST(NULLIF({staged}, '') AS INT)",
        "DOUBLE PRECISION" => $"CAST(NULLIF({staged}, '') AS FLOAT)",
        "DATE" => $"CONVERT(DATE, NULLIF({staged}, ''), 112)",
        "TIMESTAMP" => $"CONVERT(DATETIME2(6), STUFF(STUFF(STUFF(NULLIF({staged}, ''), 11, 1, ' '), 14, 1, ':'), 17, 1, ':'), 121)",
        _ => staged,
    };
}

public sealed record Table(string Name, IReadOnlyList<Column> Columns, string KeyColumn);

public sealed record ForeignKey(string Name, string Table, string Column, string ReferencedTable, string ReferencedColumn);

/// <summary>
/// FIBEN.sql, the DDL of the benchmark for Db2 and PostgreSQL: one CREATE TABLE per line with
/// the primary key inline, then one ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY per line.
/// Read by two regular expressions, because that is all the file contains; anything the
/// expressions do not match stops the import rather than being skipped.
/// </summary>
public sealed class Ddl
{
    private static readonly Regex CreateTable = new(
        @"^CREATE TABLE (?<table>\w+) \((?<body>.*), PRIMARY KEY \((?<key>\w+)\)\);$", RegexOptions.Compiled);

    private static readonly Regex ColumnDeclaration = new(
        @"^(?<name>\w+) (?<type>BIGINT|INTEGER|DOUBLE PRECISION|DATE|TIMESTAMP|CHARACTER\(5\)|VARCHAR\(1024\))(?<notnull> NOT NULL)?$", RegexOptions.Compiled);

    private static readonly Regex AddForeignKey = new(
        @"^ALTER TABLE (?<table>\w+) ADD CONSTRAINT (?<name>\w+) FOREIGN KEY \((?<column>\w+)\) REFERENCES (?<referenced>\w+) \((?<referencedColumn>\w+)\);$", RegexOptions.Compiled);

    public IReadOnlyList<Table> Tables { get; }

    public IReadOnlyList<ForeignKey> ForeignKeys { get; }

    public IReadOnlyDictionary<string, Table> ByName { get; }

    private Ddl(IReadOnlyList<Table> tables, IReadOnlyList<ForeignKey> foreignKeys)
    {
        Tables = tables;
        ForeignKeys = foreignKeys;
        ByName = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static Ddl Read(string path)
    {
        var tables = new List<Table>();
        var foreignKeys = new List<ForeignKey>();

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (CreateTable.Match(line) is { Success: true } create)
            {
                var columns = create.Groups["body"].Value.Split(", ").Select(declaration =>
                {
                    var match = ColumnDeclaration.Match(declaration);
                    if (!match.Success)
                    {
                        throw new InvalidDataException($"{create.Groups["table"].Value}: the column declaration \"{declaration}\" is not understood.");
                    }

                    return new Column(match.Groups["name"].Value, match.Groups["type"].Value, match.Groups["notnull"].Success);
                }).ToList();

                tables.Add(new Table(create.Groups["table"].Value, columns, create.Groups["key"].Value));
                continue;
            }

            if (AddForeignKey.Match(line) is { Success: true } alter)
            {
                foreignKeys.Add(new ForeignKey(
                    alter.Groups["name"].Value,
                    alter.Groups["table"].Value,
                    alter.Groups["column"].Value,
                    alter.Groups["referenced"].Value,
                    alter.Groups["referencedColumn"].Value));
                continue;
            }

            throw new InvalidDataException($"FIBEN.sql: the line \"{line}\" is neither a table nor a foreign key.");
        }

        return new Ddl(tables, foreignKeys);
    }
}
