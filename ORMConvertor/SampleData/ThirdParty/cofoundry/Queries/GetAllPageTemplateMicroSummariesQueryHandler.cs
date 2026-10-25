// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageTemplates/Queries/GetAllPageTemplateMicroSummariesQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

public class GetAllPageTemplateMicroSummariesQueryHandler
    : IQueryHandler<GetAllPageTemplateMicroSummariesQuery, IReadOnlyCollection<PageTemplateMicroSummary>>
    , IPermissionRestrictedQueryHandler<GetAllPageTemplateMicroSummariesQuery, IReadOnlyCollection<PageTemplateMicroSummary>>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IPageTemplateMicroSummaryMapper _pageTemplateMapper;

    public GetAllPageTemplateMicroSummariesQueryHandler(
        CofoundryDbContext dbContext,
        IPageTemplateMicroSummaryMapper pageTemplateMapper
        )
    {
        _dbContext = dbContext;
        _pageTemplateMapper = pageTemplateMapper;
    }

    public async Task<IReadOnlyCollection<PageTemplateMicroSummary>> ExecuteAsync(GetAllPageTemplateMicroSummariesQuery query, IExecutionContext executionContext)
    {
        var dbResults = await _dbContext
            .PageTemplates
            .AsNoTracking()
            .Where(p => !p.IsArchived) // ORMConvertor: was .FilterActive()
            .OrderBy(l => l.FileName)
            .ToArrayAsync();

        var results = dbResults
            .Select(_pageTemplateMapper.Map)
            .ToArray();

        return results;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetAllPageTemplateMicroSummariesQuery query)
    {
        yield return new PageTemplateReadPermission();
    }
}
