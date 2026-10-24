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

    /// <summary>The scenario of <c>CrossFrameworkInputs</c> of one source framework: entities, their mapping and one query; measured up to the third level.</summary>
    public const string Cross = "cross";

    /// <summary>
    /// An example of the explanatory page by its key (decision 099) - the excerpts of real
    /// projects among them, the case study of T1 (decision 174): a whole input of entities and
    /// queries as a project keeps them, measured up to the third level because no database of
    /// the tool holds its tables.
    /// </summary>
    public const string Example = "example";

    /// <summary>
    /// One point of a series of the search for limits (decision 182), <c>&lt;axis&gt;-&lt;point&gt;</c>
    /// over the axes of <c>Experiment/LimitSeries</c>: a query over the domain of the query-shape
    /// matrices, which runs over the fixture at the fourth level without a measure of
    /// equivalence, or entities alone, which end at the third.
    /// </summary>
    public const string Series = "series";

    public static ForeignKey Parse(string answers)
    {
        var colon = answers.IndexOf(':');
        if (colon <= 0 || colon == answers.Length - 1)
        {
            throw new InvalidOperationException($"The key \"{answers}\" is not written as kind:id (matrix:<query>, fiben:<key>, ldbc:<key>, cross:<source framework>, example:<key>, series:<axis>-<point>).");
        }

        var kind = answers[..colon].Trim().ToLowerInvariant();
        if (kind is not (Matrix or Fiben or Ldbc or Cross or Example or Series))
        {
            throw new InvalidOperationException($"The key \"{answers}\" names the kind \"{kind}\", which no suite measures; the kinds are matrix, fiben, ldbc, cross, example and series.");
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
    /// The units that carry the queries as the consumer calls them: the binding methods, or the
    /// LINQ forms where an NHibernate artifact carries those alone (decision 118). One for an
    /// artifact that answers a query; several for an artifact of a whole project (an example, the
    /// case study of T1 - decision 174), which the path judges up to the third level as a project;
    /// none for an artifact of entities and mappings only, which ends at the third level too.
    /// </summary>
    public IReadOnlyList<ForeignUnit> QueryMethods
    {
        get
        {
            var binding = Units.Where(unit => unit.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery).ToList();
            return binding.Count > 0 ? binding : [.. Units.Where(unit => unit.ContentType == ConversionContentType.CSharpLinqQuery)];
        }
    }

    /// <summary>The one query method of an artifact that answers a query; null for an artifact of entities alone or of a whole project.</summary>
    public ForeignUnit? QueryMethod => QueryMethods is [var single] ? single : null;

    /// <summary>Whether the artifact is a whole project - several queries -, judged up to the third level as one.</summary>
    public bool IsProject => QueryMethods.Count > 1;

    /// <summary>The bare HQL texts beside the NHibernate methods, which the third level compiles against the mapping; empty where the artifact carries none.</summary>
    public IReadOnlyList<ForeignUnit> HqlTexts => [.. Units.Where(unit => unit.ContentType == ConversionContentType.HqlQuery)];

    /// <summary>The form the query method is in: the binding method, or the LINQ form (decision 118).</summary>
    public ConversionContentType Form
        => QueryMethods.Count > 0 && QueryMethods.All(method => method.ContentType == ConversionContentType.CSharpLinqQuery) ? ConversionContentType.CSharpLinqQuery : ConversionContentType.CSharpQuery;

    /// <summary>
    /// The language the queries of the artifact are written in, as the row of the T2 matrix reads
    /// a cell (decision 173): <c>target</c> - the target's own query language -, <c>native SQL</c>
    /// where a target whose language is not SQL carries every query as a SQL statement beside its
    /// method (the escape path of decision 113, or a model that took it), <c>mixed</c> where a
    /// project carries some queries so and the rest in the target's language, or <c>none</c> for
    /// an artifact of entities and mappings alone. SQL is the language of Dapper and MyBatis, so
    /// their artifacts are never native SQL.
    /// </summary>
    public string Language
    {
        get
        {
            if (!Units.Any(unit => unit.ContentType.IsQuery()))
            {
                return LanguageNone;
            }

            if (Target is ORMEnum.Dapper or ORMEnum.MyBatis)
            {
                return LanguageTarget;
            }

            var methods = Math.Max(1, QueryMethods.Count);
            var statements = Units.Count(unit => unit.ContentType == ConversionContentType.SqlQuery);
            return statements == 0 ? LanguageTarget : statements >= methods ? LanguageNativeSql : LanguageMixed;
        }
    }

    public const string LanguageTarget = "target";
    public const string LanguageNativeSql = "native SQL";
    public const string LanguageMixed = "mixed";
    public const string LanguageNone = "none";

    /// <summary>The language value of a row whose translation the tool refused, so that no artifact exists (decision 173).</summary>
    public const string LanguageRefused = "refused";

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
