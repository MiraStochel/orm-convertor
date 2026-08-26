[![ORMConvertor tests](https://github.com/MiraStochel/orm-convertor/actions/workflows/ormconvertor-tests.yml/badge.svg)](https://github.com/MiraStochel/orm-convertor/actions/workflows/ormconvertor-tests.yml)

# ORMConvertor

A tool for translating entities, mappings, and queries between .NET ORM frameworks (Dapper, NHibernate, EF Core) through a framework-agnostic intermediate representation, with a performance-aware advisor that recommends the best framework (or combination of frameworks) for a given query workload using ILP optimization over real benchmark measurements.

This repository continues the development of a prototype originally created by Milan Abrahám as part of his master thesis (see [Origin and attribution](#origin-and-attribution)). The fork has completed and extended the tool well beyond the original prototype — see [What the tool does](#what-the-tool-does) and [Beyond this version](#beyond-this-version).

## Repository structure

| Directory | Contents |
|---|---|
| `ORMConvertor/` | The translation and advisor tool: a .NET 10 solution — ASP.NET Core REST API serving a hand-written static web frontend (no framework, no build step). See [`ORMConvertor/README.md`](ORMConvertor/README.md) for build and run instructions. |
| `docs/` | Project documentation ([index](docs/README.md)): [how the tool works today](docs/architecture.md), [what remains](docs/open-items.md), [design decisions](docs/decisions/), [who uses it and for what](docs/use-cases.md), [where each requirement is met](docs/traceability.md), [what the tool is exposed to](docs/threat-model.md), [dated state reviews](docs/audits/), [framework analyses](docs/analysis/), and the [state at takeover](docs/baseline.md). |
| `benchmarks/` | Experimental comparison of seven .NET ORMs (unit tests and performance benchmarks) inherited from the original research. |
| `diagrams/` | Diagrams created with [draw.io](https://www.drawio.com/). |
| `notes/` | Research notes in Czech, inherited with the fork and kept as they were written: `comparison-research/` holds one note per framework filled against a shared questionnaire — `Template CZ.md` is that questionnaire itself, and therefore blank — and `abstrakce.md` collects the per-framework differences the intermediate representation has to carry, in bullet form rather than as a finished comparison. |

## What the tool does

- **Entity and mapping translation** between Dapper, NHibernate, and EF Core, in any direction, including composite primary keys, multi-column foreign keys, and many-to-many relationships generated as explicit junction entities. Parsers read framework-specific source code (C# via Roslyn, NHibernate XML via LINQ to XML) into a shared intermediate representation; builders generate code for the target framework from it.
- **Query translation in all nine directions** between the three frameworks (LINQ is read for EF Core and NHibernate, bare HQL for NHibernate as a second source language, SQL for Dapper; the generated queries are EF Core LINQ, NHibernate HQL, and Dapper SQL — so NHibernate → NHibernate round-trips textually over the bare HQL artifact), covering projection, filtering, joins, aggregation, ordering, paging, subqueries in conditions — `IN (SELECT …)`, `EXISTS`, and scalar-aggregate comparisons — and set operations — UNION, UNION ALL, INTERSECT, and EXCEPT. A shape the target cannot render faithfully refuses the artifact with a diagnostic record instead of approximating: HQL has no set operations, pagination that cannot be carried is never dropped silently, and a query that would return a different set of rows is not emitted at all.
- **Completion from the database catalog**: mapping facts the source does not state (typical for micro-ORMs like Dapper) are completed from the metadata of a connected database — columns and types, primary and foreign keys, junction tables — and every supplied fact is reported in the conversion diagnostics.
- **Structured diagnostics** for every conversion: facts the target cannot express, applied conventions, conflicts between sources, and incomplete input are returned as records alongside the generated artifacts, never lost silently.
- **Advisor**: given a set of entities and queries, translates the queries into candidate frameworks, compiles and benchmarks them against a live database (Roslyn dynamic compilation), and solves an ILP model (GLPK) to recommend a framework assignment under user constraints (framework count limit, memory budget, query weights). Currently supports Dapper and EF Core, and sits outside the guarantees below.

## Guarantees

This section is the authoritative statement of the boundary between what the tool vouches for and what merely ships in the repository, as it stands on `main`; a release freezes it together with the version number in `ORMConvertor/Directory.Build.props`, so a tag carries the boundary as it stood for that version. [`docs/architecture.md`](docs/architecture.md) §9 carries the same boundary in Czech together with the reasoning behind it, and is corrected against this section if the two ever differ.

**Covered:** entity, mapping, and query translation as described above, merging of multi-file input into one conversion with per-file input and output, completion from the database catalog, structured diagnostics of every conversion — stamped with a run identifier and the versions of the tool and of both frameworks — deterministic output, translation artifacts that never carry database credentials, translation performance measured against a stated limit, an architecture in which a new framework is a new self-contained wrapper, and a documented container configuration that both runs the system and reproduces the whole test suite on a machine with nothing installed but Docker.

Four of those hold in a narrower sense than the words alone suggest:

- A diagnostic record born from reading one input unit points at that unit — by the name the client sent with it, or by its position in the request. Records from the completion and generation phases name the entity and property instead, deliberately: an entity may legitimately be declared by several units, so a record about the merged entity belongs to no single file. Displaying the intermediate representation is not claimed.
- Syntactic correctness of generated files is proven by the test suite, not at run time: the translation path never compiles foreign code, and what every conversion does check is the completeness of the intermediate representation.
- Client-side validation is a helper, not a gate — it catches empty and mistyped input and malformed XML, while C# and SQL syntax errors come back from the server (SQL with line and column).
- The container configuration covers what the repository contains: the system, the database, and the .NET test project, not the Java test projects and experimental pipeline, which do not exist.

**Exempt from guarantees:** four areas, each excluded as a whole rather than case by case — the Advisor and its benchmarking infrastructure (untested; the native ILP library builds only inside Docker); inheritance, components, `<join>`, and everything else beyond a flat class in NHibernate mappings, `<natural-id>`, `<idbag>` and `<array>` among them; database dialects other than SQL Server — and with them `CHECK` constraints and column defaults, whose content is a literal SQL expression the intermediate representation does not carry (unique constraints do not belong here: they are carried and written by both annotation targets); and the Java ecosystem together with the experimental part of the assignment, which this version does not claim at all. Exempt is not the same as absent: such an area may well ship in the repository and run, the version simply promises nothing about it, and input that touches one is reported in the conversion records rather than silently degraded.

The remaining work is tracked in [`docs/open-items.md`](docs/open-items.md).

## Versioning and releases

Version numbers follow semantic versioning, and the public surface they describe is **the shape of the generated artifact** as much as the REST contract and the guarantee boundary above ([decision 041](docs/decisions/041-versioning-and-release.md)). For a tool whose product is generated code, that is the distinction a consumer actually needs:

- **MAJOR** — the same input now yields an artifact you have to adapt to, the REST contract breaks, or an area leaves the guarantee boundary.
- **MINOR** — something new arrives and existing output is untouched: another framework, another query category, another kind of diagnostic record, or an area entering the boundary.
- **PATCH** — a fix that changes the output only where it was wrong.

A release is an annotated tag on the commit that already carries the released number in `ORMConvertor/Directory.Build.props`, and the release notes live in the tag's annotation (`git tag -n99`) rather than in a changelog file. Tags are never moved and numbers are never reused: a release that turns out wrong is corrected by the next number. Pinned dependency versions move for exactly two reasons — a published advisory or the needs of ongoing work — and CI checks the first of those on every run and once a week.

The tag `1.0` predates this policy, uses a two-part number this policy would not issue, and does not contain the container configuration described above. Its own guarantees section is consistent about that — it lists containerized deployment among the areas it exempts. What briefly claimed the configuration was the working `README.md` on `main`, whose heading still read *Version 1.0 guarantees* after its body had already moved on; that mismatch is what prompted the policy. Under the rule just stated the tag is not moved: the release `1.1.0` corrects the situation instead, and is a MINOR step for a single reason — an area entered the boundary above, while the generated artifacts and the REST contract are unchanged.

## Getting started

The tool lives in the `ORMConvertor` directory. In short:

```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```

then open `http://localhost:5072/orm/`. The frontend is served as-is from `ORMConvertorAPI/wwwroot` — there is nothing to compile. On a machine with Docker, `docker compose up --build` from `ORMConvertor/` starts the application together with a SQL Server holding the sample database, and `docker compose --profile test run --rm tests` runs the whole test suite without a .NET SDK or a database of your own. For prerequisites and details, see [`ORMConvertor/README.md`](ORMConvertor/README.md).

## Benchmarks

The `benchmarks` directory contains the static and experimental comparison of seven .NET data-access frameworks that preceded the tool: [Dapper](https://github.com/DapperLib/Dapper), [PetaPoco](https://github.com/CollaboratingPlatypus/PetaPoco), [RepoDB](https://github.com/mikependon/RepoDB), [linq2db](https://github.com/linq2db/linq2db), [NHibernate](https://github.com/nhibernate), [Entity Framework Core](https://github.com/dotnet/efcore), and [Entity Framework 6](https://github.com/dotnet/ef6). Benchmarks run against the Microsoft SQL Server [WideWorldImporters](https://learn.microsoft.com/en-us/sql/samples/wide-world-importers-what-is) sample database; see [`benchmarks/README.md`](benchmarks/README.md) for setup.

The measured output of that comparison is **not versioned** ([decision 042](docs/decisions/042-measured-benchmark-output-out-of-git.md)): it is a single run from March 2025, and the exclusion covers the whole directory — the measurements and the R script that renders the charts from them, as much as the charts themselves. A clone therefore contains the benchmark projects and their description, not their results; earlier revisions still hold them, so `git show <commit>:benchmarks/results/…` retrieves any of it.

## Beyond this version

The main directions of future work (detailed in [`docs/open-items.md`](docs/open-items.md)):

- Extending the Advisor to all supported frameworks.
- Java ecosystem support (Hibernate, MyBatis, EclipseLink) and cross-ecosystem translation between .NET and Java.

## License

Released under the [MIT License](LICENSE) — use, modify and redistribute it freely, commercially included, as long as the licence text and the copyright notice travel with it.

The licence covers the contents of this repository. Third-party assets vendored under `ORMConvertor/ORMConvertorAPI/wwwroot/vendor/` keep their own terms, stated in the licence file shipped beside each of them.

## How to cite

Machine-readable citation metadata is in [`CITATION.cff`](CITATION.cff), which GitHub renders as the *Cite this repository* button. Please cite the version you used rather than `main`, which moves under the reader. The file also lists the publications describing the original prototype (see below), which is what you want to cite for the design of the approach itself.

## Origin and attribution

This repository is a fork of [`milan252525/orm-convertor`](https://github.com/milan252525/orm-convertor), which was created by **Milan Abrahám** as part of his [master thesis](https://is.cuni.cz/studium/dipl_st/index.php?id=&tid=&do=main&doo=detail&did=277574) at the Faculty of Mathematics and Physics, Charles University:

> Milan Abrahám: *Framework-Agnostic Query Adaptation: Ensuring SQL Compatibility Across .NET Database Frameworks*. Master thesis, Charles University, Prague, 2025.

The LaTeX sources of the thesis are available in the [`thesis` folder of the original repository](https://github.com/milan252525/orm-convertor/tree/main/thesis) (removed from this fork). The approach is also described in two papers by Milan Abrahám and Pavel Koupil:

> *ORMorpher: An Interactive Framework for ORM Translation and Optimization.* 40th International Conference on Automated Software Engineering (ASE 2025), Seoul, South Korea, 2025.
>
> *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection.* (Extended journal version.)

The tool is referred to as **ORMorpher** in the publications. Note that the papers describe the design and the state of the original prototype; where this repository has since diverged, [`docs/`](docs/) reflects the actual implementation.
