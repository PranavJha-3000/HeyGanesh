namespace HeyGanesh.DrawStage;

/// <summary>
/// Where the vendored drawing stage page lands next to the built assembly (the csproj copies Page/ there). The window
/// that serves it from a virtual host (FEATURE-011 step 2) and DS-1 both find the page through this.
/// </summary>
public static class DrawStagePage
{
    /// <summary>The output folder name of the page, beside the assembly.</summary>
    public const string Folder = "DrawStagePage";

    /// <summary>The page folder for a process whose output holds this assembly's content.</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, Folder);
}
