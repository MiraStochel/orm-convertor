// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/GetPageRenderSummaryByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Query to get a page by an id, projected as a PageRenderSummary which is
/// a lighter weight projection designed for rendering to a site when the 
/// templates, region and block data is not required. The result is 
/// version-sensitive and defaults to returning published versions only, but
/// this behavior can be controlled by the publishStatus query property.
/// </summary>
public class GetPageRenderSummaryByIdQueryHandler
    : IQueryHandler<GetPageRenderSummaryByIdQuery, PageRenderSummary?>
    , IPermissionRestrictedQueryHandler<GetPageRenderSummaryByIdQuery, PageRenderSummary?>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IPageRenderSummaryMapper _pageRenderSummaryMapper;

    public GetPageRenderSummaryByIdQueryHandler(
        CofoundryDbContext dbContext,
        IQueryExecutor queryExecutor,
        IPageRenderSummaryMapper pageRenderSummaryMapper
        )
    {
        _dbContext = dbContext;
        _queryExecutor = queryExecutor;
        _pageRenderSummaryMapper = pageRenderSummaryMapper;
    }

    public async Task<PageRenderSummary?> ExecuteAsync(GetPageRenderSummaryByIdQuery query, IExecutionContext executionContext)
    {
        var pageRouteQuery = new GetPageRouteByIdQuery(query.PageId);
        var pageRoute = await _queryExecutor.ExecuteAsync(pageRouteQuery, executionContext);
        if (pageRoute == null)
        {
            return null;
        }

        var dbPage = await QueryPageAsync(query, executionContext);
        if (dbPage == null)
        {
            return null;
        }

        var page = _pageRenderSummaryMapper.Map(dbPage, pageRoute);

        return page;
    }

    private async Task<PageVersion?> QueryPageAsync(GetPageRenderSummaryByIdQuery query, IExecutionContext executionContext)
    {
        PageVersion? result;

        if (query.PublishStatus == PublishStatusQuery.SpecificVersion)
        {
            if (!query.PageVersionId.HasValue)
            {
                throw new Exception("A PageVersionId must be included in the query to use PublishStatusQuery.SpecificVersion");
            }

            // ORMConvertor: rewritten - FilterActive, FilterByPageId and FilterByPageVersionId as the Where each wraps
            // (PageVersionQueryExtensions), the argument of FilterByPageVersionId evaluated before the query, as the
            // call evaluates it. The project's text:
            // result = await _dbContext
                // .PageVersions
                // .AsNoTracking()
                // .Include(v => v.OpenGraphImageAsset)
                // .FilterActive()
                // .FilterByPageId(query.PageId)
                // .FilterByPageVersionId(query.PageVersionId.Value)
                // .FirstOrDefaultAsync();
            var pageVersionId = query.PageVersionId.Value;
            result = await _dbContext
                .PageVersions
                .AsNoTracking()
                .Include(v => v.OpenGraphImageAsset)
                .Where(v => !v.PageTemplate.IsArchived)
                .Where(v => v.PageId == query.PageId)
                .Where(v => v.PageVersionId == pageVersionId)
                .FirstOrDefaultAsync();
        }
        else
        {
            // ORMConvertor: left out - FilterByStatus picks its filter by the status the caller passes - with the publish date for Published, without
            // it otherwise -, so the query is chosen at run time, which the tool does not read. The project's text:
            // result = await _dbContext
                // .PagePublishStatusQueries
                // .AsNoTracking()
                // .Include(v => v.PageVersion)
                // .ThenInclude(v => v.OpenGraphImageAsset)
                // .FilterActive()
                // .FilterByStatus(query.PublishStatus, executionContext.ExecutionDate)
                // .FilterByPageId(query.PageId)
                // .Select(p => p.PageVersion)
                // .FirstOrDefaultAsync();
        }

        return result;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageRenderSummaryByIdQuery query)
    {
        yield return new PageReadPermission();
    }
}
