namespace EasySave.Services
{

    public static class PathManager
    {
        // Base application data folder (no hardcoded paths)
        // %AppData%\ProSoft\EasySave\
        private static readonly string BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProSoft",
            "EasySave"
        );

        // TASK 2.1 - Sub-folders for each type of data file
        private static readonly string LogsDirectory = Path.Combine(BaseDirectory, "Logs");
        private static readonly string ConfigDirectory = Path.Combine(BaseDirectory, "Config");
        private static readonly string StateDirectory = Path.Combine(BaseDirectory, "State");


        public static void EnsureDirectoriesExist()
        {
            // Create all subdirectories if they do not exist yet
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(StateDirectory);
        }

        // Public path accessors used by all other services

        public static string GetDailyLogFilePath()
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.json";
            return Path.Combine(LogsDirectory, fileName);
        }

        public static string GetJobsConfigFilePath()
        {
            return Path.Combine(ConfigDirectory, "jobs.json");
        }

        public static string GetStateFilePath()
        {
            return Path.Combine(StateDirectory, "state.json");
        }
    }
}