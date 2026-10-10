-- Indexes of the LDBC tables (decision 110), run after constraints.sql and, in the container,
-- after the data is loaded. The script is rerunnable: every index is dropped if it exists and
-- created again, so the same script brings a database that already holds the data to the
-- physical design of this revision - load-ldbc.sh runs it whenever its checksum differs from
-- the extended property ldbc.indexes of the database, and a database loaded by hand gets it
-- run by hand.
--
-- The indexes are those of the reference implementations of LDBC - every foreign key column
-- that is not the first column of a primary key - with two of them widened for IC 5 (decision
-- 121): the membership index carries the join date, so that the memberships of a person after
-- a date are one range, and the forum index of Message carries the creator and the parent, so
-- that the posts of a member in a forum are one seek. Measured over scale factor 1, the two
-- take one read of IC 5 from 1.4 s to 0.4 s; without the shape of decision 121 they take it
-- from 5.5 s to 4.4 s only.
--
-- The {{schema}} placeholder and the GO separators work as in schema.sql.

DROP INDEX IF EXISTS [IX_Place_PartOfPlaceId] ON [{{schema}}].[Place];
CREATE INDEX [IX_Place_PartOfPlaceId] ON [{{schema}}].[Place] ([PartOfPlaceId]);
DROP INDEX IF EXISTS [IX_Organisation_LocationPlaceId] ON [{{schema}}].[Organisation];
CREATE INDEX [IX_Organisation_LocationPlaceId] ON [{{schema}}].[Organisation] ([LocationPlaceId]);
DROP INDEX IF EXISTS [IX_Tag_TypeTagClassId] ON [{{schema}}].[Tag];
CREATE INDEX [IX_Tag_TypeTagClassId] ON [{{schema}}].[Tag] ([TypeTagClassId]);
DROP INDEX IF EXISTS [IX_Tag_Name] ON [{{schema}}].[Tag];
CREATE INDEX [IX_Tag_Name] ON [{{schema}}].[Tag] ([Name]);
DROP INDEX IF EXISTS [IX_TagClass_SubclassOfTagClassId] ON [{{schema}}].[TagClass];
CREATE INDEX [IX_TagClass_SubclassOfTagClassId] ON [{{schema}}].[TagClass] ([SubclassOfTagClassId]);
DROP INDEX IF EXISTS [IX_Person_LocationCityId] ON [{{schema}}].[Person];
CREATE INDEX [IX_Person_LocationCityId] ON [{{schema}}].[Person] ([LocationCityId]);
DROP INDEX IF EXISTS [IX_Person_FirstName] ON [{{schema}}].[Person];
CREATE INDEX [IX_Person_FirstName] ON [{{schema}}].[Person] ([FirstName]);
DROP INDEX IF EXISTS [IX_Forum_ModeratorPersonId] ON [{{schema}}].[Forum];
CREATE INDEX [IX_Forum_ModeratorPersonId] ON [{{schema}}].[Forum] ([ModeratorPersonId]);
DROP INDEX IF EXISTS [IX_Message_CreatorPersonId] ON [{{schema}}].[Message];
CREATE INDEX [IX_Message_CreatorPersonId] ON [{{schema}}].[Message] ([CreatorPersonId], [CreationDate]);
DROP INDEX IF EXISTS [IX_Message_ParentMessageId] ON [{{schema}}].[Message];
CREATE INDEX [IX_Message_ParentMessageId] ON [{{schema}}].[Message] ([ParentMessageId]);
DROP INDEX IF EXISTS [IX_Message_RootPostId] ON [{{schema}}].[Message];
CREATE INDEX [IX_Message_RootPostId] ON [{{schema}}].[Message] ([RootPostId]);
DROP INDEX IF EXISTS [IX_Message_ContainerForumId] ON [{{schema}}].[Message];
CREATE INDEX [IX_Message_ContainerForumId] ON [{{schema}}].[Message] ([ContainerForumId], [CreatorPersonId]) INCLUDE ([ParentMessageId]);
DROP INDEX IF EXISTS [IX_Message_LocationCountryId] ON [{{schema}}].[Message];
CREATE INDEX [IX_Message_LocationCountryId] ON [{{schema}}].[Message] ([LocationCountryId]);
DROP INDEX IF EXISTS [IX_Message_CreationDate] ON [{{schema}}].[Message];
CREATE INDEX [IX_Message_CreationDate] ON [{{schema}}].[Message] ([CreationDate]);
DROP INDEX IF EXISTS [IX_Person_knows_Person_Person2Id] ON [{{schema}}].[Person_knows_Person];
CREATE INDEX [IX_Person_knows_Person_Person2Id] ON [{{schema}}].[Person_knows_Person] ([Person2Id]);
DROP INDEX IF EXISTS [IX_Person_likes_Message_MessageId] ON [{{schema}}].[Person_likes_Message];
CREATE INDEX [IX_Person_likes_Message_MessageId] ON [{{schema}}].[Person_likes_Message] ([MessageId]);
DROP INDEX IF EXISTS [IX_Message_hasTag_Tag_TagId] ON [{{schema}}].[Message_hasTag_Tag];
CREATE INDEX [IX_Message_hasTag_Tag_TagId] ON [{{schema}}].[Message_hasTag_Tag] ([TagId]);
DROP INDEX IF EXISTS [IX_Forum_hasMember_Person_PersonId] ON [{{schema}}].[Forum_hasMember_Person];
CREATE INDEX [IX_Forum_hasMember_Person_PersonId] ON [{{schema}}].[Forum_hasMember_Person] ([PersonId], [JoinDate]);
DROP INDEX IF EXISTS [IX_Forum_hasTag_Tag_TagId] ON [{{schema}}].[Forum_hasTag_Tag];
CREATE INDEX [IX_Forum_hasTag_Tag_TagId] ON [{{schema}}].[Forum_hasTag_Tag] ([TagId]);
DROP INDEX IF EXISTS [IX_Person_hasInterest_Tag_TagId] ON [{{schema}}].[Person_hasInterest_Tag];
CREATE INDEX [IX_Person_hasInterest_Tag_TagId] ON [{{schema}}].[Person_hasInterest_Tag] ([TagId]);
DROP INDEX IF EXISTS [IX_Person_studyAt_University_UniversityId] ON [{{schema}}].[Person_studyAt_University];
CREATE INDEX [IX_Person_studyAt_University_UniversityId] ON [{{schema}}].[Person_studyAt_University] ([UniversityId]);
DROP INDEX IF EXISTS [IX_Person_workAt_Company_CompanyId] ON [{{schema}}].[Person_workAt_Company];
CREATE INDEX [IX_Person_workAt_Company_CompanyId] ON [{{schema}}].[Person_workAt_Company] ([CompanyId]);
GO
