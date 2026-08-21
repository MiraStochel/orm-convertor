# syntax=docker/dockerfile:1.6

# Stage 1: Build native Advisor library
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS advisor-native
WORKDIR /src/advisor
RUN --mount=type=cache,target=/var/lib/apt,sharing=locked,id=apt-lists \
    --mount=type=cache,target=/var/cache/apt,sharing=locked,id=apt-cache \
    apt-get update && \
    apt-get install -y --no-install-recommends gcc libglpk-dev \
    && mkdir -p /artifacts
COPY Advisor/ilp.c .
RUN gcc -fPIC -shared -o /artifacts/libadvisor.so ilp.c -lglpk

# Stage 2: Build and publish the .NET app; the static frontend travels inside wwwroot
# with the source copy (decision 032) - there is no frontend build step
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /app

# Restore with project files only so cache survives source edits
COPY ORMConvertor.sln Directory.Build.props Directory.Packages.props ./
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

# Add the native library artifact
COPY --from=advisor-native /artifacts/libadvisor.so Advisor/libadvisor.so

# Publish the application
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish ORMConvertorAPI/ORMConvertorAPI.csproj -c Release -o /app/out --no-restore \
    && cp Advisor/libadvisor.so /app/out/

# Stage 3: Create runtime image
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
