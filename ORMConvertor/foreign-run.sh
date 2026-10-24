#!/usr/bin/env bash
# The loop of decision 172 over a directory of foreign artifacts: one artifact, one process
# of the suite that owns its target, one row. A process that outlives the limit is killed and
# its artifact is written down at the level it had reached, with the outcome "timed out" -
# the suite rewrites its row after every level, so the level is there to read. The rows of
# every artifact end up in one file, foreign-rows.tsv, under the directory of results; the
# log of each process and its own row stay beside it in a directory of the artifact's name.
#
# Usage, from ORMConvertor/:
#
#   ./foreign-run.sh <directory of artifacts> <directory of results> [seconds per artifact]
#
# The suites take their databases from the environment as they do elsewhere: the .NET half
# from ConnectionStrings__TestDatabase or the user secret, the Java half from
# ORMCONVERTOR_TEST_JDBC_URL, and an artifact that answers a FIBEN query from the FIBEN
# variables (ORMConvertor/README.md, *Foreign artifacts*). The Java half needs JAVA_HOME
# and mvn on PATH; MVN=<path> names another mvn. Neither half needs an instance of the tool.
set -u

root=${1:?the directory of artifacts}
results=${2:?the directory of results}
limit=${3:-900}
mvn_cmd=${MVN:-mvn}

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$root" && pwd)
mkdir -p "$results"
results=$(cd "$results" && pwd)
rows="$results/foreign-rows.tsv"

printf 'artifact\tanswers\tsource\ttarget\tproducer\treached\toutcome\tdetail\trows\tagrees\treadMs\tcompileMs\tacceptMs\trunMs\tmetadata\n' > "$rows"

# Build once, run many times: every process below runs with --no-build.
(cd "$here" && dotnet build Tests/Tests.csproj --configuration Release > "$results/build-dotnet.log" 2>&1) || { echo "the .NET suite does not build; see $results/build-dotnet.log"; exit 1; }
(cd "$here/JavaTests" && "$mvn_cmd" -B -q test-compile > "$results/build-java.log" 2>&1) || { echo "the Java suite does not build; see $results/build-java.log"; exit 1; }

for dir in "$root"/*/; do
  [ -f "$dir/artifact.json" ] || continue
  dir=${dir%/}
  name=$(basename "$dir")
  target=$(grep -oE '"target"[[:space:]]*:[[:space:]]*"[^"]+"' "$dir/artifact.json" | head -n 1 | sed -E 's/.*"([^"]+)"$/\1/')
  work="$results/$name"
  mkdir -p "$work"

  case "$target" in
    Dapper|EFCore|NHibernate)
      file="$work/foreign-rows.tsv"
      ORMCONVERTOR_FOREIGN_ARTIFACTS="$dir" ORMCONVERTOR_FOREIGN_RESULTS="$work" \
        timeout --signal=KILL "$limit" dotnet test "$here/Tests/Tests.csproj" --configuration Release --no-build \
        --filter "FullyQualifiedName~ForeignArtifactRunTest.TheArtifactIsJudgedAndRecorded" > "$work/log.txt" 2>&1
      status=$?
      ;;
    Hibernate|EclipseLink|MyBatis)
      file="$work/java-foreign-rows.tsv"
      (cd "$here/JavaTests" && ORMCONVERTOR_FOREIGN_ARTIFACTS="$dir" ORMCONVERTOR_FOREIGN_RESULTS="$work" \
        timeout --signal=KILL "$limit" "$mvn_cmd" -B -q test -Dtest=ForeignArtifactRunTest -Dsurefire.failIfNoSpecifiedTests=false) > "$work/log.txt" 2>&1
      status=$?
      ;;
    *)
      echo "$name: the manifest names \"$target\" as the target, which no suite owns; skipped"
      continue
      ;;
  esac

  if [ -f "$file" ]; then
    row=$(tail -n +2 "$file" | head -n 1)
  else
    row=$(printf '%s\t\t\t%s\t\t0\tno row\tthe suite wrote no row; see log.txt\t\t\t\t\t\t\t' "$name" "$target")
  fi

  # 124 is timeout's own status, 137 the one of a process killed by it.
  if [ "$status" -eq 124 ] || [ "$status" -eq 137 ]; then
    row=$(printf '%s' "$row" | awk -F'\t' -v OFS='\t' -v limit="$limit" '{ $8 = "timed out"; $9 = "the process was ended after " limit " s"; print }')
  fi

  printf '%s\n' "$row" >> "$rows"
  echo "$name: $(printf '%s' "$row" | cut -f7,8 | tr '\t' ' ')"
done

echo "rows in $rows"
