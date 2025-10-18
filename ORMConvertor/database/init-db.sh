#!/bin/bash
set -euo pipefail

# Start SQL Server in background
/opt/mssql/bin/sqlservr &
sql_pid=$!

# Wait for SQL Server to accept connections
until /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" >/dev/null 2>&1; do
    echo "Waiting for SQL Server to start..."
    sleep 2
done

db_path="/var/opt/mssql/data/WideWorldImporters.mdf"

if [ ! -f "${db_path}" ]; then
    echo "Restoring WideWorldImporters database..."
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C \
        -Q "RESTORE DATABASE WideWorldImporters FROM DISK = '/var/opt/mssql/backup/WideWorldImporters-Full.bak' WITH MOVE 'WWI_Primary' TO '/var/opt/mssql/data/WideWorldImporters.mdf', MOVE 'WWI_UserData' TO '/var/opt/mssql/data/WideWorldImporters_UserData.ndf', MOVE 'WWI_Log' TO '/var/opt/mssql/data/WideWorldImporters.ldf', MOVE 'WWI_InMemory_Data_1' TO '/var/opt/mssql/data/WideWorldImporters_InMemory.ndf';"
else
    echo "WideWorldImporters already present. Skipping restore."
fi

# Keep container running
wait "${sql_pid}"
