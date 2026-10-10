FROM mcr.microsoft.com/mssql/server:2022-latest

ENV ACCEPT_EULA=Y \
    MSSQL_SA_PASSWORD=Testingorms123 \
    MSSQL_PID=Developer

USER root
RUN --mount=type=cache,target=/var/lib/apt \
    --mount=type=cache,target=/var/cache/apt \
    apt-get update && \
    apt-get install -y --no-install-recommends curl zstd && \
    rm -rf /var/lib/apt/lists/* && \
    mkdir -p /var/opt/mssql/backup && \
    curl -L -o /var/opt/mssql/backup/WideWorldImporters-Full.bak \
    https://github.com/Microsoft/sql-server-samples/releases/download/wide-world-importers-v1.0/WideWorldImporters-Full.bak

# The LDBC SNB Interactive v1 data set of the second database, LdbcSnb (decision 110). The
# archive is fetched while the image is built, so the container starts without the network,
# and it stays packed: load-ldbc.sh unpacks it once, on the first start, into a temporary
# directory. It lives outside /var/opt/mssql because that path is the data volume, and an
# existing volume would hide whatever the image put under it. A scale factor LDBC publishes
# for Interactive v1 (0.1, 0.3, 1, 3, 10, ...) selects another archive; none leaves it out.
ARG LDBC_SCALE_FACTOR=1
RUN mkdir -p /opt/ldbc && \
    if [ "${LDBC_SCALE_FACTOR}" != "none" ]; then \
        curl -fL -o "/opt/ldbc/social_network-sf${LDBC_SCALE_FACTOR}-CsvMergeForeign-StringDateFormatter.tar.zst" \
        "https://datasets.ldbcouncil.org/snb-interactive-v1/social_network-sf${LDBC_SCALE_FACTOR}-CsvMergeForeign-StringDateFormatter.tar.zst"; \
    fi

# The validation set of the same scale factor (decision 117), the judge of the LDBC query
# catalog. LDBC publishes it for 0.1 to 10 in one archive of 205 MB; the image keeps the file
# of its own scale factor, packed again, and drops the rest in the same step, so the archive
# never stands in a layer. A scale factor outside that range gets the data and no judge.
RUN case "${LDBC_SCALE_FACTOR}" in \
        0.1|0.3|1|3|10) \
            curl -fL "https://datasets.ldbcouncil.org/interactive-v1/validation_params-interactive-v1.0.0-sf0.1-to-sf10.tar.zst" \
                | zstd -dc | tar -x -C /opt/ldbc "validation_params-sf${LDBC_SCALE_FACTOR}.csv" && \
            zstd -q --rm "/opt/ldbc/validation_params-sf${LDBC_SCALE_FACTOR}.csv" ;; \
    esac

# The scripts are shared with the test suite, which checks out with CRLF on Windows; sqlcmd
# and bash here want LF, so the line endings are normalized on the way in.
COPY database/ldbc/schema.sql database/ldbc/load.sql database/ldbc/constraints.sql database/ldbc/indexes.sql database/ldbc/validation.sql database/ldbc/load-ldbc.sh /opt/ldbc/
COPY database/init-db.sh /usr/local/bin/init-db.sh
RUN sed -i 's/\r$//' /opt/ldbc/*.sql /opt/ldbc/load-ldbc.sh /usr/local/bin/init-db.sh && \
    chmod +x /usr/local/bin/init-db.sh /opt/ldbc/load-ldbc.sh && \
    chmod -R a+rX /opt/ldbc

USER mssql

ENTRYPOINT ["/usr/local/bin/init-db.sh"]

# Health check to ensure SQL Server is up
HEALTHCHECK --interval=10s --start-period=60s \
    CMD /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" || exit 1
