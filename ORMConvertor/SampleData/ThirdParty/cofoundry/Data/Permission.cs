// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Roles/Permission.cs.
// Changed: the navigation to an entity outside the excerpt is left out, marked where it stood. See README.md in the folder.
namespace Cofoundry.Domain.Data;

/// <summary>
/// A permission represents an type action a user can
/// be permitted to perform. Typically this is associated
/// with a specified entity type, but doesn't have to be e.g.
/// "read pages", "access dashboard", "delete images".
/// The combination of EntityDefinitionCode and PermissionCode
/// must be unique
/// </summary>
public class Permission
{
    /// <summary>
    /// Database id of the permission.
    /// </summary>
    public int PermissionId { get; set; }

    /// <summary>
    /// Optional code of the entity this permission relates to.
    /// </summary>
    public string? EntityDefinitionCode { get; set; }

    // ORMConvertor: the navigation EntityDefinition is left out: it leads to an entity outside the excerpt, and EntityDefinitionCode above keeps its column.

    /// <summary>
    /// A three letter code representing the action that is being permitted. The 
    /// combination of EntityDefinitionCode and PermissionCode must be unique 
    /// so some common permission codes can be used to reprsent common actions 
    /// such as "read", "create" or "delete" when used in
    /// combination with a specific entity type.
    /// </summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>
    /// A permission can be assigned to many roles.
    /// </summary>
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    /// <summary>
    /// Creates a unique code that represents this permission by combining the
    /// entity definition code and the permission code. This logic is replicated for
    /// IPermissions in <see cref="IPermissionExtensions.GetUniqueIdentifier"/>.
    /// </summary>
    /// <returns>A string code that uniquely identifies this permission</returns>
    public string? GetUniqueCode()
    {
        var code = EntityDefinitionCode + PermissionCode;
        if (code == null)
        {
            return null;
        }

        return code.ToUpperInvariant();
    }

    /// <summary>
    /// Creates a unique code that represents this permission by combining the
    /// entity definition code and the permission code. This logic is replicated for
    /// IPermissions in <see cref="IPermissionExtensions.GetUniqueIdentifier"/>.
    /// </summary>
    /// <returns>A string code that uniquely identifies this permission</returns>
    public string GetRequiredUniqueCode()
    {
        var code = GetUniqueCode();

        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException($"Unique code for permission with id {PermissionId} is expected to have a value");
        }

        return code;
    }
}
