using System.Collections;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Model;
using NHibernate;
using OrmConvertor;
using Tests.Database;
using Tests.Verification;

namespace Tests.Differential;

/// <summary>
/// Runs a generated query against the fixture and renders what came back (decision 089).
/// This is the fourth verification level of decision 016 applied to a query: until now no
/// generated .NET query was executed at all - <see cref="QueryVerificationTest"/> stops at
/// the third level, which is dry on purpose - so the compile-and-run half is new here.
///
/// It owns the three .NET frameworks. The other three are the Java suite's, and the two
/// never meet in a process: they meet over the canonical result, because the decision
/// refused to grow an endpoint that would run foreign code.
/// </summary>
internal static class DotNetQueryRunner
{
    /// <summary>Whether this suite can run a query of that framework at all.</summary>
    public static bool Owns(ORMEnum framework)
        => framework is ORMEnum.Dapper or ORMEnum.EFCore or ORMEnum.NHibernate;

    /// <summary>
    /// Translates the query from the source into the target and runs it, returning the
    /// rendered rows in the order the comparison wants them: as they came for a query that
    /// carries an ordering, sorted by the rendered line for one that does not.
    /// </summary>
    /// <param name="form">The artifact run: the binding method of the query, or the LINQ form NHibernate writes beside its HQL (decision 118).</param>
    public static List<string> Run(
        DifferentialQuery query,
        ORMEnum source,
        ORMEnum target,
        TestSchemaFixture fixture,
        string? mutated = null,
        ConversionContentType form = ConversionContentType.CSharpQuery)
    {
        var conversion = Translate(query, source, target, fixture);

        using var prepared = PreparedQuery.Prepare(
            AssemblyName(query, source, target) + (form == ConversionContentType.CSharpQuery ? string.Empty : "_" + form),
            target,
            conversion,
            TestDatabase.ConnectionString!,
            mutated,
            form);

        // The matrix states the arguments in the order of the method's parameters.
        var rows = prepared.Run(query.Id, [.. query.Arguments.Select(argument => argument.Materialize())], query.Fields, query.Projection);
        var rendered = rows.Select(row => ResultRow.Render(row, query.Settings));

        return query.Ordered ? [.. rendered] : ResultRow.Sorted(rendered);
    }

    /// <summary>
    /// The conversion behind a run. The catalog reader is handed in because two sources -
    /// Dapper and MyBatis - state nothing but property names and need the catalog to become
    /// a mapping at all (decision 015, F6); it is also what types their parameters, so the
    /// refusal the dry matrices state for a Dapper parameter (decision 083) does not arise
    /// here.
    /// </summary>
    public static ConversionResult Translate(DifferentialQuery query, ORMEnum source, ORMEnum target, TestSchemaFixture fixture)
    {
        var conversion = ConversionHandler.Convert(source, target, query.Units(source), fixture.CatalogReader);

        var refusals = conversion.Records
            .Where(record => record.Kind == AbstractWrappers.Diagnostics.ConversionRecordKind.Failure)
            .ToList();

        Assert.True(
            refusals.Count == 0,
            $"{query.Id}: {source} -> {target} was refused, so the pair has nothing to compare:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, refusals.Select(record => record.Reason)));

        return conversion;
    }

    /// <summary>The generated query artifact - the binding method, or the form asked for (decision 118) -, or the mutation of it a negative case supplies.</summary>
    public static string QueryMethod(ConversionResult conversion, string? mutated = null, ConversionContentType form = ConversionContentType.CSharpQuery)
        => mutated ?? conversion.Sources
            .Single(source => source.ContentType == form)
            .Content;

    /// <summary>Whether the conversion carries the form - a second form is written only where its language speaks the query (decision 118).</summary>
    public static bool Carries(ConversionResult conversion, ConversionContentType form)
        => conversion.Sources.Any(source => source.ContentType == form);

    /// <summary>
    /// A name of its own per query, source and target. Assemblies are loaded into the test
    /// process and never unloaded, so two runs sharing a name would be two different images
    /// under one name - and NHibernate resolves persistent classes by assembly name.
    /// </summary>
    private static string AssemblyName(DifferentialQuery query, ORMEnum source, ORMEnum target)
        => $"Differential_{query.Id.Replace('-', '_')}_{source}_{target}";
}

/// <summary>
/// A generated query compiled once and run as often as its caller needs: the differential
/// matrix runs each artifact once, the replay of the LDBC validation set (decision 117) runs
/// one artifact for every read of its operation, thousands of times, so the compilation, the
/// EF Core model and the NHibernate factory are built here once and only the run repeats.
/// A run takes a connection of its own from the pool - EF Core opens and closes the
/// connection of its context around every query -, so nothing a run does stays open after it.
/// </summary>
internal sealed class PreparedQuery : IDisposable
{
    private readonly ORMEnum target;
    private readonly string connectionString;
    private readonly MethodInfo method;
    private readonly SqlConnection? contextConnection;
    private readonly DbContext? context;
    private readonly ISessionFactory? sessionFactory;
    private readonly ConversionContentType form;

    private PreparedQuery(
        ORMEnum target,
        string connectionString,
        MethodInfo method,
        SqlConnection? contextConnection = null,
        DbContext? context = null,
        ISessionFactory? sessionFactory = null,
        ConversionContentType form = ConversionContentType.CSharpQuery)
    {
        this.target = target;
        this.connectionString = connectionString;
        this.method = method;
        this.contextConnection = contextConnection;
        this.context = context;
        this.sessionFactory = sessionFactory;
        this.form = form;
    }

    /// <summary>The parameters of the query, after the framework's handle (decision 083).</summary>
    public IReadOnlyList<ParameterInfo> Parameters => method.GetParameters()[1..];

    /// <summary>
    /// Compiles the generated query - or the mutation of it a negative case supplies - with
    /// the entities of the conversion, under an assembly name of its own: assemblies are loaded
    /// into the test process and never unloaded, so two compilations sharing a name would be
    /// two different images under one name, and NHibernate resolves persistent classes by
    /// assembly name. The run is the framework's own, over the given database.
    /// </summary>
    /// <param name="form">The artifact compiled: the binding method, or the LINQ form NHibernate writes beside its HQL (decision 118), which a consumer project imports <c>NHibernate.Linq</c> for.</param>
    public static PreparedQuery Prepare(
        string assemblyName,
        ORMEnum target,
        ConversionResult conversion,
        string connectionString,
        string? mutated = null,
        ConversionContentType form = ConversionContentType.CSharpQuery)
    {
        var entities = EntitySources(conversion).ToList();
        var queryMethod = DotNetQueryRunner.QueryMethod(conversion, mutated, form);

        switch (target)
        {
            case ORMEnum.Dapper:
            {
                var compiled = GeneratedQueryCompiler.CompileOrFail(
                    assemblyName,
                    queryMethod,
                    entities,
                    GeneratedQueryCompiler.DapperConsumerReferences,
                    Usings(conversion, "using Dapper;" + Environment.NewLine + "using System.Data;"));

                return new PreparedQuery(target, connectionString, GeneratedMethod(Assembly.Load(compiled)));
            }

            case ORMEnum.EFCore:
            {
                var compiled = GeneratedQueryCompiler.CompileOrFail(
                    assemblyName,
                    queryMethod,
                    entities,
                    GeneratedQueryCompiler.EFCoreConsumerReferences,
                    Usings(conversion, "using Microsoft.EntityFrameworkCore;"));

                var assembly = Assembly.Load(compiled);
                var connection = new SqlConnection(connectionString);

                return new PreparedQuery(
                    target, connectionString, GeneratedMethod(assembly), connection, EFCoreAcceptance.OpenContext(assembly, connection));
            }

            case ORMEnum.NHibernate:
            {
                var mappings = conversion.Sources
                    .Where(source => source.ContentType == ConversionContentType.XML)
                    .Select(source => source.Content)
                    .ToList();

                var compiled = GeneratedQueryCompiler.CompileOrFail(
                    assemblyName,
                    queryMethod,
                    entities,
                    GeneratedQueryCompiler.NHibernateConsumerReferences,
                    Usings(conversion, form == ConversionContentType.CSharpLinqQuery
                        ? "using NHibernate;" + Environment.NewLine + "using NHibernate.Linq;"
                        : "using NHibernate;"));

                var (factory, assembly) = NHibernateAcceptance.OpenSessionFactory(compiled, mappings);

                // The provider of the LINQ form registers the entity types it meets once per
                // process, by name (NHibernateTypeRegistry); the names recur across the
                // assemblies this suite compiles.
                if (form == ConversionContentType.CSharpLinqQuery)
                {
                    NHibernateTypeRegistry.Forget(assembly);
                }

                return new PreparedQuery(target, connectionString, GeneratedMethod(assembly), sessionFactory: factory, form: form);
            }

            default:
                throw new NotSupportedException(
                    $"{target} is not a framework this suite runs; its half of the matrix is the Java suite's.");
        }
    }

    /// <summary>
    /// Runs the query with the arguments in the order of its parameters and returns the rows,
    /// each with the fields read in the order given. Dapper materializes a whole-entity query
    /// into the generated entity type and a projection into its own untyped row, a dictionary
    /// keyed by the columns the projection named (decision 104); EF Core projects into an
    /// anonymous type whose members carry the projected names; both are read by name. HQL hands
    /// a projection back as an object array per row and an entity as the instance itself, so
    /// for NHibernate the shape of the query decides how a row is read; its LINQ form (decision
    /// 118) returns a queryable of anonymous instances or entities, read by name as EF Core's.
    /// </summary>
    public List<object?[]> Run(string id, IReadOnlyList<object?> arguments, IReadOnlyList<string> fields, bool projection)
    {
        switch (target)
        {
            case ORMEnum.Dapper:
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();

                return Rows(Invoke(id, connection, arguments), id, fields, positional: false);
            }

            case ORMEnum.EFCore:
                return Rows(Invoke(id, context!, arguments), id, fields, positional: false);

            default:
            {
                using var connection = new SqlConnection(connectionString);
                connection.Open();
                using var session = sessionFactory!.WithOptions().Connection(connection).OpenSession();

                // The HQL method returns the IQuery to list; the LINQ form the queryable itself,
                // which runs as it is enumerated, inside the session (decision 118).
                var returned = Invoke(id, session, arguments);
                var rows = returned is IQuery query ? query.List() : returned;
                return Rows(rows, id, fields, positional: projection && form == ConversionContentType.CSharpQuery);
            }
        }
    }

    public void Dispose()
    {
        context?.Dispose();
        contextConnection?.Dispose();
        sessionFactory?.Dispose();
    }

    internal static IEnumerable<string> EntitySources(ConversionResult conversion)
        => conversion.Sources
            .Where(source => source.ContentType == ConversionContentType.CSharpEntity)
            .Select(source => source.Content);

    /// <summary>
    /// The usings a consumer project would put around the query method: the framework's own
    /// plus the namespace the generated entities declare. The artifact names the entity type
    /// unqualified - it is written for a project that has it in scope - so without this the
    /// compilation fails on the type the query is about.
    /// </summary>
    internal static string Usings(ConversionResult conversion, string frameworkUsings)
    {
        var declarations = EntitySources(conversion)
            .Select(source => System.Text.RegularExpressions.Regex.Match(source, @"^\s*namespace\s+([\w.]+)\s*;", System.Text.RegularExpressions.RegexOptions.Multiline))
            .Where(match => match.Success)
            .Select(match => $"using {match.Groups[1].Value};")
            .Distinct();

        return string.Join(Environment.NewLine, declarations.Prepend(frameworkUsings));
    }

    /// <summary>The one generated method. Its first parameter is the framework's handle - a connection, a context, a session.</summary>
    private static MethodInfo GeneratedMethod(Assembly assembly)
        => assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

    /// <summary>Calls the generated method with the handle in front of the parameters of the query (decision 083).</summary>
    private object? Invoke(string id, object handle, IReadOnlyList<object?> arguments)
    {
        var parameters = method.GetParameters();

        Assert.True(
            parameters.Length == arguments.Count + 1,
            $"{id}: the generated method takes {parameters.Length} parameters, and the caller "
            + $"binds {arguments.Count} beside the framework's handle.");

        object?[] values =
        [
            handle,
            .. arguments.Select((argument, index) => Argument(argument, parameters[index + 1].ParameterType)),
        ];

        return method.Invoke(null, values);
    }

    /// <summary>
    /// The value as the parameter wants it: as it is where it already fits - an array where
    /// the method takes an IEnumerable of the element -, converted where the scalar is spelled
    /// with another width, and a moment at midnight as the date a DateOnly parameter is.
    /// </summary>
    private static object? Argument(object? value, Type parameterType)
    {
        if (value is null || parameterType.IsInstanceOfType(value))
        {
            return value;
        }

        var type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

        if (type == typeof(DateOnly) && value is DateTime moment && moment.TimeOfDay == TimeSpan.Zero)
        {
            return DateOnly.FromDateTime(moment);
        }

        return Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<object?[]> Rows(object? returned, string id, IReadOnlyList<string> fields, bool positional)
    {
        if (returned is not IEnumerable sequence)
        {
            throw new InvalidOperationException(
                $"{id}: the generated method returned {returned?.GetType().Name ?? "null"}, "
                + "which is not a sequence of rows.");
        }

        List<object?[]> rows = [];

        foreach (var item in sequence)
        {
            rows.Add(positional ? Positional(item, id, fields) : ByName(item, id, fields));
        }

        return rows;
    }

    private static object?[] Positional(object? item, string id, IReadOnlyList<string> fields)
    {
        // A single projected field comes back bare rather than as a one-element array.
        var values = item as object?[] ?? [item];

        Assert.True(
            values.Length == fields.Count,
            $"{id}: a row came back with {values.Length} fields and the caller states {fields.Count}.");

        return values;
    }

    /// <summary>
    /// The fields of a row read by the names the caller states: off a dictionary for
    /// Dapper's untyped row of a projection (decision 104), off the properties for an entity
    /// or an anonymous type.
    /// </summary>
    private static object?[] ByName(object? item, string id, IReadOnlyList<string> fields)
    {
        if (item is IDictionary<string, object?> columns)
        {
            return [.. fields.Select(field =>
                columns.TryGetValue(field, out var value)
                    ? value
                    : throw new InvalidOperationException(
                        $"{id}: the row has no column \"{field}\"; it has {string.Join(", ", columns.Keys)} "
                        + $"and the caller states the fields as {string.Join(", ", fields)}."))];
        }

        var type = item?.GetType()
            ?? throw new InvalidOperationException($"{id}: a row of the result is null.");

        return [.. fields.Select(field =>
            (type.GetProperty(field)
                ?? throw new InvalidOperationException(
                    $"{id}: the row type {type.Name} has no member \"{field}\"; the caller states "
                    + $"the fields as {string.Join(", ", fields)}."))
            .GetValue(item))];
    }
}
