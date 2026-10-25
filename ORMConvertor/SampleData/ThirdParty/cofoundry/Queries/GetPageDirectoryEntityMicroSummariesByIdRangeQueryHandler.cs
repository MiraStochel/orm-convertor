// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Domain/PageDirectories/Queries/GetPageDirectoryEntityMicroSummariesByIdRangeQueryHandler.cs.
// Changed: the project's query extensions are written as the Where (OrderBy, Include) each one wraps,
// and what the tool does not read is rewritten or left out, each change marked where it stands.
// See README.md in the folder.
using Cofoundry.Domain.Data;

namespace Cofoundry.Domain.Internal;

public class GetPageDirectoryEntityMicroSummariesByIdRangeQueryHandler
    : IQueryHandler<GetPageDirectoryEntityMicroSummariesByIdRangeQuery, IReadOnlyDictionary<int, RootEntityMicroSummary>>
    , IPermissionRestrictedQueryHandler<GetPageDirectoryEntityMicroSummariesByIdRangeQuery, IReadOnlyDictionary<int, RootEntityMicroSummary>>
{
    private readonly CofoundryDbContext _dbContext;
    private readonly IEntityDefinitionRepository _entityDefinitionRepository;

    public GetPageDirectoryEntityMicroSummariesByIdRangeQueryHandler(
        CofoundryDbContext dbContext,
        IEntityDefinitionRepository entityDefinitionRepository
        )
    {
        _dbContext = dbContext;
        _entityDefinitionRepository = entityDefinitionRepository;
    }

    public async Task<IReadOnlyDictionary<int, RootEntityMicroSummary>> ExecuteAsync(GetPageDirectoryEntityMicroSummariesByIdRangeQuery query, IExecutionContext executionContext)
    {
        var definition = _entityDefinitionRepository.GetRequiredByCode(PageDirectoryEntityDefinition.DefinitionCode);

        var results = await _dbContext
            .PageDirectories
            .AsNoTracking()
            .Where(d => query.PageDirectoryIds.Contains(d.PageDirectoryId))
            .Select(d => new RootEntityMicroSummary()
            {
                RootEntityId = d.PageDirectoryId,
                RootEntityTitle = d.Name ?? d.UrlPath,
                EntityDefinitionName = definition.Name,
                EntityDefinitionCode = definition.EntityDefinitionCode
            })
            .ToDictionaryAsync(e => e.RootEntityId);

        return results;
    }

    public IEnumerable<IPermissionApplication> GetPermissions(GetPageDirectoryEntityMicroSummariesByIdRangeQuery query)
    {
        yield return new PageDirectoryReadPermission();
    }
}
