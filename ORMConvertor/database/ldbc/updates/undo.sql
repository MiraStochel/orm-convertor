-- The compensation of the validation set (decision 117): deletes every row that INS 1-8 of
-- the set in ValidationOperation write, by the keys the operations name, and so returns the
-- database to the bulk-loaded state - whether the whole set was replayed, part of it, or none.
-- Deleting a row that is not there does nothing, so this is safe to run at any time, and the
-- suites run it before a replay as well as after it: a replay cut short leaves nothing the
-- next one does not clean up first. That no key named here is in the bulk-loaded data is
-- checked once, when validation.sql loads the set. One batch, no parameters; the {{schema}}
-- placeholder works as in schema.sql. The order respects the foreign keys of constraints.sql.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DROP TABLE IF EXISTS [#Person], [#Message], [#Forum], [#Like], [#Membership], [#Friendship];

CREATE TABLE [#Person] ([Id] BIGINT NOT NULL PRIMARY KEY);
CREATE TABLE [#Message] ([Id] BIGINT NOT NULL PRIMARY KEY);
CREATE TABLE [#Forum] ([Id] BIGINT NOT NULL PRIMARY KEY);
CREATE TABLE [#Like] ([PersonId] BIGINT NOT NULL, [MessageId] BIGINT NOT NULL, PRIMARY KEY ([PersonId], [MessageId]));
CREATE TABLE [#Membership] ([ForumId] BIGINT NOT NULL, [PersonId] BIGINT NOT NULL, PRIMARY KEY ([ForumId], [PersonId]));
CREATE TABLE [#Friendship] ([Person1Id] BIGINT NOT NULL, [Person2Id] BIGINT NOT NULL, PRIMARY KEY ([Person1Id], [Person2Id]));

INSERT INTO [#Person] ([Id])
SELECT DISTINCT CAST(JSON_VALUE([Parameters], '$.personId') AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] = 'INS1';

INSERT INTO [#Message] ([Id])
SELECT DISTINCT CAST(COALESCE(JSON_VALUE([Parameters], '$.postId'), JSON_VALUE([Parameters], '$.commentId')) AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] IN ('INS6', 'INS7');

INSERT INTO [#Forum] ([Id])
SELECT DISTINCT CAST(JSON_VALUE([Parameters], '$.forumId') AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] = 'INS4';

INSERT INTO [#Like] ([PersonId], [MessageId])
SELECT DISTINCT CAST(JSON_VALUE([Parameters], '$.personId') AS BIGINT),
       CAST(COALESCE(JSON_VALUE([Parameters], '$.postId'), JSON_VALUE([Parameters], '$.commentId')) AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] IN ('INS2', 'INS3');

INSERT INTO [#Membership] ([ForumId], [PersonId])
SELECT DISTINCT CAST(JSON_VALUE([Parameters], '$.forumId') AS BIGINT), CAST(JSON_VALUE([Parameters], '$.personId') AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] = 'INS5';

-- Both directions, as ins8.sql writes them.
INSERT INTO [#Friendship] ([Person1Id], [Person2Id])
SELECT CAST(JSON_VALUE([Parameters], '$.person1Id') AS BIGINT), CAST(JSON_VALUE([Parameters], '$.person2Id') AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] = 'INS8'
UNION
SELECT CAST(JSON_VALUE([Parameters], '$.person2Id') AS BIGINT), CAST(JSON_VALUE([Parameters], '$.person1Id') AS BIGINT)
FROM [{{schema}}].[ValidationOperation] WHERE [Operation] = 'INS8';

BEGIN TRANSACTION;

DELETE [t] FROM [{{schema}}].[Message_hasTag_Tag] AS [t] JOIN [#Message] AS [k] ON [k].[Id] = [t].[MessageId];
DELETE [l] FROM [{{schema}}].[Person_likes_Message] AS [l] JOIN [#Like] AS [k] ON [k].[PersonId] = [l].[PersonId] AND [k].[MessageId] = [l].[MessageId];

-- One statement for every new message: a comment of the set may reply to a message of the set,
-- and a foreign key of a table to itself is checked when the statement ends, not row by row.
DELETE [m] FROM [{{schema}}].[Message] AS [m] JOIN [#Message] AS [k] ON [k].[Id] = [m].[Id];

DELETE [fm] FROM [{{schema}}].[Forum_hasMember_Person] AS [fm] JOIN [#Membership] AS [k] ON [k].[ForumId] = [fm].[ForumId] AND [k].[PersonId] = [fm].[PersonId];
DELETE [ft] FROM [{{schema}}].[Forum_hasTag_Tag] AS [ft] JOIN [#Forum] AS [k] ON [k].[Id] = [ft].[ForumId];
DELETE [f] FROM [{{schema}}].[Forum] AS [f] JOIN [#Forum] AS [k] ON [k].[Id] = [f].[Id];
DELETE [kp] FROM [{{schema}}].[Person_knows_Person] AS [kp] JOIN [#Friendship] AS [k] ON [k].[Person1Id] = [kp].[Person1Id] AND [k].[Person2Id] = [kp].[Person2Id];

DELETE [e] FROM [{{schema}}].[Person_email_EmailAddress] AS [e] JOIN [#Person] AS [k] ON [k].[Id] = [e].[PersonId];
DELETE [s] FROM [{{schema}}].[Person_speaks_Language] AS [s] JOIN [#Person] AS [k] ON [k].[Id] = [s].[PersonId];
DELETE [i] FROM [{{schema}}].[Person_hasInterest_Tag] AS [i] JOIN [#Person] AS [k] ON [k].[Id] = [i].[PersonId];
DELETE [u] FROM [{{schema}}].[Person_studyAt_University] AS [u] JOIN [#Person] AS [k] ON [k].[Id] = [u].[PersonId];
DELETE [w] FROM [{{schema}}].[Person_workAt_Company] AS [w] JOIN [#Person] AS [k] ON [k].[Id] = [w].[PersonId];
DELETE [p] FROM [{{schema}}].[Person] AS [p] JOIN [#Person] AS [k] ON [k].[Id] = [p].[Id];

COMMIT TRANSACTION;

DROP TABLE [#Person], [#Message], [#Forum], [#Like], [#Membership], [#Friendship];
