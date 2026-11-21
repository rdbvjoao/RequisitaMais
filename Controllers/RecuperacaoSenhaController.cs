using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RequisitaPlus.Web.Models;
using RequisitaPlus.Web.Services;
namespace RequisitaPlus.Web.Controllers;
[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecuperacaoSenhaController(RecuperacaoSenhaService service, IWebHostEnvironment environment) : Controller
{
    private const string Cookie = "RequisitaPlus.Recuperacao";
    private string? Sessao => Request.Cookies[Cookie];
    private CookieOptions OpcoesCookie => new()
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true,
        Secure = !environment.IsDevelopment() || Request.IsHttps,
        Path = "/RecuperacaoSenha", MaxAge = TimeSpan.FromMinutes(5)
    };
    [HttpGet]
    public IActionResult Solicitar() => View(new SolicitarRecuperacaoViewModel());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Solicitar(SolicitarRecuperacaoViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var handle = await service.SolicitarAsync(model.Email, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local");
        if (handle != null) Response.Cookies.Append(Cookie, handle, OpcoesCookie);
        return RedirectToAction(nameof(Codigo));
    }
    [HttpGet]
    public IActionResult Codigo() => View(new ValidarCodigoViewModel());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Codigo(ValidarCodigoViewModel model)
    {
        var valido = await service.ValidarAsync(Sessao, model.Codigo ?? "");
        if (ModelState.IsValid && valido) return RedirectToAction(nameof(NovaSenha));
        ModelState.AddModelError("", RecuperacaoSenhaService.ErroCodigo);
        return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> NovaSenha() => await service.AutorizadoAsync(Sessao)
        ? View(new NovaSenhaViewModel()) : RedirectToAction(nameof(Solicitar));
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> NovaSenha(NovaSenhaViewModel model)
    {
        if (!await service.AutorizadoAsync(Sessao)) return RedirectToAction(nameof(Solicitar));
        if (!ModelState.IsValid) return View(model);
        var result = await service.RedefinirAsync(Sessao, model.Senha);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors.DistinctBy(e => e.Description)) ModelState.AddModelError("", error.Description);
            return View(model);
        }
        Response.Cookies.Delete(Cookie, OpcoesCookie);
        return RedirectToAction(nameof(Concluida));
    }
    [HttpGet]
    public IActionResult Concluida() => View();
}
