using System.Text.RegularExpressions;
using Model;
using Tests.Verification;

namespace Tests.Foreign;

/// <summary>
/// The envelope of the files of a reply and its repair (decision 204): what a consumer project
/// gives the bare method of the tool - the using directives and imports of the target, the
/// namespace or package of the entities in scope, the class around a method, one public type per
/// Java file, the extension a role has, the namespace of a MyBatis mapper document - given to a
/// whole file that lacks it. The import applies it to every variant but <c>zero-shot</c>, which is
/// judged as it came, and every repair is one sentence in the manifest. Nothing here touches a
/// mapping fact, a query text, a parameter, or an entity the model did not write: those are the
/// model's result in every variant.
/// </summary>
internal static class WebEnvelope
{
    public const string ZeroShot = "zero-shot";

    /// <summary>The value of <c>envelope</c> in the manifest of an attempt the import did not touch.</summary>
    public const string AsWritten = "as written";

    /// <summary>The value where the import repairs the variant's envelope and found nothing to repair.</summary>
    public const string Intact = "intact";

    /// <summary>The value where the import repaired something; <c>envelopeRepairs</c> says what.</summary>
    public const string Repaired = "repaired";

    private static readonly Regex CSharpNamespace = new(@"^\s*namespace\s+([\w.]+)\s*[;{]", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpUsing = new(@"^\s*(?:global\s+)?using\s+(?:static\s+)?([\w.]+)\s*;", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex CSharpUsingLine = new(@"^\s*(?:global\s+)?using\s", RegexOptions.CultureInvariant);

    private static readonly Regex CSharpHeaderLine = new(@"^\s*(?:(?:global\s+)?using\s.*;|namespace\s+[\w.]+\s*;|//.*)?\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex JavaPackage = new(@"^\s*package\s+([\w.]+)\s*;", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex JavaImport = new(@"^\s*import\s+(?:static\s+)?([\w.]+(?:\.\*)?)\s*;", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex JavaImportLine = new(@"^\s*import\s", RegexOptions.CultureInvariant);

    private static readonly Regex JavaPackageLine = new(@"^\s*package\s", RegexOptions.CultureInvariant);

    private static readonly Regex JavaHeaderLine = new(@"^\s*(?:package\s.*;|import\s.*;|//.*)?\s*$", RegexOptions.CultureInvariant);

    /// <summary>A public top-level type, at the start of a line as the Java suite reads it (a nested one is indented).</summary>
    private static readonly Regex JavaPublicType = new(@"^public\s+(?:(?:final|abstract|sealed|non-sealed|strictfp|static)\s+)*(?:class|interface|enum|record)\s+(\w+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>The same reading of a whole file as <c>JavaSources.isCompilationUnit</c> of the Java suite.</summary>
    private static readonly Regex JavaCompilationUnit = new(@"^[ \t]*(?:package\s|import\s|(?:@\w+(?:\([^)]*\))?\s*)*(?:(?:public|final|abstract|sealed|non-sealed|strictfp)\s+)*(?:class|interface|enum|record)\s+\w+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex JavaPublicInterface = new(@"^public\s+interface\s+(\w+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Any top-level type, public or not, at the start of a line.</summary>
    private static readonly Regex JavaTopLevelType = new(@"^(?:(?:public|final|abstract|sealed|non-sealed|strictfp)\s+)*(?:class|interface|enum|record)\s+\w+", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>A block-scoped namespace, which the wrap of a bare method does not reach into.</summary>
    private static readonly Regex CSharpBlockNamespace = new(@"^\s*namespace\s+[\w.]+\s*\{", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex MapperNamespace = new(@"<mapper\s+namespace\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);

    /// <summary>The imports a consumer gives a JPA query method (<c>JavaSources.wrapQuery</c>).</summary>
    private static readonly string[] JpaQueryImports =
    [
        "jakarta.persistence.EntityManager", "jakarta.persistence.Query", "jakarta.persistence.TypedQuery",
        "java.math.BigDecimal", "java.time.Duration", "java.time.LocalDate", "java.time.LocalDateTime", "java.time.LocalTime", "java.time.OffsetDateTime",
        "java.util.Collection", "java.util.UUID",
    ];

    /// <summary>The imports a consumer gives a mapper method (<c>JavaSources.wrapMapperInterface</c>).</summary>
    private static readonly string[] MapperImports =
    [
        "java.math.BigDecimal", "java.time.Duration", "java.time.LocalDate", "java.time.LocalDateTime", "java.time.LocalTime", "java.time.OffsetDateTime",
        "java.util.Collection", "java.util.List", "java.util.Map", "java.util.UUID", "org.apache.ibatis.annotations.Param",
    ];

    /// <summary>Whether the import repairs the envelope of an attempt of the variant: every variant but <c>zero-shot</c> (decision 204).</summary>
    public static bool Repairs(string variant) => !string.Equals(variant.Trim(), ZeroShot, StringComparison.OrdinalIgnoreCase);

    /// <summary>The files with the envelope repaired, and one sentence per repair; no sentence where there was nothing to repair.</summary>
    public static (List<WebReplyFile> Files, List<string> Repairs) Repair(IReadOnlyList<WebReplyFile> files, ORMEnum target)
    {
        var repairs = new List<string>();

        // The extension by the role (decision 111: the role names the language).
        var result = new List<WebReplyFile>();
        foreach (var file in files)
        {
            if (RoleOf(file) is { } role && Extension(role) is { } extension && !file.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                var renamed = Path.ChangeExtension(file.Path, extension).Replace('\\', '/');
                repairs.Add($"{file.Path} renamed to {renamed}: a file of role {role} ends in {extension}");
                result.Add(file with { Path = renamed });
            }
            else
            {
                result.Add(file);
            }
        }

        // One public type per Java file (JLS §7.6; JavaProject.add names the file after it).
        result = [.. result.SelectMany(file => IsJava(file) ? Split(file, repairs) : [file])];

        // The class around a method that already carries its directives.
        result = [.. result.Select(file => Wrap(file, target, result, repairs))];

        // The directives of the target and the namespace or package of the entities in scope.
        var entityNamespaces = result.Where(file => RoleOf(file) == ConversionContentType.CSharpEntity)
            .SelectMany(file => CSharpNamespace.Matches(file.Content).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var entityPackage = result.Where(file => RoleOf(file) == ConversionContentType.JavaEntity)
            .Select(file => JavaPackage.Match(file.Content))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .FirstOrDefault();
        result = [.. result.Select(file => Directives(file, target, entityNamespaces, entityPackage, repairs))];

        // The namespace of a mapper document binds it to the interface (JavaSources.wrapMapperInterface named the interface after it).
        if (target == ORMEnum.MyBatis)
        {
            result = [.. Bind(result, repairs)];
        }

        return (result, repairs);
    }

    private static ConversionContentType? RoleOf(WebReplyFile file)
        => Enum.TryParse<ConversionContentType>(file.Role, ignoreCase: true, out var role) ? role : null;

    private static bool IsJava(WebReplyFile file) => RoleOf(file) is ConversionContentType.JavaEntity or ConversionContentType.JavaQuery or ConversionContentType.Java;

    private static bool IsCSharp(WebReplyFile file) => RoleOf(file) is ConversionContentType.CSharpEntity or ConversionContentType.CSharpQuery or ConversionContentType.CSharpLinqQuery or ConversionContentType.CSharp;

    private static bool IsQuery(WebReplyFile file) => RoleOf(file) is ConversionContentType.CSharpQuery or ConversionContentType.CSharpLinqQuery or ConversionContentType.JavaQuery;

    internal static string? Extension(ConversionContentType role) => role switch
    {
        ConversionContentType.CSharpEntity or ConversionContentType.CSharpQuery or ConversionContentType.CSharpLinqQuery or ConversionContentType.CSharp => ".cs",
        ConversionContentType.JavaEntity or ConversionContentType.JavaQuery or ConversionContentType.Java => ".java",
        ConversionContentType.XML => ".xml",
        ConversionContentType.SqlQuery => ".sql",
        ConversionContentType.HqlQuery => ".hql",
        ConversionContentType.JpqlQuery => ".jpql",
        _ => null,
    };

    /// <summary>A Java file with several public top-level types, one file per type, each with the package and imports of the original; any other file as it is.</summary>
    private static IEnumerable<WebReplyFile> Split(WebReplyFile file, List<string> repairs)
    {
        var lines = file.Content.Split('\n');
        var starts = new List<(int Line, string Name)>();
        for (var index = 0; index < lines.Length; index++)
        {
            var match = JavaPublicType.Match(lines[index]);
            if (match.Success && match.Index == 0)
            {
                // The annotations and the comment directly above the declaration belong to it.
                var start = index;
                while (start > 0 && lines[start - 1].TrimStart() is { Length: > 0 } above && (above.StartsWith('@') || above.StartsWith("/*", StringComparison.Ordinal) || above.StartsWith('*') || above.StartsWith("//", StringComparison.Ordinal)))
                {
                    start--;
                }

                starts.Add((start, match.Groups[1].Value));
            }
        }

        if (starts.Count < 2)
        {
            return [file];
        }

        var header = string.Join('\n', lines[..starts[0].Line]).TrimEnd();
        var directory = file.Path.Contains('/') ? file.Path[..(file.Path.LastIndexOf('/') + 1)] : string.Empty;
        var pieces = new List<WebReplyFile>();
        for (var index = 0; index < starts.Count; index++)
        {
            var end = index + 1 < starts.Count ? starts[index + 1].Line : lines.Length;
            var body = string.Join('\n', lines[starts[index].Line..end]).Trim('\n');
            var content = (header.Length > 0 ? header + "\n\n" : string.Empty) + body + "\n";
            pieces.Add(file with { Path = directory + starts[index].Name + ".java", Content = content, Cut = file.Cut && index == starts.Count - 1 });
        }

        repairs.Add($"{file.Path} split into {string.Join(", ", pieces.Select(piece => piece.Path))}: one public type per file");
        return pieces;
    }

    /// <summary>A query file that carries directives and no type around its method gets the class a consumer puts around a bare method.</summary>
    private static WebReplyFile Wrap(WebReplyFile file, ORMEnum target, IReadOnlyList<WebReplyFile> all, List<string> repairs)
    {
        if (!IsQuery(file))
        {
            return file;
        }

        if (IsCSharp(file))
        {
            if (!GeneratedQueryCompiler.IsCompilationUnit(file.Content) || GeneratedQueryCompiler.DeclaresAType(file.Content) || CSharpBlockNamespace.IsMatch(file.Content))
            {
                return file;
            }

            var (header, body) = SplitHeader(file.Content, CSharpHeaderLine);
            repairs.Add($"{file.Path}: the method wrapped in a public static class; the file carried directives and no class");
            return file with { Content = header + "public static class GeneratedQueries\n{\n" + body.TrimEnd('\n') + "\n}\n" };
        }

        if (!JavaCompilationUnit.IsMatch(file.Content) || JavaTopLevelType.IsMatch(file.Content))
        {
            return file;
        }

        var (javaHeader, javaBody) = SplitHeader(file.Content, JavaHeaderLine);
        if (target == ORMEnum.MyBatis)
        {
            var name = all.Where(other => RoleOf(other) == ConversionContentType.XML)
                .Select(other => MapperNamespace.Match(other.Content))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value[(match.Groups[1].Value.LastIndexOf('.') + 1)..])
                .FirstOrDefault(name => name.Length > 0) ?? "GeneratedMapper";
            repairs.Add($"{file.Path}: the method wrapped in the interface {name}; the file carried directives and no interface");
            return file with { Content = javaHeader + "public interface " + name + " {\n" + Indent(javaBody) + "}\n" };
        }

        repairs.Add($"{file.Path}: the method wrapped in a public class; the file carried directives and no class");
        return file with { Content = javaHeader + "public class GeneratedQueries {\n" + Indent(javaBody) + "}\n" };
    }

    /// <summary>The leading lines that are directives, a package or namespace statement, comments or blank, and the rest.</summary>
    private static (string Header, string Body) SplitHeader(string content, Regex headerLine)
    {
        var lines = content.Split('\n');
        var end = 0;
        while (end < lines.Length && headerLine.IsMatch(lines[end]))
        {
            end++;
        }

        var header = string.Join('\n', lines[..end]).TrimEnd();
        return (header.Length > 0 ? header + "\n\n" : string.Empty, string.Join('\n', lines[end..]));
    }

    private static string Indent(string body)
        => string.Join('\n', body.TrimEnd('\n').Split('\n').Select(line => line.Length > 0 ? "    " + line : line)) + "\n";

    /// <summary>The directives of the target the file lacks, and the namespace or package of the entities where the file is in another.</summary>
    private static WebReplyFile Directives(WebReplyFile file, ORMEnum target, IReadOnlyList<string> entityNamespaces, string? entityPackage, List<string> repairs)
    {
        var role = RoleOf(file);

        if (IsCSharp(file) && GeneratedQueryCompiler.IsCompilationUnit(file.Content))
        {
            var needed = Usings(target, role!.Value).ToList();
            if (IsQuery(file))
            {
                var own = CSharpNamespace.Matches(file.Content).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
                needed.AddRange(entityNamespaces.Where(ns => !own.Contains(ns)));
            }

            var present = CSharpUsing.Matches(file.Content).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var missing = needed.Where(ns => !present.Contains(ns)).Distinct(StringComparer.Ordinal).Select(ns => $"using {ns};").ToList();
            if (missing.Count == 0)
            {
                return file;
            }

            repairs.Add($"{file.Path}: added {string.Join(" ", missing)}");
            return file with { Content = Insert(file.Content, missing, line => CSharpUsingLine.IsMatch(line), before: true, fallback: null) };
        }

        if (IsJava(file) && JavaCompilationUnit.IsMatch(file.Content))
        {
            var needed = Imports(target, role!.Value, file.Content).ToList();
            if (IsQuery(file) && entityPackage is not null)
            {
                var own = JavaPackage.Match(file.Content);
                if (!own.Success || own.Groups[1].Value != entityPackage)
                {
                    needed.Add(entityPackage + ".*");
                }
            }

            var present = JavaImport.Matches(file.Content).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var missing = needed
                .Where(import => !present.Contains(import) && !(import.Contains('.') && present.Contains(import[..import.LastIndexOf('.')] + ".*")))
                .Distinct(StringComparer.Ordinal)
                .Select(import => $"import {import};")
                .ToList();
            if (missing.Count == 0)
            {
                return file;
            }

            repairs.Add($"{file.Path}: added {string.Join(" ", missing)}");
            return file with { Content = Insert(file.Content, missing, line => JavaImportLine.IsMatch(line), before: false, fallback: line => JavaPackageLine.IsMatch(line)) };
        }

        return file;
    }

    private static IEnumerable<string> Usings(ORMEnum target, ConversionContentType role) => (target, role) switch
    {
        (ORMEnum.Dapper, ConversionContentType.CSharpQuery) => ["Dapper", "System.Data"],
        (ORMEnum.EFCore, ConversionContentType.CSharpQuery) => ["Microsoft.EntityFrameworkCore"],
        (ORMEnum.EFCore, ConversionContentType.CSharpEntity) => ["System.ComponentModel.DataAnnotations", "System.ComponentModel.DataAnnotations.Schema", "Microsoft.EntityFrameworkCore"],
        (ORMEnum.NHibernate, ConversionContentType.CSharpQuery) => ["NHibernate"],
        (ORMEnum.NHibernate, ConversionContentType.CSharpLinqQuery) => ["NHibernate", "NHibernate.Linq"],
        _ => [],
    };

    private static IEnumerable<string> Imports(ORMEnum target, ConversionContentType role, string content) => (target, role) switch
    {
        (ORMEnum.Hibernate or ORMEnum.EclipseLink, ConversionContentType.JavaQuery) => target == ORMEnum.EclipseLink && content.Contains("QueryHints.", StringComparison.Ordinal)
            ? [.. JpaQueryImports, "org.eclipse.persistence.config.HintValues", "org.eclipse.persistence.config.QueryHints"]
            : JpaQueryImports,
        (ORMEnum.Hibernate or ORMEnum.EclipseLink, ConversionContentType.JavaEntity) => ["jakarta.persistence.*"],
        (ORMEnum.MyBatis, ConversionContentType.JavaQuery) => MapperImports,
        _ => [],
    };

    /// <summary>
    /// The lines inserted where the file keeps such lines: before the first line the anchor
    /// matches, or after the last one; where there is none, after the line the fallback matches,
    /// or at the top of the file.
    /// </summary>
    private static string Insert(string content, IReadOnlyList<string> lines, Func<string, bool> anchor, bool before, Func<string, bool>? fallback)
    {
        var existing = content.Split('\n').ToList();
        var anchored = existing.Select((line, index) => (Line: line, Index: index)).Where(pair => anchor(pair.Line)).Select(pair => pair.Index).ToList();

        int at;
        if (anchored.Count > 0)
        {
            at = before ? anchored[0] : anchored[^1] + 1;
        }
        else if (fallback is not null && existing.FindIndex(line => fallback(line)) is var found && found >= 0)
        {
            at = found + 1;
            existing.Insert(at, string.Empty);
            at++;
        }
        else
        {
            at = 0;
        }

        existing.InsertRange(at, lines);
        if (anchored.Count == 0 && at == 0 && existing.Count > lines.Count && existing[lines.Count].Trim().Length > 0)
        {
            existing.Insert(lines.Count, string.Empty);
        }

        return string.Join('\n', existing);
    }

    /// <summary>A mapper document whose namespace names no interface of the artifact is bound to the one interface the artifact declares.</summary>
    private static IEnumerable<WebReplyFile> Bind(IReadOnlyList<WebReplyFile> files, List<string> repairs)
    {
        var interfaces = files
            .Where(file => RoleOf(file) == ConversionContentType.JavaQuery)
            .Select(file => (Match: JavaPublicInterface.Match(file.Content), Package: JavaPackage.Match(file.Content)))
            .Where(pair => pair.Match.Success)
            .Select(pair => pair.Package.Success ? pair.Package.Groups[1].Value + "." + pair.Match.Groups[1].Value : pair.Match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var file in files)
        {
            var match = RoleOf(file) == ConversionContentType.XML ? MapperNamespace.Match(file.Content) : Match.Empty;
            if (interfaces is [var only] && match.Success && match.Groups[1].Value != only)
            {
                repairs.Add($"{file.Path}: namespace \"{match.Groups[1].Value}\" set to \"{only}\", the mapper interface of the artifact");
                yield return file with { Content = file.Content[..match.Groups[1].Index] + only + file.Content[(match.Groups[1].Index + match.Groups[1].Length)..] };
            }
            else
            {
                yield return file;
            }
        }
    }
}
