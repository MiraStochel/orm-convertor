using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using SampleData;

namespace Tests.LdbcJudge;

/// <summary>
/// The claim behind the five path queries of the LDBC catalog - IC 13, IC 14, BI 15, BI 19
/// and BI 20 -, which decision 140 writes as a chain of non-recursive definitions, one per
/// level of a breadth-first search, as deep as the data set measures. The chain of depth d
/// returns the cheapest path of at most d edges; it is the path the specification asks for
/// wherever no cheapest path of the data set has more edges than d. Two things are held
/// here over a loaded LdbcSnb, where the validation set of decision 117 reaches only the two
/// Interactive queries: that the text returns what the algorithm returns - a breadth-first
/// search or Bellman-Ford in this class, over edges read by SQL of its own, from the example
/// parameters and from persons, cities and companies picked out of the data -, and that the
/// depth the catalog states is the depth of the data set it names, measured from every
/// person. The oracle is an algorithm, not a tolerance written from the note, in the spirit
/// of decision 089. Without a configured LdbcSnb every test here skips with the reason.
/// </summary>
public class LdbcPathQueryTest(ITestOutputHelper output)
{
    private const double Tolerance = 1e-6;

    public static TheoryData<string> PathQueries() => [.. LdbcSnbSample.Queries.Where(query => query.SearchDepth is not null).Select(query => query.Key)];

    /// <summary>
    /// Every path query returns, for its example parameters and for parameters picked out of
    /// the data set, exactly what the algorithm of its specification returns over the edges of
    /// its own definition, bounded by the depth the text runs with.
    /// </summary>
    [Theory]
    [MemberData(nameof(PathQueries))]
    public void TheTextReturnsWhatTheAlgorithmReturns(string key)
    {
        using var connection = Open();
        var query = LdbcSnbSample.Queries.Single(candidate => candidate.Key == key);
        var depth = query.SearchDepth!.Levels;
        var graph = Graph.Load(connection, key, query);

        var cases = 0;
        foreach (var arguments in Cases(connection, key, query))
        {
            var actual = Rows(connection, query, arguments);
            var expected = Expected(connection, key, graph, depth, arguments);

            Assert.True(
                expected.SequenceEqual(actual),
                $"{key} with {Describe(arguments)}:{Environment.NewLine}  expected {string.Join(" | ", expected)}{Environment.NewLine}  actual   {string.Join(" | ", actual)}");
            cases++;
        }

        output.WriteLine($"{key}: {cases} parameter sets agree with the algorithm over {graph.Edges} edges, depth {depth}.");
    }

    /// <summary>
    /// The depth the catalog states for a path query is at least the depth of the data set
    /// the catalog names, measured from every person: the diameter of the graph the query
    /// walks for a search by the fewest steps, the most edges a cheapest path has for a search
    /// by weight. Over another data set the claim is not made, and the test says so instead
    /// of measuring a number the catalog does not state.
    /// </summary>
    [Theory]
    [MemberData(nameof(PathQueries))]
    public void TheDepthIsThatOfTheDataSet(string key)
    {
        using var connection = Open();
        var dataSet = connection.ExecuteScalar<string?>(
            "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'ldbc.dataset'");

        if (!string.Equals(dataSet, LdbcSnbSample.DataSet, StringComparison.Ordinal))
        {
            Assert.Skip($"The depths of the catalog are measured over {LdbcSnbSample.DataSet}; this LdbcSnb holds {dataSet ?? "no data set"}.");
        }

        var query = LdbcSnbSample.Queries.Single(candidate => candidate.Key == key);
        var graph = Graph.Load(connection, key, query);
        var (measured, pair) = graph.MostEdgesOnACheapestPath();

        output.WriteLine($"{key}: {measured} over {LdbcSnbSample.DataSet} ({pair.From} to {pair.To}), the text runs with {query.SearchDepth!.Levels}.");
        Assert.True(
            measured <= query.SearchDepth.Levels,
            $"{key} runs with {query.SearchDepth.Levels} levels, but a cheapest path of {LdbcSnbSample.DataSet} has {measured} edges ({pair.From} to {pair.To}).");
    }

    /// <summary>
    /// The connection the test reads through, holding the application lock of the replay
    /// (decision 117) so that no replay inserts into the tables while the text and the
    /// algorithm read them. Skips - or fails where the environment promised the judge - without
    /// a configured LdbcSnb.
    /// </summary>
    private static SqlConnection Open()
    {
        if (!LdbcDatabase.IsConfigured)
        {
            LdbcDatabase.Unavailable(LdbcDatabase.NotConfiguredReason, LdbcDatabase.IsRequired);
        }

        var connection = new SqlConnection(LdbcDatabase.ConnectionString);
        try
        {
            connection.Open();
        }
        catch (Exception exception)
        {
            connection.Dispose();
            LdbcDatabase.Unavailable($"The LDBC database \"{LdbcDatabase.ConnectionStringName}\" is configured but cannot be reached: {exception.Message}", LdbcDatabase.IsRequired);
        }

        connection.Execute(
            "DECLARE @granted INT; EXEC @granted = sp_getapplock @Resource = N'ldbc.validation', @LockMode = 'Shared', @LockOwner = 'Session', @LockTimeout = -1; "
            + "IF @granted < 0 THROW 50117, 'The application lock ldbc.validation was not granted.', 1;",
            commandTimeout: 0);
        return connection;
    }

    /// <summary>The rows the text returns, one string per row, in the order of the text.</summary>
    private static List<string> Rows(SqlConnection connection, LdbcQuery query, DynamicParameters arguments)
        => connection.Query(query.Sql!, arguments, commandTimeout: 900)
            .Select(row => Row(((IDictionary<string, object>)row).Values))
            .ToList();

    private static string Row(IEnumerable<object?> values)
        => string.Join(",", values.Select(value => value switch
        {
            null => "NULL",
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            decimal m => ((double)m).ToString("0.####", CultureInfo.InvariantCulture),
            float f => ((double)f).ToString("0.####", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        }));

    private static string Describe(DynamicParameters arguments)
        => string.Join(", ", arguments.ParameterNames.Select(name => $"@{name} = {arguments.Get<object>(name)}"));

    /// <summary>
    /// The parameter sets a query is run with: its example parameters, then persons, cities
    /// and companies taken from the data at fixed ranks, so that the cases are the same on
    /// every run and are not the pairs the example was chosen for.
    /// </summary>
    private static IEnumerable<DynamicParameters> Cases(SqlConnection connection, string key, LdbcQuery query)
    {
        yield return Example(query);

        var persons = connection.Query<long>(
            "SELECT x.Id FROM (SELECT p.Id AS Id, ROW_NUMBER() OVER (ORDER BY p.Id) AS Rank FROM Person AS p) AS x WHERE x.Rank % 823 = 1 ORDER BY x.Id").ToList();

        switch (key)
        {
            case "ic13":
            case "ic14":
                foreach (var (first, second) in Pairs(persons))
                {
                    yield return Arguments(("person1Id", first), ("person2Id", second));
                }

                yield return Arguments(("person1Id", persons[0]), ("person2Id", persons[0]));
                break;

            case "bi15":
                var example = query.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Example);
                foreach (var (first, second) in Pairs(persons))
                {
                    var arguments = Arguments(("person1Id", first), ("person2Id", second));
                    arguments.Add("startDate", DateTime.Parse(example["startDate"], CultureInfo.InvariantCulture), DbType.Date);
                    arguments.Add("endDate", DateTime.Parse(example["endDate"], CultureInfo.InvariantCulture), DbType.Date);
                    yield return arguments;
                }

                break;

            case "bi19":
                var cities = connection.Query<long>(
                    "SELECT TOP (4) p.LocationCityId FROM Person AS p GROUP BY p.LocationCityId ORDER BY COUNT(*) DESC, p.LocationCityId ASC").ToList();
                foreach (var (first, second) in Pairs(cities))
                {
                    yield return Arguments(("city1Id", first), ("city2Id", second));
                }

                break;

            case "bi20":
                var companies = connection.Query<string>(
                    "SELECT TOP (3) o.Name FROM Organisation AS o JOIN Person_workAt_Company AS w ON w.CompanyId = o.Id GROUP BY o.Name ORDER BY COUNT(*) DESC, o.Name ASC").ToList();
                foreach (var (company, person) in companies.Zip(persons))
                {
                    var arguments = new DynamicParameters();
                    arguments.Add("company", company, DbType.String);
                    arguments.Add("person2Id", person, DbType.Int64);
                    yield return arguments;
                }

                break;

            default:
                throw new InvalidOperationException($"{key} is no path query this test knows.");
        }
    }

    private static IEnumerable<(T First, T Second)> Pairs<T>(IReadOnlyList<T> items)
    {
        for (var i = 0; i + 1 < items.Count; i += 2)
        {
            yield return (items[i], items[i + 1]);
        }
    }

    private static DynamicParameters Arguments(params (string Name, long Value)[] values)
    {
        var arguments = new DynamicParameters();
        foreach (var (name, value) in values)
        {
            arguments.Add(name, value, DbType.Int64);
        }

        return arguments;
    }

    private static DynamicParameters Example(LdbcQuery query)
    {
        var arguments = new DynamicParameters();
        foreach (var parameter in query.Parameters)
        {
            var type = parameter.SqlType.Split('(')[0].ToUpperInvariant();
            var (value, dbType) = type switch
            {
                "BIGINT" => ((object)long.Parse(parameter.Example, CultureInfo.InvariantCulture), DbType.Int64),
                "DATE" => (DateTime.Parse(parameter.Example, CultureInfo.InvariantCulture), DbType.Date),
                "NVARCHAR" => (parameter.Example, DbType.String),
                _ => throw new InvalidOperationException($"{query.Key}: no binding for the type {parameter.SqlType}."),
            };
            arguments.Add(parameter.Name, value, dbType);
        }

        return arguments;
    }

    /// <summary>What the specification's algorithm returns for one parameter set, as the rows the text is expected to return.</summary>
    private static List<string> Expected(SqlConnection connection, string key, Graph graph, int depth, DynamicParameters arguments)
    {
        switch (key)
        {
            case "ic13":
            {
                var cost = graph.Cheapest([arguments.Get<long>("person1Id")], depth);
                var target = arguments.Get<long>("person2Id");
                return [Row([cost.TryGetValue(target, out var steps) ? (long)steps : -1L])];
            }

            case "ic14":
                return ShortestPaths(connection, graph, depth, arguments.Get<long>("person1Id"), arguments.Get<long>("person2Id"));

            case "bi15":
            {
                var cost = graph.Cheapest([arguments.Get<long>("person1Id")], depth);
                var target = arguments.Get<long>("person2Id");
                return [Row([cost.TryGetValue(target, out var total) ? total : -1.0])];
            }

            case "bi19":
            {
                var first = connection.Query<long>("SELECT p.Id FROM Person AS p WHERE p.LocationCityId = @city", new { city = arguments.Get<long>("city1Id") }).ToList();
                var second = connection.Query<long>("SELECT p.Id FROM Person AS p WHERE p.LocationCityId = @city", new { city = arguments.Get<long>("city2Id") }).ToHashSet();
                var pairs = new List<(long Person1, long Person2, double Weight)>();
                foreach (var source in first)
                {
                    foreach (var (person, weight) in graph.Cheapest([source], depth))
                    {
                        if (second.Contains(person))
                        {
                            pairs.Add((source, person, weight));
                        }
                    }
                }

                if (pairs.Count == 0)
                {
                    return [];
                }

                var cheapest = pairs.Min(pair => pair.Weight);
                return [.. pairs
                    .Where(pair => Math.Abs(pair.Weight - cheapest) <= Tolerance)
                    .OrderBy(pair => pair.Person1).ThenBy(pair => pair.Person2)
                    .Select(pair => Row([pair.Person1, pair.Person2, pair.Weight]))];
            }

            case "bi20":
            {
                var person = arguments.Get<long>("person2Id");
                var employees = connection.Query<long>(
                    "SELECT DISTINCT w.PersonId FROM Person_workAt_Company AS w JOIN Organisation AS o ON o.Id = w.CompanyId WHERE o.Name = @company",
                    new { company = arguments.Get<string>("company") }).ToHashSet();
                var cost = graph.Cheapest([person], depth);
                return [.. cost
                    .Where(reached => employees.Contains(reached.Key) && reached.Key != person)
                    .OrderBy(reached => reached.Value).ThenBy(reached => reached.Key)
                    .Take(20)
                    .Select(reached => Row([reached.Key, (long)reached.Value]))];
            }

            default:
                throw new InvalidOperationException($"{key} is no path query this test knows.");
        }
    }

    /// <summary>
    /// IC 14: every shortest path between the two persons, each with its weight over the
    /// replies between its neighbours, as the text writes them - the identifiers joined by
    /// commas, the heaviest first, equal weights by the path.
    /// </summary>
    private static List<string> ShortestPaths(SqlConnection connection, Graph graph, int depth, long first, long second)
    {
        var fromSecond = graph.Cheapest([second], depth);
        if (!fromSecond.TryGetValue(first, out var length))
        {
            return [];
        }

        var paths = new List<List<long>>();
        Extend([first], paths);

        void Extend(List<long> path, List<List<long>> found)
        {
            var last = path[^1];
            var position = path.Count - 1;
            if (position == (int)length)
            {
                if (last == second)
                {
                    found.Add(path);
                }

                return;
            }

            foreach (var (next, _) in graph.Neighbours(last))
            {
                if (fromSecond.TryGetValue(next, out var remaining) && (int)remaining == (int)length - position - 1)
                {
                    Extend([.. path, next], found);
                }
            }
        }

        var persons = paths.SelectMany(path => path).Distinct().ToList();
        var replies = connection.Query<(long Replier, long Author, decimal Weight)>(
            """
            SELECT c.CreatorPersonId AS Replier, m.CreatorPersonId AS Author,
                   SUM(CASE WHEN m.ParentMessageId IS NULL THEN 1.0 ELSE 0.5 END) AS Weight
            FROM Message AS c
            JOIN Message AS m ON m.Id = c.ParentMessageId
            WHERE c.CreatorPersonId IN @persons AND m.CreatorPersonId IN @persons
            GROUP BY c.CreatorPersonId, m.CreatorPersonId
            """,
            new { persons },
            commandTimeout: 900).ToDictionary(reply => (reply.Replier, reply.Author), reply => (double)reply.Weight);

        double Between(long a, long b)
            => (replies.TryGetValue((a, b), out var ab) ? ab : 0.0) + (replies.TryGetValue((b, a), out var ba) ? ba : 0.0);

        return [.. paths
            .Select(path => (Text: string.Join(",", path), Weight: path.Zip(path.Skip(1), Between).Sum()))
            .OrderByDescending(path => path.Weight).ThenBy(path => path.Text, StringComparer.Ordinal)
            .Select(path => Row([path.Text, path.Weight]))];
    }

    /// <summary>
    /// The directed, weighted edges a path query walks, read by SQL of this class over the
    /// base tables - the oracle does not take the catalog's definition of them on trust -, and
    /// the two searches over them: the cheapest cost over at most d edges, which is what a
    /// chain of d levels returns, and the most edges any cheapest path has, which is what the
    /// depth of a data set is.
    /// </summary>
    private sealed class Graph
    {
        private readonly Dictionary<long, List<(long To, double Weight)>> adjacency = [];

        public int Edges { get; private set; }

        public IEnumerable<(long To, double Weight)> Neighbours(long person) => adjacency.TryGetValue(person, out var edges) ? edges : [];

        private bool UnitWeights => adjacency.Values.All(edges => edges.All(edge => edge.Weight == 1.0));

        public static Graph Load(SqlConnection connection, string key, LdbcQuery query)
        {
            var example = query.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Example);
            var (sql, arguments) = key switch
            {
                "ic13" or "ic14" => ("SELECT k.Person1Id AS F, k.Person2Id AS T, CAST(1 AS FLOAT) AS W FROM Person_knows_Person AS k", (object?)null),
                "bi15" => (
                    """
                    WITH Interaction AS (
                        SELECT m.CreatorPersonId AS AuthorId, r.CreatorPersonId AS ReplierId,
                               SUM(CASE WHEN m.ParentMessageId IS NULL THEN 10 ELSE 5 END) AS Weight
                        FROM Message AS m
                        JOIN Message AS r ON r.ParentMessageId = m.Id
                        JOIN Forum AS mf ON mf.Id = m.ContainerForumId
                        JOIN Forum AS rf ON rf.Id = r.ContainerForumId
                        WHERE mf.CreationDate >= @startDate AND mf.CreationDate <= @endDate
                          AND rf.CreationDate >= @startDate AND rf.CreationDate <= @endDate
                        GROUP BY m.CreatorPersonId, r.CreatorPersonId)
                    SELECT k.Person1Id AS F, k.Person2Id AS T,
                           CAST(10 AS FLOAT) / (COALESCE(i1.Weight, 0) + COALESCE(i2.Weight, 0) + 10) AS W
                    FROM Person_knows_Person AS k
                    LEFT JOIN Interaction AS i1 ON i1.AuthorId = k.Person1Id AND i1.ReplierId = k.Person2Id
                    LEFT JOIN Interaction AS i2 ON i2.AuthorId = k.Person2Id AND i2.ReplierId = k.Person1Id
                    """,
                    new { startDate = DateTime.Parse(example["startDate"], CultureInfo.InvariantCulture), endDate = DateTime.Parse(example["endDate"], CultureInfo.InvariantCulture) }),
                "bi19" => (
                    """
                    WITH Replies AS (
                        SELECT c.CreatorPersonId AS Replier, p.CreatorPersonId AS Author, COUNT(*) AS Replies
                        FROM Message AS c
                        JOIN Message AS p ON p.Id = c.ParentMessageId
                        WHERE c.CreatorPersonId <> p.CreatorPersonId
                        GROUP BY c.CreatorPersonId, p.CreatorPersonId)
                    SELECT k.Person1Id AS F, k.Person2Id AS T,
                           CAST(CASE WHEN ROUND(40 - SQRT(COALESCE(r1.Replies, 0) + COALESCE(r2.Replies, 0)), 0) > 1
                                     THEN ROUND(40 - SQRT(COALESCE(r1.Replies, 0) + COALESCE(r2.Replies, 0)), 0) ELSE 1 END AS FLOAT) AS W
                    FROM Person_knows_Person AS k
                    LEFT JOIN Replies AS r1 ON r1.Replier = k.Person1Id AND r1.Author = k.Person2Id
                    LEFT JOIN Replies AS r2 ON r2.Replier = k.Person2Id AND r2.Author = k.Person1Id
                    WHERE r1.Replies IS NOT NULL OR r2.Replies IS NOT NULL
                    """,
                    null),
                "bi20" => (
                    """
                    SELECT k.Person1Id AS F, k.Person2Id AS T, CAST(MIN(ABS(s1.ClassYear - s2.ClassYear) + 1) AS FLOAT) AS W
                    FROM Person_knows_Person AS k
                    JOIN Person_studyAt_University AS s1 ON s1.PersonId = k.Person1Id
                    JOIN Person_studyAt_University AS s2 ON s2.PersonId = k.Person2Id AND s2.UniversityId = s1.UniversityId
                    GROUP BY k.Person1Id, k.Person2Id
                    """,
                    null),
                _ => throw new InvalidOperationException($"{key} is no path query this test knows."),
            };

            var graph = new Graph();
            foreach (var (from, to, weight) in connection.Query<(long F, long T, double W)>(sql, arguments, commandTimeout: 900))
            {
                if (!graph.adjacency.TryGetValue(from, out var edges))
                {
                    graph.adjacency[from] = edges = [];
                }

                edges.Add((to, weight));
                graph.Edges++;
            }

            return graph;
        }

        /// <summary>
        /// Bellman-Ford in d synchronous rounds from the sources: the cheapest cost to every
        /// person reached over at most d edges - the chain of decision 140, level by level.
        /// </summary>
        public Dictionary<long, double> Cheapest(IEnumerable<long> sources, int depth)
        {
            var cost = sources.ToDictionary(source => source, _ => 0.0);
            for (var round = 0; round < depth; round++)
            {
                var next = new Dictionary<long, double>(cost);
                foreach (var (person, reached) in cost)
                {
                    foreach (var (to, weight) in Neighbours(person))
                    {
                        var candidate = reached + weight;
                        if (!next.TryGetValue(to, out var known) || candidate < known - Tolerance)
                        {
                            next[to] = candidate;
                        }
                    }
                }

                cost = next;
            }

            return cost;
        }

        /// <summary>
        /// Dijkstra from every person, the fewest edges among paths of one cost - a plain
        /// breadth-first search where every edge weighs one -: the most edges a cheapest path
        /// of the data set has, and one pair that far apart. Over arrays, because from every
        /// one of the 9,163 persons of scale factor 1 the search touches 361 thousand edges.
        /// </summary>
        public (int Edges, (long From, long To) Pair) MostEdgesOnACheapestPath()
        {
            var ids = adjacency.Keys.Concat(adjacency.Values.SelectMany(edges => edges.Select(edge => edge.To))).Distinct().ToArray();
            var index = ids.Select((id, i) => (id, i)).ToDictionary(pair => pair.id, pair => pair.i);
            var n = ids.Length;
            var neighbours = new (int To, double Weight)[n][];
            for (var i = 0; i < n; i++)
            {
                neighbours[i] = [.. Neighbours(ids[i]).Select(edge => (index[edge.To], edge.Weight))];
            }

            var unit = UnitWeights;
            var most = 0;
            (int, int) pair = default;
            var cost = new double[n];
            var hops = new int[n];
            var done = new bool[n];
            var queue = new Queue<int>();
            var heap = new PriorityQueue<int, (double, int)>();

            for (var source = 0; source < n; source++)
            {
                Array.Fill(cost, double.PositiveInfinity);
                Array.Fill(hops, -1);
                cost[source] = 0.0;
                hops[source] = 0;

                if (unit)
                {
                    queue.Enqueue(source);
                    while (queue.TryDequeue(out var person))
                    {
                        foreach (var (to, _) in neighbours[person])
                        {
                            if (hops[to] < 0)
                            {
                                hops[to] = hops[person] + 1;
                                queue.Enqueue(to);
                            }
                        }
                    }
                }
                else
                {
                    Array.Fill(done, false);
                    heap.Enqueue(source, (0.0, 0));
                    while (heap.TryDequeue(out var person, out _))
                    {
                        if (done[person])
                        {
                            continue;
                        }

                        done[person] = true;
                        foreach (var (to, weight) in neighbours[person])
                        {
                            var candidate = cost[person] + weight;
                            var edges = hops[person] + 1;
                            if (candidate < cost[to] - Tolerance || (Math.Abs(candidate - cost[to]) <= Tolerance && edges < hops[to]))
                            {
                                cost[to] = candidate;
                                hops[to] = edges;
                                heap.Enqueue(to, (candidate, edges));
                            }
                        }
                    }
                }

                for (var to = 0; to < n; to++)
                {
                    if (hops[to] > most)
                    {
                        most = hops[to];
                        pair = (source, to);
                    }
                }
            }

            return (most, (ids[pair.Item1], ids[pair.Item2]));
        }
    }
}
