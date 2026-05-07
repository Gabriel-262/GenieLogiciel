namespace EasySave.Services;

// Cause d'une pause sur un job. Plusieurs causes peuvent être actives en
// même temps : un job ne reprend que lorsque TOUTES ont été levées.
public enum PauseReason
{
    User,         // bouton Pause utilisateur
    Business,     // logiciel métier détecté
    FileLocked    // fichier source verrouillé par une autre application
}
