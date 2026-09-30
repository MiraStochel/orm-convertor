#!/bin/bash
# Loads the LDBC SNB data set the image carries into the database LdbcSnb (decision 110).
# Called by init-db.sh once SQL Server accepts connections and WideWorldImporters is in
# place, so the sample database the application's catalog reads never waits for this one.
#
# The load runs once per data set. A finished load writes the name of the archive into the
# extended property ldbc.dataset of the database; a database without it - one a stopped
# container left half-loaded - or with another data set's name, because the image was
# built with another scale factor, is dropped and loaded again. The archive stays packed in
# the image and is unpacked into a temporary directory only for the load.
set -euo pipefail

scripts=/opt/ldbc
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -b -I)

archive=$(find "${scripts}" -maxdepth 1 -name '*.tar.zst' | head -n 1)
if [ -z "${archive}" ]; then
    echo "The image carries no LDBC data set (LDBC_SCALE_FACTOR=none); LdbcSnb is not created."
    exit 0
fi
dataset=$(basename "${archive}" .tar.zst)

loaded=$("${sqlcmd[@]}" -h -1 -W -Q "SET NOCOUNT ON; IF DB_ID('LdbcSnb') IS NOT NULL EXEC (N'SELECT CAST(value AS NVARCHAR(200)) FROM LdbcSnb.sys.extended_properties WHERE class = 0 AND name = N''ldbc.dataset''');")
if [ "${loaded}" = "${dataset}" ]; then
    echo "LdbcSnb already holds ${dataset}. Skipping the load."
    exit 0
fi

echo "Loading ${dataset} into LdbcSnb..."
work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

zstd -dc "${archive}" | tar -x -C "${work}"
data=$(find "${work}" -mindepth 1 -maxdepth 1 -type d | head -n 1)

for script in schema load constraints; do
    sed 's/{{schema}}/dbo/g' "${scripts}/${script}.sql" > "${work}/${script}.sql"
done

"${sqlcmd[@]}" -Q "IF DB_ID('LdbcSnb') IS NOT NULL BEGIN ALTER DATABASE LdbcSnb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE LdbcSnb; END; CREATE DATABASE LdbcSnb COLLATE SQL_Latin1_General_CP1_CI_AS; ALTER DATABASE LdbcSnb SET RECOVERY SIMPLE;"
"${sqlcmd[@]}" -d LdbcSnb -i "${work}/schema.sql"
"${sqlcmd[@]}" -d LdbcSnb -v DataPath="${data}" -i "${work}/load.sql"
"${sqlcmd[@]}" -d LdbcSnb -i "${work}/constraints.sql"
"${sqlcmd[@]}" -d LdbcSnb -Q "EXEC sys.sp_addextendedproperty @name = N'ldbc.dataset', @value = N'${dataset}';"

echo "LdbcSnb holds ${dataset}."
