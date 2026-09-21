using System.Text;
using AbstractWrappers;

namespace JavaEntityParsing;

/// <summary>
/// Reads the subset of Java a mapped class is made of (decision 076): the package and
/// the imports, the class header with its annotations and modifiers, the fields and the
/// method headers with theirs, and nested classes. Since decision 084 a top-level
/// interface is read too, with its method headers and their declared parameters, because
/// a MyBatis mapper is an interface. Everything else - method bodies, constructors,
/// initializer blocks, enums, records - is skipped by matching braces and parentheses over
/// the complete token stream, so that a lambda or an anonymous class inside a body never
/// reaches the reader.
///
/// A shape the reader does not expect is a <see cref="JavaSyntaxError"/> with a line and a
/// column, never a guess: the language read here is closed by what the intermediate
/// representation carries, and the tool itself emits it (decision 062, carried over to
/// Java by decision 076).
/// </summary>
public sealed class JavaClassReader
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "static", "final", "abstract", "transient", "volatile",
        "synchronized", "native", "strictfp", "sealed", "non-sealed", "default",
    };

    private static readonly HashSet<string> Primitives = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "boolean", "float", "double", "char", "void",
    };

    private readonly string source;
    private readonly List<JavaToken> tokens;
    private readonly ParseLimits limits;
    private int position;

    /// <summary>
    /// How deep the type arguments being read are nested. Counted here rather than by the
    /// shared guard because Java's angle brackets are not brackets: &lt; is a comparison in an
    /// initializer and a type argument in a declaration, and only the reader knows which it is
    /// looking at (decision 092). The number it is measured against is the same one.
    /// </summary>
    private int typeArgumentDepth;

    private JavaClassReader(string source, ParseLimits limits)
    {
        this.source = source;
        this.limits = limits;
        tokens = JavaLexer.Lex(source);
    }

    /// <param name="limits">
    /// The limits the caller reads under (decision 092); the default cap when it says nothing.
    /// </param>
    public static JavaCompilationUnit Read(string source, ParseLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var reader = new JavaClassReader(source, limits ?? ParseLimits.Default);

        // Before the descent, because the descent is what the cap protects: from here on the
        // depth of the input is the depth of the recursion, and an overflow ends the process
        // rather than the reading. Lexing is a loop, so the tokens are safe to have at any
        // depth - it is walking them as a tree that is not.
        if (NestingDepthGuard.FirstBeyond(Tracked(reader.tokens), reader.limits) is { } tooDeep)
        {
            throw new JavaInputTooDeep(tooDeep);
        }

        return reader.ReadCompilationUnit();
    }

    /// <summary>
    /// The lexer's own tokens as the shared nesting guard reads them - text and position, and
    /// nothing else, because that is all a token type has in common across five languages
    /// (decision 092). Lazy on purpose: the guard stops at the first token past the cap.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(IEnumerable<JavaToken> read)
        => read.Select(token => new SourceToken(token.Text, token.Line, token.Column));

    /* ---- token helpers -------------------------------------------------------------- */

    private JavaToken Current => tokens[position];

    private JavaToken Peek(int ahead = 1) => tokens[Math.Min(position + ahead, tokens.Count - 1)];

    private void Advance() => position++;

    private bool AtSymbol(string symbol) => Current.Kind == JavaTokenKind.Symbol && Current.Text == symbol;

    private bool AtWord(string word) => Current.Kind == JavaTokenKind.Identifier && Current.Text == word;

    private bool TryConsumeSymbol(string symbol)
    {
        if (!AtSymbol(symbol))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void ConsumeSymbol(string symbol)
    {
        if (!TryConsumeSymbol(symbol))
        {
            throw Error($"expected '{symbol}'");
        }
    }

    private bool TryConsumeWord(string word)
    {
        if (!AtWord(word))
        {
            return false;
        }

        Advance();
        return true;
    }

    private string ConsumeIdentifier(string expectation)
    {
        if (Current.Kind != JavaTokenKind.Identifier)
        {
            throw Error(expectation);
        }

        var text = Current.Text;
        Advance();
        return text;
    }

    private JavaSyntaxError Error(string message) => new(
        Current.Line,
        Current.Column,
        Current.Kind == JavaTokenKind.End ? $"{message}, found the end of the source" : $"{message}, found '{Current.Text}'");

    /* ---- compilation unit ----------------------------------------------------------- */

    private JavaCompilationUnit ReadCompilationUnit()
    {
        string? package = null;
        var imports = new List<string>();
        var classes = new List<JavaClass>();
        var interfaces = new List<JavaInterface>();

        // A package declaration may carry annotations (package-info.java); they are skipped.
        var leading = position;
        SkipAnnotations();
        if (TryConsumeWord("package"))
        {
            package = ReadQualifiedName();
            ConsumeSymbol(";");
        }
        else
        {
            position = leading;
        }

        while (TryConsumeWord("import"))
        {
            TryConsumeWord("static");
            var name = new StringBuilder(ReadQualifiedName());
            if (TryConsumeSymbol("."))
            {
                ConsumeSymbol("*");
                name.Append(".*");
            }

            ConsumeSymbol(";");
            imports.Add(name.ToString());
        }

        while (Current.Kind != JavaTokenKind.End)
        {
            if (TryConsumeSymbol(";"))
            {
                continue;
            }

            var (declaredClass, declaredInterface) = ReadTypeDeclaration();
            if (declaredClass is not null)
            {
                classes.Add(declaredClass);
            }

            if (declaredInterface is not null)
            {
                interfaces.Add(declaredInterface);
            }
        }

        return new JavaCompilationUnit(package, imports, classes, interfaces);
    }

    private string ReadQualifiedName()
    {
        var name = new StringBuilder(ConsumeIdentifier("expected a name"));
        while (AtSymbol(".") && Peek().Kind == JavaTokenKind.Identifier)
        {
            Advance();
            name.Append('.').Append(ConsumeIdentifier("expected a name"));
        }

        return name.ToString();
    }

    /* ---- type declarations ---------------------------------------------------------- */

    /// <summary>
    /// A class becomes a <see cref="JavaClass"/> and an interface a
    /// <see cref="JavaInterface"/> (decision 084); an enum, a record or an annotation type
    /// is skipped whole, because none of them can be an entity (an entity may not be a
    /// record, an enum or an interface - Jakarta Persistence 3.2 §2.1) and none of them is
    /// a mapper either.
    /// </summary>
    private (JavaClass? Class, JavaInterface? Interface) ReadTypeDeclaration()
    {
        var line = Current.Line;
        var annotations = ReadAnnotations();
        var modifiers = ReadModifiers();

        if (TryConsumeSymbol("@"))
        {
            // @interface: an annotation type declaration.
            if (!TryConsumeWord("interface"))
            {
                throw Error("expected 'interface' after '@'");
            }

            ConsumeIdentifier("expected a name");
            SkipBlock();
            return (null, null);
        }

        if (TryConsumeWord("class"))
        {
            return (ReadClassBody(annotations, modifiers, line), null);
        }

        if (TryConsumeWord("interface"))
        {
            return (null, ReadInterfaceBody(annotations, modifiers, line));
        }

        if (TryConsumeWord("enum") || TryConsumeWord("record"))
        {
            ConsumeIdentifier("expected a name");
            SkipUntilBlock();
            SkipBlock();
            return (null, null);
        }

        throw Error("expected a type declaration");
    }

    /// <summary>
    /// An interface body: its method headers with their annotations and declared
    /// parameters. Constants, nested types and the bodies of default and static methods are
    /// skipped the same way a class's are - what a mapper states, it states in the header.
    /// </summary>
    private JavaInterface ReadInterfaceBody(IReadOnlyList<JavaAnnotation> annotations, IReadOnlyList<string> modifiers, int line)
    {
        var name = ConsumeIdentifier("expected an interface name");

        if (AtSymbol("<"))
        {
            SkipTypeArguments();
        }

        var extends = new List<string>();

        if (TryConsumeWord("extends"))
        {
            do
            {
                extends.Add(ReadType());
            }
            while (TryConsumeSymbol(","));
        }

        if (TryConsumeWord("permits"))
        {
            do
            {
                ReadType();
            }
            while (TryConsumeSymbol(","));
        }

        ConsumeSymbol("{");

        var methods = new List<JavaMethod>();

        while (!AtSymbol("}"))
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error($"expected '}}' closing interface {name}");
            }

            ReadInterfaceMember(methods);
        }

        ConsumeSymbol("}");

        return new JavaInterface(name, modifiers, annotations, methods, extends, line);
    }

    private void ReadInterfaceMember(List<JavaMethod> methods)
    {
        if (TryConsumeSymbol(";"))
        {
            return;
        }

        var line = Current.Line;
        var memberAnnotations = ReadAnnotations();
        var modifiers = ReadModifiers();

        if (AtSymbol("@") && Peek() is { Kind: JavaTokenKind.Identifier, Text: "interface" })
        {
            Advance();
            Advance();
            ConsumeIdentifier("expected a name");
            SkipBlock();
            return;
        }

        if (TryConsumeWord("class") || TryConsumeWord("interface") || TryConsumeWord("enum") || TryConsumeWord("record"))
        {
            ConsumeIdentifier("expected a name");
            SkipUntilBlock();
            SkipBlock();
            return;
        }

        if (AtSymbol("<"))
        {
            SkipTypeArguments();
        }

        var type = ReadType();
        var memberName = ConsumeIdentifier("expected a member name");

        if (AtSymbol("("))
        {
            var parameters = ReadParameters();
            while (TryConsumeSymbol("["))
            {
                ConsumeSymbol("]");
            }

            SkipThrows();

            if (!TryConsumeSymbol(";"))
            {
                // A default or static method carries a body; the header is all that is read.
                SkipBlock();
            }

            methods.Add(new JavaMethod(memberName, type, modifiers, memberAnnotations, parameters, line));
            return;
        }

        // A constant: interface fields are implicitly public static final and are not read.
        SkipUntilSymbolAtDepthZero(";");
        ConsumeSymbol(";");
    }

    private JavaClass ReadClassBody(IReadOnlyList<JavaAnnotation> annotations, IReadOnlyList<string> modifiers, int line)
    {
        var name = ConsumeIdentifier("expected a class name");

        if (AtSymbol("<"))
        {
            SkipTypeArguments();
        }

        string? extends = null;
        var interfaces = new List<string>();

        if (TryConsumeWord("extends"))
        {
            extends = ReadType();
        }

        if (TryConsumeWord("implements"))
        {
            do
            {
                interfaces.Add(ReadType());
            }
            while (TryConsumeSymbol(","));
        }

        if (TryConsumeWord("permits"))
        {
            do
            {
                ReadType();
            }
            while (TryConsumeSymbol(","));
        }

        ConsumeSymbol("{");

        var fields = new List<JavaField>();
        var methods = new List<JavaMethod>();
        var nested = new List<JavaClass>();

        while (!AtSymbol("}"))
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error($"expected '}}' closing class {name}");
            }

            ReadMember(name, fields, methods, nested);
        }

        ConsumeSymbol("}");

        return new JavaClass(name, modifiers, annotations, fields, methods, nested, extends, interfaces, line);
    }

    private void ReadMember(string className, List<JavaField> fields, List<JavaMethod> methods, List<JavaClass> nested)
    {
        if (TryConsumeSymbol(";"))
        {
            return;
        }

        // An initializer block, static or not.
        if (AtSymbol("{") || (AtWord("static") && Peek() is { Kind: JavaTokenKind.Symbol, Text: "{" }))
        {
            TryConsumeWord("static");
            SkipBlock();
            return;
        }

        var line = Current.Line;
        var annotations = ReadAnnotations();
        var modifiers = ReadModifiers();

        if (AtSymbol("@") && Peek() is { Kind: JavaTokenKind.Identifier, Text: "interface" })
        {
            Advance();
            Advance();
            ConsumeIdentifier("expected a name");
            SkipBlock();
            return;
        }

        if (TryConsumeWord("class"))
        {
            nested.Add(ReadClassBody(annotations, modifiers, line));
            return;
        }

        if (TryConsumeWord("interface") || TryConsumeWord("enum") || TryConsumeWord("record"))
        {
            ConsumeIdentifier("expected a name");
            SkipUntilBlock();
            SkipBlock();
            return;
        }

        // A generic method declares its type parameters before the return type.
        if (AtSymbol("<"))
        {
            SkipTypeArguments();
        }

        // A constructor: the class name followed by a parameter list.
        if (AtWord(className) && Peek() is { Kind: JavaTokenKind.Symbol, Text: "(" })
        {
            Advance();
            SkipParenthesized();
            SkipThrows();
            SkipBlock();
            return;
        }

        var type = ReadType();
        var memberName = ConsumeIdentifier("expected a member name");

        if (AtSymbol("("))
        {
            var parameters = ReadParameters();
            while (TryConsumeSymbol("["))
            {
                ConsumeSymbol("]");
            }

            SkipThrows();

            if (TryConsumeSymbol(";"))
            {
                // abstract or native
            }
            else if (TryConsumeWord("default"))
            {
                // an annotation element default; not reachable inside a class, kept for completeness
                SkipUntilSymbolAtDepthZero(";");
                ConsumeSymbol(";");
            }
            else
            {
                SkipBlock();
            }

            methods.Add(new JavaMethod(memberName, type, modifiers, annotations, parameters, line));
            return;
        }

        // One or more field declarators: int a = 1, b;
        while (true)
        {
            var fieldType = type;
            while (TryConsumeSymbol("["))
            {
                ConsumeSymbol("]");
                fieldType += "[]";
            }

            string? initializer = null;
            if (TryConsumeSymbol("="))
            {
                initializer = ReadInitializerText();
            }

            fields.Add(new JavaField(memberName, fieldType, modifiers, annotations, initializer, line));

            if (TryConsumeSymbol(","))
            {
                memberName = ConsumeIdentifier("expected a field name");
                continue;
            }

            ConsumeSymbol(";");
            return;
        }
    }

    /// <summary>
    /// The original text of an initializer up to the comma or semicolon that ends the
    /// declarator, with nesting respected so that a lambda body or an array initializer
    /// with commas inside is not cut short.
    /// </summary>
    private string ReadInitializerText()
    {
        var start = Current.Offset;
        var depth = 0;
        var last = Current;

        while (Current.Kind != JavaTokenKind.End)
        {
            if (Current.Kind == JavaTokenKind.Symbol)
            {
                switch (Current.Text)
                {
                    case "(" or "{" or "[":
                        depth++;
                        break;
                    case ")" or "}" or "]":
                        depth--;
                        break;
                    case "," or ";" when depth == 0:
                        return source[start..(last.Offset + last.Length)].Trim();
                }
            }

            last = Current;
            Advance();
        }

        throw Error("expected ';' after the initializer");
    }

    private List<string> ReadModifiers()
    {
        var modifiers = new List<string>();

        while (Current.Kind == JavaTokenKind.Identifier && Modifiers.Contains(Current.Text)
               // "default" as a modifier appears only in interfaces; inside a class it
               // would be a switch label, which cannot stand at member level.
               && !(Current.Text == "sealed" && Peek() is { Kind: JavaTokenKind.Symbol, Text: "." }))
        {
            modifiers.Add(Current.Text);
            Advance();
        }

        return modifiers;
    }

    /* ---- types ---------------------------------------------------------------------- */

    /// <summary>
    /// A type in its normalized spelling: the (possibly qualified) name, type arguments
    /// recursively with ", " between them, and array brackets. Annotations inside a type
    /// (a type-use annotation such as @NonNull) are skipped.
    /// </summary>
    private string ReadType()
    {
        SkipAnnotations();

        var text = new StringBuilder();

        if (Current.Kind == JavaTokenKind.Identifier && Primitives.Contains(Current.Text))
        {
            text.Append(Current.Text);
            Advance();
        }
        else
        {
            text.Append(ConsumeIdentifier("expected a type"));

            while (true)
            {
                if (AtSymbol("<"))
                {
                    text.Append(ReadTypeArguments());
                }

                if (AtSymbol(".") && Peek().Kind == JavaTokenKind.Identifier)
                {
                    Advance();
                    text.Append('.').Append(ConsumeIdentifier("expected a type"));
                    continue;
                }

                break;
            }
        }

        while (AtSymbol("[") && Peek() is { Kind: JavaTokenKind.Symbol, Text: "]" })
        {
            Advance();
            Advance();
            text.Append("[]");
        }

        if (TryConsumeSymbol("..."))
        {
            text.Append("[]");
        }

        return text.ToString();
    }

    private string ReadTypeArguments()
    {
        // The one nesting the shared guard cannot count, so it is counted here, where the
        // angle bracket is known to open a type argument and not a comparison (decision 092).
        if (limits.CapsNesting && ++typeArgumentDepth > limits.MaxNestingDepth)
        {
            throw new JavaInputTooDeep(new SourceToken(Current.Text, Current.Line, Current.Column));
        }

        try
        {
            return ReadTypeArgumentsCore();
        }
        finally
        {
            typeArgumentDepth--;
        }
    }

    private string ReadTypeArgumentsCore()
    {
        ConsumeSymbol("<");
        var arguments = new List<string>();

        if (AtSymbol(">"))
        {
            // The diamond: new ArrayList<>() inside an initializer never reaches ReadType,
            // but a declaration "List<>" is not legal either; accept it rather than guess.
            Advance();
            return "<>";
        }

        do
        {
            SkipAnnotations();
            if (TryConsumeSymbol("?"))
            {
                var bound = new StringBuilder("?");
                if (TryConsumeWord("extends"))
                {
                    bound.Append(" extends ").Append(ReadType());
                }
                else if (TryConsumeWord("super"))
                {
                    bound.Append(" super ").Append(ReadType());
                }

                arguments.Add(bound.ToString());
            }
            else
            {
                arguments.Add(ReadType());
            }
        }
        while (TryConsumeSymbol(","));

        ConsumeSymbol(">");
        return $"<{string.Join(", ", arguments)}>";
    }

    private void SkipTypeArguments()
    {
        ConsumeSymbol("<");
        var depth = 1;
        while (depth > 0)
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error("expected '>'");
            }

            if (AtSymbol("<"))
            {
                depth++;
            }
            else if (AtSymbol(">"))
            {
                depth--;
            }

            Advance();
        }
    }

    /* ---- annotations ---------------------------------------------------------------- */

    private List<JavaAnnotation> ReadAnnotations()
    {
        var annotations = new List<JavaAnnotation>();

        while (AtSymbol("@") && !(Peek() is { Kind: JavaTokenKind.Identifier, Text: "interface" }))
        {
            annotations.Add(ReadAnnotation());
        }

        return annotations;
    }

    private void SkipAnnotations() => ReadAnnotations();

    private JavaAnnotation ReadAnnotation()
    {
        var line = Current.Line;
        var column = Current.Column;
        ConsumeSymbol("@");
        var name = ReadQualifiedName();
        var arguments = new List<JavaAnnotationArgument>();

        if (TryConsumeSymbol("("))
        {
            if (!AtSymbol(")"))
            {
                do
                {
                    string? element = null;
                    if (Current.Kind == JavaTokenKind.Identifier && Peek() is { Kind: JavaTokenKind.Symbol, Text: "=" })
                    {
                        element = Current.Text;
                        Advance();
                        Advance();
                    }

                    arguments.Add(new JavaAnnotationArgument(element, ReadAnnotationValue()));
                }
                while (TryConsumeSymbol(","));
            }

            ConsumeSymbol(")");
        }

        return new JavaAnnotation(name, arguments, line, column);
    }

    private JavaAnnotationValue ReadAnnotationValue()
    {
        if (AtSymbol("@"))
        {
            var nested = ReadAnnotation();
            return new JavaAnnotationValue(JavaAnnotationValueKind.Annotation, nested.Name, nested);
        }

        if (TryConsumeSymbol("{"))
        {
            var items = new List<JavaAnnotationValue>();
            while (!AtSymbol("}"))
            {
                items.Add(ReadAnnotationValue());
                if (!TryConsumeSymbol(","))
                {
                    break;
                }
            }

            ConsumeSymbol("}");
            return new JavaAnnotationValue(JavaAnnotationValueKind.Array, string.Empty, Items: items);
        }

        if (Current.Kind == JavaTokenKind.String)
        {
            var text = new StringBuilder(Current.Text);
            Advance();

            // Adjacent literals joined with + are one constant expression.
            while (AtSymbol("+") && Peek().Kind == JavaTokenKind.String)
            {
                Advance();
                text.Append(Current.Text);
                Advance();
            }

            return new JavaAnnotationValue(JavaAnnotationValueKind.String, text.ToString());
        }

        if (Current.Kind == JavaTokenKind.Char)
        {
            var text = Current.Text;
            Advance();
            return new JavaAnnotationValue(JavaAnnotationValueKind.Char, text);
        }

        if (Current.Kind == JavaTokenKind.Number
            || (AtSymbol("-") && Peek().Kind == JavaTokenKind.Number))
        {
            var negative = TryConsumeSymbol("-");
            var text = (negative ? "-" : string.Empty) + Current.Text;
            Advance();
            return IsOperatorNext()
                ? SkipExpressionValue(text)
                : new JavaAnnotationValue(JavaAnnotationValueKind.Number, text);
        }

        if (Current.Kind == JavaTokenKind.Identifier)
        {
            if (Current.Text is "true" or "false")
            {
                var text = Current.Text;
                Advance();
                return new JavaAnnotationValue(JavaAnnotationValueKind.Boolean, text);
            }

            // A qualified name read segment by segment, so that the ".class" of a class
            // literal is not taken for one of its segments - "class" lexes as an identifier.
            var name = new StringBuilder(ConsumeIdentifier("expected a name"));
            while (AtSymbol(".") && Peek().Kind == JavaTokenKind.Identifier && Peek().Text != "class")
            {
                Advance();
                name.Append('.').Append(ConsumeIdentifier("expected a name"));
            }

            if (AtSymbol(".") && Peek() is { Kind: JavaTokenKind.Identifier, Text: "class" })
            {
                Advance();
                Advance();
                return new JavaAnnotationValue(JavaAnnotationValueKind.ClassLiteral, name.ToString());
            }

            if (AtSymbol("<"))
            {
                // A generic class literal: List.class does not carry arguments, but be safe.
                SkipTypeArguments();
            }

            return IsOperatorNext()
                ? SkipExpressionValue(name.ToString())
                : new JavaAnnotationValue(JavaAnnotationValueKind.Name, name.ToString());
        }

        return SkipExpressionValue(string.Empty);
    }

    private bool IsOperatorNext()
        => Current.Kind == JavaTokenKind.Symbol && Current.Text is "+" or "-" or "*" or "/" or "%" or "(" or "[" or "?" or "&" or "|" or "^" or "<" or ">";

    /// <summary>
    /// Consumes a value the reader does not model - an expression - up to the comma or
    /// parenthesis that ends the argument, and keeps its text so that a record can name it.
    /// </summary>
    private JavaAnnotationValue SkipExpressionValue(string prefix)
    {
        var start = Current.Offset;
        var depth = 0;
        var last = Current;
        var consumed = false;

        while (Current.Kind != JavaTokenKind.End)
        {
            if (Current.Kind == JavaTokenKind.Symbol)
            {
                if (Current.Text is "(" or "{" or "[")
                {
                    depth++;
                }
                else if (Current.Text is ")" or "}" or "]")
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (Current.Text == "," && depth == 0)
                {
                    break;
                }
            }

            last = Current;
            consumed = true;
            Advance();
        }

        var text = consumed ? source[start..(last.Offset + last.Length)] : string.Empty;
        return new JavaAnnotationValue(JavaAnnotationValueKind.Other, (prefix + text).Trim());
    }

    /* ---- skipping ------------------------------------------------------------------- */

    /// <summary>
    /// The declared parameters of a method (decision 084). A parameter list is well formed
    /// wherever it stands, so it is read rather than skipped: its annotations carry
    /// MyBatis's @Param and its types are the only place the type of a query parameter
    /// lives. A varargs parameter reads as an array, which is what it is.
    /// </summary>
    private List<JavaParameter> ReadParameters()
    {
        ConsumeSymbol("(");

        var parameters = new List<JavaParameter>();

        if (!AtSymbol(")"))
        {
            do
            {
                var annotations = ReadAnnotations();

                while (TryConsumeWord("final"))
                {
                    // A parameter may be final; it says nothing about the declaration.
                }

                var type = ReadType();
                var name = ConsumeIdentifier("expected a parameter name");

                while (TryConsumeSymbol("["))
                {
                    ConsumeSymbol("]");
                    type += "[]";
                }

                parameters.Add(new JavaParameter(name, type, annotations));
            }
            while (TryConsumeSymbol(","));
        }

        ConsumeSymbol(")");

        return parameters;
    }

    /// <summary>Skips a parenthesized list and returns the number of top-level comma-separated items in it.</summary>
    private int SkipParenthesized()
    {
        ConsumeSymbol("(");
        var depth = 1;
        var items = AtSymbol(")") ? 0 : 1;

        while (depth > 0)
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error("expected ')'");
            }

            if (Current.Kind == JavaTokenKind.Symbol)
            {
                switch (Current.Text)
                {
                    case "(" or "{" or "[":
                        depth++;
                        break;
                    case ")" or "}" or "]":
                        depth--;
                        break;
                    case "," when depth == 1:
                        items++;
                        break;
                }
            }

            Advance();
        }

        return items;
    }

    private void SkipThrows()
    {
        if (TryConsumeWord("throws"))
        {
            do
            {
                ReadType();
            }
            while (TryConsumeSymbol(","));
        }
    }

    private void SkipUntilBlock()
    {
        while (!AtSymbol("{"))
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error("expected '{'");
            }

            if (AtSymbol("("))
            {
                SkipParenthesized();
                continue;
            }

            Advance();
        }
    }

    private void SkipUntilSymbolAtDepthZero(string symbol)
    {
        var depth = 0;
        while (!(depth == 0 && AtSymbol(symbol)))
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error($"expected '{symbol}'");
            }

            if (Current.Kind == JavaTokenKind.Symbol)
            {
                if (Current.Text is "(" or "{" or "[")
                {
                    depth++;
                }
                else if (Current.Text is ")" or "}" or "]")
                {
                    depth--;
                }
            }

            Advance();
        }
    }

    private void SkipBlock()
    {
        ConsumeSymbol("{");
        var depth = 1;

        while (depth > 0)
        {
            if (Current.Kind == JavaTokenKind.End)
            {
                throw Error("expected '}'");
            }

            if (AtSymbol("{"))
            {
                depth++;
            }
            else if (AtSymbol("}"))
            {
                depth--;
            }

            Advance();
        }
    }
}
