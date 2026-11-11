using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace FocusLog.Core;

/// <summary>Local SQLite storage (WAL, synchronous=NORMAL). One long-lived connection, used from one thread.</summary>
public sealed class Database : ISessionStore, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly Dictionary<string, long> _appIds = new();
    private HashSet<string> _study = new(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; }

    public Database(string path)
    {
        FilePath = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        _conn = new SqliteConnection(cs);
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Exec("PRAGMA busy_timeout=3000;");
        Exec(@"CREATE TABLE IF NOT EXISTS apps(
                 id INTEGER PRIMARY KEY,
                 app_key TEXT NOT NULL UNIQUE,
                 name TEXT NOT NULL,
                 exe TEXT NOT NULL,
                 path TEXT NOT NULL DEFAULT '',
                 study INTEGER NOT NULL DEFAULT 0);");
        Exec(@"CREATE TABLE IF NOT EXISTS sessions(
                 id INTEGER PRIMARY KEY,
                 app_id INTEGER NOT NULL REFERENCES apps(id),
                 start_ms INTEGER NOT NULL,
                 dur_ms INTEGER NOT NULL,
                 local_date TEXT NOT NULL,
                 cont INTEGER NOT NULL DEFAULT 0,
                 open INTEGER NOT NULL DEFAULT 0);");
        Exec("CREATE INDEX IF NOT EXISTS ix_sessions_date ON sessions(local_date);");
        Exec("CREATE INDEX IF NOT EXISTS ix_sessions_app ON sessions(app_id);");
        // Crash recovery: a session still flagged open was cut off. Its duration is the last checkpoint.
        Exec("UPDATE sessions SET open=0 WHERE open=1;");
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void SetStudyKeys(IEnumerable<string> keys)
    {
        _study = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        Exec("UPDATE apps SET study=0;");
        foreach (var k in _study)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE apps SET study=1 WHERE app_key=$k;";
            cmd.Parameters.AddWithValue("$k", k.ToLowerInvariant());
            cmd.ExecuteNonQuery();
        }
    }

    private long EnsureAppId(AppInfo a)
    {
        if (_appIds.TryGetValue(a.Key, out var cached)) return cached;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO apps(app_key,name,exe,path,study) VALUES($k,$n,$e,$p,$s)
                                ON CONFLICT(app_key) DO UPDATE SET name=excluded.name, exe=excluded.exe, path=excluded.path;";
            cmd.Parameters.AddWithValue("$k", a.Key);
            cmd.Parameters.AddWithValue("$n", a.Name);
            cmd.Parameters.AddWithValue("$e", a.Exe);
            cmd.Parameters.AddWithValue("$p", a.Path ?? "");
            cmd.Parameters.AddWithValue("$s", _study.Contains(a.Key) ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        using var q = _conn.CreateCommand();
        q.CommandText = "SELECT id FROM apps WHERE app_key=$k;";
        q.Parameters.AddWithValue("$k", a.Key);
        long id = Convert.ToInt64(q.ExecuteScalar(), CultureInfo.InvariantCulture);
        _appIds[a.Key] = id;
        return id;
    }

    public long Insert(AppInfo app, long startUtcMs, string localDate, bool continuation, long durMs, bool open)
    {
        long appId = EnsureAppId(app);
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO sessions(app_id,start_ms,dur_ms,local_date,cont,open) VALUES($a,$s,$d,$l,$c,$o);";
            cmd.Parameters.AddWithValue("$a", appId);
            cmd.Parameters.AddWithValue("$s", startUtcMs);
            cmd.Parameters.AddWithValue("$d", durMs);
            cmd.Parameters.AddWithValue("$l", localDate);
            cmd.Parameters.AddWithValue("$c", continuation ? 1 : 0);
            cmd.Parameters.AddWithValue("$o", open ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        using var q = _conn.CreateCommand();
        q.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(q.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Update(long id, long durMs, bool open)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET dur_ms=$d, open=$o WHERE id=$i;";
        cmd.Parameters.AddWithValue("$d", durMs);
        cmd.Parameters.AddWithValue("$o", open ? 1 : 0);
        cmd.Parameters.AddWithValue("$i", id);
        cmd.ExecuteNonQuery();
    }

    // ---- reporting -------------------------------------------------------------------------

    public List<SessionRecord> GetSessions(DateOnly from, DateOnly to)
    {
        var list = new List<SessionRecord>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT s.id, a.app_key, a.name, a.exe, a.path, s.start_ms, s.dur_ms, s.cont, a.study
                            FROM sessions s JOIN apps a ON a.id = s.app_id
                            WHERE s.local_date >= $f AND s.local_date <= $t
                            ORDER BY s.start_ms;";
        cmd.Parameters.AddWithValue("$f", ReportMath.Key(from));
        cmd.Parameters.AddWithValue("$t", ReportMath.Key(to));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(5)).LocalDateTime;
            long dur = r.GetInt64(6);
            list.Add(new SessionRecord
            {
                Id = r.GetInt64(0),
                AppKey = r.GetString(1),
                AppName = r.GetString(2),
                Exe = r.GetString(3),
                Path = r.GetString(4),
                Start = start,
                End = start.AddMilliseconds(dur),
                DurMs = dur,
                Continuation = r.GetInt32(7) != 0,
                IsStudy = r.GetInt32(8) != 0
            });
        }
        return list;
    }

    public List<DayTotal> GetDayTotals(DateOnly from, DateOnly to)
    {
        var list = new List<DayTotal>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT s.local_date, SUM(s.dur_ms), SUM(CASE WHEN a.study=1 THEN s.dur_ms ELSE 0 END)
                            FROM sessions s JOIN apps a ON a.id = s.app_id
                            WHERE s.local_date >= $f AND s.local_date <= $t
                            GROUP BY s.local_date ORDER BY s.local_date;";
        cmd.Parameters.AddWithValue("$f", ReportMath.Key(from));
        cmd.Parameters.AddWithValue("$t", ReportMath.Key(to));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DayTotal
            {
                Date = DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                TotalMs = r.GetInt64(1),
                StudyMs = r.GetInt64(2)
            });
        }
        return list;
    }

    public List<KnownApp> GetKnownApps()
    {
        var list = new List<KnownApp>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT app_key, name, exe, path, study FROM apps WHERE app_key NOT LIKE 'site:%' ORDER BY name COLLATE NOCASE;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new KnownApp(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) != 0));
        return list;
    }

    // ---- data management -------------------------------------------------------------------

    public void ExportCsv(string path, DateOnly from, DateOnly to)
    {
        var sb = new StringBuilder();
        sb.AppendLine("date,start,end,duration_seconds,application,executable,study");
        foreach (var s in GetSessions(from, to))
        {
            sb.Append(s.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.End.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
              .Append((s.DurMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(s.AppName)).Append(',').Append(Csv(s.Exe)).Append(',')
              .AppendLine(s.IsStudy ? "yes" : "no");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string v) =>
        v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    public void Backup(string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "VACUUM INTO '" + destination.Replace("'", "''") + "';";
        cmd.ExecuteNonQuery();
    }

    public void ClearAll()
    {
        Exec("DELETE FROM sessions;");
        Exec("DELETE FROM apps;");
        _appIds.Clear();
        Exec("VACUUM;");
    }

    public void Dispose() => _conn.Dispose();
}
