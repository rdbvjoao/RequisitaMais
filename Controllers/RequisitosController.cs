using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Models;
using RequisitaPlus.Web.Services;

namespace RequisitaPlus.Web.Controllers;

[Authorize]
public class RequisitosController(RequisitosContext context, ILogger<RequisitosController> logger) : Controller
{
    private string? UsuarioId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private IQueryable<Requisito> MeusRequisitos => context.Requisitos
        .Where(r => UsuarioId != null && r.ProprietarioId != null && r.ProprietarioId == UsuarioId);
    private IQueryable<AnaliseRequisito> MinhasAnalises => context.AnalisesRequisitos
        .Where(a => UsuarioId != null && a.Requisito.ProprietarioId != null && a.Requisito.ProprietarioId == UsuarioId);

    [HttpGet("/Requisitos")]
    [HttpGet("/Requisitos/Index")]
    public async Task<IActionResult> Index(TipoRequisito? tipo, StatusRequisito? status, string? titulo = null)
    {
        titulo = titulo?.Trim();
        ViewData["Tipo"] = tipo;
        ViewData["Status"] = status;
        ViewData["Titulo"] = titulo;
        var query = MeusRequisitos.AsNoTracking();
        if (tipo.HasValue) query = query.Where(r => r.Tipo == tipo);
        if (status.HasValue) query = query.Where(r => r.Status == status);
        var requisitos = await query.OrderByDescending(r => r.DataCriacao).ThenByDescending(r => r.Id).ToListAsync();
        if (!string.IsNullOrEmpty(titulo))
            requisitos = requisitos.Where(r => r.Titulo.Contains(titulo, StringComparison.OrdinalIgnoreCase)).ToList();
        return View(requisitos);
    }

    public async Task<IActionResult> Details(int id)
    {
        var requisito = await MeusRequisitos.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return requisito is null ? NaoEncontrado() : View(requisito);
    }

    public IActionResult Create() => View(new Requisito());

    [HttpGet]
    public async Task<IActionResult> Historico(int id, CancellationToken cancellationToken)
    {
        var requisito = await MeusRequisitos.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (requisito is null) return NaoEncontrado();

        var analises = await MinhasAnalises.AsNoTracking().Include(a => a.Decisoes)
            .Where(a => a.RequisitoId == id)
            .OrderByDescending(a => a.DataAnalise).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
        return View(new HistoricoAnalisesViewModel { Requisito = requisito, Analises = analises });
    }

    private static string Versao(Requisito requisito) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { requisito.Titulo, requisito.Descricao, requisito.Tipo, requisito.Status, requisito.VersaoConcorrencia }))));

    private Task<int> UltimaAnalise(int requisitoId, CancellationToken ct) => MinhasAnalises
        .Where(a => a.RequisitoId == requisitoId).OrderByDescending(a => a.DataAnalise)
        .ThenByDescending(a => a.Id).Select(a => a.Id).FirstOrDefaultAsync(ct);

    [HttpGet]
    public async Task<IActionResult> Revisar(int id, int indice, CancellationToken cancellationToken)
    {
        var analise = await MinhasAnalises.AsNoTracking().Include(a => a.Requisito)
            .Include(a => a.Decisoes).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (analise is null) return NotFound();
        var problemas = analise.ObterResultado().Problemas;
        if (indice < 0 || indice >= problemas.Count) return NotFound();
        if (id != await UltimaAnalise(analise.RequisitoId, cancellationToken) ||
            analise.Decisoes.Any(d => d.IndiceSugestao == indice))
            return RedirectToAction(nameof(Historico), new { id = analise.RequisitoId });
        return View(new RevisarSugestaoViewModel
        {
            AnaliseId = id, IndiceSugestao = indice, Requisito = analise.Requisito,
            Sugestao = problemas[indice], VersaoRequisito = Versao(analise.Requisito)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revisar(
        [Bind("AnaliseId,IndiceSugestao,VersaoRequisito,Decisao,RedacaoFinal,Confirmacao")] RevisarSugestaoViewModel dados,
        CancellationToken cancellationToken)
    {
        // A decisão, o texto e o status são gravados juntos. A transação serializa revisões concorrentes no SQLite.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var analise = await MinhasAnalises.Include(a => a.Requisito).Include(a => a.Decisoes)
            .FirstOrDefaultAsync(a => a.Id == dados.AnaliseId, cancellationToken);
        if (analise is null) return NotFound();
        var problemas = analise.ObterResultado().Problemas;
        if (dados.IndiceSugestao < 0 || dados.IndiceSugestao >= problemas.Count) return NotFound();
        if (analise.Id != await UltimaAnalise(analise.RequisitoId, cancellationToken) ||
            analise.Decisoes.Any(d => d.IndiceSugestao == dados.IndiceSugestao) ||
            dados.VersaoRequisito != Versao(analise.Requisito))
        {
            TempData["Erro"] = "O requisito, a análise ou a decisão mudou. Consulte o histórico e abra novamente a revisão.";
            return RedirectToAction(nameof(Historico), new { id = analise.RequisitoId });
        }
        dados.Requisito = analise.Requisito;
        dados.Sugestao = problemas[dados.IndiceSugestao];
        if (!dados.Confirmacao)
            ModelState.AddModelError(nameof(dados.Confirmacao), "Confirme explicitamente a decisão e, quando aplicável, a redação final.");
        if (dados.Decisao is TipoDecisaoSugestao.Aceitar or TipoDecisaoSugestao.Ajustar &&
            string.IsNullOrWhiteSpace(dados.RedacaoFinal))
            ModelState.AddModelError(nameof(dados.RedacaoFinal), "Informe a redação final completa, validada por você.");
        var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(usuarioId)) return Challenge();
        if (!ModelState.IsValid) return View(dados);
        var decisao = new DecisaoSugestao
        {
            AnaliseRequisitoId = analise.Id, IndiceSugestao = dados.IndiceSugestao,
            Decisao = dados.Decisao!.Value, DataDecisao = DateTime.Now, UsuarioId = usuarioId,
            DescricaoAnterior = analise.Requisito.Descricao,
            RedacaoFinal = dados.Decisao == TipoDecisaoSugestao.Descartar ? null : dados.RedacaoFinal!.Trim()
        };
        var revisaoConcluida = analise.Decisoes.Count + 1 == problemas.Count;
        context.DecisoesSugestoes.Add(decisao);
        // Toda decisão invalida formulários e análises abertos anteriormente, mesmo sem alteração de texto.
        context.Entry(analise.Requisito).Property(r => r.VersaoConcorrencia).IsModified = true;
        if (decisao.RedacaoFinal is not null) analise.Requisito.Descricao = decisao.RedacaoFinal;
        analise.Requisito.Status = revisaoConcluida
            ? StatusRequisito.Revisado : StatusRequisito.EmAnalise;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Não foi possível registrar a revisão da análise {AnaliseId}.", analise.Id);
            ModelState.AddModelError("", "Não foi possível salvar a decisão. Reabra a revisão pelo histórico e tente novamente.");
            return View(dados);
        }
        TempData["Sucesso"] = revisaoConcluida
            ? "Todas as sugestões foram avaliadas. O requisito foi marcado automaticamente como Revisado."
            : "Decisão registrada com sucesso.";
        return RedirectToAction(nameof(Historico), new { id = analise.RequisitoId });
    }

    [HttpGet]
    public async Task<IActionResult> Analisar(int id, CancellationToken cancellationToken)
    {
        var requisito = await MeusRequisitos.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return requisito is null ? NaoEncontrado() : View(new AnaliseRequisitoViewModel { Requisito = requisito });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Analisar(int id, [FromServices] AnaliseRequisitoService analise,
        CancellationToken cancellationToken)
    {
        var requisito = await MeusRequisitos.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (requisito is null) return NaoEncontrado();

        try
        {
            var resultado = await analise.AnalisarAsync(requisito, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var atual = await MeusRequisitos.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (atual is null) return NotFound();
            if (atual.VersaoConcorrencia != requisito.VersaoConcorrencia)
                return Conflict("O requisito mudou durante a análise. Abra novamente e tente outra vez.");
            context.AnalisesRequisitos.Add(new AnaliseRequisito
            {
                RequisitoId = requisito.Id,
                DataAnalise = DateTime.Now,
                ResultadoJson = JsonSerializer.Serialize(resultado),
                TokensEntrada = resultado.UsoTokens?.Entrada,
                TokensSaida = resultado.UsoTokens?.Saida,
                TokensTotal = resultado.UsoTokens?.Total
            });
            await context.SaveChangesAsync(cancellationToken);
            atual.Status = StatusRequisito.EmAnalise;
            context.Entry(atual).Property(r => r.VersaoConcorrencia).IsModified = true;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return View(new AnaliseRequisitoViewModel { Requisito = atual, Resultado = resultado });
        }
        catch (AnaliseRequisitoException ex)
        {
            return View(new AnaliseRequisitoViewModel { Requisito = requisito, Erro = ex.Message });
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Não foi possível salvar o histórico da análise do requisito {RequisitoId}.", id);
            return View(new AnaliseRequisitoViewModel
            {
                Requisito = requisito,
                Erro = "A Gemini concluiu a análise, mas não foi possível salvá-la no histórico. Tente novamente mais tarde."
            });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Titulo,Descricao,Tipo")] Requisito requisito)
    {
        if (string.IsNullOrEmpty(UsuarioId)) return Challenge();
        requisito.ProprietarioId = UsuarioId;
        requisito.Status = StatusRequisito.Pendente;
        ModelState.Remove(nameof(Requisito.ProprietarioId));
        ModelState.Remove(nameof(Requisito.Status));
        if (!ModelState.IsValid) return View(requisito);
        requisito.Titulo = requisito.Titulo.Trim();
        requisito.Descricao = requisito.Descricao.Trim();
        requisito.DataCriacao = DateTime.Now;
        context.Add(requisito);
        if (!await Salvar()) return View(requisito);
        TempData["Sucesso"] = "Requisito cadastrado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var requisito = await MeusRequisitos.FirstOrDefaultAsync(r => r.Id == id);
        return requisito is null ? NaoEncontrado() : View(requisito);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Titulo,Descricao,Tipo,Status")] Requisito dados)
    {
        var requisito = await MeusRequisitos.FirstOrDefaultAsync(r => r.Id == id);
        if (requisito is null) return NaoEncontrado();
        if (!TokenDoFormularioCorresponde(requisito.VersaoConcorrencia, out var token))
            return Conflict("O requisito mudou. Reabra a edição.");
        dados.VersaoConcorrencia = token!;
        ModelState.Remove(nameof(Requisito.ProprietarioId));
        if (dados.Status == StatusRequisito.Revisado)
        {
            var ultima = await MinhasAnalises.AsNoTracking().Include(a => a.Decisoes)
                .Where(a => a.RequisitoId == id).OrderByDescending(a => a.DataAnalise)
                .ThenByDescending(a => a.Id).FirstOrDefaultAsync();
            if (ultima is not null && ultima.Decisoes.Count < ultima.ObterResultado().Problemas.Count)
                ModelState.AddModelError(nameof(dados.Status), "Decida sobre todas as sugestões da análise mais recente antes de marcar como Revisado.");
        }
        if (!ModelState.IsValid) { dados.Id = id; return View(dados); }
        requisito.Titulo = dados.Titulo.Trim();
        requisito.Descricao = dados.Descricao.Trim();
        requisito.Tipo = dados.Tipo;
        requisito.Status = dados.Status;
        if (!await Salvar()) { dados.Id = id; return View(dados); }
        TempData["Sucesso"] = "Requisito atualizado com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var requisito = await MeusRequisitos.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return requisito is null ? NaoEncontrado() : View(requisito);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, string? versaoConcorrencia)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var requisito = await MeusRequisitos.FirstOrDefaultAsync(r => r.Id == id);
        if (requisito is null) return NaoEncontrado();
        if (!TokenDoFormularioCorresponde(requisito.VersaoConcorrencia, out _))
            return Conflict("O requisito mudou. Reabra a confirmação de exclusão.");
        if (await MinhasAnalises.AnyAsync(a => a.RequisitoId == id))
        {
            TempData["Erro"] = "Este requisito possui histórico de análises e não pode ser excluído, para preservar o histórico original.";
            return RedirectToAction(nameof(Details), new { id });
        }
        context.Remove(requisito);
        if (!await Salvar()) return View("Delete", requisito);
        await transaction.CommitAsync();
        TempData["Sucesso"] = "Requisito excluído com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    // O model binding converte o vazio legado em null. Leia somente o campo literal
    // do formulário, sem aceitar ausência, múltiplos valores ou normalização.
    private bool TokenDoFormularioCorresponde(string? armazenado, out string? token)
    {
        token = null;
        if (!Request.HasFormContentType ||
            !Request.Form.TryGetValue(nameof(Requisito.VersaoConcorrencia), out var valores) ||
            valores.Count != 1)
            return false;
        token = valores[0];
        return token is not null &&
            (token.Length == 0 || !string.IsNullOrWhiteSpace(token)) &&
            string.Equals(token, armazenado, StringComparison.Ordinal);
    }

    private IActionResult NaoEncontrado() => NotFound();

    private async Task<bool> Salvar()
    {
        try { await context.SaveChangesAsync(); return true; }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Não foi possível salvar o requisito.");
            ModelState.AddModelError("", "Não foi possível salvar a alteração. Tente novamente.");
            return false;
        }
    }
}
