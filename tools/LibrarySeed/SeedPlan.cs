using System.Text.RegularExpressions;
using RequisitaPlus.Web.Models;

namespace LibrarySeed;

internal sealed record SeedPlan(int Before, List<Requisito> Candidates)
{
    public const int Target = 100;
    public static string Code(Requisito item) => item.Titulo[..18];

    public static SeedPlan Create(IReadOnlyList<Requisito> existing, List<Requisito> catalog)
    {
        var known = catalog.ToDictionary(Code, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in existing)
        {
            if (!item.Titulo.Contains("TESTE-BIB-V1", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(item.Titulo, @"^\[TESTE-BIB-V1-\d{3}\] ", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidOperationException($"Marcador V1 inválido no requisito {item.Id}.");
            var code = Code(item);
            if (!known.TryGetValue(code, out var expected) || !used.Add(code))
                throw new InvalidOperationException($"Código desconhecido ou duplicado: {code}.");
            // Revisões podem alterar status e data; conteúdo divergente exige intervenção explícita.
            if (item.Titulo != expected.Titulo || item.Descricao != expected.Descricao || item.Tipo != expected.Tipo)
                throw new InvalidOperationException($"Conteúdo divergente para {code}. Nenhum dado será sobrescrito.");
        }
        var needed = Math.Max(0, Target - existing.Count);
        var candidates = catalog.Where(r => !used.Contains(Code(r))).Take(needed).ToList();
        if (candidates.Count != needed) throw new InvalidOperationException("Catálogo insuficiente para completar a meta.");
        foreach (var item in candidates) Catalog.Validate(item);
        return new SeedPlan(existing.Count, candidates);
    }
}
