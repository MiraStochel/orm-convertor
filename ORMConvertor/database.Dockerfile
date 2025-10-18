FROM mcr.microsoft.com/mssql/server:2022-latest

ENV ACCEPT_EULA=Y \
    MSSQL_SA_PASSWORD=Testingorms123 \
    MSSQL_PID=Developer

USER root
RUN --mount=type=cache,target=/var/lib/apt \
    --mount=type=cache,target=/var/cache/apt \
    apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* && \
    mkdir -p /var/opt/mssql/backup && \
    curl -L -o /var/opt/mssql/backup/WideWorldImporters-Full.bak \
    https://github.com/Microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImporters-Full.bak

COPY database/init-db.sh /usr/local/bin/init-db.sh
RUN chmod +x /usr/local/bin/init-db.sh

USER mssql

ENTRYPOINT ["/usr/local/bin/init-db.sh"]

# Health check to ensure SQL Server is up
HEALTHCHECK --interval=10s --start-period=60s \
    CMD /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" || exit 1
