using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers.Convertors;
using System.Text;

namespace NHibernateWrappers;

public class NHibernateEntityBuilder : AbstractEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => NHibernateDescriptor.Instance;

    /// <summary>
    /// True when the entity is mapped with a composite identifier.
    /// NHibernate then imposes extra requirements on the persistent class,
    /// see decision 006.
    /// </summary>
    private static bool HasCompositeKey(EntityMap em)
        => em.PrimaryKey is not null && em.PrimaryKey.Parts.Count > 1;

    /// <summary>
    /// Adds C# namespace.
    /// Adds XML prolog and root <hibernate-mapping> tag.
    /// </summary>
    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        // System is required for [Serializable] and HashCode in the identity
        // members emitted for composite keys. A plain entity needs no imports.
        if (HasCompositeKey(entityMap))
        {
            artifact.Code.AppendLine("using System;");
            artifact.Code.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(entityMap.Entity.Namespace))
        {
            artifact.Code.AppendLine($"namespace {entityMap.Entity.Namespace};");
            artifact.Code.AppendLine();
        }

        // XML: prolog + root <hibernate-mapping>
        AppendXml(artifact.Mapping, 0, "<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
        var xmlNs = "urn:nhibernate-mapping-2.2";
        var nsAttr = string.IsNullOrWhiteSpace(entityMap.Entity.Namespace)
            ? string.Empty
            : $" namespace=\"{entityMap.Entity.Namespace}\"";
        AppendXml(artifact.Mapping, 0, $"<hibernate-mapping xmlns=\"{xmlNs}\"{nsAttr}>");
    }

    /// <summary>
    /// Builds C# class header and XML <class> tag.
    /// </summary>
    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        var modifier = AccessModifierConvertor.ToModifierString(entityMap.Entity.AccessModifier);
        var name = entityMap.Entity.Name;

        // C#
        if (HasCompositeKey(entityMap))
        {
            // Required by NHibernate for classes mapped with <composite-id>. Declared as an
            // enforced member, but emitted here because it precedes the class header.
            artifact.Code.AppendLine("[Serializable]");
        }

        artifact.Code.AppendLine($"{modifier} class {name}");
        artifact.Code.AppendLine("{");

        // XML <class>
        var nameWithNamespace = string.IsNullOrWhiteSpace(entityMap.Entity.Namespace)
            ? name
            : $"{entityMap.Entity.Namespace}.{name}, {entityMap.Entity.Namespace}";

        var table = entityMap.Table ?? name; // default = class name
        var schema = entityMap.Schema ?? string.Empty; // TODO schema
        var schemaAttr = string.IsNullOrWhiteSpace(schema) ? string.Empty : $" schema=\"{schema}\"";

        AppendXml(artifact.Mapping, 1, $"<class name=\"{nameWithNamespace}\" table=\"{table}\"{schemaAttr}>");
        artifact.ClassOpened = true;
    }

    /// <summary>
    /// Builds C# primary key property and XML <id> tag.
    /// </summary>
    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is null)
        {
            return; // no PK
        }

        if (entityMap.PrimaryKey.Parts.Count == 1)
        {
            var part = entityMap.PrimaryKey.Parts[0];
            var propertyMap = part.PropertyMap;
            var prop = propertyMap.Property;
            var columnName = propertyMap.ColumnName ?? prop.Name;

            AppendPropertyToCode(artifact.Code, prop, isPrimaryKey: true);

            var facets = BuildColumnFacets(propertyMap);
            if (facets.Count == 0)
            {
                AppendXml(artifact.Mapping, 2, $"<id name=\"{prop.Name}\" column=\"{columnName}\"{TypeAttribute(propertyMap)}>");
            }
            else
            {
                AppendXml(artifact.Mapping, 2, $"<id name=\"{prop.Name}\"{TypeAttribute(propertyMap)}>");
                AppendXml(artifact.Mapping, 3, $"<column name=\"{columnName}\" {string.Join(' ', facets)} />");
            }

            AppendGenerator(artifact.Mapping, entityMap, part);
            AppendXml(artifact.Mapping, 2, "</id>");
            return;
        }

        // Composite key: <composite-id> without a generator (assigned semantics),
        // the order of <key-property> elements matches PrimaryKeyPart.Order.
        AppendXml(artifact.Mapping, 2, "<composite-id>");
        foreach (var part in entityMap.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;
            var prop = propertyMap.Property;
            var columnName = propertyMap.ColumnName ?? prop.Name;

            if (part.Strategy is not (PrimaryKeyStrategy.Unspecified or PrimaryKeyStrategy.Assigned))
            {
                // <composite-id> admits no <generator> at all, so a stated mechanism cannot
                // survive; assigned semantics apply (decision 011).
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.XML,
                    Entity = entityMap.Entity.Name,
                    Property = prop.Name,
                    Category = MappingFactCategory.PrimaryKeyStrategy,
                    Reason = $"<composite-id> carries no generator, so the strategy {part.Strategy} of this key part is dropped and assigned semantics apply.",
                });
            }

            AppendPropertyToCode(artifact.Code, prop, isPrimaryKey: true);

            var facets = BuildColumnFacets(propertyMap);
            if (facets.Count == 0)
            {
                AppendXml(artifact.Mapping, 3, $"<key-property name=\"{prop.Name}\" column=\"{columnName}\"{TypeAttribute(propertyMap)} />");
            }
            else
            {
                AppendXml(artifact.Mapping, 3, $"<key-property name=\"{prop.Name}\"{TypeAttribute(propertyMap)}>");
                AppendXml(artifact.Mapping, 4, $"<column name=\"{columnName}\" {string.Join(' ', facets)} />");
                AppendXml(artifact.Mapping, 3, "</key-property>");
            }
        }
        AppendXml(artifact.Mapping, 2, "</composite-id>");
    }

    private static string? ResolveNhType(PropertyMap propertyMap)
    {
        if (propertyMap.Type != null)
        {
            return DatabaseTypeConvertor.ToNHibernate(propertyMap.Type.Value);
        }

        // TODO this would be a place to query database for the missing type
        // for now we guess it from the language scalar; for anything else - a reference,
        // a collection, an unknown name - no claim is made and NHibernate decides itself.
        return propertyMap.Property.Type is { Category: LangTypeCategory.Scalar } langType
            ? DatabaseTypeConvertor.GuessFromScalarType(langType.ScalarType!.Value)
            : null;
    }

    /// <summary>
    /// The type attribute of an &lt;id&gt; or &lt;key-property&gt;, empty when there is
    /// nothing to claim - NHibernate then infers the type from the persistent class.
    /// </summary>
    private static string TypeAttribute(PropertyMap propertyMap)
        => ResolveNhType(propertyMap) is string type ? $" type=\"{type}\"" : string.Empty;

    /// <summary>
    /// Facets of a key column that NHibernate accepts only inside a nested &lt;column&gt; element.
    /// &lt;property&gt; can carry length, precision and scale as its own attributes, &lt;id&gt; and
    /// &lt;key-property&gt; cannot - so without the nested form a key column loses them silently.
    ///
    /// Nullability is deliberately left out: a column that carries the identifier is not
    /// nullable, and emitting not-null="false" there would produce a mapping that contradicts
    /// itself.
    ///
    /// An empty result means the compact form with a column attribute is enough, which keeps
    /// the output of a plain key exactly as it was.
    /// </summary>
    private static List<string> BuildColumnFacets(PropertyMap propertyMap)
    {
        var facets = new List<string>();

        if (propertyMap.Length.HasValue)
        {
            facets.Add($"length=\"{propertyMap.Length.Value}\"");
        }

        if (propertyMap.Precision.HasValue)
        {
            facets.Add($"precision=\"{propertyMap.Precision.Value}\"");
        }

        if (propertyMap.Scale.HasValue)
        {
            facets.Add($"scale=\"{propertyMap.Scale.Value}\"");
        }

        return facets;
    }

    /// <summary>
    /// Builds C# properties and XML <property> tags.
    /// Primary and foreign keys are handled separately.
    /// </summary>
    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var pm in entityMap.PropertyMaps)
        {
            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == pm.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (entityMap.Relations.Any(r => r.SourceNavigationProperty == pm.Property.Name))
            {
                continue; // navigation property – handled in BuildForeignKey
            }

            AppendPropertyToCode(artifact.Code, pm.Property);
            AppendPropertyToXml(artifact.Mapping, pm);
        }
    }

    /// <summary>
    /// Builds C# foreign key properties and XML <one-to-one>, <many-to-one>, <bag> or <many-to-many> tags.
    /// </summary>
    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
        // 1:1 and N:1 foreign keys
        foreach (var relation in entityMap.Relations.Where(r => r.Cardinality is Cardinality.OneToOne or Cardinality.ManyToOne))
        {
            var propertyMap = FindNavigationPropertyMap(entityMap, relation);
            if (propertyMap is null)
            {
                continue;
            }

            AppendPropertyToCode(artifact.Code, propertyMap.Property); // navigation property in C#

            AppendReference(artifact.Mapping, entityMap, relation, propertyMap.Property.Name);
        }

        // 1:N and N:N collections
        foreach (var relation in entityMap.Relations.Where(r => r.Cardinality is Cardinality.OneToMany or Cardinality.ManyToMany))
        {
            var propertyMap = FindNavigationPropertyMap(entityMap, relation);
            if (propertyMap is null)
            {
                continue;
            }

            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property)}");
            artifact.Code.AppendLine();

            // XML <bag> (TODO: allow set/list/map etc.)
            // TODO other collection properties
            AppendXml(artifact.Mapping, 2, $"<bag name=\"{propertyMap.Property.Name}\" inverse=\"true\" cascade=\"all-delete-orphan\">");
            AppendKey(artifact.Mapping, entityMap, relation);

            if (relation.Cardinality == Cardinality.OneToMany)
            {
                AppendXml(artifact.Mapping, 3, $"<one-to-many class=\"{relation.TargetEntity}\" />");
            }
            else // ManyToMany
            {
                AppendXml(artifact.Mapping, 3, $"<many-to-many class=\"{relation.TargetEntity}\" />");
            }

            AppendXml(artifact.Mapping, 2, "</bag>");
        }
    }

    private static PropertyMap? FindNavigationPropertyMap(EntityMap em, Relation relation)
        => relation.SourceNavigationProperty is null
            ? null
            : em.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

    /// <summary>
    /// Emits the Equals/GetHashCode overrides that NHibernate requires from any
    /// class mapped with <composite-id>, as declared by <see cref="Descriptor"/>.
    /// Without them the mapping fails to compile with "composite-id class must
    /// override Equals()" while the session factory is being built. The matching
    /// [Serializable] attribute precedes the class header and is therefore emitted
    /// in BuildTableSchema. See decision 006.
    /// </summary>
    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
        if (!HasCompositeKey(entityMap))
        {
            return;
        }

        var className = entityMap.Entity.Name;
        var keyNames = entityMap.PrimaryKey!.Parts
            .Select(p => p.PropertyMap.Property.Name)
            .ToList();

        artifact.Code.AppendLine("    public override bool Equals(object? obj)");
        artifact.Code.AppendLine("    {");
        artifact.Code.AppendLine("        if (ReferenceEquals(this, obj))");
        artifact.Code.AppendLine("        {");
        artifact.Code.AppendLine("            return true;");
        artifact.Code.AppendLine("        }");
        artifact.Code.AppendLine();
        // Pattern matching rather than GetType() equality: an NHibernate proxy is a
        // subclass of the entity, so comparing the runtime types would reject it.
        artifact.Code.AppendLine($"        if (obj is not {className} other)");
        artifact.Code.AppendLine("        {");
        artifact.Code.AppendLine("            return false;");
        artifact.Code.AppendLine("        }");
        artifact.Code.AppendLine();

        for (var i = 0; i < keyNames.Count; i++)
        {
            var name = keyNames[i];
            var prefix = i == 0 ? "return " : "    && ";
            var suffix = i == keyNames.Count - 1 ? ";" : string.Empty;
            artifact.Code.AppendLine($"        {prefix}Equals({name}, other.{name}){suffix}");
        }

        artifact.Code.AppendLine("    }");
        artifact.Code.AppendLine();

        artifact.Code.AppendLine("    public override int GetHashCode()");
        artifact.Code.AppendLine("    {");

        if (keyNames.Count <= 8)
        {
            artifact.Code.AppendLine($"        return HashCode.Combine({string.Join(", ", keyNames)});");
        }
        else
        {
            // HashCode.Combine is only defined up to eight arguments.
            artifact.Code.AppendLine("        var hash = new HashCode();");
            foreach (var name in keyNames)
            {
                artifact.Code.AppendLine($"        hash.Add({name});");
            }
            artifact.Code.AppendLine("        return hash.ToHashCode();");
        }

        artifact.Code.AppendLine("    }");
        artifact.Code.AppendLine();
    }

    /// <summary>
    /// Finalizes the build process by closing the class and XML tags.
    /// </summary>
    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
    {
        // Close C# class
        artifact.Code.AppendLine("}");

        if (artifact.ClassOpened)
        {
            AppendXml(artifact.Mapping, 1, "</class>");
        }

        AppendXml(artifact.Mapping, 0, "</hibernate-mapping>", appendLine: false);

        yield return new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = artifact.Code.ToString() };
        yield return new ConversionSource { ContentType = ConversionContentType.XML, Content = artifact.Mapping.ToString() };
    }

    /// <summary>
    /// Appends a property to the C# code.
    /// </summary>
    private static void AppendPropertyToCode(StringBuilder codeResult, Property prop, bool isPrimaryKey = false)
    {
        var declaration = BuildPropertySignature(prop, isPrimaryKey);
        codeResult.AppendLine($"    {declaration}");
        codeResult.AppendLine();
    }

    /// <summary>
    /// Appends a property to the XML mapping.
    /// </summary>
    private static void AppendPropertyToXml(StringBuilder mappingResult, PropertyMap propertyMap)
    {
        var prop = propertyMap.Property;

        var attrs = new List<string> { $"name=\"{prop.Name}\"" };

        if (!string.IsNullOrWhiteSpace(propertyMap.ColumnName))
        {
            attrs.Add($"column=\"{propertyMap.ColumnName}\"");
        }

        if (propertyMap.IsNullable.HasValue)
        {
            attrs.Add($"not-null=\"{(!propertyMap.IsNullable.Value).ToString().ToLowerInvariant()}\"");
        }
        else if (prop.Type is { IsNullable: false })
        {
            // The language side is the fallback claim; a property with no stated type
            // says nothing about nullability either.
            attrs.Add("not-null=\"true\"");
        }

        if (propertyMap.Type.HasValue)
        {
            attrs.Add($"type=\"{DatabaseTypeConvertor.ToNHibernate(propertyMap.Type.Value)}\"");
        }

        if (propertyMap.Precision.HasValue)
        {
            attrs.Add($"precision=\"{propertyMap.Precision.Value}\"");
        }

        if (propertyMap.Scale.HasValue)
        {
            attrs.Add($"scale=\"{propertyMap.Scale.Value}\"");
        }

        if (propertyMap.Length.HasValue)
        {
            attrs.Add($"length=\"{propertyMap.Length.Value}\"");
        }

        AppendXml(mappingResult, 2, $"<property {string.Join(' ', attrs)} />");
    }

    /// <summary>
    /// Writes the generator of a simple key. Parameters - sequence name, block size - go in as
    /// nested elements: without them the mapping names no sequence and the target falls back
    /// to its own default, so it compiles and fails at runtime. The class written is the
    /// canonical name of the strategy; what the source called it is a record for diagnostics,
    /// not an input to generation (decision 011) - so a source name the canonical form does
    /// not restate is reported as a loss, and assigned written for a strategy nobody stated
    /// is reported as a convention of the target (decision 010).
    /// </summary>
    private void AppendGenerator(StringBuilder mapping, EntityMap entityMap, PrimaryKeyPart part)
    {
        var generatorClass = PrimaryKeyStrategyConvertor.ToNHibernate(part.Strategy);

        if (part.SourceStrategyName is not null && part.SourceStrategyName != generatorClass)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = $"The source called the generator '{part.SourceStrategyName}'; the canonical '{generatorClass}' is written and the source's own name is not restated (decision 011).",
            });
        }
        else if (part.Strategy == PrimaryKeyStrategy.Unspecified)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = "No generation strategy was stated; the generator 'assigned' is written, which is a convention of the target, not a fact of the source.",
            });
        }

        if (part.StrategyParameters.Count == 0)
        {
            AppendXml(mapping, 3, $"<generator class=\"{generatorClass}\" />");
            return;
        }

        AppendXml(mapping, 3, $"<generator class=\"{generatorClass}\">");

        foreach (var (name, value) in part.StrategyParameters)
        {
            AppendXml(mapping, 4, $"<param name=\"{name}\">{value}</param>");
        }

        AppendXml(mapping, 3, "</generator>");
    }

    /// <summary>
    /// Writes the mapping of a 1:1 or N:1 reference. The element name follows the shape of the
    /// columns rather than the multiplicity: &lt;one-to-one&gt; is the side holding no foreign key
    /// and admits no column at all, so the owning side of a 1:1 with its own key is written as
    /// &lt;many-to-one unique="true"&gt; (decision 012).
    /// </summary>
    private void AppendReference(StringBuilder mapping, EntityMap entityMap, Relation relation, string navigationProperty)
    {
        bool sharedKey = SharesPrimaryKeyThrough(entityMap, navigationProperty);

        if (relation.Cardinality == Cardinality.OneToOne && (relation.Role == RelationRole.Inverse || sharedKey))
        {
            // Nothing to name here: either the far side holds the key, or both entities share the
            // primary key, and constrained is how NHibernate says the identity comes from there.
            var constrained = sharedKey ? " constrained=\"true\"" : string.Empty;
            AppendXml(mapping, 2, $"<one-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\"{constrained} />");
            return;
        }

        var unique = relation.Cardinality == Cardinality.OneToOne ? " unique=\"true\"" : string.Empty;
        var columns = relation.ColumnPairs.Select(pair => pair.Source.ColumnName ?? pair.Source.Property.Name).ToList();

        if (columns.Count == 0)
        {
            // Nobody said which column carries the key, so neither do we: NHibernate falls back to
            // the property name, and a name of our own making would be a claim the source never
            // made (rozhodnutí 008). Silence is allowed here precisely because the target fills in
            // the same thing we would have written - still its convention, so it is recorded.
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = navigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"No foreign key columns are known for the relation to '{relation.TargetEntity}'; the column attribute is left out and NHibernate derives the column from the property name (decision 012).",
            });
            AppendXml(mapping, 2, $"<many-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\"{unique} />");
            return;
        }

        if (columns.Count == 1)
        {
            AppendXml(mapping, 2, $"<many-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\" column=\"{columns[0]}\"{unique} />");
            return;
        }

        AppendXml(mapping, 2, $"<many-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\"{unique}>");

        foreach (var column in columns)
        {
            AppendXml(mapping, 3, $"<column name=\"{column}\" />");
        }

        AppendXml(mapping, 2, "</many-to-one>");
    }

    /// <summary>
    /// Writes the key of a collection. Those columns belong to the child table, so they reach the
    /// model only where both entities take part in the same conversion. Until then the key column of
    /// the owner is written, as before: leaving the attribute out is not silence here, because
    /// NHibernate then names the column id rather than anything derived from the owner
    /// (Collection.DefaultKeyColumnName in 5.7.0), which would change the mapping instead of
    /// declining to state it (decision 012).
    /// </summary>
    private void AppendKey(StringBuilder mapping, EntityMap entityMap, Relation relation)
    {
        var columns = relation.ColumnPairs.Select(pair => pair.Source.ColumnName ?? pair.Source.Property.Name).ToList();

        if (columns.Count == 0)
        {
            var ownerKey = entityMap.PrimaryKey?.Parts.FirstOrDefault()?.PropertyMap;
            var ownerColumn = ownerKey?.ColumnName ?? ownerKey?.Property.Name ?? "Id";

            // A third-degree convention of the tool, not silence: leaving the attribute out
            // would make NHibernate name the column id, which changes the mapping instead of
            // declining to state it (decision 012). Reported as such.
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = relation.SourceNavigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"No key columns are known for the collection towards '{relation.TargetEntity}'; the owner's key column '{ownerColumn}' is written, which is the tool's fallback, not a fact of the source (decision 012).",
            });
            AppendXml(mapping, 3, $"<key column=\"{ownerColumn}\" />");
            return;
        }

        if (columns.Count == 1)
        {
            AppendXml(mapping, 3, $"<key column=\"{columns[0]}\" />");
            return;
        }

        AppendXml(mapping, 3, "<key>");

        foreach (var column in columns)
        {
            AppendXml(mapping, 4, $"<column name=\"{column}\" />");
        }

        AppendXml(mapping, 3, "</key>");
    }

    /// <summary>
    /// Whether this entity takes its identity from the entity it references. NHibernate says so with
    /// the foreign generator, which decision 011 keeps under its own name together with the property
    /// it points at - a claim local to this entity, so no other one has to be at hand.
    /// </summary>
    private static bool SharesPrimaryKeyThrough(EntityMap entityMap, string navigationProperty)
    {
        var part = entityMap.PrimaryKey?.Parts.FirstOrDefault(p => p.SourceStrategyName == "foreign");

        if (part is null)
        {
            return false;
        }

        return !part.StrategyParameters.TryGetValue("property", out var through)
            || through == navigationProperty;
    }

    /// <summary>
    /// Appends a line to the XML mapping with indentation.
    /// </summary>
    private static void AppendXml(StringBuilder mappingResult, int indentLevels, string content, bool appendLine = true)
    {
        var indent = new string(' ', indentLevels * 4);
        if (appendLine)
        {
            mappingResult.AppendLine($"{indent}{content}");
        }
        else
        {
            mappingResult.Append($"{indent}{content}");
        }

    }

    /// <summary>
    /// Builds the property signature for C# code.
    /// Adds modifiers, type, name, getter/setter, and default value.
    /// </summary>
    private static string BuildPropertySignature(Property prop, bool isPrimaryKey = false)
    {
        var otherMods = new List<string>(prop.OtherModifiers ?? []);
        if (!otherMods.Any(m => m.Equals("virtual", StringComparison.OrdinalIgnoreCase)))
        {
            otherMods.Add("virtual");
        }

        var access = AccessModifierConvertor.ToModifierString(prop.AccessModifier);
        var modifiers = $"{access} {string.Join(' ', otherMods)}".Trim();
        var langType = prop.Type
            ?? throw new NotSupportedException($"Property '{prop.Name}' has no language type.");
        var typeName = CSharpTypeConvertor.ToString(langType);
        var type = (!isPrimaryKey && langType.IsNullable) ? $"{typeName}?" : typeName;

        var getterSetter = (prop.HasGetter || prop.HasSetter)
            ? $" {{ {(prop.HasGetter ? "get;" : "")}{(prop.HasSetter ? " set;" : "")} }}"
            : "";
        var defaultVal = string.IsNullOrWhiteSpace(prop.DefaultValue)
            ? ""
            : $" = {prop.DefaultValue};";

        return $"{modifiers} {type} {prop.Name}{getterSetter}{defaultVal}";
    }
}