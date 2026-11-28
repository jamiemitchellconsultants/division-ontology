using System.Security.Cryptography;
using System.Text;

namespace RuntimeKnowledge.Impact.Json;

public static class GitBlob
{
    public static string Sha1Hex(byte[] bytes)
    {
        using var sha1 = SHA1.Create();
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        sha1.TransformBlock(header, 0, header.Length, header, 0);
        sha1.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToHexStringLower(sha1.Hash!);
    }
}
