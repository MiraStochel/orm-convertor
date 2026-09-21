using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace Tests.Combined;

/// <summary>
/// The guards the model puts on its own construction. Every one of them is a sentence the
/// architecture states - "instances are created through the factory methods only, so an
/// invalid combination cannot be written down" (decision 014), "a negative value is refused
/// by the factory" (decision 085), "one or the other, never both and never neither"
/// (decision 083) - and each is what lets the rest of the code skip a check. The key's own
/// invariants are in <see cref="PrimaryKeyTest"/> and the unique constraint's in
/// <see cref="UniqueConstraintTest"/>; this is the rest of them.
/// </summary>
public class ModelFactoryTest
{
    /* ---- the row count of a pagination (decision 085) ---------------------------------- */

    [Fact]
    public void ARowCountIsEitherANumberOrAParameter()
    {
        var literal = RowCount.Literal(5);
        Assert.Equal(5, literal.Value);
        Assert.Null(literal.Parameter);
        Assert.False(literal.IsParameter);

        var bound = RowCount.Bound(QueryParameter.Named("take"));
        Assert.Null(bound.Value);
        Assert.True(bound.IsParameter);
        Assert.Equal("take", bound.Parameter!.Name);
    }

    [Fact]
    public void ANegativeRowCountIsRefusedByTheFactory()
    {
        // A negative slice has no meaning in any source and no parser can produce one, so
        // the refusal lives here and no step downstream has to repeat it.
        Assert.Throws<ArgumentOutOfRangeException>(() => RowCount.Literal(-1));
        Assert.Equal(0, RowCount.Literal(0).Value);
    }

    [Fact]
    public void ABoundRowCountNeedsAParameter()
        => Assert.Throws<ArgumentNullException>(() => RowCount.Bound(null!));

    /* ---- the parameter (decision 083) -------------------------------------------------- */

    [Fact]
    public void AParameterCarriesANameOrAPositionAndNeverBoth()
    {
        var named = QueryParameter.Named("id");
        Assert.Equal("id", named.Name);
        Assert.Null(named.Position);
        Assert.False(named.IsPositional);

        var positional = QueryParameter.Positional(1);
        Assert.Null(positional.Name);
        Assert.Equal(1, positional.Position);
        Assert.True(positional.IsPositional);
    }

    [Fact]
    public void AParameterWithoutANameOrWithoutAnOrderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => QueryParameter.Named(""));
        Assert.Throws<ArgumentException>(() => QueryParameter.Named("   "));
        Assert.Throws<ArgumentNullException>(() => QueryParameter.Named(null!));

        // Counted from one, as ?1 is: a zeroth parameter is not a shape any source writes.
        Assert.Throws<ArgumentOutOfRangeException>(() => QueryParameter.Positional(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => QueryParameter.Positional(-1));
    }

    [Fact]
    public void FillingInTheScalarLeavesTheOriginalParameterAlone()
    {
        // The condition tree a parser handed over is not rewritten: the gate of the template
        // puts the resolved parameter on the builder's own list (decision 083).
        var read = QueryParameter.Named("id", isCollection: true);
        var resolved = read.WithType(ScalarType.Int);

        Assert.Null(read.Type);
        Assert.Equal(ScalarType.Int, resolved.Type);
        Assert.True(resolved.IsCollection);
        Assert.Equal("id", resolved.Name);
    }

    /* ---- the language type (decision 014) ---------------------------------------------- */

    [Fact]
    public void EachLanguageTypeCategoryCarriesOnlyItsOwnFacts()
    {
        var scalar = LangType.Scalar(ScalarType.Int, isNullable: true);
        Assert.Equal(LangTypeCategory.Scalar, scalar.Category);
        Assert.Equal(ScalarType.Int, scalar.ScalarType);
        Assert.True(scalar.IsNullable);
        Assert.Null(scalar.TargetEntity);
        Assert.Null(scalar.ElementType);
        Assert.Null(scalar.SourceName);

        var reference = LangType.Reference("Customer");
        Assert.Equal(LangTypeCategory.Reference, reference.Category);
        Assert.Equal("Customer", reference.TargetEntity);
        Assert.Null(reference.ScalarType);

        var collection = LangType.Collection(reference, CollectionKind.Set);
        Assert.Equal(LangTypeCategory.Collection, collection.Category);
        Assert.Equal(CollectionKind.Set, collection.CollectionKind);
        Assert.Same(reference, collection.ElementType);

        var unknown = LangType.Unknown("Instant");
        Assert.Equal(LangTypeCategory.Unknown, unknown.Category);
        Assert.Equal("Instant", unknown.SourceName);
    }

    [Fact]
    public void ALanguageTypeWithoutTheFactItsCategoryNeedsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => LangType.Reference(""));
        Assert.Throws<ArgumentException>(() => LangType.Reference("  "));
        Assert.Throws<ArgumentException>(() => LangType.Unknown(""));
        Assert.Throws<ArgumentNullException>(() => LangType.Collection(null!));
    }

    /* ---- the operand (decisions 024, 061, 074, 083) ------------------------------------ */

    [Fact]
    public void EachOperandShapeAnswersForItselfAndForNoOther()
    {
        var column = QueryOperand.Column("c", "CreditLimit");
        Assert.True(column.IsColumn);
        Assert.False(column.IsConstant || column.IsSubQuery || column.IsValueList || column.IsParameter);

        var constant = QueryOperand.Value(QueryConstant.Of("2000", ScalarType.Decimal));
        Assert.True(constant.IsConstant);
        Assert.False(constant.IsColumn || constant.IsSubQuery || constant.IsValueList || constant.IsParameter);

        var values = QueryOperand.ValueList([QueryConstant.Of("1", ScalarType.Int)]);
        Assert.True(values.IsValueList);
        Assert.False(values.IsColumn || values.IsConstant || values.IsSubQuery || values.IsParameter);

        var parameter = QueryOperand.Bound(QueryParameter.Named("id"));
        Assert.True(parameter.IsParameter);
        Assert.False(parameter.IsColumn || parameter.IsConstant || parameter.IsSubQuery || parameter.IsValueList);

        var nested = QueryOperand.Nested(new SubQueryInstruction([]));
        Assert.True(nested.IsSubQuery);
        Assert.False(nested.IsColumn || nested.IsConstant || nested.IsValueList || nested.IsParameter);
    }

    [Fact]
    public void AnEmptyListOfValuesIsNotConstructible()
        // IN () has no form in any target, so the emptiness is refused where the list is made
        // rather than at every place that reads one (decision 074).
        => Assert.Throws<ArgumentException>(() => QueryOperand.ValueList([]));

    /* ---- the marker of a collapsed projection (decision 073) --------------------------- */

    [Fact]
    public void ADistinctMarkerCarriesNothingSoTwoOfThemAreOneClaim()
    {
        // The whole of the marker's shape: no values at all, which is what makes a second
        // one in the same scope need no rule against it.
        Assert.Equal(new DistinctInstruction(), new DistinctInstruction());
        Assert.Empty(new DistinctInstruction().GetType().GetProperties());
    }

    /* ---- the pagination instruction (decisions 060 and 085) ---------------------------- */

    [Fact]
    public void APaginationCarriesEitherCountOrNeitherInOffsetThenLimitForm()
    {
        var both = new PaginationInstruction(RowCount.Literal(10), RowCount.Literal(5));
        Assert.Equal(10, both.Offset!.Value);
        Assert.Equal(5, both.Limit!.Value);

        var limitOnly = new PaginationInstruction(null, RowCount.Literal(5));
        Assert.Null(limitOnly.Offset);
        Assert.Equal(5, limitOnly.Limit!.Value);

        var offsetOnly = new PaginationInstruction(RowCount.Literal(10), null);
        Assert.Equal(10, offsetOnly.Offset!.Value);
        Assert.Null(offsetOnly.Limit);
    }
}
