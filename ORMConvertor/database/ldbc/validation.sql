-- Loads the validation set of LDBC SNB Interactive v1 (decision 117) into the table
-- ValidationOperation, beside the data set it judges. Run by sqlcmd after load.sql and
-- constraints.sql, with one variable, ValidationFile: the file validation_params-sf<N>.csv
-- of the archive validation_params-interactive-v1.0.0-sf0.1-to-sf10.tar.zst, unpacked, as a
-- path the database server itself can read, since BULK INSERT runs there. The scale factor of
-- the file has to be the scale factor of the data; the loader takes both from one setting.
--
-- The file is what the LDBC driver wrote while it replayed the update streams and the reads
-- in one thread against the reference implementation: one operation per line, written as
-- "parameters|result", both JSON objects keyed by the field names of the driver. A line does
-- not name its operation; the fields do, and the CASE below reads the operation off the one
-- field only it has. An insert's result is the string "-1". Dates and moments are numbers
-- of milliseconds since the epoch, in UTC. The set is used as a test oracle under CC BY 4.0;
-- running it is not an LDBC Benchmark and nothing measured by it is an LDBC Benchmark result.
--
-- Position is the line number, and the order of the lines is the whole point: a read's
-- expected result holds in the state after every insert above it. The lines are numbered by
-- an IDENTITY as BULK INSERT reads the file, and the driver replays the update streams in
-- the order of their dates, so a load that lost the order of the file stops at the check at
-- the end rather than handing the suites a judge that is wrong. The {{schema}} placeholder
-- and the GO separators work as in schema.sql.

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'[{{schema}}].[ValidationOperation]', N'U') IS NOT NULL DROP TABLE [{{schema}}].[ValidationOperation];
IF SCHEMA_ID('ldbc_load') IS NULL EXEC (N'CREATE SCHEMA [ldbc_load]');
GO

CREATE TABLE [{{schema}}].[ValidationOperation] (
    [Position]    INT            NOT NULL,
    [Operation]   VARCHAR(8)     COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Parameters]  NVARCHAR(MAX)  NOT NULL,
    [Result]      NVARCHAR(MAX)  NOT NULL,
    CONSTRAINT [PK_ValidationOperation] PRIMARY KEY ([Position])
);
GO

CREATE INDEX [IX_ValidationOperation_Operation] ON [{{schema}}].[ValidationOperation] ([Operation]);

CREATE TABLE [ldbc_load].[ValidationLine] (
    [Position]    INT IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    [Parameters]  VARCHAR(MAX) COLLATE Latin1_General_100_BIN2_UTF8,
    [Result]      VARCHAR(MAX) COLLATE Latin1_General_100_BIN2_UTF8
);
GO

-- BULK INSERT cannot skip the IDENTITY column of a table without a format file; a view over
-- the two text columns takes its place.
CREATE VIEW [ldbc_load].[ValidationLineInput] AS SELECT [Parameters], [Result] FROM [ldbc_load].[ValidationLine];
GO

BULK INSERT [ldbc_load].[ValidationLineInput] FROM '$(ValidationFile)' WITH (FIELDTERMINATOR = '|', ROWTERMINATOR = '0x0a', CODEPAGE = 'RAW', TABLOCK);
GO

INSERT INTO [{{schema}}].[ValidationOperation] WITH (TABLOCK) ([Position], [Operation], [Parameters], [Result])
SELECT [Position],
       CASE
           WHEN JSON_VALUE([Parameters], '$.personIdSQ1') IS NOT NULL THEN 'IS1'
           WHEN JSON_VALUE([Parameters], '$.personIdSQ2') IS NOT NULL THEN 'IS2'
           WHEN JSON_VALUE([Parameters], '$.personIdSQ3') IS NOT NULL THEN 'IS3'
           WHEN JSON_VALUE([Parameters], '$.messageIdContent') IS NOT NULL THEN 'IS4'
           WHEN JSON_VALUE([Parameters], '$.messageIdCreator') IS NOT NULL THEN 'IS5'
           WHEN JSON_VALUE([Parameters], '$.messageForumId') IS NOT NULL THEN 'IS6'
           WHEN JSON_VALUE([Parameters], '$.messageRepliesId') IS NOT NULL THEN 'IS7'
           WHEN JSON_VALUE([Parameters], '$.personIdQ1') IS NOT NULL THEN 'IC1'
           WHEN JSON_VALUE([Parameters], '$.personIdQ2') IS NOT NULL THEN 'IC2'
           WHEN JSON_VALUE([Parameters], '$.personIdQ3') IS NOT NULL THEN 'IC3'
           WHEN JSON_VALUE([Parameters], '$.personIdQ4') IS NOT NULL THEN 'IC4'
           WHEN JSON_VALUE([Parameters], '$.personIdQ5') IS NOT NULL THEN 'IC5'
           WHEN JSON_VALUE([Parameters], '$.personIdQ6') IS NOT NULL THEN 'IC6'
           WHEN JSON_VALUE([Parameters], '$.personIdQ7') IS NOT NULL THEN 'IC7'
           WHEN JSON_VALUE([Parameters], '$.personIdQ8') IS NOT NULL THEN 'IC8'
           WHEN JSON_VALUE([Parameters], '$.personIdQ9') IS NOT NULL THEN 'IC9'
           WHEN JSON_VALUE([Parameters], '$.personIdQ10') IS NOT NULL THEN 'IC10'
           WHEN JSON_VALUE([Parameters], '$.personIdQ11') IS NOT NULL THEN 'IC11'
           WHEN JSON_VALUE([Parameters], '$.personIdQ12') IS NOT NULL THEN 'IC12'
           WHEN JSON_VALUE([Parameters], '$.person1IdQ13StartNode') IS NOT NULL THEN 'IC13'
           WHEN JSON_VALUE([Parameters], '$.person1IdQ14StartNode') IS NOT NULL THEN 'IC14'
           -- The inserts, each by the field no other one has; a post and a like of a post
           -- both name postId, a comment and a like of a comment both name commentId, so
           -- the post and the comment are told apart first.
           WHEN JSON_VALUE([Parameters], '$.personFirstName') IS NOT NULL THEN 'INS1'
           WHEN JSON_VALUE([Parameters], '$.imageFile') IS NOT NULL THEN 'INS6'
           WHEN JSON_VALUE([Parameters], '$.replyToPostId') IS NOT NULL THEN 'INS7'
           WHEN JSON_VALUE([Parameters], '$.postId') IS NOT NULL THEN 'INS2'
           WHEN JSON_VALUE([Parameters], '$.commentId') IS NOT NULL THEN 'INS3'
           WHEN JSON_VALUE([Parameters], '$.forumTitle') IS NOT NULL THEN 'INS4'
           WHEN JSON_VALUE([Parameters], '$.joinDate') IS NOT NULL THEN 'INS5'
           WHEN JSON_VALUE([Parameters], '$.person1Id') IS NOT NULL THEN 'INS8'
       END,
       [Parameters], [Result]
FROM [ldbc_load].[ValidationLine];
GO

IF EXISTS (SELECT 1 FROM [{{schema}}].[ValidationOperation] WHERE [Operation] IS NULL)
    THROW 50117, 'A line of the validation set names fields no operation of Interactive v1 has.', 1;

IF NOT EXISTS (SELECT 1 FROM [{{schema}}].[ValidationOperation])
    THROW 50117, 'The validation set is empty.', 1;

-- The order of the file: the inserts come in the order of their dates.
IF EXISTS (
    SELECT 1
    FROM (
        SELECT CAST(COALESCE(JSON_VALUE([Parameters], '$.creationDate'), JSON_VALUE([Parameters], '$.joinDate')) AS BIGINT) AS [Date],
               LAG(CAST(COALESCE(JSON_VALUE([Parameters], '$.creationDate'), JSON_VALUE([Parameters], '$.joinDate')) AS BIGINT))
                   OVER (ORDER BY [Position]) AS [PreviousDate]
        FROM [{{schema}}].[ValidationOperation]
        WHERE [Operation] LIKE 'INS%') AS [inserts]
    WHERE [Date] < [PreviousDate])
    THROW 50117, 'The inserts of the validation set are not in the order of their dates; the order of the file was lost on the way in.', 1;
GO

-- The compensation of updates/undo.sql deletes by the keys the inserts name. That is safe only
-- while no such key is in the bulk-loaded data, which the set was made for; checked once here,
-- so that a set over other data stops the load instead of letting a compensation delete rows
-- the data set holds.
IF EXISTS (
    SELECT 1 FROM [{{schema}}].[ValidationOperation] AS [o]
    JOIN [{{schema}}].[Person] AS [p] ON [p].[Id] = CAST(JSON_VALUE([o].[Parameters], '$.personId') AS BIGINT)
    WHERE [o].[Operation] = 'INS1')
OR EXISTS (
    SELECT 1 FROM [{{schema}}].[ValidationOperation] AS [o]
    JOIN [{{schema}}].[Message] AS [m]
      ON [m].[Id] = CAST(COALESCE(JSON_VALUE([o].[Parameters], '$.postId'), JSON_VALUE([o].[Parameters], '$.commentId')) AS BIGINT)
    WHERE [o].[Operation] IN ('INS6', 'INS7'))
OR EXISTS (
    SELECT 1 FROM [{{schema}}].[ValidationOperation] AS [o]
    JOIN [{{schema}}].[Forum] AS [f] ON [f].[Id] = CAST(JSON_VALUE([o].[Parameters], '$.forumId') AS BIGINT)
    WHERE [o].[Operation] = 'INS4')
OR EXISTS (
    SELECT 1 FROM [{{schema}}].[ValidationOperation] AS [o]
    JOIN [{{schema}}].[Person_likes_Message] AS [l]
      ON [l].[PersonId] = CAST(JSON_VALUE([o].[Parameters], '$.personId') AS BIGINT)
     AND [l].[MessageId] = CAST(COALESCE(JSON_VALUE([o].[Parameters], '$.postId'), JSON_VALUE([o].[Parameters], '$.commentId')) AS BIGINT)
    WHERE [o].[Operation] IN ('INS2', 'INS3'))
OR EXISTS (
    SELECT 1 FROM [{{schema}}].[ValidationOperation] AS [o]
    JOIN [{{schema}}].[Forum_hasMember_Person] AS [fm]
      ON [fm].[ForumId] = CAST(JSON_VALUE([o].[Parameters], '$.forumId') AS BIGINT)
     AND [fm].[PersonId] = CAST(JSON_VALUE([o].[Parameters], '$.personId') AS BIGINT)
    WHERE [o].[Operation] = 'INS5')
OR EXISTS (
    SELECT 1 FROM [{{schema}}].[ValidationOperation] AS [o]
    JOIN [{{schema}}].[Person_knows_Person] AS [k]
      ON [k].[Person1Id] = CAST(JSON_VALUE([o].[Parameters], '$.person1Id') AS BIGINT)
     AND [k].[Person2Id] = CAST(JSON_VALUE([o].[Parameters], '$.person2Id') AS BIGINT)
    WHERE [o].[Operation] = 'INS8')
    THROW 50117, 'An insert of the validation set names a key the loaded data already holds; the set does not belong to this data set, or a replay left its rows behind.', 1;
GO

DROP VIEW [ldbc_load].[ValidationLineInput];
DROP TABLE [ldbc_load].[ValidationLine];
DROP SCHEMA [ldbc_load];
GO
