using AbstractWrappers;

namespace ORMConvertorAPI;

/// <summary>
/// Where the parse limits of decision 092 come from: the application's own configuration,
/// read once at startup and registered as one value for the lifetime of the process.
///
/// Never from a conversion request. The input a cap defends against must not be able to
/// carry permission to exceed it, so the limit has no field in <c>ConvertRequest</c> and the
/// endpoint takes it from the container instead of from the body.
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

    public static ParseLimits Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration.GetValue<int?>(Key);

        return configured is null
            ? ParseLimits.Default
            : new ParseLimits { MaxNestingDepth = configured.Value };
    }
}
