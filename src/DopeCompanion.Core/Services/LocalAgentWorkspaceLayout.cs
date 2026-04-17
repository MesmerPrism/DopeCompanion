namespace DopeCompanion.Core.Services;

public static class LocalAgentWorkspaceLayout
{
    public static string RootPath => CompanionOperatorDataLayout.LocalAgentWorkspaceRootPath;

    public static string BundledCliRootPath => Path.Combine(RootPath, "cli", "current");
    public static string BundledCliExecutablePath => Path.Combine(BundledCliRootPath, "dope-companion.exe");
    public static string BundledCliDllPath => Path.Combine(BundledCliRootPath, "dope-companion.dll");
    public static string BundledCliLslDllPath => LslRuntimeLayout.GetLocalDllPath(BundledCliRootPath);
    public static string BundledCliRuntimeLslDllPath => LslRuntimeLayout.GetRuntimeDllPath(BundledCliRootPath);
}
