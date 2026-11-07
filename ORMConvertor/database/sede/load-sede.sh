#!/bin/bash
# Loads the data dump of one Stack Exchange site the image carries into the database Sede
# (decision 208), the SEDE schema of database/sede/, and beside it the judge of the SEDE
# catalog, where the repository has one. Called by init-db.sh after the FIBEN loader, once SQL
# Server has started every database of the volume.
#
# Each part loads once, by the rules of load-fiben.sh: a finished load writes the name of the
# archive into the extended property sede.dataset of the database, and a database without it
# or with another archive's name is dropped and loaded again; the judge follows judge.tsv by
# its checksum in sede.judge. Every step rests on a question to the database, and a question
# the server does not answer ends the script with the database as it was.
#
# The dump is XML, one file per table; each file is taken out of the archive alone, turned
# into CSV by xml-to-csv.py (the tables and columns in dump.tsv, as load.sql stages them)
# and removed again, so the work directory holds one table at a time.
set -euo pipefail

scripts=/opt/sede
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -b -I)

archive=$(find "${scripts}" -maxdepth 1 -name '*.7z' | head -n 1)
if [ -z "${archive}" ]; then
    echo "The image carries no SEDE data set (SEDE_DATASET=none); Sede is not created."
    exit 0
fi
dataset=$(basename "${archive}" .7z)

# Asks what the extended property $1 of Sede holds: MISSING when there is no such database,
# NONE when the database has no such property, otherwise the value.
property() {
    local answer
    if ! answer=$("${sqlcmd[@]}" -r1 -h -1 -W -Q "SET NOCOUNT ON; IF DB_ID(N'Sede') IS NULL SELECT N'MISSING' ELSE EXEC (N'SELECT ISNULL((SELECT CAST(value AS NVARCHAR(200)) FROM Sede.sys.extended_properties WHERE class = 0 AND name = N''$1''), N''NONE'')');") || [ -z "${answer}" ]; then
        echo "Sede did not answer what $1 holds; leaving it as it is." >&2
        return 1
    fi
    printf '%s\n' "${answer}"
}

set_property() {
    "${sqlcmd[@]}" -d Sede -Q "IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'$1') EXEC sys.sp_updateextendedproperty @name = N'$1', @value = N'$2'; ELSE EXEC sys.sp_addextendedproperty @name = N'$1', @value = N'$2';"
}

access=$("${sqlcmd[@]}" -r1 -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL((SELECT user_access_desc FROM sys.databases WHERE name = N'Sede'), N'MULTI_USER');") || exit 1
if [ "${access}" != "MULTI_USER" ]; then
    echo "Sede was left ${access}; opening it again."
    "${sqlcmd[@]}" -Q "ALTER DATABASE Sede SET MULTI_USER WITH ROLLBACK IMMEDIATE;"
fi

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

for script in schema load constraints judge; do
    sed 's/{{schema}}/dbo/g' "${scripts}/${script}.sql" > "${work}/${script}.sql"
done

loaded=$(property sede.dataset) || exit 1
if [ "${loaded}" = "${dataset}" ]; then
    echo "Sede already holds ${dataset}. Skipping the load."
else
    echo "Loading ${dataset} into Sede (found: ${loaded})..."
    mkdir -p "${work}/xml" "${work}/csv"
    while IFS=$'\t' read -r table file columns; do
        [ -n "${table}" ] || continue
        echo "  ${table}: unpacking ${file}..."
        7z e -y -o"${work}/xml" "${archive}" "${file}" > /dev/null
        python3 "${scripts}/xml-to-csv.py" "${work}/xml/${file}" "${work}/csv/${table}.csv" "${columns}"
        rm -f "${work}/xml/${file}"
    done < "${scripts}/dump.tsv"

    if [ "${loaded}" = "MISSING" ]; then
        rm -f /var/opt/mssql/data/Sede.mdf /var/opt/mssql/data/Sede_log.ldf
    fi
    "${sqlcmd[@]}" -Q "SET XACT_ABORT ON; IF DB_ID(N'Sede') IS NOT NULL BEGIN ALTER DATABASE Sede SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE Sede; END; CREATE DATABASE Sede COLLATE SQL_Latin1_General_CP1_CI_AS; ALTER DATABASE Sede SET RECOVERY SIMPLE;"
    "${sqlcmd[@]}" -d Sede -i "${work}/schema.sql"
    "${sqlcmd[@]}" -d Sede -v DataPath="${work}/csv" -i "${work}/load.sql"
    "${sqlcmd[@]}" -d Sede -i "${work}/constraints.sql"
    set_property sede.dataset "${dataset}"
    rm -rf "${work}/csv" "${work}/xml"

    echo "Sede holds ${dataset}."
fi

if [ ! -f "${scripts}/judge.tsv" ]; then
    echo "The image carries no judge of the SEDE catalog (database/sede/judge.tsv); Sede has no Judge table."
    exit 0
fi

judge_version=$(md5sum "${scripts}/judge.tsv" | cut -c1-32)
judge=$(property sede.judge) || exit 1
if [ "${judge}" = "${judge_version}" ]; then
    echo "Sede already holds the judge of judge.tsv."
    exit 0
fi

echo "Loading the judge of the SEDE catalog from judge.tsv..."
cp "${scripts}/judge.tsv" "${work}/judge.tsv"
"${sqlcmd[@]}" -d Sede -v JudgeFile="${work}/judge.tsv" -i "${work}/judge.sql"
set_property sede.judge "${judge_version}"

echo "Sede holds the judge of judge.tsv."
