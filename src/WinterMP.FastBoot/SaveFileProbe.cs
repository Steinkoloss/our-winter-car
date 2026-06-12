using System.IO;
using UnityEngine;

namespace WinterMP.FastBoot
{
    internal static class SaveFileProbe
    {
        private const string SaveFileName = "savefile.txt";

        public static bool HasSave()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, SaveFileName);
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
