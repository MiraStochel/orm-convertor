# Apache Ambari: alerts

- **Project:** [apache/ambari](https://github.com/apache/ambari), a manager of Hadoop clusters;
  JPA entities on EclipseLink 4.0.2, `jakarta.persistence` annotations.
- **Commit:** `a01b6745cf618b20accde2e670b7c78115c21367` (2026-09-23).
- **Licence:** Apache License 2.0, [`LICENSE.txt`](LICENSE.txt) and [`NOTICE.txt`](NOTICE.txt).
  The project's own `LICENSE.txt` appends the licences of subcomponents it bundles; none of them
  is part of this excerpt, so the copy here stops at the end of the Apache License.
- **Taken from:** `ambari-server/src/main/java/org/apache/ambari/server/orm/entities/`, the six
  alert entities of its 98 - definitions, groups, targets, the current alert, its history and
  the notices sent for it, with two many-to-many relations through join tables.

## What was changed

Ambari keeps its queries in `@NamedQuery` annotations on the entities and runs them with
`createNamedQuery`; the tool reads a named query where it is defined, so the queries stay in the
annotations. Every change is marked in the file where it stands, with a note above the query,
and the opening note of each file counts the changes it carries.

1. **The six named queries that delete are left out**, each with a note in place of its
   annotation: the tool translates read-only queries.
2. **Six queries are left out**, each with a note in place of its annotation: five compare a
   parameter with an attribute of an enum type (`Scope`, `SourceType`, `AlertState`,
   `NotificationState`) that lives outside the excerpt, so the generated method could not type the
   parameter; one asks `MEMBER OF` a many-to-many with the whole entity as the parameter.
3. **No read-only query is rewritten.** The eight queries of `AlertCurrentEntity` with a path
   through an association in a condition (`alert.alertHistory.clusterId = :clusterId`,
   `alert.alertHistory.alertDefinition.definitionName = :definitionName`) stand as the project
   wrote them: the tool reads a path to the referenced key as the foreign key column and every
   other path as an implicit join.
4. **The query hints stand as the project wrote them** (`eclipselink.query-results-cache` on
   `AlertDefinitionEntity.findByName`); the tool reports each as a dropped hint.
