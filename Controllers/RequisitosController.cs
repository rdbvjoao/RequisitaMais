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
    [HttpGet("/Requisitos")]
    [HttpGet("/Requisitos/Index")]
    public async Task<IActionResult> Index(TipoRequisito? tipo, StatusRequisito? status, string? titulo = null)
    {
        titulo = titulo?.Trim();
        ViewData["Tipo"] = tipo;
        ViewData["Status"] = status;
        ViewData["Titulo"] = titulo;
        var query = context.Requisitos.AsNoTracking();
        if (tipo.HasValue) query = query.Where(r => r.Tipo == tipo);
        if (status.HasValue) query = query.Where(r => r.Status == status);
        var requisitos = await query.OrderByDescending(r => r.DataCriacao).ThenByDescending(r => r.Id).ToListAsync();
        if (!string.IsNullOrEmpty(titulo))
            requisitos = requisitos.Where(r => r.Titulo.Contains(titulo, StringComparison.OrdinalIgnoreCase)).ToList();
        return View(requisitos);
    }

    public async Task<IActionResult> Details(int id)
    {
        var requisito = await context.Requisitos.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return requisito is null ? NaoEncontrado() : View(requisito);
    }

    public IActionResult Create() => View(new Requisito());

    [HttpGet]
    public async Task<IActionResult> Historico(int id, CancellationToken cancellationToken)
    {
        var requisito = await context.Requisitos.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (requisito is null) return NaoEncontrado();

        var analises = await context.AnalisesRequisitos.AsNoTracking().Include(a => a.Decisoes)
            .Where(a => a.RequisitoId == id)
            .OrderByDescending(a => a.DataAnalise).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
        return View(new HistoricoAnalisesViewModel { Requisito = requisito, Analises = analises });
    }

    private static string Versao(Requisito requisito) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { requisito.Titulo, requisito.Descricao, requisito.Tipo, requisito.Status }))));

    private Task<int> UltimaAnalise(int requisitoId, CancellationToken ct) => context.AnalisesRequisitos
        .Where(a => a.RequisitoId == requisitoId).OrderByDescending(a => a.DataAnalise)
        .ThenByDescending(a => a.Id).Select(a => a.Id).FirstOrDefaultAsync(ct);

    [HttpGet]
    public async Task<IActionResult> Revisar(int id, int indice, CancellationToken cancellationToken)
    {
        var analise = await context.AnalisesRequisitos.AsNoTracking().Include(a => a.Requisito)
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
        var analise = await context.AnalisesRequisitos.Include(a => a.Requisito).Include(a => a.Decisoes)
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
        if (decisao.RedacaoFinal is not null) analise.Requisito.Descricao = decisao.RedacaoFinal;
        if (revisaoConcluida)
            analise.Requisito.Status = StatusRequisito.Revisado;
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
        TempData["Sucesso"] = "Decisão registrada com sucesso.";
        return RedirectToAction(nameof(Historico), new { id = analise.RequisitoId });
    }

    [HttpGet]
    public async Task<IActionResult> Analisar(int id, CancellationToken cancellationToken)
    {
        var requisito = await context.Requisitos.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return requisito is null ? NaoEncontrado() : View(new AnaliseRequisitoViewModel { Requisito = requisito });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Analisar(int id, [FromServices] AnaliseRequisitoService analise,
        CancellationToken cancellationToken)
    {
        var requisito = await context.Requisitos.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (requisito is null) return NaoEncontrado();

        try
        {
            var resultado = await analise.AnalisarAsync(requisito, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
            return View(new AnaliseRequisitoViewModel { Requisito = requisito, Resultado = resultado });
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
    public async Task<IActionResult> Create([Bind("Titulo,Descricao,Tipo,Status")] Requisito requisito)
    {
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
        var requisito = await context.Requisitos.FindAsync(id);
        return requisito is null ? NaoEncontrado() : View(requisito);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Titulo,Descricao,Tipo,Status")] Requisito dados)
    {
        var requisito = await context.Requisitos.FindAsync(id);
        if (requisito is null) return NaoEncontrado();
        if (dados.Status == StatusRequisito.Revisado)
        {
            var ultima = await context.AnalisesRequisitos.AsNoTracking().Include(a => a.Decisoes)
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
        var requisito = await context.Requisitos.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return requisito is null ? NaoEncontrado() : View(requisito);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var requisito = await context.Requisitos.FindAsync(id);
        if (requisito is null) return NaoEncontrado();
        if (await context.AnalisesRequisitos.AnyAsync(a => a.RequisitoId == id))
        {
            TempData["Erro"] = "Este requisito possui histórico de análises e não pode ser excluído, para preservar o histórico original.";
            return RedirectToAction(nameof(Details), new { id });
        }
        context.Remove(requisito);
        if (!await Salvar()) return View("Delete", requisito);
        TempData["Sucesso"] = "Requisito excluído com sucesso.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult NaoEncontrado()
    {
        TempData["Erro"] = "Requisito não encontrado. Ele pode ter sido excluído.";
        return RedirectToAction(nameof(Index));
    }

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
