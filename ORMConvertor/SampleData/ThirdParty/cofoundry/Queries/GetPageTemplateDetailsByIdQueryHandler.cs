// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageTemplates/Queries/GetPageTemplateDetailsByIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;
using Cofoundry.Domain.QueryModels;

namespace Cofoundry.Domain.Internal;

public class GetPageTemplateDetailsByIdQueryHandler
    : IQueryHandler<GetPageTemplateDetailsByIdQuery, PageTemplateDetails?>
    , IPermissionRestrictedQueryHandler<GetPageTemplateDetailsByIdQuery, PageTemplateDetails?>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IPageTemplateDetailsMapper _pageTemplateDetailsMapper;

    public GetPageTemplateDetailsByIdQueryHandler(
        CofoundryDbContext dbContext,
        IQueryExecutor queryExecutor,
        IPageTemplateDetailsMapper pageTemplateDetailsMapper
        )
    {
        _queryExecutor = queryExecutor;
        _dbContext = dbContext;
        _pageTemplateDetailsMapper = pageTemplateDetailsMapper;
    }

    public async Task<PageTemplateDetails?> ExecuteAsync(GetPageTemplateDetailsByIdQuery query, IExecutionContext executionContext)
    {
        var dbTemplate = await _dbContext
            .PageTemplates
            .AsNoTracking()
            .Include(t => t.PageTemplateRegions)
            .Where(l => l.PageTemplateId == query.PageTemplateId)
            .SingleOrDefaultAsync();

        if (dbTemplate == null)
        {
            return null;
        }

        var queryModel = new PageTemplateDetailsQueryModel()
        {
            PageTemplate = dbTemplate
        };

        if (!string.IsNullOrEmpty(queryModel.PageTemplate.CustomEntityDefinitionCode))
        {
            var definitionQuery = new GetCustomEntityDefinitionMicroSummaryByCodeQuery(queryModel.PageTemplate.CustomEntityDefinitionCode);
            queryModel.CustomEntityDefinition = await _queryExecutor.ExecuteAsync(definitionQuery, executionContext);
        }

        // ORMConvertor: left out - the query ends in CountAsync, a scalar the tool does not translate. The project's text:
        // queryModel.NumPages = await _dbContext
            // .PageVersions
            // .AsNoTracking()
            // .Where(v => v.PageTemplateId == query.PageTemplateId)
            // .Select(v => v.Page)
            // .Distinct()
            // .CountAsync();

        var template = _pageTemplateDetailsMapper.Map(queryModel);

        return template;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageTemplateDetailsByIdQuery query)
    {
        yield return new PageTemplateReadPermission();
    }
}
