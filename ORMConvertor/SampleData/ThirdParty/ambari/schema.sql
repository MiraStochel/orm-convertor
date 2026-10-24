-- ORMConvertor: taken from apache/ambari at a01b674, ambari-server/src/main/resources/Ambari-DDL-SQLServer-CREATE.sql.
-- Changed: only the tables the excerpt maps are kept - the six alert tables, the two join tables
-- alert_grouping and alert_group_target, and ambari_sequences, which the @TableGenerator of every
-- entity names; the rest of the script (the other tables, the indexes, the inserted data) is left
-- out. Every table is qualified with {{schema}}, the schema the caller creates, where the script
-- creates in the default schema. The two foreign keys to clusters (alert_definition.cluster_id,
-- alert_history.cluster_id) are left out, because clusters is outside the excerpt; the table
-- alert_target_states of the element collection the excerpt leaves out stays, as the project has
-- it. Column types, defaults, constraint names and the unnamed constraints are the script's own.
--
-- The test suites put these tables, empty, into a schema of their own as the catalog of the
-- excerpt (decision 176).

CREATE TABLE [{{schema}}].ambari_sequences (
  sequence_name VARCHAR(255) NOT NULL,
  [sequence_value] BIGINT NOT NULL,
  CONSTRAINT PK_ambari_sequences PRIMARY KEY (sequence_name));

CREATE TABLE [{{schema}}].alert_definition (
  definition_id BIGINT NOT NULL,
  cluster_id BIGINT NOT NULL,
  definition_name VARCHAR(255) NOT NULL,
  service_name VARCHAR(255) NOT NULL,
  component_name VARCHAR(255),
  scope VARCHAR(255) DEFAULT 'ANY' NOT NULL,
  label VARCHAR(255),
  help_url VARCHAR(512),
  description TEXT,
  enabled SMALLINT DEFAULT 1 NOT NULL,
  schedule_interval INTEGER NOT NULL,
  source_type VARCHAR(255) NOT NULL,
  alert_source TEXT NOT NULL,
  hash VARCHAR(64) NOT NULL,
  ignore_host SMALLINT DEFAULT 0 NOT NULL,
  repeat_tolerance INTEGER DEFAULT 1 NOT NULL,
  repeat_tolerance_enabled SMALLINT DEFAULT 0 NOT NULL,
  CONSTRAINT PK_alert_definition PRIMARY KEY CLUSTERED (definition_id),
  CONSTRAINT uni_alert_def_name UNIQUE(cluster_id,definition_name)
);

CREATE TABLE [{{schema}}].alert_history (
  alert_id BIGINT NOT NULL,
  cluster_id BIGINT NOT NULL,
  alert_definition_id BIGINT NOT NULL,
  service_name VARCHAR(255) NOT NULL,
  component_name VARCHAR(255),
  host_name VARCHAR(255),
  alert_instance VARCHAR(255),
  alert_timestamp BIGINT NOT NULL,
  alert_label VARCHAR(1024),
  alert_state VARCHAR(255) NOT NULL,
  alert_text TEXT,
  CONSTRAINT PK_alert_history PRIMARY KEY CLUSTERED (alert_id),
  FOREIGN KEY (alert_definition_id) REFERENCES [{{schema}}].alert_definition(definition_id)
);

CREATE TABLE [{{schema}}].alert_current (
  alert_id BIGINT NOT NULL,
  definition_id BIGINT NOT NULL,
  history_id BIGINT NOT NULL UNIQUE,
  maintenance_state VARCHAR(255) NOT NULL,
  original_timestamp BIGINT NOT NULL,
  latest_timestamp BIGINT NOT NULL,
  latest_text TEXT,
  occurrences BIGINT NOT NULL DEFAULT 1,
  firmness VARCHAR(255) NOT NULL DEFAULT 'HARD',
  CONSTRAINT PK_alert_current PRIMARY KEY CLUSTERED (alert_id),
  FOREIGN KEY (definition_id) REFERENCES [{{schema}}].alert_definition(definition_id),
  FOREIGN KEY (history_id) REFERENCES [{{schema}}].alert_history(alert_id)
);

CREATE TABLE [{{schema}}].alert_group (
  group_id BIGINT NOT NULL,
  cluster_id BIGINT NOT NULL,
  group_name VARCHAR(255) NOT NULL,
  is_default SMALLINT NOT NULL DEFAULT 0,
  service_name VARCHAR(255),
  CONSTRAINT PK_alert_group PRIMARY KEY CLUSTERED (group_id),
  CONSTRAINT uni_alert_group_name UNIQUE(cluster_id,group_name)
);

CREATE TABLE [{{schema}}].alert_target (
  target_id BIGINT NOT NULL,
  target_name VARCHAR(255) NOT NULL UNIQUE,
  notification_type VARCHAR(64) NOT NULL,
  properties TEXT,
  description VARCHAR(1024),
  is_global SMALLINT NOT NULL DEFAULT 0,
  is_enabled SMALLINT NOT NULL DEFAULT 1,
  CONSTRAINT PK_alert_target PRIMARY KEY CLUSTERED (target_id)
);

CREATE TABLE [{{schema}}].alert_target_states (
  target_id BIGINT NOT NULL,
  alert_state VARCHAR(255) NOT NULL,
  FOREIGN KEY (target_id) REFERENCES [{{schema}}].alert_target(target_id)
);

CREATE TABLE [{{schema}}].alert_group_target (
  group_id BIGINT NOT NULL,
  target_id BIGINT NOT NULL,
  CONSTRAINT PK_alert_group_target PRIMARY KEY CLUSTERED (group_id, target_id),
  FOREIGN KEY (group_id) REFERENCES [{{schema}}].alert_group(group_id),
  FOREIGN KEY (target_id) REFERENCES [{{schema}}].alert_target(target_id)
);

CREATE TABLE [{{schema}}].alert_grouping (
  definition_id BIGINT NOT NULL,
  group_id BIGINT NOT NULL,
  CONSTRAINT PK_alert_grouping PRIMARY KEY CLUSTERED (group_id, definition_id),
  FOREIGN KEY (definition_id) REFERENCES [{{schema}}].alert_definition(definition_id),
  FOREIGN KEY (group_id) REFERENCES [{{schema}}].alert_group(group_id)
);

CREATE TABLE [{{schema}}].alert_notice (
  notification_id BIGINT NOT NULL,
  target_id BIGINT NOT NULL,
  history_id BIGINT NOT NULL,
  notify_state VARCHAR(255) NOT NULL,
  uuid VARCHAR(64) NOT NULL UNIQUE,
  CONSTRAINT PK_alert_notice PRIMARY KEY CLUSTERED (notification_id),
  FOREIGN KEY (target_id) REFERENCES [{{schema}}].alert_target(target_id),
  FOREIGN KEY (history_id) REFERENCES [{{schema}}].alert_history(alert_id)
);
