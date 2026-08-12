namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// How the source framework attached a dedicated key class to the entity.
/// The forms differ in the path to a key part: through the key class property
/// (Embedded), or directly on the entity (Mirrored).
/// </summary>
public enum KeyClassForm
{
    /// <summary>
    /// The key class is a property of the entity and the key parts live inside it:
    /// NHibernate &lt;composite-id name="Id" class="OrderLineId"&gt;, JPA @EmbeddedId.
    /// </summary>
    Embedded = 1,

    /// <summary>
    /// The key parts stay on the entity and the key class only mirrors them:
    /// NHibernate &lt;composite-id class="OrderLineId"&gt; without name, JPA @IdClass.
    /// </summary>
    Mirrored = 2,
}