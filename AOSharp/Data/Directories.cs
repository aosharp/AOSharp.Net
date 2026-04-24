using System;
using System.IO;

namespace AOSharp.Data
{
    public static class Directories
    {
        public static readonly string CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ConfigFilePath = Path.Combine(CurrentDirectory, "config.json");
        public static readonly string LogDirPath = Path.Combine(CurrentDirectory, "logs");
        public static readonly string ReposDirPath = Path.Combine(CurrentDirectory, "repos");
        public static readonly string PluginsDirPath = Path.Combine(CurrentDirectory, "Plugins");
    }
}
