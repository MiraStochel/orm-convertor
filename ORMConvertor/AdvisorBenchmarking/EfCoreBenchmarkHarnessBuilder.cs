using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Model;
using static AdvisorBenchmarking.HarnessGenerationUtilities;
using EntityInfo = AdvisorBenchmarking.HarnessGenerationUtilities.EntityInfo;

namespace AdvisorBenchmarking;

internal static class EfCoreBenchmarkHarnessBuilder
{
    public static BenchmarkSource Build(
        IReadOnlyList<ConversionSource> sources,
        string connectionString)
    {
        // Entities are shared across translated outputs. We load them once so the DbContext can register each type.
        var entityInfos = ExtractEntityInfos(sources, connectionString);
        if (entityInfos.Count == 0)
        {
            throw new InvalidOperationException("EF Core harness requires at least one entity definition.");
        }

        // Translations emit query helper types (static methods, etc.). Gather them, keeping track of any extra usings.
        var rawQuerySources = ExtractQuerySources(sources);
        if (rawQuerySources.Count == 0)
        {
            throw new InvalidOperationException("EF Core harness requires at least one query definition.");
        }

        var additionalUsings = new HashSet<string>(StringComparer.Ordinal);
        var queryBodies = new List<string>();
        foreach (var rawSource in rawQuerySources)
        {
            var lines = rawSource.Split('\n');
            var bodyLines = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("using ", StringComparison.Ordinal))
                {
                    additionalUsings.Add(trimmed.TrimEnd(';'));
                    continue;
                }

                bodyLines.Add(line);
            }

            var body = string.Join("\n", bodyLines).Trim();
            if (!string.IsNullOrWhiteSpace(body))
            {
                queryBodies.Add(body);
            }
        }

        var ns = "DynamicBenchmarks.Generated";
        var typeName = $"EfCoreBenchmark_{Guid.NewGuid():N}";

        var sb = new StringBuilder();
        var usingSet = new HashSet<string>(StringComparer.Ordinal);
        void AddUsing(string nsName)
        {
            var normalized = nsName.Trim().TrimEnd(';');
            if (usingSet.Add(normalized))
            {
                sb.AppendLine($"using {normalized};");
            }
        }

        AddUsing("System");
        AddUsing("System.Collections");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("System.Reflection");
        AddUsing("System.Threading.Tasks");
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Metadata.Builders");
        foreach (var distinctNs in entityInfos.Select(e => e.Namespace).Where(n => n != null).Distinct())
        {
            AddUsing(distinctNs!);
        }
        foreach (var usingDirective in additionalUsings.OrderBy(u => u, StringComparer.Ordinal))
        {
            var nsName = usingDirective.StartsWith("using ", StringComparison.Ordinal)
                ? usingDirective["using ".Length..]
                : usingDirective;
            AddUsing(nsName);
        }
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {typeName}");
        sb.AppendLine("    {");
        sb.AppendLine($"        private const string ConnectionString = @\"{EscapeVerbatim(connectionString)}\";");
        sb.AppendLine("        private BenchmarkDbContext context = default!;");
        // Cache resolver output so we only reflect once per harness instance.
        sb.AppendLine("        private static readonly QueryBinding CachedQuery = ResolveQueryBinding();");
        sb.AppendLine();
        sb.AppendLine("        public void Setup()");
        sb.AppendLine("        {");
        sb.AppendLine("            var options = new DbContextOptionsBuilder<BenchmarkDbContext>()");
        sb.AppendLine("                .UseSqlServer(ConnectionString)");
        sb.AppendLine("                .EnableThreadSafetyChecks(false)");
        sb.AppendLine("                .Options;");
        sb.AppendLine("            context = new BenchmarkDbContext(options);");
        sb.AppendLine("            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public void Cleanup()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (context is null) { return; }");
        sb.AppendLine("            context.Dispose();");
        sb.AppendLine("            context = null!;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public int Execute()");
        sb.AppendLine("        {");
        sb.AppendLine("            return Query().Count;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public List<object> Query()");
        sb.AppendLine("        {");
        sb.AppendLine("            // Invoke the translated query method (static or instance) against our DbContext.");
        sb.AppendLine("            var result = CachedQuery.Method.Invoke(CachedQuery.Target, new object[] { context });");
        sb.AppendLine("            return Materialize(result);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static List<object> Materialize(object? value)");
        sb.AppendLine("        {");
        sb.AppendLine("            // Support synchronous IQueryable/IEnumerable as well as async Task/ValueTask shapes.");
        sb.AppendLine("            if (value is null)");
        sb.AppendLine("            {");
        sb.AppendLine("                return new List<object>();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (value is Task task)");
        sb.AppendLine("            {");
        sb.AppendLine("                task.GetAwaiter().GetResult();");
        sb.AppendLine("                var taskType = task.GetType();");
        sb.AppendLine("                if (taskType.IsGenericType)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var resultProperty = taskType.GetProperty(\"Result\");");
        sb.AppendLine("                    var taskResult = resultProperty?.GetValue(task);");
        sb.AppendLine("                    return Materialize(taskResult);");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                return new List<object>();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            var valueType = value.GetType();");
        sb.AppendLine("            if (valueType.IsValueType && valueType.FullName is string fullName && fullName.StartsWith(\"System.Threading.Tasks.ValueTask\", StringComparison.Ordinal))");
        sb.AppendLine("            {");
        sb.AppendLine("                var awaiter = valueType.GetMethod(\"GetAwaiter\")?.Invoke(value, null);");
        sb.AppendLine("                if (awaiter != null)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var awaiterType = awaiter.GetType();");
        sb.AppendLine("                    var getResult = awaiterType.GetMethod(\"GetResult\");");
        sb.AppendLine("                    var result = getResult?.Invoke(awaiter, null);");
        sb.AppendLine("                    if (valueType.IsGenericType)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        return Materialize(result);");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine("                    return new List<object>();");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (value is IQueryable queryable)");
        sb.AppendLine("            {");
        sb.AppendLine("                return queryable.Cast<object>().ToList();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (value is IEnumerable enumerable)");
        sb.AppendLine("            {");
        sb.AppendLine("                var list = new List<object>();");
        sb.AppendLine("                foreach (var item in enumerable)");
        sb.AppendLine("                {");
        sb.AppendLine("                    list.Add(item!);");
        sb.AppendLine("                }");
        sb.AppendLine("                return list;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return new List<object> { value };");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static bool IsSupportedReturnType(Type type)");
        sb.AppendLine("        {");
        sb.AppendLine("            // We accept LINQ pipelines (IQueryable), materialized enumerables, or async wrappers thereof.");
        sb.AppendLine("            if (typeof(IQueryable).IsAssignableFrom(type) || typeof(IEnumerable).IsAssignableFrom(type))");
        sb.AppendLine("            {");
        sb.AppendLine("                return true;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (typeof(Task).IsAssignableFrom(type))");
        sb.AppendLine("            {");
        sb.AppendLine("                return true;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return type.IsValueType && type.FullName is string fullName && fullName.StartsWith(\"System.Threading.Tasks.ValueTask\", StringComparison.Ordinal);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static QueryBinding ResolveQueryBinding()");
        sb.AppendLine("        {");
        sb.AppendLine("            // Find the generated query helper emitted by translation: public method with DbContext parameter.");
        sb.AppendLine("            var assembly = typeof(BenchmarkDbContext).Assembly;");
        sb.AppendLine("            var benchmarkType = typeof(" + typeName + ");");
        sb.AppendLine();
        sb.AppendLine("            foreach (var type in assembly.GetTypes())");
        sb.AppendLine("            {");
        sb.AppendLine("                if (type == benchmarkType || type == typeof(BenchmarkDbContext))");
        sb.AppendLine("                {");
        sb.AppendLine("                    continue;");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))");
        sb.AppendLine("                {");
        sb.AppendLine("                    if (!IsSupportedReturnType(method.ReturnType))");
        sb.AppendLine("                    {");
        sb.AppendLine("                        continue;");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine("                    var parameters = method.GetParameters();");
        sb.AppendLine("                    if (parameters.Length != 1)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        continue;");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine("                    if (!typeof(DbContext).IsAssignableFrom(parameters[0].ParameterType))");
        sb.AppendLine("                    {");
        sb.AppendLine("                        continue;");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine("                    object? target = null;");
        sb.AppendLine("                    if (!method.IsStatic)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        var ctor = type.GetConstructor(Type.EmptyTypes);");
        sb.AppendLine("                        if (ctor is null)");
        sb.AppendLine("                        {");
        sb.AppendLine("                            continue;");
        sb.AppendLine("                        }");
        sb.AppendLine();
        sb.AppendLine("                        target = ctor.Invoke(null);");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine("                    return new QueryBinding(target, method);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            throw new InvalidOperationException(\"EF Core query method not found. Ensure the translation produces a public method that accepts DbContext.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private sealed class QueryBinding");
        sb.AppendLine("        {");
        sb.AppendLine("            public QueryBinding(object? target, MethodInfo method)");
        sb.AppendLine("            {");
        sb.AppendLine("                Target = target;");
        sb.AppendLine("                Method = method;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public object? Target { get; }");
        sb.AppendLine("            public MethodInfo Method { get; }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private sealed class BenchmarkDbContext : DbContext");
        sb.AppendLine("        {");
        sb.AppendLine("            // Thin dynamic DbContext that maps translated entity types to the right table/schema.");
        sb.AppendLine("            public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : base(options)");
        sb.AppendLine("            {");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!optionsBuilder.IsConfigured)");
        sb.AppendLine("                {");
        sb.AppendLine("                    optionsBuilder.UseSqlServer(ConnectionString);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            protected override void OnModelCreating(ModelBuilder modelBuilder)");
        sb.AppendLine("            {");
        sb.AppendLine("                base.OnModelCreating(modelBuilder);");
        foreach (var entity in entityInfos)
        {
            var qualifiedType = GetQualifiedTypeName(entity);
            var tableName = entity.TableName;
            if (tableName.Contains('.', StringComparison.Ordinal))
            {
                var parts = tableName.Split('.', 2);
                var schema = parts[0];
                var name = parts[1];
                sb.AppendLine($"                modelBuilder.Entity<{qualifiedType}>(builder => builder.ToTable(\"{EscapeVerbatim(name)}\", \"{EscapeVerbatim(schema)}\"));");
            }
            else
            {
                sb.AppendLine($"                modelBuilder.Entity<{qualifiedType}>(builder => builder.ToTable(\"{EscapeVerbatim(tableName)}\"));");
            }
        }
        sb.AppendLine("            }");
        sb.AppendLine();
        foreach (var entity in entityInfos)
        {
            var qualifiedType = GetQualifiedTypeName(entity);
            var propertyName = GetDbSetPropertyName(entity);
            sb.AppendLine($"            public DbSet<{qualifiedType}> {propertyName} => Set<{qualifiedType}>();");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var body in queryBodies)
        {
            sb.AppendLine(body);
            sb.AppendLine();
        }

        foreach (var entity in entityInfos)
        {
            sb.AppendLine(NormalizeEntitySource(entity.Source));
            sb.AppendLine();
        }

        return new BenchmarkSource(ns, typeName, sb.ToString());
    }
}
