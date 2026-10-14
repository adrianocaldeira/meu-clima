using MeuClima.Core.Devices;
using MeuClima.Core.Rules;
using Microsoft.Data.Sqlite;

namespace MeuClima.Core.History;

public sealed record OutsideSample(DateTimeOffset Time, double TempC);

public sealed record EventRecord(DateTimeOffset Time, string DeviceId, string Kind, string Detail);

/// <summary>Histórico local em SQLite: leituras dos aparelhos, clima de fora e eventos de regra.</summary>
public sealed class HistoryStore : IHistoryReader, IDisposable
{
    private readonly SqliteConnection _db;
    private readonly Lock _lock = new();

    public HistoryStore(string path)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS leitura (
              ts INTEGER NOT NULL, aparelho TEXT NOT NULL, conectado INTEGER NOT NULL, ligado INTEGER NOT NULL,
              modo TEXT NOT NULL, ventilacao TEXT NOT NULL, alvo INTEGER, ambiente INTEGER);
            CREATE INDEX IF NOT EXISTS ix_leitura ON leitura(aparelho, ts);
            CREATE TABLE IF NOT EXISTS fora (ts INTEGER NOT NULL PRIMARY KEY, temp REAL NOT NULL);
            CREATE TABLE IF NOT EXISTS evento (ts INTEGER NOT NULL, aparelho TEXT NOT NULL, tipo TEXT NOT NULL, detalhe TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_evento ON evento(ts);
            """);
    }

    public void Record(DeviceSnapshot s)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO leitura VALUES ($ts,$id,$c,$on,$m,$f,$t,$a)";
            cmd.Parameters.AddWithValue("$ts", s.ReadAt.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$id", s.Id);
            cmd.Parameters.AddWithValue("$c", s.Connected ? 1 : 0);
            cmd.Parameters.AddWithValue("$on", s.On ? 1 : 0);
            cmd.Parameters.AddWithValue("$m", s.Mode.ToString());
            cmd.Parameters.AddWithValue("$f", s.Fan.ToString());
            cmd.Parameters.AddWithValue("$t", (object?)s.TargetC ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$a", (object?)s.AmbientC ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void RecordOutside(DateTimeOffset time, double tempC)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO fora VALUES ($ts,$t)";
            cmd.Parameters.AddWithValue("$ts", time.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$t", tempC);
            cmd.ExecuteNonQuery();
        }
    }

    public void RecordEvent(DateTimeOffset time, string deviceId, string kind, string detail)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO evento VALUES ($ts,$id,$k,$d)";
            cmd.Parameters.AddWithValue("$ts", time.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$d", detail);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<HistorySample> Samples(string deviceId, DateTimeOffset since)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT ts,conectado,ligado,modo,alvo,ambiente FROM leitura WHERE aparelho=$id AND ts>=$s ORDER BY ts";
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.Parameters.AddWithValue("$s", since.ToUnixTimeSeconds());
            using var r = cmd.ExecuteReader();
            var list = new List<HistorySample>();
            while (r.Read())
                list.Add(new HistorySample(
                    DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)),
                    r.GetInt64(1) == 1,
                    r.GetInt64(2) == 1,
                    Enum.TryParse<AcMode>(r.GetString(3), out var m) ? m : AcMode.Unknown,
                    r.IsDBNull(5) ? null : r.GetInt32(5),
                    r.IsDBNull(4) ? null : r.GetInt32(4)));
            return list;
        }
    }

    public IReadOnlyList<OutsideSample> Outside(DateTimeOffset since)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT ts,temp FROM fora WHERE ts>=$s ORDER BY ts";
            cmd.Parameters.AddWithValue("$s", since.ToUnixTimeSeconds());
            using var r = cmd.ExecuteReader();
            var list = new List<OutsideSample>();
            while (r.Read()) list.Add(new OutsideSample(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)), r.GetDouble(1)));
            return list;
        }
    }

    public IReadOnlyList<EventRecord> Events(DateTimeOffset since)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT ts,aparelho,tipo,detalhe FROM evento WHERE ts>=$s ORDER BY ts";
            cmd.Parameters.AddWithValue("$s", since.ToUnixTimeSeconds());
            using var r = cmd.ExecuteReader();
            var list = new List<EventRecord>();
            while (r.Read())
                list.Add(new EventRecord(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0)), r.GetString(1), r.GetString(2), r.GetString(3)));
            return list;
        }
    }

    /// <summary>Apaga dados mais antigos que a retenção (padrão 90 dias).</summary>
    public void Prune(DateTimeOffset olderThan)
    {
        var ts = olderThan.ToUnixTimeSeconds();
        Exec($"DELETE FROM leitura WHERE ts<{ts}; DELETE FROM fora WHERE ts<{ts}; DELETE FROM evento WHERE ts<{ts};");
    }

    private void Exec(string sql)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _db.Dispose();
}
