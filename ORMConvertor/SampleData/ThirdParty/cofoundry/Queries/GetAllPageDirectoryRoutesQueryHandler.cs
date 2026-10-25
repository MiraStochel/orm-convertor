// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageDirectories/Queries/GetAllPageDirectoryRoutesQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Returns all page directories as PageDirectoryRoute objects. The results of this 
/// query are cached.
/// </summary>
public class GetAllPageDirectoryRoutesQueryHandler
    : IQueryHandler<GetAllPageDirectoryRoutesQuery, IReadOnlyCollection<PageDirectoryRoute>>
    , IPermissionRestrictedQueryHandler<GetAllPageDirectoryRoutesQuery, IReadOnlyCollection<PageDirectoryRoute>>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IPageDirectoryRouteMapper _pageDirectoryRouteMapper;

    public GetAllPageDirectoryRoutesQueryHandler(
        CofoundryDbContext dbContext,
        IPageDirectoryRouteMapper pageDirectoryRouteMapper
        )
    {
        _dbContext = dbContext;
        _pageDirectoryRouteMapper = pageDirectoryRouteMapper;
    }

    public async Task<IReadOnlyCollection<PageDirectoryRoute>> ExecuteAsync(GetAllPageDirectoryRoutesQuery query, IExecutionContext executionContext)
    {
        var dbPageDirectories = await _dbContext
            .Set<PageDirectory>() // ORMConvertor: was .PageDirectories
            .AsNoTracking()
            .Include(d => d.AccessRules)
            .Include(d => d.PageDirectoryLocales)
            .ToArrayAsync();

        var activeWebRoutes = _pageDirectoryRouteMapper.Map(dbPageDirectories);

        return activeWebRoutes;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetAllPageDirectoryRoutesQuery query)
    {
        yield return new PageDirectoryReadPermission();
    }
}
