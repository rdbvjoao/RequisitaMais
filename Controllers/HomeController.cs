using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Models;

namespace RequisitaPlus.Web.Controllers;

public class HomeController(RequisitosContext context) : Controller
{
    [Authorize]
    [HttpGet("/")]
    [HttpGet("/Home/Index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(usuarioId)) return Challenge();
        var requisitos = context.Requisitos.AsNoTracking()
            .Where(r => r.ProprietarioId != null && r.ProprietarioId == usuarioId);
        var totais = await requisitos.GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Quantidade = g.Count() })
            .ToListAsync(cancellationToken);
        return View(new DashboardViewModel
        {
            Total = totais.Sum(g => g.Quantidade),
            Pendentes = totais.Where(g => g.Status == StatusRequisito.Pendente).Sum(g => g.Quantidade),
            EmAnalise = totais.Where(g => g.Status == StatusRequisito.EmAnalise).Sum(g => g.Quantidade),
            Revisados = totais.Where(g => g.Status == StatusRequisito.Revisado).Sum(g => g.Quantidade),
            Recentes = await requisitos.OrderByDescending(r => r.DataCriacao)
                .ThenByDescending(r => r.Id).Take(5).ToListAsync(cancellationToken)
        });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Ajuda() => View();

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
