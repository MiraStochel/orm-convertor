-- ORMConvertor: taken from cwoodruff/ChinookDatabase at 8065767, Scripts/Chinook_SqlServer.sql -
-- the database the Web API's README points at (Chinook Database 1.4 by Luis Rocha, MIT).
-- Changed: the eleven CREATE TABLE statements and the foreign keys are kept; the database
-- creation, the indexes and the inserted rows are left out, and every table is qualified with
-- {{schema}}, the schema the caller creates, where the script creates in dbo. The single-column
-- primary keys are renamed from <Table>Id to Id - AlbumId to Album.Id, ArtistId to Artist.Id
-- and so on -, because that is the variant the project runs against: every entity declares
-- [Key] public int Id, its fluent configuration maps the table under that name, and its Dapper
-- statements say Album.Id; PlaylistTrack keeps PlaylistId and TrackId, which are foreign keys
-- as well. The foreign key columns, the types, the lengths, the nullability and the constraint
-- names are the script's own.
--
-- The test suites put these tables, empty, into a schema of their own as the catalog of the
-- excerpt (decision 176).

CREATE TABLE [{{schema}}].[Album]
(
    [Id] INT NOT NULL,
    [Title] NVARCHAR(160) NOT NULL,
    [ArtistId] INT NOT NULL,
    CONSTRAINT [PK_Album] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[Artist]
(
    [Id] INT NOT NULL,
    [Name] NVARCHAR(120),
    CONSTRAINT [PK_Artist] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[Customer]
(
    [Id] INT NOT NULL,
    [FirstName] NVARCHAR(40) NOT NULL,
    [LastName] NVARCHAR(20) NOT NULL,
    [Company] NVARCHAR(80),
    [Address] NVARCHAR(70),
    [City] NVARCHAR(40),
    [State] NVARCHAR(40),
    [Country] NVARCHAR(40),
    [PostalCode] NVARCHAR(10),
    [Phone] NVARCHAR(24),
    [Fax] NVARCHAR(24),
    [Email] NVARCHAR(60) NOT NULL,
    [SupportRepId] INT,
    CONSTRAINT [PK_Customer] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[Employee]
(
    [Id] INT NOT NULL,
    [LastName] NVARCHAR(20) NOT NULL,
    [FirstName] NVARCHAR(20) NOT NULL,
    [Title] NVARCHAR(30),
    [ReportsTo] INT,
    [BirthDate] DATETIME,
    [HireDate] DATETIME,
    [Address] NVARCHAR(70),
    [City] NVARCHAR(40),
    [State] NVARCHAR(40),
    [Country] NVARCHAR(40),
    [PostalCode] NVARCHAR(10),
    [Phone] NVARCHAR(24),
    [Fax] NVARCHAR(24),
    [Email] NVARCHAR(60),
    CONSTRAINT [PK_Employee] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[Genre]
(
    [Id] INT NOT NULL,
    [Name] NVARCHAR(120),
    CONSTRAINT [PK_Genre] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[Invoice]
(
    [Id] INT NOT NULL,
    [CustomerId] INT NOT NULL,
    [InvoiceDate] DATETIME NOT NULL,
    [BillingAddress] NVARCHAR(70),
    [BillingCity] NVARCHAR(40),
    [BillingState] NVARCHAR(40),
    [BillingCountry] NVARCHAR(40),
    [BillingPostalCode] NVARCHAR(10),
    [Total] NUMERIC(10,2) NOT NULL,
    CONSTRAINT [PK_Invoice] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[InvoiceLine]
(
    [Id] INT NOT NULL,
    [InvoiceId] INT NOT NULL,
    [TrackId] INT NOT NULL,
    [UnitPrice] NUMERIC(10,2) NOT NULL,
    [Quantity] INT NOT NULL,
    CONSTRAINT [PK_InvoiceLine] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[MediaType]
(
    [Id] INT NOT NULL,
    [Name] NVARCHAR(120),
    CONSTRAINT [PK_MediaType] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[Playlist]
(
    [Id] INT NOT NULL,
    [Name] NVARCHAR(120),
    CONSTRAINT [PK_Playlist] PRIMARY KEY CLUSTERED ([Id])
);

CREATE TABLE [{{schema}}].[PlaylistTrack]
(
    [PlaylistId] INT NOT NULL,
    [TrackId] INT NOT NULL,
    CONSTRAINT [PK_PlaylistTrack] PRIMARY KEY NONCLUSTERED ([PlaylistId], [TrackId])
);

CREATE TABLE [{{schema}}].[Track]
(
    [Id] INT NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [AlbumId] INT,
    [MediaTypeId] INT NOT NULL,
    [GenreId] INT,
    [Composer] NVARCHAR(220),
    [Milliseconds] INT NOT NULL,
    [Bytes] INT,
    [UnitPrice] NUMERIC(10,2) NOT NULL,
    CONSTRAINT [PK_Track] PRIMARY KEY CLUSTERED ([Id])
);

ALTER TABLE [{{schema}}].[Album] ADD CONSTRAINT [FK_AlbumArtistId]
    FOREIGN KEY ([ArtistId]) REFERENCES [{{schema}}].[Artist] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[Customer] ADD CONSTRAINT [FK_CustomerSupportRepId]
    FOREIGN KEY ([SupportRepId]) REFERENCES [{{schema}}].[Employee] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[Employee] ADD CONSTRAINT [FK_EmployeeReportsTo]
    FOREIGN KEY ([ReportsTo]) REFERENCES [{{schema}}].[Employee] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[Invoice] ADD CONSTRAINT [FK_InvoiceCustomerId]
    FOREIGN KEY ([CustomerId]) REFERENCES [{{schema}}].[Customer] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[InvoiceLine] ADD CONSTRAINT [FK_InvoiceLineInvoiceId]
    FOREIGN KEY ([InvoiceId]) REFERENCES [{{schema}}].[Invoice] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[InvoiceLine] ADD CONSTRAINT [FK_InvoiceLineTrackId]
    FOREIGN KEY ([TrackId]) REFERENCES [{{schema}}].[Track] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[PlaylistTrack] ADD CONSTRAINT [FK_PlaylistTrackPlaylistId]
    FOREIGN KEY ([PlaylistId]) REFERENCES [{{schema}}].[Playlist] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[PlaylistTrack] ADD CONSTRAINT [FK_PlaylistTrackTrackId]
    FOREIGN KEY ([TrackId]) REFERENCES [{{schema}}].[Track] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[Track] ADD CONSTRAINT [FK_TrackAlbumId]
    FOREIGN KEY ([AlbumId]) REFERENCES [{{schema}}].[Album] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[Track] ADD CONSTRAINT [FK_TrackGenreId]
    FOREIGN KEY ([GenreId]) REFERENCES [{{schema}}].[Genre] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
ALTER TABLE [{{schema}}].[Track] ADD CONSTRAINT [FK_TrackMediaTypeId]
    FOREIGN KEY ([MediaTypeId]) REFERENCES [{{schema}}].[MediaType] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
