# syntax=docker/dockerfile:1.6

# Base images are pinned by digest beside a tag that names the exact version (decision 186):
# the tag is for the reader, the digest is what the build takes, and the two move together -
# by the pull request Dependabot opens, or by hand. The lock files restore in locked mode
# for the same reason: a transitive version that moved fails the build instead of entering it.

# Stage 1: Build native Advisor library
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS advisor-native
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
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS dotnet-build
WORKDIR /app

# Restore with project and lock files only so cache survives source edits
COPY ORMConvertor.sln Directory.Build.props Directory.Packages.props ./
COPY AbstractWrappers/AbstractWrappers.csproj AbstractWrappers/packages.lock.json AbstractWrappers/
COPY Advisor/Advisor.csproj Advisor/packages.lock.json Advisor/
COPY AdvisorBenchmarking/AdvisorBenchmarking.csproj AdvisorBenchmarking/packages.lock.json AdvisorBenchmarking/
COPY Common/Common.csproj Common/packages.lock.json Common/
COPY CSharpEntityParsing/CSharpEntityParsing.csproj CSharpEntityParsing/packages.lock.json CSharpEntityParsing/
COPY DapperWrappers/DapperWrappers.csproj DapperWrappers/packages.lock.json DapperWrappers/
COPY DatabaseCatalog/DatabaseCatalog.csproj DatabaseCatalog/packages.lock.json DatabaseCatalog/
COPY EFCoreWrappers/EFCoreWrappers.csproj EFCoreWrappers/packages.lock.json EFCoreWrappers/
COPY EclipseLinkWrappers/EclipseLinkWrappers.csproj EclipseLinkWrappers/packages.lock.json EclipseLinkWrappers/
COPY HibernateWrappers/HibernateWrappers.csproj HibernateWrappers/packages.lock.json HibernateWrappers/
COPY JakartaPersistence/JakartaPersistence.csproj JakartaPersistence/packages.lock.json JakartaPersistence/
COPY JavaEntityParsing/JavaEntityParsing.csproj JavaEntityParsing/packages.lock.json JavaEntityParsing/
COPY LinqBuilding/LinqBuilding.csproj LinqBuilding/packages.lock.json LinqBuilding/
COPY LinqParsing/LinqParsing.csproj LinqParsing/packages.lock.json LinqParsing/
COPY Model/Model.csproj Model/packages.lock.json Model/
COPY MyBatisWrappers/MyBatisWrappers.csproj MyBatisWrappers/packages.lock.json MyBatisWrappers/
COPY NHibernateWrappers/NHibernateWrappers.csproj NHibernateWrappers/packages.lock.json NHibernateWrappers/
COPY OrmConvertor/OrmConvertor.csproj OrmConvertor/packages.lock.json OrmConvertor/
COPY ORMConvertorAPI/ORMConvertorAPI.csproj ORMConvertorAPI/packages.lock.json ORMConvertorAPI/
COPY SampleData/SampleData.csproj SampleData/packages.lock.json SampleData/
COPY Tests/Tests.csproj Tests/packages.lock.json Tests/
COPY TransactSql/TransactSql.csproj TransactSql/packages.lock.json TransactSql/
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore ORMConvertorAPI/ORMConvertorAPI.csproj --locked-mode

# Bring in the remaining source content
COPY . .

# Restore again over the full source: a project the list above misses shows up here, and
# locked mode makes a lock file that disagrees with the pins fail the build
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore ORMConvertorAPI/ORMConvertorAPI.csproj --locked-mode

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
RUN dotnet restore Tests/Tests.csproj --locked-mode
RUN dotnet build Tests/Tests.csproj --configuration Release --no-restore
ENTRYPOINT ["dotnet", "test", "Tests/Tests.csproj", "--configuration", "Release", "--no-build"]

# Stage 4: Java test runner (decision 076). The Java suite is a Maven project the .NET
# solution knows nothing about, so this stage starts from the Maven image the tutorials
# ran in rather than from dotnet-build; a JDK exists nowhere else in the project. The same
# reasoning as for the tests stage applies: dependencies are resolved while the image is
# built, so the run - `mvn -o`, offline - needs no network, and the tests execute when the
# container starts, because only then is there a database.
FROM maven:3.9.11-eclipse-temurin-25-noble@sha256:407c4423cec0cf2981055bc2c6c0dc211d9605b6669279b95997f2d1c7e91e2c AS java-tests
WORKDIR /src/JavaTests
COPY JavaTests/pom.xml ./
# go-offline alone is not enough: it mediates transitive versions differently from the
# real build and left one artifact behind. Running the lifecycle over the bare pom - no
# sources yet, so nothing compiles and no test runs - resolves exactly the classpath the
# run will use, plugins and Surefire's provider included.
RUN mvn -B dependency:go-offline test
COPY JavaTests/src ./src
# The whole directory, and to the same relative place it has in the checkout, because that
# is where the pom reads it from. Not the files the pom names: Tests/Database is the one
# place a test resource shared with the .NET suite lives, and the pom's <testResources>
# already says which of it Maven takes. Naming them here too was a second, silent copy of
# that list - when decision 089 added Differential/ to the pom, this line still named only
# the schema, and the suite failed in the container while it was green on a checkout.
COPY Tests/Database/ /src/Tests/Database/
# The scripts of the LDBC inserts and their compensation (decision 117), likewise to the
# place the pom reads them from.
COPY database/ldbc/updates/ /src/database/ldbc/updates/
# The FIBEN catalog (decision 149), for the path of a foreign artifact that answers one of
# its queries without an instance of the tool (decision 172); likewise.
COPY SampleData/Fiben/queries.json /src/SampleData/Fiben/queries.json
# The schemas of the excerpted projects (decision 176), which the suite creates in the case-study
# database; the pom reads them from beside the excerpts, as the .NET suite embeds them.
COPY SampleData/ThirdParty/ /src/SampleData/ThirdParty/
RUN mvn -B -o test-compile
ENTRYPOINT ["mvn", "-B", "-o", "test"]

# Stage 5: Create runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
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
