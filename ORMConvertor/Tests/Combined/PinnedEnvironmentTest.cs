using System.Text.RegularExpressions;

namespace Tests.Combined;

/// <summary>
/// The environment is pinned on content, not on a tag (decision 186): every base image by
/// digest, every action of the workflow by commit, and the SQL Server image by one reference
/// in the four files that name it. Dependabot moves the Dockerfiles, the compose file and the
/// actions by pull request but not the service containers of the workflow, so this is what
/// holds the four together - and what notices a new FROM, image or action written with a
/// bare tag. The files are read as text, from the checkout on the host and from the copy the
/// tests stage was built from in the container. That copy is the build context, the solution
/// directory, so the workflow is not in it: there its checks hold only that the run is the
/// container's (the official images set DOTNET_RUNNING_IN_CONTAINER), and the host run and
/// CI, which have the workflow, make the check itself. No skip, so the skip count of a run
/// stays what the decisions say it is.
/// </summary>
public class PinnedEnvironmentTest
{
    private static readonly string Solution = TargetFrameworkDescriptorTest.SolutionDirectory();

    private static readonly string[] Dockerfiles =
    [
        Path.Combine(Solution, "ORMConvertorAPI", "Dockerfile"),
        Path.Combine(Solution, "database.Dockerfile"),
    ];

    private static readonly string Compose = Path.Combine(Solution, "docker-compose.yml");

    private static readonly string Workflow =
        Path.GetFullPath(Path.Combine(Solution, "..", ".github", "workflows", "ormconvertor-tests.yml"));

    [Fact]
    public void EveryBaseImageOfTheDockerfilesIsPinnedByDigest()
    {
        foreach (var file in Dockerfiles)
        {
            // A FROM naming a stage of the same file is built here and has no digest to carry.
            var stages = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(file))
            {
                var match = Regex.Match(line, @"^FROM\s+(?<image>\S+)(?:\s+AS\s+(?<stage>\S+))?", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                var image = match.Groups["image"].Value;
                if (match.Groups["stage"].Success)
                {
                    stages.Add(match.Groups["stage"].Value);
                }

                if (!stages.Contains(image))
                {
                    Assert.True(IsPinnedByDigest(image), $"{Path.GetFileName(file)}: `{image}` is not pinned by digest");
                }
            }
        }
    }

    [Fact]
    public void EveryImageOfTheComposeFileIsPinnedByDigest()
    {
        var images = ImagesOf(Compose).ToList();
        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.True(IsPinnedByDigest(image), $"docker-compose.yml: `{image}` is not pinned by digest"));
    }

    [Fact]
    public void EveryServiceContainerOfTheWorkflowIsPinnedByDigest()
    {
        if (!WorkflowIsHere())
        {
            return;
        }

        var images = ImagesOf(Workflow).ToList();
        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.True(IsPinnedByDigest(image), $"workflow: `{image}` is not pinned by digest"));
    }

    [Fact]
    public void EveryActionOfTheWorkflowIsPinnedByCommit()
    {
        if (!WorkflowIsHere())
        {
            return;
        }

        var actions = File.ReadLines(Workflow)
            .Select(line => Regex.Match(line, @"^\s*-?\s*uses:\s*(?<action>\S+)"))
            .Where(match => match.Success)
            .Select(match => match.Groups["action"].Value)
            .ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.Matches(@"^[^@\s]+@[0-9a-f]{40}$", action));
    }

    [Fact]
    public void TheSqlServerImageIsOneReferenceEverywhere()
    {
        var files = Dockerfiles.Append(Compose).ToList();
        if (WorkflowIsHere())
        {
            files.Add(Workflow);
        }

        var references = files
            .SelectMany(file => File.ReadLines(file)
                .Select(line => Regex.Match(line, @"mcr\.microsoft\.com/mssql/server\S*"))
                .Where(match => match.Success)
                .Select(match => match.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(references.Count == 1, "the SQL Server image is referenced as: " + string.Join(", ", references));
        Assert.True(IsPinnedByDigest(references[0]), $"`{references[0]}` is not pinned by digest");
    }

    /// <summary>
    /// True where the workflow can be read. Where it cannot, the run has to be the container's,
    /// whose build context is the solution directory - anywhere else a missing workflow is a
    /// missing file, not a narrower check.
    /// </summary>
    private static bool WorkflowIsHere()
    {
        if (File.Exists(Workflow))
        {
            return true;
        }

        Assert.Equal("true", Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"));
        return false;
    }

    private static bool IsPinnedByDigest(string image) => Regex.IsMatch(image, @"@sha256:[0-9a-f]{64}$");

    private static IEnumerable<string> ImagesOf(string yaml) => File.ReadLines(yaml)
        .Select(line => Regex.Match(line, @"^\s*image:\s*(?<image>\S+)"))
        .Where(match => match.Success)
        .Select(match => match.Groups["image"].Value);
}
