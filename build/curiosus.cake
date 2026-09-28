///////////////////////////////////////////////////////////////////////////////
// CURIOSUS BUILD SCRIPT
//
// Synced from https://github.com/curiosus-dev/dotnet-tools, do not edit in place.
// The repository build.cake loads it, adds repository-specific tasks and calls RunTarget(target):
//
//     #load "build/curiosus.cake"
//     RunTarget(target);
//
// Conventions: a single *.sln/*.slnx in the root, packages in ./src, tests in ./tests
// (integration tests under tests/IntegrationTests or in *.IntegrationTests projects),
// release notes in CHANGELOG.md of a project or of the repository.
//
// Tasks:
//   Default          - Build + Test
//   Clean            - cleans the solution and ./artifacts
//   Build            - Clean + builds the solution
//   Test             - UnitTests + IntegrationTests
//   UnitTests        - runs test projects except integration ones
//   IntegrationTests - runs integration test projects
//   CoverageReport   - Default with coverage + report of ./src assemblies in ./artifacts/coverage-report
//                      (HTML, text, GitHub Markdown and JSON summaries) and shields.io endpoint badges in
//                      ./artifacts/coverage-report/badges: coverage.json and <PackageId>.json per package
//   Pack             - packs every ./src/**/*.csproj into ./artifacts/packages
//   NuGetPush        - pushes packages to nuget.org, already published versions are skipped;
//                      API key is taken from NUGET_API_KEY (short-lived key from NuGet Trusted Publishing on CI)
//   GitHubReleases   - creates a tag and a GitHub release for every package version without one
//   Publish          - NuGetPush + GitHubReleases
//   ReleaseCheck     - lists packages changed since --since: released by the merge (version not on nuget.org yet),
//                      with a changelog entry to release later, or without one (a warning); never fails.
//                      Writes ./artifacts/release-check.md (the pull request comment) and changed=true|false
//                      to GITHUB_OUTPUT
//
// Arguments:
//   --target=Default
//   --configuration=Release
//   --framework=net10.0       limits build and tests to a single target framework
//   --coverage=true           collects Cobertura coverage into ./artifacts/coverage with Microsoft Code Coverage
//                             (comes with Microsoft.NET.Test.Sdk, test projects need no extra packages)
//   --minCoverage=70          CoverageReport fails below this line coverage, %; the repository default is set
//                             in build.cake after #load: `minLineCoverage = 70;` (0 - no check)
//   --releaseTagFormat=auto   auto: "v{version}" for single-package repositories, "{id}.v{version}" otherwise
//   --githubReleaseDryRun     writes release notes to ./artifacts/release-notes without creating releases
//   --since=origin/HEAD       ReleaseCheck compares HEAD with this commit (HEAD^1 for a pull request merge commit)
//
// Package versions come from CHANGELOG.md, see build/Curiosus.props.
///////////////////////////////////////////////////////////////////////////////

using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

var target = Argument<string>("target", "Default");
var configuration = Argument<string>("configuration", "Release");
var framework = Argument<string>("framework", "");
var collectCoverage = Argument<bool>("coverage", false) || target == "CoverageReport";
var releaseTagFormat = Argument<string>("releaseTagFormat", "auto");
var githubReleaseDryRun = HasArgument("githubReleaseDryRun");
var releaseCheckSince = Argument<string>("since", "origin/HEAD");

// Set by the repository build.cake, read when CoverageReport runs, so --minCoverage still overrides it.
var minLineCoverage = 0d;

var artifactsDir = MakeAbsolute(Directory("./artifacts"));
var packagesDir = artifactsDir.Combine("packages");
var coverageDir = artifactsDir.Combine("coverage");
var nugetSource = "https://api.nuget.org/v3/index.json";
var solutionPath = FindSolution();

///////////////////////////////////////////////////////////////////////////////
// BUILD & TEST
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
    {
        DotNetClean(solutionPath.FullPath, new DotNetCleanSettings { Configuration = configuration });
        CleanDirectory(artifactsDir);
    });

Task("Build")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        var settings = new DotNetBuildSettings { Configuration = configuration };
        if (!String.IsNullOrEmpty(framework))
            settings.Framework = framework;

        DotNetBuild(solutionPath.FullPath, settings);
    });

Task("UnitTests")
    .Does(() => RunTests(GetTestProjects().Where(x => !IsIntegrationTestProject(x))));

Task("IntegrationTests")
    .Does(() => RunTests(GetTestProjects().Where(IsIntegrationTestProject)));

Task("Test")
    .IsDependentOn("UnitTests")
    .IsDependentOn("IntegrationTests");

Task("Default")
    .IsDependentOn("Build")
    .IsDependentOn("Test");

Task("CoverageReport")
    .IsDependentOn("Default")
    .Does(() =>
    {
        var reports = GetFiles($"{coverageDir}/**/*.cobertura.xml");
        if (reports.Count == 0)
            throw new CakeException(
                $"No coverage reports found in {coverageDir}. Do test projects reference Microsoft.NET.Test.Sdk?");

        // Only the libraries count: test assemblies and third-party assemblies with symbols are left out.
        var assemblyFilters = GetFiles("./src/**/*.csproj").Select(x => $"+{ReadAssemblyName(x)}");

        var exitCode = StartProcess("dotnet", new ProcessSettings
        {
            Arguments = new ProcessArgumentBuilder()
                .Append("tool").Append("run").Append("reportgenerator")
                .AppendQuoted($"-reports:{String.Join(";", reports.Select(x => x.FullPath))}")
                .AppendQuoted($"-targetdir:{artifactsDir.Combine("coverage-report")}")
                .AppendQuoted("-reporttypes:Html;TextSummary;MarkdownSummaryGithub;JsonSummary")
                .AppendQuoted($"-assemblyfilters:{String.Join(";", assemblyFilters)}")
        });
        if (exitCode != 0)
            throw new CakeException($"reportgenerator failed (exit code {exitCode}).");

        var summaryPath = artifactsDir.Combine("coverage-report").CombineWithFilePath("Summary.json").FullPath;
        using var summary = JsonDocument.Parse(System.IO.File.ReadAllText(summaryPath));
        var lineCoverage = summary.RootElement.GetProperty("summary").TryGetProperty("linecoverage", out var value)
            && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : 0;

        var threshold = Argument<double>("minCoverage", minLineCoverage);
        if (lineCoverage < threshold)
            throw new CakeException(
                FormattableString.Invariant($"Line coverage {lineCoverage}% is below the minimum of {threshold}%."));

        Information(FormattableString.Invariant($"Line coverage: {lineCoverage}% (minimum {threshold}%)."));

        WriteCoverageBadges(summary.RootElement, lineCoverage);
    });

///////////////////////////////////////////////////////////////////////////////
// PACK & PUBLISH
///////////////////////////////////////////////////////////////////////////////

Task("Pack")
    .Does(() =>
    {
        CleanDirectory(packagesDir);

        foreach (var project in GetFiles("./src/**/*.csproj"))
        {
            Information($"Packing \"{project.GetFilename()}\"...");
            DotNetPack(project.FullPath, new DotNetPackSettings
            {
                Configuration = configuration,
                OutputDirectory = packagesDir
            });
        }
    });

Task("NuGetPush")
    .Does(() =>
    {
        var apiKey = EnvironmentVariable("NUGET_API_KEY");
        if (String.IsNullOrWhiteSpace(apiKey))
            throw new CakeException("NUGET_API_KEY environment variable is not set.");

        foreach (var package in GetPackages())
        {
            Information($"Publishing \"{package.GetFilename()}\"...");

            // Symbol packages (.snupkg) next to the .nupkg are pushed automatically.
            DotNetNuGetPush(package.FullPath, new DotNetNuGetPushSettings
            {
                Source = nugetSource,
                ApiKey = apiKey,
                SkipDuplicate = true
            });
        }
    });

Task("GitHubReleases")
    .Does(() =>
    {
        var repository = EnvironmentVariable("GITHUB_REPOSITORY");
        var commitSha = EnvironmentVariable("GITHUB_SHA");
        if (!githubReleaseDryRun && (String.IsNullOrEmpty(repository) || String.IsNullOrEmpty(commitSha)))
            throw new CakeException("GITHUB_REPOSITORY or GITHUB_SHA is not set. Pass --githubReleaseDryRun to run locally.");

        var releaseNotesDir = artifactsDir.Combine("release-notes");
        EnsureDirectoryExists(releaseNotesDir);

        var projectDirs = GetProjectDirectoriesByPackageId();
        var isSinglePackageRepository = projectDirs.Count == 1;
        var tagFormat = releaseTagFormat == "auto"
            ? isSinglePackageRepository ? "v{version}" : "{id}.v{version}"
            : releaseTagFormat;

        foreach (var package in GetPackages())
        {
            var (packageId, version) = ReadPackageIdentity(package);
            var tag = tagFormat.Replace("{id}", packageId).Replace("{version}", version);

            if (!githubReleaseDryRun && GitHubReleaseExists(tag))
            {
                Verbose($"Release \"{tag}\" already exists.");
                continue;
            }

            if (!projectDirs.TryGetValue(packageId, out var projectDir))
                throw new CakeException($"Project for package \"{packageId}\" is not found in ./src.");

            var notesFile = releaseNotesDir.CombineWithFilePath($"{tag}.md");
            System.IO.File.WriteAllText(notesFile.FullPath, BuildReleaseNotes(projectDir, packageId, version, repository, tag));

            if (githubReleaseDryRun)
            {
                Information($"[dry run] Release \"{tag}\", notes: {notesFile}");
                continue;
            }

            Information($"Creating release \"{tag}\"...");
            var arguments = new ProcessArgumentBuilder()
                .Append("release").Append("create").AppendQuoted(tag)
                .Append("--target").Append(commitSha)
                .Append("--title").AppendQuoted($"{packageId} v{version}")
                .Append("--notes-file").AppendQuoted(notesFile.FullPath);
            if (!isSinglePackageRepository)
                arguments.Append("--latest=false");
            if (version.Contains('-'))
                arguments.Append("--prerelease");

            var exitCode = StartProcess("gh", new ProcessSettings { Arguments = arguments });
            if (exitCode != 0)
                throw new CakeException($"Failed to create release \"{tag}\" (exit code {exitCode}).");
        }
    });

Task("Publish")
    .IsDependentOn("NuGetPush")
    .IsDependentOn("GitHubReleases");

// A reminder, not a gate: a pull request may change a package without a changelog entry (docs, refactoring).
Task("ReleaseCheck")
    .Does(() =>
    {
        var root = MakeAbsolute(Directory("."));
        var rows = new List<string>();
        var missingEntries = 0;
        using var http = new HttpClient();
        foreach (var (project, packageId) in GetPackableProjects())
        {
            var projectDir = root.GetRelativePath(project.GetDirectory()).FullPath;
            var changedFiles = Git("diff", "--name-only", $"{releaseCheckSince}...HEAD", "--", projectDir).ToList();
            if (changedFiles.Count == 0)
                continue;

            var properties = ReadEvaluatedProperties(project, "PackageVersion", "CuriosusChangelog");
            var version = properties["PackageVersion"];
            var changelog = String.IsNullOrEmpty(properties["CuriosusChangelog"])
                ? null
                : root.GetRelativePath(File(properties["CuriosusChangelog"])).FullPath;
            var changelogChanged = changelog != null
                && Git("diff", "--name-only", $"{releaseCheckSince}...HEAD", "--", changelog).Any();

            var published = IsPublishedOnNuGet(http, packageId, version);
            string status;
            if (published == false)
            {
                status = $"🚀 released as {version}";
            }
            else if (changelogChanged)
            {
                status = published == true
                    ? "📝 changelog entry, released later"
                    : "⚠️ changelog entry, not checked on nuget.org";
            }
            else
            {
                status = "⚠️ no changelog entry";
                missingEntries++;
                var message = $"{packageId} has changes but no CHANGELOG entry. Add one under '## [Unreleased]', "
                    + "or a '## [x.y.z]' section to release; ignore if consumers won't notice the change.";
                Warning(message);
                // On a changed file: the changelog itself is not in the diff, so an annotation there isn't shown.
                // Console, not the Cake log: its ANSI colors before "::" break the workflow command.
                if (EnvironmentVariable("GITHUB_ACTIONS") == "true")
                    Console.WriteLine($"::warning file={changedFiles[0]},title=No changelog entry::{message}");
            }

            rows.Add($"| {packageId} | {version} | {status} |");
        }

        var report = new StringBuilder("<!-- release-check -->\n### Release check\n\n");
        if (rows.Count == 0)
        {
            report.Append("No package changes.\n");
        }
        else
        {
            if (missingEntries > 0)
                report.Append($"⚠️ **{missingEntries} changed package(s) without a CHANGELOG entry.** Add the change under ")
                    .Append("`## [Unreleased]` of the package CHANGELOG, or a `## [x.y.z] - yyyy-mm-dd` section to release it. ")
                    .Append("Tests, CI and internal refactoring need no entry.\n\n");
            report.Append("| Package | Version | On merge |\n| --- | --- | --- |\n").AppendJoin("\n", rows).Append('\n');
        }

        EnsureDirectoryExists(artifactsDir);
        System.IO.File.WriteAllText(artifactsDir.CombineWithFilePath("release-check.md").FullPath, report.ToString());
        Information(report.ToString());

        var githubOutput = EnvironmentVariable("GITHUB_OUTPUT");
        if (!String.IsNullOrEmpty(githubOutput))
            System.IO.File.AppendAllText(githubOutput, $"changed={(rows.Count > 0 ? "true" : "false")}\n");
    });

///////////////////////////////////////////////////////////////////////////////
// HELPERS
///////////////////////////////////////////////////////////////////////////////

FilePath FindSolution()
{
    var solutions = GetFiles("./*.sln").Concat(GetFiles("./*.slnx")).ToList();
    if (solutions.Count != 1)
        throw new CakeException($"Expected a single *.sln or *.slnx in the repository root, found {solutions.Count}.");

    return solutions[0];
}

List<FilePath> GetTestProjects() => GetFiles("./tests/**/*.csproj").OrderBy(x => x.FullPath).ToList();

bool IsIntegrationTestProject(FilePath project) =>
    project.FullPath.Contains("/IntegrationTests/")
    || project.GetFilenameWithoutExtension().ToString().EndsWith("IntegrationTests", StringComparison.OrdinalIgnoreCase);

void RunTests(IEnumerable<FilePath> projects)
{
    var list = projects.ToList();
    if (list.Count == 0)
    {
        Information("No test projects found.");
        return;
    }

    foreach (var project in list)
    {
        Information($"Testing \"{project.GetFilename()}\"...");

        var settings = new DotNetTestSettings { Configuration = configuration };
        if (!String.IsNullOrEmpty(framework))
            settings.Framework = framework;

        if (collectCoverage)
        {
            settings.Collectors = new[] { "Code Coverage;Format=cobertura" };
            settings.ResultsDirectory = coverageDir.Combine(project.GetFilenameWithoutExtension().ToString());
        }

        DotNetTest(project.FullPath, settings);
    }
}

FilePathCollection GetPackages()
{
    var packages = GetFiles($"{packagesDir}/*.nupkg");
    if (packages.Count == 0)
        throw new CakeException($"No packages found in {packagesDir}. Run the Pack task first.");

    return packages;
}

// PackageId may differ from the project name (e.g. Curiosus.Configuration.YAML -> Curiosus.Configuration.YML).
Dictionary<string, DirectoryPath> GetProjectDirectoriesByPackageId() => GetPackableProjects()
    .ToDictionary(x => x.PackageId, x => x.Project.GetDirectory(), StringComparer.OrdinalIgnoreCase);

IEnumerable<(FilePath Project, string PackageId)> GetPackableProjects() => GetFiles("./src/**/*.csproj")
    .Where(x => !String.Equals(ReadProjectProperty(x, "IsPackable"), "false", StringComparison.OrdinalIgnoreCase))
    .Select(x => (x, ReadProjectProperty(x, "PackageId") ?? ReadAssemblyName(x)));

string ReadAssemblyName(FilePath project) =>
    ReadProjectProperty(project, "AssemblyName") ?? project.GetFilenameWithoutExtension().ToString();

string ReadProjectProperty(FilePath project, string name) => XDocument.Load(project.FullPath)
    .Descendants()
    .Where(x => x.Name.LocalName == name)
    .Select(x => x.Value.Trim())
    .FirstOrDefault(x => x.Length > 0);

// Packages whose assemblies were never loaded by tests are missing from the report and get a "no tests" badge.
void WriteCoverageBadges(JsonElement summary, double lineCoverage)
{
    var badgesDir = artifactsDir.Combine("coverage-report").Combine("badges");
    EnsureDirectoryExists(badgesDir);

    var coverageByAssembly = summary.GetProperty("coverage").GetProperty("assemblies").EnumerateArray()
        .Where(x => x.GetProperty("coverage").ValueKind == JsonValueKind.Number)
        .ToDictionary(x => x.GetProperty("name").GetString(), x => x.GetProperty("coverage").GetDouble());

    WriteCoverageBadge(badgesDir.CombineWithFilePath("coverage.json"), lineCoverage);
    foreach (var (project, packageId) in GetPackableProjects())
    {
        var coverage = coverageByAssembly.TryGetValue(ReadAssemblyName(project), out var value) ? value : (double?)null;
        WriteCoverageBadge(badgesDir.CombineWithFilePath($"{packageId}.json"), coverage);
    }
}

void WriteCoverageBadge(FilePath path, double? coverage)
{
    var (message, color) = coverage switch
    {
        null => ("no tests", "lightgrey"),
        >= 80 => (FormattableString.Invariant($"{coverage}%"), "brightgreen"),
        >= 60 => (FormattableString.Invariant($"{coverage}%"), "yellow"),
        >= 40 => (FormattableString.Invariant($"{coverage}%"), "orange"),
        _ => (FormattableString.Invariant($"{coverage}%"), "red")
    };

    var badge = new { schemaVersion = 1, label = "coverage", message, color };
    System.IO.File.WriteAllText(path.FullPath, JsonSerializer.Serialize(badge));
}

(string PackageId, string Version) ReadPackageIdentity(FilePath nupkg)
{
    using var archive = ZipFile.OpenRead(nupkg.FullPath);
    var nuspecEntry = archive.Entries.Single(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    using var stream = nuspecEntry.Open();
    var metadata = XDocument.Load(stream).Descendants().First(x => x.Name.LocalName == "metadata");

    string Get(string name) => metadata.Elements().First(x => x.Name.LocalName == name).Value.Trim();
    return (Get("id"), Get("version"));
}

IEnumerable<string> Git(params string[] arguments)
{
    var builder = new ProcessArgumentBuilder();
    foreach (var argument in arguments)
        builder.AppendQuoted(argument);

    var settings = new ProcessSettings { Arguments = builder, RedirectStandardOutput = true };
    var exitCode = StartProcess("git", settings, out var output);
    if (exitCode != 0)
        throw new CakeException($"git {String.Join(" ", arguments)} failed (exit code {exitCode}).");

    return output.Where(x => x.Length > 0).ToList();
}

Dictionary<string, string> ReadEvaluatedProperties(FilePath project, params string[] names)
{
    var arguments = new ProcessArgumentBuilder().Append("msbuild").AppendQuoted(project.FullPath);
    foreach (var name in names)
        arguments.Append($"-getProperty:{name}");

    var settings = new ProcessSettings { Arguments = arguments, RedirectStandardOutput = true };
    var exitCode = StartProcess("dotnet", settings, out var output);
    if (exitCode != 0)
        throw new CakeException($"Failed to evaluate \"{project.GetFilename()}\" (exit code {exitCode}).");

    // A single property is printed as is, several as {"Properties": {...}}.
    var text = String.Join("\n", output).Trim();
    if (names.Length == 1)
        return new Dictionary<string, string> { [names[0]] = text };

    using var json = JsonDocument.Parse(text);
    return names.ToDictionary(x => x, x => json.RootElement.GetProperty("Properties").GetProperty(x).GetString() ?? "");
}

// null when nuget.org can't be reached.
bool? IsPublishedOnNuGet(HttpClient http, string packageId, string version)
{
    try
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
        using var response = http.GetAsync(url).GetAwaiter().GetResult();
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        return json.RootElement.GetProperty("versions").EnumerateArray()
            .Any(x => String.Equals(x.GetString(), version, StringComparison.OrdinalIgnoreCase));
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        Warning($"Failed to check {packageId} on nuget.org: {exception.Message}");
        return null;
    }
}

bool GitHubReleaseExists(string tag)
{
    var exitCode = StartProcess("gh", new ProcessSettings
    {
        Arguments = new ProcessArgumentBuilder().Append("release").Append("view").AppendQuoted(tag),
        RedirectStandardOutput = true,
        RedirectStandardError = true
    });

    return exitCode == 0;
}

// Notes come from the "## [<version>]" section of the project CHANGELOG.md, falling back to the repository one.
string BuildReleaseNotes(DirectoryPath projectDir, string packageId, string version, string repository, string tag)
{
    var changelogPath = new[]
        {
            projectDir.CombineWithFilePath("CHANGELOG.md"),
            MakeAbsolute(File("./CHANGELOG.md"))
        }
        .FirstOrDefault(x => FileExists(x));

    var section = new List<string>();
    if (changelogPath != null)
    {
        var inSection = false;
        foreach (var line in System.IO.File.ReadAllLines(changelogPath.FullPath))
        {
            if (line.StartsWith("## ["))
            {
                if (inSection)
                    break;

                inSection = line.StartsWith($"## [{version}]");
                continue;
            }

            if (inSection)
                section.Add(line);
        }
    }

    string notes;
    if (section.Count > 0)
    {
        notes = String.Join("\n", section).Trim();
    }
    else if (changelogPath != null && repository != null)
    {
        var relativePath = MakeAbsolute(Directory(".")).GetRelativePath(changelogPath);
        notes = $"See [CHANGELOG](https://github.com/{repository}/blob/{tag}/{relativePath}).";
    }
    else
    {
        notes = $"{packageId} {version}";
    }

    return $"{notes}\n\n---\n\nNuGet: https://www.nuget.org/packages/{packageId}/{version}\n";
}
