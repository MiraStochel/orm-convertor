using SedeImport;

// The import of the SEDE query set (decision 208).
//
//   dotnet run --project SedeImport -- <checkout of hirupert/sede> <ORMConvertor directory>
//       [--database <connection string of a Sede database>] [--probe]
//
// Reads stackexchange_schema/tables_so.json and data/sede/val.jsonl and test.jsonl of the
// checkout, and beside the tool Schema/documentation.html (the schema documentation of SEDE)
// and, where present, Schema/types.csv (the listing of the SEDE catalog), and writes:
//   database/sede/schema.sql, load.sql, constraints.sql, dump.tsv   the Sede database of the container
//   SampleData/Sede/Entities/<Table>.cs                             the Dapper entity of every table
//   SampleData/Sede/queries.json                                    the catalog of the human-reviewed queries
// With --database, every text is asked of that database - one made by database/sede/, the
// container's Sede or one loaded by hand - what it returns, which gives the columns of the
// result and marks a text that is not valid over the schema; with --probe, every translated
// text is run once over the data with its values, which marks a text that fails and notes one
// that returns no rows. The committed catalog was made with both. Everything written is
// committed; the tool runs again only when the rules change.

var positional = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
string? connectionString = null;
var probe = false;
for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--database" when index + 1 < args.Length:
            connectionString = args[++index];
            positional.Remove(connectionString);
            break;
        case "--probe":
            probe = true;
            break;
    }
}

if (positional.Count != 2)
{
    Console.Error.WriteLine("usage: SedeImport <sede checkout> <ORMConvertor directory> [--database <connection string>] [--probe]");
    return 2;
}

var checkout = Path.GetFullPath(positional[0]);
var target = Path.GetFullPath(positional[1]);
var schemaDirectory = Path.Combine(target, "SedeImport", "Schema");

var schema = Schema.Read(checkout, schemaDirectory, Console.Out);
Console.WriteLine($"{schema.Tables.Count} tables, {schema.ForeignKeys.Count} foreign keys, {schema.Enumerations.Count} type tables filled.");

var conflicts = schema.Tables.Where(table => table.Columns.Any(column => column.Name.Equals(table.Name, StringComparison.Ordinal))).ToList();
if (conflicts.Count > 0)
{
    // C# refuses a member named as its type (CS0542); such a table would need another rule.
    throw new InvalidDataException("A column is named as its table: " + string.Join(", ", conflicts.Select(table => table.Name)));
}

var database = Directory.CreateDirectory(Path.Combine(target, "database", "sede"));
File.WriteAllText(Path.Combine(database.FullName, "schema.sql"), Scripts.Schema(schema));
File.WriteAllText(Path.Combine(database.FullName, "load.sql"), Scripts.Load(schema));
File.WriteAllText(Path.Combine(database.FullName, "constraints.sql"), Scripts.Constraints(schema));
File.WriteAllText(Path.Combine(database.FullName, "dump.tsv"), Scripts.Dump(schema));

var entities = Directory.CreateDirectory(Path.Combine(target, "SampleData", "Sede", "Entities"));
foreach (var stale in entities.GetFiles("*.cs"))
{
    stale.Delete();
}

foreach (var table in schema.Tables)
{
    File.WriteAllText(Path.Combine(entities.FullName, table.Name + ".cs"), Scripts.Entity(table));
}

var origin = File.Exists(Path.Combine(checkout, ".git", "HEAD"))
    ? "hirupert/sede " + Commit(checkout)
    : "hirupert/sede";
if (connectionString is null)
{
    Console.WriteLine("No --database: the statuses of the catalog are syntactic only and the columns of the results are unknown. Run again with the Sede database to commit the catalog.");
}

var catalog = Queries.Convert(Queries.Read(Path.Combine(checkout, "data", "sede")), schema, connectionString, probe, origin, Console.Out);
File.WriteAllText(Path.Combine(target, "SampleData", "Sede", "queries.json"), Queries.Write(catalog));

var read = catalog.Queries.SelectMany(query => query.Tables).Distinct().Count();
Console.WriteLine($"{catalog.Queries.Count} queries over {read} of the {schema.Tables.Count} tables; {catalog.Queries.Count(query => query.Translation == "AsSpecified")} translated as specified.");
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
