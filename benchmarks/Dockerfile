FROM mcr.microsoft.com/mssql/server:2022-latest

ENV ACCEPT_EULA=Y
ENV MSSQL_SA_PASSWORD=Testingorms123
ENV MSSQL_PID=Developer

USER root
RUN apt-get update && \
    apt-get install -y curl && \
    mkdir -p /var/opt/mssql/backup && \
    curl -L -o /var/opt/mssql/backup/WideWorldImporters-Full.bak \
    https://github.com/Microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImporters-Full.bak

USER mssql

CMD /opt/mssql/bin/sqlservr & \
    until /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "Testingorms123" -C -Q "SELECT 1" > /dev/null 2>&1; do \
        echo "Waiting for SQL Server to start..."; \
        sleep 2; \
    done && \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "Testingorms123" -C \
    -Q "RESTORE DATABASE WideWorldImporters FROM DISK = '/var/opt/mssql/backup/WideWorldImporters-Full.bak' WITH MOVE 'WWI_Primary' TO '/var/opt/mssql/data/WideWorldImporters.mdf', MOVE 'WWI_UserData' TO '/var/opt/mssql/data/WideWorldImporters_UserData.ndf', MOVE 'WWI_Log' TO '/var/opt/mssql/data/WideWorldImporters.ldf', MOVE 'WWI_InMemory_Data_1' TO '/var/opt/mssql/data/WideWorldImporters_InMemory.ndf';" && \
    wait

# Health check to ensure SQL Server is up
HEALTHCHECK --interval=10s --start-period=60s \
    CMD /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "Testingorms123" -C -Q "SELECT 1" || exit 1
