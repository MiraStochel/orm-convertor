// ORMConvertor: taken from cofoundry-cms/cofoundry at d3b30b1, src/Cofoundry.Domain/Data/DbContext/CofoundryDbContext.cs.
// Changed: the DbSet properties of the entities outside the excerpt are left out, marked where they
// stood. See README.md in the folder.
using Cofoundry.Core.EntityFramework;

namespace Cofoundry.Domain.Data;

/// <summary>
/// The main Cofoundry entity framework DbContext representing all the main 
/// entities in the Cofoundry database. Direct access to the <see cref="CofoundryDbContext"/>
/// is discouraged, instead we advise you use the domain queries and commands
/// available in the Cofoundry data repositories, see <see href="https://www.cofoundry.org/docs/framework/data-access"/>
/// </summary>
public partial class CofoundryDbContext : DbContext
{
    // ORMConvertor: the DbSet properties of the entities outside the excerpt are left out - authorized tasks,
    // asset file cleanup, custom entities and their definitions, entity definitions, document assets, IP
    // addresses, rewrite rules, settings, user authentication logs and unstructured data dependencies -,
    // because the tool takes a DbSet for an entity of the conversion and none of their classes is here.

    private readonly ICofoundryDbContextInitializer _cofoundryDbContextInitializer;

    public CofoundryDbContext(ICofoundryDbContextInitializer cofoundryDbContextInitializer)
    {
        _cofoundryDbContextInitializer = cofoundryDbContextInitializer;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        _cofoundryDbContextInitializer.Configure(this, optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(DbConstants.CofoundrySchema)
            .MapCofoundryContent()
            .ApplyConfiguration(new SettingMap())
            .ApplyConfiguration(new RewriteRuleMap());
    }

    /// <summary>
    /// A domain used as part of an email address i.e. the part after
    /// the '@'. These are stored as "unique" email domains, whereby
    /// multiple domains that resolve to the same mailboxes will only have
    /// a single EmailDomain entry in the database.
    /// </summary>
    public DbSet<EmailDomain> EmailDomains { get; set; }

    [Obsolete("The document asset grouping system will be revised in an upcomming release.")]
    [Obsolete("The document asset grouping system will be revised in an upcomming release.")]
    [Obsolete("The image asset grouping system will be revised in an upcomming release.")]
    public DbSet<ImageAssetGroupItem> ImageAssetGroupItems { get; set; }

    [Obsolete("The image asset grouping system will be revised in an upcomming release.")]
    public DbSet<ImageAssetGroup> ImageAssetGroups { get; set; }

    /// <summary>
    /// Represents an image that has been uploaded to the CMS.
    /// </summary>
    public DbSet<ImageAsset> ImageAssets { get; set; }

    public DbSet<ImageAssetTag> ImageAssetTags { get; set; }

    /// <summary>
    /// A Page Template represents a physical view template file and is used
    /// by a Page to render out content. 
    /// </summary>
    public DbSet<PageTemplate> PageTemplates { get; set; }

    /// <summary>
    /// Each PageTemplate can have zero or more regions which are defined in the 
    /// template file using the CofoundryTemplate helper, 
    /// e.g. @Cofoundry.Template.Region("MyRegionName"). These regions represent
    /// areas where page blocks can be placed (i.e. insert content).
    /// </summary>
    public DbSet<PageTemplateRegion> PageTemplateRegions { get; set; }

    public DbSet<Locale> Locales { get; set; }

    /// <summary>
    /// Pages represent the dynamically navigable pages of your website. Each page uses a template 
    /// which defines the regions of content that users can edit. Pages are a versioned entity and 
    /// therefore have many page version records. At one time a page may only have one draft 
    /// version, but can have many published versions; the latest published version is the one that 
    /// is rendered when the page is published. 
    /// </summary>
    public DbSet<Page> Pages { get; set; }

    /// <summary>
    /// <para>
    /// Access rules are used to restrict access to a website resource to users
    /// fulfilling certain criteria such as a specific user area or role. Page
    /// access rules are used to define the rules at a <see cref="Page"/> level, 
    /// however rules are also inherited from the directories the page is parented to.
    /// </para>
    /// <para>
    /// Note that access rules do not apply to users from the Cofoundry Admin user
    /// area. They aren't intended to be used to restrict editor access in the admin UI 
    /// but instead are used to restrict public access to website pages and routes.
    /// </para>
    /// </summary>
    public DbSet<PageAccessRule> PageAccessRules { get; set; }

    /// <summary>
    /// Represents a folder in the dynamic web page heirarchy. There is always a 
    /// single root directory.
    /// </summary>
    public DbSet<PageDirectory> PageDirectories { get; set; }

    /// <summary>
    /// Information about the full directory path and it's position in the directory 
    /// heirachy. This table is automatically updated whenever changes are made to the page 
    /// directory heirarchy and should be treated as read-only.
    /// </summary>
    public DbSet<PageDirectoryPath> PageDirectoryPaths { get; set; }

    /// <summary>
    /// <para>
    /// A "closure table" for the page directory heirarchy structure that connects
    /// every page directory with each of it's ancestor directories. The table also
    /// includes a self-referencing node (the same ancestor and descendant id).
    /// </para>
    /// <para>
    /// This table is automatically updated whenever changes are made to the page 
    /// directory heirarchy and should be treated as read-only.
    /// </para>
    /// </summary>
    public DbSet<PageDirectoryClosure> PageDirectoryClosures { get; set; }

    /// <summary>
    /// <para>
    /// Access rules are used to restrict access to a website resource to users
    /// fulfilling certain criteria such as a specific user area or role. Page
    /// directory access rules are used to define the rules at a <see cref="PageDirectory"/> 
    /// level. These rules are inherited by child directories and pages.
    /// </para>
    /// <para>
    /// Note that access rules do not apply to users from the Cofoundry Admin user
    /// area. They aren't intended to be used to restrict editor access in the admin UI 
    /// but instead are used to restrict public access to website pages and routes.
    /// </para>
    /// </summary>
    public DbSet<PageDirectoryAccessRule> PageDirectoryAccessRules { get; set; }

    public DbSet<PageDirectoryLocale> PageDirectoryLocales { get; set; }

    /// <summary>
    /// A block can optionally have display templates associated with it, 
    /// which will give the user a choice about how the data is rendered out
    /// e.g. 'Wide', 'Headline', 'Large', 'Reversed'. If no template is set then 
    /// the default view is used for rendering.
    /// </summary>
    public DbSet<PageBlockTypeTemplate> PageBlockTypeTemplates { get; set; }

    /// <summary>
    /// Page block types represent a type of content that can be inserted into a content 
    /// region of a page which could be simple content like 'RawHtml', 'Image' or 
    /// 'PlainText'. Custom and more complex block types can be defined by a 
    /// developer. Block types are typically created when the application
    /// starts up in the auto-update process.
    /// </summary>
    public DbSet<PageBlockType> PageBlockTypes { get; set; }

    [Obsolete("The page grouping system will be revised in an upcomming release.")]
    public DbSet<PageGroupItem> PageGroupItems { get; set; }

    [Obsolete("The page grouping system will be revised in an upcomming release.")]
    public DbSet<PageGroup> PageGroups { get; set; }

    public DbSet<PageTag> PageTags { get; set; }

    /// <summary>
    /// Pages are a versioned entity and therefore have many page version
    /// records. At one time a page may only have one draft version, but
    /// can have many published versions; the latest published version is
    /// the one that is rendered when the page is published. 
    /// </summary>
    public DbSet<PageVersion> PageVersions { get; set; }

    public DbSet<PageVersionBlock> PageVersionBlocks { get; set; }

    /// <summary>
    /// Lookup cache used for quickly finding the correct version for a
    /// specific publish status query e.g. 'Latest', 'Published', 
    /// 'PreferPublished'. These records are generated when pages
    /// are published or unpublished.
    /// </summary>
    public DbSet<PagePublishStatusQuery> PagePublishStatusQueries { get; set; }

    /// <summary>
    /// A permission represents an type action a user can
    /// be permitted to perform. Typically this is associated
    /// with a specified entity type, but doesn't have to be e.g.
    /// "read pages", "access dashboard", "delete images". The 
    /// combination of EntityDefinitionCode and PermissionCode
    /// must be unique
    /// </summary>
    public DbSet<Permission> Permissions { get; set; }

    /// <summary>
    /// Roles are an assignable collection of permissions. Every user has to 
    /// be assigned to one role.
    /// </summary>
    public DbSet<Role> Roles { get; set; }

    public DbSet<RolePermission> RolePermissions { get; set; }

    public DbSet<Tag> Tags { get; set; }

    /// <summary>
    /// Represents the user in the Cofoundry custom identity system. Users can be partitioned into
    /// different 'User Areas' that enabled the identity system use by the Cofoundry administration area 
    /// to be reused for other purposes, but this isn't a common scenario and often there will only be the Cofoundry UserArea.
    /// </summary>
    public DbSet<User> Users { get; set; }

    /// <summary>
    /// Users can be partitioned into different 'User Areas' that enabled the identity system use by the Cofoundry administration area 
    /// to be reused for other purposes, but this isn't a common scenario and often there will only be the Cofoundry UserArea. UserAreas
    /// are defined in code by defining an IUserAreaDefinition
    /// </summary>
    public DbSet<UserArea> UserAreas { get; set; }

}
