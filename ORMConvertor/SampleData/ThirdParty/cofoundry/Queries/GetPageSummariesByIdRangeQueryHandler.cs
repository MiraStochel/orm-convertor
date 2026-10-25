// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/GetPageSummariesByIdRangeQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Finds pages with the specified page ids and returns them as PageSummary 
/// objects. Note that this query does not account for WorkFlowStatus and so
/// pages will be returned irrecpective of whether they aree published or not.
/// </summary>
public class GetPageSummariesByIdRangeQueryHandler
    : IQueryHandler<GetPageSummariesByIdRangeQuery, IReadOnlyDictionary<int, PageSummary>>
    , IIgnorePermissionCheckHandler
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IPageSummaryMapper _pageSummaryMapper;

    public GetPageSummariesByIdRangeQueryHandler(
        CofoundryDbContext dbContext,
        IPageSummaryMapper pageSummaryMapper
        )
    {
        _dbContext = dbContext;
        _pageSummaryMapper = pageSummaryMapper;
    }

    public async Task<IReadOnlyDictionary<int, PageSummary>> ExecuteAsync(GetPageSummariesByIdRangeQuery query, IExecutionContext executionContext)
    {
        var dbResult = await QueryPages(query, executionContext);

        var mappedResult = await _pageSummaryMapper.MapAsync(dbResult, executionContext);
        var dictionary = mappedResult.ToImmutableDictionary(d => d.PageId);

        return dictionary;
    }

    private async Task<IReadOnlyCollection<Page>> QueryPages(GetPageSummariesByIdRangeQuery query, IExecutionContext executionContext)
    {
        // ORMConvertor: rewritten - the set PagePublishStatusQueries as Set<PagePublishStatusQuery>(), because the
        // tool resolves a set's name to its entity only by the plural with an s (see README.md); FilterByStatus with
        // the constant Latest as the Where of its branch for every status but Published, the status evaluated before
        // the query, as the call evaluates it; FilterActive as its Where (PagePublishStatusQueryExtensions).
        // The project's text:
        // var results = await _dbContext
            // .PagePublishStatusQueries
            // .AsNoTracking()
            // .Include(p => p.Page)
            // .ThenInclude(p => p.Creator)
            // .FilterByStatus(PublishStatusQuery.Latest, executionContext.ExecutionDate)
            // .FilterActive()
            // .Where(p => query.PageIds.Contains(p.PageId))
            // .Select(p => p.Page)
            // .ToArrayAsync();
        var publishStatusQueryId = (short)PublishStatusQuery.Latest;
        var results = await _dbContext
            .Set<PagePublishStatusQuery>()
            .AsNoTracking()
            .Include(p => p.Page)
            .ThenInclude(p => p.Creator)
            .Where(p => p.PublishStatusQueryId == publishStatusQueryId)
            .Where(p => !p.PageVersion.PageTemplate.IsArchived)
            .Where(p => query.PageIds.Contains(p.PageId))
            .Select(p => p.Page)
            .ToArrayAsync();

        return results;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(SearchPageSummariesQuery query)
    {
        yield return new PageReadPermission();
    }
}
