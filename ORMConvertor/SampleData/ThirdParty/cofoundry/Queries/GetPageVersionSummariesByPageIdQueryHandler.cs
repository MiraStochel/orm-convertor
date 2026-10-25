// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/GetPageVersionSummariesByPageIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Returns a paged collection of versions of a specific page, ordered 
/// historically with the latest/draft version first.
/// </summary>
public class GetPageVersionSummariesByPageIdQueryHandler
    : IQueryHandler<GetPageVersionSummariesByPageIdQuery, PagedQueryResult<PageVersionSummary>>
    , IPermissionRestrictedQueryHandler<GetPageVersionSummariesByPageIdQuery, PagedQueryResult<PageVersionSummary>>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IPageVersionSummaryMapper _pageVersionSummaryMapper;

    public GetPageVersionSummariesByPageIdQueryHandler(
        CofoundryDbContext dbContext,
        IPageVersionSummaryMapper pageVersionSummaryMapper
        )
    {
        _dbContext = dbContext;
        _pageVersionSummaryMapper = pageVersionSummaryMapper;
    }

    public async Task<PagedQueryResult<PageVersionSummary>> ExecuteAsync(GetPageVersionSummariesByPageIdQuery query, IExecutionContext executionContext)
    {
        var dbVersions = await Query(query.PageId).ToPagedResultAsync(query);
        var versions = _pageVersionSummaryMapper.MapVersions(query.PageId, dbVersions);

        return versions;
    }

    private IQueryable<PageVersion> Query(int id)
    {
        return _dbContext
            .PageVersions
            .AsNoTracking()
            .Include(v => v.Creator)
            .Include(v => v.PageTemplate)
            .Include(v => v.OpenGraphImageAsset)
            .Where(v => !v.PageTemplate.IsArchived) // ORMConvertor: was .FilterActive()
            .Where(v => v.PageId == id) // ORMConvertor: was .FilterByPageId(id)
            .OrderByDescending(v => v.WorkFlowStatusId == (int)WorkFlowStatus.Draft).ThenByDescending(v => v.CreateDate); // ORMConvertor: was .OrderByLatest();
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageVersionSummariesByPageIdQuery query)
    {
        yield return new PageReadPermission();
    }
}
