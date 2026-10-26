using System.Buffers.Binary;
using System.Text;

namespace MeuClima.Core.Switches;

/// <summary>O mínimo de DNS-SD para achar aparelhos eWeLink: consulta PTR e leitura dos registros TXT.</summary>
public static class MdnsMessages
{
    public const string Service = "_ewelink._tcp.local";
    private const ushort TypePtr = 12, TypeTxt = 16;

    public static byte[] BuildQuery()
    {
        var q = new List<byte>(40) { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in Service.Split('.'))
        {
            q.Add((byte)label.Length);
            q.AddRange(Encoding.ASCII.GetBytes(label));
        }
        q.AddRange([0, 0, (byte)TypePtr, 0, 1]);
        return [.. q];
    }

    /// <summary>Um dicionário por registro TXT; pacote malformado devolve o que deu para ler, sem lançar.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseTxt(ReadOnlySpan<byte> packet)
    {
        var result = new List<IReadOnlyDictionary<string, string>>();
        if (packet.Length < 12) return result;
        int questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..]);
        int records = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) + BinaryPrimitives.ReadUInt16BigEndian(packet[8..])
                      + BinaryPrimitives.ReadUInt16BigEndian(packet[10..]);
        var off = 12;
        for (var i = 0; i < questions; i++)
        {
            if (!SkipName(packet, ref off)) return result;
            off += 4;
        }
        for (var i = 0; i < records; i++)
        {
            if (!SkipName(packet, ref off) || off + 10 > packet.Length) return result;
            var type = BinaryPrimitives.ReadUInt16BigEndian(packet[off..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(packet[(off + 8)..]);
            off += 10;
            if (off + length > packet.Length) return result;
            if (type == TypeTxt) result.Add(ReadTxt(packet.Slice(off, length)));
            off += length;
        }
        return result;
    }

    /// <summary>Avança sobre um nome; um ponteiro de compressão encerra o nome no próprio lugar.</summary>
    private static bool SkipName(ReadOnlySpan<byte> p, ref int off)
    {
        while (off < p.Length)
        {
            var len = p[off];
            if (len == 0) { off++; return true; }
            if ((len & 0xC0) == 0xC0) { off += 2; return off <= p.Length; }
            off += 1 + len;
        }
        return false;
    }

    private static Dictionary<string, string> ReadTxt(ReadOnlySpan<byte> data)
    {
        var txt = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < data.Length)
        {
            int len = data[i];
            if (i + 1 + len > data.Length) break;
            var entry = Encoding.UTF8.GetString(data.Slice(i + 1, len));
            var eq = entry.IndexOf('=');
            if (eq > 0) txt[entry[..eq]] = entry[(eq + 1)..];
            i += 1 + len;
        }
        return txt;
    }
}
