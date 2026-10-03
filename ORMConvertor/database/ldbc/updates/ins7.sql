-- INS 7, add a comment (decision 117). Parameters as in ins1.sql. The comment replies to a post
-- or to a comment, the other key being -1; the root post of its thread, that post's forum and
-- language come from the message it replies to, as load.sql derives them for the bulk part.
-- A parent that is not there is an error: the comment would be written into no thread.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

INSERT INTO [{{schema}}].[Message] ([Id], [CreationDate], [LocationIp], [BrowserUsed], [Content], [ImageFile], [Length], [CreatorPersonId], [LocationCountryId], [ParentMessageId], [RootPostId], [ContainerForumId], [RootPostLanguage])
SELECT @commentId,
       DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))),
       @locationIp, @browserUsed, NULLIF(@content, N''), NULL, @length, @authorPersonId, @countryId,
       [parent].[Id], [parent].[RootPostId], [parent].[ContainerForumId], [parent].[RootPostLanguage]
FROM [{{schema}}].[Message] AS [parent]
WHERE [parent].[Id] = CASE WHEN @replyToPostId <> -1 THEN @replyToPostId ELSE @replyToCommentId END;

IF @@ROWCOUNT <> 1
    THROW 50117, 'INS 7 replies to a message that is not in the database.', 1;

INSERT INTO [{{schema}}].[Message_hasTag_Tag] ([MessageId], [TagId])
SELECT DISTINCT @commentId, CAST([value] AS BIGINT) FROM OPENJSON(@tagIds);

COMMIT TRANSACTION;
