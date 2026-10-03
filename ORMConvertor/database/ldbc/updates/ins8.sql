-- INS 8, add a friendship (decision 117). Parameters as in ins1.sql. Person_knows_Person holds
-- every friendship in both directions (schema.sql), so both rows are written.

SET NOCOUNT ON;
SET XACT_ABORT ON;

INSERT INTO [{{schema}}].[Person_knows_Person] ([Person1Id], [Person2Id], [CreationDate])
VALUES (@person1Id, @person2Id, DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3))))),
       (@person2Id, @person1Id, DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))));
