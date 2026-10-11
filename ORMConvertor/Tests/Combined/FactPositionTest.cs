using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Where a fact stands (decision 125), over every direction of the matrix. The positions are
/// optional by construction - a parser or builder that states none is complete all the same -
/// so the proof here is that the six readers and the six builders of this version do state
/// them, and state them right: a position lies inside the text it names, and for a fact with
/// a value the text at the position spells that value. The categories whose value the target
/// respells (a strategy, a type family, nullability as a word, a constraint named by its
/// columns) are held to the bounds alone.
/// </summary>
public class FactPositionTest
{
    /// <summary>The categories a reader of this version places and a builder of this version marks.</summary>
    private static readonly HashSet<MappingFactCategory> Positioned =
    [
        MappingFactCategory.TableName,
        MappingFactCategory.SchemaName,
        MappingFactCategory.ColumnName,
        MappingFactCategory.DatabaseType,
        MappingFactCategory.Length,
        MappingFactCategory.PrecisionAndScale,
        MappingFactCategory.Nullability,
        MappingFactCategory.PrimaryKey,
        MappingFactCategory.PrimaryKeyStrategy,
        MappingFactCategory.ForeignKeyColumns,
        MappingFactCategory.VersionColumn,
        MappingFactCategory.UniqueConstraint,
        MappingFactCategory.TransientProperty,
    ];

    /// <summary>The categories whose value is not spelled literally on both sides.</summary>
    private static readonly HashSet<MappingFactCategory> Respelled =
    [
        MappingFactCategory.Nullability,
        MappingFactCategory.PrimaryKeyStrategy,
        MappingFactCategory.DatabaseType,
        MappingFactCategory.UniqueConstraint,
    ];

    public static TheoryData<ORMEnum, ORMEnum> Directions() => CrossFrameworkInputs.Directions();

    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryStatedFactStandsInItsUnit(ORMEnum source, ORMEnum target)
    {
        // The whole input of the direction: for MyBatis the mapping lives in the mapper beside
        // the statement, which the matrix files as the query unit (decision 081).
        var units = CrossFrameworkInputs.Units(source);
        var result = ConversionHandler.Convert(source, target, units);

        var stated = result.Facts
            .Where(f => f.Origin == FactOrigin.Source && Positioned.Contains(f.Category))
            .ToList();

        // A Dapper class states no mapping fact at all, so there is nothing to place; every
        // other source states some, and the test would prove nothing over an empty list.
        if (source == ORMEnum.Dapper)
        {
            Assert.Empty(stated);
            return;
        }

        Assert.NotEmpty(stated);

        foreach (var fact in stated)
        {
            Assert.True(fact.Source is not null, $"{source} → {target}: {Describe(fact)} has no source position.");

            var unit = units
                .Select((u, index) => (Unit: u, Reference: string.IsNullOrWhiteSpace(u.Name) ? $"unit {index + 1}" : u.Name.Trim()))
                .SingleOrDefault(u => u.Reference == fact.Source!.Unit)
                .Unit;

            Assert.True(unit is not null, $"{source} → {target}: {Describe(fact)} names the unit '{fact.Source!.Unit}', which was not sent.");
            Assert.Equal(fact.Unit, fact.Source!.Unit);

            var text = Slice(unit!.Content, fact.Source.Start, fact.Source.Length, $"{source} → {target}: {Describe(fact)} in the unit");
            AssertSpells(fact, [text], $"{source} → {target}: {Describe(fact)} at '{text}' in the unit");
        }
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryCarriedFactStandsInItsArtifact(ORMEnum source, ORMEnum target)
    {
        var units = CrossFrameworkInputs.Units(source);
        var result = ConversionHandler.Convert(source, target, units);

        var carried = result.Facts
            .Where(f => f.Outcome == FactOutcome.Carried && Positioned.Contains(f.Category))
            .ToList();

        foreach (var fact in carried)
        {
            Assert.True(fact.Targets.Count > 0, $"{source} → {target}: {Describe(fact)} is carried and has no target position.");

            var texts = new List<string>();

            foreach (var position in fact.Targets)
            {
                var artifact = result.Sources.SingleOrDefault(s => s.Entity == fact.Entity && s.ContentType == position.Artifact);
                Assert.True(artifact is not null, $"{source} → {target}: {Describe(fact)} names a {position.Artifact} artifact the entity does not have.");

                texts.Add(Slice(artifact!.Content, position.Start, position.Length, $"{source} → {target}: {Describe(fact)} in the artifact"));
            }

            AssertSpells(fact, texts, $"{source} → {target}: {Describe(fact)} at '{string.Join(" | ", texts)}' in the artifact");
        }
    }

    /// <summary>The query units of a direction take nothing from the ledger: their artifacts carry no entity and no fact names them.</summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void PositionsSurviveTheWire(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

        // The same facts twice: the ledger is a function of the input, positions included (S2).
        var again = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

        Assert.Equal(result.Facts.Select(f => (f.Source, f.Targets.Count)), again.Facts.Select(f => (f.Source, f.Targets.Count)));
    }

    private static string Slice(string content, int start, int length, string where)
    {
        Assert.True(start >= 0 && length > 0 && start + length <= content.Length,
            $"{where}: [{start}, {start + length}) is outside a text of {content.Length} characters.");

        return content.Substring(start, length);
    }

    /// <summary>Every part of the fact's value is spelled somewhere in the texts at its positions, unless the category respells it.</summary>
    private static void AssertSpells(ConversionFact fact, IReadOnlyList<string> texts, string where)
    {
        if (fact.Value is null || Respelled.Contains(fact.Category))
        {
            return;
        }

        var all = string.Join("\n", texts);

        foreach (var part in fact.Value.Split(", ").Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            Assert.True(
                all.Contains(part, StringComparison.OrdinalIgnoreCase),
                $"{where}: the text does not spell '{part}'.");
        }
    }

    private static string Describe(ConversionFact fact)
        => $"{fact.Entity}.{fact.Property ?? "<entity>"} {fact.Category} = {fact.Value ?? "<none>"}";
}
