# syntax=docker/dockerfile:1.6

# Stage 1: Build the Angular frontend
FROM node:22-alpine AS angular-build
WORKDIR /frontend

# Install dependencies with cached npm folder
COPY ORMConvertorAPI/frontend/package*.json ./
RUN --mount=type=cache,target=/root/.npm npm ci

# Copy app source files and build
COPY ORMConvertorAPI/frontend .
RUN npx ng build --configuration production --base-href /orm/ --deploy-url /orm/ --output-path=dist

# Stage 2: Build native Advisor library
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS advisor-native
WORKDIR /src/advisor
RUN --mount=type=cache,target=/var/lib/apt,sharing=locked,id=apt-lists \
    --mount=type=cache,target=/var/cache/apt,sharing=locked,id=apt-cache \
    apt-get update && \
    apt-get install -y --no-install-recommends gcc libglpk-dev \
    && mkdir -p /artifacts
COPY Advisor/ilp.c .
RUN gcc -fPIC -shared -o /artifacts/libadvisor.so ilp.c -lglpk

# Stage 3: Build and publish the .NET app, also bring in frontend static assets
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /app

# Restore with project files only so cache survives source edits
COPY ORMConvertor.sln ./
COPY AbstractWrappers/AbstractWrappers.csproj AbstractWrappers/
COPY Advisor/Advisor.csproj Advisor/
COPY AdvisorBenchmarking/AdvisorBenchmarking.csproj AdvisorBenchmarking/
COPY Common/Common.csproj Common/
COPY DapperWrappers/DapperWrappers.csproj DapperWrappers/
COPY EFCoreWrappers/EFCoreWrappers.csproj EFCoreWrappers/
COPY Model/Model.csproj Model/
COPY NHibernateWrappers/NHibernateWrappers.csproj NHibernateWrappers/
COPY OrmConvertor/OrmConvertor.csproj OrmConvertor/
COPY ORMConvertorAPI/ORMConvertorAPI.csproj ORMConvertorAPI/
COPY SampleData/SampleData.csproj SampleData/
COPY Tests/Tests.csproj Tests/
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore ORMConvertorAPI/ORMConvertorAPI.csproj

# Bring in the remaining source content
COPY . .

# Ensure packages restored with full source context (handles new dependencies)
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore ORMConvertorAPI/ORMConvertorAPI.csproj

# Inject frontend build output
COPY --from=angular-build /frontend/dist/browser ORMConvertorAPI/wwwroot

# Add the native library artifact
COPY --from=advisor-native /artifacts/libadvisor.so Advisor/libadvisor.so

# Publish the application
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish ORMConvertorAPI/ORMConvertorAPI.csproj -c Release -o /app/out --no-restore \
    && cp Advisor/libadvisor.so /app/out/

# Stage 4: Create runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install glpk runtime (so .so can be loaded)
RUN --mount=type=cache,target=/var/lib/apt,sharing=locked,id=apt-lists \
    --mount=type=cache,target=/var/cache/apt,sharing=locked,id=apt-cache \
    apt-get update && \
    apt-get install -y --no-install-recommends libglpk40

COPY --from=dotnet-build /app/out .
EXPOSE 5072
ENV ASPNETCORE_URLS=http://+:5072
ENTRYPOINT ["dotnet", "ORMConvertorAPI.dll"]
