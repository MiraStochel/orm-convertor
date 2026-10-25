// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/GetUpdatePageCommandByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

public class GetUpdatePageCommandByIdQueryHandler
    : IQueryHandler<GetPatchableCommandByIdQuery<UpdatePageCommand>, UpdatePageCommand?>
    , IPermissionRestrictedQueryHandler<GetPatchableCommandByIdQuery<UpdatePageCommand>, UpdatePageCommand?>
{
    private readonly CofoundryDbContext _dbContext;

    public GetUpdatePageCommandByIdQueryHandler(
        CofoundryDbContext dbContext
        )
    {
        _dbContext = dbContext;
    }

    public async Task<UpdatePageCommand?> ExecuteAsync(GetPatchableCommandByIdQuery<UpdatePageCommand> query, IExecutionContext executionContext)
    {
        var dbResult = await _dbContext
            .Pages
            .AsNoTracking()
            .Include(p => p.PageTags)
            .ThenInclude(t => t.Tag)
            // ORMConvertor: .FilterActive() is left out - the FilterActive of Page returns the pages unchanged (PageQueryExtensions)
            .Where(i => i.PageId == query.Id) // ORMConvertor: was .FilterById(query.Id)
            .SingleOrDefaultAsync();

        if (dbResult == null)
        {
            return null;
        }

        var command = new UpdatePageCommand()
        {
            PageId = dbResult.PageId,
            Tags = dbResult
                .PageTags
                .Select(t => t.Tag.TagText)
                .OrderBy(t => t)
                .ToArray()
        };

        return command;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPatchableCommandByIdQuery<UpdatePageCommand> query)
    {
        yield return new PageReadPermission();
    }
}
