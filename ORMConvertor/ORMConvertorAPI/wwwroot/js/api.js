/*
 * The one module that knows the API (decision 032): endpoint paths, DTO shapes and
 * mirrors of the server enums, which serialize as numbers. Every page imports from
 * here; nothing else states a path or an enum value.
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

export const ContentType = Object.freeze({
  CSharpEntity: 10,
  CSharpQuery: 20,
  Xml: 30,
  SqlQuery: 40,
  HqlQuery: 50,
  JavaEntity: 60,
  JavaQuery: 70,
  JpqlQuery: 80,
});

// The XML value names a language and promises no role: the same value carries the hbm.xml,
// the orm.xml and - with a class beside a named query in one file - a document that is a
// mapping and a query at once (decisions 025 and 081). Which document a framework asks for
// is said per framework, by the required-content list the server sends.
export const CONTENT_TYPE_LABELS = Object.freeze({
  [ContentType.CSharpEntity]: "C# entity",
  [ContentType.CSharpQuery]: "C# query (LINQ)",
  [ContentType.Xml]: "XML document",
  [ContentType.SqlQuery]: "SQL query",
  [ContentType.HqlQuery]: "HQL query",
  [ContentType.JavaEntity]: "Java entity",
  [ContentType.JavaQuery]: "Java query method",
  [ContentType.JpqlQuery]: "JPQL query",
});

// The XML value covers hbm.xml and orm.xml alike (decision 077); ui.js names an
// orm.xml artifact by its root element, this default is the NHibernate spelling.
export const CONTENT_TYPE_EXTENSIONS = Object.freeze({
  [ContentType.CSharpEntity]: ".cs",
  [ContentType.CSharpQuery]: ".cs",
  [ContentType.Xml]: ".hbm.xml",
  [ContentType.SqlQuery]: ".sql",
  [ContentType.HqlQuery]: ".hql",
  [ContentType.JavaEntity]: ".java",
  [ContentType.JavaQuery]: ".java",
  [ContentType.JpqlQuery]: ".jpql",
});

// HQL and JPQL have no grammar of their own and are highlighted as SQL, Java as C# -
// approximations over the vendored grammars, not claims about the languages (decision 032d).
export const CONTENT_TYPE_HIGHLIGHT = Object.freeze({
  [ContentType.CSharpEntity]: "csharp",
  [ContentType.CSharpQuery]: "csharp",
  [ContentType.Xml]: "xml",
  [ContentType.SqlQuery]: "sql",
  [ContentType.HqlQuery]: "sql",
  [ContentType.JavaEntity]: "csharp",
  [ContentType.JavaQuery]: "csharp",
  [ContentType.JpqlQuery]: "sql",
});

export const RecordKind = Object.freeze({
  Failure: 1,
  Loss: 2,
  Convention: 3,
  Incompleteness: 4,
  Supplied: 5,
  Conflict: 6,
});

export const RECORD_KIND_LABELS = Object.freeze({
  [RecordKind.Failure]: "Failure",
  [RecordKind.Loss]: "Loss",
  [RecordKind.Convention]: "Convention",
  [RecordKind.Incompleteness]: "Incompleteness",
  [RecordKind.Supplied]: "Supplied",
  [RecordKind.Conflict]: "Conflict",
});

export const CatalogState = Object.freeze({
  NotConfigured: 0,
  Unused: 1,
  Reached: 2,
  Unreachable: 3,
});

export const MAPPING_FACT_CATEGORY_LABELS = Object.freeze({
  1: "table name",
  2: "schema name",
  3: "column name",
  4: "database type",
  5: "length",
  6: "precision and scale",
  7: "nullability",
  8: "primary key",
  9: "primary key strategy",
  10: "foreign key columns",
  11: "version column",
  12: "unique constraint",
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
 * GET /examples - the explanatory page's examples as whole conversion inputs (decision 099):
 * { key, sourceOrm, targetOrm, units }, each unit the { contentType, content, name } that
 * /convert takes, so the page sends it unchanged.
 */
export const getExamples = () => getJson("examples");

/**
 * POST /convert. Sources are { contentType, content } pairs; the response carries
 * runId, toolVersion, source/target framework with versions, sources (the generated
 * artifacts), records, catalogState, catalogReadMilliseconds and declaredSourceDialect.
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
