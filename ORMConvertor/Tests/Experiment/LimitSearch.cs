using System.Diagnostics;
using System.Globalization;
using System.Text;
using AbstractWrappers;
using DatabaseCatalog;
using Model;
using OrmConvertor;
using Tests.Database;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// One point of a series as the walk recorded it (decision 182): the row of the path
/// (<see cref="ForeignVerdict"/>, the columns both suites write) and, beside it, what the walk
/// alone measures - the time and the memory of the translation, the time the catalog took of
/// it, the size of the input - and the step the point is in the series: passed, failed, or in
/// progress while the walk is on it, which a crash leaves behind.
/// </summary>
internal sealed class SeriesPoint
{
    public const string Header = ForeignVerdict.Header + "\taxis\tpoint\tcatalog\tstep\ttranslateMs\tallocatedKb\tcatalogMs\tinputBytes";

    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Crash = "crash";
    public const string InProgress = "in progress";

    /// <summary>The outcome of a point whose translation the tool made and nobody judged here: a Java target, whose levels the Java suite writes over the same input.</summary>
    public const string Translated = "translated";
    public const string Threw = "threw";
    public const string TooSlow = "too slow";

    public required ForeignVerdict Verdict { get; set; }
    public required string Axis { get; init; }
    public required int Point { get; init; }
    public required string Catalog { get; init; }
    public string Step { get; set; } = InProgress;
    public double TranslateMs { get; set; }
    public long AllocatedKb { get; set; }
    public double CatalogMs { get; set; }
    public long InputBytes { get; set; }

    /// <summary>The conversion of the point while the walk is on it; never written.</summary>
    public ConversionResult? Conversion { get; set; }

    public string Id => Verdict.Artifact;

    public string Row()
        => string.Join('\t',
            Verdict.Row(),
            Axis,
            Point.ToString(CultureInfo.InvariantCulture),
            Catalog,
            Step,
            TranslateMs.ToString("F0", CultureInfo.InvariantCulture),
            AllocatedKb.ToString(CultureInfo.InvariantCulture),
            CatalogMs.ToString("F0", CultureInfo.InvariantCulture),
            InputBytes.ToString(CultureInfo.InvariantCulture));

    public string Describe()
        => $"{Axis} {Point} {Verdict.Source} -> {Verdict.Target}: {Step}, {Verdict.Outcome} at level {Verdict.Reached}, {TranslateMs:F0} ms, {AllocatedKb / 1024.0:F1} MB"
           + (Verdict.Detail.Length > 0 ? ": " + Verdict.Detail : string.Empty);

    /// <summary>The points of a file, by the columns of its header; a file written by this class reads back whole.</summary>
    public static List<SeriesPoint> Read(string file)
    {
        if (!File.Exists(file))
        {
            return [];
        }

        var rows = SummaryRow.Read(file);
        var points = new List<SeriesPoint>(rows.Count);
        foreach (var row in rows)
        {
            var verdict = new ForeignVerdict
            {
                Artifact = row["artifact"],
                Answers = row["answers"],
                Source = row["source"],
                Target = row["target"],
                Producer = row["producer"],
                Language = row["language"],
                Metadata = row["metadata"],
                Reached = row.Reached,
                Outcome = row.Outcome,
                Detail = row["detail"],
                Rows = int.TryParse(row["rows"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : null,
                AgreesWithMeasure = row["agrees"] switch { "1" => true, "0" => false, _ => null },
                ReadMs = Number(row["readMs"]),
                CompileMs = Number(row["compileMs"]),
                AcceptMs = Number(row["acceptMs"]),
                RunMs = Number(row["runMs"]),
            };

            points.Add(new SeriesPoint
            {
                Verdict = verdict,
                Axis = row["axis"],
                Point = (int)Number(row["point"]),
                Catalog = row["catalog"],
                Step = row["step"],
                TranslateMs = Number(row["translateMs"]),
                AllocatedKb = (long)Number(row["allocatedKb"]),
                CatalogMs = Number(row["catalogMs"]),
                InputBytes = (long)Number(row["inputBytes"]),
            });
        }

        return points;
    }

    /// <summary>
    /// Writes the points whole, through a temporary file, so that a process that dies while
    /// writing leaves the previous file and not half of a new one. The parent reads the file
    /// while the child writes it (the bound on a point), and on Windows the replace fails while
    /// a reader has the file open, whatever the reader shares; the replace is tried again.
    /// </summary>
    public static void Write(string file, IEnumerable<SeriesPoint> points)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);

        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var point in points.OrderBy(point => point.Axis, StringComparer.Ordinal).ThenBy(point => point.Verdict.Source, StringComparer.Ordinal).ThenBy(point => point.Verdict.Target, StringComparer.Ordinal).ThenBy(point => point.Point))
        {
            text.Append(point.Row()).Append('\n');
        }

        var temporary = file + ".tmp";
        File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, file, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < ReplaceAttempts)
            {
                Thread.Sleep(ReplacePause);
            }
        }
    }

    private const int ReplaceAttempts = 50;
    private static readonly TimeSpan ReplacePause = TimeSpan.FromMilliseconds(100);

    private static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
}

/// <summary>
/// The search for the tool's limits (decision 182): a walk up every series of
/// <see cref="LimitSeries"/>, one source at a time, every target in turn, point after point
/// until the first point that fails - the tool refused it, threw, took longer than the bound
/// of S3, or the target framework no longer accepted the artifact - or until the ceiling of the
/// series. Every point is a row with the levels of the path and the time and the memory of the
/// translation; the last point that passed is the limit found, the first that failed says why.
///
/// A walk runs in a process of its own, one per axis and source, because the uncapped series
/// end where a parser overflows the stack, which no .NET process survives (decision 092): the
/// parent test starts the child, reads what it left when it died, records the crash as the
/// outcome of the point it was on, and starts it again for the rest. The child skips every
/// point already in the file, so a walk stopped from outside continues where it was.
/// </summary>
internal static class LimitSearch
{
    public const string FileName = "series-points.tsv";

    /// <summary>The axis and the source a child process walks, as <c>axis|Source</c>; the child skips without it.</summary>
    public const string WalkVariable = "ORMCONVERTOR_SERIES_WALK";

    /// <summary>The file of points a child writes.</summary>
    public const string FileVariable = "ORMCONVERTOR_SERIES_FILE";

    /// <summary>The directory of inputs a child exports the points it reaches into, as the inputs export of decision 172 writes them; nothing is exported without it.</summary>
    public const string InputsVariable = "ORMCONVERTOR_SERIES_INPUTS";

    /// <summary>The catalog of a walk: <c>fixture</c> (the default) reads the fixture's schema, <c>none</c> translates dry, for the two measurements S3 separates.</summary>
    public const string CatalogVariable = "ORMCONVERTOR_SERIES_CATALOG";

    /// <summary>The axes a run walks, comma-separated; every axis without it.</summary>
    public const string AxesVariable = "ORMCONVERTOR_SERIES_AXES";

    public const string CatalogFixture = "fixture";
    public const string CatalogNone = "none";

    /// <summary>The bound of S3 on one translation; a point the tool needs longer for has failed, whatever the artifact.</summary>
    public static readonly TimeSpan TranslationBound = TimeSpan.FromSeconds(30);

    /// <summary>How much longer than the bound a translation is waited for before it is cut, so that one which returns just past the bound is measured rather than cut.</summary>
    private static readonly TimeSpan CutMargin = TimeSpan.FromSeconds(5);

    /// <summary>How long one point may stay in progress before the parent kills the child and records the point as hung (decision 190).</summary>
    public static readonly TimeSpan PointBound = TimeSpan.FromMinutes(30);

    /// <summary>How often the parent reads the file of a running child for the point it is on.</summary>
    private static readonly TimeSpan Watch = TimeSpan.FromSeconds(30);

    public const string ChildTestClass = "Tests.Experiment.LimitSeriesChildTest";

    public static string Catalog
        => Environment.GetEnvironmentVariable(CatalogVariable) is { } catalog && catalog.Trim().Equals(CatalogNone, StringComparison.OrdinalIgnoreCase) ? CatalogNone : CatalogFixture;

    public static IReadOnlyList<LimitAxis> Axes
    {
        get
        {
            var asked = Environment.GetEnvironmentVariable(AxesVariable);
            if (string.IsNullOrWhiteSpace(asked))
            {
                return LimitSeries.Axes;
            }

            var ids = asked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = ids.Where(id => LimitSeries.Axis(id) is null).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException($"{AxesVariable} names {string.Join(", ", unknown)}, which no series has; the axes are {string.Join(", ", LimitSeries.Axes.Select(axis => axis.Id))}.");
            }

            return LimitSeries.Axes.Where(axis => ids.Contains(axis.Id, StringComparer.Ordinal)).ToList();
        }
    }

    /// <summary>
    /// The row of a point: the input's directory and the target, as the Java suite names its row
    /// over the same input, and the catalog of the walk, so that the fixture walk and the dry walk
    /// of one run are two points and not one the second walk would skip.
    /// </summary>
    public static string PointId(LimitAxis axis, int point, ORMEnum source, ORMEnum target, string catalog)
        => $"{ForeignKey.Series}-{axis.Id}-{point.ToString(CultureInfo.InvariantCulture)}-{source}-{target}-{catalog}";

    // ---- the child: the walk itself ------------------------------------------------------

    /// <summary>
    /// Walks the series of one axis from one source into the targets given, writing the file
    /// after every change. The path judges a direction up to its first failure; from there,
    /// and past the ceiling of the series, the translation alone goes on until it fails itself
    /// (decision 190), so that the translator's limit is not hidden behind the path's. A point
    /// already in the file is not walked again: passed, the walk goes on above it; failed on
    /// the path, the translation alone goes on; failed in the translation, the walk of that
    /// target is over. A translation that has not returned by the bound of S3 ends the process,
    /// so a walk runs in a child of <see cref="Drive"/>.
    /// </summary>
    public static void Walk(LimitAxis axis, ORMEnum source, string file, string? inputs, string catalog, IEnumerable<ORMEnum>? targets = null, Action<string>? log = null)
    {
        if (!axis.Sources.Contains(source))
        {
            throw new InvalidOperationException($"{source} does not state the series {axis.Id}; its sources are {string.Join(", ", axis.Sources)}.");
        }

        var points = SeriesPoint.Read(file).ToDictionary(point => point.Id, StringComparer.Ordinal);
        void Save() => SeriesPoint.Write(file, points.Values);

        using var reader = catalog == CatalogFixture && TestDatabase.ConnectionString is { } connectionString
            ? new CachingCatalogReader(new SqlServerCatalogReader(connectionString))
            : null;
        var limits = axis.Uncapped ? new ParseLimits { MaxNestingDepth = ParseLimits.Unlimited } : ParseLimits.Default;

        foreach (var target in targets ?? Enum.GetValues<ORMEnum>())
        {
            // A Java target is judged by the Java suite, never here.
            var judged = ForeignArtifactRun.Owns(target);

            foreach (var point in axis.Walked)
            {
                var beyond = axis.IsBeyond(point);
                var id = PointId(axis, point, source, target, catalog);
                if (points.TryGetValue(id, out var known))
                {
                    if (known.Step == SeriesPoint.Pass)
                    {
                        continue;
                    }

                    if (FailedInTranslation(known))
                    {
                        break;
                    }

                    judged = false;
                    continue;
                }

                var key = axis.Key(point);
                var units = axis.Units(source, point);

                // A point past the ceiling is the translator's question alone: the experiment run
                // and the Java half judge only what the series holds.
                var exported = inputs is not null && !beyond;
                if (exported && !Directory.Exists(ForeignInputs.DirectoryOf(inputs!, key, source)))
                {
                    ForeignInputs.WriteOne(inputs!, key, source, units, questions: null);
                }

                var row = new SeriesPoint
                {
                    Verdict = new ForeignVerdict
                    {
                        Artifact = id,
                        Answers = key.ToString(),
                        Source = source.ToString(),
                        Target = target.ToString(),
                        Producer = ExperimentRun.Producer,
                        Language = ForeignArtifact.LanguageNone,
                        Metadata = NoMetadata,
                    },
                    Axis = axis.Id,
                    Point = point,
                    Catalog = catalog,
                    InputBytes = units.Sum(unit => (long)Encoding.UTF8.GetByteCount(unit.Content)),
                };
                points[id] = row;
                Save();

                if (!Translate(row, source, target, units, reader, limits))
                {
                    // The translation is still running on its thread and nothing in .NET stops
                    // it; the row says why, and the process ends so that the parent can start
                    // the next target (decision 190).
                    Save();
                    log?.Invoke(row.Describe());
                    Process.GetCurrentProcess().Kill();
                }

                if (row.Step == SeriesPoint.InProgress)
                {
                    var artifact = ExperimentRun.ArtifactOf(row.Conversion!, id, exported ? ForeignInputs.DirectoryOf(inputs!, key, source) : string.Empty, key, source, target);
                    row.Verdict = Replace(row.Verdict, artifact.Language, row.Verdict.Metadata, row.Verdict.Outcome, row.Verdict.Detail);

                    // In the file before the path starts: the language says the translation
                    // returned, so a process that dies from here on died on the path.
                    Save();

                    if (judged && !beyond)
                    {
                        // The verdict the path mutates is the row's own, so that every level it
                        // reaches is in the file before the next one starts.
                        ForeignArtifactRun.Run(artifact, verdict =>
                        {
                            row.Verdict = verdict;
                            Save();
                        });

                        if (row.Verdict.Reached >= ForeignLevel.Accepted)
                        {
                            row.Step = SeriesPoint.Pass;
                        }
                        else
                        {
                            row.Step = SeriesPoint.Fail;
                            judged = false;
                        }
                    }
                    else
                    {
                        row.Verdict.Outcome = SeriesPoint.Translated;
                        row.Verdict.Detail = !ForeignArtifactRun.Owns(target) && !beyond
                            ? "the levels of a Java target are the Java suite's, over the exported input"
                            : beyond
                                ? "past the ceiling of the series, translated alone"
                                : "the path stopped at a smaller point, translated alone";
                        row.Step = SeriesPoint.Pass;
                    }
                }

                row.Conversion = null;
                Save();
                log?.Invoke(row.Describe());

                if (row.Step != SeriesPoint.Pass && FailedInTranslation(row))
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Whether a point that did not pass failed in the translation - refused, threw, too slow,
    /// or the process died before the translation returned - rather than on the path. A
    /// translation that returned wrote the versions into the metadata of the row, which is in
    /// the file before the path starts, so a row still without them never got as far as the path.
    /// </summary>
    public static bool FailedInTranslation(SeriesPoint point)
        => point.Step != SeriesPoint.Pass
           && (point.Verdict.Outcome is ForeignVerdict.Refused or SeriesPoint.Threw or SeriesPoint.TooSlow
               || point.Verdict.Metadata == NoMetadata);

    /// <summary>The metadata of a row whose translation has not returned.</summary>
    private const string NoMetadata = "{}";

    /// <summary>
    /// The translation of one point, timed and measured: the time of the call, the bytes the
    /// process allocated during it, the time the catalog took of it. A Failure record is the
    /// row <c>refused</c> (decision 173), an exception <c>threw</c>, a translation over the bound
    /// of S3 <c>too slow</c>; each of them ends the series. Otherwise the row stays in progress
    /// for the levels of the path.
    ///
    /// The call runs on a thread of the pool, where a deployed instance translates a request,
    /// and is waited for no longer than the bound of S3: past it the outcome is <c>too slow</c>
    /// whenever the call would return (decision 190). False when it has not returned; the row
    /// then says so and the caller ends the process, which is the only way to stop the call.
    /// </summary>
    private static bool Translate(SeriesPoint row, ORMEnum source, ORMEnum target, List<ConversionSource> units, ICatalogReader? reader, ParseLimits limits)
    {
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        ConversionResult? conversion = null;
        string? threw = null;
        var call = Task.Run(() =>
        {
            try
            {
                conversion = ConversionHandler.Convert(source, target, units, reader, declaredSourceDialect: null, limits);
            }
            catch (Exception exception)
            {
                threw = exception.GetBaseException().GetType().Name + ": " + exception.GetBaseException().Message;
            }
        });

        if (!call.Wait(TranslationBound + CutMargin))
        {
            row.TranslateMs = clock.Elapsed.TotalMilliseconds;
            row.AllocatedKb = Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / 1024;
            row.Verdict = Replace(row.Verdict, row.Verdict.Language, NoMetadata, SeriesPoint.TooSlow,
                $"the translation had not returned after {clock.Elapsed.TotalSeconds:F0} s; S3 allows {TranslationBound.TotalSeconds:F0} s, and the process was ended to stop it");
            row.Step = SeriesPoint.Fail;
            return false;
        }

        clock.Stop();
        row.TranslateMs = clock.Elapsed.TotalMilliseconds;
        row.AllocatedKb = Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / 1024;
        row.CatalogMs = conversion?.CatalogReadTime?.TotalMilliseconds ?? 0;
        var metadata = ExperimentRun.Metadata(conversion);

        if (threw is not null)
        {
            row.Verdict = Replace(row.Verdict, ForeignArtifact.LanguageRefused, metadata, SeriesPoint.Threw, threw);
            row.Step = SeriesPoint.Fail;
            return true;
        }

        var failures = ExperimentRun.Failures(conversion!);
        if (failures.Count > 0)
        {
            row.Verdict = Replace(row.Verdict, ForeignArtifact.LanguageRefused, metadata, ForeignVerdict.Refused, string.Join("; ", failures));
            row.Step = SeriesPoint.Fail;
            return true;
        }

        if (clock.Elapsed > TranslationBound)
        {
            row.Verdict = Replace(row.Verdict, row.Verdict.Language, metadata, SeriesPoint.TooSlow, $"the translation took {clock.Elapsed.TotalSeconds:F1} s; S3 allows {TranslationBound.TotalSeconds:F0} s");
            row.Step = SeriesPoint.Fail;
            return true;
        }

        row.Verdict = Replace(row.Verdict, row.Verdict.Language, metadata, row.Verdict.Outcome, row.Verdict.Detail);
        row.Conversion = conversion;
        return true;
    }

    /// <summary>The same row of the path with its init-only fields - the language and the metadata - and its outcome replaced; the identity of the row stays.</summary>
    private static ForeignVerdict Replace(ForeignVerdict verdict, string language, string metadata, string outcome, string detail) => new()
    {
        Artifact = verdict.Artifact,
        Answers = verdict.Answers,
        Source = verdict.Source,
        Target = verdict.Target,
        Producer = verdict.Producer,
        Language = language,
        Metadata = metadata,
        Reached = verdict.Reached,
        Outcome = outcome,
        Detail = detail,
    };

    // ---- the parent: one child process per axis and source -------------------------------

    /// <summary>
    /// Drives the walk of one axis from one source in child processes until it is complete:
    /// a child that exits cleanly has walked every target to its first failure or its ceiling;
    /// a child that dies leaves a point in progress, which becomes the crash of that point, and
    /// the next child goes on above it. A point that stays in progress longer than
    /// <see cref="PointBound"/> is hung: the parent kills the child and the next one goes on
    /// above it. Returns the points of the walk.
    /// </summary>
    public static List<SeriesPoint> Drive(LimitAxis axis, ORMEnum source, string results, string inputs, string catalog, Action<string> log)
    {
        var file = Path.Combine(results, FileName);
        var assembly = typeof(LimitSearch).Assembly.Location;
        if (string.IsNullOrEmpty(assembly) || !File.Exists(assembly))
        {
            throw new InvalidOperationException("The test assembly has no location on disk, so no child process can run the walk.");
        }

        for (var attempt = 1; ; attempt++)
        {
            var before = Snapshot(file, axis, source, catalog);

            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(assembly)!,
            };
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("-class");
            start.ArgumentList.Add(ChildTestClass);

            // The child is a run of its own, not of the experiment: the variables that would make
            // the data of other theories reach for a run directory are cleared, and the walk gets
            // its own.
            foreach (var variable in new[] { ExperimentRun.ResultsVariable, ExperimentRun.InputsVariable, ForeignArtifact.ArtifactsVariable, ForeignArtifact.ResultsVariable, ForeignInputs.Variable, "ORMCONVERTOR_FIBEN_RESULTS" })
            {
                start.Environment.Remove(variable);
            }

            start.Environment[WalkVariable] = $"{axis.Id}|{source}";
            start.Environment[FileVariable] = file;
            start.Environment[InputsVariable] = inputs;
            start.Environment[CatalogVariable] = catalog;

            var output = new StringBuilder();
            int exitCode;
            var hung = false;
            using (var child = Process.Start(start) ?? throw new InvalidOperationException("The child process did not start."))
            {
                child.OutputDataReceived += (_, line) => { if (line.Data is not null) { lock (output) { output.AppendLine(line.Data); } } };
                child.ErrorDataReceived += (_, line) => { if (line.Data is not null) { lock (output) { output.AppendLine(line.Data); } } };
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();

                // The bound is on a point, not on the child: a child walks every target of its
                // source, and a walk of many slow points would otherwise end on whichever point
                // it was on when the bound came. The file says which point is in progress, and
                // the clock starts again whenever that changes; a child that never starts a
                // point is held to the same bound.
                string? current = null;
                var since = Stopwatch.StartNew();
                while (!child.WaitForExit(Watch))
                {
                    if (!TryInProgress(file, axis, source, catalog, out var inProgress))
                    {
                        continue;
                    }

                    if (inProgress != current)
                    {
                        current = inProgress;
                        since.Restart();
                    }
                    else if (since.Elapsed > PointBound)
                    {
                        hung = true;
                        child.Kill(entireProcessTree: true);
                        break;
                    }
                }

                child.WaitForExit();
                exitCode = child.ExitCode;
            }

            var points = SeriesPoint.Read(file);
            var crashed = OfWalk(points, axis, source, catalog).Where(point => point.Step == SeriesPoint.InProgress).ToList();
            var overflow = OverflowFrame(output.ToString());
            foreach (var point in crashed)
            {
                point.Step = SeriesPoint.Crash;
                point.Verdict.Outcome = hung ? "hung" : "crashed";
                point.Verdict.Detail = hung
                    ? $"the point was in progress for more than {PointBound.TotalMinutes:F0} min and the process was killed at level {point.Verdict.Reached}"
                    : $"the process died with exit code {exitCode} (0x{exitCode:X8}) at level {point.Verdict.Reached}"
                      + (overflow is null ? string.Empty : $", the stack overflowing in {overflow}");
                log($"{point.Describe()}");
            }

            if (crashed.Count > 0)
            {
                SeriesPoint.Write(file, points);
            }

            if (exitCode == 0 && !hung)
            {
                return OfWalk(points, axis, source, catalog).ToList();
            }

            if (Snapshot(file, axis, source, catalog) == before)
            {
                var tail = output.ToString();
                throw new InvalidOperationException(
                    $"The walk of {axis.Id} from {source} made no progress in attempt {attempt} (exit code {exitCode}); the child said:\n{tail[Math.Max(0, tail.Length - 4000)..]}");
            }
        }
    }

    /// <summary>
    /// The method whose recursion overflowed the stack, from what the runtime prints when it
    /// ends a process for it: <c>Stack overflow.</c>, then the repeated frames, the deepest
    /// first. Null where the process died otherwise - the kernel's kill for memory says nothing.
    /// </summary>
    public static string? OverflowFrame(string output)
    {
        var at = output.LastIndexOf("Stack overflow.", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        foreach (var line in output[at..].Split('\n'))
        {
            var frame = line.Trim();
            if (frame.StartsWith("at ", StringComparison.Ordinal))
            {
                var call = frame[3..];
                var parameters = call.IndexOf('(');
                return parameters > 0 ? call[..parameters] : call;
            }
        }

        return null;
    }

    /// <summary>The points of one walk in the file of a run: an axis, a source and a catalog.</summary>
    private static IEnumerable<SeriesPoint> OfWalk(IEnumerable<SeriesPoint> points, LimitAxis axis, ORMEnum source, string catalog)
        => points.Where(point => point.Axis == axis.Id && point.Verdict.Source == source.ToString() && point.Catalog == catalog);

    /// <summary>The point of the walk a running child is on, or none between two points; false when the read met the child replacing the file, and the next look will see it.</summary>
    private static bool TryInProgress(string file, LimitAxis axis, ORMEnum source, string catalog, out string? inProgress)
    {
        try
        {
            inProgress = OfWalk(SeriesPoint.Read(file), axis, source, catalog).FirstOrDefault(point => point.Step == SeriesPoint.InProgress)?.Id;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            inProgress = null;
            return false;
        }
    }

    private static string Snapshot(string file, LimitAxis axis, ORMEnum source, string catalog)
        => string.Join("\n", OfWalk(SeriesPoint.Read(file), axis, source, catalog)
            .Select(point => point.Id + ":" + point.Step));
}
