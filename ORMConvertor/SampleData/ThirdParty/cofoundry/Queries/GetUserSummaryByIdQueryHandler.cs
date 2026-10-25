// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Users/Queries/GetUserSummaryByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Finds a user by a database id returning a <see cref="UserSummary"/> projection 
/// if it is found, otherwise <see langword="null"/>.
/// </summary>
public class GetUserSummaryByIdQueryHandler
    : IQueryHandler<GetUserSummaryByIdQuery, UserSummary?>
    , IIgnorePermissionCheckHandler
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IPermissionValidationService _permissionValidationService;
    private readonly IUserSummaryMapper _userSummaryMapper;

    public GetUserSummaryByIdQueryHandler(
        CofoundryDbContext dbContext,
        IPermissionValidationService permissionValidationService,
        IUserSummaryMapper userSummaryMapper
        )
    {
        _dbContext = dbContext;
        _permissionValidationService = permissionValidationService;
        _userSummaryMapper = userSummaryMapper;
    }

    public async Task<UserSummary?> ExecuteAsync(GetUserSummaryByIdQuery query, IExecutionContext executionContext)
    {
        var dbResult = await _dbContext
            .Users
            .AsNoTracking()
            .Include(u => u.Role).Include(u => u.Creator) // ORMConvertor: was .IncludeForSummary()
            .Where(u => u.UserId == query.UserId) // ORMConvertor: was .FilterById(query.UserId)
            .SingleOrDefaultAsync();
        var user = _userSummaryMapper.Map(dbResult);

        ValidatePermission(query, executionContext, user);

        return user;
    }

    private void ValidatePermission(GetUserSummaryByIdQuery query, IExecutionContext executionContext, UserSummary? user)
    {
        if (user == null)
        {
            return;
        }

        if (user.UserArea.UserAreaCode == CofoundryAdminUserArea.Code)
        {
            _permissionValidationService.EnforceCurrentUserOrHasPermission<CofoundryUserReadPermission>(query.UserId, executionContext.UserContext);
        }
        else
        {
            _permissionValidationService.EnforceCurrentUserOrHasPermission<NonCofoundryUserReadPermission>(query.UserId, executionContext.UserContext);
        }
    }
}
