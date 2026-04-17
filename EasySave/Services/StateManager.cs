using System.Text.Json;
using EasySave.Interfaces;
using EasySave.Models;

namespace EasySave.Services
{
    public class StateManager : IStateManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };


        private static readonly object FileLock = new object();

        // Updates or inserts the state entry for the given job, then persists
        public void UpdateState(StateEntry entry)
        {
            /// TODO (Dev 1): Call this inside the file copy loop after each file is transferred.
            /// TODO (Dev 3): Ensure UI correctly observes and reflects these state changes.
            lock (FileLock)
            {
                List<StateEntry> states = ReadExistingStates();

                int existingIndex = states.FindIndex(s => s.JobName == entry.JobName);
                if (existingIndex >= 0)
                    states[existingIndex] = entry;
                else
                    states.Add(entry);

                WriteStates(states);
            }
        }

        // Sets the job status to Inactive and resets progress fields
        public void ClearState(string jobName)
        {
            /// TODO (Dev 1): Call this once the job finishes to reset its status.
            lock (FileLock)
            {
                List<StateEntry> states = ReadExistingStates();

                int index = states.FindIndex(s => s.JobName == jobName);
                if (index < 0) return;

                states[index] = new StateEntry
                {
                    JobName = jobName,
                    LastActionTime = DateTime.Now,
                    Status = JobStatus.Inactive
                };

                WriteStates(states);
            }
        }

        private static List<StateEntry> ReadExistingStates()
        {
            // Resolve path via PathManager
            string path = PathManager.GetStateFilePath();

            if (!File.Exists(path))
                return new List<StateEntry>();

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<StateEntry>>(json) ?? new List<StateEntry>();
            }
            catch (JsonException)
            {
                // Initialize clean state if the file is corrupted
                return new List<StateEntry>();
            }
        }

        private static void WriteStates(List<StateEntry> states)
        {
            string path = PathManager.GetStateFilePath();
            string json = JsonSerializer.Serialize(states, JsonOptions);
            File.WriteAllText(path, json);
        }
    }
}