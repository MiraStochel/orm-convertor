using Model.QueryInstructions.Conditions;

namespace Common.Naming;

/// <summary>
/// The identifier a query parameter is spelled with in a generated artifact (decision 083).
/// A named parameter keeps the name the source wrote, undecorated; a positional one gets a
/// name made from its order, which is the one place a name has to be invented - the
/// signature of the generated method, and every target whose query language has no
/// positional form. Made from the order rather than guessed, so that the same model always
/// yields the same signature (S2).
///
/// Lives in Common for the same reason <see cref="QueryMethodNaming"/> does: every builder
/// needs it and none may reach into another's project (S1).
/// </summary>
public static class QueryParameterNaming
{
    /// <summary>The identifier the parameter is written with: its name, or p1, p2, … .</summary>
    public static string IdentifierFor(QueryParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return parameter.Name ?? $"p{parameter.Position}";
    }
}
