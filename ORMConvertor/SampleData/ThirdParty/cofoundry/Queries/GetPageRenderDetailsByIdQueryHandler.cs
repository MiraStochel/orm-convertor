// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/GetPageRenderDetailsByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Gets a projection of a page that contains the data required to render a page, including template 
/// data for all the content-editable regions.
/// </summary>
public class GetPageRenderDetailsByIdQueryHandler
    : IQueryHandler<GetPageRenderDetailsByIdQuery, PageRenderDetails?>
    , IPermissionRestrictedQueryHandler<GetPageRenderDetailsByIdQuery, PageRenderDetails?>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IPageRenderDetailsMapper _pageRenderDetailsMapper;
    private readonly IEntityVersionPageBlockMapper _entityVersionPageBlockMapper;

    public GetPageRenderDetailsByIdQueryHandler(
        CofoundryDbContext dbContext,
        IQueryExecutor queryExecutor,
        IPageRenderDetailsMapper pageMapper,
        IEntityVersionPageBlockMapper entityVersionPageBlockMapper
        )
    {
        _dbContext = dbContext;
        _queryExecutor = queryExecutor;
        _pageRenderDetailsMapper = pageMapper;
        _entityVersionPageBlockMapper = entityVersionPageBlockMapper;
    }

    public async Task<PageRenderDetails?> ExecuteAsync(GetPageRenderDetailsByIdQuery query, IExecutionContext executionContext)
    {
        var dbPage = await QueryPageAsync(query, executionContext);
        if (dbPage == null)
        {
            return null;
        }

        var pageRouteQuery = new GetPageRouteByIdQuery(dbPage.PageId);
        var pageRoute = await _queryExecutor.ExecuteAsync(pageRouteQuery, executionContext);
        if (pageRoute == null)
        {
            return null;
        }

        var page = _pageRenderDetailsMapper.Map(dbPage, pageRoute);

        var dbPageBlocks = await GetPageBlocksAsync(page);
        var allBlockTypes = await _queryExecutor.ExecuteAsync(new GetAllPageBlockTypeSummariesQuery(), executionContext);

        await _entityVersionPageBlockMapper.MapRegionsAsync(
            dbPageBlocks,
            page.Regions,
            allBlockTypes,
            query.PublishStatus,
            executionContext
            );

        return page;
    }

    private async Task<PageVersion?> QueryPageAsync(GetPageRenderDetailsByIdQuery query, IExecutionContext executionContext)
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
                // .Include(v => v.Page)
                // .Include(v => v.OpenGraphImageAsset)
                // .Include(v => v.PageTemplate)
                // .ThenInclude(t => t.PageTemplateRegions)
                // .FilterActive()
                // .FilterByPageId(query.PageId)
                // .FilterByPageVersionId(query.PageVersionId.Value)
                // .FirstOrDefaultAsync();
            var pageVersionId = query.PageVersionId.Value;
            result = await _dbContext
                .PageVersions
                .AsNoTracking()
                .Include(v => v.Page)
                .Include(v => v.OpenGraphImageAsset)
                .Include(v => v.PageTemplate)
                .ThenInclude(t => t.PageTemplateRegions)
                .Where(v => !v.PageTemplate.IsArchived)
                .Where(v => v.PageId == query.PageId)
                .Where(v => v.PageVersionId == pageVersionId)
                .FirstOrDefaultAsync();
        }
        else
        {
            // ORMConvertor: left out - FilterByStatus picks its filter by the status the caller passes - with the publish date for Published, without
            // it otherwise -, so the query is chosen at run time, which the tool does not read. The project's text:
            // var queryResult = await _dbContext
                // .PagePublishStatusQueries
                // .AsNoTracking()
                // .Include(q => q.PageVersion)
                // .ThenInclude(v => v.Page)
                // .Include(q => q.PageVersion)
                // .ThenInclude(v => v.OpenGraphImageAsset)
                // .Include(q => q.PageVersion)
                // .ThenInclude(v => v.PageTemplate)
                // .ThenInclude(t => t.PageTemplateRegions)
                // .FilterActive()
                // .FilterByStatus(query.PublishStatus, executionContext.ExecutionDate)
                // .FilterByPageId(query.PageId)
                // .FirstOrDefaultAsync();

            result = queryResult?.PageVersion;
        }

        return result;
    }

    private async Task<IReadOnlyCollection<PageVersionBlock>> GetPageBlocksAsync(PageRenderDetails page)
    {
        var dbBlocks = await _dbContext
            .PageVersionBlocks
            .Where(p => !p.PageBlockType.IsArchived) // ORMConvertor: was .FilterActive()
            .AsNoTracking()
            .Where(m => m.PageVersionId == page.PageVersionId)
            .ToArrayAsync();

        return dbBlocks;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageRenderDetailsByIdQuery query)
    {
        yield return new PageReadPermission();
    }
}
