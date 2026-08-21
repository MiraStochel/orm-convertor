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
        // NHibernate resolves a persistent class by namespace and assembly. The namespace is
        // above; the assembly is a contribution of the consumer project, like the project
        // file or the connection string, so it is left out rather than invented from the
        // namespace (decision 028). No record: it is absent from every mapping we generate,
        // which makes it a property of the format and not a finding about this conversion.
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

        // XML <class>: the bare class name. The namespace is on <hibernate-mapping>, which
        // BuildImports writes, and the assembly belongs there too - as an attribute the
        // consumer project supplies, because which assembly the class ends up in is a fact
        // of that project and not of the conversion (decision 028).
        var table = entityMap.Table ?? name; // default = class name
        var schema = entityMap.Schema ?? string.Empty;
        var schemaAttr = string.IsNullOrWhiteSpace(schema) ? string.Empty : $" schema=\"{schema}\"";

        AppendXml(artifact.Mapping, 1, $"<class name=\"{name}\" table=\"{table}\"{schemaAttr}>");
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

            AppendPropertyToCode(artifact.Code, entityMap, prop, isPrimaryKey: true);

            var facets = BuildColumnFacets(entityMap, propertyMap);
            if (facets.Count == 0)
            {
                AppendXml(artifact.Mapping, 2, $"<id name=\"{prop.Name}\" column=\"{columnName}\"{TypeAttribute(entityMap, propertyMap)}>");
            }
            else
            {
                AppendXml(artifact.Mapping, 2, $"<id name=\"{prop.Name}\"{TypeAttribute(entityMap, propertyMap)}>");
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

            AppendPropertyToCode(artifact.Code, entityMap, prop, isPrimaryKey: true);

            var facets = BuildColumnFacets(entityMap, propertyMap);
            if (facets.Count == 0)
            {
                AppendXml(artifact.Mapping, 3, $"<key-property name=\"{prop.Name}\" column=\"{columnName}\"{TypeAttribute(entityMap, propertyMap)} />");
            }
            else
            {
                AppendXml(artifact.Mapping, 3, $"<key-property name=\"{prop.Name}\"{TypeAttribute(entityMap, propertyMap)}>");
                AppendXml(artifact.Mapping, 4, $"<column name=\"{columnName}\" {string.Join(' ', facets)} />");
                AppendXml(artifact.Mapping, 3, "</key-property>");
            }
        }
        AppendXml(artifact.Mapping, 2, "</composite-id>");
    }

    /// <summary>
    /// The NHibernate type name of a property's claim, with the narrowing the conversion
    /// table states reported at the point of emission - the counterpart of the Narrowing
    /// channel the parser reads through (decision 010). A claim NHibernate 5.7.0 has no
    /// registered name for comes out under the nearest registered one with a loss record
    /// instead of as a name the framework would refuse.
    /// </summary>
    private string? ResolveNhType(EntityMap entityMap, PropertyMap propertyMap)
    {
        if (propertyMap.Type != null)
        {
            var naming = DatabaseTypeConvertor.ToNHibernate(
                propertyMap.Type.Value, propertyMap.IsUnicode, propertyMap.Length);

            if (naming.Narrowing is not null)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.XML,
                    Entity = entityMap.Entity.Name,
                    Property = propertyMap.Property.Name,
                    Category = MappingFactCategory.DatabaseType,
                    Reason = naming.Narrowing,
                });
            }

            return naming.Name;
        }

        // The database is never queried from here - the completion phase fills the model
        // before generation (decision 015). What is still missing at this point is guessed
        // from the language scalar; for anything else - a reference, a collection, an
        // unknown name - no claim is made and NHibernate decides itself.
        return propertyMap.Property.Type is { Category: LangTypeCategory.Scalar } langType
            ? DatabaseTypeConvertor.GuessFromScalarType(langType.ScalarType!.Value)
            : null;
    }

    /// <summary>
    /// The type attribute of an &lt;id&gt; or &lt;key-property&gt;, empty when there is
    /// nothing to claim - NHibernate then infers the type from the persistent class.
    /// </summary>
    private string TypeAttribute(EntityMap entityMap, PropertyMap propertyMap)
        => ResolveNhType(entityMap, propertyMap) is string type ? $" type=\"{type}\"" : string.Empty;

    /// <summary>
    /// Facets of a key column that NHibernate accepts only inside a nested &lt;column&gt; element.
    /// &lt;property&gt; can carry length, precision and scale as its own attributes, &lt;id&gt; and
    /// &lt;key-property&gt; cannot - so without the nested form a key column loses them silently.
    /// The literal SQL type of the source travels the same way: sql-type exists only on
    /// &lt;column&gt;, and what came in through it goes back out through it (decision 019).
    ///
    /// Nullability is deliberately left out: a column that carries the identifier is not
    /// nullable, and emitting not-null="false" there would produce a mapping that contradicts
    /// itself.
    ///
    /// An empty result means the compact form with a column attribute is enough, which keeps
    /// the output of a plain key exactly as it was.
    /// </summary>
    private List<string> BuildColumnFacets(EntityMap entityMap, PropertyMap propertyMap)
    {
        var facets = new List<string>();

        if (propertyMap.Length.HasValue)
        {
            facets.Add($"length=\"{propertyMap.Length.Value}\"");
        }

        if (PrecisionIsExpressible(entityMap, propertyMap))
        {
            facets.Add($"precision=\"{propertyMap.Precision!.Value}\"");
        }

        if (propertyMap.Scale.HasValue)
        {
            facets.Add($"scale=\"{propertyMap.Scale.Value}\"");
        }

        if (propertyMap.SourceSqlType is not null)
        {
            facets.Add($"sql-type=\"{propertyMap.SourceSqlType}\"");
        }

        return facets;
    }

    /// <summary>
    /// The mapping schema of NHibernate declares precision as a positive integer, so zero -
    /// legal in the source as the sub-second precision of a date-time column - has no place
    /// to go: the framework itself refuses the document. The fact is dropped and reported
    /// as a loss (decision 004); carrying it would take a concrete SQL type on the column,
    /// which belongs to the database type neutralization (open item).
    /// </summary>
    private bool PrecisionIsExpressible(EntityMap entityMap, PropertyMap propertyMap)
    {
        if (!propertyMap.Precision.HasValue)
        {
            return false;
        }

        if (propertyMap.Precision.Value >= 1)
        {
            return true;
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.XML,
            Entity = entityMap.Entity.Name,
            Property = propertyMap.Property.Name,
            Category = MappingFactCategory.PrecisionAndScale,
            Reason = $"NHibernate's mapping schema admits only a positive precision, so precision {propertyMap.Precision.Value} cannot be expressed and is dropped (decision 004).",
        });

        return false;
    }

    /// <summary>
    /// Builds C# properties and XML <property> tags.
    /// Primary and foreign keys are handled separately.
    /// </summary>
    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        var version = AppendVersion(entityMap, artifact);

        foreach (var pm in entityMap.PropertyMaps)
        {
            if (pm == version)
            {
                continue; // emitted as <version> above
            }

            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == pm.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (entityMap.Relations.Any(r => r.SourceNavigationProperty == pm.Property.Name))
            {
                continue; // navigation property – handled in BuildForeignKey
            }

            AppendPropertyToCode(artifact.Code, entityMap, pm.Property);

            if (pm.Property.Type is { Category: LangTypeCategory.Collection })
            {
                // A collection with no relation behind it cannot become a <property> - NHibernate
                // would refuse to infer a type for it - and the value-collection form would need
                // key and element columns nobody stated. The class keeps the property, the
                // mapping leaves it out, and NHibernate ignores an unmapped member.
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.XML,
                    Entity = entityMap.Entity.Name,
                    Property = pm.Property.Name,
                    Reason = "The collection has no relation behind it, so there is nothing to build its mapping from; "
                        + "the property stays on the class and the mapping leaves it unmapped.",
                });
                continue;
            }

            AppendPropertyToXml(artifact.Mapping, entityMap, pm);
        }
    }

    /// <summary>
    /// Writes the version column (decision 030) as the &lt;version&gt; element, which the mapping
    /// schema places between the identifier and the properties - hence the head of the
    /// properties step, right after BuildPrimaryKey wrote the identifier. The element admits
    /// no length, precision, scale or not-null attributes of its own, so any of those facts
    /// forces the nested &lt;column&gt; form, the same way sql-type does on &lt;property&gt;.
    ///
    /// A binary version - the shape of a SQL Server rowversion column - is a value NHibernate
    /// cannot increment itself, so generated="always" is forced by the framework the way
    /// virtual is: the database produces the value and NHibernate reads it back. A numeric or
    /// date-time version stays framework-managed, which is the element's default.
    ///
    /// The schema admits a single &lt;version&gt; element, on a plain mapped property; a version
    /// flag anywhere else - a second flagged property, a key part, a navigation - is dropped
    /// with a loss record (decision 004).
    /// </summary>
    /// <returns>The property map emitted as the version, or null when the entity has none.</returns>
    private PropertyMap? AppendVersion(EntityMap entityMap, EntityArtifact artifact)
    {
        var flagged = entityMap.PropertyMaps.Where(pm => pm.IsVersion).ToList();

        var version = flagged.FirstOrDefault(pm =>
            entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == pm.Property.Name) != true
            && !entityMap.Relations.Any(r => r.SourceNavigationProperty == pm.Property.Name)
            && pm.Property.Type is not { Category: LangTypeCategory.Collection });

        foreach (var dropped in flagged.Where(pm => pm != version))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = dropped.Property.Name,
                Category = MappingFactCategory.VersionColumn,
                Reason = "NHibernate maps the row version with a single <version> element on a plain "
                    + "mapped property; the version flag of this property cannot be expressed there "
                    + "and is dropped (decision 004).",
            });
        }

        if (version is null)
        {
            return null;
        }

        AppendPropertyToCode(artifact.Code, entityMap, version.Property);

        var attrs = new List<string> { $"name=\"{version.Property.Name}\"" };

        if (version.Type is DatabaseType.Binary or DatabaseType.VarBinary or DatabaseType.Blob)
        {
            attrs.Add("generated=\"always\"");
        }

        var typeAttr = version.Type.HasValue
            ? $"type=\"{ResolveNhType(entityMap, version)}\""
            : null;

        string? notNull = null;

        if (version.IsNullable.HasValue)
        {
            notNull = $"not-null=\"{(!version.IsNullable.Value).ToString().ToLowerInvariant()}\"";
        }
        else if (version.Property.Type is { IsNullable: false })
        {
            notNull = "not-null=\"true\"";
        }

        var columnFacets = new List<string>();

        if (notNull is not null)
        {
            columnFacets.Add(notNull);
        }

        if (version.Length.HasValue)
        {
            columnFacets.Add($"length=\"{version.Length.Value}\"");
        }

        if (PrecisionIsExpressible(entityMap, version))
        {
            columnFacets.Add($"precision=\"{version.Precision!.Value}\"");
        }

        if (version.Scale.HasValue)
        {
            columnFacets.Add($"scale=\"{version.Scale.Value}\"");
        }

        if (version.SourceSqlType is not null)
        {
            columnFacets.Add($"sql-type=\"{version.SourceSqlType}\"");
        }

        if (columnFacets.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(version.ColumnName))
            {
                attrs.Add($"column=\"{version.ColumnName}\"");
            }

            if (typeAttr is not null)
            {
                attrs.Add(typeAttr);
            }

            AppendXml(artifact.Mapping, 2, $"<version {string.Join(' ', attrs)} />");
            return version;
        }

        if (typeAttr is not null)
        {
            attrs.Add(typeAttr);
        }

        AppendXml(artifact.Mapping, 2, $"<version {string.Join(' ', attrs)}>");
        AppendXml(artifact.Mapping, 3, $"<column name=\"{version.ColumnName ?? version.Property.Name}\" {string.Join(' ', columnFacets)} />");
        AppendXml(artifact.Mapping, 2, "</version>");
        return version;
    }

    /// <summary>
    /// Builds C# foreign key properties and XML <one-to-one>, <many-to-one> or collection
    /// (<bag>, <set>) tags, with <one-to-many> or <many-to-many> inside the collection.
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

            AppendPropertyToCode(artifact.Code, entityMap, propertyMap.Property); // navigation property in C#

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

            AppendPropertyToCode(artifact.Code, entityMap, propertyMap.Property);
            AppendCollection(artifact.Mapping, entityMap, relation, propertyMap);
        }
    }

    /// <summary>
    /// Writes the mapping of a collection. The element follows the kind the model carries
    /// (decision 014): Set is &lt;set&gt;, everything else is &lt;bag&gt; - &lt;list&gt; would need an
    /// index column the model does not keep, and &lt;bag&gt; is NHibernate's own shape for a
    /// list-typed property without one, so no claim is added and nothing is reported here;
    /// where a &lt;list&gt; was the source, its parser already reported the dropped order.
    ///
    /// The attributes carry only what the model states: inverse="true" is derived from the
    /// shape of both sides rather than assumed (see <see cref="CollectionIsInverse"/>), and
    /// cascade is never written - the source stated no cascade behavior, so the target's
    /// default (none) applies. A many-to-many that survived the junction synthesis of
    /// decision 005 writes back the junction facts the source stated - the collection
    /// table, its schema and the far side's columns - because without the table attribute
    /// the mapping is invalid, not merely poorer; what stays missing is already recorded
    /// by the resolution phase.
    /// </summary>
    private void AppendCollection(StringBuilder mapping, EntityMap entityMap, Relation relation, PropertyMap propertyMap)
    {
        var element = propertyMap.Property.Type is { CollectionKind: CollectionKind.Set } ? "set" : "bag";

        var attrs = new List<string> { $"name=\"{propertyMap.Property.Name}\"" };

        var junction = relation.Cardinality == Cardinality.ManyToMany ? StatedJunctionFacts(relation) : null;

        if (junction?.Table is not null)
        {
            attrs.Add($"table=\"{junction.Table}\"");
        }

        if (junction?.Schema is not null)
        {
            attrs.Add($"schema=\"{junction.Schema}\"");
        }

        if (CollectionIsInverse(entityMap, relation))
        {
            attrs.Add("inverse=\"true\"");
        }

        AppendXml(mapping, 2, $"<{element} {string.Join(' ', attrs)}>");
        AppendKey(mapping, entityMap, relation);

        if (relation.Cardinality == Cardinality.OneToMany)
        {
            AppendXml(mapping, 3, $"<one-to-many class=\"{relation.TargetEntity}\" />");
        }
        else // ManyToMany
        {
            AppendManyToMany(mapping, relation, junction);
        }

        AppendXml(mapping, 2, $"</{element}>");
    }

    /// <summary>
    /// Whether the write on the foreign key belongs to a counterpart the conversion knows:
    /// the target entity is part of it and carries an owning reference back here over the
    /// same columns. Only then does inverse="true" restate the model - this side holds no
    /// column and the far side maps the association. Without such a counterpart the
    /// attribute is left out, and NHibernate lets the collection write the key: with
    /// inverse="true" and nobody on the other side, the association would never persist.
    /// A surviving many-to-many gets no inverse either way - which side manages the
    /// junction rows is a claim nobody made.
    /// </summary>
    private bool CollectionIsInverse(EntityMap entityMap, Relation relation)
    {
        if (relation.Cardinality != Cardinality.OneToMany || relation.Role != RelationRole.Inverse)
        {
            return false;
        }

        var target = FindEntityMap(relation.TargetEntity);

        return target is not null && target.Relations.Any(r =>
            r is { Role: RelationRole.Owning, Cardinality: Cardinality.ManyToOne }
            && FindEntityMap(r.TargetEntity) == entityMap
            && DescribesTheSameForeignKey(r, relation));
    }

    /// <summary>
    /// Both relations describe the same foreign key when both know their columns - the
    /// source side of every pair is the child's column on either reading (decision 012).
    /// A side with unresolved pairs cannot disagree, so it does not veto the match; the
    /// check only tells apart two distinct foreign keys between the same pair of entities.
    /// </summary>
    private static bool DescribesTheSameForeignKey(Relation owning, Relation collection)
    {
        if (owning.ColumnPairs.Count == 0 || collection.ColumnPairs.Count == 0)
        {
            return true;
        }

        return owning.ColumnPairs.Count == collection.ColumnPairs.Count
            && owning.ColumnPairs.Zip(collection.ColumnPairs).All(pair =>
                string.Equals(
                    pair.First.Source.ColumnName ?? pair.First.Source.Property.Name,
                    pair.Second.Source.ColumnName ?? pair.Second.Source.Property.Name,
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The far side of a many-to-many that survived the junction synthesis: the columns the
    /// source stated for the &lt;many-to-many&gt; element go back out verbatim - they belong to
    /// the junction table, so there is no key to pair them with and no pairing to wait for.
    /// </summary>
    private static void AppendManyToMany(StringBuilder mapping, Relation relation, JunctionFacts? junction)
    {
        var columns = junction?.TargetColumns ?? [];

        if (columns.Count == 0)
        {
            AppendXml(mapping, 3, $"<many-to-many class=\"{relation.TargetEntity}\" />");
            return;
        }

        if (columns.Count == 1)
        {
            AppendXml(mapping, 3, $"<many-to-many class=\"{relation.TargetEntity}\" column=\"{columns[0]}\" />");
            return;
        }

        AppendXml(mapping, 3, $"<many-to-many class=\"{relation.TargetEntity}\">");

        foreach (var column in columns)
        {
            AppendXml(mapping, 4, $"<column name=\"{column}\" />");
        }

        AppendXml(mapping, 3, "</many-to-many>");
    }

    private static PropertyMap? FindNavigationPropertyMap(EntityMap em, Relation relation)
        => relation.SourceNavigationProperty is null
            ? null
            : em.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

    /// <summary>
    /// Whether a reference or one-to-one relation of the entity claims the column through
    /// its resolved pairs. Collections stay out: their key columns belong to the child table.
    /// </summary>
    private static bool ColumnBelongsToRelation(EntityMap em, string column)
        => em.Relations
            .Where(r => r.Cardinality is Cardinality.OneToOne or Cardinality.ManyToOne)
            .SelectMany(r => r.ColumnPairs)
            .Any(pair => string.Equals(
                pair.Source.ColumnName ?? pair.Source.Property.Name, column, StringComparison.OrdinalIgnoreCase));

    private static bool IsPrimaryKeyColumn(EntityMap em, string column)
        => em.PrimaryKey?.Parts.Any(p => string.Equals(
            p.PropertyMap.ColumnName ?? p.PropertyMap.Property.Name, column, StringComparison.OrdinalIgnoreCase)) == true;

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
    private void AppendPropertyToCode(StringBuilder codeResult, EntityMap entityMap, Property prop, bool isPrimaryKey = false)
    {
        var declaration = BuildPropertySignature(entityMap, prop, isPrimaryKey);
        codeResult.AppendLine($"    {declaration}");
        codeResult.AppendLine();
    }

    /// <summary>
    /// Appends a property to the XML mapping. A column may be mapped writable only once:
    /// when a relation of the entity claims the same column through its pairs, the scalar
    /// property is mapped read-only and the association keeps the write, otherwise
    /// NHibernate refuses the mapping as a repeated column.
    ///
    /// A literal SQL type of the source forces the nested form: sql-type exists only on a
    /// &lt;column&gt; element (decision 019), so the column facts move onto it and the
    /// property element keeps what belongs to the property - name and the NHibernate type.
    /// </summary>
    private void AppendPropertyToXml(StringBuilder mappingResult, EntityMap entityMap, PropertyMap propertyMap)
    {
        var prop = propertyMap.Property;

        var attrs = new List<string> { $"name=\"{prop.Name}\"" };

        if (ColumnBelongsToRelation(entityMap, propertyMap.ColumnName ?? prop.Name))
        {
            attrs.Add("insert=\"false\"");
            attrs.Add("update=\"false\"");
        }

        string? notNull = null;

        if (propertyMap.IsNullable.HasValue)
        {
            notNull = $"not-null=\"{(!propertyMap.IsNullable.Value).ToString().ToLowerInvariant()}\"";
        }
        else if (prop.Type is { IsNullable: false })
        {
            // The language side is the fallback claim; a property with no stated type
            // says nothing about nullability either.
            notNull = "not-null=\"true\"";
        }

        var typeAttr = propertyMap.Type.HasValue
            ? $"type=\"{ResolveNhType(entityMap, propertyMap)}\""
            : null;

        var sizeFacets = new List<string>();

        if (PrecisionIsExpressible(entityMap, propertyMap))
        {
            sizeFacets.Add($"precision=\"{propertyMap.Precision!.Value}\"");
        }

        if (propertyMap.Scale.HasValue)
        {
            sizeFacets.Add($"scale=\"{propertyMap.Scale.Value}\"");
        }

        if (propertyMap.Length.HasValue)
        {
            sizeFacets.Add($"length=\"{propertyMap.Length.Value}\"");
        }

        if (propertyMap.SourceSqlType is null)
        {
            // The compact form: everything as attributes of the property element itself.
            if (!string.IsNullOrWhiteSpace(propertyMap.ColumnName))
            {
                attrs.Add($"column=\"{propertyMap.ColumnName}\"");
            }

            if (notNull is not null)
            {
                attrs.Add(notNull);
            }

            if (typeAttr is not null)
            {
                attrs.Add(typeAttr);
            }

            attrs.AddRange(sizeFacets);

            AppendXml(mappingResult, 2, $"<property {string.Join(' ', attrs)} />");
            return;
        }

        if (typeAttr is not null)
        {
            attrs.Add(typeAttr);
        }

        var columnAttrs = new List<string> { $"name=\"{propertyMap.ColumnName ?? prop.Name}\"" };

        if (notNull is not null)
        {
            columnAttrs.Add(notNull);
        }

        columnAttrs.AddRange(sizeFacets);
        columnAttrs.Add($"sql-type=\"{propertyMap.SourceSqlType}\"");

        AppendXml(mappingResult, 2, $"<property {string.Join(' ', attrs)}>");
        AppendXml(mappingResult, 3, $"<column {string.Join(' ', columnAttrs)} />");
        AppendXml(mappingResult, 2, "</property>");
    }

    /// <summary>
    /// Writes the generator of a simple key. The class is selected in three steps (decision
    /// 021): from the canonical facts where they determine it, then from the source's own name
    /// where NHibernate knows it and it means the mechanism the part carries, and only then
    /// the canonical name of the strategy with a loss record. Canonical parameters (decision
    /// 020) go in as nested elements under the names of the selected generator: without them
    /// the mapping names no sequence and the target falls back to its own default, so it
    /// compiles and fails at runtime. A strategy that stayed on the escape path takes its
    /// literal parameters with it, so foreign keeps its property and the shared-key signal of
    /// decision 012 closes on the output side too. assigned written for a strategy nobody
    /// stated is reported as a convention of the target (decision 010).
    /// </summary>
    private void AppendGenerator(StringBuilder mapping, EntityMap entityMap, PrimaryKeyPart part)
    {
        var generatorClass = SelectGeneratorClass(part);

        if (part.SourceStrategyName is not null && part.SourceStrategyName != generatorClass)
        {
            // The loss says why the name is dropped, not merely that the spelling differs.
            var why = !PrimaryKeyStrategyConvertor.Knows(part.SourceStrategyName)
                ? "the target framework does not know a generator of that name"
                : PrimaryKeyStrategyConvertor.FromNHibernate(part.SourceStrategyName) != part.Strategy
                    ? $"that name means a different mechanism than the {part.Strategy} the key part carries"
                    : "the facts the key part carries select the generator";

            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = part.PropertyMap.Property.Name,
                Category = MappingFactCategory.PrimaryKeyStrategy,
                Reason = $"The source called the generator '{part.SourceStrategyName}'; '{generatorClass}' is written because {why} (decision 021).",
            });
        }
        else if (part.Strategy == PrimaryKeyStrategy.Unspecified && part.SourceStrategyName is null)
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

        var parameters = TranslateGeneratorParameters(entityMap, part, generatorClass);

        if (parameters.Count == 0)
        {
            AppendXml(mapping, 3, $"<generator class=\"{generatorClass}\" />");
            return;
        }

        AppendXml(mapping, 3, $"<generator class=\"{generatorClass}\">");

        foreach (var (name, value) in parameters)
        {
            AppendXml(mapping, 4, $"<param name=\"{name}\">{value}</param>");
        }

        AppendXml(mapping, 3, "</generator>");
    }

    /// <summary>
    /// The three steps of decision 021, in order: facts, then the source's name as an arbiter
    /// between spellings the model does not tell apart, then the canonical name.
    /// </summary>
    private static string SelectGeneratorClass(PrimaryKeyPart part)
    {
        // Facts before names: HiLo is the one mechanism NHibernate writes as two generators,
        // and where the counter lives is a parameter of the model, not a name of the source -
        // so this branch holds for a model that never carried a name, e.g. one parsed from JPA.
        if (part.Strategy == PrimaryKeyStrategy.HiLo)
        {
            return part.StrategyParameters.ContainsKey(GeneratorParameter.SequenceName) ? "seqhilo" : "hilo";
        }

        // The source's name never decides what is generated, only how it is spelled: it must
        // be a generator NHibernate registers AND mean the mechanism the part carries, which
        // is what keeps a custom generator class or a foreign ecosystem's name out.
        if (part.SourceStrategyName is { } sourceName
            && PrimaryKeyStrategyConvertor.Knows(sourceName)
            && PrimaryKeyStrategyConvertor.FromNHibernate(sourceName) == part.Strategy)
        {
            return sourceName;
        }

        return PrimaryKeyStrategyConvertor.ToNHibernate(part.Strategy);
    }

    /// <summary>
    /// Translates canonical parameters (decision 020) into the vocabulary of the selected
    /// generator - which generator is written decides what its parameters are called, so the
    /// table is keyed by the class, not the mechanism. Emission follows the declaration order
    /// of the vocabulary, a stable property of the model rather than of the input (S2). A
    /// parameter the selected generator cannot express is a loss record, and so is a literal
    /// parameter outside the escape path: writing a word we never understood under a generator
    /// we selected would be a claim about its meaning.
    /// </summary>
    private List<(string Name, string Value)> TranslateGeneratorParameters(
        EntityMap entityMap, PrimaryKeyPart part, string generatorClass)
    {
        var result = new List<(string Name, string Value)>();

        // The escape path proper: the strategy is unrecognized and the source's own generator
        // is being written, so its parameters mean exactly what the source meant by them.
        if (part.Strategy == PrimaryKeyStrategy.Unspecified && generatorClass == part.SourceStrategyName)
        {
            foreach (var (name, value) in part.SourceStrategyParameters)
            {
                result.Add((name, value));
            }

            return result;
        }

        var schema = part.StrategyParameters.GetValueOrDefault(GeneratorParameter.Schema);
        var schemaConsumed = false;

        foreach (var parameter in Enum.GetValues<GeneratorParameter>())
        {
            if (parameter == GeneratorParameter.Schema || !part.StrategyParameters.TryGetValue(parameter, out var value))
            {
                // The schema travels with the name it qualifies; left over, it is reported below.
                continue;
            }

            switch (parameter, generatorClass)
            {
                case (GeneratorParameter.SequenceName, "sequence" or "seqhilo"):
                    result.Add(("sequence", Qualify(schema, value)));
                    schemaConsumed = schema is not null;
                    break;
                case (GeneratorParameter.CounterTable, "hilo"):
                    result.Add(("table", Qualify(schema, value)));
                    schemaConsumed = schema is not null;
                    break;
                case (GeneratorParameter.CounterValueColumn, "hilo"):
                    result.Add(("column", value));
                    break;
                case (GeneratorParameter.BlockSize, "seqhilo" or "hilo") when int.TryParse(value, out var blockSize):
                    // BlockSize is the number of values in the block; max_lo is the highest
                    // low value, one less - the same shift the parser makes, in reverse.
                    result.Add(("max_lo", (blockSize - 1).ToString()));
                    break;
                default:
                    ReportGeneratorParameterLoss(entityMap, part, parameter.ToString(), value, generatorClass);
                    break;
            }
        }

        if (schema is not null && !schemaConsumed)
        {
            ReportGeneratorParameterLoss(
                entityMap, part, nameof(GeneratorParameter.Schema), schema, generatorClass);
        }

        foreach (var (name, value) in part.SourceStrategyParameters)
        {
            ReportGeneratorParameterLoss(entityMap, part, name, value, generatorClass);
        }

        return result;
    }

    private static string Qualify(string? schema, string name)
        => schema is null ? name : $"{schema}.{name}";

    private void ReportGeneratorParameterLoss(
        EntityMap entityMap, PrimaryKeyPart part, string parameter, string value, string generatorClass)
    {
        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.XML,
            Entity = entityMap.Entity.Name,
            Property = part.PropertyMap.Property.Name,
            Category = MappingFactCategory.PrimaryKeyStrategy,
            Reason = $"The generator parameter '{parameter}' ('{value}') has no counterpart on NHibernate's '{generatorClass}' generator and is dropped (decision 020).",
        });
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
            // No column to name here: either the far side holds the key, or both entities share
            // the primary key, and constrained is how NHibernate says the identity comes from
            // there. The inverse side of a foreign key still points at the property holding it,
            // which is what property-ref names.
            var constrained = sharedKey ? " constrained=\"true\"" : string.Empty;
            var propertyRef = sharedKey ? string.Empty : PropertyRefAttribute(entityMap, relation);
            AppendXml(mapping, 2, $"<one-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\"{propertyRef}{constrained} />");
            return;
        }

        var unique = relation.Cardinality == Cardinality.OneToOne ? " unique=\"true\"" : string.Empty;
        var columns = relation.ColumnPairs.Select(pair => pair.Source.ColumnName ?? pair.Source.Property.Name).ToList();

        // The identifier owns the write on its columns; a reference over key columns -
        // a foreign key inside the primary key - is therefore mapped read-only, otherwise
        // NHibernate refuses the repeated column.
        var readOnly = columns.Any(column => IsPrimaryKeyColumn(entityMap, column))
            ? " insert=\"false\" update=\"false\""
            : string.Empty;

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
            AppendXml(mapping, 2, $"<many-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\" column=\"{columns[0]}\"{unique}{readOnly} />");
            return;
        }

        AppendXml(mapping, 2, $"<many-to-one name=\"{navigationProperty}\" class=\"{relation.TargetEntity}\"{unique}{readOnly}>");

        foreach (var column in columns)
        {
            AppendXml(mapping, 3, $"<column name=\"{column}\" />");
        }

        AppendXml(mapping, 2, "</many-to-one>");
    }

    /// <summary>
    /// The property-ref of an inverse one-to-one names the property of the owning entity that
    /// holds the foreign key - the counterpart navigation. The model keeps no such value
    /// (entities reference each other by name only, decision 001), so it is derived from the
    /// counterpart relation once the resolution phase has made the far side reachable
    /// (decision 012). A counterpart that takes its identity from this entity is the shared
    /// primary key case: there the association joins over the identifiers, which is exactly
    /// what a bare &lt;one-to-one&gt; says, so the attribute stays out and nothing is reported.
    /// Where the target takes part in the conversion but no owning one-to-one points back,
    /// the omission is not silence - a bare &lt;one-to-one&gt; claims the shared-key join, not
    /// the foreign key the inverse role asserts - and it is recorded as incompleteness.
    /// A target outside the conversion is already recorded by the resolution phase.
    /// </summary>
    private string PropertyRefAttribute(EntityMap entityMap, Relation relation)
    {
        var target = FindEntityMap(relation.TargetEntity);

        if (target is null)
        {
            return string.Empty;
        }

        var counterpart = target.Relations.FirstOrDefault(r =>
            r is { Role: RelationRole.Owning, Cardinality: Cardinality.OneToOne, SourceNavigationProperty: not null }
            && FindEntityMap(r.TargetEntity) == entityMap);

        if (counterpart is null)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Entity = entityMap.Entity.Name,
                Property = relation.SourceNavigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"'{relation.TargetEntity}' takes part in the conversion but carries no owning "
                    + $"one-to-one back to '{entityMap.Entity.Name}', so property-ref cannot name the "
                    + "property holding the foreign key; without the attribute NHibernate joins the "
                    + "association over the primary keys (decision 012).",
            });
            return string.Empty;
        }

        if (SharesPrimaryKeyThrough(target, counterpart.SourceNavigationProperty!))
        {
            return string.Empty;
        }

        return $" property-ref=\"{counterpart.SourceNavigationProperty}\"";
    }

    /// <summary>
    /// Writes the key of a collection. Those columns belong to the child table, so they pair up
    /// only where both entities take part in the same conversion; where the pairs never resolved -
    /// a target outside the conversion, or a many-to-many whose columns belong to the junction
    /// table (decision 005) - the columns the source stated go back out verbatim. Only when the
    /// source stated none is the key column of the owner written, as before: leaving the attribute
    /// out is not silence here, because NHibernate then names the column id rather than anything
    /// derived from the owner (Collection.DefaultKeyColumnName in 5.7.0), which would change the
    /// mapping instead of declining to state it (decision 012).
    /// </summary>
    private void AppendKey(StringBuilder mapping, EntityMap entityMap, Relation relation)
    {
        var columns = relation.ColumnPairs.Select(pair => pair.Source.ColumnName ?? pair.Source.Property.Name).ToList();

        if (columns.Count == 0)
        {
            columns = StatedForeignKeyColumns(relation)?.ToList() ?? [];
        }

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

        return !part.SourceStrategyParameters.TryGetValue("property", out var through)
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
    private string BuildPropertySignature(EntityMap entityMap, Property prop, bool isPrimaryKey = false)
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
        var defaultVal = string.IsNullOrWhiteSpace(prop.DefaultValue)
            ? ""
            : $" = {prop.DefaultValue};";

        if (langType.Category == LangTypeCategory.Collection)
        {
            // NHibernate replaces a persistent collection with its own implementation when
            // loading the entity, so the declaration has to be the interface - a framework
            // enforcement of the same kind as virtual (decision 035). The concrete name the
            // shared conversion renders still types the initializer.
            var iface = langType.CollectionKind == CollectionKind.Set ? "ISet" : "IList";
            defaultVal = RewriteCollectionInitializer(entityMap, prop, typeName);
            typeName = $"{iface}{typeName[typeName.IndexOf('<')..]}";
        }

        var type = (!isPrimaryKey && langType.IsNullable) ? $"{typeName}?" : typeName;

        var getterSetter = (prop.HasGetter || prop.HasSetter)
            ? $" {{ {(prop.HasGetter ? "get;" : "")}{(prop.HasSetter ? " set;" : "")} }}"
            : "";

        return $"{modifiers} {type} {prop.Name}{getterSetter}{defaultVal}";
    }

    /// <summary>
    /// A target-typed initializer of the source does not compile once the declaration
    /// becomes an interface - new() not at all, [] not over ISet&lt;T&gt; - so a stated
    /// initializer is rewritten to the empty concrete instantiation. The empty forms mean
    /// exactly that and are replaced silently; anything else loses its content in the
    /// rewrite and is reported as a loss (decision 004).
    /// </summary>
    private string RewriteCollectionInitializer(EntityMap entityMap, Property prop, string concreteType)
    {
        if (string.IsNullOrWhiteSpace(prop.DefaultValue))
        {
            return string.Empty;
        }

        var rewritten = $"new {concreteType}()";

        static string Strip(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

        var stated = Strip(prop.DefaultValue);
        if (stated != "[]" && stated != "new()" && stated != Strip(rewritten))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.CSharpEntity,
                Entity = entityMap.Entity.Name,
                Property = prop.Name,
                Reason = $"NHibernate requires the collection declared by its interface, over which the "
                    + $"initializer '{prop.DefaultValue}' does not survive; the empty '{rewritten}' is "
                    + "written instead (decision 035).",
            });
        }

        return $" = {rewritten};";
    }
}