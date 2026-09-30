-- Foreign keys and indexes of the LDBC tables (decision 110), run after schema.sql and,
-- in the container, after the data is loaded. The foreign keys are declared WITH CHECK, so
-- the load itself is verified once, at the end, and the constraints are trusted by the
-- optimizer and by the catalog completion phase alike: the catalog reads relations from
-- exactly these declarations (decision 015). The indexes serve the joins the query catalog
-- makes - every foreign key column that is not the first column of a primary key.
--
-- The {{schema}} placeholder and the GO separators work as in schema.sql.

ALTER TABLE [{{schema}}].[Place] WITH CHECK ADD
    CONSTRAINT [FK_Place_PartOfPlace] FOREIGN KEY ([PartOfPlaceId]) REFERENCES [{{schema}}].[Place] ([Id]);
GO

ALTER TABLE [{{schema}}].[Organisation] WITH CHECK ADD
    CONSTRAINT [FK_Organisation_LocationPlace] FOREIGN KEY ([LocationPlaceId]) REFERENCES [{{schema}}].[Place] ([Id]);
GO

ALTER TABLE [{{schema}}].[TagClass] WITH CHECK ADD
    CONSTRAINT [FK_TagClass_SubclassOfTagClass] FOREIGN KEY ([SubclassOfTagClassId]) REFERENCES [{{schema}}].[TagClass] ([Id]);
GO

ALTER TABLE [{{schema}}].[Tag] WITH CHECK ADD
    CONSTRAINT [FK_Tag_TypeTagClass] FOREIGN KEY ([TypeTagClassId]) REFERENCES [{{schema}}].[TagClass] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person] WITH CHECK ADD
    CONSTRAINT [FK_Person_LocationCity] FOREIGN KEY ([LocationCityId]) REFERENCES [{{schema}}].[Place] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_email_EmailAddress] WITH CHECK ADD
    CONSTRAINT [FK_Person_email_EmailAddress_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_speaks_Language] WITH CHECK ADD
    CONSTRAINT [FK_Person_speaks_Language_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]);
GO

ALTER TABLE [{{schema}}].[Forum] WITH CHECK ADD
    CONSTRAINT [FK_Forum_ModeratorPerson] FOREIGN KEY ([ModeratorPersonId]) REFERENCES [{{schema}}].[Person] ([Id]);
GO

ALTER TABLE [{{schema}}].[Message] WITH CHECK ADD
    CONSTRAINT [FK_Message_CreatorPerson] FOREIGN KEY ([CreatorPersonId]) REFERENCES [{{schema}}].[Person] ([Id]),
    CONSTRAINT [FK_Message_LocationCountry] FOREIGN KEY ([LocationCountryId]) REFERENCES [{{schema}}].[Place] ([Id]),
    CONSTRAINT [FK_Message_ParentMessage] FOREIGN KEY ([ParentMessageId]) REFERENCES [{{schema}}].[Message] ([Id]),
    CONSTRAINT [FK_Message_RootPost] FOREIGN KEY ([RootPostId]) REFERENCES [{{schema}}].[Message] ([Id]),
    CONSTRAINT [FK_Message_ContainerForum] FOREIGN KEY ([ContainerForumId]) REFERENCES [{{schema}}].[Forum] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_knows_Person] WITH CHECK ADD
    CONSTRAINT [FK_Person_knows_Person_Person1] FOREIGN KEY ([Person1Id]) REFERENCES [{{schema}}].[Person] ([Id]),
    CONSTRAINT [FK_Person_knows_Person_Person2] FOREIGN KEY ([Person2Id]) REFERENCES [{{schema}}].[Person] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_likes_Message] WITH CHECK ADD
    CONSTRAINT [FK_Person_likes_Message_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]),
    CONSTRAINT [FK_Person_likes_Message_Message] FOREIGN KEY ([MessageId]) REFERENCES [{{schema}}].[Message] ([Id]);
GO

ALTER TABLE [{{schema}}].[Message_hasTag_Tag] WITH CHECK ADD
    CONSTRAINT [FK_Message_hasTag_Tag_Message] FOREIGN KEY ([MessageId]) REFERENCES [{{schema}}].[Message] ([Id]),
    CONSTRAINT [FK_Message_hasTag_Tag_Tag] FOREIGN KEY ([TagId]) REFERENCES [{{schema}}].[Tag] ([Id]);
GO

ALTER TABLE [{{schema}}].[Forum_hasMember_Person] WITH CHECK ADD
    CONSTRAINT [FK_Forum_hasMember_Person_Forum] FOREIGN KEY ([ForumId]) REFERENCES [{{schema}}].[Forum] ([Id]),
    CONSTRAINT [FK_Forum_hasMember_Person_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]);
GO

ALTER TABLE [{{schema}}].[Forum_hasTag_Tag] WITH CHECK ADD
    CONSTRAINT [FK_Forum_hasTag_Tag_Forum] FOREIGN KEY ([ForumId]) REFERENCES [{{schema}}].[Forum] ([Id]),
    CONSTRAINT [FK_Forum_hasTag_Tag_Tag] FOREIGN KEY ([TagId]) REFERENCES [{{schema}}].[Tag] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_hasInterest_Tag] WITH CHECK ADD
    CONSTRAINT [FK_Person_hasInterest_Tag_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]),
    CONSTRAINT [FK_Person_hasInterest_Tag_Tag] FOREIGN KEY ([TagId]) REFERENCES [{{schema}}].[Tag] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_studyAt_University] WITH CHECK ADD
    CONSTRAINT [FK_Person_studyAt_University_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]),
    CONSTRAINT [FK_Person_studyAt_University_University] FOREIGN KEY ([UniversityId]) REFERENCES [{{schema}}].[Organisation] ([Id]);
GO

ALTER TABLE [{{schema}}].[Person_workAt_Company] WITH CHECK ADD
    CONSTRAINT [FK_Person_workAt_Company_Person] FOREIGN KEY ([PersonId]) REFERENCES [{{schema}}].[Person] ([Id]),
    CONSTRAINT [FK_Person_workAt_Company_Company] FOREIGN KEY ([CompanyId]) REFERENCES [{{schema}}].[Organisation] ([Id]);
GO

CREATE INDEX [IX_Place_PartOfPlaceId] ON [{{schema}}].[Place] ([PartOfPlaceId]);
CREATE INDEX [IX_Organisation_LocationPlaceId] ON [{{schema}}].[Organisation] ([LocationPlaceId]);
CREATE INDEX [IX_Tag_TypeTagClassId] ON [{{schema}}].[Tag] ([TypeTagClassId]);
CREATE INDEX [IX_Tag_Name] ON [{{schema}}].[Tag] ([Name]);
CREATE INDEX [IX_TagClass_SubclassOfTagClassId] ON [{{schema}}].[TagClass] ([SubclassOfTagClassId]);
CREATE INDEX [IX_Person_LocationCityId] ON [{{schema}}].[Person] ([LocationCityId]);
CREATE INDEX [IX_Person_FirstName] ON [{{schema}}].[Person] ([FirstName]);
CREATE INDEX [IX_Forum_ModeratorPersonId] ON [{{schema}}].[Forum] ([ModeratorPersonId]);
CREATE INDEX [IX_Message_CreatorPersonId] ON [{{schema}}].[Message] ([CreatorPersonId], [CreationDate]);
CREATE INDEX [IX_Message_ParentMessageId] ON [{{schema}}].[Message] ([ParentMessageId]);
CREATE INDEX [IX_Message_RootPostId] ON [{{schema}}].[Message] ([RootPostId]);
CREATE INDEX [IX_Message_ContainerForumId] ON [{{schema}}].[Message] ([ContainerForumId]);
CREATE INDEX [IX_Message_LocationCountryId] ON [{{schema}}].[Message] ([LocationCountryId]);
CREATE INDEX [IX_Message_CreationDate] ON [{{schema}}].[Message] ([CreationDate]);
CREATE INDEX [IX_Person_knows_Person_Person2Id] ON [{{schema}}].[Person_knows_Person] ([Person2Id]);
CREATE INDEX [IX_Person_likes_Message_MessageId] ON [{{schema}}].[Person_likes_Message] ([MessageId]);
CREATE INDEX [IX_Message_hasTag_Tag_TagId] ON [{{schema}}].[Message_hasTag_Tag] ([TagId]);
CREATE INDEX [IX_Forum_hasMember_Person_PersonId] ON [{{schema}}].[Forum_hasMember_Person] ([PersonId]);
CREATE INDEX [IX_Forum_hasTag_Tag_TagId] ON [{{schema}}].[Forum_hasTag_Tag] ([TagId]);
CREATE INDEX [IX_Person_hasInterest_Tag_TagId] ON [{{schema}}].[Person_hasInterest_Tag] ([TagId]);
CREATE INDEX [IX_Person_studyAt_University_UniversityId] ON [{{schema}}].[Person_studyAt_University] ([UniversityId]);
CREATE INDEX [IX_Person_workAt_Company_CompanyId] ON [{{schema}}].[Person_workAt_Company] ([CompanyId]);
GO
