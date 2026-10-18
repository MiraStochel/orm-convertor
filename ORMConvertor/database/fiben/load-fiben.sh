#!/bin/bash
# Loads the FIBEN benchmark the image carries into the database Fiben (decision 149), and
# beside it the judge of the FIBEN catalog, where the repository has one. Called by init-db.sh
# after the LDBC loader, once SQL Server has started every database of the volume.
#
# Each part loads once. A finished load of the data writes the name of the archive into the
# extended property fiben.dataset of the database; a database without it - one a stopped
# container left half-loaded - or with another archive's name is dropped and loaded again. The
# judge follows judge.tsv, whatever revision loaded the data: judge.sql is rerunnable and runs
# whenever the checksum of the file is not the one the extended property fiben.judge records,
# so a volume loaded before the judge changed gets the change on the next start without
# loading the data again. An image without a judge file leaves the table out and says so.
#
# Every step rests on a question to the database, and a question the server does not answer
# ends the script with the database as it was - the rule of load-ldbc.sh, for the same reason.
set -euo pipefail

scripts=/opt/fiben
sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -b -I)

archive=$(find "${scripts}" -maxdepth 1 -name 'fiben-benchmark-*.zip' | head -n 1)
if [ -z "${archive}" ]; then
    echo "The image carries no FIBEN data set (FIBEN_DATASET=none); Fiben is not created."
    exit 0
fi
dataset=$(basename "${archive}" .zip)

# Asks what the extended property $1 of Fiben holds: MISSING when there is no such database,
# NONE when the database has no such property, otherwise the value.
property() {
    local answer
    if ! answer=$("${sqlcmd[@]}" -r1 -h -1 -W -Q "SET NOCOUNT ON; IF DB_ID(N'Fiben') IS NULL SELECT N'MISSING' ELSE EXEC (N'SELECT ISNULL((SELECT CAST(value AS NVARCHAR(200)) FROM Fiben.sys.extended_properties WHERE class = 0 AND name = N''$1''), N''NONE'')');") || [ -z "${answer}" ]; then
        echo "Fiben did not answer what $1 holds; leaving it as it is." >&2
        return 1
    fi
    printf '%s\n' "${answer}"
}

set_property() {
    "${sqlcmd[@]}" -d Fiben -Q "IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'$1') EXEC sys.sp_updateextendedproperty @name = N'$1', @value = N'$2'; ELSE EXEC sys.sp_addextendedproperty @name = N'$1', @value = N'$2';"
}

access=$("${sqlcmd[@]}" -r1 -h -1 -W -Q "SET NOCOUNT ON; SELECT ISNULL((SELECT user_access_desc FROM sys.databases WHERE name = N'Fiben'), N'MULTI_USER');") || exit 1
if [ "${access}" != "MULTI_USER" ]; then
    echo "Fiben was left ${access}; opening it again."
    "${sqlcmd[@]}" -Q "ALTER DATABASE Fiben SET MULTI_USER WITH ROLLBACK IMMEDIATE;"
fi

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

for script in schema load constraints judge; do
    sed 's/{{schema}}/dbo/g' "${scripts}/${script}.sql" > "${work}/${script}.sql"
done

loaded=$(property fiben.dataset) || exit 1
if [ "${loaded}" = "${dataset}" ]; then
    echo "Fiben already holds ${dataset}. Skipping the load."
else
    echo "Loading ${dataset} into Fiben (found: ${loaded})..."
    unzip -q -o "${archive}" 'data/*' -d "${work}"
    data="${work}/data"

    if [ "${loaded}" = "MISSING" ]; then
        rm -f /var/opt/mssql/data/Fiben.mdf /var/opt/mssql/data/Fiben_log.ldf
    fi
    "${sqlcmd[@]}" -Q "SET XACT_ABORT ON; IF DB_ID(N'Fiben') IS NOT NULL BEGIN ALTER DATABASE Fiben SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE Fiben; END; CREATE DATABASE Fiben COLLATE SQL_Latin1_General_CP1_CI_AS; ALTER DATABASE Fiben SET RECOVERY SIMPLE;"
    "${sqlcmd[@]}" -d Fiben -i "${work}/schema.sql"
    "${sqlcmd[@]}" -d Fiben -v DataPath="${data}" -i "${work}/load.sql"
    "${sqlcmd[@]}" -d Fiben -i "${work}/constraints.sql"
    set_property fiben.dataset "${dataset}"
    rm -rf "${data}"

    echo "Fiben holds ${dataset}."
fi

if [ ! -f "${scripts}/judge.tsv" ]; then
    echo "The image carries no judge of the FIBEN catalog (database/fiben/judge.tsv); Fiben has no Judge table."
    exit 0
fi

judge_version=$(md5sum "${scripts}/judge.tsv" | cut -c1-32)
judge=$(property fiben.judge) || exit 1
if [ "${judge}" = "${judge_version}" ]; then
    echo "Fiben already holds the judge of judge.tsv."
    exit 0
fi

echo "Loading the judge of the FIBEN catalog from judge.tsv..."
cp "${scripts}/judge.tsv" "${work}/judge.tsv"
"${sqlcmd[@]}" -d Fiben -v JudgeFile="${work}/judge.tsv" -i "${work}/judge.sql"
set_property fiben.judge "${judge_version}"

echo "Fiben holds the judge of judge.tsv."
