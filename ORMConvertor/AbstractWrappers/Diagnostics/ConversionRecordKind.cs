namespace AbstractWrappers.Diagnostics;

/// <summary>
/// The event a record describes. Deliberately not a severity scale (decision 010): each
/// value is tied to one defined event of the conversion. Failure and Loss follow from the
/// two descriptor states of decision 009; Convention is the record decisions 009 and 012
/// require when a convention enters the output; Incompleteness is what the pre-generation
/// check finds missing without refusing the artifact - typically a fact a database catalog
/// could still supply (decision 015). Supplied and Conflict belong to the catalog
/// completion phase: the origin of a fact lives in the record, not in the model
/// (decision 010), and a disagreement between source and catalog is reported, never
/// resolved silently (decision 015).
/// </summary>
public enum ConversionRecordKind
{
    /// <summary>
    /// A category the target requires and nobody supplied, or a property without a language
    /// type. The entity's artifacts are not generated.
    /// </summary>
    Failure = 1,

    /// <summary>
    /// A fact the source carried and the target cannot express, or can express only in a
    /// form the generated artifact does not use. The artifact is valid, only poorer than
    /// the input.
    /// </summary>
    Loss = 2,

    /// <summary>
    /// The output states something the source never said: a convention of the target or of
    /// the tool filled the gap.
    /// </summary>
    Convention = 3,

    /// <summary>
    /// The intermediate representation lacks a fact the output would need to be faithful;
    /// generation proceeds on what there is.
    /// </summary>
    Incompleteness = 4,

    /// <summary>
    /// A fact the source did not state was supplied from outside it - the database
    /// catalog. The record is the fact's origin, which the model itself does not carry
    /// (decisions 010 and 015).
    /// </summary>
    Supplied = 5,

    /// <summary>
    /// The source and the catalog disagree. The source outranks the catalog (rule E9,
    /// decision 015), so translation continues with the source value and this record
    /// says what the catalog stated instead.
    /// </summary>
    Conflict = 6,
}
