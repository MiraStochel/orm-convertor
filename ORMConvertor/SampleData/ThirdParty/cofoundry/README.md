# Cofoundry: pages, directories, templates, users and roles

- **Project:** [cofoundry-cms/cofoundry](https://github.com/cofoundry-cms/cofoundry), a content
  management system on ASP.NET Core; EF Core 10 with SQL Server as its only database.
- **Commit:** `d3b30b1016c509c077ed1518d8a30864dcd799ed` (2026-02-13).
- **Licence:** MIT, [`LICENSE`](LICENSE).
- **Taken from:** `src/Cofoundry.Domain/Data/DbContext/` - 29 of the 47 entity classes there with
  their `IEntityTypeConfiguration<T>` classes and the context `CofoundryDbContext`: pages, their
  versions, blocks, tags, groups, access rules and publish status; page directories with their
  closure, paths, locales and access rules; templates and their regions; block types; users, user
  areas, email domains, roles and permissions; tags, locales and image assets. And from
  `src/Cofoundry.Domain/Domain/{Pages,PageDirectories,PageTemplates,PageBlockTypes,Users,Roles,Locales}/Queries/`
  the query handlers that read the context, each of which runs its queries over
  `CofoundryDbContext` directly.

## Schema

`schema.sql` is the schema of the 29 tables, scripted from a database built by the project's own
installation scripts at the same commit (`src/Cofoundry.Domain/Install/Db/Schema/00001.sql` to
`00010.sql`, run in order): Cofoundry publishes no single DDL script, and its scripts create the
tables and then alter, rename and drop along the versions. Columns, defaults, primary keys and
unique constraints are those of that database; the foreign keys between these tables are kept and
the nine that lead out of the excerpt left out, with the check constraints, the triggers and the
indexes, and `Cofoundry` is replaced by `{{schema}}` (decision 176).

## What was changed

Every change is marked where it stands, with a note and, for a query, the project's text above
it; the opening note of each file says whether the file was changed.

1. **The context keeps the `DbSet` properties of the excerpt's entities only.** The tool takes a
   `DbSet` for an entity of the conversion, and the classes of the other eighteen - custom
   entities, documents, settings, rewrite rules, authentication logs and the rest - are not here.
2. **Four navigations to entities outside the excerpt are left out** with the relations that
   configure them: `CustomEntityDefinition` of `Page` and `PageTemplate`, `EntityDefinition` of
   `Permission`, and the collection `CustomEntityVersionPageBlocks` of `PageBlockType`. The key
   columns stay.
3. **`ImageAsset.DefaultAnchorLocation` is typed `int?`**, the type of its column
   `ImageCropAnchorLocationId`, instead of the project's enum `ImageAnchorLocation`, which is not
   part of the excerpt and which no Java target could compile.
4. **Fifteen handlers are left out** because nothing in them is a query the tool translates: six
   ask a yes/no question with `AnyAsync` (`DoesPageHaveDraftVersion`, `IsPagePathUnique`,
   `IsPageDirectoryPathUnique`, `IsRoleTitleUnique`, `IsUserEmailAddressUnique`,
   `IsUsernameUnique`); five search handlers (`SearchPageRenderSummaries`, `SearchPageSummaries`,
   `SearchPageTemplateSummaries`, `SearchRoles`, `SearchUserSummaries`) compose their query at run
   time from the optional criteria of the search; `GetPageEntityMicroSummariesByIdRange` projects
   an intermediate result that the tool reads as a lateral reference;
   `GetPageRenderSummariesByIdRange` filters by a publish status chosen at run time (change 7);
   and `GetPageRoutingInfoByCustomEntityIdRange` and `ValidateUserAccountRecoveryByEmail` query
   custom entities and authorized tasks, which are not part of the excerpt.
5. **The project's query extensions are written as what they wrap.** Most queries are composed
   from Cofoundry's own extension methods over `IQueryable` (`FilterActive`, `FilterById`,
   `FilterByPageId`, `FilterByUserArea`, `OrderByLatest`, `IncludeForSummary` and others, in
   `Data/DbContext/**/*QueryExtensions.cs` of the project). The tool does not see the body of a
   method and drops a step it does not know with a `Loss`, which here would drop the filter; so
   each call is written as the `Where` (`OrderBy`, `Include`) its body consists of, with the call
   in a note at the end of the line. The `FilterActive` of `Page` returns the pages unchanged and
   is left out. Where the argument of a call is more than a name or a member of one
   (`query.PageVersionId.Value`, `tokenResult.Data.UserId`), it is evaluated into a local before
   the query, as the call evaluates it; `!x.HasValue` in the bodies of `FilterCanSignIn` and
   `FilterNotDeleted` is written `x == null`, the form the tool reads.
6. **A query the tool reads only in another form is rewritten**: the inherited access rules of a
   page and of a page directory filter by `d.AncestorPageDirectory.AccessRules.Count != 0`, a
   path into a collection, which becomes a correlated `Any` over `PageDirectoryAccessRules`
   matching the ancestor directory.
7. **Queries the tool does not translate are left out**, their text kept as a comment:
   `FilterByStatus` with a status the caller passes (`GetPageRenderDetailsById`,
   `GetPageRenderDetailsByIdRange`, `GetPageRenderSummaryById`) picks one of two filters by the
   value, so the query is chosen at run time - with the constant `Latest`
   (`GetPageSummariesByIdRange`) it is the filter of its branch; the join along two associations
   in `GetPageRegionDetailsByPageVersionId`; the `CountAsync` of `GetPageTemplateDetailsById`; and
   in `GetPageRouteLookup` two statements of LINQ over versions already in memory, which the tool
   takes for a query because `PageRoute`, whose member they read, is not part of the excerpt
   (decision 114), and the private classes that carry its projections, which the tool would read
   as entities.
8. **The write is left out**: `AuthenticateUserCredentials` rehashes a password and saves the
   user; the method, its call and the helper it uses go.
9. **Two sets are named by their entity**: `_dbContext.PageDirectories` and
   `_dbContext.PagePublishStatusQueries` are written `Set<PageDirectory>()` and
   `Set<PagePublishStatusQuery>()`. The tool resolves the name of a set to its entity only through
   the table name or the plural with an `s`, and the configuration names the tables
   `PageDirectory` and `PagePublishStatusQuery`; the query would go out over a type and a table
   that do not exist (`PageDirectorie`, `PageDirectories`). That is a defect of the tool
   (finding V7 in `docs/analysis/real-world-sources.md`); the excerpt goes back to the project's
   text once it is fixed.
10. **An upstream mistake is corrected**: `PageGroup.ParentGroupId` maps by convention to a
    column `ParentGroupId`, while the project's installation script creates `ParentPageGroupId`,
    so no query over page groups runs against the project's own schema; `PageGroupMap` names the
    column. The project marks its page groups obsolete.

Named projections (`new PageGroupMicroSummary { … }`), `Include`, `ThenInclude` and
`ToDictionaryAsync` stay as the project wrote them: the tool materializes the entity instead of a
named projection and leaves the other three out, each with a `Loss`, and none of it changes the
rows a query returns.
