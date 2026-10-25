// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageDirectories/Queries/AccessRules/GetPageDirectoryAccessDetailsByPageDirectoryIdQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;
using Cofoundry.Domain.Internal;

namespace Cofoundry.Domain;

/// <summary>
/// Returns all access rules associated with a specific page using a default
/// ordering of specificity i.e. with user area rules before role-based rules.
/// </summary>
public class GetPageDirectoryAccessDetailsByPageDirectoryIdQueryHandler
    : IQueryHandler<GetPageDirectoryAccessDetailsByPageDirectoryIdQuery, PageDirectoryAccessRuleSetDetails?>
    , IPermissionRestrictedQueryHandler<GetPageDirectoryAccessDetailsByPageDirectoryIdQuery, PageDirectoryAccessRuleSetDetails?>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IEntityAccessRuleSetDetailsMapper _entityAccessDetailsMapper;
    private readonly IPageDirectoryMicroSummaryMapper _pageDirectoryMicroSummaryMapper;

    public GetPageDirectoryAccessDetailsByPageDirectoryIdQueryHandler(
        CofoundryDbContext dbContext,
        IEntityAccessRuleSetDetailsMapper entityAccessDetailsMapper,
        IPageDirectoryMicroSummaryMapper pageDirectoryMicroSummaryMapper
        )
    {
        _dbContext = dbContext;
        _entityAccessDetailsMapper = entityAccessDetailsMapper;
        _pageDirectoryMicroSummaryMapper = pageDirectoryMicroSummaryMapper;
    }

    public async Task<PageDirectoryAccessRuleSetDetails?> ExecuteAsync(GetPageDirectoryAccessDetailsByPageDirectoryIdQuery query, IExecutionContext executionContext)
    {
        var dbDirectory = await _dbContext
            .Set<PageDirectory>() // ORMConvertor: was .PageDirectories
            .AsNoTracking()
            .Include(d => d.AccessRules)
            .Where(i => i.PageDirectoryId == query.PageDirectoryId) // ORMConvertor: was .FilterById(query.PageDirectoryId)
            .SingleOrDefaultAsync();

        if (dbDirectory == null)
        {
            return null;
        }

        var result = new PageDirectoryAccessRuleSetDetails();
        await _entityAccessDetailsMapper.MapAsync(dbDirectory, result, executionContext, (dbRule, rule) =>
        {
            rule.PageDirectoryId = dbRule.PageDirectoryId;
            rule.PageDirectoryAccessRuleId = dbRule.PageDirectoryAccessRuleId;
        });

        result.PageDirectoryId = dbDirectory.PageDirectoryId;
        await MapInheritedRules(dbDirectory, result, executionContext);

        return result;
    }

    private async Task MapInheritedRules(PageDirectory dbPageDirectory, PageDirectoryAccessRuleSetDetails result, IExecutionContext executionContext)
    {
        // ORMConvertor: rewritten - FilterByDescendantId and FilterNotSelfReferencing as the Where each wraps
        // (PageDirectoryClosureQueryExtensions), and the count of the ancestor's access rules through the collection
        // AccessRules, a path the tool does not read, as a correlated Any over the access rules of the same
        // directory. The project's text:
        // var dbInheritedRules = await _dbContext
            // .PageDirectoryClosures
            // .AsNoTracking()
            // .Include(d => d.AncestorPageDirectory)
            // .ThenInclude(d => d.AccessRules)
            // .Include(d => d.AncestorPageDirectory)
            // .ThenInclude(d => d.PageDirectoryPath)
            // .FilterByDescendantId(dbPageDirectory.PageDirectoryId)
            // .FilterNotSelfReferencing()
            // .Where(d => d.DescendantPageDirectoryId == dbPageDirectory.PageDirectoryId && d.AncestorPageDirectory.AccessRules.Count != 0)
            // .OrderByDescending(d => d.Distance)
            // .ToArrayAsync();
        var dbInheritedRules = await _dbContext
            .PageDirectoryClosures
            .AsNoTracking()
            .Include(d => d.AncestorPageDirectory)
            .ThenInclude(d => d.AccessRules)
            .Include(d => d.AncestorPageDirectory)
            .ThenInclude(d => d.PageDirectoryPath)
            .Where(i => i.DescendantPageDirectoryId == dbPageDirectory.PageDirectoryId)
            .Where(i => i.AncestorPageDirectoryId != i.DescendantPageDirectoryId)
            .Where(d => d.DescendantPageDirectoryId == dbPageDirectory.PageDirectoryId && _dbContext.PageDirectoryAccessRules.Any(r => r.PageDirectoryId == d.AncestorPageDirectoryId))
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

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageDirectoryAccessDetailsByPageDirectoryIdQuery query)
    {
        yield return new PageDirectoryReadPermission();
    }
}
