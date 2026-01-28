using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace aquama;

public class SecureSerializer
{
    private readonly byte[] EncryptionKey;
    private readonly byte[] Salt;
    private readonly int Iterations;

    public class SecureSerializerConfig
    {
        [JsonProperty("secret")]    
        public string Secret { get; set; } = "";
        [JsonProperty("salt")]
        public string Salt { get; set; } = "";
        [JsonProperty("iterations")]
        public int Iterations { get; set; }
    }

    public SecureSerializer(string secret, string salt, int iterations = 10000)
    {
        EncryptionKey = Encoding.UTF8.GetBytes(secret);
        Salt = Encoding.UTF8.GetBytes(salt);
        Iterations = iterations;
    }

    public SecureSerializer(string fileName)
    {
        SecureSerializerConfig config = JsonConvert.DeserializeObject<SecureSerializerConfig>(File.ReadAllText(fileName)) ?? 
            throw new Exception($"Failed to load SecureSerializer config '{fileName}'.");
        EncryptionKey = Encoding.UTF8.GetBytes(config.Secret);
        Salt = Encoding.UTF8.GetBytes(config.Salt);
        Iterations = config.Iterations;
    }

    /// <summary>
    /// Serialize, compress, encrypt and encode to base64
    /// </summary>
    public string Serialize<T>(T obj)
    {
        // Sérialiser en JSON
        string json = JsonConvert.SerializeObject(obj, Formatting.None);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        
        // Compresser
        byte[] compressed = CompressData(jsonBytes);
        
        // Crypter
        byte[] encrypted = EncryptData(compressed);
        
        // Convertir en base64
        return Convert.ToBase64String(encrypted);
    }
    
    /// <summary>
    /// Decode from base64, decrypt, decompress and deserialize
    /// </summary>
    public T Deserialize<T>(string base64Data)
    {
        // Convertir depuis base64
        byte[] encrypted = Convert.FromBase64String(base64Data);
        
        // Décrypter
        byte[] compressed = DecryptData(encrypted);
        
        // Décompresser
        byte[] jsonBytes = DecompressData(compressed);
        
        // Désérialiser
        string json = Encoding.UTF8.GetString(jsonBytes);
        return JsonConvert.DeserializeObject<T>(json)!;
    }

    /// <summary>
    /// Deserialize with polymorphic type resolution using a discriminator field
    /// </summary>
    public T DeserializePolymorphic<T>(string base64Data, string discriminatorField, Func<string?, T> typeResolver)
    {
        // Convertir depuis base64
        byte[] encrypted = Convert.FromBase64String(base64Data);
        
        // Décrypter
        byte[] compressed = DecryptData(encrypted);
        
        // Décompresser
        byte[] jsonBytes = DecompressData(compressed);
        
        // Désérialiser - parser le discriminateur d'abord
        string json = Encoding.UTF8.GetString(jsonBytes);
        var jsonObject = JObject.Parse(json);
        var discriminatorValue = jsonObject[discriminatorField]?.Value<string>();
        
        return typeResolver(discriminatorValue);
    }

    private static byte[] CompressData(byte[] data)
    {
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
        {
            gzipStream.Write(data, 0, data.Length);
        }
        return outputStream.ToArray();
    }
    
    private static byte[] DecompressData(byte[] data)
    {
        using var inputStream = new MemoryStream(data);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
   
    private byte[] EncryptData(byte[] data)
    {
        using var aes = Aes.Create();
        
        // Dériver une clé depuis le mot de passe
        using var keyDerivation = new Rfc2898DeriveBytes(EncryptionKey, Salt, Iterations, HashAlgorithmName.SHA256);
        aes.Key = keyDerivation.GetBytes(32);
        aes.IV = keyDerivation.GetBytes(16);
        
        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();
        
        // Écrire l'IV au début pour pouvoir décrypter
        msEncrypt.Write(aes.IV, 0, aes.IV.Length);
        
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        {
            csEncrypt.Write(data, 0, data.Length);
        }
        
        return msEncrypt.ToArray();
    }
    
    private byte[] DecryptData(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        
        // Extraire l'IV du début
        byte[] iv = new byte[16];
        Array.Copy(encryptedData, 0, iv, 0, 16);
        
        // Dériver la même clé
        using var keyDerivation = new Rfc2898DeriveBytes(EncryptionKey, Salt, Iterations, HashAlgorithmName.SHA256);
        aes.Key = keyDerivation.GetBytes(32);
        aes.IV = iv;
        
        using var decryptor = aes.CreateDecryptor();
        using var msDecrypt = new MemoryStream(encryptedData, 16, encryptedData.Length - 16);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var outputStream = new MemoryStream();
        
        csDecrypt.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
