-- INS 3, add a like to a comment (decision 117). Parameters as in ins1.sql; a comment is a
-- row of Message (schema.sql).

SET NOCOUNT ON;
SET XACT_ABORT ON;

INSERT INTO [{{schema}}].[Person_likes_Message] ([PersonId], [MessageId], [CreationDate])
VALUES (@personId, @commentId,
        DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))));
