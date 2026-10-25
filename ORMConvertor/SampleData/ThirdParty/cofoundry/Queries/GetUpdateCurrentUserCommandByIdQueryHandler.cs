// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Users/Queries/GetUpdateCurrentUserCommandByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

public class GetUpdateCurrentUserCommandByIdQueryHandler
    : IQueryHandler<GetPatchableCommandQuery<UpdateCurrentUserCommand>, UpdateCurrentUserCommand?>
    , IIgnorePermissionCheckHandler
{
    private readonly CofoundryDbContext _dbContext;

    public GetUpdateCurrentUserCommandByIdQueryHandler(
        CofoundryDbContext dbContext
        )
    {
        _dbContext = dbContext;
    }

    public async Task<UpdateCurrentUserCommand?> ExecuteAsync(GetPatchableCommandQuery<UpdateCurrentUserCommand> query, IExecutionContext executionContext)
    {
        if (!executionContext.UserContext.UserId.HasValue)
        {
            return null;
        }

        // ORMConvertor: rewritten - FilterCanSignIn and FilterById as the Where each wraps (UserQueryExtensions),
        // !HasValue of a nullable date as its comparison with null, and the argument of FilterById evaluated before
        // the query, as the call evaluates it. The project's text, up to the projection:
        // var user = await _dbContext
            // .Users
            // .AsNoTracking()
            // .FilterCanSignIn()
            // .FilterById(executionContext.UserContext.UserId.Value)
        var userId = executionContext.UserContext.UserId.Value;
        var user = await _dbContext
            .Users
            .AsNoTracking()
            .Where(u => !u.IsSystemAccount && u.DeletedDate == null && u.DeactivatedDate == null)
            .Where(u => u.UserId == userId)
            .Select(u => new UpdateCurrentUserCommand()
            {
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName
            })
            .SingleOrDefaultAsync();

        return user;
    }
}
