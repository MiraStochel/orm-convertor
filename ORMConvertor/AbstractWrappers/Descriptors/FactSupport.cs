namespace AbstractWrappers.Descriptors;

/// <summary>
/// How a target framework relates to a category of mapping facts.
/// The values are ordered: everything a framework requires it can also express,
/// so a demand for catalog metadata is the union of <see cref="Required"/> and
/// <see cref="Expressible"/>, and the diagnostics list is <see cref="NotExpressible"/>.
/// </summary>
public enum FactSupport
{
    /// <summary>
    /// The framework has no way to record the fact. It must not be inferred and its
    /// presence in the model has to be reported instead of silently dropped.
    /// </summary>
    NotExpressible = 1,

    /// <summary>
    /// The framework can record the fact. Supply it when available; fall back to
    /// conventions when it is not.
    /// </summary>
    Expressible = 2,

    /// <summary>
    /// The framework cannot produce a usable artifact without the fact. If it is still
    /// missing after metadata completion, generation is refused.
    /// </summary>
    Required = 3,
}