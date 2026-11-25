using System.Text;
using System.Text.Json;
using LibrarySeed;
using Microsoft.EntityFrameworkCore;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Models;

Console.OutputEncoding = Encoding.UTF8;
try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("LibrarySeed --database <caminho.db> [--dry-run | --execute --backup-dir <diretório>]\n" +
            "LibrarySeed --validate-catalog\nInforme --owner-id <id-explícito> com --database. Padrão: simulação. A meta é 100 requisitos do proprietário.");
        return 0;
    }
    if (args.SequenceEqual(new[] { "--validate-catalog" }))
    {
        SelfCheck.Run();
        return 0;
    }
    var execute = false;
    var dryRun = false;
    string? database = null, backupRoot = null, ownerId = null;
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        var option = args[i];
        if (!seen.Add(option)) throw new ArgumentException($"Opção repetida: {option}");
        switch (option)
        {
            case "--execute": execute = true; break;
            case "--dry-run": dryRun = true; break;
            case "--database": database = Value(); break;
            case "--backup-dir": backupRoot = Value(); break;
            case "--owner-id": ownerId = Value(); break;
            default: throw new ArgumentException($"Opção desconhecida: {option}");
        }
        string Value()
        {
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Falta valor para {option}.");
            return args[i];
        }
    }
    if (execute && dryRun) throw new ArgumentException("Escolha apenas um modo.");
    if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("Informe --database explicitamente.");
    if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Informe --owner-id explicitamente. Nenhum proprietário será escolhido automaticamente.");
    if (execute && string.IsNullOrWhiteSpace(backupRoot)) throw new ArgumentException("A execução exige --backup-dir.");
    if (!execute && backupRoot is not null) throw new ArgumentException("A simulação não cria backup; remova --backup-dir.");
    var path = Path.GetFullPath(database);
    if (!File.Exists(path)) throw new FileNotFoundException("Banco inexistente; não será criado.", path);
    var catalog = Catalog.Create();
    Console.WriteLine($"Banco: {path}\nModo: {(execute ? "EXECUÇÃO" : "SIMULAÇÃO (sem inserções)")}");
    using var connection = DatabaseSafety.Open(path, readOnly: !execute);
    using var context = new RequisitosContext(new DbContextOptionsBuilder<RequisitosContext>().UseSqlite(connection).Options);
    if (!context.Users.AsNoTracking().Any(u => u.Id == ownerId))
        throw new ArgumentException("O proprietário explicitamente informado não existe.");
    if (context.Requisitos.AsNoTracking().Any(r => r.ProprietarioId == null))
        throw new InvalidOperationException("Existem requisitos sem proprietário; conclua a associação autorizada antes da carga.");
    if (!execute)
    {
        // Transação de leitura: todas as consultas enxergam o mesmo estado.
        using var readTransaction = connection.BeginTransaction(deferred: true);
        context.Database.UseTransaction(readTransaction);
        DatabaseSafety.Check(connection, readTransaction);
        var plan = SeedPlan.Create(context.Requisitos.AsNoTracking().Where(r => r.ProprietarioId == ownerId).OrderBy(r => r.Id).ToList(), catalog);
        Print(plan);
        Console.WriteLine("Simulação concluída. Nenhum backup, manifesto ou registro foi gravado.");
        return 0;
    }

    // BEGIN IMMEDIATE antes da contagem: uma segunda carga espera ou falha sem duplicar.
    using var transaction = connection.BeginTransaction(deferred: false);
    context.Database.UseTransaction(transaction);
    DatabaseSafety.Check(connection, transaction);
    var existing = context.Requisitos.AsNoTracking().Where(r => r.ProprietarioId == ownerId).OrderBy(r => r.Id).ToList();
    var actualPlan = SeedPlan.Create(existing, catalog);
    Print(actualPlan);
    if (actualPlan.Candidates.Count == 0)
    {
        Console.WriteLine("Meta já atingida ou ultrapassada. Nenhuma inserção ou exclusão realizada.");
        return 0;
    }
    var baseline = DatabaseSafety.Snapshot(connection, transaction);
    var allOriginalIds = context.Requisitos.AsNoTracking().Select(r => r.Id).ToArray();
    var runDirectory = Path.Combine(Path.GetFullPath(backupRoot!),
        $"library-v1-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
    var backup = DatabaseSafety.Backup(path, runDirectory, baseline);
    Console.WriteLine($"Backup consistente e validado: {backup}");
    var now = DateTime.Now;
    foreach (var item in actualPlan.Candidates)
    {
        item.DataCriacao = now;
        item.ProprietarioId = ownerId;
        Catalog.Validate(item);
    }
    context.Requisitos.AddRange(actualPlan.Candidates);
    if (context.ChangeTracker.Entries().Any(e => e.State != EntityState.Added || e.Entity is not Requisito))
        throw new InvalidOperationException("Somente novos requisitos podem ser gravados.");
    context.SaveChanges();
    var insertedIds = actualPlan.Candidates.Select(r => r.Id).ToArray();
    var persisted = context.Requisitos.AsNoTracking().Where(r => insertedIds.Contains(r.Id)).ToList();
    if (context.Requisitos.Count(r => r.ProprietarioId == ownerId) != SeedPlan.Target || persisted.Count != actualPlan.Candidates.Count ||
        persisted.Any(r => r.Status != StatusRequisito.Pendente || r.ProprietarioId != ownerId) ||
        persisted.Any(r => !actualPlan.Candidates.Any(expected =>
            expected.Id == r.Id && expected.Titulo == r.Titulo && expected.Descricao == r.Descricao &&
            expected.Tipo == r.Tipo && expected.DataCriacao == r.DataCriacao)))
        throw new InvalidOperationException("Falha na contagem final ou no status dos novos requisitos.");
    DatabaseSafety.Check(connection, transaction);
    DatabaseSafety.AssertPreserved(baseline, DatabaseSafety.Snapshot(connection, transaction, allOriginalIds));
    if (context.AnalisesRequisitos.Any(a => insertedIds.Contains(a.RequisitoId)))
        throw new InvalidOperationException("Os novos requisitos não devem ter análises.");
    var manifest = Path.Combine(runDirectory, "manifest.json");
    string Manifest(string state) => JsonSerializer.Serialize(new
    {
        State = state, Catalog = "TESTE-BIB-V1", Database = path, Backup = backup, OwnerId = ownerId,
        CreatedAt = DateTimeOffset.Now, Before = actualPlan.Before, After = SeedPlan.Target,
        Inserted = actualPlan.Candidates.Select(r => new { Code = SeedPlan.Code(r), r.Id, r.Titulo, r.Tipo, r.Status, r.DataCriacao })
    }, new JsonSerializerOptions { WriteIndented = true });
    // Se não for possível registrar os IDs, a transação não será confirmada.
    File.WriteAllText(manifest, Manifest("Prepared: confirmar resultado no banco; a transação pode não ter sido confirmada"));
    transaction.Commit();
    Console.WriteLine($"Carga confirmada: {actualPlan.Candidates.Count} inseridos; total 100 para o proprietário. Manifesto: {manifest}");
    try { File.WriteAllText(manifest, Manifest("Committed")); }
    catch (IOException) { Console.Error.WriteLine("A carga foi CONFIRMADA, mas o manifesto permanece Prepared. Confira os IDs no banco."); }
    catch (UnauthorizedAccessException) { Console.Error.WriteLine("A carga foi CONFIRMADA, mas não foi possível atualizar o estado do manifesto."); }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Falha: {error.Message}");
    return 1;
}

static void Print(SeedPlan plan)
{
    Console.WriteLine($"Existentes: {plan.Before}; meta: {SeedPlan.Target}; novos: {plan.Candidates.Count}.");
    foreach (var item in plan.Candidates) Console.WriteLine($"{item.Titulo} | {item.Tipo} | Pendente\n  {item.Descricao}");
}
