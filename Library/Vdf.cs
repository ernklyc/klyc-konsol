using System.Text;

namespace KlycKonsol.Library;

/// <summary>Valve'in metin VDF biçimi için küçük ayrıştırıcı ("anahtar" "değer" ve "anahtar" { ... } düğümleri).</summary>
public sealed class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.TryGetValue(key, out var n) ? n : null;

    public string? Str(string key) => this[key]?.Value;

    public long Long(string key) => long.TryParse(Str(key), out var v) ? v : 0;
}

public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ParseBlock(text, ref pos, root, topLevel: true);
        return root;
    }

    static void ParseBlock(string s, ref int pos, VdfNode into, bool topLevel)
    {
        while (true)
        {
            var key = NextToken(s, ref pos, out var kind);
            if (kind == Kind.End) return;
            if (kind == Kind.Close) { if (topLevel) continue; return; }
            if (kind != Kind.Text) continue;

            var next = NextToken(s, ref pos, out kind);
            var node = new VdfNode();
            if (kind == Kind.Text) node.Value = next;
            else if (kind == Kind.Open) ParseBlock(s, ref pos, node, topLevel: false);
            else if (kind == Kind.End) { into.Children[key!] = node; return; }
            into.Children[key!] = node;
        }
    }

    enum Kind { Text, Open, Close, End }

    static string? NextToken(string s, ref int pos, out Kind kind)
    {
        while (pos < s.Length)
        {
            var c = s[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')   // satır yorumu
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            if (c == '{') { pos++; kind = Kind.Open; return null; }
            if (c == '}') { pos++; kind = Kind.Close; return null; }
            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < s.Length && s[pos] != '"')
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length)
                    {
                        pos++;
                        sb.Append(s[pos] switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', var x => x });
                    }
                    else sb.Append(s[pos]);
                    pos++;
                }
                pos++;   // kapanış tırnağı
                kind = Kind.Text;
                return sb.ToString();
            }
            // tırnaksız sözcük
            var start = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}' && s[pos] != '"') pos++;
            kind = Kind.Text;
            return s[start..pos];
        }
        kind = Kind.End;
        return null;
    }
}
