using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RuntimeKnowledge.Impact.Json;

/// <summary>Writes JSON exactly as ECMAScript JSON.stringify does, so ids and digests match the Node pilot.</summary>
public static class EcmaJson
{
    public static string Stringify(JsonNode? node, bool indent = false)
    {
        var builder = new StringBuilder();
        Write(builder, node, indent, 0);
        return builder.ToString();
    }

    /// <summary>JSON.stringify(value, null, 2) + "\n", as the CLI writes its files.</summary>
    public static string File(JsonNode? node) => Stringify(node, indent: true) + "\n";

    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Port of canonical(): sorts object keys recursively by UTF-16 code unit order.</summary>
    public static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Canonical).ToArray()),
        null => null,
        _ => node.DeepClone()
    };

    /// <summary>sha256(JSON.stringify(canonical(value)) + '\n').</summary>
    public static string CanonicalDigest(JsonNode? node) => Sha256Hex(Stringify(Canonical(node)) + "\n");

    private static void Write(StringBuilder b, JsonNode? node, bool indent, int depth)
    {
        switch (node)
        {
            case null: b.Append("null"); return;
            case JsonObject o:
                if (o.Count == 0) { b.Append("{}"); return; }
                b.Append('{');
                var first = true;
                foreach (var (key, value) in o)
                {
                    if (!first) b.Append(',');
                    first = false;
                    Break(b, indent, depth + 1);
                    WriteString(b, key);
                    b.Append(indent ? ": " : ":");
                    Write(b, value, indent, depth + 1);
                }
                Break(b, indent, depth);
                b.Append('}');
                return;
            case JsonArray a:
                if (a.Count == 0) { b.Append("[]"); return; }
                b.Append('[');
                for (var i = 0; i < a.Count; i++)
                {
                    if (i > 0) b.Append(',');
                    Break(b, indent, depth + 1);
                    Write(b, a[i], indent, depth + 1);
                }
                Break(b, indent, depth);
                b.Append(']');
                return;
            case JsonValue v:
                switch (v.GetValueKind())
                {
                    case System.Text.Json.JsonValueKind.String: WriteString(b, v.GetValue<string>()); return;
                    case System.Text.Json.JsonValueKind.True: b.Append("true"); return;
                    case System.Text.Json.JsonValueKind.False: b.Append("false"); return;
                    case System.Text.Json.JsonValueKind.Null: b.Append("null"); return;
                    case System.Text.Json.JsonValueKind.Number:
                        if (v.TryGetValue<int>(out var i32)) { b.Append(i32.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                        if (v.TryGetValue<long>(out var whole)) { b.Append(whole.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                        var number = v.GetValue<double>();
                        if (Math.Floor(number) == number && Math.Abs(number) < 9007199254740992d)
                        { b.Append(((long)number).ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                        throw new NotSupportedException("Only integer numbers occur in impact outputs.");
                }
                break;
        }
        throw new NotSupportedException("Unsupported JSON node.");
    }

    private static void Break(StringBuilder b, bool indent, int depth)
    {
        if (!indent) return;
        b.Append('\n').Append(' ', depth * 2);
    }

    private static void WriteString(StringBuilder b, string s)
    {
        b.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { b.Append(c).Append(s[i + 1]); i++; }
                    else if (char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        b.Append('"');
    }
}
