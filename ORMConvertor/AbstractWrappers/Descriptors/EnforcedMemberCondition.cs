namespace AbstractWrappers.Descriptors;

/// <summary>
/// When an enforced member applies. A closed set rather than a predicate: conditions
/// have to be listable in diagnostics and checkable by a test that asserts a member is
/// absent when its condition does not hold.
/// </summary>
public enum EnforcedMemberCondition
{
    Always = 1,
    CompositePrimaryKey = 2,
    NoPrimaryKey = 3,
}