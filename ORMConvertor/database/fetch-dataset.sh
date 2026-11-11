#!/bin/bash
# Fetches one data-set archive while the database image is built (decision 215):
#
#   fetch-dataset.sh <target file> <address>...
#
# The addresses are tried in order - the project's own copy first, the publisher's after it -
# and the first one that answers is kept. A file whose name datasets.sha256 lists must match
# the checksum there, whichever address served it, so a replaced archive is a failed build
# and not another data set under the same name.
#
# Exit code 1: no address has the file (the caller may have another way to get it).
# Exit code 2: the file came and its checksum differs.
set -u

target=$1
shift
name=$(basename "${target}")
checksums=$(dirname "$0")/datasets.sha256

for address in "$@"; do
    echo "Fetching ${name} from ${address}"
    if curl -fL --retry 5 --retry-delay 15 -o "${target}.part" "${address}"; then
        mv "${target}.part" "${target}"
        # The list checks out with CRLF on Windows.
        expected=$(tr -d '\r' < "${checksums}" | awk -v name="${name}" '$2 == name { print $1 }')
        if [ -z "${expected}" ]; then
            echo "${name}: no checksum in datasets.sha256, taken as served."
            exit 0
        fi
        if echo "${expected}  ${target}" | sha256sum -c -; then
            exit 0
        fi
        echo "${name} from ${address} is not the archive datasets.sha256 names." >&2
        rm -f "${target}"
        exit 2
    fi
    rm -f "${target}.part"
done

echo "${name} is at none of: $*" >&2
exit 1
