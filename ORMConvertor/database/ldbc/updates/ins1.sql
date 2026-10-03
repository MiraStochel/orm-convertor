-- INS 1, add a person (decision 117; specification 2.2.4, appendix A). One batch, run with
-- the fields of the operation as parameters of the same names: a number as BIGINT, a string
-- as NVARCHAR, a list or an object as its JSON text. Dates and moments are milliseconds since
-- the epoch in UTC, as the driver writes them, and are read the way load.sql reads the files.
-- The {{schema}} placeholder works as in schema.sql. updates/undo.sql deletes what this writes.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

INSERT INTO [{{schema}}].[Person] ([Id], [FirstName], [LastName], [Gender], [Birthday], [CreationDate], [LocationIp], [BrowserUsed], [LocationCityId])
VALUES (@personId, @personFirstName, @personLastName, @gender,
        CAST(DATEADD(SECOND, @birthday / 1000, CAST('1970-01-01' AS DATETIME2(3))) AS DATE),
        DATEADD(MILLISECOND, @creationDate % 1000, DATEADD(SECOND, @creationDate / 1000, CAST('1970-01-01' AS DATETIME2(3)))),
        @locationIp, @browserUsed, @cityId);

INSERT INTO [{{schema}}].[Person_speaks_Language] ([PersonId], [Language])
SELECT DISTINCT @personId, [value] FROM OPENJSON(@languages);

INSERT INTO [{{schema}}].[Person_email_EmailAddress] ([PersonId], [Email])
SELECT DISTINCT @personId, [value] FROM OPENJSON(@emails);

INSERT INTO [{{schema}}].[Person_hasInterest_Tag] ([PersonId], [TagId])
SELECT DISTINCT @personId, CAST([value] AS BIGINT) FROM OPENJSON(@tagIds);

INSERT INTO [{{schema}}].[Person_studyAt_University] ([PersonId], [UniversityId], [ClassYear])
SELECT @personId, [organizationId], [year] FROM OPENJSON(@studyAt) WITH ([organizationId] BIGINT, [year] INT);

INSERT INTO [{{schema}}].[Person_workAt_Company] ([PersonId], [CompanyId], [WorkFrom])
SELECT @personId, [organizationId], [year] FROM OPENJSON(@workAt) WITH ([organizationId] BIGINT, [year] INT);

COMMIT TRANSACTION;
