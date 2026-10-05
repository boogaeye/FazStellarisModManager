using System.Globalization;
using System.Text;
using FazStellarisModmanager.Core.Descriptors;

namespace FazStellarisModmanager.Core.Saves;

/// <summary>
/// Pulls players and countries out of a save's gamestate (hundreds of MB) without parsing all of it: walks the bytes,
/// skips unneeded blocks by brace matching (quotes and # comments respected) and hands only small country sub-blocks to
/// <see cref="ParadoxScriptParser"/>. Tolerates truncated or odd input.
/// </summary>
public static class GamestateScanner
{
    static readonly HashSet<string> CountryBlocks = new(StringComparer.Ordinal)
    {
        "name", "adjective", "flag", "variables", "relations_manager", "tech_status",
    };

    public static (List<SavePlayer> Players, List<SaveCountry> Countries) Scan(byte[] data)
    {
        var players = new List<SavePlayer>();
        var countries = new List<SaveCountry>();
        var r = new Reader(data, 0, data.Length);
        while (r.NextEntry(out var key, out var value, out var body))
        {
            if (body is not { } b) continue;
            if (r.Is(key, "player")) players.AddRange(ReadPlayers(Parse(data, b)));
            else if (r.Is(key, "country")) ScanCountries(data, b, countries);
        }
        return (players, countries);
    }

    static IEnumerable<SavePlayer> ReadPlayers(PdxBlock block)
    {
        foreach (var p in block.Items.OfType<PdxBlock>())
            if (p.GetString("name") is { } name && int.TryParse(p.GetString("country"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                yield return new SavePlayer(name, id);
    }

    static void ScanCountries(byte[] data, (int Start, int End) range, List<SaveCountry> countries)
    {
        var r = new Reader(data, range.Start, range.End);
        while (r.NextEntry(out var key, out _, out var body))
        {
            if (body is not { } b || !int.TryParse(r.Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            countries.Add(ScanCountry(data, id, b));
        }
    }

    static SaveCountry ScanCountry(byte[] data, int id, (int Start, int End) range)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var blocks = new Dictionary<string, PdxBlock>(StringComparer.Ordinal);
        var r = new Reader(data, range.Start, range.End);
        while (r.NextEntry(out var key, out var value, out var body))
        {
            var name = r.Text(key);
            if (body is { } b)
            {
                if (CountryBlocks.Contains(name)) blocks[name] = Parse(data, b);
            }
            else if (value is { } v)
            {
                fields[name] = r.Text(v);
            }
        }
        return Build(id, fields, blocks);
    }

    static SaveCountry Build(int id, Dictionary<string, string> f, Dictionary<string, PdxBlock> b)
    {
        double Num(string k) => f.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        var name = b.GetValueOrDefault("name");
        var adjective = b.GetValueOrDefault("adjective");
        var adjectiveKey = adjective?.GetString("key");
        var adjectiveText = adjectiveKey is not null && !adjectiveKey.Contains('%') ? adjectiveKey : Variables(adjective).GetValueOrDefault("adjective");

        CountryFlag? flag = null;
        if (b.GetValueOrDefault("flag") is { } fl)
        {
            var icon = fl.GetBlock("icon");
            var bg = fl.GetBlock("background");
            flag = new CountryFlag(icon?.GetString("category"), icon?.GetString("file"), bg?.GetString("category"), bg?.GetString("file"),
                fl.GetBlock("colors")?.StringItems.ToList() ?? []);
        }

        var contacts = new List<int>();
        if (b.GetValueOrDefault("relations_manager") is { } rm)
        {
            foreach (var rel in rm.Entries.Where(e => e.Key == "relation").Select(e => e.Value).OfType<PdxBlock>())
            {
                if (rel.GetString("contact") == "yes"
                    && int.TryParse(rel.GetString("country"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var other)
                    && !contacts.Contains(other))
                    contacts.Add(other);
            }
        }

        var techs = b.GetValueOrDefault("tech_status")?.Entries
            .Where(e => e.Key == "technology" && e.Value is string).Select(e => (string)e.Value).Distinct().ToList() ?? [];

        double? cached = b.GetValueOrDefault("variables")?.GetString("egm_cached_diplo_weight") is { } cv
                         && double.TryParse(cv, NumberStyles.Float, CultureInfo.InvariantCulture, out var cd) ? cd : null;

        return new SaveCountry(
            id,
            f.GetValueOrDefault("type"),
            name?.GetString("key"),
            Variables(name),
            adjectiveText,
            flag,
            int.TryParse(f.GetValueOrDefault("victory_rank"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) ? rank : 0,
            Num("victory_score"),
            Num("military_power"),
            Num("economy_power"),
            Num("tech_power"),
            Num("fleet_size"),
            Num("empire_size"),
            Num("num_sapient_pops"),
            cached,
            contacts,
            techs);
    }

    // { variables={ { key="adjective" value={ key="Fazbear" } } … } } → adjective → Fazbear (first wins).
    static Dictionary<string, string> Variables(PdxBlock? block)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in block?.GetBlock("variables")?.Items.OfType<PdxBlock>() ?? [])
            if (v.GetString("key") is { } k && v.GetBlock("value")?.GetString("key") is { } val)
                result.TryAdd(k, val);
        return result;
    }

    static PdxBlock Parse(byte[] data, (int Start, int End) range) =>
        ParadoxScriptParser.Parse(Encoding.UTF8.GetString(data, range.Start, range.End - range.Start));

    enum Kind { End, Word, Quoted, Open, Close, Op }

    readonly record struct Token(Kind Kind, int Start, int End);

    /// <summary>Tokens of data[start..end). Quoted tokens exclude their quotes.</summary>
    sealed class Reader(byte[] data, int start, int end)
    {
        int _pos = start;

        /// <summary>
        /// Next "key op value" entry. For a block value, <paramref name="body"/> is the range between its braces and the
        /// reader moves past it. Bare items and anonymous blocks are skipped. False at the end of the range.
        /// </summary>
        public bool NextEntry(out Token key, out Token? value, out (int Start, int End)? body)
        {
            while (true)
            {
                value = null;
                body = null;
                key = Read();
                switch (key.Kind)
                {
                    case Kind.End:
                        return false;
                    case Kind.Open:
                        SkipBlock();
                        continue;
                    case Kind.Close:
                    case Kind.Op:
                        continue;
                }

                var save = _pos;
                var op = Read();
                if (op.Kind != Kind.Op)
                {
                    _pos = save; // a bare item: the next token starts the next entry
                    continue;
                }

                var v = Read();
                if (v.Kind == Kind.Open)
                {
                    var bodyStart = _pos;
                    var bodyEnd = SkipBlock();
                    body = (bodyStart, bodyEnd);
                    return true;
                }
                if (v.Kind is Kind.Word or Kind.Quoted)
                {
                    value = v;
                    return true;
                }
                if (v.Kind == Kind.End) return false;
            }
        }

        public bool Is(Token t, string ascii)
        {
            if (t.End - t.Start != ascii.Length) return false;
            for (var i = 0; i < ascii.Length; i++)
                if (data[t.Start + i] != ascii[i]) return false;
            return true;
        }

        public string Text(Token t) => Encoding.UTF8.GetString(data, t.Start, t.End - t.Start);

        Token Read()
        {
            while (_pos < end)
            {
                var c = data[_pos];
                if (c <= 32) { _pos++; continue; }
                if (c == (byte)'#')
                {
                    while (_pos < end && data[_pos] != 10) _pos++;
                    continue;
                }
                if (c == (byte)'"')
                {
                    var s = ++_pos;
                    while (_pos < end && data[_pos] != (byte)'"')
                    {
                        if (data[_pos] == 92) _pos++;
                        _pos++;
                    }
                    var e = Math.Min(_pos, end);
                    _pos = Math.Min(_pos + 1, end);
                    return new Token(Kind.Quoted, s, e);
                }
                if (c == (byte)'{') return new Token(Kind.Open, _pos, ++_pos);
                if (c == (byte)'}') return new Token(Kind.Close, _pos, ++_pos);
                if (c is (byte)'=' or (byte)'<' or (byte)'>' or (byte)'!' or (byte)'?')
                {
                    var s = _pos++;
                    if (_pos < end && data[_pos] == (byte)'=') _pos++;
                    return new Token(Kind.Op, s, _pos);
                }
                var ws = _pos;
                while (_pos < end)
                {
                    var d = data[_pos];
                    if (d <= 32 || d is (byte)'{' or (byte)'}' or (byte)'=' or (byte)'#' or (byte)'"' or (byte)'<' or (byte)'>' or (byte)'!' or (byte)'?') break;
                    _pos++;
                }
                return new Token(Kind.Word, ws, _pos);
            }
            return new Token(Kind.End, end, end);
        }

        /// <summary>Called just after a '{': moves past its matching '}' and returns the index of that '}' (or the range end).</summary>
        int SkipBlock()
        {
            var depth = 1;
            while (_pos < end)
            {
                var c = data[_pos];
                if (c == (byte)'"')
                {
                    _pos++;
                    while (_pos < end && data[_pos] != (byte)'"')
                    {
                        if (data[_pos] == 92) _pos++;
                        _pos++;
                    }
                    _pos++;
                    continue;
                }
                if (c == (byte)'#')
                {
                    while (_pos < end && data[_pos] != 10) _pos++;
                    continue;
                }
                if (c == (byte)'{') depth++;
                else if (c == (byte)'}' && --depth == 0)
                {
                    var close = _pos;
                    _pos++;
                    return close;
                }
                _pos++;
            }
            _pos = end;
            return end;
        }
    }
}
