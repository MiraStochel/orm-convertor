#!/bin/bash
set -euo pipefail

# Start SQL Server in background
/opt/mssql/bin/sqlservr &
sql_pid=$!

# Wait until SQL Server has started. A login is not the signal: the server accepts logins
# while it is still converting its system databases and before it has started the databases
# of the volume, and in that window the catalog lists a database it has not started yet as
# online while a read of it fails with error 904. The signal is the line "Recovery is
# complete", which the server writes into the errorlog once every database is started. The
# login comes first because the errorlog is rotated at process start and, before that, still
# belongs to the previous start.
until /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" >/dev/null 2>&1; do
    echo "Waiting for SQL Server to accept connections..."
    sleep 2
done
until grep -q "Recovery is complete" /var/opt/mssql/log/errorlog 2>/dev/null; do
    echo "Waiting for SQL Server to start its databases..."
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

# The LDBC data set goes into a database of its own after the sample database is in place
# (decision 110). A loader that stops says why and leaves LdbcSnb as it found it - loaded,
# missing or half-loaded; it does not take down the server that the application and
# WideWorldImporters depend on.
if ! /opt/ldbc/load-ldbc.sh; then
    echo "The LDBC loader stopped before it finished; see its messages above for what LdbcSnb holds."
fi

# Keep container running
wait "${sql_pid}"
