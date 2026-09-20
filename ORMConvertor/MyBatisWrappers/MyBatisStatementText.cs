using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model.AbstractRepresentation.Enums;
using TransactSql;

namespace MyBatisWrappers;

/// <summary>
/// Turns one MyBatis statement into the plain T-SQL the shared reader takes, in the four
/// steps decision 084 fixed - and refuses it where a step cannot be taken, because the
/// line decision 082 drew is that a foreign language is read by its own tool or not at all,
/// never by a half-written evaluator of our own.
///
/// <list type="number">
/// <item>The static tags expand. &lt;include&gt; inlines the &lt;sql&gt; of the same
/// document with its &lt;property&gt; substitutions, &lt;where&gt; and &lt;set&gt; add
/// their keyword and strip the leading AND/OR or the trailing comma, &lt;trim&gt; does the
/// same by its own attributes. The result is the same text for every set of parameters, so
/// this is an expansion and not a choice.</item>
/// <item>A tag whose expansion an OGNL expression decides - &lt;if&gt;, &lt;choose&gt;,
/// &lt;when&gt;, &lt;otherwise&gt;, &lt;bind&gt; - is a Failure naming it. One statement is
/// then a family of statements, one member of which has another set of rows than another
/// (decision 053), and the representation carries no family.</item>
/// <item>A &lt;foreach&gt; in the canonical form is one collection parameter: its
/// collection attribute is the parameter's name, its item is used once as #{item}, its
/// open and close are the parentheses and its separator the comma. Any other
/// &lt;foreach&gt; is a Failure saying how it differs.</item>
/// <item>The placeholders are substituted: #{name} becomes @name, which the shared reading
/// has read as a named parameter since decision 083, and ${name} is a Failure, because it
/// substitutes text and not a value.</item>
/// </list>
///
/// One trap is visible only from here and is held as a rule: after the fourth step @name is
/// indistinguishable from an @name that stood in the text before it, which would be a
/// parameter MyBatis never binds translated as one. The check therefore runs over every run
/// of raw text as it arrives, before anything is substituted, because afterwards it cannot
/// be made at all.
/// </summary>
internal sealed partial class MyBatisStatementText(
    MyBatisReadingContext context,
    string namespaceName,
    Action<ConversionRecordKind, string, QueryFeature?> report)
{
    private static readonly Regex Placeholder = new(@"#\{([^}]*)\}", RegexOptions.CultureInvariant);

    /// <summary>
    /// What the source stated about the statement's parameters, gathered while the text is
    /// read: the scalar from a javaType inside #{}, and the collection flag from a
    /// &lt;foreach&gt;. Handed to <see cref="SqlQueryReader"/> beside the report channel.
    /// </summary>
    public Dictionary<string, SqlParameterFacts> Parameters { get; } = new(StringComparer.Ordinal);

    private bool refused;

    /// <summary>The T-SQL of an XML statement, or null when a step refused it.</summary>
    public string? FromElement(XElement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var sql = new StringBuilder();
        AppendNodes(statement.Nodes(), sql, substitutions: null);

        return Substitute(sql.ToString());
    }

    /// <summary>
    /// The T-SQL of an @Select, or null when it was refused. The annotated form carries no
    /// element to walk: a dynamic tag would have to be wrapped in &lt;script&gt;, which is
    /// XML inside a string inside Java, and that is the form the tool reads out of the
    /// mapper document instead (decision 084).
    /// </summary>
    public string? FromAnnotationText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (ScriptTag().Match(text) is { Success: true } script)
        {
            Refuse(
                $"The @Select wraps its statement in '{script.Value}', so its body is a dynamic MyBatis document inside a Java "
                + "string; the tool reads that form out of the XML mapper, not out of an annotation; no artifact was generated.");
            return null;
        }

        CheckForBareParameter(text);

        return Substitute(text);
    }

    /* ---- step 1 to 3: the tags ------------------------------------------------------- */

    private void AppendNodes(IEnumerable<XNode> nodes, StringBuilder sql, IReadOnlyDictionary<string, string>? substitutions)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    AppendText(text.Value, sql, substitutions);
                    break;

                case XElement element:
                    AppendElement(element, sql, substitutions);
                    break;
            }
        }
    }

    /// <summary>
    /// A run of the statement's own text. The ${…} of an &lt;include&gt; property is the one
    /// text substitution the tool performs, and it is performed here so that the check for a
    /// bare @parameter sees the text the database would: the fragment's body with the
    /// include's values in it.
    /// </summary>
    private void AppendText(string value, StringBuilder sql, IReadOnlyDictionary<string, string>? substitutions)
    {
        if (substitutions is { Count: > 0 })
        {
            foreach (var (name, replacement) in substitutions)
            {
                value = value.Replace($"${{{name}}}", replacement, StringComparison.Ordinal);
            }
        }

        CheckForBareParameter(value);
        sql.Append(value);
    }

    private void AppendElement(XElement element, StringBuilder sql, IReadOnlyDictionary<string, string>? substitutions)
    {
        switch (element.Name.LocalName)
        {
            case "include":
                AppendInclude(element, sql);
                return;

            case "where":
                AppendTrimmed(element, sql, substitutions, prefix: "WHERE", suffix: null, prefixOverrides: "AND |OR |AND\t|OR\t", suffixOverrides: null);
                return;

            case "set":
                AppendTrimmed(element, sql, substitutions, prefix: "SET", suffix: null, prefixOverrides: null, suffixOverrides: ",");
                return;

            case "trim":
                AppendTrimmed(
                    element,
                    sql,
                    substitutions,
                    element.Attribute("prefix")?.Value,
                    element.Attribute("suffix")?.Value,
                    element.Attribute("prefixOverrides")?.Value,
                    element.Attribute("suffixOverrides")?.Value);
                return;

            case "foreach":
                AppendForEach(element, sql);
                return;

            // The tags OGNL decides. Named one by one rather than caught by a default arm,
            // so that an element nobody has thought about is refused as unknown instead of
            // being taken for one of these.
            case "if":
            case "choose":
            case "when":
            case "otherwise":
            case "bind":
                Refuse(
                    $"The statement carries <{element.Name.LocalName}>, whose expansion an OGNL expression decides, so the statement is "
                    + "a family of statements rather than one; the query representation carries no family and a single member of it would "
                    + "return other rows; no artifact was generated.");
                return;

            default:
                Refuse(
                    $"The statement carries <{element.Name.LocalName}>, which is no part of the static MyBatis markup the tool expands; "
                    + "no artifact was generated.");
                return;
        }
    }

    private void AppendInclude(XElement include, StringBuilder sql)
    {
        var refId = include.Attribute("refid")?.Value;

        if (string.IsNullOrWhiteSpace(refId))
        {
            Refuse("An <include> states the fragment it inlines in a refid attribute and this one carries none; no artifact was generated.");
            return;
        }

        if (context.FragmentOf(namespaceName, refId) is not { } fragment)
        {
            Refuse(
                $"The <include refid=\"{refId}\"> names a <sql> fragment this document does not declare. The unit of conversion is one "
                + "artifact, so a fragment is looked for nowhere else; no artifact was generated.");
            return;
        }

        var substitutions = include.Elements()
            .Where(e => e.Name.LocalName == "property"
                && e.Attribute("name") is not null
                && e.Attribute("value") is not null)
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Attribute("value")!.Value, StringComparer.Ordinal);

        AppendNodes(fragment.Nodes(), sql, substitutions);
    }

    /// <summary>
    /// &lt;where&gt;, &lt;set&gt; and &lt;trim&gt;, which are one operation with different
    /// attributes. Empty content writes nothing at all, which is what makes the expansion the
    /// same for every set of parameters.
    /// </summary>
    private void AppendTrimmed(
        XElement element,
        StringBuilder sql,
        IReadOnlyDictionary<string, string>? substitutions,
        string? prefix,
        string? suffix,
        string? prefixOverrides,
        string? suffixOverrides)
    {
        var inner = new StringBuilder();
        AppendNodes(element.Nodes(), inner, substitutions);

        var body = inner.ToString().Trim();

        foreach (var candidate in Overrides(prefixOverrides))
        {
            if (body.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                body = body[candidate.Length..].TrimStart();
                break;
            }
        }

        foreach (var candidate in Overrides(suffixOverrides))
        {
            if (body.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                body = body[..^candidate.Length].TrimEnd();
                break;
            }
        }

        if (body.Length == 0)
        {
            return;
        }

        sql.Append('\n');

        if (!string.IsNullOrEmpty(prefix))
        {
            sql.Append(prefix).Append(' ');
        }

        sql.Append(body);

        if (!string.IsNullOrEmpty(suffix))
        {
            sql.Append(' ').Append(suffix);
        }

        sql.Append('\n');
    }

    /// <summary>
    /// The overrides of a trim, which MyBatis writes pipe-separated. A trailing space in one
    /// of them is meaningful - "AND " must not strip the AND of ANDROID - so only the empty
    /// entries are dropped.
    /// </summary>
    private static IEnumerable<string> Overrides(string? overrides)
        => string.IsNullOrEmpty(overrides)
            ? []
            : overrides.Split('|').Where(o => o.Length > 0);

    /// <summary>
    /// A &lt;foreach&gt; in the canonical form is one collection parameter (decisions 074
    /// and 083) and the whole tag is replaced by it. Canonical means: the collection names
    /// the parameter, the item is used exactly once as #{item}, the parentheses are the open
    /// and close, and the separator is the comma. Anything else is refused saying what
    /// differs, because MyBatis would then write a text the representation cannot state.
    /// </summary>
    private void AppendForEach(XElement element, StringBuilder sql)
    {
        var collection = element.Attribute("collection")?.Value?.Trim();
        var item = element.Attribute("item")?.Value?.Trim();
        var open = element.Attribute("open")?.Value?.Trim();
        var close = element.Attribute("close")?.Value?.Trim();
        var separator = element.Attribute("separator")?.Value?.Trim();
        var body = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();

        string? differs = null;

        if (string.IsNullOrEmpty(collection))
        {
            differs = "it states no collection, so the parameter it binds has no name";
        }
        else if (string.IsNullOrEmpty(item))
        {
            differs = "it states no item";
        }
        else if (element.Elements().Any())
        {
            differs = $"its body carries <{element.Elements().First().Name.LocalName}> rather than the item alone";
        }
        else if (body != $"#{{{item}}}")
        {
            differs = $"its body is '{body}' rather than the item alone, '#{{{item}}}'";
        }
        else if (open != "(" || close != ")")
        {
            differs = $"it opens with '{open}' and closes with '{close}' rather than with parentheses";
        }
        else if (separator != ",")
        {
            differs = $"it separates the items with '{separator}' rather than with a comma";
        }
        else if (element.Attribute("index") is not null)
        {
            differs = "it states an index, whose value the representation does not carry";
        }

        if (differs is not null)
        {
            Refuse(
                $"The <foreach> is not the canonical list of bound values - {differs} - so it is not a collection parameter and its "
                + "text cannot be stated; no artifact was generated.",
                QueryFeature.QueryParameter);
            return;
        }

        // The whole tag becomes the one parameter. Its collectionness is a fact of the source
        // that the grammar could not see - to T-SQL this is a one-element list of values - so
        // it travels to the shared reader beside the text (decision 084).
        Parameters[collection!] = Parameters.TryGetValue(collection!, out var stated)
            ? stated with { IsCollection = true }
            : new SqlParameterFacts(IsCollection: true);

        // Written with the parentheses the tag itself opens and closes, because T-SQL has no
        // syntax for a bare parameter after IN: to the grammar this is a one-element list of
        // values, and that it is a whole list is what the facts above say (decision 084).
        sql.Append("(@").Append(collection).Append(')');
    }

    /* ---- step 4: the placeholders ---------------------------------------------------- */

    private string? Substitute(string text)
    {
        if (text.Contains("${", StringComparison.Ordinal))
        {
            Refuse(
                "The statement carries a ${…} placeholder, which substitutes text into the query rather than binding a value, so it "
                + "can change the tables, the columns or whole clauses the query names; no artifact was generated.",
                QueryFeature.QueryParameter);
        }

        var substituted = Placeholder.Replace(text, match => ReadPlaceholder(match.Groups[1].Value) ?? match.Value);

        return refused ? null : substituted;
    }

    /// <summary>
    /// One #{…}: the property it binds, and the attributes beside it. javaType is the stated
    /// scalar of the parameter, the one branch decision 083 described and nobody filled; the
    /// rest - jdbcType, typeHandler, mode - have no place in the representation and are
    /// dropped with a record.
    /// </summary>
    private string? ReadPlaceholder(string content)
    {
        var parts = content.Split(',');
        var property = parts[0].Trim();

        if (property.Length == 0)
        {
            Refuse("The statement carries an empty #{} placeholder; no artifact was generated.", QueryFeature.QueryParameter);
            return null;
        }

        if (property.Contains('.', StringComparison.Ordinal))
        {
            Refuse(
                $"The placeholder '#{{{property}}}' is a path into a parameter object, and the representation carries neither the "
                + "object nor what the path means; no artifact was generated.",
                QueryFeature.QueryParameter);
            return null;
        }

        ScalarType? scalar = null;

        foreach (var attribute in parts.Skip(1))
        {
            var pair = attribute.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            var key = pair[0].Trim();
            var value = pair[1].Trim();

            if (key == "javaType")
            {
                var langType = JavaTypeConvertor.FromString(value);
                if (langType.Category == LangTypeCategory.Scalar)
                {
                    scalar = langType.ScalarType;
                }
                else
                {
                    report(
                        ConversionRecordKind.Loss,
                        $"The placeholder '#{{{property}}}' states javaType '{value}', which is no scalar of the type vocabulary; "
                        + "the scalar of the parameter is derived from what it is compared against instead.",
                        QueryFeature.QueryParameter);
                }

                continue;
            }

            report(
                ConversionRecordKind.Loss,
                $"The placeholder '#{{{property}}}' states {key} '{value}', which is how MyBatis binds the value rather than what the "
                + "value is; the representation has no place for it and it was dropped.",
                QueryFeature.QueryParameter);
        }

        if (scalar is not null)
        {
            Parameters[property] = Parameters.TryGetValue(property, out var stated)
                ? stated with { Scalar = scalar }
                : new SqlParameterFacts(scalar);
        }

        return $"@{property}";
    }

    /* ---- the trap ------------------------------------------------------------------- */

    /// <summary>
    /// An identifier introduced by @ in the source text. After the substitution of step four
    /// it would be indistinguishable from a MyBatis placeholder, so the source is refused
    /// naming it - MyBatis binds nothing of the kind, and translating it as a parameter would
    /// put a value into the query the source never left open.
    /// </summary>
    private void CheckForBareParameter(string text)
    {
        foreach (Match match in BareParameter().Matches(text))
        {
            if (InsideStringLiteral(text, match.Index))
            {
                continue;
            }

            Refuse(
                $"The statement's SQL already contains '{match.Value}', an identifier introduced by @, which MyBatis binds to nothing "
                + "and which the placeholders of the mapper become; no artifact was generated.",
                QueryFeature.QueryParameter);
            return;
        }
    }

    /// <summary>
    /// Whether the position falls inside a single-quoted literal of the run. Approximate by
    /// construction - a literal that a tag interrupts is two runs - and deliberately so: the
    /// check exists to spare an ordinary string that happens to hold an @, not to parse SQL,
    /// which is the shared reader's work.
    /// </summary>
    private static bool InsideStringLiteral(string text, int position)
    {
        var inside = false;

        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\'')
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private void Refuse(string reason, QueryFeature? feature = null)
    {
        refused = true;
        report(ConversionRecordKind.Failure, reason, feature);
    }

    [GeneratedRegex(@"@[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex BareParameter();

    [GeneratedRegex(@"<\s*script\b[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();
}
