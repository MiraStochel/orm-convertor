// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/Roles/RolePermission.cs.
// Unchanged apart from this note.
namespace Cofoundry.Domain.Data;

public class RolePermission
{
    public int RoleId { get; set; }

    public Role Role
    {
        get => field ?? throw NavigationPropertyNotInitializedException.Create<RolePermission>(nameof(Role));
        set;
    }

    public int PermissionId { get; set; }

    public Permission Permission
    {
        get => field ?? throw NavigationPropertyNotInitializedException.Create<RolePermission>(nameof(Permission));
        set;
    }
}
