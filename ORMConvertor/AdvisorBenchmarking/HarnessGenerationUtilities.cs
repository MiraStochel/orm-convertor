using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Model;

namespace AdvisorBenchmarking;

internal static class HarnessGenerationUtilities
{
    internal static List<EntityInfo> ExtractEntityInfos(
        IReadOnlyList<ConversionSource> sources,
        string connectionString)
    {
        var entityInfos = sources
            .Where(s => s.ContentType == ConversionContentType.CSharpEntity)
            .Select(s =>
            {
                var (usings, body) = SplitUsings(s.Content);
                var typeName = ExtractTypeName(body);
                return new EntityInfo(
                    body,
                    usings,
                    ExtractNamespace(body),
                    typeName,
                    ExtractTableName(body, typeName));
            })
            .ToList();

        return QualifyEntityTableNames(entityInfos, connectionString);
    }

    internal static List<string> ExtractQuerySources(IReadOnlyList<ConversionSource> sources) =>
        sources
            .Where(s => s.ContentType == ConversionContentType.CSharpQuery)
            .Select(s => NormalizeQuerySource(s.Content))
            .Where(content => content.Length > 0)
            .ToList();

    internal static string NormalizeEntitySource(string content)
    {
        var normalized = content.ReplaceLineEndings("\n").Trim();

        if (!normalized.StartsWith("namespace ", StringComparison.Ordinal))
        {
            return RelaxOptionalValueTypes(normalized);
        }

        var firstLineEnd = normalized.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return RelaxOptionalValueTypes(normalized);
        }

        var header = normalized[..firstLineEnd];
        if (!header.TrimEnd().EndsWith(';'))
        {
            return RelaxOptionalValueTypes(normalized);
        }

        var ns = header[10..].Trim().TrimEnd(';');
        var body = normalized[(firstLineEnd + 1)..];

        var indentedBody = Indent(body, "    ");

        var relaxedBody = RelaxOptionalValueTypes(indentedBody);
        return $"namespace {ns}\n{{\n{relaxedBody}\n}}";
    }

    internal static string Indent(string source, string indentation)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        return string.Join("\n", lines.Select(line => indentation + line));
    }

    internal static string EscapeVerbatim(string value) =>
        value.Replace("\"", "\"\"");

    internal static string NormalizeQuerySource(string source) =>
        source.ReplaceLineEndings("\n").Trim();

    internal static string? ExtractNamespace(string entitySource)
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

    internal static string? ExtractTypeName(string entitySource)
    {
        var match = Regex.Match(entitySource, @"class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)");
        return match.Success ? match.Groups["name"].Value : null;
    }

    internal static string ExtractTableName(string entitySource, string? typeName)
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

    internal static string GetQualifiedTypeName(EntityInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.TypeName))
        {
            return "global::System.Object";
        }

        return info.Namespace is { Length: > 0 } ns
            ? $"global::{ns}.{info.TypeName}"
            : $"global::{info.TypeName}";
    }

    internal static string GetDbSetPropertyName(EntityInfo info)
    {
        var baseName = string.IsNullOrWhiteSpace(info.TypeName) ? "Entity" : info.TypeName!;
        return baseName.EndsWith("Set", StringComparison.Ordinal) ? baseName : $"{baseName}Set";
    }

    internal static string ReplaceSetPlaceholder(string sqlBody, string tableName) =>
        Regex.Replace(sqlBody, @"\bSet\b", tableName, RegexOptions.IgnoreCase);

    internal static List<EntityInfo> QualifyEntityTableNames(
        List<EntityInfo> entityInfos,
        string connectionString)
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

    internal static string ResolveQualifiedTableName(SqlConnection connection, string tableName)
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

    internal static IEnumerable<string> ExpandTableNameCandidates(string tableName)
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

    internal static (IReadOnlyList<string> Usings, string Body) SplitUsings(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var usings = new List<string>();
        int index = 0;
        for (; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(';'))
            {
                usings.Add(trimmed.TrimEnd(';'));
                continue;
            }
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }
            break;
        }

        var body = string.Join("\n", lines[index..]);
        return (usings, body);
    }

    private static string RelaxOptionalValueTypes(string source)
    {
        return Regex.Replace(
            source,
            @"(\bpublic\s+(?:virtual\s+)?)(decimal)(\s+\w+\s*\{)",
            m => $"{m.Groups[1].Value}decimal?{m.Groups[3].Value}");
    }

    internal sealed record EntityInfo(string Source, IReadOnlyList<string> Usings, string? Namespace, string? TypeName, string TableName);
}
