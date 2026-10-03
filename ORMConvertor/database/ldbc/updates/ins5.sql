-- INS 5, add a forum membership (decision 117). Parameters as in ins1.sql.

SET NOCOUNT ON;
SET XACT_ABORT ON;

INSERT INTO [{{schema}}].[Forum_hasMember_Person] ([ForumId], [PersonId], [JoinDate])
VALUES (@forumId, @personId,
        DATEADD(MILLISECOND, @joinDate % 1000, DATEADD(SECOND, @joinDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))));
