using System.Xml;
using System.Xml.Schema;

namespace Tests.Verification;

/// <summary>
/// Second verification level of decision 016 for XML artifacts: the generated mapping is
/// validated against the mapping schema NHibernate itself ships as an embedded resource.
/// Taking the schema from the referenced package rather than from a copy in the repository
/// keeps the claim honest - it is the schema of the version the acceptance level runs
/// against, and it cannot silently drift away from it.
/// </summary>
internal static class NHibernateMappingSchema
{
    private static readonly XmlSchemaSet Schemas = Load();

    private static XmlSchemaSet Load()
    {
        var nhibernate = typeof(global::NHibernate.Cfg.Configuration).Assembly;
        var resourceName = nhibernate
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("nhibernate-mapping.xsd", StringComparison.OrdinalIgnoreCase));

        using var stream = nhibernate.GetManifestResourceStream(resourceName)!;
        var schemas = new XmlSchemaSet();
        schemas.Add(XmlSchema.Read(stream, validationEventHandler: null)!);
        return schemas;
    }

    /// <summary>Returns the validation errors; an empty list means the mapping is valid.</summary>
    public static IReadOnlyList<string> Validate(string mappingXml)
    {
        var errors = new List<string>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas,
        };
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);

        using var reader = XmlReader.Create(new StringReader(mappingXml), settings);
        while (reader.Read())
        {
        }

        return errors;
    }
}
