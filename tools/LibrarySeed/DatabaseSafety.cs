using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LibrarySeed;

internal static class DatabaseSafety
{
    public static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString());
        try
        {
            connection.Open();
            if (readOnly) Execute(connection, "PRAGMA query_only=ON");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static void Check(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA integrity_check";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw new InvalidOperationException("Falha na integridade do SQLite.");
        }
        command.CommandText = "PRAGMA foreign_key_check";
        using var foreignKeys = command.ExecuteReader();
        if (foreignKeys.Read()) throw new InvalidOperationException("Existem violações de chaves estrangeiras.");
    }

    // Valores de todas as tabelas preexistentes, inclusive autenticação, ficam somente em memória.
    // sqlite_sequence é interna: a sequência de Requisitos necessariamente avança na carga.
    public static Dictionary<string, string[]> Snapshot(SqliteConnection connection,
        SqliteTransaction? transaction = null, IReadOnlyList<int>? originalIds = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var tables = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var filter = table == "Requisitos" && originalIds is not null
                ? originalIds.Count == 0 ? " WHERE 0" : " WHERE Id IN (" + string.Join(',', originalIds) + ")"
                : "";
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"" + filter;
            var rows = new List<string>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var values = new string?[reader.FieldCount];
                    for (var i = 0; i < values.Length; i++)
                        values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i) is byte[] bytes
                            ? "blob:" + Convert.ToHexString(bytes)
                            : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    rows.Add(JsonSerializer.Serialize(values));
                }
            }
            result[table] = rows.Order(StringComparer.Ordinal).ToArray();
        }
        return result;
    }

    public static void AssertPreserved(Dictionary<string, string[]> before, Dictionary<string, string[]> after)
    {
        if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var rows) ||
            !pair.Value.SequenceEqual(rows, StringComparer.Ordinal)))
            throw new InvalidOperationException("A verificação de preservação dos dados preexistentes falhou.");
    }

    public static string Backup(string sourcePath, string directory, Dictionary<string, string[]> baseline)
    {
        Directory.CreateDirectory(directory);
        var backup = Path.Combine(directory, "requisita-plus.db");
        // Reserva exclusiva; nunca sobrescrever um backup anterior.
        using (new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using (var source = Open(sourcePath, readOnly: true))
        using (var destination = Open(backup, readOnly: false))
        {
            // Outra conexão lê o estado confirmado enquanto a transação IMMEDIATE impede escritores.
            source.BackupDatabase(destination);
            // Entregar um backup autossuficiente, sem depender de WAL/SHM no destino.
            Execute(destination, "PRAGMA journal_mode=DELETE");
            Check(destination);
            AssertPreserved(baseline, Snapshot(destination));
        }
        using var stream = File.OpenRead(backup);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        File.WriteAllText(backup + ".sha256", hash + Environment.NewLine);
        return backup;
    }
}
