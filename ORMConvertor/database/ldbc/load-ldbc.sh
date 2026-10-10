#!/bin/bash
# Loads the LDBC SNB data set the image carries into the database LdbcSnb (decision 110), and
# beside it the validation set of the same scale factor (decision 117). Called by init-db.sh
# once SQL Server accepts connections and WideWorldImporters is in place, so the sample
# database the application's catalog reads never waits for this one.
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

set_property() {
    "${sqlcmd[@]}" -d LdbcSnb -Q "IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'$1') EXEC sys.sp_updateextendedproperty @name = N'$1', @value = N'$2'; ELSE EXEC sys.sp_addextendedproperty @name = N'$1', @value = N'$2';"
}

# A database the volume already carries recovers for a while after the server accepts
# connections, and a question asked before that fails - and a failure must never read as
# "no data set", because that would drop a loaded database. So first wait until LdbcSnb is
# online or absent, and take it out of the single-user mode a load interrupted in the middle
# of its drop can leave behind.
state=""
for attempt in $(seq 1 180); do
    state=$("${sqlcmd[@]}" -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL((SELECT state_desc + N' ' + user_access_desc FROM sys.databases WHERE name = N'LdbcSnb'), N'MISSING');" 2>/dev/null || true)
    case "${state}" in
        "ONLINE MULTI_USER"|MISSING) break ;;
        "ONLINE SINGLE_USER")
            echo "LdbcSnb was left single-user; opening it again."
            "${sqlcmd[@]}" -Q "ALTER DATABASE LdbcSnb SET MULTI_USER WITH ROLLBACK IMMEDIATE;"
            break ;;
    esac
    sleep 2
done
case "${state}" in
    ONLINE*|MISSING) ;;
    *) echo "LdbcSnb is ${state:-unreachable} after six minutes; leaving it as it is."; exit 1 ;;
esac

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

for script in schema load constraints indexes validation; do
    sed 's/{{schema}}/dbo/g' "${scripts}/${script}.sql" > "${work}/${script}.sql"
done

if [ "$(property ldbc.dataset)" = "${dataset}" ]; then
    echo "LdbcSnb already holds ${dataset}. Skipping the load."
else
    echo "Loading ${dataset} into LdbcSnb..."
    zstd -dc "${archive}" | tar -x -C "${work}"
    data=$(find "${work}" -mindepth 1 -maxdepth 1 -type d | head -n 1)

    # A load that failed between DROP and CREATE leaves the files of the old database behind,
    # and CREATE DATABASE refuses to overwrite them; without the database they are nothing.
    if [ "$("${sqlcmd[@]}" -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL(CAST(DB_ID(N'LdbcSnb') AS NVARCHAR(10)), N'none');")" = "none" ]; then
        rm -f /var/opt/mssql/data/LdbcSnb.mdf /var/opt/mssql/data/LdbcSnb_log.ldf
    fi
    "${sqlcmd[@]}" -Q "IF DB_ID('LdbcSnb') IS NOT NULL BEGIN ALTER DATABASE LdbcSnb SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE LdbcSnb; END; CREATE DATABASE LdbcSnb COLLATE SQL_Latin1_General_CP1_CI_AS; ALTER DATABASE LdbcSnb SET RECOVERY SIMPLE;"
    "${sqlcmd[@]}" -d LdbcSnb -i "${work}/schema.sql"
    "${sqlcmd[@]}" -d LdbcSnb -v DataPath="${data}" -i "${work}/load.sql"
    "${sqlcmd[@]}" -d LdbcSnb -i "${work}/constraints.sql"
    set_property ldbc.dataset "${dataset}"
    rm -rf "${data}"

    echo "LdbcSnb holds ${dataset}."
fi

indexes_version=$(md5sum "${scripts}/indexes.sql" | cut -c1-32)
if [ "$(property ldbc.indexes)" = "${indexes_version}" ]; then
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

if [ "$(property ldbc.validation)" = "${set_name}" ]; then
    echo "LdbcSnb already holds the validation set ${set_name}. Skipping its load."
    exit 0
fi

echo "Loading the validation set ${set_name} into LdbcSnb..."
zstd -dc "${validation}" > "${work}/${set_name}.csv"

"${sqlcmd[@]}" -d LdbcSnb -v ValidationFile="${work}/${set_name}.csv" -i "${work}/validation.sql"
set_property ldbc.validation "${set_name}"

echo "LdbcSnb holds the validation set ${set_name}."
