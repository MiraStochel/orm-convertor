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

1. **The entity files are unchanged** apart from the provenance note.
2. **`AlertNamedQueries.java` is new.** Ambari keeps its queries in `@NamedQuery` annotations
   and runs them with `createNamedQuery`; the tool reads a query where code hands it to the
   framework. The class holds 35 of the 41 read-only named queries of the six entities, each as a
   method that hands its text to `em.createQuery`; the six that delete are not carried over.
3. **Eight of the 35 are rewritten**, each with a note and its original text above it: a path
   through an association in a condition (`alert.alertHistory.clusterId = :clusterId`) becomes a
   correlated `EXISTS` over the scalar key column the entity also maps (`historyId`,
   `definitionId`, `alertDefinitionId`), or a comparison of that column. The tool does not read
   a path outside `JOIN`, and a `JOIN` whose one side is projected it does not read faithfully.
4. **Six queries are left out**, each with a note at the end of the class: five compare a
   parameter with an attribute of an enum type (`Scope`, `SourceType`, `AlertState`,
   `NotificationState`) that lives outside the excerpt, so the generated method could not type the
   parameter; one asks `MEMBER OF` a many-to-many with the whole entity as the parameter.
