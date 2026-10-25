// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageDirectories/Queries/GetPageDirectoryTreeQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

/// <summary>
/// Returns a complete tree of page directory nodes, starting
/// with the root directory as a single node.
/// </summary>
public class GetPageDirectoryTreeQueryHandler
    : IQueryHandler<GetPageDirectoryTreeQuery, PageDirectoryNode>
    , IPermissionRestrictedQueryHandler<GetPageDirectoryTreeQuery, PageDirectoryNode>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IPageDirectoryTreeMapper _pageDirectoryTreeMapper;

    public GetPageDirectoryTreeQueryHandler(
        CofoundryDbContext dbContext,
        IPageDirectoryTreeMapper pageDirectoryTreeMapper
        )
    {
        _dbContext = dbContext;
        _pageDirectoryTreeMapper = pageDirectoryTreeMapper;
    }

    public async Task<PageDirectoryNode> ExecuteAsync(GetPageDirectoryTreeQuery query, IExecutionContext executionContext)
    {
        var dbResults = await _dbContext
               .Set<PageDirectory>() // ORMConvertor: was .PageDirectories
               .AsNoTracking()
               .Include(w => w.Creator)
               .Select(d => new PageDirectoryTreeNodeQueryModel()
               {
                   Creator = d.Creator,
                   PageDirectory = d,
                   NumPages = d.Pages.Count()
               })
               .ToArrayAsync();

        var result = _pageDirectoryTreeMapper.Map(dbResults);

        return result;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageDirectoryTreeQuery query)
    {
        yield return new PageDirectoryReadPermission();
    }
}
