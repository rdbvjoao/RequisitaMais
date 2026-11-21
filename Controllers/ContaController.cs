using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using RequisitaPlus.Web.Models;
namespace RequisitaPlus.Web.Controllers;
public class ContaController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Cadastro(string? returnUrl) => View(new CadastroViewModel { ReturnUrl = returnUrl });
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cadastro(CadastroViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var email = model.Email.Trim();
        var user = new IdentityUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, model.Senha);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors.DistinctBy(e => e.Description)) ModelState.AddModelError("", error.Description);
            return View(model);
        }
        await signIn.SignInAsync(user, isPersistent: false);
        return Destino(model.ReturnUrl);
    }
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl) => View(new LoginViewModel { ReturnUrl = returnUrl });
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByEmailAsync(model.Email.Trim());
        var result = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.PasswordSignInAsync(user, model.Senha, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded) return Destino(model.ReturnUrl);
        ModelState.AddModelError("", result.IsLockedOut ? "Muitas tentativas de login. Aguarde 5 minutos e tente novamente." : "E-mail ou senha inválidos.");
        return View(model);
    }
    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
    private IActionResult Destino(string? returnUrl) => Url.IsLocalUrl(returnUrl)
        ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
}
