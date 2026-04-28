namespace EasySave.Services;

public interface ICryptoSoft
{
    /// <summary>
    /// Chiffre le fichier en place via l'exe externe CryptoSoft.
    /// </summary>
    /// <returns>
    /// Durée en ms (>0) si succès, ou code erreur négatif:
    ///  -1 = fichier introuvable,
    ///  -2 = exe CryptoSoft introuvable / échec de lancement,
    ///  -3 = exit code non nul.
    /// </returns>
    long Encrypt(string filePath);
}
