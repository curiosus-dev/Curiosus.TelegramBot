///////////////////////////////////////////////////////////////////////////////
// NUGET PUBLISHING
//
// Shared between Curiosus repositories, keep the file identical everywhere.
// Loaded from build.cake via `#load "build/publish.cake"`.
//
// Tasks:
//   Pack           - packs every ./src/**/*.csproj into ./artifacts/packages
//   NuGetPush      - pushes packages to nuget.org, already published versions are skipped;
//                    API key is taken from NUGET_API_KEY (short-lived key from NuGet Trusted Publishing on CI)
//   GitHubReleases - creates a tag and a GitHub release for every package version without one
//   Publish        - NuGetPush + GitHubReleases
//
// Arguments:
//   --configuration=Release
//   --releaseTagFormat=auto       auto: "v{version}" for single-package repositories, "{id}.v{version}" otherwise
//   --githubReleaseDryRun         writes release notes to ./artifacts/release-notes without creating releases
///////////////////////////////////////////////////////////////////////////////

using System.IO.Compression;
using System.Xml.Linq;

var publishConfiguration = Argument<string>("configuration", "Release");
var publishPackagesDir = Directory("./artifacts/packages");
var publishNuGetSource = "https://api.nuget.org/v3/index.json";
var publishReleaseTagFormat = Argument<string>("releaseTagFormat", "auto");
var publishGitHubReleaseDryRun = HasArgument("githubReleaseDryRun");

Task("Pack")
	.Does(() =>
	{
		CleanDirectory(publishPackagesDir);

		foreach (var project in GetFiles("./src/**/*.csproj"))
		{
			Information($"Packing \"{project.GetFilename()}\"...");
			DotNetPack(project.FullPath, new DotNetPackSettings
			{
				Configuration = publishConfiguration,
				OutputDirectory = publishPackagesDir
			});
		}
	});

Task("NuGetPush")
	.Does(() =>
	{
		var apiKey = EnvironmentVariable("NUGET_API_KEY");
		if (String.IsNullOrWhiteSpace(apiKey))
			throw new CakeException("NUGET_API_KEY environment variable is not set.");

		var packages = GetPublishPackages();
		foreach (var package in packages)
		{
			Information($"Publishing \"{package.GetFilename()}\"...");

			// Symbol packages (.snupkg) next to the .nupkg are pushed automatically.
			DotNetNuGetPush(package.FullPath, new DotNetNuGetPushSettings
			{
				Source = publishNuGetSource,
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
		if (!publishGitHubReleaseDryRun && (String.IsNullOrEmpty(repository) || String.IsNullOrEmpty(commitSha)))
			throw new CakeException("GITHUB_REPOSITORY or GITHUB_SHA is not set. Pass --githubReleaseDryRun to run locally.");

		var releaseNotesDir = Directory("./artifacts/release-notes");
		EnsureDirectoryExists(releaseNotesDir);

		var projectDirs = GetPublishProjectDirectoriesByPackageId();
		var isSinglePackageRepository = projectDirs.Count == 1;
		var tagFormat = publishReleaseTagFormat == "auto"
			? isSinglePackageRepository ? "v{version}" : "{id}.v{version}"
			: publishReleaseTagFormat;

		foreach (var package in GetPublishPackages())
		{
			var (packageId, version) = ReadPublishPackageIdentity(package);
			var tag = tagFormat.Replace("{id}", packageId).Replace("{version}", version);

			if (!publishGitHubReleaseDryRun && GitHubReleaseExists(tag))
			{
				Verbose($"Release \"{tag}\" already exists.");
				continue;
			}

			if (!projectDirs.TryGetValue(packageId, out var projectDir))
				throw new CakeException($"Project for package \"{packageId}\" is not found in ./src.");

			var notesFile = releaseNotesDir.Path.CombineWithFilePath($"{tag}.md");
			var notes = BuildPublishReleaseNotes(projectDir, packageId, version, repository, tag);
			System.IO.File.WriteAllText(notesFile.FullPath, notes);

			if (publishGitHubReleaseDryRun)
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

///////////////////////////////////////////////////////////////////////////////
// PUBLISHING HELPERS
///////////////////////////////////////////////////////////////////////////////

FilePathCollection GetPublishPackages()
{
	var packages = GetFiles($"{publishPackagesDir}/*.nupkg");
	if (packages.Count == 0)
		throw new CakeException($"No packages found in {publishPackagesDir}. Run the Pack task first.");

	return packages;
}

// PackageId may differ from the project name (e.g. Curiosity.Configuration.YAML -> Curiosity.Configuration.YML).
Dictionary<string, DirectoryPath> GetPublishProjectDirectoriesByPackageId()
{
	var result = new Dictionary<string, DirectoryPath>(StringComparer.OrdinalIgnoreCase);
	foreach (var project in GetFiles("./src/**/*.csproj"))
	{
		var properties = XDocument.Load(project.FullPath).Descendants();
		string GetProperty(string name) => properties
			.Where(x => x.Name.LocalName == name)
			.Select(x => x.Value.Trim())
			.FirstOrDefault(x => x.Length > 0);

		if (String.Equals(GetProperty("IsPackable"), "false", StringComparison.OrdinalIgnoreCase))
			continue;

		var packageId = GetProperty("PackageId")
			?? GetProperty("AssemblyName")
			?? project.GetFilenameWithoutExtension().ToString();
		result[packageId] = project.GetDirectory();
	}

	return result;
}

(string PackageId, string Version) ReadPublishPackageIdentity(FilePath nupkg)
{
	using var archive = ZipFile.OpenRead(nupkg.FullPath);
	var nuspecEntry = archive.Entries.Single(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
	using var stream = nuspecEntry.Open();
	var metadata = XDocument.Load(stream).Descendants().First(x => x.Name.LocalName == "metadata");

	string Get(string name) => metadata.Elements().First(x => x.Name.LocalName == name).Value.Trim();
	return (Get("id"), Get("version"));
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
string BuildPublishReleaseNotes(DirectoryPath projectDir, string packageId, string version, string repository, string tag)
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
