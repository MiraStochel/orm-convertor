[![ORMConvertor tests](https://github.com/MiraStochel/orm-convertor/actions/workflows/ormconvertor-tests.yml/badge.svg)](https://github.com/MiraStochel/orm-convertor/actions/workflows/ormconvertor-tests.yml)

# ORMConvertor

A tool for translating entities, mappings, and queries between .NET ORM frameworks (Dapper, NHibernate, EF Core) through a framework-agnostic intermediate representation, with a performance-aware advisor that recommends the best framework (or combination of frameworks) for a given query workload using ILP optimization over real benchmark measurements.

This repository continues the development of a prototype originally created by Milan Abrahám as part of his master thesis (see [Origin and attribution](#origin-and-attribution)). The goal of this fork is to complete and extend the tool well beyond the original prototype — see [Roadmap](#roadmap).

## Repository structure

| Directory | Contents |
|---|---|
| `ORMConvertor/` | The translation and advisor tool: .NET 10 solution (ASP.NET Core REST API + Angular frontend). See [`ORMConvertor/README.md`](ORMConvertor/README.md) for build and run instructions. |
| `docs/` | Project documentation: [current architecture](docs/architecture.md), [implementation status and gaps](docs/current-state.md), and [design documents](docs/design/) for planned extensions. |
| `benchmarks/` | Experimental comparison of seven .NET ORMs (unit tests and performance benchmarks) inherited from the original research. |
| `diagrams/` | Diagrams created with [draw.io](https://www.drawio.com/). |
| `notes/` | Research notes in Czech comparing ORM frameworks feature by feature. |

## What the tool does

- **Entity and mapping translation** between Dapper, NHibernate, and EF Core, in any direction. Parsers read framework-specific source code (C# via Roslyn, NHibernate XML via LINQ to XML) into a shared intermediate representation; builders generate code for the target framework from it.
- **Query translation**, currently one direction: EF Core LINQ → Dapper SQL.
- **Advisor**: given a set of entities and queries, translates the queries into candidate frameworks, compiles and benchmarks them against a live database (Roslyn dynamic compilation), and solves an ILP model (GLPK) to recommend a framework assignment under user constraints (framework count limit, memory budget, query weights). Currently supports Dapper and EF Core.

The current implementation status, including known limitations and missing features, is tracked in [`docs/current-state.md`](docs/current-state.md).

## Getting started

The tool lives in the `ORMConvertor` directory. In short:

```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

then open `http://localhost:5072/orm/`. For prerequisites, frontend compilation, Docker deployment, and running tests, see [`ORMConvertor/README.md`](ORMConvertor/README.md).

## Benchmarks

The `benchmarks` directory contains the static and experimental comparison of seven .NET data-access frameworks that preceded the tool: [Dapper](https://github.com/DapperLib/Dapper), [PetaPoco](https://github.com/CollaboratingPlatypus/PetaPoco), [RepoDB](https://github.com/mikependon/RepoDB), [linq2db](https://github.com/linq2db/linq2db), [NHibernate](https://github.com/nhibernate), [Entity Framework Core](https://github.com/dotnet/efcore), and [Entity Framework 6](https://github.com/dotnet/ef6). Benchmarks run against the Microsoft SQL Server [WideWorldImporters](https://learn.microsoft.com/en-us/sql/samples/wide-world-importers-what-is) sample database; see [`benchmarks/README.md`](benchmarks/README.md) for setup.

## Roadmap

The main directions of ongoing work (detailed in [`docs/current-state.md`](docs/current-state.md) and [`docs/design/`](docs/design/)):

- Full query parser/builder coverage for all three supported frameworks (NHibernate LINQ, Dapper SQL parsing, EF Core and NHibernate query generation).
- A proper boolean condition tree for WHERE/HAVING/JOIN clauses (replacing the current flat AND-only model).
- Composite primary keys and multi-column foreign key relationships (1:1, 1:N, N:M via junction entities).
- Database metadata enrichment: completing incomplete mappings (typical for micro-ORMs) from the database catalog.
- Extending the Advisor to all supported frameworks.

## Origin and attribution

This repository is a fork of [`milan252525/orm-convertor`](https://github.com/milan252525/orm-convertor), which was created by **Milan Abrahám** as part of his [master thesis](https://is.cuni.cz/studium/dipl_st/index.php?id=&tid=&do=main&doo=detail&did=277574) at the Faculty of Mathematics and Physics, Charles University:

> Milan Abrahám: *Framework-Agnostic Query Adaptation: Ensuring SQL Compatibility Across .NET Database Frameworks*. Master thesis, Charles University, Prague, 2025.

The LaTeX sources of the thesis are available in the [`thesis` folder of the original repository](https://github.com/milan252525/orm-convertor/tree/main/thesis) (removed from this fork). The approach is also described in two papers by Milan Abrahám and Pavel Koupil:

> *ORMorpher: An Interactive Framework for ORM Translation and Optimization.* 40th International Conference on Automated Software Engineering (ASE 2025), Seoul, South Korea, 2025.
>
> *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection.* (Extended journal version.)

The tool is referred to as **ORMorpher** in the publications. Note that the papers describe the design and the state of the original prototype; where this repository has since diverged, [`docs/`](docs/) reflects the actual implementation.