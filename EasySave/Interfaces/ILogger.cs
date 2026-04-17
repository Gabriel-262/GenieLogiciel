namespace EasySave.Interfaces
{
    // Contract for the EasyLog logging system
    /// TODO Dev 1: inject this interface into your copy loops to log each file transfer.
    public interface ILogger
    {
        // Writes a log entry to the daily JSON log file in real time
        void Log(Models.LogEntry entry);
    }
}