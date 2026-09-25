namespace PublicData.Shared;

/// <summary>
/// The key shared by the API (signs the tokens) and the MCP server (validates them): base64 of at least 32 random
/// bytes, since HS256 needs 256 bits. It always comes from a file (a Docker secret in Compose), never from
/// appsettings. Compiled into both projects as a linked file.
/// </summary>
internal static class SigningKeyFile
{
    public const int MinimumBytes = 32;

    /// <exception cref="InvalidOperationException">Missing file, invalid base64 or a key shorter than 256 bits.</exception>
    public static byte[] Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Chave de assinatura do MCP não encontrada em {path}.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(File.ReadAllText(path).Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"A chave em {path} não está em base64.");
        }

        if (key.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"A chave em {path} tem {key.Length * 8} bits; o mínimo para HS256 é {MinimumBytes * 8}.");
        }

        return key;
    }
}
