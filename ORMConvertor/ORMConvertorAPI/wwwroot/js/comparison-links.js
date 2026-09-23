/*
 * The authored half of the interactive comparison (decision 100). The artifacts in
 * `comparison-run.js` are a recording of the tool; everything here was written by hand for
 * the mockup, because the tool does not say which word of the output came from which word
 * of the input. That is the whole reason this page is a mockup and says so.
 *
 * A comparison is a row of windows: the source files on the left, the generated ones on the
 * right, both named by the keys of `RUN[key].files`.
 *
 * A link is one fact and the places it shows itself:
 *   kind    "carried" - both sides say it; "added" - only the output does; "dropped" - only
 *           the input does. Three values and no more: they describe the two texts, which is
 *           all a reader can check on the screen.
 *   label   the short name of the fact, shown in the caption and in the connections list.
 *   note    one sentence of the page's own explanation. Authored, like the link itself.
 *   record  index into `RUN[key].records` - the tool's own sentence about this fact, shown
 *           verbatim beside the note. Only where the run really carries such a record.
 *   token   + `files`: every occurrence of the token in those files, whole words only.
 *   spans   [{ file, find, nth, all }] where a token is not enough; `find` is literal text,
 *           `nth` picks the n-th occurrence (default 1), `all` takes every one.
 *
 * Overlapping spans are allowed and expected: a word can belong to two facts at once, and
 * the page marks the overlap as belonging to both.
 */

export const LINKS = {
  "efcore-to-nhibernate": {
    title: "Attributes become a mapping",
    lede: `Attributes on a C# class become an XML mapping beside a plainer class. Point at
      anything: the table, a length, the key, the generator NHibernate must name and EF Core
      never did, and the one word the database catalog put there.`,
    comparisons: [
      {
        title: "The entity becomes a class and a mapping",
        note: `One input file, two output files. The attributes leave the class and land in
          the mapping document; what stays in the class is what C# itself has to say.`,
        source: ["in/Customer.cs"],
        target: ["out/Customer.cs", "out/Customer.hbm.xml"],
        links: [
          {
            kind: "carried",
            label: "Namespace",
            note: "The namespace of the source class becomes the namespace of the mapping document.",
            token: "EFCoreEntities",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Entity Customer",
            note: "The class keeps its name, and the mapping names it once more as the mapped class.",
            token: "Customer",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "The attribute states both; the mapping writes them as attributes of the class element.",
            spans: [
              { file: "in/Customer.cs", find: '[Table("Customers", Schema = "Sales")]' },
              { file: "out/Customer.hbm.xml", find: 'table="Customers" schema="Sales"' },
            ],
          },
          {
            kind: "carried",
            label: "Primary key",
            note: "[Key] marks the property; the mapping says the same by mapping it with the id element rather than as a property.",
            spans: [
              { file: "in/Customer.cs", find: "[Key]" },
              { file: "out/Customer.hbm.xml", find: "<id" },
            ],
          },
          {
            kind: "added",
            label: "Key generator",
            note: "[Key] says nothing about how the value arises, so EF Core's own convention decides. NHibernate has to name a generator, and native is what that convention becomes here.",
            spans: [{ file: "out/Customer.hbm.xml", find: '<generator class="native" />' }],
          },
          {
            kind: "carried",
            label: "Property CustomerID",
            note: "The property keeps its name, and the mapping uses it twice: as the name of the mapped member and as the name of the column.",
            token: "CustomerID",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property CustomerName",
            note: "Same name on both sides; the column takes its name from the property, because the source never named a column.",
            token: "CustomerName",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property AccountOpenedDate",
            note: "Same name on both sides.",
            token: "AccountOpenedDate",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property CreditLimit",
            note: "Same name on both sides.",
            token: "CreditLimit",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Length 200",
            note: "The only length in the source, and the tool kept it against a catalog that said otherwise.",
            record: 2,
            spans: [
              { file: "in/Customer.cs", find: "[MaxLength(200)]" },
              { file: "out/Customer.hbm.xml", find: 'length="200"' },
            ],
          },
          {
            kind: "carried",
            label: "Database type of the date",
            note: "datetime2 and DateTime are one fact in two vocabularies: the mapping names the NHibernate type, not the column type.",
            record: 3,
            spans: [
              { file: "in/Customer.cs", find: '[Column(TypeName="datetime2")]' },
              { file: "out/Customer.hbm.xml", find: 'type="DateTime"' },
            ],
          },
          {
            kind: "carried",
            label: "Fractional seconds",
            note: "Seven digits of a second, stated by an attribute and written as an attribute of the property.",
            spans: [
              { file: "in/Customer.cs", find: "[Precision(7)]" },
              { file: "out/Customer.hbm.xml", find: 'precision="7"' },
            ],
          },
          {
            kind: "carried",
            label: "Database type of the amount",
            note: "The decimal column keeps its type under NHibernate's name for it.",
            spans: [
              { file: "in/Customer.cs", find: '[Column(TypeName="decimal")]' },
              { file: "out/Customer.hbm.xml", find: 'type="Decimal"' },
            ],
          },
          {
            kind: "carried",
            label: "Precision and scale",
            note: "One attribute with two numbers becomes two attributes with one number each.",
            spans: [
              { file: "in/Customer.cs", find: "[Precision(18, 2)]" },
              { file: "out/Customer.hbm.xml", find: 'precision="18" scale="2"' },
            ],
          },
          {
            kind: "carried",
            label: "Required, therefore not null",
            note: "C#'s required keyword is the source's whole statement about nullability; the mapping spells it out.",
            spans: [
              { file: "in/Customer.cs", find: "required", all: true },
              { file: "out/Customer.hbm.xml", find: 'not-null="true"', all: true },
            ],
          },
          {
            kind: "carried",
            label: "Nullable amount",
            note: "The question mark of the source type is the same fact as the attribute of the mapping.",
            spans: [
              { file: "in/Customer.cs", find: "decimal?" },
              { file: "out/Customer.cs", find: "decimal?" },
              { file: "out/Customer.hbm.xml", find: 'not-null="false"' },
            ],
          },
          {
            kind: "added",
            label: "Unique constraint from the catalog",
            note: "Nothing in the source says the name is unique. The database does, and the tool read it there.",
            record: 5,
            spans: [{ file: "out/Customer.hbm.xml", find: 'unique="true"' }],
          },
          {
            kind: "added",
            label: "virtual on every member",
            note: "NHibernate builds proxies over the class and needs every mapped member virtual. The source has no such word anywhere.",
            token: "virtual",
            files: ["out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "The collection of transactions",
            note: "A List property becomes an IList property and a bag in the mapping.",
            token: "Transactions",
            files: ["in/Customer.cs", "out/Customer.cs", "out/Customer.hbm.xml"],
          },
          {
            kind: "added",
            label: "Key column of the collection",
            note: "The other end of the collection is outside this conversion, so no column is known. The tool writes the owner's key column and records that it guessed.",
            record: 8,
            spans: [{ file: "out/Customer.hbm.xml", find: '<key column="CustomerID" />' }],
          },
          {
            kind: "dropped",
            label: "The initializer",
            note: "The source gives the collection an empty list; the generated class initializes it its own way, so the collection expression itself has no counterpart.",
            spans: [{ file: "in/Customer.cs", find: "= [];" }],
          },
        ],
      },
      {
        title: "The LINQ chain becomes HQL",
        note: `The method comes back as a method, and the chain of calls inside it as a query
          string. The tool returns the query twice: once inside the method that runs it, once
          as the bare HQL.`,
        source: ["in/CustomerQuery.cs"],
        target: ["out/query.cs", "out/query.hql"],
        links: [
          {
            kind: "carried",
            label: "The query root",
            note: "The set the chain starts from is the entity the query selects from.",
            spans: [
              { file: "in/CustomerQuery.cs", find: "ctx.Customers" },
              { file: "out/query.cs", find: "from Customer c" },
              { file: "out/query.hql", find: "from Customer c" },
            ],
          },
          {
            kind: "carried",
            label: "Filter on the credit limit",
            note: "The first Where becomes the first predicate of the where clause.",
            spans: [
              { file: "in/CustomerQuery.cs", find: ".Where(c => c.CreditLimit > 2000)" },
              { file: "out/query.cs", find: "c.CreditLimit > 2000" },
              { file: "out/query.hql", find: "c.CreditLimit > 2000" },
            ],
          },
          {
            kind: "carried",
            label: "Filter on the date",
            note: "A second Where over the same set is a second predicate, joined by and.",
            spans: [
              {
                file: "in/CustomerQuery.cs",
                find: ".Where(c => c.AccountOpenedDate > new System.DateTime(2025, 1, 1))",
              },
              { file: "out/query.cs", find: "c.AccountOpenedDate > '2025-01-01 00:00:00'" },
              { file: "out/query.hql", find: "c.AccountOpenedDate > '2025-01-01 00:00:00'" },
            ],
          },
          {
            kind: "carried",
            label: "The date literal",
            note: "A constructor call in C# is a quoted timestamp in HQL; the value is the same and the spelling is the target's.",
            spans: [
              { file: "in/CustomerQuery.cs", find: "new System.DateTime(2025, 1, 1)" },
              { file: "out/query.cs", find: "'2025-01-01 00:00:00'" },
              { file: "out/query.hql", find: "'2025-01-01 00:00:00'" },
            ],
          },
          {
            kind: "carried",
            label: "Ordering, descending",
            note: "OrderByDescending is the first ordering term.",
            spans: [
              { file: "in/CustomerQuery.cs", find: ".OrderByDescending(c => c.AccountOpenedDate)" },
              { file: "out/query.cs", find: "order by c.AccountOpenedDate desc" },
              { file: "out/query.hql", find: "order by c.AccountOpenedDate desc" },
            ],
          },
          {
            kind: "carried",
            label: "Ordering, the second term",
            note: "ThenBy is the second term of the same order by clause, ascending.",
            spans: [
              { file: "in/CustomerQuery.cs", find: ".ThenBy(c => c.CustomerName)" },
              { file: "out/query.cs", find: "c.CustomerName asc" },
              { file: "out/query.hql", find: "c.CustomerName asc" },
            ],
          },
          {
            kind: "carried",
            label: "The entity the query is about",
            note: "Named in the return type of the source method and as the entity of the from clause.",
            token: "Customer",
            files: ["in/CustomerQuery.cs", "out/query.cs", "out/query.hql"],
          },
          {
            kind: "added",
            label: "The session",
            note: "An NHibernate query is created from a session, so the generated method takes one. The source method reached for a context it had.",
            spans: [{ file: "out/query.cs", find: "ISession session" }],
          },
          {
            kind: "dropped",
            label: "Running the query",
            note: "The generated method hands back the query object; running it is the caller's business, so the call that materialised the list has no counterpart.",
            spans: [{ file: "in/CustomerQuery.cs", find: ".ToList()" }],
          },
        ],
      },
    ],
  },

  "dapper-to-efcore": {
    title: "A source that states little",
    lede: `The direction where the source states almost nothing: a plain class and a SQL
      string. Almost every word of the generated entity is therefore added rather than
      carried - and the records say where each one came from, which is nearly always the
      database catalog.`,
    comparisons: [
      {
        title: "A plain class becomes an annotated one",
        note: `The input has no table, no key, no column types - a Dapper entity is just a
          class. The output has all of them, and every one of them is a record.`,
        source: ["in/Customer.cs"],
        target: ["out/Customer.cs"],
        links: [
          {
            kind: "carried",
            label: "Namespace",
            note: "The one structural fact the source really does state.",
            token: "DapperEntities",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Entity Customer",
            note: "The class keeps its name.",
            token: "Customer",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
          {
            kind: "added",
            label: "Table and schema from the catalog",
            note: "Neither word is anywhere in the input. The catalog matched the class to a table and the tool wrote down which one.",
            record: 0,
            spans: [{ file: "out/Customer.cs", find: '[Table("Customers", Schema = "Sales")]' }],
          },
          {
            kind: "added",
            label: "Unique index from the catalog",
            note: "The database has a unique constraint on the name column; the generated entity declares it, with the constraint's own name.",
            record: 14,
            spans: [
              {
                file: "out/Customer.cs",
                find: '[Index(nameof(CustomerName), IsUnique = true, Name = "UQ_Sales_Customers_CustomerName")]',
              },
            ],
          },
          {
            kind: "added",
            label: "The primary key",
            note: "A Dapper class cannot mark a key. The catalog knows one and the entity gets it.",
            record: 13,
            spans: [{ file: "out/Customer.cs", find: "[Key]" }],
          },
          {
            kind: "carried",
            label: "Property CustomerID",
            note: "The name travels; everything else about this property was found elsewhere.",
            token: "CustomerID",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property CustomerName",
            note: "The name travels.",
            token: "CustomerName",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property AccountOpenedDate",
            note: "The name travels.",
            token: "AccountOpenedDate",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property CreditLimit",
            note: "The name travels.",
            token: "CreditLimit",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
          {
            kind: "added",
            label: "Column type of the key",
            note: "Read from the catalog column, not from the int of the source: the language type does not decide the column type.",
            record: 2,
            spans: [{ file: "out/Customer.cs", find: '[Column(TypeName="int")]' }],
          },
          {
            kind: "added",
            label: "Column type of the name",
            note: "The catalog column is nvarchar, which is a fact about the database, not about the C# string.",
            record: 4,
            spans: [{ file: "out/Customer.cs", find: '[Column(TypeName="nvarchar")]' }],
          },
          {
            kind: "added",
            label: "Length 100 from the catalog",
            note: "The source states no length at all. This is the real column's.",
            record: 5,
            spans: [{ file: "out/Customer.cs", find: "[MaxLength(100)]" }],
          },
          {
            kind: "added",
            label: "The date column",
            note: "A date, not a datetime: the catalog says so, and the difference matters when the entity is used to create the table.",
            record: 7,
            spans: [{ file: "out/Customer.cs", find: '[Column(TypeName="date")]' }],
          },
          {
            kind: "added",
            label: "The decimal column",
            note: "Type, precision and scale, all three from the catalog.",
            record: 10,
            spans: [
              { file: "out/Customer.cs", find: '[Column(TypeName="decimal")]' },
              { file: "out/Customer.cs", find: "[Precision(18, 2)]" },
            ],
          },
          {
            kind: "added",
            label: "Not null, from the catalog",
            note: "The source says required on one property only; the output says it on three, because the catalog columns are NOT NULL.",
            record: 3,
            spans: [{ file: "out/Customer.cs", find: "required", all: true }],
          },
          {
            kind: "carried",
            label: "The collection of transactions",
            note: "The property travels unchanged - and stays unmapped, because the entity it points at is not part of the conversion.",
            record: 16,
            token: "Transactions",
            files: ["in/Customer.cs", "out/Customer.cs"],
          },
        ],
      },
      {
        title: "SQL becomes LINQ",
        note: `The statement is read as a query, not copied as a string: the tool has to
          understand the select list to build a projection out of it.`,
        source: ["in/CustomerQuery.sql"],
        target: ["out/query.cs"],
        links: [
          {
            kind: "carried",
            label: "The table is the entity",
            note: "The from clause names a table; the generated query names the entity mapped to it.",
            spans: [
              { file: "in/CustomerQuery.sql", find: "FROM Sales.Customers AS c" },
              { file: "out/query.cs", find: "ctx.Set<Customer>()" },
            ],
          },
          {
            kind: "carried",
            label: "The select list is a projection",
            note: "Two columns in the select list become an anonymous type with the same two members.",
            spans: [
              { file: "in/CustomerQuery.sql", find: "SELECT c.CustomerName, c.CreditLimit" },
              {
                file: "out/query.cs",
                find: ".Select(c => new { CustomerName = c.CustomerName, CreditLimit = c.CreditLimit })",
              },
            ],
          },
          {
            kind: "carried",
            label: "The filter",
            note: "The where clause becomes a Where call with the same comparison.",
            spans: [
              { file: "in/CustomerQuery.sql", find: "WHERE c.CreditLimit > 2000" },
              { file: "out/query.cs", find: ".Where(c => c.CreditLimit > 2000)" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "ORDER BY ... DESC is OrderByDescending; the column it orders by is not even in the select list, and it does not have to be.",
            spans: [
              { file: "in/CustomerQuery.sql", find: "ORDER BY c.AccountOpenedDate DESC" },
              { file: "out/query.cs", find: ".OrderByDescending(c => c.AccountOpenedDate)" },
            ],
          },
          {
            kind: "carried",
            label: "CreditLimit",
            note: "The same column, in the select list, in the filter and in the projection.",
            token: "CreditLimit",
            files: ["in/CustomerQuery.sql", "out/query.cs"],
          },
          {
            kind: "carried",
            label: "CustomerName",
            note: "A column of the statement and a member of the projection.",
            token: "CustomerName",
            files: ["in/CustomerQuery.sql", "out/query.cs"],
          },
          {
            kind: "carried",
            label: "AccountOpenedDate",
            note: "Only ever used for ordering, on both sides.",
            token: "AccountOpenedDate",
            files: ["in/CustomerQuery.sql", "out/query.cs"],
          },
        ],
      },
    ],
  },

  "hibernate-to-eclipselink": {
    title: "One specification, two sets of defaults",
    lede: `Two implementations of one specification: everything the source states travels
      word for word, and the interesting places are the four where it states nothing and the
      two implementations would fill the gap differently.`,
    comparisons: [
      {
        title: "The same annotations, with the defaults written out",
        note: `Most of this file is identical on both sides - that is what a shared
          specification buys. Point at the key, at the national column and at the
          fractional seconds to see where the two implementations part company.`,
        source: ["in/Customer.java"],
        target: ["out/Customer.java"],
        links: [
          {
            kind: "carried",
            label: "Package",
            note: "Unchanged, like most of the file.",
            token: "HibernateEntities",
            files: ["in/Customer.java", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Entity Customer",
            note: "The annotation and the class name travel unchanged.",
            token: "Customer",
            files: ["in/Customer.java", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Identical on both sides; the target only appends to the annotation.",
            spans: [
              { file: "in/Customer.java", find: '@Table(name = "Customers", schema = "Sales")' },
              { file: "out/Customer.java", find: '@Table(name = "Customers", schema = "Sales"' },
            ],
          },
          {
            kind: "added",
            label: "Unique constraint from the catalog",
            note: "Nothing in the source says the name is unique; the database says so and the tool read it there.",
            record: 9,
            spans: [
              {
                file: "out/Customer.java",
                find: 'uniqueConstraints = { @UniqueConstraint(name = "UQ_Sales_Customers_CustomerName", columnNames = { "CustomerName" }) }',
              },
            ],
          },
          {
            kind: "carried",
            label: "The key",
            note: "One annotation, the same on both sides.",
            spans: [
              { file: "in/Customer.java", find: "@Id" },
              { file: "out/Customer.java", find: "@Id" },
            ],
          },
          {
            kind: "added",
            label: "The generator, spelled out",
            note: "A bare @GeneratedValue means AUTO, and AUTO means a sequence to one implementation and a counter table to the other. The artifact therefore writes the mechanism out rather than leaving a word that would change meaning.",
            record: 11,
            spans: [
              {
                file: "out/Customer.java",
                find: '(strategy = GenerationType.TABLE, generator = "Customer_CustomerID_gen")',
              },
              {
                file: "out/Customer.java",
                find: '@TableGenerator(name = "Customer_CustomerID_gen", table = "SEQUENCE", pkColumnName = "SEQ_NAME", valueColumnName = "SEQ_COUNT", pkColumnValue = "SEQ_GEN", allocationSize = 50)',
              },
            ],
          },
          {
            kind: "carried",
            label: "@GeneratedValue",
            note: "The source states that the framework generates the value; the target keeps the annotation and adds how.",
            spans: [
              { file: "in/Customer.java", find: "@GeneratedValue" },
              { file: "out/Customer.java", find: "@GeneratedValue" },
            ],
          },
          {
            kind: "carried",
            label: "National characters",
            note: "@Nationalized is Hibernate's own annotation and EclipseLink has none. The fact survives as a literal column type, which is the only place left to put it.",
            spans: [
              { file: "in/Customer.java", find: "@Nationalized" },
              { file: "out/Customer.java", find: 'columnDefinition = "nvarchar(200)"' },
            ],
          },
          {
            kind: "dropped",
            label: "The vendor import",
            note: "With the annotation gone, the import of Hibernate's own package goes with it - the generated file imports nothing outside the specification.",
            spans: [
              { file: "in/Customer.java", find: "import org.hibernate.annotations.Nationalized;" },
            ],
          },
          {
            kind: "carried",
            label: "Property CustomerID",
            note: "Field and column name, unchanged.",
            token: "CustomerID",
            files: ["in/Customer.java", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Property CustomerName",
            note: "Field and column name, unchanged.",
            token: "CustomerName",
            files: ["in/Customer.java", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Property AccountOpenedDate",
            note: "Field and column name, unchanged.",
            token: "AccountOpenedDate",
            files: ["in/Customer.java", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Property CreditLimit",
            note: "Field and column name, unchanged.",
            token: "CreditLimit",
            files: ["in/Customer.java", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Length 200",
            note: "Kept as stated, and kept against a catalog that says the column is shorter.",
            record: 3,
            spans: [
              { file: "in/Customer.java", find: "length = 200" },
              { file: "out/Customer.java", find: "length = 200" },
            ],
          },
          {
            kind: "carried",
            label: "Not null",
            note: "Two properties state it and both keep it.",
            spans: [
              { file: "in/Customer.java", find: "nullable = false", all: true },
              { file: "out/Customer.java", find: "nullable = false", all: true },
            ],
          },
          {
            kind: "added",
            label: "Nullable, from the catalog",
            note: "The source leaves the amount unsaid; the catalog column allows NULL and the artifact states it.",
            record: 6,
            spans: [{ file: "out/Customer.java", find: "nullable = true" }],
          },
          {
            kind: "carried",
            label: "Precision and scale",
            note: "Unchanged.",
            spans: [
              { file: "in/Customer.java", find: "precision = 18, scale = 2" },
              { file: "out/Customer.java", find: "precision = 18, scale = 2" },
            ],
          },
          {
            kind: "dropped",
            label: "Fractional seconds",
            note: "The catalog says the column is a date, and a date has no fractional second. The annotation has no attribute that would reach it, so the number is dropped and recorded.",
            record: 12,
            spans: [{ file: "in/Customer.java", find: "secondPrecision = 7, " }],
          },
          {
            kind: "carried",
            label: "The version column",
            note: "Optimistic locking is in the specification, so it travels as it stands.",
            spans: [
              { file: "in/Customer.java", find: "@Version" },
              { file: "out/Customer.java", find: "@Version" },
              { file: "in/Customer.java", find: "Revision", all: true },
              { file: "out/Customer.java", find: "Revision", all: true },
            ],
          },
        ],
      },
      {
        title: "The query loses its page and gets it back",
        note: `The query text is identical except for one line. Point at it: limit and offset
          exist in HQL and not in JPQL, so the page moves out of the string and onto the
          query object.`,
        source: ["in/CustomerPage.jpql"],
        target: ["out/query.java", "out/query.jpql"],
        links: [
          {
            kind: "carried",
            label: "The selection",
            note: "Unchanged.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "select c" },
              { file: "out/query.java", find: "select c" },
              { file: "out/query.jpql", find: "select c" },
            ],
          },
          {
            kind: "carried",
            label: "The queried entity",
            note: "Unchanged - and named once more in Java, because a typed query needs the class.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "from Customer c" },
              { file: "out/query.java", find: "from Customer c" },
              { file: "out/query.jpql", find: "from Customer c" },
              { file: "out/query.java", find: "Customer.class" },
              { file: "out/query.java", find: "TypedQuery<Customer>" },
            ],
          },
          {
            kind: "carried",
            label: "The filter",
            note: "Unchanged, parameter and all.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "where c.CreditLimit > :minimumCreditLimit" },
              { file: "out/query.java", find: "where c.CreditLimit > :minimumCreditLimit" },
              { file: "out/query.jpql", find: "where c.CreditLimit > :minimumCreditLimit" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter",
            note: "A named parameter of the query becomes a parameter of the method and a call that binds it; its type comes from the mapping of the property it is compared with.",
            spans: [
              { file: "in/CustomerPage.jpql", find: ":minimumCreditLimit" },
              { file: "out/query.jpql", find: ":minimumCreditLimit" },
              { file: "out/query.java", find: ":minimumCreditLimit" },
              { file: "out/query.java", find: "BigDecimal minimumCreditLimit" },
              {
                file: "out/query.java",
                find: '.setParameter("minimumCreditLimit", minimumCreditLimit)',
              },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Unchanged.",
            spans: [
              {
                file: "in/CustomerPage.jpql",
                find: "order by c.CreditLimit desc, c.CustomerName asc",
              },
              {
                file: "out/query.java",
                find: "order by c.CreditLimit desc, c.CustomerName asc",
              },
              {
                file: "out/query.jpql",
                find: "order by c.CreditLimit desc, c.CustomerName asc",
              },
            ],
          },
          {
            kind: "carried",
            label: "The page",
            note: "The clause only HQL has becomes two calls on the query object, and the two numbers swap places: offset is the first result, limit the maximum count.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "limit 10 offset 20" },
              { file: "out/query.java", find: ".setFirstResult(20)" },
              { file: "out/query.java", find: ".setMaxResults(10)" },
            ],
          },
          {
            kind: "added",
            label: "The entity manager",
            note: "A JPA query is created from an entity manager, so the generated method takes one.",
            spans: [
              { file: "out/query.java", find: "EntityManager em" },
              { file: "out/query.java", find: "em.createQuery" },
            ],
          },
        ],
      },
    ],
  },

  "hibernate-to-mybatis": {
    title: "From an ORM to a SQL mapper",
    lede: `The same input towards the other end of the Java ecosystem. MyBatis maps results,
      not tables, so most of what the annotations state has nowhere to go - and the page
      shows it as what it is: a line in the source with nothing on the other side.`,
    comparisons: [
      {
        title: "The entity keeps its fields and loses its mapping",
        note: `The class stops importing the framework altogether; what remains of the
          mapping is pairs of column and property in a result map.`,
        source: ["in/Customer.java"],
        target: ["out/Customer.java", "out/CustomerMapper.xml"],
        links: [
          {
            kind: "carried",
            label: "Package",
            note: "The package travels, and the mapper's namespace is built from it.",
            token: "HibernateEntities",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "carried",
            label: "Entity Customer",
            note: "The class keeps its name; the result map is named after it and its type names it again.",
            token: "Customer",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "dropped",
            label: "The table and the schema",
            note: "A result map says how a row becomes an object and never which table the row came from. Both facts are dropped, one record each.",
            record: 6,
            spans: [
              { file: "in/Customer.java", find: '@Table(name = "Customers", schema = "Sales")' },
            ],
          },
          {
            kind: "carried",
            label: "The key property",
            note: "The key becomes the id element - which for MyBatis marks the identity of a result row rather than the primary key of a table. It is the closest thing the target has.",
            spans: [
              { file: "in/Customer.java", find: "@Id" },
              { file: "out/CustomerMapper.xml", find: "<id" },
            ],
          },
          {
            kind: "dropped",
            label: "Key generation",
            note: "Who produces the key value is not a question a result map can answer.",
            record: 14,
            spans: [{ file: "in/Customer.java", find: "@GeneratedValue" }],
          },
          {
            kind: "carried",
            label: "Property CustomerID",
            note: "Column and property, the pair a result map is made of.",
            token: "CustomerID",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "carried",
            label: "Property CustomerName",
            note: "Column and property.",
            token: "CustomerName",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "carried",
            label: "Property AccountOpenedDate",
            note: "Column and property.",
            token: "AccountOpenedDate",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "carried",
            label: "Property CreditLimit",
            note: "Column and property.",
            token: "CreditLimit",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "carried",
            label: "National characters",
            note: "The vendor annotation has no counterpart in MyBatis either, but the JDBC type does carry the distinction - so this one fact survives the crossing.",
            spans: [
              { file: "in/Customer.java", find: "@Nationalized" },
              { file: "out/CustomerMapper.xml", find: 'jdbcType="NVARCHAR"' },
            ],
          },
          {
            kind: "added",
            label: "JDBC type of the key",
            note: "The source states no database type for it; the catalog does, and the result map writes it as a JDBC type.",
            record: 0,
            spans: [{ file: "out/CustomerMapper.xml", find: 'jdbcType="INTEGER"' }],
          },
          {
            kind: "added",
            label: "JDBC type of the date",
            note: "From the catalog: the column is a date, which is why the result map does not say TIMESTAMP.",
            record: 2,
            spans: [{ file: "out/CustomerMapper.xml", find: 'jdbcType="DATE"' }],
          },
          {
            kind: "added",
            label: "JDBC type of the amount",
            note: "From the catalog.",
            record: 3,
            spans: [{ file: "out/CustomerMapper.xml", find: 'jdbcType="DECIMAL"' }],
          },
          {
            kind: "dropped",
            label: "Length",
            note: "A result map has no place for the width of a column.",
            record: 8,
            spans: [{ file: "in/Customer.java", find: "length = 200, " }],
          },
          {
            kind: "dropped",
            label: "Nullability",
            note: "Three properties state it and none of the three can keep it.",
            record: 11,
            spans: [{ file: "in/Customer.java", find: "nullable = false", all: true }],
          },
          {
            kind: "dropped",
            label: "Fractional seconds",
            note: "Dropped with the rest of the column facts.",
            record: 9,
            spans: [{ file: "in/Customer.java", find: "secondPrecision = 7, " }],
          },
          {
            kind: "dropped",
            label: "Precision and scale",
            note: "Dropped with the rest of the column facts.",
            record: 10,
            spans: [{ file: "in/Customer.java", find: "precision = 18, scale = 2" }],
          },
          {
            kind: "dropped",
            label: "The version column",
            note: "Optimistic locking is a framework's business, and MyBatis does not have that business.",
            record: 15,
            spans: [{ file: "in/Customer.java", find: "@Version" }],
          },
          {
            kind: "carried",
            label: "The version property",
            note: "The property itself survives as an ordinary pair of column and property - only what it meant is gone. Its JDBC type is missing because the catalog has no column of that name.",
            record: 4,
            token: "Revision",
            files: ["in/Customer.java", "out/Customer.java", "out/CustomerMapper.xml"],
          },
          {
            kind: "added",
            label: "The mapper and its result map",
            note: "MyBatis needs a document with a namespace and a result map that says the mapping is not automatic. Neither is a fact of the source.",
            spans: [
              { file: "out/CustomerMapper.xml", find: '<mapper namespace="HibernateEntities.CustomerMapper">' },
              { file: "out/CustomerMapper.xml", find: 'autoMapping="false"' },
            ],
          },
        ],
      },
      {
        title: "JPQL becomes SQL in a mapper",
        note: `Here the table comes back: a statement has to name one, so the entity is
          resolved to the table the mapping knew. The parameter changes spelling and the page
          becomes a clause of T-SQL.`,
        source: ["in/CustomerPage.jpql"],
        target: ["out/query.java", "out/QueryMapper.xml"],
        links: [
          {
            kind: "carried",
            label: "The entity becomes the table",
            note: "The from clause of JPQL names an entity; a MyBatis statement is SQL and has to name the table - so the fact the entity mapping carried is spent here.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "from Customer c" },
              { file: "out/QueryMapper.xml", find: "FROM Sales.Customers AS c" },
            ],
          },
          {
            kind: "carried",
            label: "The selection",
            note: "Selecting the whole entity becomes selecting every column; which class the row becomes is said by the result type instead.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "select c" },
              { file: "out/QueryMapper.xml", find: "SELECT *" },
              { file: "out/QueryMapper.xml", find: 'resultType="HibernateEntities.Customer"' },
              { file: "out/query.java", find: "List<Customer>" },
            ],
          },
          {
            kind: "carried",
            label: "The filter",
            note: "The comparison travels; the greater-than sign has to be escaped, because the statement now lives inside XML.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "where c.CreditLimit > :minimumCreditLimit" },
              { file: "out/QueryMapper.xml", find: "WHERE c.CreditLimit &gt; #{minimumCreditLimit}" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter",
            note: "One named parameter in three spellings: the colon of JPQL, the hash braces of MyBatis and an annotated argument of the mapper method, typed from the mapping.",
            spans: [
              { file: "in/CustomerPage.jpql", find: ":minimumCreditLimit" },
              { file: "out/QueryMapper.xml", find: "#{minimumCreditLimit}" },
              { file: "out/query.java", find: '@Param("minimumCreditLimit") BigDecimal minimumCreditLimit' },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Two terms, the same two, in SQL's spelling.",
            spans: [
              {
                file: "in/CustomerPage.jpql",
                find: "order by c.CreditLimit desc, c.CustomerName asc",
              },
              {
                file: "out/QueryMapper.xml",
                find: "ORDER BY c.CreditLimit DESC, c.CustomerName ASC",
              },
            ],
          },
          {
            kind: "carried",
            label: "The page",
            note: "limit and offset become the T-SQL clause that says the same thing - the dialect of the target, which the artifact thereby commits to.",
            spans: [
              { file: "in/CustomerPage.jpql", find: "limit 10 offset 20" },
              { file: "out/QueryMapper.xml", find: "OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY" },
            ],
          },
          {
            kind: "added",
            label: "The statement and the method that calls it",
            note: "A MyBatis query is a statement with an id in a mapper document plus a method of the same name on the interface. The source had one file and no names to give either.",
            spans: [
              { file: "out/QueryMapper.xml", find: '<select id="query"' },
              { file: "out/query.java", find: "query(" },
              { file: "out/QueryMapper.xml", find: '<mapper namespace="HibernateEntities.QueryMapper">' },
            ],
          },
        ],
      },
    ],
  },

  "mybatis-to-dapper": {
    title: "SQL stays SQL",
    lede: `Two frameworks that both keep SQL as it is, one on each side of the boundary
      between the ecosystems. The statements travel almost word for word; what does not
      travel is everything the result map said about columns, because a Dapper class has
      nowhere to put it.`,
    comparisons: [
      {
        title: "Two input files, one plain class",
        note: `The class gives the properties and the result map gives their columns and
          types. Only the first of those reaches the output: point at any column name to
          see a line with nothing on the other side.`,
        source: ["in/Customer.java", "in/CustomerMapper.xml"],
        target: ["out/Customer.cs"],
        links: [
          {
            kind: "carried",
            label: "Package becomes namespace",
            note: "The Java package and the C# namespace are the same fact in two languages.",
            token: "MyBatisEntities",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Entity Customer",
            note: "Named by the class, by the result map's type, and by the generated class.",
            token: "Customer",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property CustomerID",
            note: "The property travels; the column it was mapped to does not.",
            token: "CustomerID",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property CustomerName",
            note: "The property travels.",
            token: "CustomerName",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property AccountOpenedDate",
            note: "The property travels; the Java date-time type becomes the C# one.",
            token: "AccountOpenedDate",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "carried",
            label: "Property CreditLimit",
            note: "BigDecimal and decimal are the same scalar in the two type systems.",
            token: "CreditLimit",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "dropped",
            label: "The column names",
            note: "A Dapper entity is a plain class: it has no attribute, no mapping file and no other place where a column name could go. Four properties, four records.",
            record: 4,
            spans: [
              { file: "in/CustomerMapper.xml", find: 'column="CustomerID"' },
              { file: "in/CustomerMapper.xml", find: 'column="CustomerName"' },
              { file: "in/CustomerMapper.xml", find: 'column="AccountOpenedDate"' },
              { file: "in/CustomerMapper.xml", find: 'column="CreditLimit"' },
            ],
          },
          {
            kind: "dropped",
            label: "The JDBC types",
            note: "Dropped for the same reason as the column names.",
            record: 8,
            spans: [
              { file: "in/CustomerMapper.xml", find: 'jdbcType="NVARCHAR"' },
              { file: "in/CustomerMapper.xml", find: 'jdbcType="TIMESTAMP"' },
              { file: "in/CustomerMapper.xml", find: 'jdbcType="DECIMAL"' },
            ],
          },
          {
            kind: "dropped",
            label: "The id element",
            note: "MyBatis means by it the identity of a result row, not the primary key of a table - so the tool refuses to read a key out of it and leaves that to a catalog.",
            record: 2,
            spans: [{ file: "in/CustomerMapper.xml", find: "<id" }],
          },
          {
            kind: "carried",
            label: "The collection of transactions",
            note: "The property survives, and it is all that survives: the entity it points at is not part of this conversion, so nothing could be resolved against it.",
            record: 3,
            token: "Transactions",
            files: ["in/Customer.java", "in/CustomerMapper.xml", "out/Customer.cs"],
          },
          {
            kind: "dropped",
            label: "How the collection is loaded",
            note: "The result map fills the navigation by running another statement. That is a statement about loading, not about mapping, and the intermediate representation carries no such thing.",
            record: 1,
            spans: [
              { file: "in/CustomerMapper.xml", find: 'select="findTransactions" column="CustomerID"' },
            ],
          },
          {
            kind: "dropped",
            label: "The initializer",
            note: "new ArrayList<>() is not a literal both languages spell alike, so the generated property is left uninitialized.",
            record: 0,
            spans: [{ file: "in/Customer.java", find: "= new ArrayList<>();" }],
          },
          {
            kind: "added",
            label: "Everything admits null",
            note: "Nothing in a result map says a column is NOT NULL, so every generated property is nullable. The question marks are the absence of a fact, not a fact.",
            spans: [
              { file: "out/Customer.cs", find: "int?" },
              { file: "out/Customer.cs", find: "string?" },
              { file: "out/Customer.cs", find: "DateTime?" },
              { file: "out/Customer.cs", find: "decimal?" },
              { file: "out/Customer.cs", find: "List<CustomerTransaction>?" },
            ],
          },
        ],
      },
      {
        title: "The first statement becomes a Dapper method",
        note: `The SQL crosses the boundary as T-SQL and stays readable. What changes is
          everything around it: the fragment is expanded, the parameter changes sigil, and
          the statement's id becomes a method name.`,
        source: ["in/CustomerMapper.xml", "in/CustomerMapper.java"],
        target: ["out/query.cs", "out/query.sql"],
        links: [
          {
            kind: "carried",
            label: "The statement is the method",
            note: "The id of the statement and the method of the mapper interface are one name, and the generated method carries it in C#'s spelling.",
            spans: [
              { file: "in/CustomerMapper.xml", find: 'id="findByCreditLimit"' },
              { file: "in/CustomerMapper.java", find: "findByCreditLimit" },
              { file: "out/query.cs", find: "FindByCreditLimit" },
            ],
          },
          {
            kind: "carried",
            label: "The included fragment",
            note: "The column list lives in a fragment of its own and is included by reference. Dapper has no such mechanism, so the tool expands it where it was included - the same columns, in the same order.",
            spans: [
              {
                file: "in/CustomerMapper.xml",
                find: "c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit",
              },
              { file: "in/CustomerMapper.xml", find: '<include refid="customerColumns"/>' },
              {
                file: "out/query.cs",
                find: "SELECT c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit",
              },
              {
                file: "out/query.sql",
                find: "SELECT c.CustomerID, c.CustomerName, c.AccountOpenedDate, c.CreditLimit",
              },
            ],
          },
          {
            kind: "carried",
            label: "The table",
            note: "Unchanged - both frameworks write SQL and mean the same by it.",
            spans: [
              { file: "in/CustomerMapper.xml", find: "FROM Sales.Customers AS c" },
              { file: "out/query.cs", find: "FROM Sales.Customers AS c" },
              { file: "out/query.sql", find: "FROM Sales.Customers AS c" },
            ],
          },
          {
            kind: "carried",
            label: "The where element",
            note: "MyBatis builds the keyword itself, because the conditions inside may be optional. Here they are not, so it becomes a plain WHERE.",
            spans: [
              { file: "in/CustomerMapper.xml", find: "<where>" },
              { file: "in/CustomerMapper.xml", find: "c.CreditLimit &gt; #{creditLimit}" },
              { file: "out/query.cs", find: "WHERE c.CreditLimit > @creditLimit" },
              { file: "out/query.sql", find: "WHERE c.CreditLimit > @creditLimit" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter and its type",
            note: "The sigil changes and the type comes from a different file: the SQL never says what creditLimit is, the Java interface does, and BigDecimal becomes decimal.",
            spans: [
              { file: "in/CustomerMapper.xml", find: "#{creditLimit}" },
              { file: "in/CustomerMapper.java", find: '@Param("creditLimit") BigDecimal creditLimit' },
              { file: "out/query.cs", find: "decimal creditLimit" },
              { file: "out/query.cs", find: "@creditLimit" },
              { file: "out/query.cs", find: "new { creditLimit }" },
              { file: "out/query.sql", find: "@creditLimit" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Unchanged.",
            spans: [
              {
                file: "in/CustomerMapper.xml",
                find: "ORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC",
              },
              {
                file: "out/query.cs",
                find: "ORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC",
              },
              {
                file: "out/query.sql",
                find: "ORDER BY c.AccountOpenedDate DESC, c.CustomerName ASC",
              },
            ],
          },
          {
            kind: "dropped",
            label: "The result map of the statement",
            note: "Which result map turns the rows into objects is a fact about mapping that the query representation does not carry.",
            record: 11,
            spans: [{ file: "in/CustomerMapper.xml", find: 'resultMap="customer"' }],
          },
          {
            kind: "added",
            label: "The result type",
            note: "Dapper needs a type to read the rows into, and the tool derives it from the table the statement selects from - a convention, not something the query said.",
            record: 12,
            spans: [
              { file: "out/query.cs", find: "List<Customer>" },
              { file: "out/query.cs", find: "Query<Customer>" },
            ],
          },
          {
            kind: "added",
            label: "The connection",
            note: "A Dapper call is an extension method on a connection, so the generated method takes one.",
            spans: [{ file: "out/query.cs", find: "IDbConnection connection" }],
          },
        ],
      },
      {
        title: "The second statement, with the same rules",
        note: `The mapper carries two statements and each becomes its own method. This one
          selects into an entity that is not part of the conversion at all.`,
        source: ["in/CustomerMapper.xml", "in/CustomerMapper.java"],
        target: ["out/query-2.cs", "out/query-2.sql"],
        links: [
          {
            kind: "carried",
            label: "The statement is the method",
            note: "Same rule as the first: the id is the name.",
            spans: [
              { file: "in/CustomerMapper.xml", find: 'id="findTransactions"' },
              { file: "in/CustomerMapper.java", find: "findTransactions" },
              { file: "out/query-2.cs", find: "FindTransactions" },
            ],
          },
          {
            kind: "carried",
            label: "The select list",
            note: "Two columns, unchanged.",
            spans: [
              { file: "in/CustomerMapper.xml", find: "SELECT t.TransactionID, t.Amount" },
              { file: "out/query-2.cs", find: "SELECT t.TransactionID, t.Amount" },
              { file: "out/query-2.sql", find: "SELECT t.TransactionID, t.Amount" },
            ],
          },
          {
            kind: "carried",
            label: "The table",
            note: "Unchanged.",
            spans: [
              { file: "in/CustomerMapper.xml", find: "FROM Sales.CustomerTransactions AS t" },
              { file: "out/query-2.cs", find: "FROM Sales.CustomerTransactions AS t" },
              { file: "out/query-2.sql", find: "FROM Sales.CustomerTransactions AS t" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter and its type",
            note: "Integer in the interface becomes int in the method; the SQL only ever knew the name.",
            spans: [
              { file: "in/CustomerMapper.xml", find: "#{customerId}" },
              { file: "in/CustomerMapper.java", find: '@Param("customerId") Integer customerId' },
              { file: "out/query-2.cs", find: "int customerId" },
              { file: "out/query-2.cs", find: "@customerId" },
              { file: "out/query-2.cs", find: "new { customerId }" },
              { file: "out/query-2.sql", find: "@customerId" },
            ],
          },
          {
            kind: "dropped",
            label: "The result type of the statement",
            note: "Stated by the statement, not carried by the query representation - the same loss as the result map of the first statement.",
            record: 13,
            spans: [{ file: "in/CustomerMapper.xml", find: 'resultType="CustomerTransaction"' }],
          },
          {
            kind: "added",
            label: "The result type",
            note: "Derived from the table name again, which is why an entity nobody declared still gets a name here.",
            record: 14,
            spans: [
              { file: "out/query-2.cs", find: "List<CustomerTransaction>" },
              { file: "out/query-2.cs", find: "Query<CustomerTransaction>" },
            ],
          },
        ],
      },
    ],
  },

  "order-book": {
    title: "Order book",
    lede: `A whole domain rather than a single class: four entities and six queries, from
      .NET to Java. Phases run here that one class never starts - a foreign key is paired
      with the key of the entity it points at, a composite key gets the class the target
      requires, and a grouping followed by a filter becomes a having clause.`,
    comparisons: [
      {
        title: "Customer: attributes become annotations",
        note: `The straightforward entity of the four. Two of its attributes are worth
          pointing at: the one that marks the row version and the one that says a property
          is not mapped at all.`,
        source: ["in/Customer.cs"],
        target: ["out/Customer.java"],
        links: [
          {
            kind: "carried",
            label: "Namespace becomes package",
            note: "The same word, in each language's own keyword.",
            token: "OrderBook",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Entity Customer",
            note: "The class keeps its name.",
            token: "Customer",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Both stated by the attribute, both written into the annotation.",
            spans: [
              { file: "in/Customer.cs", find: '[Table("Customers", Schema = "Ordering")]' },
              { file: "out/Customer.java", find: '@Table(name = "Customers", schema = "Ordering"' },
            ],
          },
          {
            kind: "carried",
            label: "The unique index",
            note: "A unique index is a mapping fact, so it travels - with its name, because the source gave it one.",
            spans: [
              {
                file: "in/Customer.cs",
                find: '[Index(nameof(Email), IsUnique = true, Name = "UQ_Customers_Email")]',
              },
              {
                file: "out/Customer.java",
                find: 'uniqueConstraints = { @UniqueConstraint(name = "UQ_Customers_Email", columnNames = { "Email" }) }',
              },
            ],
          },
          {
            kind: "carried",
            label: "The key",
            note: "One attribute, one annotation.",
            spans: [
              { file: "in/Customer.cs", find: "[Key]" },
              { file: "out/Customer.java", find: "@Id" },
            ],
          },
          {
            kind: "added",
            label: "The sequence",
            note: "[Key] leaves the mechanism to the framework. Hibernate resolves that to a sequence on the pinned dialect, and the artifact writes it out rather than leaving a word that means something else under the sibling implementation.",
            record: 5,
            spans: [
              {
                file: "out/Customer.java",
                find: '@GeneratedValue(strategy = GenerationType.SEQUENCE, generator = "Customer_CustomerId_gen")',
              },
              {
                file: "out/Customer.java",
                find: '@SequenceGenerator(name = "Customer_CustomerId_gen", sequenceName = "Customer_SEQ", allocationSize = 50)',
              },
            ],
          },
          {
            kind: "carried",
            label: "Property CustomerId",
            note: "Property and column name.",
            token: "CustomerId",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Property Name",
            note: "The column takes its name from the property, because the source named no column.",
            spans: [
              { file: "in/Customer.cs", find: "string Name" },
              { file: "out/Customer.java", find: 'name = "Name"' },
              { file: "out/Customer.java", find: "String Name" },
            ],
          },
          {
            kind: "carried",
            label: "Property Email",
            note: "Named three times on the right: as the column, as the field and in the unique constraint.",
            token: "Email",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Property CreditLimit",
            note: "Property and column name.",
            token: "CreditLimit",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Property CustomerSince",
            note: "A DateOnly becomes a LocalDate: the same scalar under two names.",
            token: "CustomerSince",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "carried",
            label: "Lengths",
            note: "Both lengths travel, each onto its own column annotation.",
            spans: [
              { file: "in/Customer.cs", find: "[MaxLength(100)]" },
              { file: "in/Customer.cs", find: "[MaxLength(254)]" },
              { file: "out/Customer.java", find: "length = 100" },
              { file: "out/Customer.java", find: "length = 254" },
            ],
          },
          {
            kind: "carried",
            label: "Precision and scale",
            note: "One attribute with two numbers becomes two attributes of the column annotation.",
            spans: [
              { file: "in/Customer.cs", find: "[Precision(18, 2)]" },
              { file: "out/Customer.java", find: "precision = 18, scale = 2" },
            ],
          },
          {
            kind: "carried",
            label: "Required, therefore not null",
            note: "Two properties are required and both become not-null columns.",
            spans: [
              { file: "in/Customer.cs", find: "required", all: true },
              { file: "out/Customer.java", find: "nullable = false", all: true },
            ],
          },
          {
            kind: "carried",
            label: "The row version",
            note: "EF Core marks the concurrency token with [Timestamp]; JPA calls the same thing @Version.",
            spans: [
              { file: "in/Customer.cs", find: "[Timestamp]" },
              { file: "out/Customer.java", find: "@Version" },
            ],
          },
          {
            kind: "carried",
            label: "The unmapped property",
            note: "A property the mapping must ignore: one word on each side, and the class keeps the member either way.",
            spans: [
              { file: "in/Customer.cs", find: "[NotMapped]" },
              { file: "out/Customer.java", find: "@Transient" },
            ],
          },
          {
            kind: "carried",
            label: "The collection of orders",
            note: "The navigation travels, and the annotation names the field on the other side that owns the foreign key.",
            token: "Orders",
            files: ["in/Customer.cs", "out/Customer.java"],
          },
          {
            kind: "dropped",
            label: "The initializer of the row version",
            note: "An empty collection expression is not a literal both languages spell alike, so the byte array is left uninitialized.",
            record: 6,
            spans: [{ file: "in/Customer.cs", find: "= [];", nth: 1 }],
          },
          {
            kind: "carried",
            label: "The initializer of the collection",
            note: "This one does survive: an empty list is something both languages can write.",
            spans: [
              { file: "in/Customer.cs", find: "= [];", nth: 2 },
              { file: "out/Customer.java", find: "= new ArrayList<>();" },
            ],
          },
        ],
      },
      {
        title: "SalesOrder: a relation resolved against another file",
        note: `The second source window is here because the conversion needs it: the foreign
          key names a property of this class, and the column it points at is the key of the
          other one. That pairing is why a domain is converted as a whole.`,
        source: ["in/SalesOrder.cs", "in/Customer.cs"],
        target: ["out/SalesOrder.java"],
        links: [
          {
            kind: "carried",
            label: "Entity SalesOrder",
            note: "The class keeps its name.",
            token: "SalesOrder",
            files: ["in/SalesOrder.cs", "out/SalesOrder.java"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Stated, and written out.",
            spans: [
              { file: "in/SalesOrder.cs", find: '[Table("SalesOrders", Schema = "Ordering")]' },
              {
                file: "out/SalesOrder.java",
                find: '@Table(name = "SalesOrders", schema = "Ordering"',
              },
            ],
          },
          {
            kind: "carried",
            label: "The unique index, unnamed",
            note: "The source named no constraint, so the target writes none - the columns alone.",
            spans: [
              { file: "in/SalesOrder.cs", find: "[Index(nameof(OrderNumber), IsUnique = true)]" },
              {
                file: "out/SalesOrder.java",
                find: 'uniqueConstraints = { @UniqueConstraint(columnNames = { "OrderNumber" }) }',
              },
            ],
          },
          {
            kind: "dropped",
            label: "The index that is not unique",
            note: "A plain index speeds queries up and states nothing about the mapping, so the intermediate representation does not carry it.",
            record: 0,
            spans: [{ file: "in/SalesOrder.cs", find: "[Index(nameof(OrderedAt))]" }],
          },
          {
            kind: "carried",
            label: "The relation to the customer",
            note: "The navigation property and its foreign key attribute become a many-to-one with a join column.",
            spans: [
              { file: "in/SalesOrder.cs", find: "[ForeignKey(nameof(CustomerId))]" },
              { file: "in/SalesOrder.cs", find: "public required Customer Customer { get; set; }" },
              { file: "out/SalesOrder.java", find: "@ManyToOne(optional = false)" },
              {
                file: "out/SalesOrder.java",
                find: '@JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId", nullable = false)',
              },
              { file: "out/SalesOrder.java", find: "private Customer Customer;" },
            ],
          },
          {
            kind: "carried",
            label: "The column the relation points at",
            note: "The source says which property of this class holds the foreign key; which column it points at comes from the other file, where that entity declares its key. Nothing in this file states it.",
            spans: [
              { file: "in/Customer.cs", find: "[Key]" },
              { file: "in/Customer.cs", find: "public int CustomerId { get; set; }" },
              { file: "out/SalesOrder.java", find: 'referencedColumnName = "CustomerId"' },
            ],
          },
          {
            kind: "added",
            label: "The foreign key column is read-only",
            note: "Two members now map the same column: the relation and the plain property beside it. The plain one is kept from writing it, or the two would fight over the value.",
            spans: [
              {
                file: "out/SalesOrder.java",
                find: '@Column(name = "CustomerId", nullable = false, insertable = false, updatable = false)',
              },
            ],
          },
          {
            kind: "carried",
            label: "A column with a different name",
            note: "The only place in this domain where the column is not called what the property is called - and the one word that would be lost if the column name were not carried.",
            spans: [
              { file: "in/SalesOrder.cs", find: '[Column("ShipToCity")]' },
              { file: "in/SalesOrder.cs", find: "public string? City { get; set; }" },
              { file: "out/SalesOrder.java", find: 'name = "ShipToCity"' },
              { file: "out/SalesOrder.java", find: "private String City;" },
            ],
          },
          {
            kind: "carried",
            label: "Fractional seconds",
            note: "Three digits of a second, stated once and written once.",
            spans: [
              { file: "in/SalesOrder.cs", find: "[Precision(3)]" },
              { file: "out/SalesOrder.java", find: "secondPrecision = 3" },
            ],
          },
          {
            kind: "carried",
            label: "Property OrderNumber",
            note: "Property, column and unique constraint.",
            token: "OrderNumber",
            files: ["in/SalesOrder.cs", "out/SalesOrder.java"],
          },
          {
            kind: "carried",
            label: "Property OrderedAt",
            note: "Property and column.",
            token: "OrderedAt",
            files: ["in/SalesOrder.cs", "out/SalesOrder.java"],
          },
          {
            kind: "carried",
            label: "Property Status",
            note: "Property and column.",
            token: "Status",
            files: ["in/SalesOrder.cs", "out/SalesOrder.java"],
          },
          {
            kind: "carried",
            label: "The collection of lines",
            note: "The other end is owned by OrderLine's own relation, which the annotation names.",
            token: "Lines",
            files: ["in/SalesOrder.cs", "out/SalesOrder.java"],
          },
        ],
      },
      {
        title: "OrderLine: a key of two columns",
        note: `The key is made of two properties, and the first of them is at the same time
          the foreign key to the order. JPA needs a class for such a key, and the tool has
          to name it.`,
        source: ["in/OrderLine.cs"],
        target: ["out/OrderLine.java"],
        links: [
          {
            kind: "carried",
            label: "The composite key",
            note: "One attribute listing two properties becomes two @Id annotations plus a class that holds both.",
            spans: [
              {
                file: "in/OrderLine.cs",
                find: "[PrimaryKey(nameof(SalesOrderId), nameof(LineNumber))]",
              },
              { file: "out/OrderLine.java", find: "@Id", all: true },
              { file: "out/OrderLine.java", find: "@IdClass(OrderLine.OrderLineId.class)" },
            ],
          },
          {
            kind: "added",
            label: "The name of the key class",
            note: "The source has no key class at all, so the target's requirement has to be met with a name the tool invents from the entity's.",
            record: 8,
            spans: [
              { file: "out/OrderLine.java", find: "public static class OrderLineId implements Serializable {" },
            ],
          },
          {
            kind: "added",
            label: "What the key class must implement",
            note: "JPA compares key instances, so the class needs equals and hashCode over exactly the key members, and it has to be serializable.",
            spans: [
              { file: "out/OrderLine.java", find: "public boolean equals(Object obj) {" },
              { file: "out/OrderLine.java", find: "public int hashCode() {" },
              { file: "out/OrderLine.java", find: "implements Serializable" },
            ],
          },
          {
            kind: "carried",
            label: "The relation to the order",
            note: "The first member of the key is also the foreign key, so the relation maps a column that is already mapped - and is therefore kept from writing it.",
            spans: [
              { file: "in/OrderLine.cs", find: "[ForeignKey(nameof(SalesOrderId))]" },
              { file: "in/OrderLine.cs", find: "public required SalesOrder Order { get; set; }" },
              {
                file: "out/OrderLine.java",
                find: '@JoinColumn(name = "SalesOrderId", referencedColumnName = "SalesOrderId", nullable = false, insertable = false, updatable = false)',
              },
              { file: "out/OrderLine.java", find: "private SalesOrder Order;" },
            ],
          },
          {
            kind: "carried",
            label: "The relation to the product",
            note: "The same shape without the key: this foreign key is an ordinary column.",
            spans: [
              { file: "in/OrderLine.cs", find: "[ForeignKey(nameof(ProductId))]" },
              { file: "in/OrderLine.cs", find: "public required Product Product { get; set; }" },
              {
                file: "out/OrderLine.java",
                find: '@JoinColumn(name = "ProductId", referencedColumnName = "ProductId", nullable = false)',
              },
              { file: "out/OrderLine.java", find: "private Product Product;" },
            ],
          },
          {
            kind: "carried",
            label: "Property SalesOrderId",
            note: "Key member, column, and the member of the key class.",
            token: "SalesOrderId",
            files: ["in/OrderLine.cs", "out/OrderLine.java"],
          },
          {
            kind: "carried",
            label: "Property LineNumber",
            note: "The second key member; a C# short becomes a Java Short.",
            token: "LineNumber",
            files: ["in/OrderLine.cs", "out/OrderLine.java"],
          },
          {
            kind: "carried",
            label: "Property Quantity",
            note: "Property and column.",
            token: "Quantity",
            files: ["in/OrderLine.cs", "out/OrderLine.java"],
          },
          {
            kind: "carried",
            label: "Prices and their precision",
            note: "Two decimal columns, each with its own precision and scale.",
            spans: [
              { file: "in/OrderLine.cs", find: "[Precision(18, 2)]" },
              { file: "in/OrderLine.cs", find: "[Precision(5, 2)]" },
              { file: "out/OrderLine.java", find: "precision = 18, scale = 2" },
              { file: "out/OrderLine.java", find: "precision = 5, scale = 2" },
            ],
          },
        ],
      },
      {
        title: "Product: a key the application supplies",
        note: `The one entity whose key is not generated - and the way the target says so is
          by writing nothing at all.`,
        source: ["in/Product.cs"],
        target: ["out/Product.java"],
        links: [
          {
            kind: "carried",
            label: "Entity Product",
            note: "The class keeps its name.",
            token: "Product",
            files: ["in/Product.cs", "out/Product.java"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Stated, and written out.",
            spans: [
              { file: "in/Product.cs", find: '[Table("Products", Schema = "Ordering")]' },
              { file: "out/Product.java", find: '@Table(name = "Products", schema = "Ordering"' },
            ],
          },
          {
            kind: "carried",
            label: "The unique index",
            note: "Unnamed in the source, unnamed in the target.",
            spans: [
              { file: "in/Product.cs", find: "[Index(nameof(Sku), IsUnique = true)]" },
              {
                file: "out/Product.java",
                find: 'uniqueConstraints = { @UniqueConstraint(columnNames = { "Sku" }) }',
              },
            ],
          },
          {
            kind: "carried",
            label: "The key, generated by nobody",
            note: "The source says the database generates nothing here. JPA says the same by leaving @Id without a @GeneratedValue beside it - compare the other three entities, which all have one.",
            spans: [
              { file: "in/Product.cs", find: "[Key]" },
              { file: "in/Product.cs", find: "[DatabaseGenerated(DatabaseGeneratedOption.None)]" },
              { file: "out/Product.java", find: "@Id" },
            ],
          },
          {
            kind: "carried",
            label: "Property ProductId",
            note: "Property and column.",
            token: "ProductId",
            files: ["in/Product.cs", "out/Product.java"],
          },
          {
            kind: "carried",
            label: "Property Sku",
            note: "Property, column and unique constraint.",
            token: "Sku",
            files: ["in/Product.cs", "out/Product.java"],
          },
          {
            kind: "carried",
            label: "Property ListPrice",
            note: "Property and column, with precision and scale.",
            token: "ListPrice",
            files: ["in/Product.cs", "out/Product.java"],
          },
          {
            kind: "carried",
            label: "Property IsDiscontinued",
            note: "A bool becomes a boolean; the column is not null because neither language lets it be missing.",
            token: "IsDiscontinued",
            files: ["in/Product.cs", "out/Product.java"],
          },
          {
            kind: "carried",
            label: "Lengths",
            note: "Both travel.",
            spans: [
              { file: "in/Product.cs", find: "[MaxLength(32)]" },
              { file: "in/Product.cs", find: "[MaxLength(200)]" },
              { file: "out/Product.java", find: "length = 32" },
              { file: "out/Product.java", find: "length = 200" },
            ],
          },
          {
            kind: "carried",
            label: "The collection of lines",
            note: "Owned by the other side, which the annotation names.",
            token: "Lines",
            files: ["in/Product.cs", "out/Product.java"],
          },
        ],
      },
      {
        title: "A page of open orders",
        note: `Six LINQ files become six queries, each returned twice: as the method that
          builds it and as the bare JPQL. Here: a captured variable becomes a named
          parameter, and the page moves out of the query onto the query object.`,
        source: ["in/OpenOrders.cs"],
        target: ["out/query.java", "out/query.jpql"],
        links: [
          {
            kind: "carried",
            label: "The query root",
            note: "The set the chain starts from is the entity of the from clause.",
            spans: [
              { file: "in/OpenOrders.cs", find: "ctx.SalesOrders" },
              { file: "out/query.java", find: "from SalesOrder o" },
              { file: "out/query.jpql", find: "from SalesOrder o" },
            ],
          },
          {
            kind: "carried",
            label: "The filter on the customer",
            note: "One equality; the captured variable on the right of it becomes a parameter.",
            spans: [
              { file: "in/OpenOrders.cs", find: "o.CustomerId == customerId" },
              { file: "out/query.java", find: "o.CustomerId = :customerId" },
              { file: "out/query.jpql", find: "o.CustomerId = :customerId" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter customerId",
            note: "A variable the method captured becomes a named parameter of the query, a parameter of the generated method, and a call that binds the one to the other.",
            token: "customerId",
            files: ["in/OpenOrders.cs", "out/query.java", "out/query.jpql"],
          },
          {
            kind: "carried",
            label: "The parameter placedAfter",
            note: "The same treatment; a DateTime becomes a LocalDateTime.",
            token: "placedAfter",
            files: ["in/OpenOrders.cs", "out/query.java", "out/query.jpql"],
          },
          {
            kind: "carried",
            label: "A set of literals",
            note: "Contains over an array of constants is an in predicate; the three strings keep their order.",
            spans: [
              {
                file: "in/OpenOrders.cs",
                find: 'new[] { "New", "Paid", "Packed" }.Contains(o.Status)',
              },
              { file: "out/query.java", find: "o.Status in ('New', 'Paid', 'Packed')" },
              { file: "out/query.jpql", find: "o.Status in ('New', 'Paid', 'Packed')" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "OrderByDescending and ThenBy are the two terms of one order by clause.",
            spans: [
              { file: "in/OpenOrders.cs", find: ".OrderByDescending(o => o.OrderedAt)" },
              { file: "in/OpenOrders.cs", find: ".ThenBy(o => o.OrderNumber)" },
              { file: "out/query.java", find: "order by o.OrderedAt desc, o.OrderNumber asc" },
              { file: "out/query.jpql", find: "order by o.OrderedAt desc, o.OrderNumber asc" },
            ],
          },
          {
            kind: "carried",
            label: "The page",
            note: "Skip and Take are not clauses of JPQL, so they move onto the query object - and the values are parameters of the method, not numbers.",
            spans: [
              { file: "in/OpenOrders.cs", find: ".Skip(skip)" },
              { file: "in/OpenOrders.cs", find: ".Take(take)" },
              { file: "out/query.java", find: ".setFirstResult(skip)" },
              { file: "out/query.java", find: ".setMaxResults(take)" },
            ],
          },
          {
            kind: "carried",
            label: "The result is an entity",
            note: "Because the query selects the entity itself, the generated query is the typed one and names the class.",
            spans: [
              { file: "in/OpenOrders.cs", find: "List<SalesOrder>" },
              { file: "out/query.java", find: "TypedQuery<SalesOrder>" },
              { file: "out/query.java", find: "SalesOrder.class" },
            ],
          },
          {
            kind: "dropped",
            label: "The name of the query",
            note: "A LINQ method's own name is not read as the name of its query, so every generated method here is called query. Only a source that names its queries - a mapping document, for instance - gets named methods.",
            spans: [{ file: "in/OpenOrders.cs", find: "OpenOrders" }],
          },
        ],
      },
      {
        title: "Best sellers: a grouping with a filter after it",
        note: `The one query where the order of the calls decides the clause: a Where before
          the GroupBy is a where, the same call after it is a having.`,
        source: ["in/BestSellers.cs"],
        target: ["out/query-2.java", "out/query-2.jpql"],
        links: [
          {
            kind: "carried",
            label: "The query root",
            note: "The lines of all orders.",
            spans: [
              { file: "in/BestSellers.cs", find: "ctx.OrderLines" },
              { file: "out/query-2.java", find: "from OrderLine l" },
              { file: "out/query-2.jpql", find: "from OrderLine l" },
            ],
          },
          {
            kind: "carried",
            label: "The filter before the grouping",
            note: "It filters rows, so it is a where clause.",
            spans: [
              { file: "in/BestSellers.cs", find: ".Where(l => l.DiscountPercent < 50)" },
              { file: "out/query-2.java", find: "where l.DiscountPercent < 50" },
              { file: "out/query-2.jpql", find: "where l.DiscountPercent < 50" },
            ],
          },
          {
            kind: "carried",
            label: "The grouping",
            note: "One key, and the same key orders the result further down.",
            spans: [
              { file: "in/BestSellers.cs", find: ".GroupBy(l => l.ProductId)" },
              { file: "out/query-2.java", find: "group by l.ProductId" },
              { file: "out/query-2.jpql", find: "group by l.ProductId" },
            ],
          },
          {
            kind: "carried",
            label: "The filter after the grouping",
            note: "The same method call as above, in a different place: it filters groups, so it becomes a having clause with the aggregate repeated inside it.",
            spans: [
              {
                file: "in/BestSellers.cs",
                find: ".Where(g => g.Sum(l => l.Quantity) >= minimumQuantity)",
              },
              { file: "out/query-2.java", find: "having sum(l.Quantity) >= :minimumQuantity" },
              { file: "out/query-2.jpql", find: "having sum(l.Quantity) >= :minimumQuantity" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering by the group key",
            note: "The key of the group is the property it groups by, so the ordering names that property again.",
            spans: [
              { file: "in/BestSellers.cs", find: ".OrderBy(g => g.Key)" },
              { file: "out/query-2.java", find: "order by l.ProductId asc" },
              { file: "out/query-2.jpql", find: "order by l.ProductId asc" },
            ],
          },
          {
            kind: "carried",
            label: "The projection",
            note: "Four members of an anonymous type become four aliased items of the select list, in the same order.",
            spans: [
              { file: "in/BestSellers.cs", find: "ProductId = g.Key," },
              { file: "in/BestSellers.cs", find: "Quantity = g.Sum(l => l.Quantity)," },
              { file: "in/BestSellers.cs", find: "Lines = g.Count()," },
              { file: "in/BestSellers.cs", find: "HighestPrice = g.Max(l => l.UnitPrice)," },
              {
                file: "out/query-2.java",
                find: "select l.ProductId as ProductId, sum(l.Quantity) as Quantity, count(l) as Lines, max(l.UnitPrice) as HighestPrice",
              },
              {
                file: "out/query-2.jpql",
                find: "select l.ProductId as ProductId, sum(l.Quantity) as Quantity, count(l) as Lines, max(l.UnitPrice) as HighestPrice",
              },
            ],
          },
          {
            kind: "carried",
            label: "The parameter minimumQuantity",
            note: "Captured by the lambda, bound on the query object.",
            token: "minimumQuantity",
            files: ["in/BestSellers.cs", "out/query-2.java", "out/query-2.jpql"],
          },
          {
            kind: "added",
            label: "An untyped query",
            note: "The result is a row of four values rather than an entity, so the generated query names no class - compare the typed one in the comparison above.",
            spans: [{ file: "out/query-2.java", find: "public static Query query" }],
          },
        ],
      },
      {
        title: "Dormant customers: a subquery that has to not exist",
        note: `Two things to point at: the parentheses the target needs around a mixed and/or,
          and the negated Any, which becomes not exists over a whole subquery.`,
        source: ["in/DormantCustomers.cs"],
        target: ["out/query-3.java", "out/query-3.jpql"],
        links: [
          {
            kind: "carried",
            label: "The query root",
            note: "All customers.",
            spans: [
              { file: "in/DormantCustomers.cs", find: "ctx.Customers" },
              { file: "out/query-3.java", find: "from Customer c" },
              { file: "out/query-3.jpql", find: "from Customer c" },
            ],
          },
          {
            kind: "carried",
            label: "Or, in parentheses",
            note: "The condition is a tree, not a list, so the visitor writes the brackets itself: without them the or would swallow the and that follows.",
            spans: [
              {
                file: "in/DormantCustomers.cs",
                find: "(c.CreditLimit == null || c.CreditLimit > 0)",
              },
              { file: "out/query-3.java", find: "(c.CreditLimit is null or c.CreditLimit > 0)" },
              { file: "out/query-3.jpql", find: "(c.CreditLimit is null or c.CreditLimit > 0)" },
            ],
          },
          {
            kind: "carried",
            label: "Comparison with null",
            note: "Equality with null in C# is the is-null predicate in a query language.",
            spans: [
              { file: "in/DormantCustomers.cs", find: "c.CreditLimit == null" },
              { file: "out/query-3.java", find: "c.CreditLimit is null" },
              { file: "out/query-3.jpql", find: "c.CreditLimit is null" },
            ],
          },
          {
            kind: "carried",
            label: "The negated existence",
            note: "Any over another set is an exists subquery, and the exclamation mark in front of it is the not around the whole of it.",
            spans: [
              { file: "in/DormantCustomers.cs", find: "!ctx.SalesOrders.Any(o => o.CustomerId == c.CustomerId && o.OrderedAt >= since))" },
              {
                file: "out/query-3.java",
                find: "not (exists (select o from SalesOrder o where o.CustomerId = c.CustomerId and o.OrderedAt >= :since))",
              },
              {
                file: "out/query-3.jpql",
                find: "not (exists (select o from SalesOrder o where o.CustomerId = c.CustomerId and o.OrderedAt >= :since))",
              },
            ],
          },
          {
            kind: "carried",
            label: "The parameter since",
            note: "Captured inside the subquery and bound outside it, because a parameter belongs to the whole query.",
            token: "since",
            files: ["in/DormantCustomers.cs", "out/query-3.java", "out/query-3.jpql"],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Ascending, which the target writes out rather than leaving implied.",
            spans: [
              { file: "in/DormantCustomers.cs", find: ".OrderBy(c => c.Name)" },
              { file: "out/query-3.java", find: "order by c.Name asc" },
              { file: "out/query-3.jpql", find: "order by c.Name asc" },
            ],
          },
        ],
      },
      {
        title: "Orders with given products: a subquery on the other side of in",
        note: `The chain nests a whole query inside the filter of another one. The collection
          the caller passes stays one parameter, not a list of them.`,
        source: ["in/OrdersWithProducts.cs"],
        target: ["out/query-4.java", "out/query-4.jpql"],
        links: [
          {
            kind: "carried",
            label: "The outer root",
            note: "The orders themselves.",
            spans: [
              { file: "in/OrdersWithProducts.cs", find: "ctx.SalesOrders" },
              { file: "out/query-4.java", find: "from SalesOrder o" },
              { file: "out/query-4.jpql", find: "from SalesOrder o" },
            ],
          },
          {
            kind: "carried",
            label: "The inner root",
            note: "A second query inside the filter of the first.",
            spans: [
              { file: "in/OrdersWithProducts.cs", find: "ctx.OrderLines" },
              { file: "out/query-4.java", find: "from OrderLine l" },
              { file: "out/query-4.jpql", find: "from OrderLine l" },
            ],
          },
          {
            kind: "carried",
            label: "What the subquery selects",
            note: "One column, which is what an in predicate can compare against.",
            spans: [
              { file: "in/OrdersWithProducts.cs", find: ".Select(l => l.SalesOrderId)" },
              { file: "out/query-4.java", find: "select l.SalesOrderId as SalesOrderId" },
              { file: "out/query-4.jpql", find: "select l.SalesOrderId as SalesOrderId" },
            ],
          },
          {
            kind: "carried",
            label: "Membership in the subquery",
            note: "Contains over a query is the in predicate, with the subquery as its right-hand side.",
            spans: [
              { file: "in/OrdersWithProducts.cs", find: ".Contains(o.SalesOrderId))" },
              { file: "out/query-4.java", find: "o.SalesOrderId in (select" },
              { file: "out/query-4.jpql", find: "o.SalesOrderId in (select" },
            ],
          },
          {
            kind: "carried",
            label: "Membership in a collection the caller passes",
            note: "The same method call with a different argument: here the right-hand side is one parameter that holds many values, which the target binds as a collection.",
            spans: [
              { file: "in/OrdersWithProducts.cs", find: "productIds.Contains(l.ProductId)" },
              { file: "out/query-4.java", find: "l.ProductId in :productIds" },
              { file: "out/query-4.jpql", find: "l.ProductId in :productIds" },
            ],
          },
          {
            kind: "carried",
            label: "The collection parameter",
            note: "A List of ints becomes a Collection of Integers in the method signature, and one named parameter in the query.",
            token: "productIds",
            files: ["in/OrdersWithProducts.cs", "out/query-4.java", "out/query-4.jpql"],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "By the key of the outer entity.",
            spans: [
              { file: "in/OrdersWithProducts.cs", find: ".OrderBy(o => o.SalesOrderId)" },
              { file: "out/query-4.java", find: "order by o.SalesOrderId asc" },
              { file: "out/query-4.jpql", find: "order by o.SalesOrderId asc" },
            ],
          },
        ],
      },
      {
        title: "Priced above average: an aggregate as a value",
        note: `The subquery here is not a set but a single number, and it sits on the
          right-hand side of a comparison.`,
        source: ["in/PricedAboveAverage.cs"],
        target: ["out/query-5.java", "out/query-5.jpql"],
        links: [
          {
            kind: "carried",
            label: "The outer root",
            note: "Products, with a second reading of the same entity inside the subquery.",
            spans: [
              { file: "in/PricedAboveAverage.cs", find: "ctx.Products", nth: 1 },
              { file: "out/query-5.java", find: "from Product p" },
              { file: "out/query-5.jpql", find: "from Product p" },
            ],
          },
          {
            kind: "carried",
            label: "The inner root",
            note: "The same entity again, under its own alias, so the two readings do not get confused with one another.",
            spans: [
              { file: "in/PricedAboveAverage.cs", find: "ctx.Products", nth: 2 },
              { file: "out/query-5.java", find: "from Product x" },
              { file: "out/query-5.jpql", find: "from Product x" },
            ],
          },
          {
            kind: "carried",
            label: "The average",
            note: "An aggregate over a query is a scalar subquery: one value, in brackets, on the right of the comparison.",
            spans: [
              { file: "in/PricedAboveAverage.cs", find: ".Average(x => x.ListPrice))" },
              { file: "out/query-5.java", find: "(select avg(x.ListPrice)" },
              { file: "out/query-5.jpql", find: "(select avg(x.ListPrice)" },
            ],
          },
          {
            kind: "carried",
            label: "The outer filter",
            note: "Equality with a boolean constant, written out on both sides.",
            spans: [
              { file: "in/PricedAboveAverage.cs", find: "p.IsDiscontinued == false" },
              { file: "out/query-5.java", find: "p.IsDiscontinued = false" },
              { file: "out/query-5.jpql", find: "p.IsDiscontinued = false" },
            ],
          },
          {
            kind: "carried",
            label: "The filter inside the subquery",
            note: "The same condition once more, this time over the inner alias - the average is taken over the same rows.",
            spans: [
              { file: "in/PricedAboveAverage.cs", find: ".Where(x => x.IsDiscontinued == false)" },
              { file: "out/query-5.java", find: "where x.IsDiscontinued = false" },
              { file: "out/query-5.jpql", find: "where x.IsDiscontinued = false" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Descending by the price.",
            spans: [
              { file: "in/PricedAboveAverage.cs", find: ".OrderByDescending(p => p.ListPrice)" },
              { file: "out/query-5.java", find: "order by p.ListPrice desc" },
              { file: "out/query-5.jpql", find: "order by p.ListPrice desc" },
            ],
          },
          {
            kind: "added",
            label: "A query with no parameters",
            note: "Nothing is captured here, so the generated method takes the entity manager and nothing else.",
            spans: [{ file: "out/query-5.java", find: "query(EntityManager em)" }],
          },
        ],
      },
      {
        title: "A mailing list from two queries at once",
        note: `Union is one of the few places where a query has two halves of equal standing.
          Both keep their own filter and their own parameter.`,
        source: ["in/MailingList.cs"],
        target: ["out/query-6.java", "out/query-6.jpql"],
        links: [
          {
            kind: "carried",
            label: "The union",
            note: "One call becomes one keyword between the two halves.",
            spans: [
              { file: "in/MailingList.cs", find: ".Union(ctx.Customers" },
              { file: "out/query-6.java", find: "union" },
              { file: "out/query-6.jpql", find: "union" },
            ],
          },
          {
            kind: "carried",
            label: "The first half",
            note: "Its own filter, over the credit limit.",
            spans: [
              { file: "in/MailingList.cs", find: ".Where(c => c.CreditLimit >= minimumCreditLimit)" },
              {
                file: "out/query-6.java",
                find: "select c.Email as Email from Customer c where c.CreditLimit >= :minimumCreditLimit",
              },
              {
                file: "out/query-6.jpql",
                find: "select c.Email as Email from Customer c where c.CreditLimit >= :minimumCreditLimit",
              },
            ],
          },
          {
            kind: "carried",
            label: "The second half",
            note: "Its own filter, over the date of joining.",
            spans: [
              { file: "in/MailingList.cs", find: ".Where(c => c.CustomerSince < customerBefore)" },
              {
                file: "out/query-6.java",
                find: "select c.Email as Email from Customer c where c.CustomerSince < :customerBefore",
              },
              {
                file: "out/query-6.jpql",
                find: "select c.Email as Email from Customer c where c.CustomerSince < :customerBefore",
              },
            ],
          },
          {
            kind: "carried",
            label: "The projection of both halves",
            note: "The same single member on both sides of the union, which is what makes the two halves compatible.",
            spans: [
              { file: "in/MailingList.cs", find: ".Select(c => new { Email = c.Email })", all: true },
              { file: "out/query-6.java", find: "c.Email as Email", all: true },
              { file: "out/query-6.jpql", find: "c.Email as Email", all: true },
            ],
          },
          {
            kind: "carried",
            label: "The parameter minimumCreditLimit",
            note: "A decimal becomes a BigDecimal.",
            token: "minimumCreditLimit",
            files: ["in/MailingList.cs", "out/query-6.java", "out/query-6.jpql"],
          },
          {
            kind: "carried",
            label: "The parameter customerBefore",
            note: "A DateOnly becomes a LocalDate.",
            token: "customerBefore",
            files: ["in/MailingList.cs", "out/query-6.java", "out/query-6.jpql"],
          },
        ],
      },
    ],
  },

  "lending-library": {
    title: "Lending library",
    lede: `The other direction across the ecosystems, over a second domain: five annotated
      Java entities and five JPQL queries become C# classes, hbm.xml mappings and HQL. The
      comparison worth opening first is the sixth - a class that exists on the right and
      nowhere on the left.`,
    comparisons: [
      {
        title: "Book: one file in, two files out",
        note: `Annotations split into a class and a mapping document. The sequence keeps its
          own name and schema; the one thing about it that does not fit is the size of the
          block it hands out.`,
        source: ["in/Book.java"],
        target: ["out/Book.cs", "out/Book.hbm.xml"],
        links: [
          {
            kind: "carried",
            label: "Package becomes namespace",
            note: "Named by the class and once more by the mapping document.",
            token: "Library",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Entity Book",
            note: "The class keeps its name.",
            token: "Book",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Stated by the annotation, written as attributes of the class element.",
            spans: [
              { file: "in/Book.java", find: '@Table(name = "Books", schema = "Lending",' },
              { file: "out/Book.hbm.xml", find: 'table="Books" schema="Lending"' },
            ],
          },
          {
            kind: "carried",
            label: "The unique constraint",
            note: "An hbm.xml states uniqueness on the property itself, so one constraint over one column becomes one attribute.",
            spans: [
              { file: "in/Book.java", find: 'columnNames = {"Isbn"}' },
              { file: "out/Book.hbm.xml", find: 'unique="true"' },
            ],
          },
          {
            kind: "dropped",
            label: "The name of the constraint",
            note: "unique=\"true\" is the whole of what an hbm.xml says about a constraint over one column. The token that could hold a name, unique-key, exists to tie several columns together inside the document, and there is only one column here.",
            spans: [{ file: "in/Book.java", find: 'name = "UQ_Books_Isbn", ' }],
          },
          {
            kind: "carried",
            label: "The key",
            note: "The annotation becomes the id element, and the C# type of the key is named there as well.",
            spans: [
              { file: "in/Book.java", find: "@Id" },
              { file: "out/Book.hbm.xml", find: '<id name="BookId" column="BookId" type="Int64">' },
            ],
          },
          {
            kind: "carried",
            label: "The sequence",
            note: "Strategy and generator become the generator element; the sequence keeps its own name, and its schema is written in front of it.",
            spans: [
              {
                file: "in/Book.java",
                find: '@GeneratedValue(strategy = GenerationType.SEQUENCE, generator = "book_numbers")',
              },
              {
                file: "in/Book.java",
                find: '@SequenceGenerator(name = "book_numbers", sequenceName = "BookNumbers", schema = "Lending",',
              },
              { file: "out/Book.hbm.xml", find: '<generator class="sequence">' },
              {
                file: "out/Book.hbm.xml",
                find: '<param name="sequence">Lending.BookNumbers</param>',
              },
            ],
          },
          {
            kind: "dropped",
            label: "The allocation size",
            note: "How many numbers the sequence hands out at once has no counterpart on NHibernate's sequence generator, so the parameter is dropped and recorded.",
            record: 10,
            spans: [{ file: "in/Book.java", find: "allocationSize = 20" }],
          },
          {
            kind: "carried",
            label: "Property BookId",
            note: "Key, column, and the column of two collections further down.",
            token: "BookId",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property Isbn",
            note: "Property, column and the unique one.",
            token: "Isbn",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property Title",
            note: "Property and column.",
            token: "Title",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property PublishedYear",
            note: "A Java short becomes a C# short; not-null because neither language lets it be missing.",
            token: "PublishedYear",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property PageCount",
            note: "An Integer becomes an int? - the boxed type of the source is what says the column may be null.",
            token: "PageCount",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Lengths",
            note: "Both travel onto the property elements.",
            spans: [
              { file: "in/Book.java", find: "length = 13" },
              { file: "in/Book.java", find: "length = 300" },
              { file: "out/Book.hbm.xml", find: 'length="13"' },
              { file: "out/Book.hbm.xml", find: 'length="300"' },
            ],
          },
          {
            kind: "carried",
            label: "Not null",
            note: "Two columns state it, and the mapping writes both.",
            spans: [
              { file: "in/Book.java", find: "nullable = false", all: true },
              { file: "out/Book.hbm.xml", find: 'not-null="true"', all: true },
            ],
          },
          {
            kind: "carried",
            label: "The many-to-many",
            note: "A collection of authors with a join table becomes a collection of the junction entity the target needs - which is the sixth comparison below.",
            record: 9,
            spans: [
              { file: "in/Book.java", find: "@ManyToMany" },
              { file: "out/Book.hbm.xml", find: '<set name="Authors" inverse="true">' },
              { file: "out/Book.hbm.xml", find: '<one-to-many class="BookAuthor" />', nth: 1 },
              { file: "out/Book.cs", find: "ISet<BookAuthor> Authors" },
            ],
          },
          {
            kind: "carried",
            label: "The collection of copies",
            note: "Owned by the other side, so the mapping marks it inverse.",
            token: "Copies",
            files: ["in/Book.java", "out/Book.cs", "out/Book.hbm.xml"],
          },
          {
            kind: "added",
            label: "The key columns of the collections",
            note: "The source never says which column joins a collection back to its owner - the other side does, and the tool falls back to the owner's own key column and records the fallback.",
            record: 11,
            spans: [{ file: "out/Book.hbm.xml", find: '<key column="BookId" />', all: true }],
          },
          {
            kind: "carried",
            label: "The property that is not mapped",
            note: "It reaches the class and not the mapping document, which is how an hbm.xml says transient: by not mentioning the member at all.",
            spans: [
              { file: "in/Book.java", find: "@Transient" },
              { file: "in/Book.java", find: "DisplayTitle", all: true },
              { file: "out/Book.cs", find: "DisplayTitle" },
            ],
          },
          {
            kind: "dropped",
            label: "The initializers",
            note: "Neither new HashSet<>() nor new ArrayList<>() is a literal both languages spell alike, so both are dropped, one record each.",
            record: 0,
            spans: [
              { file: "in/Book.java", find: "= new HashSet<>();" },
              { file: "in/Book.java", find: "= new ArrayList<>();" },
            ],
          },
          {
            kind: "added",
            label: "virtual on every member",
            note: "NHibernate builds proxies over the class and needs it; nothing in the source says so.",
            token: "virtual",
            files: ["out/Book.cs"],
          },
        ],
      },
      {
        title: "Author: a fixed-length column the target cannot name",
        note: `The interesting property is Country: two characters, fixed length. NHibernate
          has no type for that, so the claim moves down onto the column itself - and names a
          database dialect while doing it.`,
        source: ["in/Author.java"],
        target: ["out/Author.cs", "out/Author.hbm.xml"],
        links: [
          {
            kind: "carried",
            label: "Entity Author",
            note: "The class keeps its name.",
            token: "Author",
            files: ["in/Author.java", "out/Author.cs", "out/Author.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Stated, and written out.",
            spans: [
              { file: "in/Author.java", find: '@Table(name = "Authors", schema = "Lending")' },
              { file: "out/Author.hbm.xml", find: 'table="Authors" schema="Lending"' },
            ],
          },
          {
            kind: "carried",
            label: "The key and its generator",
            note: "An identity column on both sides - the one generator both frameworks spell almost alike.",
            spans: [
              { file: "in/Author.java", find: "@GeneratedValue(strategy = GenerationType.IDENTITY)" },
              { file: "out/Author.hbm.xml", find: '<generator class="identity" />' },
            ],
          },
          {
            kind: "carried",
            label: "Property AuthorId",
            note: "Key, column, and the key column of a collection.",
            token: "AuthorId",
            files: ["in/Author.java", "out/Author.cs", "out/Author.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property FullName",
            note: "Property and column, with its length.",
            token: "FullName",
            files: ["in/Author.java", "out/Author.cs", "out/Author.hbm.xml"],
          },
          {
            kind: "carried",
            label: "The fixed-length column",
            note: "NHibernate registers no fixed-length string type, so the property is mapped as variable-length character data and the column carries the real claim as a literal type - which is T-SQL's spelling, a dialect the source never named.",
            record: 12,
            spans: [
              { file: "in/Author.java", find: 'columnDefinition = "char(2)"' },
              { file: "out/Author.hbm.xml", find: 'type="AnsiString"' },
              { file: "out/Author.hbm.xml", find: 'sql-type="char(2)"' },
            ],
          },
          {
            kind: "carried",
            label: "Property Country",
            note: "The one property whose mapping needs an element of its own rather than an attribute.",
            token: "Country",
            files: ["in/Author.java", "out/Author.cs", "out/Author.hbm.xml"],
          },
          {
            kind: "carried",
            label: "The other end of the many-to-many",
            note: "The source marks it as owned by Book; the mapping marks the set inverse and points it at the junction entity.",
            spans: [
              { file: "in/Author.java", find: '@ManyToMany(mappedBy = "Authors")' },
              { file: "out/Author.hbm.xml", find: '<set name="Books" inverse="true">' },
              { file: "out/Author.hbm.xml", find: '<one-to-many class="BookAuthor" />' },
              { file: "out/Author.cs", find: "ISet<BookAuthor> Books" },
            ],
          },
          {
            kind: "added",
            label: "The key column of the collection",
            note: "The same fallback as in Book, and the same record beside it.",
            record: 13,
            spans: [{ file: "out/Author.hbm.xml", find: '<key column="AuthorId" />' }],
          },
        ],
      },
      {
        title: "BookCopy: a key of two columns, and what it costs",
        note: `JPA puts the obligations of a composite key on a class of its own. NHibernate
          puts them on the entity: point at the nested class and watch where its equals and
          hash code land.`,
        source: ["in/BookCopy.java"],
        target: ["out/BookCopy.cs", "out/BookCopy.hbm.xml"],
        links: [
          {
            kind: "carried",
            label: "Entity BookCopy",
            note: "The class keeps its name.",
            token: "BookCopy",
            files: ["in/BookCopy.java", "out/BookCopy.cs", "out/BookCopy.hbm.xml"],
          },
          {
            kind: "carried",
            label: "The composite key",
            note: "Two @Id annotations and a key class become one composite-id element with two key properties, in the order the source declared them.",
            spans: [
              { file: "in/BookCopy.java", find: "@IdClass(BookCopy.BookCopyId.class)" },
              { file: "in/BookCopy.java", find: "@Id", all: true },
              { file: "out/BookCopy.hbm.xml", find: "<composite-id>" },
              {
                file: "out/BookCopy.hbm.xml",
                find: '<key-property name="BookId" column="BookId" type="Int64" />',
              },
              {
                file: "out/BookCopy.hbm.xml",
                find: '<key-property name="CopyNumber" column="CopyNumber" type="Int16" />',
              },
            ],
          },
          {
            kind: "carried",
            label: "What the key class carried",
            note: "The separate key class disappears and its obligations move onto the entity itself: NHibernate wants a composite-keyed entity to be serializable and to compare by its key members.",
            spans: [
              { file: "in/BookCopy.java", find: "public static class BookCopyId implements Serializable {" },
              { file: "in/BookCopy.java", find: "public boolean equals(Object other) {" },
              { file: "in/BookCopy.java", find: "public int hashCode() {" },
              { file: "out/BookCopy.cs", find: "[Serializable]" },
              { file: "out/BookCopy.cs", find: "public override bool Equals(object? obj)" },
              { file: "out/BookCopy.cs", find: "public override int GetHashCode()" },
            ],
          },
          {
            kind: "carried",
            label: "The relation to the book",
            note: "The relation maps a column that is already part of the key, so it is kept from writing it - the same two words on both sides, under different names.",
            spans: [
              { file: "in/BookCopy.java", find: "@ManyToOne(optional = false)" },
              {
                file: "in/BookCopy.java",
                find: '@JoinColumn(name = "BookId", referencedColumnName = "BookId", insertable = false, updatable = false)',
              },
              {
                file: "out/BookCopy.hbm.xml",
                find: '<many-to-one name="Book" class="Book" column="BookId" insert="false" update="false" />',
              },
            ],
          },
          {
            kind: "carried",
            label: "Property CopyNumber",
            note: "The second member of the key; a Java Short becomes a C# short.",
            token: "CopyNumber",
            files: ["in/BookCopy.java", "out/BookCopy.cs", "out/BookCopy.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property AcquiredOn",
            note: "A LocalDate becomes a DateOnly.",
            token: "AcquiredOn",
            files: ["in/BookCopy.java", "out/BookCopy.cs", "out/BookCopy.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property ShelfMark",
            note: "Nullable, with a length.",
            token: "ShelfMark",
            files: ["in/BookCopy.java", "out/BookCopy.cs", "out/BookCopy.hbm.xml"],
          },
        ],
      },
      {
        title: "Loan: a relation over two columns",
        note: `The loan points at a copy, and a copy is identified by two columns - so the
          relation has to carry both, in order. The version column is here too.`,
        source: ["in/Loan.java"],
        target: ["out/Loan.cs", "out/Loan.hbm.xml"],
        links: [
          {
            kind: "carried",
            label: "Entity Loan",
            note: "The class keeps its name.",
            token: "Loan",
            files: ["in/Loan.java", "out/Loan.cs", "out/Loan.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Stated, and written out.",
            spans: [
              { file: "in/Loan.java", find: '@Table(name = "Loans", schema = "Lending")' },
              { file: "out/Loan.hbm.xml", find: 'table="Loans" schema="Lending"' },
            ],
          },
          {
            kind: "carried",
            label: "The version column",
            note: "Optimistic locking: one annotation becomes an element of its own, before every ordinary property.",
            spans: [
              { file: "in/Loan.java", find: "@Version" },
              { file: "out/Loan.hbm.xml", find: '<version name="Revision">' },
            ],
          },
          {
            kind: "carried",
            label: "The relation over two columns",
            note: "Two join columns become two column elements inside the many-to-one, and their order is the order of the key they point at.",
            spans: [
              {
                file: "in/Loan.java",
                find: '@JoinColumn(name = "BookId", referencedColumnName = "BookId", insertable = false, updatable = false),',
              },
              {
                file: "in/Loan.java",
                find: '@JoinColumn(name = "CopyNumber", referencedColumnName = "CopyNumber", insertable = false, updatable = false)',
              },
              { file: "out/Loan.hbm.xml", find: '<many-to-one name="Copy" class="BookCopy">' },
              { file: "out/Loan.hbm.xml", find: '<column name="BookId" />' },
              { file: "out/Loan.hbm.xml", find: '<column name="CopyNumber" />' },
            ],
          },
          {
            kind: "carried",
            label: "The relation to the member",
            note: "One column, so it fits in an attribute.",
            spans: [
              {
                file: "in/Loan.java",
                find: '@JoinColumn(name = "MemberId", referencedColumnName = "MemberId", insertable = false, updatable = false)',
              },
              {
                file: "out/Loan.hbm.xml",
                find: '<many-to-one name="Member" class="Member" column="MemberId" />',
              },
            ],
          },
          {
            kind: "carried",
            label: "The read-only foreign key columns",
            note: "Three plain properties map columns that a relation also maps, so all three are kept from writing them.",
            spans: [
              { file: "in/Loan.java", find: "insertable = false, updatable = false", all: true },
              { file: "out/Loan.hbm.xml", find: 'insert="false" update="false"', all: true },
            ],
          },
          {
            kind: "carried",
            label: "Fractional seconds",
            note: "Two timestamps, each with three digits of a second.",
            spans: [
              { file: "in/Loan.java", find: "secondPrecision = 3", all: true },
              { file: "out/Loan.hbm.xml", find: 'precision="3"', all: true },
            ],
          },
          {
            kind: "carried",
            label: "Property LoanedAt",
            note: "A LocalDateTime becomes a DateTime.",
            token: "LoanedAt",
            files: ["in/Loan.java", "out/Loan.cs", "out/Loan.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property DueOn",
            note: "A LocalDate becomes a DateOnly.",
            token: "DueOn",
            files: ["in/Loan.java", "out/Loan.cs", "out/Loan.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property ReturnedAt",
            note: "The nullable one: it is what tells an open loan from a closed one.",
            token: "ReturnedAt",
            files: ["in/Loan.java", "out/Loan.cs", "out/Loan.hbm.xml"],
          },
        ],
      },
      {
        title: "Member: uniqueness stated on the column",
        note: `The simplest of the five, and the second place where a unique column appears -
          this time declared on the column annotation rather than on the table.`,
        source: ["in/Member.java"],
        target: ["out/Member.cs", "out/Member.hbm.xml"],
        links: [
          {
            kind: "carried",
            label: "Entity Member",
            note: "The class keeps its name.",
            token: "Member",
            files: ["in/Member.java", "out/Member.cs", "out/Member.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Table and schema",
            note: "Stated, and written out.",
            spans: [
              { file: "in/Member.java", find: '@Table(name = "Members", schema = "Lending")' },
              { file: "out/Member.hbm.xml", find: 'table="Members" schema="Lending"' },
            ],
          },
          {
            kind: "carried",
            label: "The unique column",
            note: "One attribute of the column annotation, one attribute of the property element.",
            spans: [
              { file: "in/Member.java", find: "unique = true" },
              { file: "out/Member.hbm.xml", find: 'unique="true"' },
            ],
          },
          {
            kind: "carried",
            label: "Property MemberId",
            note: "Key, column, and the key column of the collection of loans.",
            token: "MemberId",
            files: ["in/Member.java", "out/Member.cs", "out/Member.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property GivenName",
            note: "Property and column, with its length.",
            token: "GivenName",
            files: ["in/Member.java", "out/Member.cs", "out/Member.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property Surname",
            note: "Property and column, with its length.",
            token: "Surname",
            files: ["in/Member.java", "out/Member.cs", "out/Member.hbm.xml"],
          },
          {
            kind: "carried",
            label: "Property Email",
            note: "The unique one.",
            token: "Email",
            files: ["in/Member.java", "out/Member.cs", "out/Member.hbm.xml"],
          },
          {
            kind: "carried",
            label: "The collection of loans",
            note: "Owned by the other side, so the bag is inverse.",
            token: "Loans",
            files: ["in/Member.java", "out/Member.cs", "out/Member.hbm.xml"],
          },
          {
            kind: "added",
            label: "The key column of the collection",
            note: "The third and last fallback of this conversion.",
            record: 14,
            spans: [{ file: "out/Member.hbm.xml", find: '<key column="MemberId" />' }],
          },
        ],
      },
      {
        title: "BookAuthor: the class nobody wrote",
        note: `There is no such file on the left. A many-to-many has no class of its own in
          Java, and NHibernate needs one - so the tool builds it out of the join table, and
          every word of it can be traced back to the two files beside it.`,
        source: ["in/Book.java", "in/Author.java"],
        target: ["out/BookAuthor.cs", "out/BookAuthor.hbm.xml"],
        links: [
          {
            kind: "added",
            label: "The junction entity",
            note: "Generated with two many-to-one relations, and named after the join table - which is the tool's convention, not a fact of the source.",
            record: 9,
            spans: [
              { file: "out/BookAuthor.cs", find: "public class BookAuthor" },
              {
                file: "out/BookAuthor.hbm.xml",
                find: '<class name="BookAuthor" table="BookAuthors" schema="Lending">',
              },
            ],
          },
          {
            kind: "carried",
            label: "The join table",
            note: "Name and schema of the class come straight from the join table the source declared.",
            spans: [
              { file: "in/Book.java", find: '@JoinTable(name = "BookAuthors", schema = "Lending",' },
              { file: "out/BookAuthor.hbm.xml", find: 'table="BookAuthors" schema="Lending"' },
            ],
          },
          {
            kind: "carried",
            label: "The column towards the book",
            note: "The join column of the owning side becomes the first member of the key and the column of the first relation.",
            spans: [
              { file: "in/Book.java", find: 'joinColumns = @JoinColumn(name = "BookId")' },
              {
                file: "out/BookAuthor.hbm.xml",
                find: '<key-property name="BookId" column="BookId" type="Int64" />',
              },
              {
                file: "out/BookAuthor.hbm.xml",
                find: '<many-to-one name="Book" class="Book" column="BookId" insert="false" update="false" />',
              },
            ],
          },
          {
            kind: "carried",
            label: "The column towards the author",
            note: "The inverse join column becomes the second member of the key and the column of the second relation.",
            spans: [
              {
                file: "in/Book.java",
                find: 'inverseJoinColumns = @JoinColumn(name = "AuthorId")',
              },
              {
                file: "out/BookAuthor.hbm.xml",
                find: '<key-property name="AuthorId" column="AuthorId" type="Int32" />',
              },
              {
                file: "out/BookAuthor.hbm.xml",
                find: '<many-to-one name="Author" class="Author" column="AuthorId" insert="false" update="false" />',
              },
            ],
          },
          {
            kind: "carried",
            label: "The two ends of the relation",
            note: "Both collections of the source now hold this entity instead of holding each other.",
            spans: [
              { file: "in/Book.java", find: "private Set<Author> Authors" },
              { file: "in/Author.java", find: "private Set<Book> Books" },
              { file: "out/BookAuthor.cs", find: "public virtual Book Book { get; set; }" },
              { file: "out/BookAuthor.cs", find: "public virtual Author Author { get; set; }" },
            ],
          },
          {
            kind: "added",
            label: "What a composite key costs here too",
            note: "The generated class is keyed by both columns, so it needs the same serializability and comparison as BookCopy - and nobody wrote a line of it.",
            spans: [
              { file: "out/BookAuthor.cs", find: "[Serializable]" },
              { file: "out/BookAuthor.cs", find: "public override bool Equals(object? obj)" },
              { file: "out/BookAuthor.cs", find: "public override int GetHashCode()" },
              { file: "out/BookAuthor.hbm.xml", find: "<composite-id>" },
            ],
          },
        ],
      },
      {
        title: "Overdue loans: a join with a condition",
        note: `JPQL joins with on, HQL with with. Everything else in this query crosses the
          ecosystem boundary unchanged.`,
        source: ["in/OverdueLoans.jpql"],
        target: ["out/query.cs", "out/query.hql"],
        links: [
          {
            kind: "carried",
            label: "The select list",
            note: "Three aliased items, unchanged.",
            spans: [
              {
                file: "in/OverdueLoans.jpql",
                find: "select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn",
              },
              {
                file: "out/query.cs",
                find: "select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn",
              },
              {
                file: "out/query.hql",
                find: "select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn",
              },
            ],
          },
          {
            kind: "carried",
            label: "The join",
            note: "The same join under two keywords: JPQL writes the condition after on, HQL after with, and HQL says inner out loud.",
            spans: [
              { file: "in/OverdueLoans.jpql", find: "join Member m on m.MemberId = l.MemberId" },
              { file: "out/query.cs", find: "inner join Member m with m.MemberId = l.MemberId" },
              { file: "out/query.hql", find: "inner join Member m with m.MemberId = l.MemberId" },
            ],
          },
          {
            kind: "carried",
            label: "The filter",
            note: "An is-null test and a comparison with a parameter, unchanged.",
            spans: [
              { file: "in/OverdueLoans.jpql", find: "where l.ReturnedAt is null and l.DueOn < :today" },
              { file: "out/query.cs", find: "where l.ReturnedAt is null and l.DueOn < :today" },
              { file: "out/query.hql", find: "where l.ReturnedAt is null and l.DueOn < :today" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter today",
            note: "Its type comes from the property it is compared with: a LocalDate there is a DateOnly here.",
            token: "today",
            files: ["in/OverdueLoans.jpql", "out/query.cs", "out/query.hql"],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Two terms, unchanged.",
            spans: [
              { file: "in/OverdueLoans.jpql", find: "order by l.DueOn asc, m.Surname asc" },
              { file: "out/query.cs", find: "order by l.DueOn asc, m.Surname asc" },
              { file: "out/query.hql", find: "order by l.DueOn asc, m.Surname asc" },
            ],
          },
          {
            kind: "added",
            label: "The session",
            note: "An NHibernate query is created from a session, and the bare HQL beside it is the same text without that wrapper.",
            spans: [
              { file: "out/query.cs", find: "ISession session" },
              { file: "out/query.cs", find: "session.CreateQuery" },
            ],
          },
        ],
      },
      {
        title: "Active members: counting rows",
        note: `The one place in this domain where the two query languages disagree about a
          function: JPQL counts an alias, HQL counts rows.`,
        source: ["in/ActiveMembers.jpql"],
        target: ["out/query-2.cs", "out/query-2.hql"],
        links: [
          {
            kind: "carried",
            label: "Counting",
            note: "Counting the alias and counting rows are the same number; the target's spelling is the star, in the select list and in the having clause alike.",
            spans: [
              { file: "in/ActiveMembers.jpql", find: "count(l)", all: true },
              { file: "out/query-2.cs", find: "count(*)", all: true },
              { file: "out/query-2.hql", find: "count(*)", all: true },
            ],
          },
          {
            kind: "carried",
            label: "The grouping",
            note: "One key, and the select list repeats it.",
            spans: [
              { file: "in/ActiveMembers.jpql", find: "group by l.MemberId" },
              { file: "out/query-2.cs", find: "group by l.MemberId" },
              { file: "out/query-2.hql", find: "group by l.MemberId" },
            ],
          },
          {
            kind: "carried",
            label: "The filter on the groups",
            note: "A having clause, with the aggregate repeated inside it.",
            spans: [
              { file: "in/ActiveMembers.jpql", find: "having count(l) >= :minimumLoans" },
              { file: "out/query-2.cs", find: "having count(*) >= :minimumLoans" },
              { file: "out/query-2.hql", find: "having count(*) >= :minimumLoans" },
            ],
          },
          {
            kind: "carried",
            label: "The parameter since",
            note: "Compared with a timestamp, so it becomes a DateTime.",
            token: "since",
            files: ["in/ActiveMembers.jpql", "out/query-2.cs", "out/query-2.hql"],
          },
          {
            kind: "carried",
            label: "The parameter minimumLoans",
            note: "Compared with a count rather than with a property - and a count is a long, which is where the type of this parameter comes from.",
            token: "minimumLoans",
            files: ["in/ActiveMembers.jpql", "out/query-2.cs", "out/query-2.hql"],
          },
        ],
      },
      {
        title: "Never borrowed: two subqueries of different kinds",
        note: `One subquery asks whether anything exists, the other returns a number. Point
          at the select that disappears: HQL lets a query that selects the whole entity say
          so by saying nothing.`,
        source: ["in/NeverBorrowed.jpql"],
        target: ["out/query-3.cs", "out/query-3.hql"],
        links: [
          {
            kind: "carried",
            label: "Selecting the whole entity",
            note: "Two words on the left, none on the right: in HQL a query that starts with from selects the entity it names.",
            spans: [
              { file: "in/NeverBorrowed.jpql", find: "select b" },
              { file: "out/query-3.cs", find: "from Book b" },
              { file: "out/query-3.hql", find: "from Book b" },
            ],
          },
          {
            kind: "carried",
            label: "The existence subquery",
            note: "The same subquery, minus its own select for the same reason, and wrapped in brackets that make the not unambiguous.",
            spans: [
              {
                file: "in/NeverBorrowed.jpql",
                find: "not exists (select l from Loan l where l.BookId = b.BookId)",
              },
              {
                file: "out/query-3.cs",
                find: "not (exists (from Loan l where l.BookId = b.BookId))",
              },
              {
                file: "out/query-3.hql",
                find: "not (exists (from Loan l where l.BookId = b.BookId))",
              },
            ],
          },
          {
            kind: "carried",
            label: "The scalar subquery",
            note: "An average over the same entity under another alias - unchanged, because here both languages say it the same way.",
            spans: [
              {
                file: "in/NeverBorrowed.jpql",
                find: "b.PageCount > (select avg(x.PageCount) from Book x)",
              },
              {
                file: "out/query-3.cs",
                find: "b.PageCount > (select avg(x.PageCount) from Book x)",
              },
              {
                file: "out/query-3.hql",
                find: "b.PageCount > (select avg(x.PageCount) from Book x)",
              },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Unchanged.",
            spans: [
              { file: "in/NeverBorrowed.jpql", find: "order by b.Title asc" },
              { file: "out/query-3.cs", find: "order by b.Title asc" },
              { file: "out/query-3.hql", find: "order by b.Title asc" },
            ],
          },
        ],
      },
      {
        title: "Copies acquired: a range, a list and a page",
        note: `Three things happen here that nowhere else does: a between becomes two
          comparisons, a collection parameter needs a binding call of its own, and the page
          is given by parameters rather than by numbers.`,
        source: ["in/CopiesAcquired.jpql"],
        target: ["out/query-4.cs", "out/query-4.hql"],
        links: [
          {
            kind: "carried",
            label: "The range",
            note: "HQL has no between, so one predicate becomes two comparisons joined by and - and the tool records that it rewrote the condition.",
            record: 15,
            spans: [
              {
                file: "in/CopiesAcquired.jpql",
                find: "c.AcquiredOn between :fromDate and :toDate",
              },
              {
                file: "out/query-4.cs",
                find: "c.AcquiredOn >= :fromDate and c.AcquiredOn <= :toDate",
              },
              {
                file: "out/query-4.hql",
                find: "c.AcquiredOn >= :fromDate and c.AcquiredOn <= :toDate",
              },
            ],
          },
          {
            kind: "carried",
            label: "The collection parameter",
            note: "One parameter holding many values: HQL wants it in brackets, and NHibernate wants it bound by a call of its own rather than by the ordinary one.",
            spans: [
              { file: "in/CopiesAcquired.jpql", find: "c.BookId in :bookIds" },
              { file: "out/query-4.cs", find: "c.BookId in (:bookIds)" },
              { file: "out/query-4.hql", find: "c.BookId in (:bookIds)" },
              { file: "out/query-4.cs", find: '.SetParameterList("bookIds", bookIds)' },
              { file: "out/query-4.cs", find: "IEnumerable<long> bookIds" },
            ],
          },
          {
            kind: "carried",
            label: "The page",
            note: "limit and offset have no clause in HQL, so they move onto the query object - and because the source paged by parameters rather than by numbers, the generated method takes two.",
            spans: [
              { file: "in/CopiesAcquired.jpql", find: "limit :take offset :skip" },
              { file: "out/query-4.cs", find: ".SetFirstResult(skip)" },
              { file: "out/query-4.cs", find: ".SetMaxResults(take)" },
              { file: "out/query-4.cs", find: "int skip, int take" },
            ],
          },
          {
            kind: "carried",
            label: "Selecting the whole entity",
            note: "The select disappears again, the same way as in the query above.",
            spans: [
              { file: "in/CopiesAcquired.jpql", find: "select c" },
              { file: "out/query-4.cs", find: "from BookCopy c" },
              { file: "out/query-4.hql", find: "from BookCopy c" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Both members of the key, in their order.",
            spans: [
              { file: "in/CopiesAcquired.jpql", find: "order by c.BookId asc, c.CopyNumber asc" },
              { file: "out/query-4.cs", find: "order by c.BookId asc, c.CopyNumber asc" },
              { file: "out/query-4.hql", find: "order by c.BookId asc, c.CopyNumber asc" },
            ],
          },
          {
            kind: "carried",
            label: "The dates of the range",
            note: "Two parameters, both LocalDate on the left and DateOnly on the right.",
            spans: [
              { file: "in/CopiesAcquired.jpql", find: "fromDate", all: true },
              { file: "in/CopiesAcquired.jpql", find: "toDate", all: true },
              { file: "out/query-4.cs", find: "fromDate", all: true },
              { file: "out/query-4.cs", find: "toDate", all: true },
              { file: "out/query-4.hql", find: "fromDate", all: true },
              { file: "out/query-4.hql", find: "toDate", all: true },
            ],
          },
        ],
      },
      {
        title: "Authors from a region: nothing changes",
        note: `The last comparison is here for contrast. A query can cross the boundary
          between the ecosystems without a single word moving - and that is worth seeing
          once, after five that did not.`,
        source: ["in/AuthorsFromRegion.jpql"],
        target: ["out/query-5.cs", "out/query-5.hql"],
        links: [
          {
            kind: "carried",
            label: "Distinct rows",
            note: "One word, in the same place.",
            spans: [
              { file: "in/AuthorsFromRegion.jpql", find: "select distinct" },
              { file: "out/query-5.cs", find: "select distinct" },
              { file: "out/query-5.hql", find: "select distinct" },
            ],
          },
          {
            kind: "carried",
            label: "The select list",
            note: "Two aliased items, unchanged.",
            spans: [
              {
                file: "in/AuthorsFromRegion.jpql",
                find: "a.FullName as FullName, a.Country as Country",
              },
              {
                file: "out/query-5.cs",
                find: "a.FullName as FullName, a.Country as Country",
              },
              {
                file: "out/query-5.hql",
                find: "a.FullName as FullName, a.Country as Country",
              },
            ],
          },
          {
            kind: "carried",
            label: "A list of literals",
            note: "Three strings in brackets; no parameter, so nothing has to be bound.",
            spans: [
              { file: "in/AuthorsFromRegion.jpql", find: "a.Country in ('CZ', 'SK', 'PL')" },
              { file: "out/query-5.cs", find: "a.Country in ('CZ', 'SK', 'PL')" },
              { file: "out/query-5.hql", find: "a.Country in ('CZ', 'SK', 'PL')" },
            ],
          },
          {
            kind: "carried",
            label: "The ordering",
            note: "Unchanged.",
            spans: [
              { file: "in/AuthorsFromRegion.jpql", find: "order by a.FullName asc" },
              { file: "out/query-5.cs", find: "order by a.FullName asc" },
              { file: "out/query-5.hql", find: "order by a.FullName asc" },
            ],
          },
          {
            kind: "added",
            label: "The method around it",
            note: "All that the conversion added: a session to create the query from. The query itself is the same text.",
            spans: [{ file: "out/query-5.cs", find: "public static IQuery Query(ISession session)" }],
          },
        ],
      },
    ],
  },
};
