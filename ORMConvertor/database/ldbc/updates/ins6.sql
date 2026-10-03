-- INS 6, add a post (decision 117). Parameters as in ins1.sql. A post is the root of its own
-- thread, so RootPostId is its own key, and the forum and language of the thread are its own,
-- as load.sql writes them; an empty image file, content or language is NULL there as here.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

INSERT INTO [{{schema}}].[Message] ([Id], [CreationDate], [LocationIp], [BrowserUsed], [Content], [ImageFile], [Length], [CreatorPersonId], [LocationCountryId], [ParentMessageId], [RootPostId], [ContainerForumId], [RootPostLanguage])
VALUES (@postId,
        DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))),
        @locationIp, @browserUsed, NULLIF(@content, N''), NULLIF(@imageFile, N''), @length, @authorPersonId, @countryId,
        NULL, @postId, @forumId, NULLIF(@language, N''));

INSERT INTO [{{schema}}].[Message_hasTag_Tag] ([MessageId], [TagId])
SELECT DISTINCT @postId, CAST([value] AS BIGINT) FROM OPENJSON(@tagIds);

COMMIT TRANSACTION;
