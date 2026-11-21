using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<IEmailSender, GmailEmailSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RecuperacaoSenhaStore>();
builder.Services.AddSingleton<RecuperacaoSenhaEntrega>();
builder.Services.AddHostedService(services => services.GetRequiredService<RecuperacaoSenhaEntrega>());
builder.Services.AddScoped<RecuperacaoSenhaService>();

builder.Services.AddOptions<GeminiOptions>()
    .Bind(builder.Configuration.GetSection("Gemini"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Modelo), "Informe o modelo da Gemini.")
    .Validate(o => o.TimeoutSegundos is >= 1 and <= 600, "O timeout da Gemini deve ficar entre 1 e 600 segundos.")
    .ValidateOnStart();
builder.Services.AddHttpClient<AnaliseRequisitoService>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<GeminiOptions>>().Value;
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSegundos);
    client.MaxResponseContentBufferSize = 256 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
  .RedactLoggedHeaders(_ => true);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"O valor informado para {field} é inválido.");
    messages.SetValueIsInvalidAccessor(value => "O valor informado é inválido.");
    messages.SetValueMustNotBeNullAccessor(value => "Informe um valor válido.");
});
builder.Services.AddDbContext<RequisitosContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(builder.Environment.ContentRootPath, "requisita-plus.db")}"));
builder.Services.Configure<RequestLocalizationOptions>(options =>
    options.SetDefaultCulture("pt-BR").AddSupportedCultures("pt-BR").AddSupportedUICultures("pt-BR"));

builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<RequisitosContext>()
  .AddSignInManager()
  .AddDefaultTokenProviders()
  .AddErrorDescriber<RequisitaPlus.Web.Data.ErrosIdentity>();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Conta/Login";
    options.AccessDeniedPath = "/Conta/Login";
    options.Cookie.Name = "RequisitaPlus.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Requisitos}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
