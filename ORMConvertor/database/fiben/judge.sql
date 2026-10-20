-- Loads the judge of the FIBEN catalog (decision 149) into the table Judge, beside the data it
-- judges. Run by sqlcmd after constraints.sql with one variable, JudgeFile: database/fiben/
-- judge.tsv as a path the database server itself can read, since BULK INSERT runs there.
--
-- The file is the result of a documented run of the catalog, frozen: for every query
-- its rows in the canonical form of decision 089, one row per line as
-- "key<TAB>position<TAB>rendered row", the rendered row itself tab-separated as the form
-- writes it, and lines beginning with # the record of when and over which commit it was made.
-- Every query the run translated has its digest at position 0, one that returned no rows the
-- digest of none; a query with no line was refused by that run and is not judged. The judge
-- is a regression judge: it says that a version returns what the version of that run
-- returned, not what is true elsewhere, and it changes only by a decision with a date and a
-- reason.
--
-- The lines go into a staging table whole - a separator no line contains - and are split
-- here, because the rendered row carries the very tabs a field terminator would split. The
-- {{schema}} placeholder and the GO separators work as in schema.sql.

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'[{{schema}}].[Judge]', N'U') IS NOT NULL DROP TABLE [{{schema}}].[Judge];
IF SCHEMA_ID('fiben_load') IS NULL EXEC (N'CREATE SCHEMA [fiben_load]');
GO

CREATE TABLE [{{schema}}].[Judge] (
    [QueryKey]  VARCHAR(8)     COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Position]  INT            NOT NULL,
    [Rendered]  NVARCHAR(MAX)  NOT NULL,
    CONSTRAINT [PK_Judge] PRIMARY KEY ([QueryKey], [Position])
);
GO

CREATE TABLE [fiben_load].[JudgeLine] (
    [Line] VARCHAR(MAX) COLLATE Latin1_General_100_BIN2_UTF8
);
GO

BULK INSERT [fiben_load].[JudgeLine] FROM '$(JudgeFile)' WITH (FIELDTERMINATOR = '0x01', ROWTERMINATOR = '0x0a', CODEPAGE = 'RAW', TABLOCK);
GO

INSERT INTO [{{schema}}].[Judge] WITH (TABLOCK) ([QueryKey], [Position], [Rendered])
SELECT LEFT([Line], CHARINDEX(CHAR(9), [Line]) - 1),
       CAST(SUBSTRING([Line], CHARINDEX(CHAR(9), [Line]) + 1, CHARINDEX(CHAR(9), [Line], CHARINDEX(CHAR(9), [Line]) + 1) - CHARINDEX(CHAR(9), [Line]) - 1) AS INT),
       CAST(SUBSTRING([Line], CHARINDEX(CHAR(9), [Line], CHARINDEX(CHAR(9), [Line]) + 1) + 1, LEN([Line] + 'x') - 1) AS NVARCHAR(MAX))
FROM [fiben_load].[JudgeLine]
WHERE [Line] IS NOT NULL AND LEFT([Line], 1) <> '#' AND LEN([Line] + 'x') > 1;
GO

IF NOT EXISTS (SELECT 1 FROM [{{schema}}].[Judge])
    THROW 50149, 'The judge of the FIBEN catalog is empty.', 1;
GO

DROP TABLE [fiben_load].[JudgeLine];
DROP SCHEMA [fiben_load];
GO
