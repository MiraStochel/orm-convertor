namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// Canonical names of generator parameters (decision 020). Each value fixes a meaning and a
/// unit, not a spelling: BlockSize is the number of values in one allocated block, so
/// NHibernate's max_lo (the highest low value) maps onto it one higher, while Jakarta
/// Persistence's allocationSize maps onto it unchanged. Absence means nobody stated the
/// parameter - defaults of the source are not materialized. What no value names stays on
/// PrimaryKeyPart.SourceStrategyParameters as the source wrote it. Declaration order is the
/// emission order of builders, a stable property of the model rather than of the input (S2).
/// </summary>
public enum GeneratorParameter
{
    /// <summary>Name of the sequence the value comes from.</summary>
    SequenceName = 0,

    /// <summary>Schema of the sequence or of the counter table.</summary>
    Schema = 1,

    /// <summary>Number of values in one allocated block.</summary>
    BlockSize = 2,

    /// <summary>First value handed out.</summary>
    InitialValue = 3,

    /// <summary>Table the counter lives in, outside the entity's own table.</summary>
    CounterTable = 4,

    /// <summary>Column holding the next high value.</summary>
    CounterValueColumn = 5,

    /// <summary>Column selecting the counter's row; JPA table generator, no NHibernate counterpart.</summary>
    CounterKeyColumn = 6,

    /// <summary>Value selecting the counter's row; JPA table generator, no NHibernate counterpart.</summary>
    CounterKeyValue = 7,
}
