-- Read-only data of the differential verification (decision 089). Both suites create it in
-- their own schema right after TestSchema.sql, from this one script, so the two halves of a
-- pair do not read the same rows but rows made by the same statements - which is all that
-- comparing results needs, and it avoids two runtimes sharing one schema.
--
-- It brings its own table rather than seeding one of the schema's. Seeded rows in a table
-- other tests write to would couple the two: a scenario that stores one row and asserts the
-- table holds one row would start failing because of data it never asked for. Nothing in a
-- differential scenario writes, so there is no transaction and no rollback either, and the
-- result of a query here is a function of the query alone.
--
-- The rows are chosen against the mutation list of decision 089, not for convenience. Each
-- of the five mutations has to change the outcome over this data, or the negative half
-- proves nothing:
--
--   dropping the filter        UnitPrice > 100 keeps four of the six rows and
--                              IsDiscontinued = 0 keeps four - neither is satisfied by all;
--   flipping the operator      > 100 and < 100 select disjoint, non-empty sets;
--   dropping the ordering      ProductId is the clustered key, so an unordered read comes
--                              back in its order; ProductName ascending is the exact
--                              reverse of it and Sku descending likewise, so no query of
--                              the matrix may order by ProductId - it would be ordered and
--                              unordered alike, and the mutation would pass unnoticed;
--   changing the row count     TOP (3) of six rows is a proper prefix;
--   swapping projected fields  Sku and ProductName differ in every row, so a projection
--                              read in the wrong order differs in every field.
--
-- Two more properties are deliberate. No column carries a default, so nothing unstated can
-- reach a canonical result. And every Weight is exactly representable in binary floating
-- point, so that the two ecosystems cannot disagree about a value before the renderer even
-- sees it - what the renderer does with the general case is the conformance test's
-- business, not the fixture's.

CREATE TABLE [{{schema}}].[DifferentialProducts] (
    [ProductId]      INT             NOT NULL,
    [ProductName]    NVARCHAR(100)   NOT NULL,
    [Sku]            VARCHAR(32)     NOT NULL,
    [UnitPrice]      DECIMAL(18,4)   NOT NULL,
    [Weight]         FLOAT           NULL,
    [IsDiscontinued] BIT             NOT NULL,
    CONSTRAINT [PK_DifferentialProducts] PRIMARY KEY ([ProductId])
);
GO

INSERT INTO [{{schema}}].[DifferentialProducts]
    ([ProductId], [ProductName], [Sku], [UnitPrice], [Weight], [IsDiscontinued])
VALUES
    (1, N'Zither',    'SKU-001', 1250.5000, 2.5,  0),
    (2, N'Yarn',      'SKU-002',   99.9900, NULL, 0),
    (3, N'Xylophone', 'SKU-003',  450.0000, 1.25, 1),
    (4, N'Whistle',   'SKU-004',   12.5000, 0.5,  0),
    (5, N'Violin',    'SKU-005', 2100.7500, 3.0,  1),
    (6, N'Ukulele',   'SKU-006',  320.2500, 1.75, 0);
GO
