#!/bin/bash
# Loads the LDBC SNB data set the image carries into the database LdbcSnb (decision 110), and
# beside it the validation set of the same scale factor (decision 117). Called by init-db.sh
# once SQL Server accepts connections and WideWorldImporters is in place, so the sample
# database the application's catalog reads never waits for this one.
#
# Each part loads once. A finished load of the data writes the name of the archive into the
# extended property ldbc.dataset of the database; a database without it - one a stopped
# container left half-loaded - or with another data set's name, because the image was built
# with another scale factor, is dropped and loaded again. A finished load of the validation
# set writes the name of its file into ldbc.validation; a database that has the data and not
# the set gets the set alone, without loading the data again. Both files stay packed in the
# image and are unpacked into a temporary directory only for the load.
set -euo pipefail

scripts=/opt/ldbc
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -b -I)

archive=$(find "${scripts}" -maxdepth 1 -name '*.tar.zst' | head -n 1)
if [ -z "${archive}" ]; then
    echo "The image carries no LDBC data set (LDBC_SCALE_FACTOR=none); LdbcSnb is not created."
    exit 0
fi
dataset=$(basename "${archive}" .tar.zst)

property() {
    "${sqlcmd[@]}" -h -1 -W -Q "SET NOCOUNT ON; IF DB_ID('LdbcSnb') IS NOT NULL EXEC (N'SELECT CAST(value AS NVARCHAR(200)) FROM LdbcSnb.sys.extended_properties WHERE class = 0 AND name = N''$1''');"
}

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

if [ "$(property ldbc.dataset)" = "${dataset}" ]; then
    echo "LdbcSnb already holds ${dataset}. Skipping the load."
else
    echo "Loading ${dataset} into LdbcSnb..."
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
    rm -rf "${data}"

    echo "LdbcSnb holds ${dataset}."
fi

validation=$(find "${scripts}" -maxdepth 1 -name 'validation_params-*.csv.zst' | head -n 1)
if [ -z "${validation}" ]; then
    echo "The image carries no validation set for this scale factor; LdbcSnb has no ValidationOperation."
    exit 0
fi
set_name=$(basename "${validation}" .csv.zst)

if [ "$(property ldbc.validation)" = "${set_name}" ]; then
    echo "LdbcSnb already holds the validation set ${set_name}. Skipping its load."
    exit 0
fi

echo "Loading the validation set ${set_name} into LdbcSnb..."
zstd -dc "${validation}" > "${work}/${set_name}.csv"
sed 's/{{schema}}/dbo/g' "${scripts}/validation.sql" > "${work}/validation.sql"

"${sqlcmd[@]}" -d LdbcSnb -v ValidationFile="${work}/${set_name}.csv" -i "${work}/validation.sql"
"${sqlcmd[@]}" -d LdbcSnb -Q "IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'ldbc.validation') EXEC sys.sp_updateextendedproperty @name = N'ldbc.validation', @value = N'${set_name}'; ELSE EXEC sys.sp_addextendedproperty @name = N'ldbc.validation', @value = N'${set_name}';"

echo "LdbcSnb holds the validation set ${set_name}."
