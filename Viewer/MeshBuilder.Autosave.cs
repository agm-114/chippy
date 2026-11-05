using YamlDotNet.Core;

namespace Chippy.Viewer;

internal partial class MeshBuilder
{
    private const string WorkingShipName = "WorkingShip.yaml";
    internal static readonly string WorkingShipRelativePath = Path.Combine("Assets", "Meshes", WorkingShipName);
    private string? workingShipPath, workingShipError;
    private bool workingShipDirty;
    private long retryAutosaveAfter;

    public static string? FindWorkingShipPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "Chippy.csproj")))
                    return Path.Combine(folder.FullName, WorkingShipRelativePath);
        return null;
    }

    public void OpenWorkingShip(string? path)
    {
        if (path is null) workingShipError = "Autosave needs the Chippy source folder.";
        else LoadWorkingShip(path);
    }

    private void LoadWorkingShip(string path)
    {
        try
        {
            ApplyYaml(File.ReadAllText(path));
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or YamlException or InvalidDataException)
        {
            workingShipError = $"Couldn't load {WorkingShipName}. Autosave is paused. {error.Message}";
            return;
        }
        workingShipPath = path;
        workingShipDirty = false;
    }

    private void MarkShipChanged() => workingShipDirty = yamlStale = true;

    public void SaveWorkingShip(bool force = false)
    {
        if (!workingShipDirty || workingShipPath is not { } path
            || (!force && Environment.TickCount64 < retryAutosaveAfter)) return;
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                File.WriteAllText(temporary, ExportYaml());
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            workingShipError = null;
            workingShipDirty = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            workingShipError = $"Couldn't autosave {WorkingShipName}. {error.Message}";
            retryAutosaveAfter = Environment.TickCount64 + 2000;
        }
    }
}
