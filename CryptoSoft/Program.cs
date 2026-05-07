namespace CryptoSoft;

public static class Program
{
    // Mutex Windows global : garantit qu'une seule instance de CryptoSoft.exe
    // tourne à la fois sur la machine, peu importe l'utilisateur appelant.
    // Une 2e instance lancée pendant que la 1re travaille quitte immédiatement
    // avec le code -100 (l'appelant — EasySave — doit donc sérialiser ses
    // appels en amont via un SemaphoreSlim).
    private const string MutexName = "Global\\CryptoSoft_SingleInstance";
    private const int ExitCodeAlreadyRunning = -100;

    public static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: false, MutexName, out _);

        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.Zero, exitContext: false); }
            catch (AbandonedMutexException) { acquired = true; }

            if (!acquired)
            {
                Console.Error.WriteLine("CryptoSoft is already running (single-instance).");
                Environment.Exit(ExitCodeAlreadyRunning);
                return;
            }

            try
            {
                foreach (var arg in args)
                    Console.WriteLine(arg);

                var fileManager = new FileManager(args[0], args[1]);
                int elapsed = fileManager.TransformFile();
                Environment.Exit(elapsed);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Environment.Exit(-99);
            }
        }
        finally
        {
            if (acquired)
            {
                try { mutex.ReleaseMutex(); } catch { /* best-effort */ }
            }
        }
    }
}
