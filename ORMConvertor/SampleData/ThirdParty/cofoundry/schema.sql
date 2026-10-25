-- ORMConvertor: this file is not part of cofoundry-cms/cofoundry; it is the schema of the excerpt's
-- 29 tables, scripted from a database built by the project's own installation scripts at d3b30b1
-- (src/Cofoundry.Domain/Install/Db/Schema/00001.sql to 00010.sql, run in order on SQL Server):
-- columns with their types, nullability, identity and defaults, primary keys and unique
-- constraints as that database holds them, and the foreign keys between these tables. Cofoundry
-- publishes no single DDL script; its scripts create the schema and then alter, rename and drop
-- along the versions, which is why the tables are taken from the database they build.
--
-- Left out: the nine foreign keys that lead out of the excerpt - to ImageCropAnchorLocation,
-- CustomEntityDefinition, PageType (two), PublishStatus, PublishStatusQuery, WorkFlowStatus and
-- EntityDefinition, the code tables and entities the excerpt does not take -, the check
-- constraints, the triggers and the indexes, none of which the catalog reads, and the schema
-- Cofoundry, which {{schema}} replaces.
--
-- {{schema}} is the schema the caller creates; the test suites put these tables, empty, into a
-- schema of their own as the catalog of the excerpt (decision 176).

CREATE TABLE [{{schema}}].[User]
(
    [UserId] int IDENTITY(1, 1) NOT NULL,
    [FirstName] nvarchar(32) NULL,
    [LastName] nvarchar(32) NULL,
    [Email] nvarchar(150) NULL,
    [Username] nvarchar(150) NOT NULL,
    [Password] nvarchar(max) NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [LastPasswordChangeDate] datetime2(7) NOT NULL,
    [PreviousSignInDate] datetime2(7) NULL,
    [LastSignInDate] datetime2(7) NULL,
    [RequirePasswordChange] bit NOT NULL DEFAULT ((0)),
    [CreatorId] int NULL,
    [RoleId] int NOT NULL,
    [UserAreaCode] char(3) NOT NULL,
    [PasswordHashVersion] int NULL,
    [IsSystemAccount] bit NULL,
    [EmailDomainId] int NULL,
    [UniqueEmail] nvarchar(150) NULL,
    [UniqueUsername] nvarchar(150) NOT NULL,
    [SecurityStamp] nvarchar(max) NOT NULL,
    [AccountVerifiedDate] datetime2(7) NULL,
    [DeletedDate] datetime2(7) NULL,
    [DeactivatedDate] datetime2(7) NULL,
    [DisplayName] nvarchar(150) NULL,
    CONSTRAINT [PK_User] PRIMARY KEY ([UserId])
);

CREATE TABLE [{{schema}}].[UserArea]
(
    [UserAreaCode] char(3) NOT NULL,
    [Name] nvarchar(20) NULL,
    CONSTRAINT [PK_UserArea] PRIMARY KEY ([UserAreaCode])
);

CREATE TABLE [{{schema}}].[Role]
(
    [RoleId] int IDENTITY(1, 1) NOT NULL,
    [Title] nvarchar(50) NOT NULL,
    [RoleCode] char(3) NULL,
    [UserAreaCode] char(3) NOT NULL,
    CONSTRAINT [PK_Role] PRIMARY KEY ([RoleId])
);

CREATE TABLE [{{schema}}].[Permission]
(
    [PermissionId] int IDENTITY(1, 1) NOT NULL,
    [EntityDefinitionCode] char(6) NULL,
    [PermissionCode] char(6) NOT NULL,
    CONSTRAINT [PK_Permission] PRIMARY KEY ([PermissionId])
);

CREATE TABLE [{{schema}}].[RolePermission]
(
    [RoleId] int NOT NULL,
    [PermissionId] int NOT NULL,
    CONSTRAINT [PK_RolePermission] PRIMARY KEY ([RoleId], [PermissionId])
);

CREATE TABLE [{{schema}}].[EmailDomain]
(
    [EmailDomainId] int IDENTITY(1, 1) NOT NULL,
    [Name] nvarchar(255) NOT NULL,
    [NameHash] binary(32) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_EmailDomain] PRIMARY KEY ([EmailDomainId])
);

CREATE TABLE [{{schema}}].[Locale]
(
    [LocaleId] int IDENTITY(1, 1) NOT NULL,
    [ParentLocaleId] int NULL,
    [IETFLanguageTag] nvarchar(16) NOT NULL,
    [LocaleName] nvarchar(64) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT ((0)),
    CONSTRAINT [PK_Locale] PRIMARY KEY ([LocaleId])
);

CREATE TABLE [{{schema}}].[Tag]
(
    [TagId] int IDENTITY(1, 1) NOT NULL,
    [TagText] nvarchar(32) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_Tag] PRIMARY KEY ([TagId])
);

CREATE TABLE [{{schema}}].[ImageAsset]
(
    [ImageAssetId] int IDENTITY(1, 1) NOT NULL,
    [FileName] nvarchar(130) NOT NULL,
    [WidthInPixels] int NOT NULL,
    [HeightInPixels] int NOT NULL,
    [FileExtension] nvarchar(30) NOT NULL,
    [FileSizeInBytes] bigint NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    [UpdateDate] datetime2(7) NOT NULL,
    [ImageCropAnchorLocationId] int NULL,
    [UpdaterId] int NOT NULL,
    [Title] nvarchar(130) NOT NULL,
    [FileUpdateDate] datetime2(7) NOT NULL,
    [FileNameOnDisk] varchar(50) NOT NULL,
    [VerificationToken] char(6) NOT NULL,
    CONSTRAINT [PK_ImageAsset] PRIMARY KEY ([ImageAssetId])
);

CREATE TABLE [{{schema}}].[ImageAssetTag]
(
    [ImageAssetId] int NOT NULL,
    [TagId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    CONSTRAINT [PK_ImageAssetTag] PRIMARY KEY ([ImageAssetId], [TagId])
);

CREATE TABLE [{{schema}}].[ImageAssetGroup]
(
    [ImageAssetGroupId] int IDENTITY(1, 1) NOT NULL,
    [GroupName] nvarchar(64) NOT NULL,
    [ParentImageAssetGroupId] int NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    CONSTRAINT [PK_ImageAssetGroup] PRIMARY KEY ([ImageAssetGroupId])
);

CREATE TABLE [{{schema}}].[ImageAssetGroupItem]
(
    [ImageAssetId] int NOT NULL,
    [ImageAssetGroupId] int NOT NULL,
    [CreatorId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [Ordering] int NOT NULL,
    CONSTRAINT [PK_ImageAssetGroupItem] PRIMARY KEY ([ImageAssetId], [ImageAssetGroupId])
);

CREATE TABLE [{{schema}}].[PageDirectory]
(
    [PageDirectoryId] int IDENTITY(1, 1) NOT NULL,
    [ParentPageDirectoryId] int NULL,
    [Name] nvarchar(200) NULL,
    [UrlPath] nvarchar(200) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    [AccessRuleViolationActionId] int NULL,
    [UserAreaCodeForSignInRedirect] char(3) NULL,
    CONSTRAINT [PK_PageDirectory] PRIMARY KEY ([PageDirectoryId])
);

CREATE TABLE [{{schema}}].[PageDirectoryClosure]
(
    [AncestorPageDirectoryId] int NOT NULL,
    [DescendantPageDirectoryId] int NOT NULL,
    [Distance] int NOT NULL,
    CONSTRAINT [PK_PageDirectoryClosure] PRIMARY KEY ([AncestorPageDirectoryId], [DescendantPageDirectoryId])
);

CREATE TABLE [{{schema}}].[PageDirectoryPath]
(
    [PageDirectoryId] int NOT NULL,
    [FullUrlPath] nvarchar(max) NOT NULL,
    [Depth] int NOT NULL,
    CONSTRAINT [PK_PageDirectoryPath] PRIMARY KEY ([PageDirectoryId])
);

CREATE TABLE [{{schema}}].[PageDirectoryLocale]
(
    [PageDirectoryLocaleId] int IDENTITY(1, 1) NOT NULL,
    [PageDirectoryId] int NOT NULL,
    [LocaleId] int NOT NULL,
    [UrlPath] nvarchar(64) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    CONSTRAINT [PK_PageDirectoryLocale] PRIMARY KEY ([PageDirectoryLocaleId])
);

CREATE TABLE [{{schema}}].[PageDirectoryAccessRule]
(
    [PageDirectoryAccessRuleId] int IDENTITY(1, 1) NOT NULL,
    [PageDirectoryId] int NOT NULL,
    [UserAreaCode] char(3) NOT NULL,
    [RoleId] int NULL,
    [CreatorId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PageDirectoryAccessRule] PRIMARY KEY ([PageDirectoryAccessRuleId])
);

CREATE TABLE [{{schema}}].[PageTemplate]
(
    [PageTemplateId] int IDENTITY(1, 1) NOT NULL,
    [FileName] nvarchar(100) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [FullPath] nvarchar(400) NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [CustomEntityDefinitionCode] char(6) NULL,
    [CustomEntityModelType] nvarchar(400) NULL,
    [PageTypeId] int NOT NULL,
    [IsArchived] bit NOT NULL,
    [UpdateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PageTemplate] PRIMARY KEY ([PageTemplateId])
);

CREATE TABLE [{{schema}}].[PageTemplateRegion]
(
    [PageTemplateRegionId] int IDENTITY(1, 1) NOT NULL,
    [PageTemplateId] int NOT NULL,
    [Name] nvarchar(50) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [IsCustomEntityRegion] bit NOT NULL,
    [UpdateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PageTemplateRegion] PRIMARY KEY ([PageTemplateRegionId])
);

CREATE TABLE [{{schema}}].[PageBlockType]
(
    [PageBlockTypeId] int IDENTITY(1, 1) NOT NULL,
    [Name] nvarchar(50) NOT NULL,
    [Description] nvarchar(max) NULL,
    [FileName] nvarchar(50) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [IsArchived] bit NOT NULL,
    [UpdateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PageBlockType] PRIMARY KEY ([PageBlockTypeId])
);

CREATE TABLE [{{schema}}].[PageBlockTypeTemplate]
(
    [PageBlockTypeTemplateId] int IDENTITY(1, 1) NOT NULL,
    [PageBlockTypeId] int NOT NULL,
    [Name] nvarchar(50) NOT NULL,
    [FileName] nvarchar(50) NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [Description] nvarchar(max) NULL,
    CONSTRAINT [PK_PageBlockTypeTemplate] PRIMARY KEY ([PageBlockTypeTemplateId])
);

CREATE TABLE [{{schema}}].[Page]
(
    [PageId] int IDENTITY(1, 1) NOT NULL,
    [PageDirectoryId] int NOT NULL,
    [LocaleId] int NULL,
    [UrlPath] nvarchar(200) NOT NULL,
    [PageTypeId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    [CustomEntityDefinitionCode] char(6) NULL,
    [PublishStatusCode] char(1) NOT NULL,
    [PublishDate] datetime2(7) NULL,
    [AccessRuleViolationActionId] int NOT NULL,
    [UserAreaCodeForSignInRedirect] char(3) NULL,
    [LastPublishDate] datetime2(7) NULL,
    CONSTRAINT [PK_Page] PRIMARY KEY ([PageId])
);

CREATE TABLE [{{schema}}].[PageTag]
(
    [PageId] int NOT NULL,
    [TagId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    CONSTRAINT [PK_PageTag] PRIMARY KEY ([PageId], [TagId])
);

CREATE TABLE [{{schema}}].[PageGroup]
(
    [PageGroupId] int IDENTITY(1, 1) NOT NULL,
    [GroupName] nvarchar(64) NOT NULL,
    [ParentPageGroupId] int NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    CONSTRAINT [PK_PageGroup] PRIMARY KEY ([PageGroupId])
);

CREATE TABLE [{{schema}}].[PageGroupItem]
(
    [PageId] int NOT NULL,
    [PageGroupId] int NOT NULL,
    [Ordering] int NOT NULL,
    [CreatorId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PageGroupItem] PRIMARY KEY ([PageId], [PageGroupId])
);

CREATE TABLE [{{schema}}].[PageAccessRule]
(
    [PageAccessRuleId] int IDENTITY(1, 1) NOT NULL,
    [PageId] int NOT NULL,
    [UserAreaCode] char(3) NOT NULL,
    [RoleId] int NULL,
    [CreatorId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    CONSTRAINT [PK_PageAccessRule] PRIMARY KEY ([PageAccessRuleId])
);

CREATE TABLE [{{schema}}].[PageVersion]
(
    [PageVersionId] int IDENTITY(1, 1) NOT NULL,
    [PageId] int NOT NULL,
    [PageTemplateId] int NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [MetaDescription] nvarchar(300) NULL,
    [WorkFlowStatusId] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    [ExcludeFromSitemap] bit NOT NULL DEFAULT ((0)),
    [OpenGraphTitle] nvarchar(300) NULL,
    [OpenGraphDescription] nvarchar(max) NULL,
    [OpenGraphImageId] int NULL,
    [DisplayVersion] int NOT NULL,
    CONSTRAINT [PK_PageVersion] PRIMARY KEY ([PageVersionId])
);

CREATE TABLE [{{schema}}].[PageVersionBlock]
(
    [PageVersionBlockId] int IDENTITY(1, 1) NOT NULL,
    [PageVersionId] int NOT NULL,
    [PageTemplateRegionId] int NOT NULL,
    [PageBlockTypeId] int NOT NULL,
    [SerializedData] nvarchar(max) NOT NULL,
    [Ordering] int NOT NULL,
    [CreateDate] datetime2(7) NOT NULL,
    [CreatorId] int NOT NULL,
    [UpdateDate] datetime2(7) NOT NULL,
    [PageBlockTypeTemplateId] int NULL,
    CONSTRAINT [PK_PageVersionBlock] PRIMARY KEY ([PageVersionBlockId])
);

CREATE TABLE [{{schema}}].[PagePublishStatusQuery]
(
    [PageId] int NOT NULL,
    [PublishStatusQueryId] smallint NOT NULL,
    [PageVersionId] int NOT NULL,
    CONSTRAINT [PK_PagePublishStatusQuery] PRIMARY KEY ([PageId], [PublishStatusQueryId])
);

ALTER TABLE [{{schema}}].[User] ADD CONSTRAINT [FK_User_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[User] ADD CONSTRAINT [FK_User_Role] FOREIGN KEY ([RoleId]) REFERENCES [{{schema}}].[Role] ([RoleId]);
ALTER TABLE [{{schema}}].[User] ADD CONSTRAINT [FK_User_UserArea] FOREIGN KEY ([UserAreaCode]) REFERENCES [{{schema}}].[UserArea] ([UserAreaCode]);
ALTER TABLE [{{schema}}].[Role] ADD CONSTRAINT [FK_Role_UserArea] FOREIGN KEY ([UserAreaCode]) REFERENCES [{{schema}}].[UserArea] ([UserAreaCode]);
ALTER TABLE [{{schema}}].[RolePermission] ADD CONSTRAINT [FK_RolePermission_Permission] FOREIGN KEY ([PermissionId]) REFERENCES [{{schema}}].[Permission] ([PermissionId]);
ALTER TABLE [{{schema}}].[RolePermission] ADD CONSTRAINT [FK_RolePermission_Role] FOREIGN KEY ([RoleId]) REFERENCES [{{schema}}].[Role] ([RoleId]);
ALTER TABLE [{{schema}}].[Locale] ADD CONSTRAINT [FK_Locale_ParentLocale] FOREIGN KEY ([ParentLocaleId]) REFERENCES [{{schema}}].[Locale] ([LocaleId]);
ALTER TABLE [{{schema}}].[ImageAsset] ADD CONSTRAINT [FK_ImageAsset_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[ImageAsset] ADD CONSTRAINT [FK_ImageAsset_UpdaterUser] FOREIGN KEY ([UpdaterId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[ImageAssetTag] ADD CONSTRAINT [FK_ImageAssetTag_ImageAsset] FOREIGN KEY ([ImageAssetId]) REFERENCES [{{schema}}].[ImageAsset] ([ImageAssetId]);
ALTER TABLE [{{schema}}].[ImageAssetTag] ADD CONSTRAINT [FK_ImageAssetTag_Tag] FOREIGN KEY ([TagId]) REFERENCES [{{schema}}].[Tag] ([TagId]);
ALTER TABLE [{{schema}}].[ImageAssetTag] ADD CONSTRAINT [FK_ImageAssetTag_User] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[ImageAssetGroup] ADD CONSTRAINT [FK_ImageAssetGroup_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[ImageAssetGroup] ADD CONSTRAINT [FK_ImageAssetGroup_ParentImageAssetGroup] FOREIGN KEY ([ParentImageAssetGroupId]) REFERENCES [{{schema}}].[ImageAssetGroup] ([ImageAssetGroupId]);
ALTER TABLE [{{schema}}].[ImageAssetGroupItem] ADD CONSTRAINT [FK_ImageAssetGroupItem_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[ImageAssetGroupItem] ADD CONSTRAINT [FK_ImageAssetGroupItem_ImageAsset] FOREIGN KEY ([ImageAssetId]) REFERENCES [{{schema}}].[ImageAsset] ([ImageAssetId]);
ALTER TABLE [{{schema}}].[ImageAssetGroupItem] ADD CONSTRAINT [FK_ImageAssetGroupItem_ImageAssetGroup] FOREIGN KEY ([ImageAssetGroupId]) REFERENCES [{{schema}}].[ImageAssetGroup] ([ImageAssetGroupId]);
ALTER TABLE [{{schema}}].[PageDirectory] ADD CONSTRAINT [FK_PageDirectory_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageDirectory] ADD CONSTRAINT [FK_PageDirectory_ParentPageDirectory] FOREIGN KEY ([ParentPageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[PageDirectory] ADD CONSTRAINT [FK_PageDirectory_UserAreaForSignInRedirect] FOREIGN KEY ([UserAreaCodeForSignInRedirect]) REFERENCES [{{schema}}].[UserArea] ([UserAreaCode]);
ALTER TABLE [{{schema}}].[PageDirectoryClosure] ADD CONSTRAINT [FK_PageDirectoryClosure_AncestorPageDirectory] FOREIGN KEY ([AncestorPageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[PageDirectoryClosure] ADD CONSTRAINT [FK_PageDirectoryClosure_DescendantPageDirectory] FOREIGN KEY ([DescendantPageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[PageDirectoryPath] ADD CONSTRAINT [FK_PageDirectoryPath_PageDirectory] FOREIGN KEY ([PageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[PageDirectoryLocale] ADD CONSTRAINT [FK_PageDirectoryLocale_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageDirectoryLocale] ADD CONSTRAINT [FK_PageDirectoryLocale_Locale] FOREIGN KEY ([LocaleId]) REFERENCES [{{schema}}].[Locale] ([LocaleId]);
ALTER TABLE [{{schema}}].[PageDirectoryLocale] ADD CONSTRAINT [FK_PageDirectoryLocale_PageDirectory] FOREIGN KEY ([PageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[PageDirectoryAccessRule] ADD CONSTRAINT [FK_PageDirectoryAccessRule_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageDirectoryAccessRule] ADD CONSTRAINT [FK_PageDirectoryAccessRule_Page] FOREIGN KEY ([PageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[PageDirectoryAccessRule] ADD CONSTRAINT [FK_PageDirectoryAccessRule_Role] FOREIGN KEY ([RoleId]) REFERENCES [{{schema}}].[Role] ([RoleId]);
ALTER TABLE [{{schema}}].[PageDirectoryAccessRule] ADD CONSTRAINT [FK_PageDirectoryAccessRule_UserArea] FOREIGN KEY ([UserAreaCode]) REFERENCES [{{schema}}].[UserArea] ([UserAreaCode]);
ALTER TABLE [{{schema}}].[PageTemplateRegion] ADD CONSTRAINT [FK_PageTemplateRegion_PageTemplate] FOREIGN KEY ([PageTemplateId]) REFERENCES [{{schema}}].[PageTemplate] ([PageTemplateId]);
ALTER TABLE [{{schema}}].[PageBlockTypeTemplate] ADD CONSTRAINT [FK_PageBlockTypeTemplate_PageBlockType] FOREIGN KEY ([PageBlockTypeId]) REFERENCES [{{schema}}].[PageBlockType] ([PageBlockTypeId]);
ALTER TABLE [{{schema}}].[Page] ADD CONSTRAINT [FK_Page_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[Page] ADD CONSTRAINT [FK_Page_Locale] FOREIGN KEY ([LocaleId]) REFERENCES [{{schema}}].[Locale] ([LocaleId]);
ALTER TABLE [{{schema}}].[Page] ADD CONSTRAINT [FK_Page_PageDirectory] FOREIGN KEY ([PageDirectoryId]) REFERENCES [{{schema}}].[PageDirectory] ([PageDirectoryId]);
ALTER TABLE [{{schema}}].[Page] ADD CONSTRAINT [FK_Page_UserAreasForSignInRedirect] FOREIGN KEY ([UserAreaCodeForSignInRedirect]) REFERENCES [{{schema}}].[UserArea] ([UserAreaCode]);
ALTER TABLE [{{schema}}].[PageTag] ADD CONSTRAINT [FK_PageTag_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageTag] ADD CONSTRAINT [FK_PageTag_Page] FOREIGN KEY ([PageId]) REFERENCES [{{schema}}].[Page] ([PageId]);
ALTER TABLE [{{schema}}].[PageTag] ADD CONSTRAINT [FK_PageTag_Tag] FOREIGN KEY ([TagId]) REFERENCES [{{schema}}].[Tag] ([TagId]);
ALTER TABLE [{{schema}}].[PageGroup] ADD CONSTRAINT [FK_PageGroup_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageGroup] ADD CONSTRAINT [FK_PageGroup_ParentPageGroup] FOREIGN KEY ([ParentPageGroupId]) REFERENCES [{{schema}}].[PageGroup] ([PageGroupId]);
ALTER TABLE [{{schema}}].[PageGroupItem] ADD CONSTRAINT [FK_PageGroupItem_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageGroupItem] ADD CONSTRAINT [FK_PageGroupItem_Page] FOREIGN KEY ([PageId]) REFERENCES [{{schema}}].[Page] ([PageId]);
ALTER TABLE [{{schema}}].[PageGroupItem] ADD CONSTRAINT [FK_PageGroupItem_PageGroup] FOREIGN KEY ([PageGroupId]) REFERENCES [{{schema}}].[PageGroup] ([PageGroupId]);
ALTER TABLE [{{schema}}].[PageAccessRule] ADD CONSTRAINT [FK_PageAccessRule_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageAccessRule] ADD CONSTRAINT [FK_PageAccessRule_Page] FOREIGN KEY ([PageId]) REFERENCES [{{schema}}].[Page] ([PageId]);
ALTER TABLE [{{schema}}].[PageAccessRule] ADD CONSTRAINT [FK_PageAccessRule_Role] FOREIGN KEY ([RoleId]) REFERENCES [{{schema}}].[Role] ([RoleId]);
ALTER TABLE [{{schema}}].[PageAccessRule] ADD CONSTRAINT [FK_PageAccessRule_UserArea] FOREIGN KEY ([UserAreaCode]) REFERENCES [{{schema}}].[UserArea] ([UserAreaCode]);
ALTER TABLE [{{schema}}].[PageVersion] ADD CONSTRAINT [FK_PageVersion_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageVersion] ADD CONSTRAINT [FK_PageVersion_OpenGraphImageAsset] FOREIGN KEY ([OpenGraphImageId]) REFERENCES [{{schema}}].[ImageAsset] ([ImageAssetId]);
ALTER TABLE [{{schema}}].[PageVersion] ADD CONSTRAINT [FK_PageVersion_Page] FOREIGN KEY ([PageId]) REFERENCES [{{schema}}].[Page] ([PageId]);
ALTER TABLE [{{schema}}].[PageVersion] ADD CONSTRAINT [FK_PageVersion_PageTemplate] FOREIGN KEY ([PageTemplateId]) REFERENCES [{{schema}}].[PageTemplate] ([PageTemplateId]);
ALTER TABLE [{{schema}}].[PageVersionBlock] ADD CONSTRAINT [FK_PageVersionBlock_CreatorUser] FOREIGN KEY ([CreatorId]) REFERENCES [{{schema}}].[User] ([UserId]);
ALTER TABLE [{{schema}}].[PageVersionBlock] ADD CONSTRAINT [FK_PageVersionBlock_PageBlockType] FOREIGN KEY ([PageBlockTypeId]) REFERENCES [{{schema}}].[PageBlockType] ([PageBlockTypeId]);
ALTER TABLE [{{schema}}].[PageVersionBlock] ADD CONSTRAINT [FK_PageVersionBlock_PageBlockTypeTemplate] FOREIGN KEY ([PageBlockTypeTemplateId]) REFERENCES [{{schema}}].[PageBlockTypeTemplate] ([PageBlockTypeTemplateId]);
ALTER TABLE [{{schema}}].[PageVersionBlock] ADD CONSTRAINT [FK_PageVersionBlock_PageTemplateRegion] FOREIGN KEY ([PageTemplateRegionId]) REFERENCES [{{schema}}].[PageTemplateRegion] ([PageTemplateRegionId]);
ALTER TABLE [{{schema}}].[PageVersionBlock] ADD CONSTRAINT [FK_PageVersionBlock_PageVersion] FOREIGN KEY ([PageVersionId]) REFERENCES [{{schema}}].[PageVersion] ([PageVersionId]);
ALTER TABLE [{{schema}}].[PagePublishStatusQuery] ADD CONSTRAINT [FK_PagePublishStatusQuery_Page] FOREIGN KEY ([PageId]) REFERENCES [{{schema}}].[Page] ([PageId]);
ALTER TABLE [{{schema}}].[PagePublishStatusQuery] ADD CONSTRAINT [FK_PagePublishStatusQuery_PageVersion] FOREIGN KEY ([PageVersionId]) REFERENCES [{{schema}}].[PageVersion] ([PageVersionId]);
