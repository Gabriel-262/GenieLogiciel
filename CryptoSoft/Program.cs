using System.Security.Cryptography;
using System.Text;

// Usage: CryptoSoft.exe <filePath> <key> <algo:xor|aes>
// Exit codes: 0 OK, 1 bad args, 2 file not found, 3 crypto error

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: CryptoSoft <filePath> <key> <xor|aes>");
    return 1;
}

string filePath = args[0];
string key      = args[1];
string algo     = args[2].ToLowerInvariant();

if (!File.Exists(filePath))
{
    Console.Error.WriteLine($"File not found: {filePath}");
    return 2;
}

try
{
    byte[] keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    byte[] plain    = File.ReadAllBytes(filePath);
    byte[] cipher   = algo switch
    {
        "xor" => XorTransform(plain, keyBytes),
        "aes" => AesEncrypt(plain, keyBytes),
        _     => throw new ArgumentException($"Unknown algo: {algo}")
    };
    File.WriteAllBytes(filePath, cipher);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

static byte[] XorTransform(byte[] data, byte[] key)
{
    byte[] result = new byte[data.Length];
    for (int i = 0; i < data.Length; i++)
        result[i] = (byte)(data[i] ^ key[i % key.Length]);
    return result;
}

static byte[] AesEncrypt(byte[] data, byte[] key)
{
    using var aes = Aes.Create();
    aes.Key = key;
    aes.GenerateIV();
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;

    using var encryptor = aes.CreateEncryptor();
    byte[] body = encryptor.TransformFinalBlock(data, 0, data.Length);

    byte[] output = new byte[aes.IV.Length + body.Length];
    Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
    Buffer.BlockCopy(body,  0, output, aes.IV.Length, body.Length);
    return output;
}
