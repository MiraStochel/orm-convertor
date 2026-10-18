using FibenImport;

// The import of the FIBEN benchmark (decision 149).
//
//   dotnet run --project FibenImport -- <checkout of IBM/fiben-benchmark> <ORMConvertor directory>
//
// Reads FIBEN.sql and FIBEN_Queries.json of the checkout and writes:
//   database/fiben/schema.sql, load.sql, constraints.sql   the Fiben database of the container
//   SampleData/Fiben/Entities/<TABLE>.cs                   the Dapper entity of every table
//   SampleData/Fiben/queries.json                          the catalog of the 237 queries
// Everything written is committed; the tool runs again only when the rules change.

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: FibenImport <fiben-benchmark checkout> <ORMConvertor directory>");
    return 2;
}

var checkout = Path.GetFullPath(args[0]);
var target = Path.GetFullPath(args[1]);

var ddl = Ddl.Read(Path.Combine(checkout, "FIBEN.sql"));
Console.WriteLine($"{ddl.Tables.Count} tables, {ddl.ForeignKeys.Count} foreign keys.");

var conflicts = ddl.Tables.Where(table => table.Columns.Any(column => column.Name.Equals(table.Name, StringComparison.Ordinal))).ToList();
if (conflicts.Count > 0)
{
    // C# refuses a member named as its type (CS0542); such a table would need another rule.
    throw new InvalidDataException("A column is named as its table: " + string.Join(", ", conflicts.Select(table => table.Name)));
}

var database = Directory.CreateDirectory(Path.Combine(target, "database", "fiben"));
File.WriteAllText(Path.Combine(database.FullName, "schema.sql"), Scripts.Schema(ddl));
File.WriteAllText(Path.Combine(database.FullName, "load.sql"), Scripts.Load(ddl));
File.WriteAllText(Path.Combine(database.FullName, "constraints.sql"), Scripts.Constraints(ddl));

var entities = Directory.CreateDirectory(Path.Combine(target, "SampleData", "Fiben", "Entities"));
foreach (var stale in entities.GetFiles("*.cs"))
{
    stale.Delete();
}

foreach (var table in ddl.Tables)
{
    File.WriteAllText(Path.Combine(entities.FullName, table.Name + ".cs"), Scripts.Entity(table));
}

var origin = File.Exists(Path.Combine(checkout, ".git", "HEAD"))
    ? "IBM/fiben-benchmark " + Commit(checkout)
    : "IBM/fiben-benchmark";
var catalog = Queries.Convert(Queries.Read(Path.Combine(checkout, "FIBEN_Queries.json")), ddl, origin, Console.Out);
File.WriteAllText(Path.Combine(target, "SampleData", "Fiben", "queries.json"), Queries.Write(catalog));

var read = catalog.Queries.SelectMany(query => query.Tables).Distinct().Count();
Console.WriteLine($"{catalog.Queries.Count} queries over {read} of the {ddl.Tables.Count} tables; {catalog.Queries.Sum(query => query.Questions.Count)} questions.");
return 0;

static string Commit(string checkout)
{
    var head = File.ReadAllText(Path.Combine(checkout, ".git", "HEAD")).Trim();
    if (head.StartsWith("ref: ", StringComparison.Ordinal))
    {
        var reference = Path.Combine(checkout, ".git", head[5..].Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(reference))
        {
            head = File.ReadAllText(reference).Trim();
        }
    }

    return head.Length >= 7 ? head[..7] : head;
}
