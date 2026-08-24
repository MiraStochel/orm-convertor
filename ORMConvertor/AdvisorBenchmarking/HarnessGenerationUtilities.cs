using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Common.Naming;
using DatabaseCatalog;
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
        // Prefer the table name (without schema) so user queries like ctx.Customers work naturally.
        var table = info.TableName;
        var nameOnly = table.Contains('.') ? table.Split('.', 2)[1] : table;
        // Basic sanitization for C# identifiers
        var prop = Regex.Replace(nameOnly, @"[^A-Za-z0-9_]", "");
        if (string.IsNullOrWhiteSpace(prop))
        {
            var baseName = string.IsNullOrWhiteSpace(info.TypeName) ? "Entity" : info.TypeName!;
            return baseName.EndsWith("Set", StringComparison.Ordinal) ? baseName : $"{baseName}Set";
        }
        // Ensure it starts with a letter or underscore
        if (!char.IsLetter(prop[0]) && prop[0] != '_')
        {
            prop = "_" + prop;
        }
        return prop;
    }

    internal static string ReplaceSetPlaceholder(string sqlBody, string tableName) =>
        Regex.Replace(sqlBody, @"\bSet\b", tableName, RegexOptions.IgnoreCase);

    /// <summary>
    /// Qualifies unqualified table names against the advisor database, so generated SQL
    /// keeps working even when the translated entity omitted schema information (common
    /// with EF models). One mechanism answers the question for the whole solution: the
    /// catalog reader of decision 015, one batch per run. A connection failure propagates
    /// instead of being swallowed - the benchmark run needs the database anyway, and an
    /// early clear error beats SQL that silently targets the wrong table.
    /// </summary>
    internal static List<EntityInfo> QualifyEntityTableNames(
        List<EntityInfo> entityInfos,
        string connectionString)
    {
        var requests = entityInfos
            .Select((info, index) => (Info: info, Index: index))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Info.TableName)
                && !entry.Info.TableName.Contains('.', StringComparison.Ordinal))
            .Select(entry => new TableRequest(
                entry.Index.ToString(),
                Schema: null,
                EntityTableNaming.TableCandidatesFor(entry.Info.TableName)))
            .ToList();

        if (requests.Count == 0)
        {
            return entityInfos;
        }

        var lookups = new SqlServerCatalogReader(connectionString).ReadTables(requests);

        return entityInfos
            .Select((info, index) =>
                lookups.TryGetValue(index.ToString(), out var lookup) && lookup.Image is not null
                    ? info with { TableName = lookup.Image.QualifiedName }
                    : info)
            .ToList();
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
