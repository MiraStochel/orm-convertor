-- Loads one LDBC SNB Interactive v1 data set (decision 110) into the tables of schema.sql.
-- Run by sqlcmd between schema.sql and constraints.sql, with one variable, DataPath: the
-- directory the archive social_network-sf<N>-CsvMergeForeign-StringDateFormatter.tar.zst
-- unpacks to - the one holding static/ and dynamic/ - as a path the database server itself
-- can read, since BULK INSERT runs there.
--
-- The files are the bulk-load part of the data set, 90 % of the network; the update streams
-- are not loaded. They are pipe-separated UTF-8 with a header row, and every date-time is
-- GMT written as 2010-02-14T15:32:10.447+0000, which is read as a DATETIME2 in UTC.
--
-- Every file goes into a staging table of strings first and is converted from there, so a
-- malformed value fails with the name of the table rather than in the middle of a bulk
-- insert. The staging columns are VARCHAR in a UTF-8 collation and the files are read with
-- CODEPAGE = 'RAW': the bytes arrive as they are and the collation says they are UTF-8. That
-- is the one form that works on both platforms - SQL Server on Linux accepts no CODEPAGE
-- but RAW, so '65001' would stop the load in the container - and the conversion to
-- NVARCHAR happens in the INSERT that follows. The staging schema is dropped at the end.
-- The {{schema}} placeholder works as in schema.sql.

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID('ldbc_load') IS NULL EXEC (N'CREATE SCHEMA [ldbc_load]');
GO

CREATE TABLE [ldbc_load].[Place] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Name] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Url] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Type] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [IsPartOf] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Organisation] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Type] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Name] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Url] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Place] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[TagClass] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Name] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Url] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [IsSubclassOf] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Tag] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Name] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Url] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [HasType] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Person] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [FirstName] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [LastName] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [Gender] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8, [Birthday] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8, [LocationIp] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [BrowserUsed] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [Place] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[PersonEmail] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Email] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[PersonLanguage] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Language] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Forum] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Title] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8, [Moderator] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Post] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [ImageFile] VARCHAR(1024) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8, [LocationIp] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [BrowserUsed] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [Language] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [Content] VARCHAR(8000) COLLATE Latin1_General_100_BIN2_UTF8, [Length] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Creator] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [ForumId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Place] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Comment] ([Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8, [LocationIp] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [BrowserUsed] VARCHAR(80) COLLATE Latin1_General_100_BIN2_UTF8, [Content] VARCHAR(8000) COLLATE Latin1_General_100_BIN2_UTF8, [Length] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Creator] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Place] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [ReplyOfPost] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [ReplyOfComment] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Knows] ([Person1Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [Person2Id] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[LikesPost] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [MessageId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[LikesComment] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [MessageId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [CreationDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[PostTag] ([MessageId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [TagId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[CommentTag] ([MessageId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [TagId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[ForumMember] ([ForumId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [JoinDate] VARCHAR(40) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[ForumTag] ([ForumId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [TagId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[Interest] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [TagId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[StudyAt] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [OrganisationId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [ClassYear] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
CREATE TABLE [ldbc_load].[WorkAt] ([PersonId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [OrganisationId] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8, [WorkFrom] VARCHAR(20) COLLATE Latin1_General_100_BIN2_UTF8);
GO

BULK INSERT [ldbc_load].[Place] FROM '$(DataPath)/static/place_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Organisation] FROM '$(DataPath)/static/organisation_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[TagClass] FROM '$(DataPath)/static/tagclass_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Tag] FROM '$(DataPath)/static/tag_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Person] FROM '$(DataPath)/dynamic/person_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[PersonEmail] FROM '$(DataPath)/dynamic/person_email_emailaddress_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[PersonLanguage] FROM '$(DataPath)/dynamic/person_speaks_language_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Forum] FROM '$(DataPath)/dynamic/forum_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Post] FROM '$(DataPath)/dynamic/post_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Comment] FROM '$(DataPath)/dynamic/comment_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Knows] FROM '$(DataPath)/dynamic/person_knows_person_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[LikesPost] FROM '$(DataPath)/dynamic/person_likes_post_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[LikesComment] FROM '$(DataPath)/dynamic/person_likes_comment_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[PostTag] FROM '$(DataPath)/dynamic/post_hasTag_tag_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[CommentTag] FROM '$(DataPath)/dynamic/comment_hasTag_tag_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[ForumMember] FROM '$(DataPath)/dynamic/forum_hasMember_person_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[ForumTag] FROM '$(DataPath)/dynamic/forum_hasTag_tag_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[Interest] FROM '$(DataPath)/dynamic/person_hasInterest_tag_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[StudyAt] FROM '$(DataPath)/dynamic/person_studyAt_organisation_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
BULK INSERT [ldbc_load].[WorkAt] FROM '$(DataPath)/dynamic/person_workAt_organisation_0_0.csv' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', FIRSTROW = 2, CODEPAGE = 'RAW', TABLOCK);
GO

-- Static part.

INSERT INTO [{{schema}}].[Place] ([Id], [Name], [Url], [Type], [PartOfPlaceId])
SELECT CAST([Id] AS BIGINT), [Name], [Url], [Type], CAST(NULLIF([IsPartOf], '') AS BIGINT)
FROM [ldbc_load].[Place];

INSERT INTO [{{schema}}].[Organisation] ([Id], [Type], [Name], [Url], [LocationPlaceId])
SELECT CAST([Id] AS BIGINT), [Type], [Name], [Url], CAST([Place] AS BIGINT)
FROM [ldbc_load].[Organisation];

INSERT INTO [{{schema}}].[TagClass] ([Id], [Name], [Url], [SubclassOfTagClassId])
SELECT CAST([Id] AS BIGINT), [Name], [Url], CAST(NULLIF([IsSubclassOf], '') AS BIGINT)
FROM [ldbc_load].[TagClass];

INSERT INTO [{{schema}}].[Tag] ([Id], [Name], [Url], [TypeTagClassId])
SELECT CAST([Id] AS BIGINT), [Name], [Url], CAST([HasType] AS BIGINT)
FROM [ldbc_load].[Tag];
GO

-- People and forums.

INSERT INTO [{{schema}}].[Person] ([Id], [FirstName], [LastName], [Gender], [Birthday], [CreationDate], [LocationIp], [BrowserUsed], [LocationCityId])
SELECT CAST([Id] AS BIGINT), [FirstName], [LastName], [Gender],
       CONVERT(DATE, [Birthday], 23), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126),
       [LocationIp], [BrowserUsed], CAST([Place] AS BIGINT)
FROM [ldbc_load].[Person];

INSERT INTO [{{schema}}].[Person_email_EmailAddress] ([PersonId], [Email])
SELECT DISTINCT CAST([PersonId] AS BIGINT), [Email] FROM [ldbc_load].[PersonEmail];

INSERT INTO [{{schema}}].[Person_speaks_Language] ([PersonId], [Language])
SELECT DISTINCT CAST([PersonId] AS BIGINT), [Language] FROM [ldbc_load].[PersonLanguage];

INSERT INTO [{{schema}}].[Forum] ([Id], [Title], [CreationDate], [ModeratorPersonId])
SELECT CAST([Id] AS BIGINT), [Title], CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126), CAST(NULLIF([Moderator], '') AS BIGINT)
FROM [ldbc_load].[Forum];
GO

-- Messages. Posts and comments go into one staging table keyed by id; the root post of every
-- comment is then found by walking up one level per pass until no comment is left without
-- one, which takes as many passes as the deepest thread is deep.

CREATE TABLE [ldbc_load].[Message] (
    [Id] BIGINT NOT NULL PRIMARY KEY,
    [CreationDate] DATETIME2(3) NOT NULL,
    [LocationIp] VARCHAR(40) NOT NULL,
    [BrowserUsed] VARCHAR(40) NOT NULL,
    [Content] NVARCHAR(2000) NULL,
    [ImageFile] VARCHAR(256) NULL,
    [Length] INT NOT NULL,
    [CreatorPersonId] BIGINT NOT NULL,
    [LocationCountryId] BIGINT NOT NULL,
    [ParentMessageId] BIGINT NULL,
    [RootPostId] BIGINT NULL,
    [ContainerForumId] BIGINT NULL,
    [RootPostLanguage] VARCHAR(40) NULL
);

INSERT INTO [ldbc_load].[Message] WITH (TABLOCK)
    ([Id], [CreationDate], [LocationIp], [BrowserUsed], [Content], [ImageFile], [Length], [CreatorPersonId], [LocationCountryId], [ParentMessageId], [RootPostId], [ContainerForumId], [RootPostLanguage])
SELECT CAST([Id] AS BIGINT), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126), [LocationIp], [BrowserUsed],
       NULLIF([Content], N''), NULLIF([ImageFile], ''), CAST([Length] AS INT), CAST([Creator] AS BIGINT), CAST([Place] AS BIGINT),
       NULL, CAST([Id] AS BIGINT), CAST([ForumId] AS BIGINT), NULLIF([Language], '')
FROM [ldbc_load].[Post]
UNION ALL
SELECT CAST([Id] AS BIGINT), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126), [LocationIp], [BrowserUsed],
       NULLIF([Content], N''), NULL, CAST([Length] AS INT), CAST([Creator] AS BIGINT), CAST([Place] AS BIGINT),
       CAST(COALESCE(NULLIF([ReplyOfPost], ''), NULLIF([ReplyOfComment], '')) AS BIGINT), NULL, NULL, NULL
FROM [ldbc_load].[Comment];
GO

DECLARE @walked INT = 1;
WHILE @walked > 0
BEGIN
    UPDATE [child]
    SET [RootPostId] = [parent].[RootPostId],
        [ContainerForumId] = [parent].[ContainerForumId],
        [RootPostLanguage] = [parent].[RootPostLanguage]
    FROM [ldbc_load].[Message] AS [child]
    JOIN [ldbc_load].[Message] AS [parent] ON [parent].[Id] = [child].[ParentMessageId]
    WHERE [child].[RootPostId] IS NULL AND [parent].[RootPostId] IS NOT NULL;

    SET @walked = @@ROWCOUNT;
END;

IF EXISTS (SELECT 1 FROM [ldbc_load].[Message] WHERE [RootPostId] IS NULL)
    THROW 50110, 'Some comments reply to a message that is not in the data set; their thread has no root post.', 1;
GO

INSERT INTO [{{schema}}].[Message] WITH (TABLOCK)
    ([Id], [CreationDate], [LocationIp], [BrowserUsed], [Content], [ImageFile], [Length], [CreatorPersonId], [LocationCountryId], [ParentMessageId], [RootPostId], [ContainerForumId], [RootPostLanguage])
SELECT [Id], [CreationDate], [LocationIp], [BrowserUsed], [Content], [ImageFile], [Length], [CreatorPersonId], [LocationCountryId], [ParentMessageId], [RootPostId], [ContainerForumId], [RootPostLanguage]
FROM [ldbc_load].[Message];
GO

-- Edges. Friendship is undirected and serialized once, so both directions are stored; UNION
-- rather than UNION ALL, so that a pair the file happens to state both ways stays one row.

INSERT INTO [{{schema}}].[Person_knows_Person] WITH (TABLOCK) ([Person1Id], [Person2Id], [CreationDate])
SELECT CAST([Person1Id] AS BIGINT), CAST([Person2Id] AS BIGINT), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126) FROM [ldbc_load].[Knows]
UNION
SELECT CAST([Person2Id] AS BIGINT), CAST([Person1Id] AS BIGINT), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126) FROM [ldbc_load].[Knows];

INSERT INTO [{{schema}}].[Person_likes_Message] WITH (TABLOCK) ([PersonId], [MessageId], [CreationDate])
SELECT CAST([PersonId] AS BIGINT), CAST([MessageId] AS BIGINT), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126) FROM [ldbc_load].[LikesPost]
UNION ALL
SELECT CAST([PersonId] AS BIGINT), CAST([MessageId] AS BIGINT), CONVERT(DATETIME2(3), LEFT([CreationDate], 23), 126) FROM [ldbc_load].[LikesComment];

INSERT INTO [{{schema}}].[Message_hasTag_Tag] WITH (TABLOCK) ([MessageId], [TagId])
SELECT CAST([MessageId] AS BIGINT), CAST([TagId] AS BIGINT) FROM [ldbc_load].[PostTag]
UNION ALL
SELECT CAST([MessageId] AS BIGINT), CAST([TagId] AS BIGINT) FROM [ldbc_load].[CommentTag];

INSERT INTO [{{schema}}].[Forum_hasMember_Person] WITH (TABLOCK) ([ForumId], [PersonId], [JoinDate])
SELECT CAST([ForumId] AS BIGINT), CAST([PersonId] AS BIGINT), CONVERT(DATETIME2(3), LEFT([JoinDate], 23), 126) FROM [ldbc_load].[ForumMember];

INSERT INTO [{{schema}}].[Forum_hasTag_Tag] WITH (TABLOCK) ([ForumId], [TagId])
SELECT CAST([ForumId] AS BIGINT), CAST([TagId] AS BIGINT) FROM [ldbc_load].[ForumTag];

INSERT INTO [{{schema}}].[Person_hasInterest_Tag] WITH (TABLOCK) ([PersonId], [TagId])
SELECT CAST([PersonId] AS BIGINT), CAST([TagId] AS BIGINT) FROM [ldbc_load].[Interest];

INSERT INTO [{{schema}}].[Person_studyAt_University] WITH (TABLOCK) ([PersonId], [UniversityId], [ClassYear])
SELECT CAST([PersonId] AS BIGINT), CAST([OrganisationId] AS BIGINT), CAST([ClassYear] AS INT) FROM [ldbc_load].[StudyAt];

INSERT INTO [{{schema}}].[Person_workAt_Company] WITH (TABLOCK) ([PersonId], [CompanyId], [WorkFrom])
SELECT CAST([PersonId] AS BIGINT), CAST([OrganisationId] AS BIGINT), CAST([WorkFrom] AS INT) FROM [ldbc_load].[WorkAt];
GO

DECLARE @drop NVARCHAR(MAX) = N'';
SELECT @drop = @drop + N'DROP TABLE [ldbc_load].' + QUOTENAME([name]) + N';'
FROM sys.tables WHERE [schema_id] = SCHEMA_ID('ldbc_load');
EXEC sp_executesql @drop;
DROP SCHEMA [ldbc_load];
GO
