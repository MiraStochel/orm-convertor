/*
 * The one module that knows the API (decision 032): endpoint paths, DTO shapes and
 * mirrors of the server enums, which serialize as numbers. Every page imports from
 * here; nothing else states a path or an enum value.
 *
 * All paths are relative, so the deployment base path (/orm) is never written down.
 */

export const ORM = Object.freeze({ Dapper: 10, NHibernate: 20, EFCore: 30 });

export const ORM_LABELS = Object.freeze({
  [ORM.Dapper]: "Dapper",
  [ORM.NHibernate]: "NHibernate",
  [ORM.EFCore]: "EF Core",
});

export const ContentType = Object.freeze({
  CSharpEntity: 10,
  CSharpQuery: 20,
  Xml: 30,
  SqlQuery: 40,
  HqlQuery: 50,
});

export const CONTENT_TYPE_LABELS = Object.freeze({
  [ContentType.CSharpEntity]: "C# entity",
  [ContentType.CSharpQuery]: "C# query (LINQ)",
  [ContentType.Xml]: "XML mapping",
  [ContentType.SqlQuery]: "SQL query",
  [ContentType.HqlQuery]: "HQL query",
});

export const CONTENT_TYPE_EXTENSIONS = Object.freeze({
  [ContentType.CSharpEntity]: ".cs",
  [ContentType.CSharpQuery]: ".cs",
  [ContentType.Xml]: ".hbm.xml",
  [ContentType.SqlQuery]: ".sql",
  [ContentType.HqlQuery]: ".hql",
});

// HQL has no grammar of its own and is highlighted as SQL - an approximation,
// not a claim about the language (decision 032d).
export const CONTENT_TYPE_HIGHLIGHT = Object.freeze({
  [ContentType.CSharpEntity]: "csharp",
  [ContentType.CSharpQuery]: "csharp",
  [ContentType.Xml]: "xml",
  [ContentType.SqlQuery]: "sql",
  [ContentType.HqlQuery]: "sql",
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
 * POST /convert. Sources are { contentType, content } pairs; the response carries
 * runId, toolVersion, source/target framework with versions, sources (the generated
 * artifacts), records, catalogState and catalogReadMilliseconds.
 */
export const convert = async (sourceOrm, targetOrm, sources) =>
  (await post("convert", { sourceOrm, targetOrm, sources })).json();

/** POST /advisor/run - needs the native ILP solver, available only in the Docker image. */
export const runAdvisor = async (advisorRequest) =>
  (await post("advisor/run", advisorRequest)).json();

/** POST /archive - packs { name, content } files into a ZIP blob (decision 033). */
export const archive = async (files) => (await post("archive", { files })).blob();
