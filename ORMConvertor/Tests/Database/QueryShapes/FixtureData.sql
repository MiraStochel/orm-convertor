-- Read-only data of the domain the query categories of T2 are written over (decision 089,
-- the differential matrix over the categories). Both suites create it in their schema
-- right after TestSchema.sql and Differential/FixtureData.sql, from this one script, so the
-- two halves of a pair read rows made by the same statements; the {{schema}} placeholder
-- is the same one every shared file carries.
--
-- Five tables of its own, prefixed Shop. The domain has the shape of TestSchema.sql -
-- customers, orders under a two-part key, lines under a three-part key, allocations under a
-- four-part key, products - and cannot reuse its tables: seeding tables that other
-- scenarios write to was refused when the first read-only fixture was written (decision
-- 089, history), and a source that states no table - Dapper, MyBatis - reaches its table
-- from the class name by the rule of decision 050, so the class has to carry the prefix
-- too and ShopOrderLine has to find ShopOrderLines and nothing else in the database. (A
-- schema of its own was tried first and failed for exactly that reason: OrderLines in two
-- schemas is an ambiguous match the catalog refuses to guess at.)
--
-- Every ordered query of the matrix has to have a total order over the rows it returns, or
-- two correct targets could disagree about ties; and no query may order by its clustered
-- key alone, or an unordered read would come back in the same order and the dropped
-- ordering would pass unnoticed (decision 089, history). Hence:
--
--   LineNumber is unique over the whole ShopOrderLines table, and its order is not the
--   order of the primary key (CompanyId, OrderId, LineNumber) - the paged category orders
--   by it;
--   (ProductId, Quantity) is unique over ShopOrderLines - the ordering category orders by it;
--
-- and the rows are chosen against the mutation list of decision 089, category by category:
--
--   filtering       (Quantity > 5 OR UnitPrice >= 100.5) AND NOT (ProductId = 3) keeps seven
--                   of ten lines and drops three; flipped to < and <= it keeps another seven;
--   scalar param.   Quantity >= 5 keeps five lines, <= 5 the other five - the two sets differ;
--   join            two orders belong to the walk-in customer 0, so CustomerId > 0 drops
--                   their three lines and < 0 drops everything;
--   aggregation     SUM(Quantity) per product is 11, 26, 9, 3, 4, 13: three products above
--                   ten, three below, none at ten;
--   COUNT DISTINCT  product 6 has two lines in two orders that share an OrderId (1,1) and
--                   (2,1), so the distinct count of orders is one where the line count is two;
--   subquery IN     products above 100 are 1, 4 and 6, below 100 are 2, 3 and 5, and lines
--                   exist for all six;
--   scalar subquery the average product price above 1 is 288.0833; three lines are priced
--                   above it, seven below;
--   bound count     four orders have two lines and two have one, so a count of lines >= 2
--                   keeps four orders and <= 2 keeps all six;
--   EXISTS          five lines have an allocation, five have none;
--   set operation   the description 'Zither' of a line with Quantity > 5 is also the name of
--                   a product above 100, so UNION meets on it and UNION ALL would not;
--   DISTINCT        ten lines over six products;
--   IN (1, 2, 3)    five lines; IN (1, 2, 5) another five;
--   collection      products 2 and 4 have four lines between them;
--   moment          three orders are placed after 2025-01-01 and three before it;
--   LIKE 'W%'       three product names start with W; LIKE 'W!_%' ESCAPE '!' matches only
--                   W_Bolt, and would match Whistle too if the escape were lost;
--   pagination      ORDER BY LineNumber OFFSET 2 FETCH NEXT 3 is a proper slice of ten, and
--                   the unordered read comes back in key order, which is a different slice.
--
-- No column carries a default, so nothing unstated can reach a canonical result, and every
-- DATETIME2 value states its fraction, so the renderer's three digits are the column's.

CREATE TABLE [{{schema}}].[ShopCustomers] (
    [CustomerId]   INT             IDENTITY(1,1) NOT NULL,
    [Name]         NVARCHAR(100)   NOT NULL,
    [Notes]        NVARCHAR(400)   NULL,
    CONSTRAINT [PK_ShopCustomers] PRIMARY KEY ([CustomerId])
);
GO

CREATE TABLE [{{schema}}].[ShopProducts] (
    [ProductId]       INT             NOT NULL,
    [ProductName]     NVARCHAR(100)   NOT NULL,
    [Sku]             VARCHAR(32)     NOT NULL,
    [UnitPrice]       DECIMAL(18,4)   NOT NULL,
    [IsDiscontinued]  BIT             NOT NULL,
    CONSTRAINT [PK_ShopProducts] PRIMARY KEY ([ProductId])
);
GO

CREATE TABLE [{{schema}}].[ShopOrders] (
    [CompanyId]    INT             NOT NULL,
    [OrderId]      INT             NOT NULL,
    [CustomerId]   INT             NOT NULL,
    [PlacedAt]     DATETIME2(3)    NOT NULL,
    [IsCancelled]  BIT             NOT NULL,
    CONSTRAINT [PK_ShopOrders] PRIMARY KEY ([CompanyId], [OrderId]),
    CONSTRAINT [FK_ShopOrders_ShopCustomers] FOREIGN KEY ([CustomerId])
        REFERENCES [{{schema}}].[ShopCustomers] ([CustomerId])
);
GO

CREATE TABLE [{{schema}}].[ShopOrderLines] (
    [CompanyId]    INT             NOT NULL,
    [OrderId]      INT             NOT NULL,
    [LineNumber]   INT             NOT NULL,
    [ProductId]    INT             NOT NULL,
    [Description]  NVARCHAR(200)   NOT NULL,
    [Quantity]     INT             NOT NULL,
    [UnitPrice]    DECIMAL(18,4)   NOT NULL,
    CONSTRAINT [PK_ShopOrderLines] PRIMARY KEY ([CompanyId], [OrderId], [LineNumber]),
    CONSTRAINT [FK_ShopOrderLines_ShopOrders] FOREIGN KEY ([CompanyId], [OrderId])
        REFERENCES [{{schema}}].[ShopOrders] ([CompanyId], [OrderId]),
    CONSTRAINT [FK_ShopOrderLines_ShopProducts] FOREIGN KEY ([ProductId])
        REFERENCES [{{schema}}].[ShopProducts] ([ProductId])
);
GO

CREATE TABLE [{{schema}}].[ShopOrderLineAllocations] (
    [CompanyId]         INT           NOT NULL,
    [OrderId]           INT           NOT NULL,
    [LineNumber]        INT           NOT NULL,
    [AllocationId]      INT           NOT NULL,
    [AllocatedQuantity] INT           NOT NULL,
    [Notes]             NVARCHAR(400) NULL,
    CONSTRAINT [PK_ShopOrderLineAllocations] PRIMARY KEY ([CompanyId], [OrderId], [LineNumber], [AllocationId]),
    CONSTRAINT [FK_ShopOrderLineAllocations_ShopOrderLines] FOREIGN KEY ([CompanyId], [OrderId], [LineNumber])
        REFERENCES [{{schema}}].[ShopOrderLines] ([CompanyId], [OrderId], [LineNumber])
);
GO

-- The walk-in customer has the key 0, which the join category's filter CustomerId > 0 is
-- there to exclude; the key column is an identity, as the entities of the domain state it,
-- so the values are inserted explicitly.
SET IDENTITY_INSERT [{{schema}}].[ShopCustomers] ON;
INSERT INTO [{{schema}}].[ShopCustomers] ([CustomerId], [Name], [Notes])
VALUES
    (0, N'Walk-in', NULL),
    (1, N'Alice',   NULL),
    (2, N'Bob',     N'Prefers email'),
    (3, N'Carol',   NULL);
SET IDENTITY_INSERT [{{schema}}].[ShopCustomers] OFF;
GO

INSERT INTO [{{schema}}].[ShopProducts] ([ProductId], [ProductName], [Sku], [UnitPrice], [IsDiscontinued])
VALUES
    (1, N'Widget',  'SKU-W1',  120.0000, 0),
    (2, N'W_Bolt',  'SKU-W2',   15.5000, 0),
    (3, N'Whistle', 'SKU-W3',   12.5000, 0),
    (4, N'Anvil',   'SKU-A4',  250.0000, 1),
    (5, N'Bracket', 'SKU-B5',   80.0000, 0),
    (6, N'Zither',  'SKU-Z6', 1250.5000, 0);
GO

INSERT INTO [{{schema}}].[ShopOrders] ([CompanyId], [OrderId], [CustomerId], [PlacedAt], [IsCancelled])
VALUES
    (1, 1, 1, '2024-11-05T09:30:00.000', 0),
    (1, 2, 2, '2025-02-14T14:05:30.250', 0),
    (1, 3, 0, '2025-03-01T08:00:00.000', 1),
    (2, 1, 3, '2024-12-31T23:59:59.999', 0),
    (2, 2, 1, '2025-06-30T12:00:00.000', 0),
    (2, 3, 0, '2023-01-15T10:10:10.100', 1);
GO

INSERT INTO [{{schema}}].[ShopOrderLines] ([CompanyId], [OrderId], [LineNumber], [ProductId], [Description], [Quantity], [UnitPrice])
VALUES
    (1, 1,  7, 1, N'Widget, boxed',        3,  120.0000),
    (1, 1,  8, 6, N'Zither',              12, 1250.5000),
    (1, 2,  1, 2, N'W_Bolt, bag of ten',  20,   15.5000),
    (1, 2,  2, 1, N'Widget, loose',        8,  118.0000),
    (1, 3,  5, 3, N'Whistle',              9,   12.5000),
    (2, 1,  3, 4, N'Anvil, standard',      1,   99.0000),
    (2, 1,  4, 6, N'Zither, left-handed',  1, 1300.0000),
    (2, 2,  6, 5, N'Bracket',              4,   80.0000),
    (2, 3,  9, 2, N'W_Bolt, single',       6,   15.5000),
    (2, 3, 10, 4, N'Anvil, heavy',         2,  300.0000);
GO

INSERT INTO [{{schema}}].[ShopOrderLineAllocations] ([CompanyId], [OrderId], [LineNumber], [AllocationId], [AllocatedQuantity], [Notes])
VALUES
    (1, 1,  7, 1,  3, NULL),
    (1, 1,  8, 1, 10, N'partial'),
    (1, 1,  8, 2,  2, NULL),
    (1, 2,  1, 1, 20, NULL),
    (2, 1,  3, 1,  1, NULL),
    (2, 3, 10, 1,  2, N'heavy');
GO
