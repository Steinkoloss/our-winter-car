using System.IO;

namespace WinterMP.Launcher.Services
{
    public static class ModRemoval
    {
        public static string RemoveFromGame(string gameDir)
        {
            string modDir = Path.Combine(gameDir, "BepInEx", "plugins", "WinterMP");
            if (!Directory.Exists(modDir))
                return $"{Branding.ProductName} is not installed.";

            Directory.Delete(modDir, recursive: true);
            return $"Removed {Branding.ProductName} from the game. BepInEx remains installed.";
        }
    }
}
