// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/Pages/Queries/AccessRules/GetPageAccessRuleSetDetailsByPageIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;
using Cofoundry.Domain.Internal;

namespace Cofoundry.Domain;

/// <summary>
/// Query that returns detailed information about access restrictions
/// configured for a page, including all access rules as well as those 
/// inherited from parent directories.
/// </summary>
public class GetPageAccessRuleSetDetailsByPageIdQueryHandler
    : IQueryHandler<GetPageAccessRuleSetDetailsByPageIdQuery, PageAccessRuleSetDetails?>
    , IPermissionRestrictedQueryHandler<GetPageAccessRuleSetDetailsByPageIdQuery, PageAccessRuleSetDetails?>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IEntityAccessRuleSetDetailsMapper _entityAccessDetailsMapper;
    private readonly IPageDirectoryMicroSummaryMapper _pageDirectoryMicroSummaryMapper;

    public GetPageAccessRuleSetDetailsByPageIdQueryHandler(
        CofoundryDbContext dbContext,
        IEntityAccessRuleSetDetailsMapper entityAccessDetailsMapper,
        IPageDirectoryMicroSummaryMapper pageDirectoryMicroSummaryMapper
        )
    {
        _dbContext = dbContext;
        _entityAccessDetailsMapper = entityAccessDetailsMapper;
        _pageDirectoryMicroSummaryMapper = pageDirectoryMicroSummaryMapper;
    }

    public async Task<PageAccessRuleSetDetails?> ExecuteAsync(GetPageAccessRuleSetDetailsByPageIdQuery query, IExecutionContext executionContext)
    {
        var dbPage = await _dbContext
            .Pages
            .AsNoTracking()
            .Include(p => p.AccessRules)
            // ORMConvertor: .FilterActive() is left out - the FilterActive of Page returns the pages unchanged (PageQueryExtensions)
            .Where(i => i.PageId == query.PageId) // ORMConvertor: was .FilterById(query.PageId)
            .SingleOrDefaultAsync();

        if (dbPage == null)
        {
            return null;
        }

        var result = new PageAccessRuleSetDetails();
        await _entityAccessDetailsMapper.MapAsync(dbPage, result, executionContext, (dbRule, rule) =>
        {
            rule.PageId = dbRule.PageId;
            rule.PageAccessRuleId = dbRule.PageAccessRuleId;
        });

        result.PageId = dbPage.PageId;
        await MapInheritedRules(dbPage, result, executionContext);

        return result;
    }

    private async Task MapInheritedRules(Page dbPage, PageAccessRuleSetDetails result, IExecutionContext executionContext)
    {
        // ORMConvertor: rewritten - FilterByDescendantId as the Where it wraps (PageDirectoryClosureQueryExtensions),
        // and the count of the ancestor's access rules through the collection AccessRules, a path the tool does not
        // read, as a correlated Any over the access rules of the same directory. The project's text:
        // var dbInheritedRules = await _dbContext
            // .PageDirectoryClosures
            // .AsNoTracking()
            // .Include(d => d.AncestorPageDirectory)
            // .ThenInclude(d => d.AccessRules)
            // .Include(d => d.AncestorPageDirectory)
            // .ThenInclude(d => d.PageDirectoryPath)
            // .FilterByDescendantId(dbPage.PageDirectoryId)
            // .Where(d => d.DescendantPageDirectoryId == dbPage.PageDirectoryId && d.AncestorPageDirectory.AccessRules.Count != 0)
            // .OrderByDescending(d => d.Distance)
            // .ToArrayAsync();
        var dbInheritedRules = await _dbContext
            .PageDirectoryClosures
            .AsNoTracking()
            .Include(d => d.AncestorPageDirectory)
            .ThenInclude(d => d.AccessRules)
            .Include(d => d.AncestorPageDirectory)
            .ThenInclude(d => d.PageDirectoryPath)
            .Where(i => i.DescendantPageDirectoryId == dbPage.PageDirectoryId)
            .Where(d => d.DescendantPageDirectoryId == dbPage.PageDirectoryId && _dbContext.PageDirectoryAccessRules.Any(r => r.PageDirectoryId == d.AncestorPageDirectoryId))
            .OrderByDescending(d => d.Distance)
            .ToArrayAsync();

        var inheritedAccessRules = new List<InheritedPageDirectoryAccessDetails>();

        foreach (var dbInheritedRule in dbInheritedRules)
        {
            var inheritedDirectory = new InheritedPageDirectoryAccessDetails();
            inheritedDirectory.PageDirectory = _pageDirectoryMicroSummaryMapper.Map(dbInheritedRule.AncestorPageDirectory);
            await _entityAccessDetailsMapper.MapAsync(dbInheritedRule.AncestorPageDirectory, inheritedDirectory, executionContext, (dbRule, rule) =>
            {
                rule.PageDirectoryId = dbRule.PageDirectoryId;
                rule.PageDirectoryAccessRuleId = dbRule.PageDirectoryAccessRuleId;
            });

            inheritedAccessRules.Add(inheritedDirectory);
        }

        result.InheritedAccessRules = inheritedAccessRules;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageAccessRuleSetDetailsByPageIdQuery query)
    {
        yield return new PageReadPermission();
    }
}
