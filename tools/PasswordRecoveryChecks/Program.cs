using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RequisitaPlus.Web.Controllers;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Models;
using RequisitaPlus.Web.Services;

// Não executa Program da aplicação, não carrega seus appsettings/User Secrets,
// não lê nenhum arquivo SQLite e não registra GmailEmailSender.
var testRoot = Path.Combine(Path.GetTempPath(), "RequisitaPlusRecovery-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = testRoot, Args = [] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Environment.EnvironmentName = "Development"; // cookies HTTP apenas neste host isolado
var clock = new TestClock();
var mail = new FakeEmail();
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
builder.Services.AddDbContext<RequisitosContext>(o => o.UseSqlite(connection));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddIdentityCore<IdentityUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; })
    .AddEntityFrameworkStores<RequisitosContext>().AddSignInManager().AddDefaultTokenProviders().AddErrorDescriber<ErrosIdentity>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<TimeProvider>(clock);
builder.Services.AddSingleton<IEmailSender>(mail);
builder.Services.AddSingleton<RecuperacaoSenhaStore>();
builder.Services.AddSingleton<RecuperacaoSenhaEntrega>();
builder.Services.AddScoped<RecuperacaoSenhaService>();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ContaController).Assembly);
await using var app = builder.Build();
app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Requisitos}/{action=Index}/{id?}");
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RequisitosContext>();
    await db.Database.EnsureCreatedAsync(); // somente conexão :memory: criada acima
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    foreach (var email in new[] { "alice@example.invalid", "bob@example.invalid" })
        if (!(await users.CreateAsync(new IdentityUser { UserName = email, Email = email }, "Anterior@123")).Succeeded) throw new Exception("Seed");
    db.Requisitos.Add(new Requisito { ProprietarioId = (await users.FindByEmailAsync("alice@example.invalid"))!.Id, Titulo = "Requisito sintético", Descricao = "Preservar este requisito sintético", Tipo = TipoRequisito.Funcional });
    await db.SaveChangesAsync();
}
await app.StartAsync();
var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FALHOU: " + label); checks++; Console.WriteLine("OK " + label); }
async Task<T> Service<T>(Func<RecuperacaoSenhaService, Task<T>> action)
{
    await using var scope = app.Services.CreateAsyncScope();
    return await action(scope.ServiceProvider.GetRequiredService<RecuperacaoSenhaService>());
}
async Task Drain()
{
    while (await app.Services.GetRequiredService<RecuperacaoSenhaEntrega>().ProcessarProximoAsync()) { }
}
async Task<string?> Request(string email = "alice@example.invalid", string ip = "local")
{
    var h = await Service(s => s.SolicitarAsync(email, ip));
    await Drain();
    return h;
}
Task<bool> Verify(string? h, string code) => Service(s => s.ValidarAsync(h, code));
Task<bool> Authorized(string? h) => Service(s => s.AutorizadoAsync(h));
Task<IdentityResult> Reset(string? h, string password = "NovaSenha@123") => Service(s => s.RedefinirAsync(h, password));
void Fresh() { clock.Advance(TimeSpan.FromMinutes(16)); mail.Fail = false; }
string Code(string email = "alice@example.invalid") => mail.Codes[email];

var handle = await Request();
Check(handle?.Length == 64, "solicitação existente cria sessão opaca");
Check(Regex.IsMatch(Code(), "^[0-9]{6}$"), "código nativo com seis dígitos");
var missing = await Request("ausente@example.invalid");
Check(missing?.Length == 64 && !mail.Codes.ContainsKey("ausente@example.invalid"), "conta ausente recebe sessão equivalente sem envio");
Check(!await Verify(missing, Code()), "conta ausente não obtém autorização");
Check(!await Authorized(handle), "solicitar não autoriza redefinição");
Check(!(await Reset(handle)).Succeeded, "redefinição direta recusada");
Check(!await Verify("cookie-adulterado", Code()), "sessão manipulada recusada");
Check(!await Verify(handle, "abcdef"), "código incorreto recusado");
Check(await Verify(handle, Code()), "código nativo aceito sem email confirmado");
Check(!await Verify(handle, Code()), "código não pode ser reutilizado");
Check(!(await Reset(handle, "fraca")).Succeeded && await Authorized(handle), "política preservada e correção de senha permitida");
Check((await Reset(handle)).Succeeded, "redefinição bem-sucedida");
Check(!(await Reset(handle)).Succeeded && !await Authorized(handle), "autorização consumida");
Check(await Request() == null, "sucesso não remove intervalo mínimo entre solicitações");
await using (var scope = app.Services.CreateAsyncScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var alice = (await users.FindByEmailAsync("alice@example.invalid"))!;
    var bob = (await users.FindByEmailAsync("bob@example.invalid"))!;
    Check(await users.CheckPasswordAsync(alice, "NovaSenha@123") && !await users.CheckPasswordAsync(alice, "Anterior@123"), "senha nova aceita e antiga recusada");
    Check(await users.CheckPasswordAsync(bob, "Anterior@123"), "outro usuário preservado");
    string code = "", purpose = "";
    for (int i = 0; i < 5000 && !code.StartsWith('0'); i++) { purpose = "zero:" + i; code = await users.GenerateUserTokenAsync(alice, TokenOptions.DefaultEmailProvider, purpose); }
    Check(code.Length == 6 && code.StartsWith('0') && await users.VerifyUserTokenAsync(alice, TokenOptions.DefaultEmailProvider, purpose, code), "zeros iniciais preservados pelo provedor nativo");
}
Fresh(); handle = await Request(); var oldCode = Code();
clock.Advance(TimeSpan.FromMinutes(5));
Check(!await Verify(handle, oldCode), "expiração exata em cinco minutos");
Fresh(); handle = await Request();
for (int i = 0; i < 5; i++) Check(!await Verify(handle, "abcdef"), "tentativa inválida " + (i + 1));
Check(!await Verify(handle, Code()), "quinta falha bloqueia até o código correto");
Fresh(); handle = await Request();
Check(await Request() == null, "reenvio imediato recusado");
clock.Advance(TimeSpan.FromSeconds(59));
Check(await Request() == null, "reenvio antes de 60 segundos recusado");
clock.Advance(TimeSpan.FromSeconds(1));
var next = await Request();
Check(next != null && next != handle, "reenvio aos 60 segundos cria outra solicitação");
Check(!await Verify(handle, Code()), "solicitação anterior invalidada");
Check(await Verify(next, Code()), "novo código pode ser validado");
clock.Advance(TimeSpan.FromSeconds(60)); var newer = await Request();
Check(!await Authorized(next) && !(await Reset(next)).Succeeded, "reenvio invalida também autorização anterior");
Fresh();
for (int i = 0; i < 5; i++) { Check(await Request(i % 2 == 0 ? "alice@example.invalid" : " ALICE@EXAMPLE.INVALID ") != null, "solicitação normalizada " + (i + 1)); clock.Advance(TimeSpan.FromSeconds(60)); }
Check(await Request() == null, "limite por email normalizado");
clock.Advance(TimeSpan.FromMinutes(15)); Check(await Request() != null, "janela de limite expira");
Fresh();
for (int i = 0; i < 20; i++) Check(await Request($"ausente{i}@example.invalid", "ip-abuso") != null, "cota IP " + (i + 1));
Check(await Request("outro@example.invalid", "ip-abuso") == null, "limite por IP entre emails diferentes");
Fresh();
var duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Request()));
Check(duplicates.Count(h => h != null) == 1, "solicitações simultâneas geram uma entrega");
handle = duplicates.Single(h => h != null);
var validations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Verify(handle, Code())));
Check(validations.Count(v => v) == 1, "validação concorrente autoriza uma vez");
var resets = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Reset(handle)));
Check(resets.Count(r => r.Succeeded) == 1, "consumo concorrente redefine uma vez");
Fresh(); mail.Fail = true; handle = await Request();
Check(handle != null && !await Authorized(handle) && !await Verify(handle, "123456"), "falha SMTP simulada não autoriza nem propaga erro");
Check(await Request() == null, "falha SMTP preserva cooldown");
Fresh(); handle = await Request(); Check(await Verify(handle, Code()), "prepara autorização com prazo original");
clock.Advance(TimeSpan.FromMinutes(5));
Check(!await Authorized(handle) && !(await Reset(handle)).Succeeded, "autorização expira no prazo original");

Fresh(); handle = await Request();
for (int i = 0; i < 4; i++) await Verify(handle, "abcdef");
Check(await Verify(handle, Code()), "código correto na quinta tentativa permitido");
await using (var scope = app.Services.CreateAsyncScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    await users.UpdateSecurityStampAsync((await users.FindByEmailAsync("alice@example.invalid"))!);
}
Check(!(await Reset(handle)).Succeeded, "token de redefinição respeita alteração do security stamp");
Check(!await Authorized(handle), "autorização com token invalidado é removida");

Fresh();
var pending = await Service(s => s.SolicitarAsync("alice@example.invalid", "local"));
var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
mail.BlockNext = pause;
mail.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
var delivery = app.Services.GetRequiredService<RecuperacaoSenhaEntrega>().ProcessarProximoAsync();
await mail.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
clock.Advance(TimeSpan.FromSeconds(60));
var replacing = Service(s => s.SolicitarAsync("alice@example.invalid", "local"));
Check(await Task.WhenAny(replacing, Task.Delay(2000)) == replacing, "SMTP lento não bloqueia nova solicitação");
var replacement = await replacing;
pause.SetResult(); await delivery; await Drain();
Check(!await Verify(pending, Code()), "entrega atrasada não reativa solicitação substituída");
Check(await Verify(replacement, Code()), "substituição durante envio mantém somente autorização atual");

Fresh();
for (int i = 0; i < 32; i++) await Service(s => s.SolicitarAsync($"fila{i}@example.invalid", "fila-ip-" + i));
var overflow = await Service(s => s.SolicitarAsync("alice@example.invalid", "fila-lotada"));
await Drain();
Check(overflow != null && !await Verify(overflow, Code()), "fila limitada recusa entrega excedente sem autorizar");

// Verificação HTTP real via Kestrel, com antiforgery e renderização Razor compilada.
HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(origin) };
using var client = Client(); using var stranger = Client();
async Task<string> Html(HttpClient c, string path) => await c.GetStringAsync(path);
string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
async Task<HttpResponseMessage> Post(HttpClient c, string path, string token, params (string, string)[] fields)
{
    var values = fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)).ToList();
    if (token != "") values.Add(new("__RequestVerificationToken", token));
    var result = await c.PostAsync(path, new FormUrlEncodedContent(values));
    await Drain();
    return result;
}
Check((await client.GetAsync("/RecuperacaoSenha/NovaSenha")).StatusCode == HttpStatusCode.Redirect, "GET direto nova senha bloqueado");
foreach (var path in new[] { "Solicitar", "Codigo", "NovaSenha" })
    Check((await Post(client, "/RecuperacaoSenha/" + path, "")).StatusCode == HttpStatusCode.BadRequest, "antiforgery " + path);
var html = await Html(client, "/RecuperacaoSenha/Solicitar");
Check(html.Contains("logo-requisita.png") && Token(html) != "", "formulário Razor usa logo e antiforgery");
Fresh();
var response = await Post(client, "/RecuperacaoSenha/Solicitar", Token(html), ("Email", "alice@example.invalid"));
Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/RecuperacaoSenha/Codigo", "solicitação HTTP existente");
var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("RequisitaPlus.Recuperacao="));
Check(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && cookie.Contains("path=/RecuperacaoSenha"), "cookie restrito à sessão de recuperação");
html = await Html(client, "/RecuperacaoSenha/Codigo");
Check(html.Contains("5 minutos") && html.Contains("60 segundos"), "prazo e intervalo visíveis");
var strangerHtml = await Html(stranger, "/RecuperacaoSenha/Codigo");
response = await Post(stranger, "/RecuperacaoSenha/Codigo", Token(strangerHtml), ("Codigo", Code()));
Check(response.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Código inválido"), "outro navegador não valida código");
response = await Post(client, "/RecuperacaoSenha/Codigo", Token(html), ("Codigo", Code()));
Check(response.StatusCode == HttpStatusCode.Redirect, "validação HTTP válida");
html = await Html(client, "/RecuperacaoSenha/NovaSenha");
Check(!html.Contains("name=\"UserId\"") && !html.Contains("name=\"Token\""), "sem autorização manipulável em campos ocultos");
response = await Post(client, "/RecuperacaoSenha/NovaSenha", Token(html), ("Senha", "Final@123"), ("ConfirmacaoSenha", "Diferente@123"));
Check(response.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("As senhas não conferem"), "confirmação divergente recusada no servidor");
response = await Post(client, "/RecuperacaoSenha/NovaSenha", Token(html), ("Senha", "fraca"), ("ConfirmacaoSenha", "fraca"));
Check(response.StatusCode == HttpStatusCode.OK, "senha fora da política recusada via HTTP");
response = await Post(client, "/RecuperacaoSenha/NovaSenha", Token(html), ("Senha", "Final@123"), ("ConfirmacaoSenha", "Final@123"), ("Email", "bob@example.invalid"), ("UserId", "bob"));
Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/RecuperacaoSenha/Concluida", "alteração HTTP ignora usuário injetado");
Check(!response.Headers.GetValues("Set-Cookie").Any(c => c.Contains(".AspNetCore.Identity.Application=")), "redefinição não autentica automaticamente");
Check((await client.GetAsync("/RecuperacaoSenha/NovaSenha")).StatusCode == HttpStatusCode.Redirect, "autorização HTTP consumida");
html = await Html(client, "/Conta/Login");
Check(html.Contains("Esqueci minha senha"), "link no login");
response = await Post(client, "/Conta/Login", Token(html), ("Email", "alice@example.invalid"), ("Senha", "Final@123"));
Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.TryGetValues("Set-Cookie", out var auth) && auth.Any(c => c.Contains(".AspNetCore.Identity.Application=")), "login real com senha nova");
html = await Html(stranger, "/RecuperacaoSenha/Solicitar");
response = await Post(stranger, "/RecuperacaoSenha/Solicitar", Token(html), ("Email", "ausente-http@example.invalid"));
Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/RecuperacaoSenha/Codigo", "resposta HTTP equivalente para email inexistente");
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RequisitosContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    Check(await users.CheckPasswordAsync((await users.FindByEmailAsync("bob@example.invalid"))!, "Anterior@123"), "injeção HTTP não altera outro usuário");
    Check(await db.Requisitos.CountAsync() == 1 && (await db.Requisitos.SingleAsync()).Descricao == "Preservar este requisito sintético" && await db.AnalisesRequisitos.CountAsync() == 0 && await db.DecisoesSugestoes.CountAsync() == 0, "requisitos histórico e revisão preservados no banco sintético");
}
Check((await stranger.GetAsync("/Requisitos")).StatusCode == HttpStatusCode.Redirect, "autenticação de requisitos preservada");
Fresh();
var hostedWorker = app.Services.GetRequiredService<RecuperacaoSenhaEntrega>();
pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
mail.BlockNext = pause;
mail.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
await hostedWorker.StartAsync(CancellationToken.None);
html = await Html(stranger, "/RecuperacaoSenha/Solicitar");
response = await stranger.PostAsync("/RecuperacaoSenha/Solicitar", new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["Email"] = "alice@example.invalid", ["__RequestVerificationToken"] = Token(html)
})).WaitAsync(TimeSpan.FromSeconds(5));
await mail.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(response.StatusCode == HttpStatusCode.Redirect && !pause.Task.IsCompleted, "worker hospedado entrega em escopo próprio sem POST aguardar SMTP");
pause.SetResult();
await hostedWorker.StopAsync(CancellationToken.None);
await app.StopAsync();
Directory.Delete(testRoot);
Console.WriteLine($"PASSOU: {checks} verificações; SQLite :memory:, SMTP simulado, sem appsettings ou User Secrets.");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan span) => now += span;
}
sealed class FakeEmail : IEmailSender
{
    public bool Fail;
    public TaskCompletionSource? BlockNext;
    public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentDictionary<string, string> Codes = new();
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (Fail) throw new InvalidOperationException("Falha SMTP sintética");
        Codes[recipient] = Regex.Match(body, @"\b[0-9]{6}\b").Value;
        var pause = Interlocked.Exchange(ref BlockNext, null);
        if (pause != null)
        {
            Started.TrySetResult();
            await pause.Task.WaitAsync(cancellationToken);
        }
    }
}
