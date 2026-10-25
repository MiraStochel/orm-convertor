// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/GetUpdatePageDraftVersionCommandByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

public class GetUpdatePageDraftVersionCommandByIdQueryHandler
    : IQueryHandler<GetPatchableCommandByIdQuery<UpdatePageDraftVersionCommand>, UpdatePageDraftVersionCommand?>
    , IPermissionRestrictedQueryHandler<GetPatchableCommandByIdQuery<UpdatePageDraftVersionCommand>, UpdatePageDraftVersionCommand?>
{
    private readonly CofoundryDbContext _dbContext;

    public GetUpdatePageDraftVersionCommandByIdQueryHandler(
        CofoundryDbContext dbContext
        )
    {
        _dbContext = dbContext;
    }

    public async Task<UpdatePageDraftVersionCommand?> ExecuteAsync(GetPatchableCommandByIdQuery<UpdatePageDraftVersionCommand> query, IExecutionContext executionContext)
    {
        var command = await _dbContext
            .PageVersions
            .AsNoTracking()
            .Where(v => !v.PageTemplate.IsArchived) // ORMConvertor: was .FilterActive()
            .Where(v => v.PageId == query.Id) // ORMConvertor: was .FilterByPageId(query.Id)
            .Where(p => p.WorkFlowStatusId == (int)WorkFlowStatus.Draft)
            .Select(v => new UpdatePageDraftVersionCommand
            {
                MetaDescription = v.MetaDescription,
                OpenGraphDescription = v.OpenGraphDescription,
                OpenGraphImageId = v.OpenGraphImageId,
                OpenGraphTitle = v.OpenGraphTitle,
                PageId = v.PageId,
                ShowInSiteMap = !v.ExcludeFromSitemap,
                Title = v.Title
            })
            .SingleOrDefaultAsync();

        return command;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPatchableCommandByIdQuery<UpdatePageDraftVersionCommand> query)
    {
        yield return new PageReadPermission();
    }
}
