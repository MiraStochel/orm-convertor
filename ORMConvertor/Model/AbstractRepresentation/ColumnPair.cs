namespace Model.AbstractRepresentation;

/// <summary>
/// Uspořádaná dvojice sloupců kompozitního FK (source.Col1 ↔ target.Col1).
/// Třída místo tuple záměrně – System.Text.Json tuple položky neserializuje.
/// </summary>
public sealed class ColumnPair
{
    public required PropertyMap Source { get; init; }
    public required PropertyMap Target { get; init; }
}