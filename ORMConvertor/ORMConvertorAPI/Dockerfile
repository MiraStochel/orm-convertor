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
COPY CSharpEntityParsing/CSharpEntityParsing.csproj CSharpEntityParsing/
COPY DapperWrappers/DapperWrappers.csproj DapperWrappers/
COPY DatabaseCatalog/DatabaseCatalog.csproj DatabaseCatalog/
COPY EFCoreWrappers/EFCoreWrappers.csproj EFCoreWrappers/
COPY HibernateWrappers/HibernateWrappers.csproj HibernateWrappers/
COPY JakartaPersistence/JakartaPersistence.csproj JakartaPersistence/
COPY JavaEntityParsing/JavaEntityParsing.csproj JavaEntityParsing/
COPY LinqParsing/LinqParsing.csproj LinqParsing/
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

# Stage 3: Test runner (decision 039). It builds on the same compilation the application
# gets, so producing this image is itself a check that the solution builds on Linux.
# The test run happens when the container starts, not while the image is built - at build
# time there is no database service to talk to. Packages are therefore restored into the
# image rather than into a cache mount, so the run needs no network of its own.
FROM dotnet-build AS tests
WORKDIR /app
RUN dotnet restore Tests/Tests.csproj
RUN dotnet build Tests/Tests.csproj --configuration Release --no-restore
ENTRYPOINT ["dotnet", "test", "Tests/Tests.csproj", "--configuration", "Release", "--no-build"]

# Stage 4: Java test runner (decision 076). The Java suite is a Maven project the .NET
# solution knows nothing about, so this stage starts from the Maven image the tutorials
# ran in rather than from dotnet-build; a JDK exists nowhere else in the project. The same
# reasoning as for the tests stage applies: dependencies are resolved while the image is
# built, so the run - `mvn -o`, offline - needs no network, and the tests execute when the
# container starts, because only then is there a database. The schema script is copied to
# the same relative place it has in the checkout, where the pom reads it from.
FROM maven:3.9.11-eclipse-temurin-25-noble AS java-tests
WORKDIR /src/JavaTests
COPY JavaTests/pom.xml ./
# go-offline alone is not enough: it mediates transitive versions differently from the
# real build and left one artifact behind. Running the lifecycle over the bare pom - no
# sources yet, so nothing compiles and no test runs - resolves exactly the classpath the
# run will use, plugins and Surefire's provider included.
RUN mvn -B dependency:go-offline test
COPY JavaTests/src ./src
COPY Tests/Database/TestSchema.sql /src/Tests/Database/TestSchema.sql
RUN mvn -B -o test-compile
ENTRYPOINT ["mvn", "-B", "-o", "test"]

# Stage 5: Create runtime image
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
