using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

/// <summary>
/// Record of a key class the source used to express a composite key. Every target renders
/// the key flat (decision 006), so the class name and its form would otherwise be lost.
/// An optional signal, like EntityMap.IsJunctionTable - absent means the source declared
/// the key parts directly on the entity.
/// </summary>
public sealed class SourceKeyClass
{
    /// <summary>
    /// Creates the record. The pairing of form and property name is validated here, so an
    /// inconsistent signal cannot reach the intermediate representation in the first place.
    /// </summary>
    /// <param name="className">Key class name as written in the source.</param>
    /// <param name="form">How the source attached the class to the entity.</param>
    /// <param name="propertyName">Entity property holding the key class; required for
    /// Embedded, not applicable to Mirrored.</param>
    public SourceKeyClass(string className, KeyClassForm form, string? propertyName = null)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Key class name must not be empty.", nameof(className));
        }

        if (form == KeyClassForm.Embedded && string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException(
                "An embedded key class is reached through a property of the entity, so its name is required.",
                nameof(propertyName));
        }

        if (form == KeyClassForm.Mirrored && !string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException(
                "A mirrored key class has no property on the entity; the key parts stay on the entity itself.",
                nameof(propertyName));
        }

        ClassName = className;
        Form = form;
        PropertyName = propertyName;
    }

    public string ClassName { get; }

    public KeyClassForm Form { get; }

    /// <summary>
    /// Entity property holding the key class; null for the Mirrored form.
    /// </summary>
    public string? PropertyName { get; }
}