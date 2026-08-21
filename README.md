[![ORMConvertor tests](https://github.com/MiraStochel/orm-convertor/actions/workflows/ormconvertor-tests.yml/badge.svg)](https://github.com/MiraStochel/orm-convertor/actions/workflows/ormconvertor-tests.yml)

# ORMConvertor

A tool for translating entities, mappings, and queries between .NET ORM frameworks (Dapper, NHibernate, EF Core) through a framework-agnostic intermediate representation, with a performance-aware advisor that recommends the best framework (or combination of frameworks) for a given query workload using ILP optimization over real benchmark measurements.

This repository continues the development of a prototype originally created by Milan Abrahám as part of his master thesis (see [Origin and attribution](#origin-and-attribution)). The fork has completed and extended the tool well beyond the original prototype — see [What the tool does](#what-the-tool-does) and [Beyond version 1.0](#beyond-version-10).

## Repository structure

| Directory | Contents |
|---|---|
| `ORMConvertor/` | The translation and advisor tool: a .NET 10 solution — ASP.NET Core REST API serving a hand-written static web frontend (no framework, no build step). See [`ORMConvertor/README.md`](ORMConvertor/README.md) for build and run instructions. |
| `docs/` | Project documentation: [how the tool works today](docs/architecture.md), [what remains](docs/open-items.md), [design decisions](docs/decisions/), [dated state reviews](docs/audits/), [framework analyses](docs/analysis/), and the [state at takeover](docs/baseline.md). |
| `benchmarks/` | Experimental comparison of seven .NET ORMs (unit tests and performance benchmarks) inherited from the original research. |
| `diagrams/` | Diagrams created with [draw.io](https://www.drawio.com/). |
| `notes/` | Research notes in Czech comparing ORM frameworks feature by feature. |

## What the tool does

- **Entity and mapping translation** between Dapper, NHibernate, and EF Core, in any direction, including composite primary keys, multi-column foreign keys, and many-to-many relationships generated as explicit junction entities. Parsers read framework-specific source code (C# via Roslyn, NHibernate XML via LINQ to XML) into a shared intermediate representation; builders generate code for the target framework from it.
- **Query translation in all nine directions** between the three frameworks (LINQ is read for EF Core and NHibernate, SQL for Dapper; the generated queries are EF Core LINQ, NHibernate HQL, and Dapper SQL), covering projection, filtering, joins, aggregation, and ordering.
- **Completion from the database catalog**: mapping facts the source does not state (typical for micro-ORMs like Dapper) are completed from the metadata of a connected database — columns and types, primary and foreign keys, junction tables — and every supplied fact is reported in the conversion diagnostics.
- **Structured diagnostics** for every conversion: facts the target cannot express, applied conventions, conflicts between sources, and incomplete input are returned as records alongside the generated artifacts, never lost silently.
- **Advisor**: given a set of entities and queries, translates the queries into candidate frameworks, compiles and benchmarks them against a live database (Roslyn dynamic compilation), and solves an ILP model (GLPK) to recommend a framework assignment under user constraints (framework count limit, memory budget, query weights). Currently supports Dapper and EF Core, and sits outside the version 1.0 guarantees (see below).

## Version 1.0 guarantees

Version 1.0 draws an explicit line between what it vouches for and what merely ships in the repository. The authoritative statement of the boundary is [`docs/architecture.md`](docs/architecture.md), §9 (in Czech); in short:

**Covered:** entity, mapping, and query translation as described above, merging of multi-file input with per-file diagnostics, completion from the database catalog, structured diagnostics of every conversion, deterministic output, and translation artifacts that never carry database credentials.

**Exempt from guarantees:** the Advisor and its benchmarking infrastructure (untested; the native ILP library builds only inside Docker), inheritance, components, and `<join>` in NHibernate mappings, subqueries, set operations, and paging in queries, a textual NHibernate → NHibernate round-trip (there is no HQL parser), database dialects other than SQL Server, and containerized deployment. Input that touches an exempt area is reported in the conversion records rather than silently degraded.

The remaining work is tracked in [`docs/open-items.md`](docs/open-items.md).

## Getting started

The tool lives in the `ORMConvertor` directory. In short:

```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

then open `http://localhost:5072/orm/`. The frontend is served as-is from `ORMConvertorAPI/wwwroot` — there is nothing to compile. For prerequisites, Docker deployment, and running tests, see [`ORMConvertor/README.md`](ORMConvertor/README.md).

## Benchmarks

The `benchmarks` directory contains the static and experimental comparison of seven .NET data-access frameworks that preceded the tool: [Dapper](https://github.com/DapperLib/Dapper), [PetaPoco](https://github.com/CollaboratingPlatypus/PetaPoco), [RepoDB](https://github.com/mikependon/RepoDB), [linq2db](https://github.com/linq2db/linq2db), [NHibernate](https://github.com/nhibernate), [Entity Framework Core](https://github.com/dotnet/efcore), and [Entity Framework 6](https://github.com/dotnet/ef6). Benchmarks run against the Microsoft SQL Server [WideWorldImporters](https://learn.microsoft.com/en-us/sql/samples/wide-world-importers-what-is) sample database; see [`benchmarks/README.md`](benchmarks/README.md) for setup.

## Beyond version 1.0

The main directions of future work (detailed in [`docs/open-items.md`](docs/open-items.md)):

- Extending the Advisor to all supported frameworks.
- Java ecosystem support (Hibernate, MyBatis, EclipseLink) and cross-ecosystem translation between .NET and Java.
- The remaining query categories: paging, subqueries, and set operations.

## Origin and attribution

This repository is a fork of [`milan252525/orm-convertor`](https://github.com/milan252525/orm-convertor), which was created by **Milan Abrahám** as part of his [master thesis](https://is.cuni.cz/studium/dipl_st/index.php?id=&tid=&do=main&doo=detail&did=277574) at the Faculty of Mathematics and Physics, Charles University:

> Milan Abrahám: *Framework-Agnostic Query Adaptation: Ensuring SQL Compatibility Across .NET Database Frameworks*. Master thesis, Charles University, Prague, 2025.

The LaTeX sources of the thesis are available in the [`thesis` folder of the original repository](https://github.com/milan252525/orm-convertor/tree/main/thesis) (removed from this fork). The approach is also described in two papers by Milan Abrahám and Pavel Koupil:

> *ORMorpher: An Interactive Framework for ORM Translation and Optimization.* 40th International Conference on Automated Software Engineering (ASE 2025), Seoul, South Korea, 2025.
>
> *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection.* (Extended journal version.)

The tool is referred to as **ORMorpher** in the publications. Note that the papers describe the design and the state of the original prototype; where this repository has since diverged, [`docs/`](docs/) reflects the actual implementation.
