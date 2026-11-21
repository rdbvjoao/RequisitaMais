using System.ComponentModel.DataAnnotations;
using RequisitaPlus.Web.Models;

namespace LibrarySeed;

internal static class SelfCheck
{
    // Verificações puras: não abrem SQLite nem criam arquivos.
    public static void Run()
    {
        var checks = 0;
        void Check(bool condition)
        {
            if (!condition) throw new InvalidOperationException($"Verificação {checks + 1} falhou.");
            checks++;
        }
        void Reject(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is InvalidOperationException or ValidationException) { checks++; return; }
            throw new InvalidOperationException("Era esperada a rejeição de dados inválidos.");
        }
        var catalog = Catalog.Create();
        Check(catalog.Count == 100);
        Check(catalog.Count(r => r.Tipo == TipoRequisito.Funcional) == 70);
        Check(catalog.Count(r => r.Tipo == TipoRequisito.NaoFuncional) == 30);
        Check(catalog.All(r => r.Status == StatusRequisito.Pendente && r.Id == 0));
        Check(catalog.Select(SeedPlan.Code).SequenceEqual(Enumerable.Range(1, 100).Select(i => $"[TESTE-BIB-V1-{i:000}]")));
        Check(catalog.Select(r => (r.Titulo, r.Descricao, r.Tipo)).SequenceEqual(Catalog.Create().Select(r => (r.Titulo, r.Descricao, r.Tipo))));
        Check(catalog.Select(r => r.Descricao).Distinct().Count() == 100);
        Check(SeedPlan.Create([], catalog).Candidates.Count == 100);
        List<Requisito> Originals(int count) => Enumerable.Range(1, count)
            .Select(i => new Requisito { Id = i, Titulo = $"Original {i}" }).ToList();
        var originals = Originals(6);
        var plan = SeedPlan.Create(originals, catalog);
        Check(plan.Before == 6 && plan.Candidates.Count == 94);
        Check(plan.Candidates.Count(r => r.Tipo == TipoRequisito.Funcional) == 70);
        Check(plan.Candidates.Count(r => r.Tipo == TipoRequisito.NaoFuncional) == 24);
        Check(originals.All(r => r.Titulo.StartsWith("Original ", StringComparison.Ordinal)));
        Check(SeedPlan.Create(originals.Concat(plan.Candidates).ToList(), catalog).Candidates.Count == 0);
        Check(SeedPlan.Create(Originals(100), catalog).Candidates.Count == 0);
        Check(SeedPlan.Create(Originals(101), catalog).Candidates.Count == 0);
        Check(SeedPlan.Create(originals.Concat(catalog.Take(20)).ToList(), catalog).Candidates.First().Titulo == catalog[20].Titulo);
        Reject(() => SeedPlan.Create([catalog[0], catalog[0]], catalog));
        var conflicting = Catalog.Create()[0];
        conflicting.Descricao = "Editado posteriormente";
        Reject(() => SeedPlan.Create([conflicting], catalog));
        Reject(() => SeedPlan.Create([new Requisito { Titulo = "[TESTE-BIB-V1-999] Inválido" }], catalog));
        Reject(() => SeedPlan.Create([new Requisito { Titulo = "[TESTE-BIB-V1-x] Inválido" }], catalog));
        Invalid(r => r.Titulo = " ");
        Invalid(r => r.Titulo = new string('x', 151));
        Invalid(r => r.Descricao = "");
        Invalid(r => r.Descricao = new string('x', 5001));
        Invalid(r => r.Tipo = null);
        Invalid(r => r.Tipo = (TipoRequisito)99);
        Invalid(r => r.Status = null);
        Invalid(r => r.Status = (StatusRequisito)99);
        Invalid(r => r.Status = StatusRequisito.Revisado);
        Invalid(r => r.Id = 1);
        Invalid(r => r.DataCriacao = default);
        Invalid(r => r.Titulo += " ");
        var allowed = Catalog.Create()[0];
        allowed.Titulo = new string('t', 150);
        allowed.Descricao = new string('d', 5000);
        Catalog.Validate(allowed);
        Check(true);
        Console.WriteLine($"Catálogo V1 válido: 100 itens (70 funcionais e 30 não funcionais). {checks} verificações aprovadas, sem acessar SQLite.");

        void Invalid(Action<Requisito> change)
        {
            var item = Catalog.Create()[0];
            change(item);
            Reject(() => Catalog.Validate(item));
        }
    }
}
