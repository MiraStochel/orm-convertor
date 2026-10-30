using AbstractWrappers;

namespace ORMConvertorAPI;

/// <summary>
/// Where the limits of decisions 092 and 194 come from: the application's own configuration,
/// read once at startup and registered as one value for the lifetime of the process.
///
/// Never from a conversion request. The input a cap defends against must not be able to
/// carry permission to exceed it, so the limits have no field in <c>ConvertRequest</c> and the
/// endpoint takes them from the container instead of from the body.
/// </summary>
public static class ParseLimitsConfiguration
{
    /// <summary>
    /// <c>Parsing:MaxNestingDepth</c> - appsettings, an environment variable, any provider
    /// the host reads. Absent means the default cap; the number is stated once, in
    /// <see cref="ParseLimits.DefaultMaxNestingDepth"/>, and deliberately not repeated in
    /// appsettings.json, where a second copy could drift from the first.
    /// <see cref="ParseLimits.Unlimited"/> switches the cap off and takes threat 2 back whole.
    /// </summary>
    public const string Key = "Parsing:MaxNestingDepth";

    /// <summary>
    /// <c>Parsing:MaxQueryTextLength</c> - the cap on the text of one query (decision 194), read
    /// the same way, stated once in <see cref="ParseLimits.DefaultMaxQueryTextLength"/>, and
    /// switched off by <see cref="ParseLimits.Unlimited"/> like the depth.
    /// </summary>
    public const string QueryTextKey = "Parsing:MaxQueryTextLength";

    public static ParseLimits Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var depth = configuration.GetValue<int?>(Key);
        var queryText = configuration.GetValue<int?>(QueryTextKey);

        return new ParseLimits
        {
            MaxNestingDepth = depth ?? ParseLimits.DefaultMaxNestingDepth,
            MaxQueryTextLength = queryText ?? ParseLimits.DefaultMaxQueryTextLength,
        };
    }
}
