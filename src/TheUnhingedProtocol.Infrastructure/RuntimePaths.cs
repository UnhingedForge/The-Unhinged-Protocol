namespace TheUnhingedProtocol.Infrastructure;

public static class RuntimePaths
{
    public static string Root
    {
        get
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UnhingedForge",
                "TheUnhingedProtocol");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string Database => Path.Combine(Root, "state.db");

    public static string Preferences => Path.Combine(Root, "settings.v1.json");
}
