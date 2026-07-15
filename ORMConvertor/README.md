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
`docker-compose.yml` in this directory starts two containers:

- `ormconvertor` – the application, built by the multi-stage `ORMConvertorAPI/Dockerfile` (compiles the Angular frontend, the native Advisor library `libadvisor.so`, and the .NET app). Exposed on [http://localhost:5072/orm/](http://localhost:5072/orm/).
- `mssql_db` – Microsoft SQL Server initialized with the WideWorldImporters sample database (`database.Dockerfile`), exposed on `localhost,1444` (`SA` / `Testingorms123` – development credentials only).

```sh
docker compose up --build
```

Note: the compose file currently runs the app with `ASPNETCORE_ENVIRONMENT=Development`, so the Swagger UI is available at `/orm/swagger`.

## PM2
`ecosystem.config.js` is a configuration for the [PM2](https://pm2.keymetrics.io/) process manager, used to run the app via `dotnet run` on a server without Docker: `pm2 start ecosystem.config.js`.

# Advisor prerequisites
The translation features work in any environment. The Advisor additionally needs:

- A reachable SQL Server with the WideWorldImporters database. The connection string is read from `ConnectionStrings:AdvisorDatabase` or `Advisor:ConnectionString` (via `appsettings.json` or environment variables, e.g. `ConnectionStrings__AdvisorDatabase`). Inside docker-compose this resolves to the `mssql_db` service automatically.
- The native GLPK wrapper `libadvisor.so`, which is currently built only inside the Docker image. Outside Linux/Docker the Advisor endpoints will fail, while the rest of the application works normally.

# Tests
Tests can be executed via Visual Studio's Test Explorer window or with a .NET CLI command:
```sh
dotnet test Tests/Tests.csproj --configuration Release
```
The output displays the results of succeeded and failed tests. Tests are also run automatically by a GitHub Actions pipeline – on pushes to `main` and on pull requests, whenever something inside `ORMConvertor/**` changes. The pipeline configuration is located in the `.github` folder at the root of the repository.

# Frontend
The Angular frontend is precompiled and served by the ASP.NET web server running the API. To prepare the frontend, its source files must be compiled and copied to the `wwwroot` directory, from which they are served as static files. This process is performed by executing the following commands in the `ORMConvertorAPI/frontend` directory.
```sh
npm install
ng build --configuration "production" --base-href "/orm/" --deploy-url "/orm/" && rmdir /s /q "..\wwwroot" && mkdir "..\wwwroot" && xcopy /s /e /y "dist\browser\*" "..\wwwroot\"
```

For Linux:
```sh
npm install
ng build --configuration "production" --base-href "/orm/" --deploy-url "/orm/" && rm -rf "../wwwroot" && mkdir "../wwwroot" && cp -r dist/browser/* ../wwwroot/
```

The frontend is served by the backend application, there is no need to initialize it separately.