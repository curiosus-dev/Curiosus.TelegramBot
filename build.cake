#load "build/publish.cake"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument<string>("target", "Build");
var configuration = Argument<string>("configuration", "Release");
var framework = Argument<string>("framework", "");
var collectCoverage = Argument<bool>("coverage", false);

var artifactsDir = Directory("./artifacts");
var solutionPath = "./Markeli.TelegramBot.sln";

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
	.Does(() =>
	{
		DotNetClean(solutionPath);
		CleanDirectory(artifactsDir);
		EnsureDirectoryExists(artifactsDir);
	});

Task("Build")
	.IsDependentOn("Clean")
	.Does(() =>
	{
		var settings = new DotNetBuildSettings
		{
			Configuration = configuration
		};

		if (!String.IsNullOrEmpty(framework))
			settings.Framework = framework;

		DotNetBuild(solutionPath, settings);
	});

Task("Test")
	.IsDependentOn("Build")
	.Does(() =>
	{
		var settings = new DotNetTestSettings
		{
			Configuration = configuration,
			NoBuild = true
		};

		if (!String.IsNullOrEmpty(framework))
			settings.Framework = framework;

		if (collectCoverage)
		{
			settings.ArgumentCustomization = args => args
				.Append("/p:CollectCoverage=true")
				.Append("/p:CoverletOutputFormat=cobertura")
				.Append($"/p:CoverletOutput={MakeAbsolute(artifactsDir)}/coverage");
		}

		DotNetTest(solutionPath, settings);
	});

Task("Coverage-Report")
	.IsDependentOn("Test")
	.Does(() =>
	{
		var suffix = String.IsNullOrEmpty(framework)
			? "coverage.cobertura.xml"
			: $"coverage.{framework}.cobertura.xml";
		var reportPath = $"{MakeAbsolute(artifactsDir)}/{suffix}";
		if (!FileExists(reportPath))
			throw new Exception($"Coverage report not found at {reportPath}. Did you run with --coverage=true?");

		StartProcess("dotnet", new ProcessSettings
		{
			Arguments = new ProcessArgumentBuilder()
				.Append("tool").Append("run").Append("reportgenerator")
				.Append($"-reports:{reportPath}")
				.Append($"-targetdir:{MakeAbsolute(artifactsDir)}/coverage-report")
				.Append("-reporttypes:Html;TextSummary")
		});
	});

Task("Default")
	.IsDependentOn("Test");

RunTarget(target);
