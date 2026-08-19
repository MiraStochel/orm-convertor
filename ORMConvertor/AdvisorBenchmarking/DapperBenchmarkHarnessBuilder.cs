using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Model;
using static AdvisorBenchmarking.HarnessGenerationUtilities;
using EntityInfo = AdvisorBenchmarking.HarnessGenerationUtilities.EntityInfo;

namespace AdvisorBenchmarking;

internal static class DapperBenchmarkHarnessBuilder
{
    public static BenchmarkSource Build(
        IReadOnlyList<ConversionSource> sources,
        string connectionString)
    {
        // Parse entity definitions up front so the generated harness can compile them alongside the benchmark.
        var entityInfos = ExtractEntityInfos(sources, connectionString);
        if (entityInfos.Count == 0)
        {
            throw new InvalidOperationException("Dapper harness requires at least one entity definition.");
        }

        // Dapper is fed with a single translated query. Since decision 025 the builder emits
        // the bare SQL beside the method, so the harness takes that instead of digging the
        // literal back out of the generated code with a regex.
        var sqlSource = sources
            .FirstOrDefault(s => s.ContentType == ConversionContentType.SqlQuery)?.Content
            ?? throw new InvalidOperationException("Dapper harness requires a query definition.");
        var querySource = sources
            .FirstOrDefault(s => s.ContentType == ConversionContentType.CSharpQuery)?.Content
            ?? string.Empty;
        querySource = NormalizeQuerySource(querySource);

        var ns = "DynamicBenchmarks.Generated";
        var typeName = $"DapperBenchmark_{Guid.NewGuid():N}";

        var sb = new StringBuilder();
        var usingSet = new HashSet<string>(StringComparer.Ordinal)
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "Microsoft.Data.SqlClient",
            "Dapper"
        };
        foreach (var entityUsing in entityInfos.SelectMany(e => e.Usings))
        {
            usingSet.Add(entityUsing.Replace(";", string.Empty).Trim());
        }
        foreach (var distinctNs in entityInfos.Select(e => e.Namespace).Where(n => n != null).Distinct())
        {
            usingSet.Add(distinctNs!);
        }
        foreach (var import in usingSet.OrderBy(u => u, StringComparer.Ordinal))
        {
            sb.AppendLine($"using {import};");
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
        sb.AppendLine(BuildDapperQueryMethod(querySource, sqlSource, entityInfos));
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

    private static string BuildDapperQueryMethod(string querySource, string sqlSource, IReadOnlyList<EntityInfo> entityInfos)
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

        string sqlBody = sqlSource.ReplaceLineEndings("\n").Trim();

        string primaryTable = entityInfos.FirstOrDefault()?.TableName ?? resultType;
        // Alias handling is delegated to the translator; we just swap placeholder tokens for the resolved table name.
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
}
