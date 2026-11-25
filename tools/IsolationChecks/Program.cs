using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using RequisitaPlus.Web.Controllers;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Models;
using RequisitaPlus.Web.Services;

// Host próprio: não executa Program da aplicação, não lê configurações reais ou User Secrets.
var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var root = Path.Combine(projectRoot, ".local", "isolation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FALHOU: " + name); checks++; Console.WriteLine("OK " + name); }
var fake = new FakeGemini();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = root, Args = [] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Environment.EnvironmentName = "Development";
var database = Path.Combine(root, "synthetic.db");
builder.Services.AddDbContext<RequisitosContext>(o => o.UseSqlite("Data Source=" + database));
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddIdentityCore<IdentityUser>().AddEntityFrameworkStores<RequisitosContext>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o => o.LoginPath = "/Conta/Login");
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(RequisitosController).Assembly);
builder.Services.AddScoped(_ => Service());
await using var app = builder.Build();
app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Requisitos}/{action=Index}/{id?}");
AnaliseRequisitoService Service() => new(new HttpClient(fake, false) { BaseAddress = new Uri("https://synthetic.invalid/") },
    Options.Create(new GeminiOptions { ApiKey = "synthetic-only" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<AnaliseRequisitoService>.Instance);
RequisitosContext Db() => new(new DbContextOptionsBuilder<RequisitosContext>().UseSqlite("Data Source=" + database).Options);
RequisitosController Controller(RequisitosContext db, string user) {
    var c = new RequisitosController(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<RequisitosController>.Instance);
    c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test")) } };
    c.TempData = new TempDataDictionary(c.HttpContext, new MemoryTempData());
    return c;
}
await using (var scope = app.Services.CreateAsyncScope()) {
    var db = scope.ServiceProvider.GetRequiredService<RequisitosContext>();
    if(args.Contains("--no-migrations")) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    foreach (var id in new[] { "user-a", "user-b" })
        Check((await users.CreateAsync(new IdentityUser { Id = id, UserName = id + "@example.invalid", Email = id + "@example.invalid" }, "Synthetic@123")).Succeeded, "conta sintética criada");
}
await app.StartAsync();
var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
HttpClient Client() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) };
async Task<string> Token(HttpClient client, string path = "/Requisitos/Create") {
    var html = await client.GetStringAsync(path);
    return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
}
async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string,string>? values = null, string tokenPath = "/Requisitos/Create") {
    values ??= new(); values["__RequestVerificationToken"] = await Token(client, tokenPath);
    return await client.PostAsync(path, new FormUrlEncodedContent(values));
}
async Task Login(HttpClient client, string id) {
    Check((await Post(client, "/Conta/Login", new() { ["Email"] = id + "@example.invalid", ["Senha"] = "Synthetic@123" }, "/Conta/Login")).StatusCode == HttpStatusCode.Redirect, "login sintético");
}
using var a = Client(); using var b = Client(); using var anonymous = Client();
await Login(a, "user-a"); await Login(b, "user-b");
Check((await anonymous.GetAsync("/Requisitos")).StatusCode == HttpStatusCode.Redirect, "anônimo sem acesso");
foreach (var client in new[] { a, b }) {
    var html = await client.GetStringAsync("/");
    Check(html.Contains("Seu primeiro requisito") && Regex.Matches(html,"<dd>0</dd>").Count==4, "dashboard inicialmente vazio e quatro indicadores zerados");
    foreach (var forged in new[] { "3", "99", "invalido", "" }) {
        var response = await Post(client, "/Requisitos/Create", new() { ["Titulo"] = ReferenceEquals(client,a) ? "ALPHA" : "BETA", ["Descricao"] = "Consultar rapidamente o catálogo.", ["Tipo"] = "1", ["Status"] = forged, ["ProprietarioId"] = "intruso" });
        Check(response.StatusCode == HttpStatusCode.Redirect, "cadastro ignora status e proprietário forjados");
    }
}
int aid, bid;
await using (var db = Db()) {
    Check(await db.Requisitos.CountAsync() == 8 && await db.Requisitos.AllAsync(r => r.Status == StatusRequisito.Pendente && (r.ProprietarioId == "user-a" || r.ProprietarioId == "user-b")), "cadastros pendentes com identidade do servidor");
    aid = await db.Requisitos.Where(r => r.ProprietarioId == "user-a").Select(r=>r.Id).FirstAsync();
    bid = await db.Requisitos.Where(r => r.ProprietarioId == "user-b").Select(r=>r.Id).FirstAsync();
}
foreach (var (client, own, other) in new[] { (a,"ALPHA","BETA"), (b,"BETA","ALPHA") }) {
    foreach (var url in new[] { "/", "/Requisitos", "/Requisitos?tipo=1&status=1", "/Requisitos?titulo="+other, "/Requisitos?titulo="+own.ToLowerInvariant() }) {
        var html = await client.GetStringAsync(url);
        Check(!Regex.IsMatch(html, @"<td[^>]*>(?:<a[^>]*>)?\s*" + other + @"\s*(?:</a>)?</td>"), "dashboard/lista/pesquisa/filtro isolados");
    }
}
foreach(var user in new[]{"user-a","user-b"}) {
    await using var db=Db();
    var c=Controller(db,user);
    var model=(List<Requisito>)((ViewResult)await c.Index(TipoRequisito.Funcional,StatusRequisito.Pendente,user=="user-a"?"alpha":"beta")).Model!;
    Check(model.Count==4 && model.All(r=>r.ProprietarioId==user),"filtros combinados e pesquisa sem distinção de maiúsculas");
    var home=new HomeController(db){ControllerContext=c.ControllerContext};
    var dashboard=(DashboardViewModel)((ViewResult)await home.Index(default)).Model!;
    Check(dashboard.Total==4 && dashboard.Pendentes==4 && dashboard.EmAnalise==0 && dashboard.Revisados==0 && dashboard.Recentes.Count==4 && dashboard.Recentes.All(r=>r.ProprietarioId==user),"indicadores e recentes calculados por proprietário");
}
async Task<(StatusRequisito?,int,string)> State(int id) {
    await using var db = Db(); var r = await db.Requisitos.SingleAsync(r=>r.Id==id);
    return (r.Status, await db.AnalisesRequisitos.CountAsync(x=>x.RequisitoId==id), r.Descricao);
}
foreach (var status in Enum.GetValues<StatusRequisito>()) {
    foreach (var problems in new[] { 0, 2 }) {
        await using (var db=Db()) { var r=await db.Requisitos.FindAsync(aid); r!.Status=status; await db.SaveChangesAsync(); }
        fake.Reset(problems);
        var before = await State(aid);
        await using (var db=Db()) {
            var result=await Controller(db,"user-a").Analisar(aid,Service(),default);
            Check(result is ViewResult { Model: AnaliseRequisitoViewModel { Resultado: not null, Requisito.Status: StatusRequisito.EmAnalise } }, "análise válida atualiza resposta e status");
        }
        var after=await State(aid);
        Check(after.Item1==StatusRequisito.EmAnalise && after.Item2==before.Item2+1 && after.Item3==before.Item3 && fake.Calls==1, "persistência válida inclusive zero sugestões e reanálise");
    }
}
foreach (var failure in new[] { "http", "json", "timeout", "network", "insert", "update", "cancel" }) {
    await using (var db=Db()) {
        var r=await db.Requisitos.FindAsync(aid); r!.Status=StatusRequisito.Revisado; await db.SaveChangesAsync();
        if(failure is "insert" or "update") await db.Database.ExecuteSqlRawAsync(failure=="insert"
            ? "CREATE TRIGGER FailAnalysis BEFORE INSERT ON AnalisesRequisitos BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;"
            : "CREATE TRIGGER FailStatus BEFORE UPDATE ON Requisitos WHEN NEW.Status=2 BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;");
    }
    var before=await State(aid); fake.Reset(1); fake.Failure=failure;
    using var cancel=new CancellationTokenSource();
    if(failure=="cancel") fake.Callback=()=>{cancel.Cancel();return Task.CompletedTask;};
    try { await using var db=Db(); await Controller(db,"user-a").Analisar(aid,Service(),cancel.Token); }
    catch(OperationCanceledException) when(failure=="cancel") { }
    Check(await State(aid)==before, "falha preserva status/texto/histórico: "+failure);
    await using(var db=Db()) { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS FailAnalysis;"); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS FailStatus;"); }
}
fake.Reset(1);
var cancelBefore=await State(aid);
using(var cancellation=new CancellationTokenSource()) {
    await using var db=new RequisitosContext(new DbContextOptionsBuilder<RequisitosContext>().UseSqlite("Data Source="+database).AddInterceptors(new CancelAfterSave(cancellation)).Options);
    try{await Controller(db,"user-a").Analisar(aid,Service(),cancellation.Token);}catch(OperationCanceledException){}
}
Check(await State(aid)==cancelBefore,"cancelamento após INSERT reverte análise e status");
fake.Reset(1);
fake.Callback=async()=>{ await using var db=Db(); var r=await db.Requisitos.FindAsync(aid); r!.Descricao="Texto alterado durante análise"; await db.SaveChangesAsync(); };
var concurrentBefore=(await State(aid)).Item2;
await using(var db=Db()) Check(await Controller(db,"user-a").Analisar(aid,Service(),default) is ConflictObjectResult, "edição durante Gemini detectada");
Check((await State(aid)).Item2==concurrentBefore && (await State(aid)).Item1==StatusRequisito.Revisado, "análise concorrente descartada sem mudar status");
// Duas análises iniciadas na mesma versão: a interna confirma primeiro, a externa recebe conflito.
fake.Reset(1);
fake.Callback=async()=> { fake.Callback=null; await using var db=Db(); await Controller(db,"user-a").Analisar(aid,Service(),default); };
await using(var db=Db()) Check(await Controller(db,"user-a").Analisar(aid,Service(),default) is ConflictObjectResult, "reanálises concorrentes detectadas");
fake.Reset(2);
Check((await Post(a,"/Requisitos/Analisar/"+aid)).StatusCode==HttpStatusCode.OK, "análise HTTP autorizada simulada");
fake.Reset(2);
Check((await Post(b,"/Requisitos/Analisar/"+bid)).StatusCode==HttpStatusCode.OK, "análise HTTP da segunda conta");
int analysisA, analysisB;
await using(var db=Db()) {
    analysisA=await db.AnalisesRequisitos.Where(x=>x.RequisitoId==aid).MaxAsync(x=>x.Id);
    analysisB=await db.AnalisesRequisitos.Where(x=>x.RequisitoId==bid).MaxAsync(x=>x.Id);
}
foreach(var (client, foreign, analysis) in new[] { (a,bid,analysisB), (b,aid,analysisA) }) {
    var calls=fake.Calls; var before=await State(foreign);
    foreach(var action in new[] { "Details", "Edit", "Delete", "Analisar", "Historico" })
        Check((await client.GetAsync($"/Requisitos/{action}/{foreign}")).StatusCode==HttpStatusCode.NotFound,"GET alheio 404: "+action);
    Check((await client.GetAsync($"/Requisitos/Revisar/{analysis}?indice=0")).StatusCode==HttpStatusCode.NotFound,"GET revisão alheia 404");
    foreach(var action in new[] { "Edit", "Delete", "Analisar" })
        Check((await Post(client,$"/Requisitos/{action}/{foreign}",new() { ["Titulo"]="ataque",["Descricao"]="ataque",["Tipo"]="1",["Status"]="3" })).StatusCode==HttpStatusCode.NotFound,"POST alheio 404: "+action);
    Check((await Post(client,"/Requisitos/Revisar",new() { ["AnaliseId"]=analysis.ToString(),["IndiceSugestao"]="0",["Decisao"]="1",["Confirmacao"]="true",["RedacaoFinal"]="ataque" })).StatusCode==HttpStatusCode.NotFound,"POST revisão alheia 404");
    Check(fake.Calls==calls && await State(foreign)==before,"acesso alheio sem efeitos nem chamadas Gemini");
}
foreach(var action in new[] { "Details", "Edit", "Delete", "Analisar", "Historico", "Revisar" })
    Check((await a.GetAsync($"/Requisitos/{action}/2147483647")).StatusCode==HttpStatusCode.NotFound,"ID inexistente 404: "+action);
foreach(var action in new[] { "Edit", "Delete", "Analisar", "Revisar" })
    Check((await Post(a,$"/Requisitos/{action}/2147483647",new() { ["AnaliseId"]="2147483647" })).StatusCode==HttpStatusCode.NotFound,"POST inexistente 404: "+action);
Check((await a.PostAsync("/Requisitos/Analisar/"+aid,new FormUrlEncodedContent([]))).StatusCode==HttpStatusCode.BadRequest,"antiforgery preservado");
// Revisão parcial, formulário antigo, conclusão humana e histórico imutável.
var reviewHtml=await a.GetStringAsync($"/Requisitos/Revisar/{analysisA}?indice=0");
string Version(string html,string field) => WebUtility.HtmlDecode(Regex.Match(html,"name=\""+field+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
var reviewVersion=Version(reviewHtml,"VersaoRequisito");
var pendingEdit=await a.GetStringAsync("/Requisitos/Edit/"+aid);
Check((await Post(a,"/Requisitos/Edit/"+aid,new(){["Titulo"]="ALPHA",["Descricao"]="Texto",["Tipo"]="1",["Status"]="3",["VersaoConcorrencia"]=Version(pendingEdit,"VersaoConcorrencia")})).StatusCode==HttpStatusCode.OK && (await State(aid)).Item1==StatusRequisito.EmAnalise,"conclusão manual bloqueada com sugestões pendentes");
await Post(a,"/Requisitos/Revisar",new(){["AnaliseId"]=analysisA.ToString(),["IndiceSugestao"]="0",["VersaoRequisito"]=reviewVersion,["Decisao"]="2",["RedacaoFinal"]="Texto sem confirmação",["Confirmacao"]="false"});
await using(var db=Db()) Check(await db.DecisoesSugestoes.CountAsync()==0,"ajuste exige confirmação humana");
Check((await Post(a,"/Requisitos/Revisar",new(){["AnaliseId"]=analysisA.ToString(),["IndiceSugestao"]="0",["VersaoRequisito"]=reviewVersion,["Decisao"]="1",["RedacaoFinal"]="Texto humano confirmado.",["Confirmacao"]="true"})).StatusCode==HttpStatusCode.Redirect,"aceite humano confirmado");
Check((await State(aid)).Item1==StatusRequisito.EmAnalise,"revisão parcial não conclui");
Check((await Post(a,"/Requisitos/Revisar",new(){["AnaliseId"]=analysisA.ToString(),["IndiceSugestao"]="1",["VersaoRequisito"]=reviewVersion,["Decisao"]="3",["Confirmacao"]="true"})).StatusCode==HttpStatusCode.Redirect,"formulário antigo de revisão recusado");
await using(var db=Db()) Check(await db.DecisoesSugestoes.CountAsync()==1,"decisão concorrente não duplicada");
reviewHtml=await a.GetStringAsync($"/Requisitos/Revisar/{analysisA}?indice=1");
await Post(a,"/Requisitos/Revisar",new(){["AnaliseId"]=analysisA.ToString(),["IndiceSugestao"]="1",["VersaoRequisito"]=Version(reviewHtml,"VersaoRequisito"),["Decisao"]="3",["Confirmacao"]="true"});
Check((await State(aid)).Item1==StatusRequisito.Revisado,"última decisão conclui revisão humana");
string originalJson;
await using(var db=Db()) {
    originalJson=(await db.AnalisesRequisitos.FindAsync(analysisA))!.ResultadoJson;
    Check(await db.DecisoesSugestoes.AllAsync(d=>d.UsuarioId=="user-a"),"autoria humana preservada");
}
fake.Reset(0); await Post(a,"/Requisitos/Analisar/"+aid);
await using(var db=Db()) Check((await db.AnalisesRequisitos.FindAsync(analysisA))!.ResultadoJson==originalJson && await db.DecisoesSugestoes.CountAsync()==2 && (await State(aid)).Item1==StatusRequisito.EmAnalise,"reanálise preserva JSON/decisões e reabre status");
// Edição legítima e proteção de formulário obsoleto.
var edit=await a.GetStringAsync("/Requisitos/Edit/"+aid); var editVersion=Version(edit,"VersaoConcorrencia");
var editData=new Dictionary<string,string>{["Titulo"]="ALPHA editado",["Descricao"]="Texto editado",["Tipo"]="2",["Status"]="3",["VersaoConcorrencia"]=editVersion};
Check((await Post(a,"/Requisitos/Edit/"+aid,editData)).StatusCode==HttpStatusCode.Redirect,"edição legítima e conclusão manual sem sugestões");
Check((await Post(a,"/Requisitos/Edit/"+aid,editData)).StatusCode==HttpStatusCode.Conflict,"edição obsoleta recusada");
var deletion=await a.GetStringAsync("/Requisitos/Delete/"+aid);
Check((await Post(a,"/Requisitos/Delete/"+aid,new(){["VersaoConcorrencia"]=Version(deletion,"VersaoConcorrencia")})).StatusCode==HttpStatusCode.Redirect && (await State(aid)).Item2>0,"exclusão com histórico bloqueada");
int disposable; await using(var db=Db()) disposable=await db.Requisitos.Where(r=>r.ProprietarioId=="user-a"&&r.Id!=aid).Select(r=>r.Id).FirstAsync();
deletion=await a.GetStringAsync("/Requisitos/Delete/"+disposable);
Check((await Post(a,"/Requisitos/Delete/"+disposable,new(){["VersaoConcorrencia"]=Version(deletion,"VersaoConcorrencia")})).StatusCode==HttpStatusCode.Redirect,"exclusão legítima sem histórico em banco sintético");
// Concorrência real do EF entre dois contextos.
await using(var first=Db()) await using(var second=Db()) {
    var x=await first.Requisitos.FindAsync(aid); var y=await second.Requisitos.FindAsync(aid);
    x!.Titulo="ALPHA concorrente"; await first.SaveChangesAsync(); y!.Titulo="sobrescrita";
    var rejected=false; try { await second.SaveChangesAsync(); } catch(DbUpdateConcurrencyException) {rejected=true;}
    Check(rejected,"token EF impede sobrescrita entre contextos");
}
if (!args.Contains("--skip-library-seed")) {
async Task<int> Seed(params string[] arguments) {
    var start=new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory=projectRoot, RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false, CreateNoWindow=true };
    start.ArgumentList.Add(Path.Combine(projectRoot,"tools/LibrarySeed/bin/Debug/net10.0/LibrarySeed.dll"));
    foreach(var arg in arguments)start.ArgumentList.Add(arg);
    using var process=System.Diagnostics.Process.Start(start)!;
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();await Task.WhenAll(stdout,stderr);return process.ExitCode;
}
Check(await Seed("--database",database,"--dry-run")!=0,"LibrarySeed recusa proprietário ausente");
Check(await Seed("--database",database,"--owner-id","inexistente","--dry-run")!=0,"LibrarySeed recusa proprietário inexistente");
await using(var db=Db()) {
    var before=await db.Requisitos.AsNoTracking().Where(r=>r.ProprietarioId=="user-b").OrderBy(r=>r.Id).ToListAsync();
    Check(await Seed("--database",database,"--owner-id","user-a","--dry-run")==0 && await db.Requisitos.CountAsync()==7,"LibrarySeed simulação não altera banco sintético");
    Check(await Seed("--database",database,"--owner-id","user-a","--execute","--backup-dir",Path.Combine(root,"seed-backups"))==0,"LibrarySeed execução apenas no banco sintético");
    var after=await db.Requisitos.AsNoTracking().Where(r=>r.ProprietarioId=="user-b").OrderBy(r=>r.Id).ToListAsync();
    Check(JsonSerializer.Serialize(before)==JsonSerializer.Serialize(after) && await db.Requisitos.CountAsync(r=>r.ProprietarioId=="user-a")==100,"LibrarySeed preserva outra conta e atribui proprietário explícito");
}
}
else Console.WriteLine("IGNORADOS: 5 checks de LibrarySeed por restrição explícita; ferramenta não executada.");
await HumanReviewRegression.Run(a, b, Db, Check, fake.Reset, Service, database);
await app.StopAsync();
if(args.Contains("--no-migrations")) Console.WriteLine("IGNORADOS: ensaios de migrations por restrição explícita; schema sintético criado com EnsureCreated.");
else if(args.Length>=1) await Rehearse(args[0]);
else throw new Exception("Informe explicitamente a cópia consistente a ensaiar.");
if(args.Length>=2 && args[1]!="--skip-library-seed")
    await LegacyRegression.Run(projectRoot, root, args[1], Check, Service, fake.Reset);
var report=new { Executed=checks, Passed=checks, Failed=0, RealGeminiCalls=0, Root=root };
await File.WriteAllTextAsync(Path.Combine(root,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));

async Task Rehearse(string source) {
    // Somente cópia adicional explicitamente fornecida; nunca aceita o caminho do banco real.
    var full=Path.GetFullPath(source,projectRoot);
    if(!full.Contains(Path.Combine(".local","backups","pre-ownership-"),StringComparison.OrdinalIgnoreCase)) throw new Exception("Cópia de ensaio não autorizada");
    var clone=Path.Combine(root,"rehearsal.db"); File.Copy(full,clone,false);
    Dictionary<string,List<string>> Snapshot() {
        using var c=new SqliteConnection("Data Source="+clone);c.Open();
        var output=new Dictionary<string,List<string>>();
        foreach(var table in new[]{"Requisitos","AnalisesRequisitos","DecisoesSugestoes","AspNetUsers"}) {
            using var cmd=c.CreateCommand();cmd.CommandText="SELECT * FROM "+table+" ORDER BY Id";
            using var reader=cmd.ExecuteReader();var rows=new List<string>();
            while(reader.Read()) { var values=new SortedDictionary<string,object?>(StringComparer.Ordinal); for(var i=0;i<reader.FieldCount;i++) if(reader.GetName(i) is not ("ProprietarioId" or "VersaoConcorrencia")) values[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(JsonSerializer.Serialize(values)); }
            output[table]=rows;
        }
        return output;
    }
    RequisitosContext Clone()=>new(new DbContextOptionsBuilder<RequisitosContext>().UseSqlite("Data Source="+clone).Options);
    var before=Snapshot();
    await using(var db=Clone()) await db.GetService<IMigrator>().MigrateAsync("20261125003912_AdicionarPropriedadeOpcional");
    Check(before.All(p=>Snapshot()[p.Key].SequenceEqual(p.Value)),"etapa anulável preserva todos os valores originais");
    await using(var db=Clone()) {
        var c=Controller(db,"user-a");
        var id=await db.Requisitos.Select(r=>r.Id).FirstAsync();
        var analysis=await db.AnalisesRequisitos.Select(a=>a.Id).FirstAsync();
        var initialCalls=fake.Calls;
        Check(await c.Details(id) is NotFoundResult,"legado sem proprietário inacessível");
        Check(await c.Edit(id) is NotFoundResult && await c.Edit(id,new Requisito()) is NotFoundResult,"edição GET/POST de legado sem proprietário bloqueada");
        Check(await c.Delete(id) is NotFoundResult && await c.DeleteConfirmed(id,null) is NotFoundResult,"exclusão GET/POST de legado sem proprietário bloqueada");
        Check(await c.Analisar(id,default) is NotFoundResult && await c.Analisar(id,Service(),default) is NotFoundResult && fake.Calls==initialCalls,"análise GET/POST de legado sem proprietário sem Gemini");
        Check(await c.Historico(id,default) is NotFoundResult && await c.Revisar(analysis,0,default) is NotFoundResult && await c.Revisar(new RevisarSugestaoViewModel{AnaliseId=analysis},default) is NotFoundResult,"histórico e revisão GET/POST de legado sem proprietário bloqueados");
        Check(((ViewResult)await c.Index(null,null)).Model is List<Requisito>{Count:0},"listagem não expõe legado sem proprietário");
        var rejected=false;try {await db.Database.MigrateAsync();}catch(SqliteException){rejected=true;}
        Check(rejected,"segunda migration recusa associação incompleta");
    }
    // Conta SINTÉTICA explícita apenas na cópia; nenhuma inferência sobre as contas reais.
    await using(var db=Clone()) {
        db.Users.Add(new IdentityUser { Id="rehearsal-explicit-synthetic-owner",UserName="rehearsal@example.invalid" });await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE Requisitos SET ProprietarioId = 'rehearsal-explicit-synthetic-owner' WHERE ProprietarioId IS NULL");
        await db.Database.MigrateAsync();
        Check(await db.Requisitos.AllAsync(r=>r.ProprietarioId=="rehearsal-explicit-synthetic-owner"),"associação sintética explícita na cópia");
        await db.Database.MigrateAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count()==6,"migrations aplicadas em duas etapas e repetição sem alterações");
        var cn=db.Database.GetDbConnection(); await cn.OpenAsync();
        using(var cmd=cn.CreateCommand()) {cmd.CommandText="PRAGMA integrity_check";Check((string?)await cmd.ExecuteScalarAsync()=="ok","integridade SQLite após migrations");cmd.CommandText="PRAGMA foreign_key_check";using var reader=await cmd.ExecuteReaderAsync();Check(!await reader.ReadAsync(),"chaves estrangeiras íntegras");}
        var user=await db.Users.FindAsync("rehearsal-explicit-synthetic-owner");db.Users.Remove(user!);
        var blocked=false;try{await db.SaveChangesAsync();}catch(DbUpdateException){blocked=true;}Check(blocked,"exclusão de proprietário restrita");db.ChangeTracker.Clear();
        Check(await db.Requisitos.CountAsync()==100 && await db.AnalisesRequisitos.CountAsync()==31 && await db.DecisoesSugestoes.CountAsync()==18,"contagens atuais preservadas na cópia");
        Check((await db.AnalisesRequisitos.Select(a=>a.ResultadoJson).ToListAsync()).Sum(j=>JsonDocument.Parse(j).RootElement.GetProperty("problemas").GetArrayLength())==73,"73 sugestões preservadas integralmente");
    }
    var after=Snapshot();
    Check(before.All(p=>p.Key=="AspNetUsers" ? p.Value.All(v=>after[p.Key].Contains(v)) : after[p.Key].SequenceEqual(p.Value)),"IDs/conteúdo/status/autoria/vínculos originais preservados após ensaio completo");
    await File.WriteAllTextAsync(Path.Combine(root,"migration-result.json"),JsonSerializer.Serialize(new{Requirements=100,Analyses=31,Suggestions=73,Decisions=18,OriginalColumnsUnchanged=true,SyntheticOwnerOnlyInClone=true}));
}

sealed class MemoryTempData : ITempDataProvider {
    public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
    public void SaveTempData(HttpContext context, IDictionary<string,object> values) { }
}
sealed class CancelAfterSave(CancellationTokenSource source) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor {
    public override ValueTask<int> SavedChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,int result,CancellationToken cancellationToken=default) {
        source.Cancel();return ValueTask.FromResult(result);
    }
}
sealed class FakeGemini : HttpMessageHandler {
    public int Calls; public int Problems; public string? Failure; public Func<Task>? Callback;
    public void Reset(int count) {Calls=0;Problems=count;Failure=null;Callback=null;}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        Calls++; if(Callback is not null) await Callback(); ct.ThrowIfCancellationRequested();
        if(Failure=="timeout") throw new TaskCanceledException();
        if(Failure=="network") throw new HttpRequestException();
        if(Failure=="http") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{}")};
        if(Failure=="json") return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("invalid")};
        var problems=Enumerable.Range(0,Problems).Select(i=>new{categoria="Ambiguidade",problema="Termo vago "+i,justificativa="Falta critério",sugestao="Definir critério"});
        var json=JsonSerializer.Serialize(new{candidates=new[]{new{finishReason="STOP",content=new{parts=new[]{new{text=JsonSerializer.Serialize(new{problemas=problems})}}}}}});
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)};
    }
}
