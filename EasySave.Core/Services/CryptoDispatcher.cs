namespace EasySave.Services;

/// <summary>
/// Aiguille chaque appel de chiffrement vers la stratégie adaptée au CryptoMode courant:
///  - "Rapide"   -> XOR via exe externe CryptoSoft (VRE)
///  - "Standard" -> AES-256-CBC in-process
///  - "Premium"  -> ECIES (ECDH P-256 + AES-256-GCM) in-process
/// </summary>
public class CryptoDispatcher : ICryptoSoft
{
    private readonly SettingsService _settings;
    private readonly XorCryptoService _xor;
    private readonly AesCryptoService _aes;
    private readonly EciesCryptoService _ecies;

    public CryptoDispatcher(
        SettingsService settings,
        XorCryptoService xor,
        AesCryptoService aes,
        EciesCryptoService ecies)
    {
        _settings = settings;
        _xor   = xor;
        _aes   = aes;
        _ecies = ecies;
    }

    public long Encrypt(string filePath)
    {
        ICryptoSoft strategy = _settings.Current.CryptoMode switch
        {
            "Premium"  => _ecies,
            "Standard" => _aes,
            _          => _xor
        };
        return strategy.Encrypt(filePath);
    }
}
