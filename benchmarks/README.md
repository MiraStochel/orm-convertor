Visual Studio solution (``ORMComparison.sln``) containing unit and benchmark tests for 7 different .NET ORMs.

Visual Studio Test Explorer window can be used to run unit tests.

To run performance benchmarks, set `BenchmarkMain` as a startup project. Configuration must be set to `Release`. Then start the project without debugging (CTRL + F5). A console window will appear. It contains instructions on how to target specific benchmarks. To run all, type in an asterisk (*) and press enter. This will trigger a full run, which takes approximately one hour to finish. You can switch to the test configuration which does only a few numbers of iterations in `BenchmarkMain/Program.cs` file. 

Console command for running benchmarks is `dotnet run --configuration Release --project BenchmarkMain\BenchmarkMain.csproj`. To run with a shorter test configuration, append `-- --testb` at the end of the command.

Tests will not execute without a database running locally. The instructions to start an instance are below.

# Database setup
Dockerfile is provided to initialize Microsoft SQL Server database and fill it with data. Tested using Podman and Docker.

Build image:
`podman build -t orm-comparison .`

Run container: 
`podman run -d --name orm-comparison -p 1444:1433 orm-comparison`

Test connection:
`sqlcmd -S 127.0.0.1,1444 -U SA -P Testingorms123 -Q "SELECT * FROM [WideWorldImporters].[Purchasing].[PurchaseOrders]"`

# Measured results

`results/` is **not versioned** ([decision 042](../docs/decisions/042-measured-benchmark-output-out-of-git.md)): a clone contains the benchmark projects and this description, not their output, and the output of a future run does not go back into git either. The exclusion is whole-directory, so the measurements, the summaries and `BuildPlots.R` are outside git along with the rendered charts. Nothing is lost: every one of those files stays in earlier revisions, and `git show <commit>:benchmarks/results/…` retrieves any of it.

Where the directory is present on disk, it holds a single benchmark run from **2025-03-06**, inherited from the original research: `joined/` is the full cross-framework run (BenchmarkDotNet reports plus the plots `BuildPlots.R` derives from `-measurements.csv`), `separate/` the per-framework reports.

**These numbers do not describe the code in this solution as it stands today.** The run predates the migration to .NET 10, and every measured framework except PetaPoco has been raised since:

| | Run of 2025-03-06 | This solution today |
|---|---|---|
| Target framework | net8.0 (runtime 8.0.11, SDK 9.0.101) | net10.0 |
| BenchmarkDotNet | 0.14.0 | 0.15.8 |
| Dapper | 2.1.35 | 2.1.79 |
| Entity Framework Core | 9.0.0 | 10.0.10 |
| Entity Framework 6 | 6.5.1 | 6.5.2 |
| NHibernate | 5.5.2 | 5.7.0 |
| linq2db | 5.4.1 | 6.3.0 |
| RepoDB | 1.13.1 | 1.14.0 |
| PetaPoco | 6.0.683 | 6.0.683 |
| Microsoft.Data.SqlClient | 5.2.2 | 7.0.2 |
| System.Data.SqlClient | 4.9.0 | 4.9.1 |

It was measured on one machine (AMD Ryzen 5 5600H, 6 physical cores, Windows 11 23H2) with the database on the same host, and it has not been repeated.

One caveat applies to the suite itself rather than to this run: the ADO.NET provider is not uniform, and the split is four frameworks to three. Dapper, EF Core, linq2db and RepoDB use `Microsoft.Data.SqlClient` — Dapper and linq2db by a direct package reference, EF Core and RepoDB through their SQL Server provider packages. NHibernate (through `db.Driver<SqlClientDriver>()`), EF6 and PetaPoco use `System.Data.SqlClient`, which reaches all three through `Common`, whose own code never opens a connection itself. PetaPoco is the case that used to be untraced, and it resolves by elimination: `PetaPoco.Compiled` 6.0.683 declares no provider dependency of its own, and `Microsoft.Data.SqlClient` appears nowhere in the package graph of either PetaPoco project, so the `SqlServerDatabaseProvider` both of them configure has nothing but `System.Data.SqlClient` to bind to. One reference muddies the reading without changing it: `EF6Features` names `Microsoft.Data.SqlClient` directly, but no code in that project uses it, and `EF6Performance` — the project that produces the measured numbers — does not reference it at all. This does not affect the feature tests, but in the performance comparison it is a confound.

Treat the run as a historical record, quoted together with the versions above. A figure describing the current state of the solution needs a fresh run.

# Sources
- [Wide World Importers sample databases for Microsoft SQL](https://learn.microsoft.com/en-us/sql/samples/wide-world-importers-what-is?view=sql-server-ver16)
- [BAK file with WWI database](https://github.com/microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImporters-Full.bak)
- [Microsoft SQL Server image on Docker Hub](https://hub.docker.com/r/microsoft/mssql-server)
