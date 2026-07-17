using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public sealed class PrimaryKeyPart
{
    // Typ, název sloupce, nullabilita atd. se berou z Property/PropertyMap.
    public required PropertyMap PropertyMap { get; init; }

    // Explicitní pořadí (1-based) – NE pořadí v listu (viz design doc 001, §3.2).
    public required int Order { get; init; }

    // Strategie generování per-part, ne pro celý klíč (viz §3.4).
    public PrimaryKeyStrategy Strategy { get; init; } = PrimaryKeyStrategy.None;
}