// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageDirectories/Queries/GetUpdatePageDirectoryCommandByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

public class GetUpdatePageDirectoryCommandByIdQueryHandler
    : IQueryHandler<GetPatchableCommandByIdQuery<UpdatePageDirectoryCommand>, UpdatePageDirectoryCommand?>
    , IPermissionRestrictedQueryHandler<GetPatchableCommandByIdQuery<UpdatePageDirectoryCommand>, UpdatePageDirectoryCommand?>
{
    private readonly CofoundryDbContext _dbContext;

    public GetUpdatePageDirectoryCommandByIdQueryHandler(
        CofoundryDbContext dbContext
        )
    {
        _dbContext = dbContext;
    }

    public async Task<UpdatePageDirectoryCommand?> ExecuteAsync(GetPatchableCommandByIdQuery<UpdatePageDirectoryCommand> query, IExecutionContext executionContext)
    {
        var dbResult = await _dbContext
            .Set<PageDirectory>() // ORMConvertor: was .PageDirectories
            .AsNoTracking()
            .Where(i => i.PageDirectoryId == query.Id) // ORMConvertor: was .FilterById(query.Id)
            .SingleOrDefaultAsync();

        EntityNotFoundException.ThrowIfNull(dbResult, query.Id);

        if (!dbResult.ParentPageDirectoryId.HasValue)
        {
            throw new NotPermittedException("The root directory cannot be updated.");
        }

        var command = new UpdatePageDirectoryCommand()
        {
            Name = dbResult.Name,
            PageDirectoryId = dbResult.PageDirectoryId
        };

        return command;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPatchableCommandByIdQuery<UpdatePageDirectoryCommand> query)
    {
        yield return new PageDirectoryReadPermission();
    }
}
