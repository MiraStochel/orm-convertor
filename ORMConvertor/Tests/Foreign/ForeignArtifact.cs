using System.Text.Json;
using Model;

namespace Tests.Foreign;

/// <summary>One unit of a foreign artifact: its path inside the artifact's directory, the language and role the manifest states, and its text.</summary>
internal sealed record ForeignUnit(string Path, ConversionContentType ContentType, string Content);

/// <summary>
/// The input an artifact answers (decision 172): the kind of measure - a query of the
/// differential matrix, a query of the FIBEN catalog, a query of the LDBC catalog - and the
/// identifier within it. The kind decides the database, the fields of a row and what the
/// fourth level compares with; a kind nobody knows stops the run rather than picking one.
/// </summary>
internal sealed record ForeignKey(string Kind, string Id)
{
    public const string Matrix = "matrix";
    public const string Fiben = "fiben";
    public const string Ldbc = "ldbc";

    public static ForeignKey Parse(string answers)
    {
        var colon = answers.IndexOf(':');
        if (colon <= 0 || colon == answers.Length - 1)
        {
            throw new InvalidOperationException($"The key \"{answers}\" is not written as kind:id (matrix:<query>, fiben:<key>, ldbc:<key>).");
        }

        var kind = answers[..colon].Trim().ToLowerInvariant();
        if (kind is not (Matrix or Fiben or Ldbc))
        {
            throw new InvalidOperationException($"The key \"{answers}\" names the kind \"{kind}\", which no suite measures; the kinds are matrix, fiben and ldbc.");
        }

        return new ForeignKey(kind, answers[(colon + 1)..].Trim());
    }

    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>
/// An artifact the tool did not make (decision 172): a directory with the units of the
/// target - every file a consumer would get from <c>/convert</c> for the same input - and a
/// manifest, <c>artifact.json</c>, that says what the artifact answers, from which source into
/// which target, who made it, which file holds which unit, and whatever the producer wants
/// the record to carry. The suite reads the four fields and copies the fifth.
///
/// The files may carry the <c>{{schema}}</c> placeholder of the fixture scripts and inputs; it
/// is substituted with the suite's own schema when the artifact is read, which is how one
/// artifact runs in both suites' schemas and how the inputs the suite exports can be handed
/// to a model and the answer handed back unchanged.
/// </summary>
internal sealed record ForeignArtifact(
    string Id,
    string Directory,
    ForeignKey Key,
    ORMEnum Source,
    ORMEnum Target,
    string Producer,
    IReadOnlyList<ForeignUnit> Units,
    string Metadata)
{
    public const string ManifestFileName = "artifact.json";

    /// <summary>The directory of foreign artifacts a run judges: one artifact, or a directory of them.</summary>
    public const string ArtifactsVariable = "ORMCONVERTOR_FOREIGN_ARTIFACTS";

    /// <summary>The directory the rows of a run are written into; without it the rows go to the test output alone.</summary>
    public const string ResultsVariable = "ORMCONVERTOR_FOREIGN_RESULTS";

    private const string SchemaPlaceholder = "{{schema}}";

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    /// <summary>The entity classes, whatever language they are written in.</summary>
    public IEnumerable<ForeignUnit> Entities => Units.Where(unit => unit.ContentType is
        ConversionContentType.CSharpEntity or ConversionContentType.JavaEntity or
        ConversionContentType.CSharp or ConversionContentType.Java);

    /// <summary>The mapping documents: the hbm.xml of NHibernate, the mapper of MyBatis, an orm.xml.</summary>
    public IEnumerable<ForeignUnit> Mappings => Units.Where(unit => unit.ContentType == ConversionContentType.XML);

    /// <summary>
    /// The unit that carries the query as the consumer calls it: the binding method, or the
    /// LINQ form where an NHibernate artifact carries that alone (decision 118); null for an
    /// artifact of entities and mappings only, which ends at the third level.
    /// </summary>
    public ForeignUnit? QueryMethod
        => Single(ConversionContentType.CSharpQuery) ?? Single(ConversionContentType.JavaQuery) ?? Single(ConversionContentType.CSharpLinqQuery);

    /// <summary>The bare HQL beside an NHibernate method, which the third level compiles against the mapping; null where the artifact carries none.</summary>
    public ForeignUnit? HqlText => Single(ConversionContentType.HqlQuery);

    /// <summary>The form the query method is in: the binding method, or the LINQ form (decision 118).</summary>
    public ConversionContentType Form
        => QueryMethod?.ContentType == ConversionContentType.CSharpLinqQuery ? ConversionContentType.CSharpLinqQuery : ConversionContentType.CSharpQuery;

    private ForeignUnit? Single(ConversionContentType contentType)
    {
        var matching = Units.Where(unit => unit.ContentType == contentType).ToList();
        if (matching.Count > 1)
        {
            throw new InvalidOperationException($"{Id}: the manifest states {matching.Count} units of {contentType}, and a query artifact has one.");
        }

        return matching.SingleOrDefault();
    }

    /// <summary>
    /// The artifacts under a root: the root itself where it holds a manifest, otherwise every
    /// directory directly under it that holds one, in the order of their names. A root without
    /// any is a misconfiguration and stops the run.
    /// </summary>
    public static IReadOnlyList<ForeignArtifact> Discover(string root, string schema)
    {
        if (File.Exists(Path.Combine(root, ManifestFileName)))
        {
            return [Read(root, schema)];
        }

        if (!System.IO.Directory.Exists(root))
        {
            throw new InvalidOperationException($"{ArtifactsVariable} names {root}, which is not a directory.");
        }

        var artifacts = System.IO.Directory.GetDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, ManifestFileName)))
            .Order(StringComparer.Ordinal)
            .Select(directory => Read(directory, schema))
            .ToList();

        if (artifacts.Count == 0)
        {
            throw new InvalidOperationException($"{root} holds no artifact: neither it nor a directory under it has an {ManifestFileName}.");
        }

        return artifacts;
    }

    /// <summary>Reads one artifact: the manifest, then every unit it names, with the schema placeholder substituted.</summary>
    public static ForeignArtifact Read(string directory, string schema)
    {
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"{id}: {directory} holds no {ManifestFileName}.");
        }

        JsonDocument manifest;
        try
        {
            manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{id}: {ManifestFileName} is not JSON: {exception.Message}");
        }

        using (manifest)
        {
            var root = manifest.RootElement;

            string Required(string name)
                => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()!.Trim()
                    : throw new InvalidOperationException($"{id}: {ManifestFileName} states no \"{name}\".");

            var key = ForeignKey.Parse(Required("answers"));
            var source = Framework(id, "source", Required("source"));
            var target = Framework(id, "target", Required("target"));
            var producer = Required("producer");

            if (!root.TryGetProperty("units", out var unitsElement) || unitsElement.ValueKind != JsonValueKind.Array || unitsElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"{id}: {ManifestFileName} states no \"units\" - the files of the artifact with their content types.");
            }

            var units = new List<ForeignUnit>();
            foreach (var element in unitsElement.EnumerateArray())
            {
                var path = element.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String
                    ? pathElement.GetString()!.Trim()
                    : throw new InvalidOperationException($"{id}: a unit of {ManifestFileName} states no \"path\".");

                var contentType = element.TryGetProperty("contentType", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? ContentType(id, path, typeElement.GetString()!)
                    : DefaultContentType(path)
                      ?? throw new InvalidOperationException($"{id}: the unit {path} states no \"contentType\", and its extension names only a language; a .cs or .java file says whether it is an entity or a query.");

                var file = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(file))
                {
                    throw new InvalidOperationException($"{id}: the manifest names {path}, which is not in the artifact's directory.");
                }

                units.Add(new ForeignUnit(path, contentType, File.ReadAllText(file).Replace("﻿", string.Empty).Replace(SchemaPlaceholder, schema, StringComparison.Ordinal)));
            }

            var metadata = root.TryGetProperty("metadata", out var metadataElement) && metadataElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Serialize(metadataElement, Compact)
                : "{}";

            return new ForeignArtifact(id, directory, key, source, target, producer, units, metadata);
        }
    }

    /// <summary>
    /// Writes an artifact in the shape the reader takes: the files and the manifest. Used for
    /// the tool's own artifacts put through the path to prove that it measures what the client
    /// measures, and by a test that makes an artifact on the fly.
    /// </summary>
    public static void Write(
        string directory,
        ForeignKey key,
        ORMEnum source,
        ORMEnum target,
        string producer,
        IEnumerable<ForeignUnit> units,
        string? metadataJson = null)
    {
        System.IO.Directory.CreateDirectory(directory);

        var manifest = new List<string>
        {
            "{",
            $"  \"answers\": {JsonSerializer.Serialize(key.ToString())},",
            $"  \"source\": \"{source}\",",
            $"  \"target\": \"{target}\",",
            $"  \"producer\": {JsonSerializer.Serialize(producer)},",
            "  \"units\": [",
        };

        var list = units.ToList();
        for (var index = 0; index < list.Count; index++)
        {
            var unit = list[index];
            var file = Path.Combine(directory, unit.Path.Replace('/', Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, unit.Content);

            manifest.Add($"    {{ \"path\": {JsonSerializer.Serialize(unit.Path)}, \"contentType\": \"{unit.ContentType}\" }}{(index < list.Count - 1 ? "," : string.Empty)}");
        }

        manifest.Add("  ],");
        manifest.Add($"  \"metadata\": {metadataJson ?? "{}"}");
        manifest.Add("}");

        File.WriteAllText(Path.Combine(directory, ManifestFileName), string.Join("\n", manifest) + "\n");
    }

    /// <summary>
    /// The content type an extension names by itself: the query languages and XML. A .cs or
    /// .java file names a language only (decision 111), and an artifact's unit is written in a
    /// role - entity or query -, so the manifest has to say which.
    /// </summary>
    internal static ConversionContentType? DefaultContentType(string path) => path switch
    {
        _ when path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) => ConversionContentType.SqlQuery,
        _ when path.EndsWith(".hql", StringComparison.OrdinalIgnoreCase) => ConversionContentType.HqlQuery,
        _ when path.EndsWith(".jpql", StringComparison.OrdinalIgnoreCase) => ConversionContentType.JpqlQuery,
        _ when path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) => ConversionContentType.XML,
        _ => null,
    };

    private static ConversionContentType ContentType(string id, string path, string name)
        => Enum.TryParse<ConversionContentType>(name.Trim(), ignoreCase: true, out var contentType)
            ? contentType
            : throw new InvalidOperationException($"{id}: the unit {path} states the content type \"{name}\", which the tool does not have; the names are those of ConversionContentType.");

    private static ORMEnum Framework(string id, string field, string name)
        => Enum.TryParse<ORMEnum>(name, ignoreCase: true, out var framework)
            ? framework
            : throw new InvalidOperationException($"{id}: the manifest states the {field} \"{name}\", which is no framework of the tool.");
}
