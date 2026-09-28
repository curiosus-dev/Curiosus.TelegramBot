// Shared tasks and arguments come from build/curiosus.cake (synced from curiosus-dev/dotnet-tools).
// Repository-specific tasks go between #load and RunTarget.
#load "build/curiosus.cake"

// Line coverage, %: raise it when coverage grows.
minLineCoverage = 70;

RunTarget(target);
