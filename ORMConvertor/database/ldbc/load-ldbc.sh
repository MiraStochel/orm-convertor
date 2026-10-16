#!/bin/bash
# Loads the LDBC SNB data set the image carries into the database LdbcSnb (decision 110), and
# beside it the validation set of the same scale factor (decision 117). Called by init-db.sh
# once SQL Server has started every database of the volume and WideWorldImporters is in
# place, so the sample database the application's catalog reads never waits for this one.
#
# Each part loads once. A finished load of the data writes the name of the archive into the
# extended property ldbc.dataset of the database; a database without it - one a stopped
# container left half-loaded - or with another data set's name, because the image was built
# with another scale factor, is dropped and loaded again. The indexes follow indexes.sql,
# whatever revision loaded the data: the script is rerunnable and runs whenever its checksum
# is not the one the extended property ldbc.indexes records, so a volume loaded before an
# index changed (decision 121) gets the change on the next start without loading the data
# again. A finished load of the validation set writes the name of its file into
# ldbc.validation; a database that has the data and not the set gets the set alone. Both
# files stay packed in the image and are unpacked into a temporary directory only for the load.
#
# Every step rests on a question to the database, and a question the server does not answer
# ends the script with the database as it was. An empty or failed answer must never read as
# "no data set", because that is what drops a loaded database - and the server answers
# wrongly while it is still starting: its catalog lists a database it has not started yet as
# online, and a read of it fails with error 904. That is why init-db.sh waits for the whole
# start before calling this script, and why nothing here infers a state from a failure.
set -euo pipefail

scripts=/opt/ldbc
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -b -I)

archive=$(find "${scripts}" -maxdepth 1 -name '*.tar.zst' | head -n 1)
if [ -z "${archive}" ]; then
    echo "The image carries no LDBC data set (LDBC_SCALE_FACTOR=none); LdbcSnb is not created."
    exit 0
fi
dataset=$(basename "${archive}" .tar.zst)

# Asks what the extended property $1 of LdbcSnb holds: MISSING when there is no such database,
# NONE when the database has no such property, otherwise the value. -r1 keeps error text off
# stdout, so what comes back is the answer or nothing; a failed or empty answer ends the script.
property() {
    local answer
    if ! answer=$("${sqlcmd[@]}" -r1 -h -1 -W -Q "SET NOCOUNT ON; IF DB_ID(N'LdbcSnb') IS NULL SELECT N'MISSING' ELSE EXEC (N'SELECT ISNULL((SELECT CAST(value AS NVARCHAR(200)) FROM LdbcSnb.sys.extended_properties WHERE class = 0 AND name = N''$1''), N''NONE'')');") || [ -z "${answer}" ]; then
        echo "LdbcSnb did not answer what $1 holds; leaving it as it is." >&2
        return 1
    fi
    printf '%s\n' "${answer}"
}

set_property() {
    "${sqlcmd[@]}" -d LdbcSnb -Q "IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'$1') EXEC sys.sp_updateextendedproperty @name = N'$1', @value = N'$2'; ELSE EXEC sys.sp_addextendedproperty @name = N'$1', @value = N'$2';"
}

# A load interrupted between taking the database single-user and dropping it leaves it
# single-user; open it again before asking it anything.
access=$("${sqlcmd[@]}" -r1 -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL((SELECT user_access_desc FROM sys.databases WHERE name = N'LdbcSnb'), N'MULTI_USER');") || exit 1
if [ "${access}" != "MULTI_USER" ]; then
    echo "LdbcSnb was left ${access}; opening it again."
    "${sqlcmd[@]}" -Q "ALTER DATABASE LdbcSnb SET MULTI_USER WITH ROLLBACK IMMEDIATE;"
fi

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

for script in schema load constraints indexes validation; do
    sed 's/{{schema}}/dbo/g' "${scripts}/${script}.sql" > "${work}/${script}.sql"
done

loaded=$(property ldbc.dataset) || exit 1
if [ "${loaded}" = "${dataset}" ]; then
    echo "LdbcSnb already holds ${dataset}. Skipping the load."
else
    echo "Loading ${dataset} into LdbcSnb (found: ${loaded})..."
    zstd -dc "${archive}" | tar -x -C "${work}"
    data=$(find "${work}" -mindepth 1 -maxdepth 1 -type d | head -n 1)

    # A load that failed between DROP and CREATE leaves the files of the old database behind,
    # and CREATE DATABASE refuses to overwrite them; without the database they are nothing.
    if [ "${loaded}" = "MISSING" ]; then
        rm -f /var/opt/mssql/data/LdbcSnb.mdf /var/opt/mssql/data/LdbcSnb_log.ldf
    fi
    # XACT_ABORT ends the batch at the first failed statement, so a database that could not be
    # taken single-user is not dropped - a DROP of a database the server has not opened leaves
    # its files on disk, and the CREATE after it fails on them.
    "${sqlcmd[@]}" -Q "SET XACT_ABORT ON; IF DB_ID(N'LdbcSnb') IS NOT NULL BEGIN ALTER DATABASE LdbcSnb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE LdbcSnb; END; CREATE DATABASE LdbcSnb COLLATE SQL_Latin1_General_CP1_CI_AS; ALTER DATABASE LdbcSnb SET RECOVERY SIMPLE;"
    "${sqlcmd[@]}" -d LdbcSnb -i "${work}/schema.sql"
    "${sqlcmd[@]}" -d LdbcSnb -v DataPath="${data}" -i "${work}/load.sql"
    "${sqlcmd[@]}" -d LdbcSnb -i "${work}/constraints.sql"
    set_property ldbc.dataset "${dataset}"
    rm -rf "${data}"

    echo "LdbcSnb holds ${dataset}."
fi

indexes_version=$(md5sum "${scripts}/indexes.sql" | cut -c1-32)
indexes=$(property ldbc.indexes) || exit 1
if [ "${indexes}" = "${indexes_version}" ]; then
    echo "LdbcSnb already holds the indexes of indexes.sql."
else
    echo "Creating the indexes of LdbcSnb from indexes.sql..."
    "${sqlcmd[@]}" -d LdbcSnb -i "${work}/indexes.sql"
    set_property ldbc.indexes "${indexes_version}"
    echo "LdbcSnb holds the indexes of indexes.sql."
fi

validation=$(find "${scripts}" -maxdepth 1 -name 'validation_params-*.csv.zst' | head -n 1)
if [ -z "${validation}" ]; then
    echo "The image carries no validation set for this scale factor; LdbcSnb has no ValidationOperation."
    exit 0
fi
set_name=$(basename "${validation}" .csv.zst)

validated=$(property ldbc.validation) || exit 1
if [ "${validated}" = "${set_name}" ]; then
    echo "LdbcSnb already holds the validation set ${set_name}. Skipping its load."
    exit 0
fi

echo "Loading the validation set ${set_name} into LdbcSnb..."
zstd -dc "${validation}" > "${work}/${set_name}.csv"

"${sqlcmd[@]}" -d LdbcSnb -v ValidationFile="${work}/${set_name}.csv" -i "${work}/validation.sql"
set_property ldbc.validation "${set_name}"

echo "LdbcSnb holds the validation set ${set_name}."
