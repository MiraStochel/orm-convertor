using Model;
using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// The profile of a Jakarta Persistence implementation (decisions 076 and 077): what the
/// version number of the descriptor cannot say, because the same annotation means
/// different things under Hibernate and under EclipseLink. Beside the descriptor rather
/// than inside it - the shared descriptor type is the .NET frameworks' too and stays
/// untouched by the Java side.
/// </summary>
/// <param name="Implementation">The framework the profile belongs to; the key to its defaults.</param>
/// <param name="SpecificationLevel">The Jakarta Persistence level the shared layer is written against.</param>
/// <param name="AutoStrategy">The mechanism the implementation resolves GenerationType.AUTO to on the pinned dialect.</param>
/// <param name="DefaultAllocationSize">The allocation size the implementation assumes when none is stated.</param>
/// <param name="NamingStrategy">The naming the artifact is correct under, in words - a fact of the consumer's configuration (decision 040).</param>
/// <param name="NationalizedByDefault">Whether a plain String maps to national character data without an annotation.</param>
/// <param name="UppercaseImplicitNames">Whether the implementation writes implicit names in upper case (EclipseLink).</param>
/// <param name="VendorAnnotationPackage">The package of the implementation's own annotations.</param>
public sealed record JpaImplementationProfile(
    ORMEnum Implementation,
    string SpecificationLevel,
    PrimaryKeyStrategy AutoStrategy,
    int DefaultAllocationSize,
    string NamingStrategy,
    bool NationalizedByDefault,
    bool UppercaseImplicitNames,
    string VendorAnnotationPackage);
