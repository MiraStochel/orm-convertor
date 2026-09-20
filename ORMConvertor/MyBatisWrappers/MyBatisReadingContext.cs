using System.Runtime.CompilerServices;
using System.Xml.Linq;
using AbstractWrappers;

namespace MyBatisWrappers;

/// <summary>Which of the two forms a statement was declared in (decision 084).</summary>
public enum MyBatisStatementForm
{
    /// <summary>An annotation on a method of the mapper interface: @Select and its siblings.</summary>
    Annotation,

    /// <summary>An element of the XML mapper: &lt;select&gt; and its siblings.</summary>
    Xml,
}

/// <summary>
/// One parameter of a mapper method: the name a statement writes it under and the Java type
/// the signature declares. The signature is the only place the type of a MyBatis query
/// parameter lives - the mapper writes <c>#{id}</c> and nothing else (decision 084).
/// </summary>
/// <param name="Name">
/// The value of @Param, or the declared name where the method has a single parameter and
/// MyBatis would derive it. Null where neither holds, because MyBatis itself would then
/// only know arg0, arg1 - a name the statement cannot have written.
/// </param>
public sealed record MyBatisMethodParameter(string? Name, string Type);

/// <summary>One method of a mapper interface, as the entity pass read it.</summary>
public sealed record MyBatisMethodSignature(string Name, string ReturnType, IReadOnlyList<MyBatisMethodParameter> Parameters);

/// <summary>
/// What the parsers of one MyBatis conversion share (decision 084) - the shape decision 077
/// introduced for the pair of JPA parsers, with one difference: here the entity pass and
/// the query pass share it, because two of the three units are a mapping and a query at
/// once (decision 081) and the two passes read them for different halves.
///
/// It carries the three things the decision names. The <c>&lt;namespace, id&gt;</c> of every
/// statement already read, with the form it was written in, so that the same id stated in an
/// annotation and in XML is recognized as the input MyBatis refuses to build a factory from
/// - a Failure, not a Conflict (decision 068). The bodies of the <c>&lt;sql&gt;</c>
/// fragments, so that an <c>&lt;include&gt;</c> of the query pass can be expanded although
/// the fragment was read on the entity pass. And the signatures of the interface's methods,
/// which is where the type of a query parameter lives. The entity pass runs whole before the
/// query pass, so all three are in place before the query parsers need them.
///
/// One context per conversion, found through the entity builder, which the orchestration
/// creates once per conversion and hands to both calls of the parser factory. The table is
/// weak on the key, so a finished conversion takes its context with it; nothing static
/// survives the run.
/// </summary>
public sealed class MyBatisReadingContext
{
    private static readonly ConditionalWeakTable<AbstractEntityBuilder, MyBatisReadingContext> Contexts = [];

    /// <summary>
    /// The context of the conversion the entity builder belongs to. Called by the parser
    /// factory, once per parser list, and the same instance comes back both times.
    /// </summary>
    public static MyBatisReadingContext For(AbstractEntityBuilder entityBuilder)
    {
        ArgumentNullException.ThrowIfNull(entityBuilder);
        return Contexts.GetValue(entityBuilder, _ => new MyBatisReadingContext());
    }

    private readonly Dictionary<string, HashSet<MyBatisStatementForm>> statements = new(StringComparer.Ordinal);

    private readonly Dictionary<string, XElement> fragments = new(StringComparer.Ordinal);

    private readonly Dictionary<string, MyBatisMethodSignature> signatures = new(StringComparer.Ordinal);

    /// <summary>Records that a statement of this id was declared in this form.</summary>
    public void DeclareStatement(string namespaceName, string id, MyBatisStatementForm form)
    {
        var key = Qualify(namespaceName, id);

        if (!statements.TryGetValue(key, out var forms))
        {
            statements[key] = forms = [];
        }

        forms.Add(form);
    }

    /// <summary>
    /// Whether both forms declare this statement, which is the input MyBatis 3.5.19 refuses
    /// while building the SqlSessionFactory ("Mapped Statements collection already contains
    /// key"). No precedence can be applied to it, so it is a Failure (decision 068).
    /// </summary>
    public bool IsDeclaredTwice(string namespaceName, string id)
        => statements.TryGetValue(Qualify(namespaceName, id), out var forms) && forms.Count > 1;

    /// <summary>The body of a &lt;sql&gt; fragment, kept under the namespace of its document.</summary>
    public void DeclareFragment(string namespaceName, string id, XElement body)
        => fragments[Qualify(namespaceName, id)] = body;

    /// <summary>
    /// The fragment an &lt;include refid&gt; names, looked up inside the document that wrote
    /// the include: the unit of conversion is one artifact, and reaching for a fragment
    /// elsewhere would make the result depend on what the user happened to attach.
    /// </summary>
    public XElement? FragmentOf(string namespaceName, string refId)
        => fragments.GetValueOrDefault(Qualify(namespaceName, refId));

    public void DeclareMethod(string namespaceName, MyBatisMethodSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        signatures[Qualify(namespaceName, signature.Name)] = signature;
    }

    /// <summary>
    /// The method a statement pairs with, by name. Null where the interface is not part of
    /// the conversion or names no such method, and that is no verdict: the scalar of the
    /// parameter is then derived by the builder template like any other (decision 084).
    /// </summary>
    public MyBatisMethodSignature? MethodOf(string namespaceName, string id)
        => signatures.GetValueOrDefault(Qualify(namespaceName, id));

    private static string Qualify(string namespaceName, string id)
        => string.IsNullOrEmpty(namespaceName) ? id : $"{namespaceName}.{id}";
}
