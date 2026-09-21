using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;

namespace MyBatisWrappers;

/// <summary>
/// Reads the mapping half of a MyBatis mapper interface: the @Results of its methods
/// (decision 084). Like the XML mapper, the interface is a mapping and a query at once - the
/// @Select on the same method is read by <see cref="MyBatisAnnotationQueryParser"/> on the
/// query pass (decision 081).
///
/// Its second job is the one the query pass cannot do for itself: the signatures of the
/// methods go into the shared context. The signature is the only place the type of a MyBatis
/// query parameter lives - a mapper writes #{id} and states nothing about it - and the
/// entity pass runs whole before the query pass, so the signature is there whichever order
/// the units arrived in.
/// </summary>
public sealed class MyBatisMapperInterfaceParser(AbstractEntityBuilder entityBuilder, MyBatisReadingContext context)
    : MyBatisMappingParser(entityBuilder)
{
    /// <summary>The annotations that declare a statement, whose ids share one collection.</summary>
    private static readonly string[] StatementAnnotations =
    [
        "Select", "Insert", "Update", "Delete",
        "SelectProvider", "InsertProvider", "UpdateProvider", "DeleteProvider",
    ];

    protected override ConversionContentType Artifact => ConversionContentType.JavaQuery;

    public override bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.JavaQuery;

    public override IReadOnlyCollection<EntityMap> Parse(string source)
    {
        JavaCompilationUnit unit;
        try
        {
            unit = JavaClassReader.Read(source, Limits);
        }
        catch (JavaSyntaxError error)
        {
            Report(ConversionRecordKind.Failure, null, null, null,
                $"The Java source could not be read at line {error.Line}, column {error.Column}: {error.Message}.");
            return [];
        }
        catch (JavaInputTooDeep tooDeep)
        {
            Report(ConversionRecordKind.Failure, null, null, null,
                NestingDepthGuard.Reason(tooDeep.Token, Limits));
            return [];
        }

        var read = new List<EntityMap>();

        foreach (var mapper in unit.Interfaces)
        {
            var namespaceName = string.IsNullOrEmpty(unit.Package) ? mapper.Name : $"{unit.Package}.{mapper.Name}";

            foreach (var method in mapper.Methods)
            {
                context.DeclareMethod(namespaceName, SignatureOf(method));

                foreach (var annotation in method.Annotations.Where(a => StatementAnnotations.Contains(a.SimpleName)))
                {
                    context.DeclareStatement(namespaceName, method.Name, MyBatisStatementForm.Annotation);
                    break;
                }

                if (ReadResults(method) is { } entityMap && !read.Contains(entityMap))
                {
                    read.Add(entityMap);
                }

                // A mapper interface that carries no @Results still says something about the
                // conversion: it names the entity its statements materialize and it types
                // their parameters, which is the one fact a MyBatis mapper never states
                // (decision 084). So the entities its methods name are what the unit yielded,
                // and the ordinary interface - the shape of every MyBatis project, whose
                // statements live in the XML mapper - is not called barren for it
                // (decision 066). Only entities some unit already declared count: naming a
                // class is not declaring one.
                if (MyBatisTypeNames.MaterializedClass(method.ReturnType) is { } materialized
                    && entityBuilder.EntityMaps.FirstOrDefault(em =>
                        string.Equals(em.Entity.Name, materialized, StringComparison.Ordinal)) is { } named
                    && !read.Contains(named))
                {
                    read.Add(named);
                }
            }
        }

        return read;
    }

    private static MyBatisMethodSignature SignatureOf(JavaMethod method)
    {
        var parameters = method.Parameters.Select(parameter => new MyBatisMethodParameter(
            parameter.Annotations.FirstOrDefault(a => a.SimpleName == "Param")?.String("value")
                // MyBatis derives the name from the declaration only where there is one
                // parameter; from the second on it knows arg0, arg1 without @Param, and a
                // statement cannot have written those.
                ?? (method.Parameters.Count == 1 ? parameter.Name : null),
            parameter.Type)).ToList();

        return new MyBatisMethodSignature(method.Name, method.ReturnType, parameters);
    }

    /// <summary>
    /// The @Results of one method: the annotated spelling of a &lt;resultMap&gt;. The entity
    /// it maps is the one the method materializes, which is the method's return type - the
    /// annotated form has nowhere else to name it.
    /// </summary>
    private EntityMap? ReadResults(JavaMethod method)
    {
        foreach (var dropped in method.Annotations.Where(a => a.SimpleName is "ConstructorArgs" or "TypeDiscriminator"))
        {
            Report(ConversionRecordKind.Loss, null, null, null,
                $"The method '{method.Name}' carries @{dropped.SimpleName}, which the intermediate representation does not carry - it "
                + "maps columns onto properties and holds no choice of type and no constructor mapping; it was dropped.");
        }

        if (method.Annotations.FirstOrDefault(a => a.SimpleName == "Results") is not { } results)
        {
            return null;
        }

        if (MyBatisTypeNames.MaterializedClass(method.ReturnType) is not { } className)
        {
            Report(ConversionRecordKind.Incompleteness, null, null, null,
                $"The method '{method.Name}' carries @Results and returns '{method.ReturnType}', which names no class of the "
                + "conversion, so there is no entity its pairs belong to; they were not read.");
            return null;
        }

        var entityMap = Entity(className);
        var identity = new List<string>();

        // The indexer answers "value" for the single-value form too, so @Results({ … }) and
        // @Results(value = { … }) read the same.
        foreach (var result in results["value"]?.AsList ?? [])
        {
            if (result.Annotation is not { } mapping || mapping.SimpleName != "Result")
            {
                continue;
            }

            ReadResult(entityMap, mapping, identity);
        }

        ReportIdentity(entityMap, identity);

        return entityMap;
    }

    private void ReadResult(EntityMap entityMap, JavaAnnotation result, List<string> identity)
    {
        var property = result.String("property");
        var column = result.String("column");

        var many = result["many"]?.Annotation;
        var one = result["one"]?.Annotation;

        if (many is not null || one is not null)
        {
            ReadNavigation(entityMap, property, many ?? one!, isCollection: many is not null);
            return;
        }

        if (result.Boolean("id") == true && column is not null)
        {
            identity.Add(column);
        }

        WriteColumn(
            entityMap,
            property!,
            column,
            result["javaType"] is { Kind: JavaAnnotationValueKind.ClassLiteral } javaType ? javaType.Text : null,
            result["jdbcType"]?.SimpleName);
    }

    /// <summary>
    /// The annotated navigation: @Many for a collection, @One for a reference. Neither names
    /// the entity on the far side - MyBatis takes it from the declaration of the property -
    /// so it is read out of the domain class, and the select they carry is how the value is
    /// loaded, which the model does not hold.
    /// </summary>
    private void ReadNavigation(EntityMap entityMap, string? property, JavaAnnotation navigation, bool isCollection)
    {
        if (string.IsNullOrWhiteSpace(property))
        {
            Report(ConversionRecordKind.Loss, entityMap, null, null,
                $"A @Result of '{entityMap.Entity.Name}' carries @{navigation.SimpleName} and names no property; it was dropped.");
            return;
        }

        if (navigation.String("select") is { } select)
        {
            Report(ConversionRecordKind.Loss, entityMap, property, null,
                $"The navigation is filled by the statement '{select}' rather than by the columns of this result, which says how the "
                + "value is loaded; the intermediate representation does not carry it and it was dropped.");
        }

        if (navigation["fetchType"] is { } fetchType)
        {
            Report(ConversionRecordKind.Loss, entityMap, property, null,
                $"The navigation states fetchType '{fetchType.SimpleName}', which is how the value is loaded rather than what it is; "
                + "the intermediate representation does not carry a loading strategy and it was dropped.");
        }

        if (TargetOfNavigation(entityMap, property) is not { } target)
        {
            Report(ConversionRecordKind.Incompleteness, entityMap, property, null,
                $"@{navigation.SimpleName} takes the entity it navigates to from the declaration of the property, and no unit of the "
                + "conversion declares it; the relation was not recorded.");
            return;
        }

        WriteNavigation(entityMap, property, target, isCollection);
    }
}
