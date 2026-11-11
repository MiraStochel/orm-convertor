FROM mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04@sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718

ENV ACCEPT_EULA=Y \
    MSSQL_SA_PASSWORD=Testingorms123 \
    MSSQL_PID=Developer

USER root
RUN --mount=type=cache,target=/var/lib/apt \
    --mount=type=cache,target=/var/cache/apt \
    apt-get update && \
    apt-get install -y --no-install-recommends curl zstd unzip p7zip-full python3 && \
    rm -rf /var/lib/apt/lists/* && \
    mkdir -p /var/opt/mssql/backup && \
    curl -L -o /var/opt/mssql/backup/WideWorldImporters-Full.bak \
    https://github.com/Microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImporters-Full.bak

# Where the data sets come from (decision 215): the project's own copies, attached to the
# release data_sets of the repository, are asked first, the publisher's address after them.
# fetch-dataset.sh tries the addresses in order and checks the file against
# database/datasets.sha256 when its name is listed there.
ARG DATASETS_URL=https://github.com/MiraStochel/orm-convertor/releases/download/data_sets
COPY database/fetch-dataset.sh database/datasets.sha256 /opt/datasets/
RUN sed -i 's/\r$//' /opt/datasets/fetch-dataset.sh && chmod +x /opt/datasets/fetch-dataset.sh

# The LDBC SNB Interactive v1 data set of the second database, LdbcSnb (decision 110). The
# archive is fetched while the image is built, so the container starts without the network,
# and it stays packed: load-ldbc.sh unpacks it once, on the first start, into a temporary
# directory. It lives outside /var/opt/mssql because that path is the data volume, and an
# existing volume would hide whatever the image put under it. A scale factor LDBC publishes
# for Interactive v1 (0.1, 0.3, 1, 3, 10, ...) selects another archive; none leaves it out.
ARG LDBC_SCALE_FACTOR=1
RUN mkdir -p /opt/ldbc && \
    if [ "${LDBC_SCALE_FACTOR}" != "none" ]; then \
        archive="social_network-sf${LDBC_SCALE_FACTOR}-CsvMergeForeign-StringDateFormatter.tar.zst" && \
        /opt/datasets/fetch-dataset.sh "/opt/ldbc/${archive}" \
            "${DATASETS_URL}/${archive}" \
            "https://datasets.ldbcouncil.org/snb-interactive-v1/${archive}" || \
        { echo "No LDBC data set of scale factor ${LDBC_SCALE_FACTOR}; LDBC_SCALE_FACTOR=none builds the image without LdbcSnb." >&2; exit 1; }; \
    fi

# The validation set of the same scale factor (decision 117), the judge of the LDBC query
# catalog. Our own copy is the one file of the scale factor, packed. LDBC publishes the set
# for 0.1 to 10 in one archive of 205 MB; when the copy is missing, the image keeps the file
# of its own scale factor out of it, packed again, and drops the rest in the same step, so
# the archive never stands in a layer. A scale factor outside that range gets the data and
# no judge.
RUN case "${LDBC_SCALE_FACTOR}" in \
        0.1|0.3|1|3|10) \
            file="validation_params-sf${LDBC_SCALE_FACTOR}.csv" && \
            { /opt/datasets/fetch-dataset.sh "/opt/ldbc/${file}.zst" "${DATASETS_URL}/${file}.zst" || \
              { [ $? -eq 1 ] && \
                curl -fL "https://datasets.ldbcouncil.org/interactive-v1/validation_params-interactive-v1.0.0-sf0.1-to-sf10.tar.zst" \
                    | zstd -dc | tar -x -C /opt/ldbc "${file}" && \
                zstd -q --rm "/opt/ldbc/${file}"; }; } ;; \
    esac

# The FIBEN benchmark of the third database, Fiben (decision 149): data.zip of IBM/fiben-benchmark
# (Apache-2.0, 80 MB) at the commit the catalog was imported from, fetched while the image is
# built and unpacked by load-fiben.sh once, on the first start. The repository is gone from
# GitHub, so the archive exists only as our copy; none leaves the benchmark out.
ARG FIBEN_DATASET=96eaffc60cc824b8f545cd29adffc2a170e08d04
RUN mkdir -p /opt/fiben && \
    if [ "${FIBEN_DATASET}" != "none" ]; then \
        dataset="fiben-benchmark-$(echo "${FIBEN_DATASET}" | cut -c1-7)" && \
        /opt/datasets/fetch-dataset.sh "/opt/fiben/${dataset}.zip" "${DATASETS_URL}/${dataset}-data.zip" || \
        { echo "No FIBEN data set of commit ${FIBEN_DATASET}; FIBEN_DATASET=none builds the image without Fiben." >&2; exit 1; }; \
    fi

# The data of the SEDE query set, the fourth database Sede (decision 208): the data dump of one
# Stack Exchange site (CC BY-SA 4.0) as Stack Exchange uploaded it to the item "stackexchange"
# on archive.org, fetched while the image is built and unpacked table by table by load-sede.sh
# on the first start. The name of the data set is the site and the date of the dump, the
# archive is checked against the MD5 the item publishes for it, so that a replaced file is a
# failed build and not another data set under the same name; none leaves the database out.
# The tools of the loader: 7z unpacks the archive, python3 turns the XML into CSV. archive.org
# serves the file slowly and drops a connection after an hour or two, so the download resumes
# where it stopped, up to thirty times.
ARG SEDE_DATASET=dba.stackexchange.com-2024-04-06
ARG SEDE_DATASET_URL=https://archive.org/download/stackexchange/dba.stackexchange.com.7z
ARG SEDE_DATASET_MD5=d0f417b3c3c9210ed32623a071651411
RUN mkdir -p /opt/sede && \
    if [ "${SEDE_DATASET}" != "none" ]; then \
        curl -fL --retry 30 --retry-delay 15 --retry-all-errors -C - -o "/opt/sede/${SEDE_DATASET}.7z" "${SEDE_DATASET_URL}" && \
        if [ -n "${SEDE_DATASET_MD5}" ]; then echo "${SEDE_DATASET_MD5}  /opt/sede/${SEDE_DATASET}.7z" | md5sum -c -; fi; \
    fi

# The scripts are shared with the test suite, which checks out with CRLF on Windows; sqlcmd
# and bash here want LF, so the line endings are normalized on the way in. The FIBEN and the
# SEDE folders go whole: their judges, judge.tsv, exist only from the first documented run on.
COPY database/ldbc/schema.sql database/ldbc/load.sql database/ldbc/constraints.sql database/ldbc/indexes.sql database/ldbc/validation.sql database/ldbc/load-ldbc.sh /opt/ldbc/
COPY database/fiben/ /opt/fiben/
COPY database/sede/ /opt/sede/
COPY database/init-db.sh /usr/local/bin/init-db.sh
RUN sed -i 's/\r$//' /opt/ldbc/*.sql /opt/ldbc/load-ldbc.sh /opt/fiben/*.sql /opt/fiben/load-fiben.sh /opt/sede/*.sql /opt/sede/*.tsv /opt/sede/load-sede.sh /opt/sede/xml-to-csv.py /usr/local/bin/init-db.sh && \
    if [ -f /opt/fiben/judge.tsv ]; then sed -i 's/\r$//' /opt/fiben/judge.tsv; fi && \
    chmod +x /usr/local/bin/init-db.sh /opt/ldbc/load-ldbc.sh /opt/fiben/load-fiben.sh /opt/sede/load-sede.sh && \
    chmod -R a+rX /opt/ldbc /opt/fiben /opt/sede

USER mssql

ENTRYPOINT ["/usr/local/bin/init-db.sh"]

# Health check to ensure SQL Server is up
HEALTHCHECK --interval=10s --start-period=60s \
    CMD /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" || exit 1
