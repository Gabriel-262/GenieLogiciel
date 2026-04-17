using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services
{
    //Service for managing backup jobs
    public class BackupJobService
    {
        // Use the .env for the const
        private static readonly int MaxJobs = LoadMaxJobsFromEnv();

        private static int LoadMaxJobsFromEnv()
        {
            string envPath = File.Exists(".env") ? ".env" : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            string line = File.ReadLines(envPath).First(l => l.StartsWith("MAX_JOBS="));
            return int.Parse(line.Substring(9));
        }

        //json serializer options: pretty print for Notepad readability
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public List<BackupJob> LoadJobs()
        {
            //Read path from PathManager
            string path = PathManager.GetJobsConfigFilePath();

            if (!File.Exists(path))
                return new List<BackupJob>();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<BackupJob>>(json) ?? new List<BackupJob>();
        }

        //Saves the full list of backup jobs to the configuration JSON file

        public void SaveJobs(List<BackupJob> jobs)
        {
            if (jobs.Count > MaxJobs)
                throw new InvalidOperationException($"Cannot save more than {MaxJobs} backup jobs.");

            string path = PathManager.GetJobsConfigFilePath();
            string json = JsonSerializer.Serialize(jobs, JsonOptions);
            File.WriteAllText(path, json);
        }

        public void AddJob(BackupJob job)
        {
            var jobs = LoadJobs();

            if (jobs.Count >= MaxJobs)
                throw new InvalidOperationException($"Maximum number of backup jobs ({MaxJobs}) already reached.");

            jobs.Add(job);
            SaveJobs(jobs);
        }


        public void UpdateJob(int index, BackupJob updatedJob)
        {
            //persist
            var jobs = LoadJobs();

            if (index < 0 || index >= jobs.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Job index is out of range.");

            jobs[index] = updatedJob;
            SaveJobs(jobs);
        }

        public void DeleteJob(int index)
        {
            //persist
            var jobs = LoadJobs();

            if (index < 0 || index >= jobs.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Job index is out of range.");

            jobs.RemoveAt(index);
            SaveJobs(jobs);
        }
    }
}