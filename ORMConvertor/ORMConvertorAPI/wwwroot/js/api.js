/*
 * The one module that knows the API (decision 032): endpoint paths, DTO shapes and
 * mirrors of the server enums, which serialize as numbers. Every page imports from
 * here; no other script states a path or an enum value (the markup of the LDBC page carries
 * a few as numbers in data attributes, which its script reads back through these mirrors).
 *
 * All paths are relative, so the deployment base path (/orm) is never written down.
 */

export const ORM = Object.freeze({ Dapper: 10, NHibernate: 20, EFCore: 30, Hibernate: 40, EclipseLink: 50, MyBatis: 60 });

export const ORM_LABELS = Object.freeze({
  [ORM.Dapper]: "Dapper",
  [ORM.NHibernate]: "NHibernate",
  [ORM.EFCore]: "EF Core",
  [ORM.Hibernate]: "Hibernate",
  [ORM.EclipseLink]: "EclipseLink",
  [ORM.MyBatis]: "MyBatis",
});

/*
 * Which frameworks the advisor can measure. It mirrors AdvisorRunCoordinator.SupportedFrameworks,
 * and the server stays authoritative: a target it does not support is filtered out there and the
 * run ends with "no supported target frameworks resolved". This copy exists so the screen can
 * say so before the request goes out, and it lives here because api.js is the one module that
 * mirrors server facts (decision 032).
 */
export const ADVISOR_FRAMEWORKS = Object.freeze([ORM.Dapper, ORM.EFCore]);

/*
 * What the source may declare about the dialect of its own literal SQL (decision 088).
 * Two values and no list of system names: the tool behaves identically towards every system
 * it does not read, so a name would select nothing - and a vocabulary of names of systems we
 * do not read would promise a reading we do not have. Undeclared is the third state and it
 * is the absence of the field, not a value of it.
 */
export const SourceDialect = Object.freeze({ SqlServer2022: 10, AnotherSystem: 1000 });

export const SOURCE_DIALECT_LABELS = Object.freeze({
  [SourceDialect.SqlServer2022]: "SQL Server 2022",
  [SourceDialect.AnotherSystem]: "another database system",
});

// An input unit declares its language only - CSharp and Java are whole files with whatever
// they hold (decision 111). The five values that name a role besides the language belong to
// the artifacts the tool writes, and no parser takes them on input; CSharpLinqQuery is the
// LINQ form NHibernate writes beside its HQL method (decision 118).
export const ContentType = Object.freeze({
  CSharpEntity: 10,
  CSharpQuery: 20,
  Xml: 30,
  SqlQuery: 40,
  HqlQuery: 50,
  JavaEntity: 60,
  JavaQuery: 70,
  JpqlQuery: 80,
  CSharp: 90,
  Java: 100,
  CSharpLinqQuery: 110,
});

// The XML value names a language and promises no role: the same value carries the hbm.xml,
// the orm.xml and - with a class beside a named query in one file - a document that is a
// mapping and a query at once (decisions 025 and 081). Which document a framework asks for
// is said per framework, by the required-content list the server sends.
export const CONTENT_TYPE_LABELS = Object.freeze({
  [ContentType.CSharpEntity]: "C# entity",
  [ContentType.CSharpQuery]: "C# query method",
  [ContentType.Xml]: "XML document",
  [ContentType.SqlQuery]: "SQL query",
  [ContentType.HqlQuery]: "HQL query",
  [ContentType.JavaEntity]: "Java entity",
  [ContentType.JavaQuery]: "Java query method",
  [ContentType.JpqlQuery]: "JPQL query",
  [ContentType.CSharp]: "C# source",
  [ContentType.Java]: "Java source",
  [ContentType.CSharpLinqQuery]: "C# query (LINQ)",
});

// The XML value covers hbm.xml and orm.xml alike (decision 077); ui.js names an
// orm.xml artifact by its root element, this default is the NHibernate spelling. The LINQ
// form of an NHibernate query takes an infix, so that it stands beside the HQL method's
// file under a name of its own.
export const CONTENT_TYPE_EXTENSIONS = Object.freeze({
  [ContentType.CSharpEntity]: ".cs",
  [ContentType.CSharpQuery]: ".cs",
  [ContentType.Xml]: ".hbm.xml",
  [ContentType.SqlQuery]: ".sql",
  [ContentType.HqlQuery]: ".hql",
  [ContentType.JavaEntity]: ".java",
  [ContentType.JavaQuery]: ".java",
  [ContentType.JpqlQuery]: ".jpql",
  [ContentType.CSharp]: ".cs",
  [ContentType.Java]: ".java",
  [ContentType.CSharpLinqQuery]: ".linq.cs",
});

// HQL and JPQL have no grammar of their own and are highlighted as SQL - an approximation
// over the vendored grammars, not a claim about the languages (decision 032d).
export const CONTENT_TYPE_HIGHLIGHT = Object.freeze({
  [ContentType.CSharpEntity]: "csharp",
  [ContentType.CSharpQuery]: "csharp",
  [ContentType.Xml]: "xml",
  [ContentType.SqlQuery]: "sql",
  [ContentType.HqlQuery]: "sql",
  [ContentType.JavaEntity]: "java",
  [ContentType.JavaQuery]: "java",
  [ContentType.JpqlQuery]: "sql",
  [ContentType.CSharp]: "csharp",
  [ContentType.Java]: "java",
  [ContentType.CSharpLinqQuery]: "csharp",
});

export const RecordKind = Object.freeze({
  Failure: 1,
  Loss: 2,
  Convention: 3,
  Incompleteness: 4,
  Supplied: 5,
  Conflict: 6,
  Fallback: 7,
  Omitted: 8,
});

export const RECORD_KIND_LABELS = Object.freeze({
  [RecordKind.Failure]: "Failure",
  [RecordKind.Loss]: "Loss",
  [RecordKind.Convention]: "Convention",
  [RecordKind.Incompleteness]: "Incompleteness",
  [RecordKind.Supplied]: "Supplied",
  [RecordKind.Conflict]: "Conflict",
  [RecordKind.Fallback]: "Fallback",
  [RecordKind.Omitted]: "Omitted",
});

export const CatalogState = Object.freeze({
  NotConfigured: 0,
  Unused: 1,
  Reached: 2,
  Unreachable: 3,
});

export const MappingFactCategory = Object.freeze({
  TableName: 1,
  SchemaName: 2,
  ColumnName: 3,
  DatabaseType: 4,
  Length: 5,
  PrecisionAndScale: 6,
  Nullability: 7,
  PrimaryKey: 8,
  PrimaryKeyStrategy: 9,
  ForeignKeyColumns: 10,
  VersionColumn: 11,
  UniqueConstraint: 12,
  TransientProperty: 13,
});

export const MAPPING_FACT_CATEGORY_LABELS = Object.freeze({
  [MappingFactCategory.TableName]: "table name",
  [MappingFactCategory.SchemaName]: "schema name",
  [MappingFactCategory.ColumnName]: "column name",
  [MappingFactCategory.DatabaseType]: "database type",
  [MappingFactCategory.Length]: "length",
  [MappingFactCategory.PrecisionAndScale]: "precision and scale",
  [MappingFactCategory.Nullability]: "nullability",
  [MappingFactCategory.PrimaryKey]: "primary key",
  [MappingFactCategory.PrimaryKeyStrategy]: "primary key strategy",
  [MappingFactCategory.ForeignKeyColumns]: "foreign key columns",
  [MappingFactCategory.VersionColumn]: "version column",
  [MappingFactCategory.UniqueConstraint]: "unique constraint",
  [MappingFactCategory.TransientProperty]: "transient property",
});

/*
 * The fact ledger (decision 124): where a mapping fact came from and what became of it. The
 * origin has three values because a fact can be born in exactly three phases - the reading
 * of a unit, the catalog completion, the build - and the outcome three because a reader can
 * check exactly three things against the two texts: it is in the output, it is not, there is
 * no output.
 */
export const FactOrigin = Object.freeze({ Source: 1, Catalog: 2, Derived: 3 });

export const FACT_ORIGIN_LABELS = Object.freeze({
  [FactOrigin.Source]: "stated by the input",
  [FactOrigin.Catalog]: "supplied by the catalog",
  [FactOrigin.Derived]: "derived by the tool",
});

export const FactOutcome = Object.freeze({ Carried: 1, Dropped: 2, NotGenerated: 3 });

export const FACT_OUTCOME_LABELS = Object.freeze({
  [FactOutcome.Carried]: "carried",
  [FactOutcome.Dropped]: "dropped",
  [FactOutcome.NotGenerated]: "not generated",
});

export const QUERY_FEATURE_LABELS = Object.freeze({
  1: "projection",
  2: "filtering",
  3: "join",
  4: "join kind",
  5: "aggregation",
  6: "grouping",
  7: "post-aggregation filtering",
  8: "ordering",
  9: "pagination",
  10: "subquery",
  11: "set operation",
  12: "query parameter",
  13: "expression",
  14: "intermediate result",
  15: "recursion",
  16: "grouping by an expression",
  17: "window function",
  18: "list aggregation",
});

/*
 * The LDBC query catalog (decision 110): the workload a query belongs to and how much of it
 * the tool translates, numbered as SampleData numbers them. The catalog's claims are held by
 * the suite (LdbcCatalogTest); the page only shows them.
 */
export const LdbcWorkload = Object.freeze({ InteractiveShort: 10, InteractiveComplex: 20, BusinessIntelligence: 30 });

export const LdbcTranslation = Object.freeze({ AsSpecified: 10, Simplified: 20, NotTranslated: 30 });

export const LDBC_TRANSLATION_LABELS = Object.freeze({
  [LdbcTranslation.AsSpecified]: "Translated as specified",
  [LdbcTranslation.Simplified]: "Translated, simplified",
  [LdbcTranslation.NotTranslated]: "Not translated",
});

/*
 * The FIBEN query catalog (decision 149): Kim's type of a nested query and how much of it the
 * tool translates, numbered as SampleData numbers them. The status is a claim the on-demand run
 * checks; the page shows it and lets the reader translate any query.
 */
export const FibenNesting = Object.freeze({ NonNested: 10, TypeN: 20, TypeA: 30, TypeJ: 40, TypeJA: 50 });

export const FIBEN_NESTING_LABELS = Object.freeze({
  [FibenNesting.NonNested]: "single block",
  [FibenNesting.TypeN]: "type N",
  [FibenNesting.TypeA]: "type A",
  [FibenNesting.TypeJ]: "type J",
  [FibenNesting.TypeJA]: "type JA",
});

export const FibenTranslation = Object.freeze({ AsSpecified: 10, Simplified: 20, NotTranslated: 30 });

export const FIBEN_TRANSLATION_LABELS = Object.freeze({
  [FibenTranslation.AsSpecified]: "Translated as the benchmark states it",
  [FibenTranslation.Simplified]: "Translated, simplified",
  [FibenTranslation.NotTranslated]: "Not translated",
});

/*
 * Error bodies: handlers answer with ProblemDetails per RFC 9457 (decision 044), so the
 * reason is in `detail`; `title` is the generic "Bad Request" and would tell the user
 * nothing. The string branch stays for a bare-string body, which costs nothing and reads
 * an older instance. Read the body, not the HTTP status line - the status line was the
 * bug of the old frontend.
 */
async function errorMessage(response) {
  let text = "";
  try {
    text = await response.text();
  } catch {
    /* fall through to the status line */
  }
  if (text) {
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed === "string") return parsed;
      if (parsed && typeof parsed.detail === "string") return parsed.detail;
      if (parsed && typeof parsed.title === "string") return parsed.title;
    } catch {
      return text;
    }
    return text;
  }
  return `${response.status} ${response.statusText}`;
}

async function request(path, options) {
  const response = await fetch(path, options);
  if (!response.ok) throw new Error(await errorMessage(response));
  return response;
}

const getJson = async (path) => (await request(path)).json();

const post = (path, body) =>
  request(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

/** GET /required-content - which content types each source framework reads. */
export const getRequiredContent = () => getJson("required-content");

/** GET /required-content-advisor - the advisor's variant of the same. */
export const getRequiredContentAdvisor = () => getJson("required-content-advisor");

/** GET /samples - sample contents keyed by the ids of /required-content units. */
export const getSamples = () => getJson("samples");

/** GET /samples-advisor - sample contents for the advisor screen. */
export const getAdvisorSamples = () => getJson("samples-advisor");

/**
 * GET /examples - the examples as whole conversion inputs (decision 099):
 * { key, title, sourceOrm, targetOrm, units }, each unit the { contentType, content, name }
 * that /convert takes, so the explanatory page sends it unchanged and the translation screen
 * loads it into its editor as it is.
 */
export const getExamples = () => getJson("examples");

/**
 * GET /ldbc - the LDBC query catalog (decision 110): { sourceOrm, entities, queries }, the
 * entities as /convert units and every query as { key, workload, number, title, translation,
 * note, sql, parameters, refusedBy, fallbackBy, validation } - validation being the operation
 * of the LDBC validation set that judges an Interactive query (decision 117), null for a BI
 * one. A query is run by sending the entities plus its text.
 */
export const getLdbc = () => getJson("ldbc");

/**
 * GET /fiben - the FIBEN query catalog (decision 149): { sourceOrm, dataSet, entities, queries },
 * the entities as /convert units and every query as { key, number, nesting, questions, tables,
 * columns, ordered, orderingKey, sql, benchmarkSql, translation, note }. A query is run by sending the
 * entities of the tables it names plus its text.
 */
export const getFiben = () => getJson("fiben");

/**
 * POST /convert. Sources are { contentType, content, name? } units; the response carries
 * runId, toolVersion, source/target framework with versions, targetDatabaseDialect, sources
 * (the generated artifacts, each with the entity it was generated for and the units it came
 * from - decision 124), records, facts (the ledger, with the positions of decision 125),
 * catalogState, catalogReadMilliseconds, declaredSourceDialect and maxNestingDepth.
 *
 * declaredSourceDialect is optional on the way in and null means the source declares
 * nothing, which reads as it always did (decision 088); the field is sent only when it has
 * a value, so an undeclared conversion is byte for byte the request it was before.
 */
export const convert = async (sourceOrm, targetOrm, sources, declaredSourceDialect = null) =>
  (await post("convert", {
    sourceOrm,
    targetOrm,
    sources,
    ...(declaredSourceDialect == null ? {} : { declaredSourceDialect }),
  })).json();

/** POST /advisor/run - needs the native ILP solver, available only in the Docker image. */
export const runAdvisor = async (advisorRequest) =>
  (await post("advisor/run", advisorRequest)).json();

/** POST /archive - packs { name, content } files into a ZIP blob (decision 033). */
export const archive = async (files) => (await post("archive", { files })).blob();
