using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Model;

namespace AdvisorBenchmarking;

internal static class BenchmarkHarnessBuilder
{
    public static BenchmarkSource Build(
        ORMEnum framework,
        IReadOnlyList<ConversionSource> sources,
        string connectionString)
    {
        return framework switch
        {
            ORMEnum.Dapper => BuildDapperHarness(sources, connectionString),
            _ => throw new NotSupportedException($"Benchmark harness for framework {framework} is not implemented yet.")
        };
    }

    private static BenchmarkSource BuildDapperHarness(
        IReadOnlyList<ConversionSource> sources,
        string connectionString)
    {
        var entityInfos = sources
            .Where(s => s.ContentType == ConversionContentType.CSharpEntity)
            .Select(s =>
            {
                var typeName = ExtractTypeName(s.Content);
                return new EntityInfo(
                    s.Content,
                    ExtractNamespace(s.Content),
                    typeName,
                    ExtractTableName(s.Content, typeName));
            })
            .ToList();
        entityInfos = QualifyEntityTableNames(entityInfos, connectionString);

        if (entityInfos.Count == 0)
        {
            throw new InvalidOperationException("Dapper harness requires at least one entity definition.");
        }

        var querySource = sources
            .FirstOrDefault(s => s.ContentType == ConversionContentType.CSharpQuery)?.Content
            ?? throw new InvalidOperationException("Dapper harness requires a query definition.");
        querySource = NormalizeQuerySource(querySource);

        var ns = "DynamicBenchmarks.Generated";
        var typeName = $"DapperBenchmark_{Guid.NewGuid():N}";

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.Data.SqlClient;");
        sb.AppendLine("using Dapper;");
        foreach (var distinctNs in entityInfos.Select(e => e.Namespace).Where(n => n != null).Distinct())
        {
            sb.AppendLine($"using {distinctNs};");
        }
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {typeName}");
        sb.AppendLine("    {");
        sb.AppendLine($"        private const string ConnectionString = @\"{EscapeVerbatim(connectionString)}\";");
        sb.AppendLine("        private SqlConnection connection = default!;");
        sb.AppendLine();
        sb.AppendLine("        public void Setup()");
        sb.AppendLine("        {");
        sb.AppendLine("            connection = new SqlConnection(ConnectionString);");
        sb.AppendLine("            connection.Open();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Cleanup()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (connection is null) { return; }");
        sb.AppendLine("            if (connection.State != System.Data.ConnectionState.Closed)");
        sb.AppendLine("            {");
        sb.AppendLine("                connection.Close();");
        sb.AppendLine("            }");
        sb.AppendLine("            connection.Dispose();");
        sb.AppendLine("            connection = null!;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int Execute()");
        sb.AppendLine("        {");
        sb.AppendLine("            return Query().Count;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine(BuildDapperQueryMethod(querySource, entityInfos));
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var entity in entityInfos)
        {
            sb.AppendLine(NormalizeEntitySource(entity.Source));
            sb.AppendLine();
        }

        return new BenchmarkSource(ns, typeName, sb.ToString());
    }

    private static string NormalizeEntitySource(string content)
    {
        var normalized = content.ReplaceLineEndings("\n").Trim();

        if (!normalized.StartsWith("namespace ", StringComparison.Ordinal))
        {
            return normalized;
        }

        var firstLineEnd = normalized.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return normalized;
        }

        var header = normalized[..firstLineEnd];
        if (!header.TrimEnd().EndsWith(';'))
        {
            return normalized;
        }

        var ns = header[10..].Trim().TrimEnd(';');
        var body = normalized[(firstLineEnd + 1)..];

        var indentedBody = Indent(body, "    ");

        return $"namespace {ns}\n{{\n{indentedBody}\n}}";
    }

    private static string Indent(string source, string indentation)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        return string.Join("\n", lines.Select(line => indentation + line));
    }

    private static string EscapeVerbatim(string value) =>
        value.Replace("\"", "\"\"");

    private static string NormalizeQuerySource(string source) =>
        source.ReplaceLineEndings("\n").Trim();

    private static string? ExtractNamespace(string entitySource)
    {
        var normalized = entitySource.ReplaceLineEndings("\n");
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("namespace ", StringComparison.Ordinal))
            {
                var ns = trimmed["namespace ".Length..].Trim();
                if (ns.EndsWith(';'))
                {
                    ns = ns[..^1].Trim();
                }
                return ns.Length > 0 ? ns : null;
            }
        }

        return null;
    }

    private sealed record EntityInfo(string Source, string? Namespace, string? TypeName, string TableName);

    private static string? ExtractTypeName(string entitySource)
    {
        var match = Regex.Match(entitySource, @"class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)");
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string ExtractTableName(string entitySource, string? typeName)
    {
        var attrMatch = Regex.Match(
            entitySource,
            @"\[Table\(\s*""(?<table>[^""]+)""(?:\s*,\s*Schema\s*=\s*""(?<schema>[^""]+)"")?\s*\)\]",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        if (attrMatch.Success)
        {
            var table = attrMatch.Groups["table"].Value;
            var schema = attrMatch.Groups["schema"].Success ? attrMatch.Groups["schema"].Value : null;
            return schema is { Length: > 0 } ? $"{schema}.{table}" : table;
        }

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            return typeName.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? typeName
                : $"{typeName}s";
        }

        return "UnknownTable";
    }

    private static string BuildDapperQueryMethod(string querySource, IReadOnlyList<EntityInfo> entityInfos)
    {
        string normalized = querySource.ReplaceLineEndings("\n");

        var typeMatch = Regex.Match(normalized, @"connection\.Query<(?<type>[^>]+)>", RegexOptions.Multiline);
        string resultType = typeMatch.Success ? typeMatch.Groups["type"].Value.Trim() : entityInfos.FirstOrDefault()?.TypeName ?? "global::System.Collections.Generic.Dictionary<string, object>";

        var knownTypes = entityInfos
            .Select(e => e.TypeName)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet(StringComparer.Ordinal);

        if (!knownTypes.Contains(resultType))
        {
            resultType = entityInfos.FirstOrDefault()?.TypeName ?? "global::System.Collections.Generic.Dictionary<string, object>";
        }

        var sqlMatch = Regex.Match(normalized, "\"\"\"(?<sql>.*?)\"\"\"", RegexOptions.Singleline);
        string sqlBody = sqlMatch.Success ? sqlMatch.Groups["sql"].Value.Trim('\r', '\n') : "SELECT 1";

        string primaryTable = entityInfos.FirstOrDefault()?.TableName ?? resultType;
        sqlBody = ReplaceSetPlaceholder(sqlBody, primaryTable);

        var builder = new StringBuilder();
        builder.AppendLine($"        public List<{resultType}> Query()");
        builder.AppendLine("        {");
        builder.AppendLine("            const string Sql = @\"");
        builder.AppendLine(sqlBody.Replace("\"", "\"\""));
        builder.AppendLine("\";");
        builder.AppendLine($"            return connection.Query<{resultType}>(Sql).ToList();");
        builder.AppendLine("        }");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string ReplaceSetPlaceholder(string sqlBody, string tableName) =>
        Regex.Replace(sqlBody, @"\bSet\b", tableName, RegexOptions.IgnoreCase);

    private static List<EntityInfo> QualifyEntityTableNames(List<EntityInfo> entityInfos, string connectionString)
    {
        if (entityInfos.Count == 0)
        {
            return entityInfos;
        }

        try
        {
            // Reach into the advisor database once per run so generated SQL keeps working even when
            // the translated entity omitted schema information (common with EF models).
            using var connection = new SqlConnection(connectionString);
            connection.Open();

            return entityInfos
                .Select(info => info with { TableName = ResolveQualifiedTableName(connection, info.TableName) })
                .ToList();
        }
        catch (Exception)
        {
            return entityInfos;
        }
    }

    private static string ResolveQualifiedTableName(SqlConnection connection, string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName) || tableName.Contains('.', StringComparison.Ordinal))
        {
            return tableName;
        }

        foreach (var candidate in ExpandTableNameCandidates(tableName))
        {
            // Prefer matches in dbo but tolerate other schemas so long as SQL Server can find the table.
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT TOP (1) TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = @TableName
                ORDER BY CASE WHEN TABLE_SCHEMA = 'dbo' THEN 0 ELSE 1 END, TABLE_SCHEMA
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@TableName", candidate);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var schema = reader.GetString(0);
                var name = reader.GetString(1);
                return $"{schema}.{name}";
            }
        }

        return tableName;
    }

    private static IEnumerable<string> ExpandTableNameCandidates(string tableName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seen.Add(tableName))
        {
            yield return tableName;
        }

        if (tableName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            var singular = tableName[..^1];
            if (seen.Add(singular))
            {
                yield return singular;
            }
        }
        else
        {
            var plural = $"{tableName}s";
            if (seen.Add(plural))
            {
                yield return plural;
            }
        }
    }
}
