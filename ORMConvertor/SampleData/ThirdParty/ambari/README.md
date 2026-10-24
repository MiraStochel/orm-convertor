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
2. **Two queries are left out**, each with a note in place of its annotation:
   `AlertDefinitionEntity.findByServiceMaster` names an enum constant by its Java path
   (`ad.sourceType <> org.apache.ambari.server.state.alert.SourceType.AGGREGATE`), which the tool
   does not read as a value;
   `AlertGroupEntity.findByAssociatedDefinition` asks `MEMBER OF` a many-to-many with the whole
   entity as the parameter.
3. **The six enum-typed columns are rewritten as `String`**, with their accessors and with the
   original text above each: `scope` and `sourceType` of `AlertDefinitionEntity`,
   `maintenanceState` and `firmness` of `AlertCurrentEntity`, `alertState` of
   `AlertHistoryEntity` and `notifyState` of `AlertNoticeEntity`. Every one of them is
   `@Enumerated(EnumType.STRING)`, so the column holds the constant's name and a `String` reads
   the same rows. The enums (`Scope`, `SourceType`, `MaintenanceState`, `AlertFirmness`,
   `AlertState`, `NotificationState`) live outside the excerpt and the tool reads no enum, so
   their names went into the generated classes as types nobody declares: the first check of
   the experiment run found the Dapper, EF Core and NHibernate translations of the excerpt not
   compilable for it (2026-10-24). With the columns as `String`, the four queries that compare
   them with a parameter (`AlertCurrentEntity.findByService`,
   `AlertDefinitionEntity.findBySourceType`, `AlertHistoryEntity.findAllInClusterWithState`,
   `AlertNoticeEntity.findByState`) are back as the project wrote them.
4. **One association to an entity outside the excerpt is left out** with its accessors, with a
   note in its place: `AlertDefinitionEntity.clusterEntity`, a read-only `@ManyToOne` over
   `cluster_id` (`insertable = false, updatable = false`) to `ClusterEntity`, which the same check
   found in the generated classes as a type nobody declares. The column stays as the project's
   `clusterId` beside it; `ClusterEntity` itself refers to most of Ambari's model.
5. **One element collection is left out** with its accessors, with a note in its place:
   `AlertTargetEntity.alertStates` (`@ElementCollection Set<AlertState>` over
   `alert_target_states`). A collection table is not a column of the entity's table and the tool
   does not translate it, and its element is the enum of change 3.
6. **No read-only query is rewritten.** The eight queries of `AlertCurrentEntity` with a path
   through an association in a condition (`alert.alertHistory.clusterId = :clusterId`,
   `alert.alertHistory.alertDefinition.definitionName = :definitionName`) stand as the project
   wrote them: the tool reads a path to the referenced key as the foreign key column and every
   other path as an implicit join.
7. **The query hints stand as the project wrote them** (`eclipselink.query-results-cache` on
   `AlertDefinitionEntity.findByName`); the tool reports each as a dropped hint.
