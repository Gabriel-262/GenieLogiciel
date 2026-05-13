using EasySave.Models;

namespace EasySave.Services;

// Vue minimale des settings runtime requise par le moteur d'exécution.
// Permet d'injecter un faux jeu de paramètres dans les tests sans construire
// un SettingsService complet (qui charge un fichier disque).
public interface ISettingsProvider
{
    AppSettings Current { get; }
}
