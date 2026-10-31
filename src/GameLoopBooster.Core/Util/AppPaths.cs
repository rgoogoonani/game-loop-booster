namespace GameLoopBooster.Core.Util;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameLoopBooster");

    public static string BackupFile => Path.Combine(DataDir, "backup.json");
    public static string RegExportDir => Path.Combine(DataDir, "reg-exports");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "booster.log");
}
