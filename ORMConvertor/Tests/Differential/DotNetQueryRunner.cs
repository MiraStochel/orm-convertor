using System.Collections;
using System.Data;
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
    public static List<string> Run(
        DifferentialQuery query,
        ORMEnum source,
        ORMEnum target,
        TestSchemaFixture fixture,
        string? mutated = null)
    {
        var conversion = Translate(query, source, target, fixture);
        var rows = Execute(query, source, target, conversion, fixture, mutated);
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

    /// <summary>The generated query artifact, or the mutation of it a negative case supplies.</summary>
    public static string QueryMethod(ConversionResult conversion, string? mutated = null)
        => mutated ?? conversion.Sources
            .Single(source => source.ContentType == ConversionContentType.CSharpQuery)
            .Content;

    private static IEnumerable<string> EntitySources(ConversionResult conversion)
        => conversion.Sources
            .Where(source => source.ContentType == ConversionContentType.CSharpEntity)
            .Select(source => source.Content);

    /// <summary>
    /// The usings a consumer project would put around the query method: the framework's own
    /// plus the namespace the generated entities declare. The artifact names the entity type
    /// unqualified - it is written for a project that has it in scope - so without this the
    /// compilation fails on the type the query is about.
    /// </summary>
    private static string Usings(ConversionResult conversion, string frameworkUsings)
    {
        var declarations = EntitySources(conversion)
            .Select(source => System.Text.RegularExpressions.Regex.Match(source, @"^\s*namespace\s+([\w.]+)\s*;", System.Text.RegularExpressions.RegexOptions.Multiline))
            .Where(match => match.Success)
            .Select(match => $"using {match.Groups[1].Value};")
            .Distinct();

        return string.Join(Environment.NewLine, declarations.Prepend(frameworkUsings));
    }

    private static List<object?[]> Execute(
        DifferentialQuery query,
        ORMEnum source,
        ORMEnum target,
        ConversionResult conversion,
        TestSchemaFixture fixture,
        string? mutated)
        => target switch
        {
            ORMEnum.Dapper => RunDapper(query, source, conversion, fixture, mutated),
            ORMEnum.EFCore => RunEFCore(query, source, conversion, fixture, mutated),
            ORMEnum.NHibernate => RunNHibernate(query, source, conversion, fixture, mutated),
            _ => throw new NotSupportedException(
                $"{target} is not a framework this suite runs; its half of the matrix is the Java suite's."),
        };

    private static List<object?[]> RunDapper(
        DifferentialQuery query, ORMEnum source, ConversionResult conversion, TestSchemaFixture fixture, string? mutated)
    {
        var compiled = GeneratedQueryCompiler.CompileOrFail(
            AssemblyName(query, source, ORMEnum.Dapper),
            QueryMethod(conversion, mutated),
            EntitySources(conversion),
            GeneratedQueryCompiler.DapperConsumerReferences,
            Usings(conversion, "using Dapper;" + Environment.NewLine + "using System.Data;"));

        var assembly = Assembly.Load(compiled);

        using var connection = fixture.OpenConnection();
        var returned = Invoke(assembly, query, connection);

        // Dapper materializes a whole-entity query into the generated entity type and a
        // projection into its own untyped row, a dictionary keyed by the columns the
        // projection named (decision 104); both are therefore read by name.
        return Rows(returned, query, positional: false);
    }

    private static List<object?[]> RunEFCore(
        DifferentialQuery query, ORMEnum source, ConversionResult conversion, TestSchemaFixture fixture, string? mutated)
    {
        var compiled = GeneratedQueryCompiler.CompileOrFail(
            AssemblyName(query, source, ORMEnum.EFCore),
            QueryMethod(conversion, mutated),
            EntitySources(conversion),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            Usings(conversion, "using Microsoft.EntityFrameworkCore;"));

        var assembly = Assembly.Load(compiled);

        using var connection = fixture.OpenConnection();
        using var context = EFCoreAcceptance.OpenContext(assembly, connection);

        var returned = Invoke(assembly, query, context);

        // A projection is an anonymous type whose members carry the projected names, so
        // this side reads by name whichever shape the query has.
        return Rows(returned, query, positional: false);
    }

    private static List<object?[]> RunNHibernate(
        DifferentialQuery query, ORMEnum source, ConversionResult conversion, TestSchemaFixture fixture, string? mutated)
    {
        var mappings = conversion.Sources
            .Where(s => s.ContentType == ConversionContentType.XML)
            .Select(s => s.Content)
            .ToList();

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            AssemblyName(query, source, ORMEnum.NHibernate),
            QueryMethod(conversion, mutated),
            EntitySources(conversion),
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            Usings(conversion, "using NHibernate;"));

        List<object?[]> rows = [];

        NHibernateAcceptance.UseSessionFactory(compiled, mappings, (factory, assembly) =>
        {
            using var connection = fixture.OpenConnection();
            using var session = factory.WithOptions().Connection(connection).OpenSession();

            var returned = Invoke(assembly, query, session);
            var list = ((IQuery)returned!).List();

            // HQL hands a projection back as an object array per row and an entity as the
            // instance itself, so the shape of the query decides how a row is read.
            rows = Rows(list, query, positional: query.Projection);
        });

        return rows;
    }

    /// <summary>
    /// Calls the one generated method. Its first parameter is the framework's handle - a
    /// connection, a context, a session - and the parameters of the query follow it
    /// (decision 083); the matrix states their values.
    /// </summary>
    private static object? Invoke(Assembly assembly, DifferentialQuery query, object handle)
    {
        var method = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

        var parameters = method.GetParameters();

        Assert.True(
            parameters.Length == query.Arguments.Count + 1,
            $"{query.Id}: the generated method takes {parameters.Length} parameters, and the matrix "
            + $"binds {query.Arguments.Count} beside the framework's handle.");

        object?[] arguments =
        [
            handle,
            .. query.Arguments.Select((argument, index) => Argument(argument.Materialize(), parameters[index + 1].ParameterType)),
        ];

        return method.Invoke(null, arguments);
    }

    /// <summary>
    /// The materialized value as the parameter wants it: as it is where it already fits -
    /// an array where the method takes an IEnumerable of the element -, converted where the
    /// scalar is spelled with another width.
    /// </summary>
    private static object Argument(object value, Type parameterType)
    {
        if (parameterType.IsInstanceOfType(value))
        {
            return value;
        }

        return Convert.ChangeType(
            value,
            Nullable.GetUnderlyingType(parameterType) ?? parameterType,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<object?[]> Rows(object? returned, DifferentialQuery query, bool positional)
    {
        if (returned is not IEnumerable sequence)
        {
            throw new InvalidOperationException(
                $"{query.Id}: the generated method returned {returned?.GetType().Name ?? "null"}, "
                + "which is not a sequence of rows.");
        }

        List<object?[]> rows = [];

        foreach (var item in sequence)
        {
            rows.Add(positional ? Positional(item, query) : ByName(item, query));
        }

        return rows;
    }

    private static object?[] Positional(object? item, DifferentialQuery query)
    {
        // A single projected field comes back bare rather than as a one-element array.
        var values = item as object?[] ?? [item];

        Assert.True(
            values.Length == query.Fields.Count,
            $"{query.Id}: a row came back with {values.Length} fields and the matrix states {query.Fields.Count}.");

        return values;
    }

    /// <summary>
    /// The fields of a row read by the names the matrix states: off a dictionary for
    /// Dapper's untyped row of a projection (decision 104), off the properties for an entity
    /// or an anonymous type.
    /// </summary>
    private static object?[] ByName(object? item, DifferentialQuery query)
    {
        if (item is IDictionary<string, object?> columns)
        {
            return [.. query.Fields.Select(field =>
                columns.TryGetValue(field, out var value)
                    ? value
                    : throw new InvalidOperationException(
                        $"{query.Id}: the row has no column \"{field}\"; it has {string.Join(", ", columns.Keys)} "
                        + $"and the matrix states the fields as {string.Join(", ", query.Fields)}."))];
        }

        var type = item?.GetType()
            ?? throw new InvalidOperationException($"{query.Id}: a row of the result is null.");

        return [.. query.Fields.Select(field =>
            (type.GetProperty(field)
                ?? throw new InvalidOperationException(
                    $"{query.Id}: the row type {type.Name} has no member \"{field}\"; the matrix states "
                    + $"the fields as {string.Join(", ", query.Fields)}."))
            .GetValue(item))];
    }

    /// <summary>
    /// A name of its own per query, source and target. Assemblies are loaded into the test
    /// process and never unloaded, so two runs sharing a name would be two different images
    /// under one name - and NHibernate resolves persistent classes by assembly name.
    /// </summary>
    private static string AssemblyName(DifferentialQuery query, ORMEnum source, ORMEnum target)
        => $"Differential_{query.Id.Replace('-', '_')}_{source}_{target}";
}
