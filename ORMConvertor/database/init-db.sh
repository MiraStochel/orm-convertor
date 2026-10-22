#!/bin/bash
set -euo pipefail

# The mode of the server (decision 164). Without the variables it is the measurement mode of
# decision 145: a fixed buffer pool, large enough to hold the LDBC data set at scale factor 1
# after warm-up, and single-threaded plans whose time does not depend on how many cores happen
# to be idle. The judge's mode (judge.env) keeps these and gives the container every processor
# of the VM instead. A value that is not a whole number stops the container before the server
# starts, rather than running it in a mode nobody chose.
maxdop="${DATABASE_MAXDOP:-1}"
max_server_memory="${DATABASE_MAX_SERVER_MEMORY_MB:-4096}"
for setting in "DATABASE_MAXDOP=${maxdop}" "DATABASE_MAX_SERVER_MEMORY_MB=${max_server_memory}"; do
    if ! [[ "${setting#*=}" =~ ^[0-9]+$ ]]; then
        echo "${setting%%=*} must be a whole number, not '${setting#*=}'."
        exit 1
    fi
done

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

# Set on every start, so that an existing volume gets the mode too; both are server settings
# and survive in master either way.
echo "Server settings: max degree of parallelism ${maxdop}, max server memory ${max_server_memory} MB."
/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -b \
    -Q "EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure 'max server memory (MB)', ${max_server_memory}; EXEC sp_configure 'max degree of parallelism', ${maxdop}; RECONFIGURE;"

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

# The FIBEN benchmark goes into a third database the same way (decision 149).
if ! /opt/fiben/load-fiben.sh; then
    echo "The FIBEN loader stopped before it finished; see its messages above for what Fiben holds."
fi

# Keep container running
wait "${sql_pid}"
