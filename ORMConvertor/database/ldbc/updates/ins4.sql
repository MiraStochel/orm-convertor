-- INS 4, add a forum (decision 117). Parameters as in ins1.sql.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

INSERT INTO [{{schema}}].[Forum] ([Id], [Title], [CreationDate], [ModeratorPersonId])
VALUES (@forumId, @forumTitle,
        DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))),
        @moderatorPersonId);

INSERT INTO [{{schema}}].[Forum_hasTag_Tag] ([ForumId], [TagId])
SELECT DISTINCT @forumId, CAST([value] AS BIGINT) FROM OPENJSON(@tagIds);

COMMIT TRANSACTION;
