A web-based tool for translating across different .NET ORMs.

# Deployment

## Visual Studio
Open the solution file `ORMConvertor.sln` and set `ORMConvertorAPI` as the startup project, if not selected by default. The app can be launched in `Debug` configuration for development or compiled in `Release` configuration for production. Three launch profiles are defined in `ORMConvertorAPI/Properties/launchSettings.json` (`http`, `https`, `IIS Express`), but only `http` has been configured and tested. The application can be started with `CTRL + F5` to run without debugging or `F5` to launch with the debugger. A browser window should open automatically.

## .NET CLI
```sh
dotnet run --configuration Release --launch-profile http --project ORMConvertorAPI/ORMConvertorAPI.csproj
```
This approach does not open a browser automatically. Instead, the local URL is printed to the console (typically [http://localhost:5072/orm/](http://localhost:5072/orm/)).

## Docker (application + database)
`docker-compose.yml` in this directory describes the whole environment — the system and the test suite alike — and a profile keeps the two apart, so the command below never starts the test services.

```sh
docker compose up --build
```

starts two containers:

- `ormconvertor` – the application, built by the multi-stage `ORMConvertorAPI/Dockerfile` (compiles the native Advisor library `libadvisor.so` and the .NET app; the static frontend needs no build and enters the image as-is inside `wwwroot`). Exposed on [http://localhost:5072/orm/](http://localhost:5072/orm/). It receives both connection strings — `ConnectionStrings__AdvisorDatabase` for the Advisor's benchmarking and `ConnectionStrings__CatalogDatabase` for the catalog completion phase — so the containerized instance translates with the catalog rather than without it.
- `mssql_db` – Microsoft SQL Server 2022 initialized with the WideWorldImporters sample database (`database.Dockerfile`), exposed on `localhost,1444` (`SA` / `Testingorms123` – development credentials only).

Note: the compose file runs the app with `ASPNETCORE_ENVIRONMENT=Development`, so the Swagger UI is available at `/orm/swagger`.

## PM2
`ecosystem.config.js` is a configuration for the [PM2](https://pm2.keymetrics.io/) process manager, used to run the app via `dotnet run` on a server without Docker: `pm2 start ecosystem.config.js`.

# Advisor prerequisites
The translation features work in any environment. The Advisor additionally needs:

- A reachable SQL Server with the WideWorldImporters database. The connection string is read from `ConnectionStrings:AdvisorDatabase` or `Advisor:ConnectionString` (via `appsettings.json` or environment variables, e.g. `ConnectionStrings__AdvisorDatabase`). Inside docker-compose this resolves to the `mssql_db` service automatically.
- The native GLPK wrapper `libadvisor.so`, which is currently built only inside the Docker image. Outside Linux/Docker the Advisor endpoints will fail, while the rest of the application works normally.

# Tests

## In a container — one command, nothing but Docker required

```sh
docker compose --profile test run --rm tests
```

This builds the solution, starts a SQL Server of its own, waits for it, creates the database and runs the whole suite. The first run also pulls the images and builds them, which takes a few minutes; later runs reuse the layer cache. A clean machine needs neither the .NET SDK nor a database of its own (requirement S5). The `test` profile adds three services: `test_db` (a plain SQL Server 2022 — the tests create their own schema in it and never touch WideWorldImporters), `test_db_init` (creates the `ORMConvertorTests` database once the server answers, because the test fixture creates a schema, not a database) and `tests` (the `tests` stage of `ORMConvertorAPI/Dockerfile`).

## On the host

```sh
dotnet test Tests/Tests.csproj --configuration Release
```

Tests can also be run from Visual Studio's Test Explorer window. Database-dependent tests need a connection string, which is never stored in the repository: put it in the test project's user secrets as `ConnectionStrings:TestDatabase`, or pass the environment variable `ConnectionStrings__TestDatabase`, for example

```
Server=(localdb)\MSSQLLocalDB;Database=ORMConvertorTests;Trusted_Connection=True;TrustServerCertificate=True
```

The tests create and drop their own schema (`ormconvertor_test` by default; override with `ORMCONVERTOR_TEST_SCHEMA`), so any reachable SQL Server will do. Without a connection string those tests skip and say why, while everything else still runs. Where the environment states that it does provide a database — `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1`, set by the compose `test` profile and by CI — a missing database is a failure instead of a skip, so a run that quietly skipped everything database-dependent cannot pass as green.

## Continuous integration
A GitHub Actions pipeline runs the suite on pushes to `main` and on pull requests, whenever something inside `ORMConvertor/**` changes. It starts SQL Server 2022 as a service container, creates the test database and runs with the database required, so the database-dependent tests run there too. The pipeline configuration is located in the `.github` folder at the root of the repository.

# Frontend
The frontend is a set of hand-written static pages in `ORMConvertorAPI/wwwroot` — HTML, native ES modules, and CSS with no framework, no npm, and no build step. What is committed is exactly what the browser runs, so there is nothing to compile or copy; the ASP.NET application serves the files directly under `/orm/`. Third-party assets (Pico CSS, highlight.js) are vendored under `wwwroot/vendor/` with their versions and licenses.
