using Model.AbstractRepresentation.Enums;

namespace NHibernateWrappers.Convertors;

public class PrimaryKeyStrategyConvertor
{
    /// <summary>
    /// Maps an NHibernate generator onto the vocabulary of mechanisms. A generator with no
    /// counterpart - a custom class, foreign, select - lands on Unspecified; its name is not
    /// lost, the parser keeps it through <see cref="SourceNameFor"/> (decision 011).
    /// </summary>
    public static PrimaryKeyStrategy FromNHibernate(string? generator)
    {
        return generator switch
        {
            null or "" => PrimaryKeyStrategy.Unspecified,
            "assigned" => PrimaryKeyStrategy.Assigned,
            "native" => PrimaryKeyStrategy.Auto,
            "identity" => PrimaryKeyStrategy.Identity,
            "sequence" => PrimaryKeyStrategy.Sequence,
            "hilo" or "seqhilo" => PrimaryKeyStrategy.HiLo,
            "guid" or "guid.comb" or "uuid.hex" => PrimaryKeyStrategy.Uuid,
            "increment" => PrimaryKeyStrategy.Increment,
            _ => PrimaryKeyStrategy.Unspecified,
        };
    }

    /// <summary>
    /// The generator name to emit. Unspecified falls back to assigned, which is a convention
    /// of the target rather than a fact of the source and is reported as such (decision 015).
    /// The mapping is total on purpose: no branch throws, because a combination the target
    /// cannot express belongs to diagnostics, not to a crash mid-generation (decision 010).
    /// </summary>
    public static string ToNHibernate(PrimaryKeyStrategy strategy)
    {
        return strategy switch
        {
            PrimaryKeyStrategy.Assigned => "assigned",
            PrimaryKeyStrategy.Auto => "native",
            PrimaryKeyStrategy.Identity => "identity",
            PrimaryKeyStrategy.Sequence => "sequence",
            PrimaryKeyStrategy.HiLo => "hilo",
            PrimaryKeyStrategy.Uuid => "guid",
            PrimaryKeyStrategy.Increment => "increment",
            _ => "assigned",
        };
    }

    /// <summary>
    /// What to keep as the source's own name for the strategy: the generator we read, unless
    /// it is exactly the one we would write back. Variants such as guid.comb or seqhilo and
    /// generators outside the vocabulary are therefore kept, canonical names are not
    /// duplicated into the model.
    /// </summary>
    public static string? SourceNameFor(string? generator, PrimaryKeyStrategy strategy)
        => string.IsNullOrEmpty(generator) || generator == ToNHibernate(strategy)
            ? null
            : generator;

    /// <summary>
    /// Whether NHibernate registers a generator of this name - the first condition for writing
    /// a source's own name back into the output (decision 021). Deliberately distinct from
    /// <see cref="FromNHibernate"/> landing on Unspecified, which conflates a name we know and
    /// map onto nothing with a name we never saw, such as a custom generator class.
    /// </summary>
    public static bool Knows(string generator) => KnownGenerators.Contains(generator);

    // The short names IdentifierGeneratorFactory registers in NHibernate 5.7.0, plus native,
    // which the binder resolves against the dialect. A name missing here only costs fidelity
    // (canonical fallback with a loss record); a name NHibernate does not accept would cost a
    // runnable artifact, so the list errs on the side of leaving out.
    private static readonly HashSet<string> KnownGenerators = new(StringComparer.Ordinal)
    {
        "assigned",
        "native",
        "identity",
        "sequence",
        "seqhilo",
        "hilo",
        "increment",
        "guid",
        "guid.comb",
        "guid.native",
        "uuid.hex",
        "uuid.string",
        "foreign",
        "select",
        "sequence-identity",
        "trigger-identity",
        "enhanced-sequence",
        "enhanced-table",
    };
}