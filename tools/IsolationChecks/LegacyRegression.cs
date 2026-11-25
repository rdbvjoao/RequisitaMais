using System.Net;
using System.Security.Claims;
using System.Text.Json;
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

internal static class LegacyRegression
{
    public static async Task Run(string projectRoot, string root, string source, Action<bool,string> check,
        Func<AnaliseRequisitoService> service, Action<int> reset)
    {
        var full = Path.GetFullPath(source, projectRoot);
        var allowed = Path.GetFullPath(Path.Combine(projectRoot, ".local/backups")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new Exception("Informe somente o novo backup validado; o banco real nunca é aceito.");
        var database = Path.Combine(root, "legacy-regression.db");
        File.Copy(full, database, false);
        RequisitosContext Db() => new(new DbContextOptionsBuilder<RequisitosContext>().UseSqlite("Data Source=" + database).Options);
        async Task<Requisito> Read(int id) { await using var db=Db(); return await db.Requisitos.AsNoTracking().SingleAsync(r=>r.Id==id); }
        string owner;
        await using(var db=Db()) {
            var r=await db.Requisitos.AsNoTracking().SingleAsync(r=>r.Id==103);
            owner=r.ProprietarioId;
            check(r.VersaoConcorrencia=="" && r.Status==StatusRequisito.Pendente && await db.AnalisesRequisitos.AnyAsync(a=>a.RequisitoId==103), "cópia atual reproduz ID 103 legado Pendente com histórico");
            check(!db.Database.HasPendingModelChanges(), "modelo atual sem alterações pendentes de migration");
            check(db.Model.FindEntityType(typeof(Requisito))!.FindProperty(nameof(Requisito.VersaoConcorrencia))!.IsConcurrencyToken, "IsConcurrencyToken preservado");
        }
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing",ContentRootPath=root,Args=[] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<RequisitosContext>(o=>o.UseSqlite("Data Source="+database));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentityCore<IdentityUser>().AddEntityFrameworkStores<RequisitosContext>().AddSignInManager();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(); builder.Services.AddAuthorization();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(RequisitosController).Assembly);
        builder.Services.AddScoped(_=>service());
        await using var app=builder.Build(); app.UseRouting(); app.UseAuthentication();
        // Identidade exclusivamente no host de teste, sem senha, secrets ou sessão real.
        app.Use(async(ctx,next)=> { ctx.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,
            ctx.Request.Headers.ContainsKey("X-Synthetic-Other")?"synthetic-other":owner)],"isolated-test")); await next(); });
        app.UseAuthorization(); app.MapControllerRoute("default","{controller=Requisitos}/{action=Index}/{id?}");
        await app.StartAsync();
        var origin=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        HttpClient Client()=>new(new HttpClientHandler {CookieContainer=new CookieContainer(),AllowAutoRedirect=false}) {BaseAddress=origin};
        using var client=Client(); using var other=Client(); other.DefaultRequestHeaders.Add("X-Synthetic-Other","true");
        string Hidden(string html,string name) {
            var matches=Regex.Matches(html,"<input[^>]*name=\""+name+"\"[^>]*value=\"([^\"]*)\"[^>]*>");
            if(name=="VersaoConcorrencia") check(matches.Count==1,"formulário contém exatamente um campo "+name);
            else if(matches.Count==0) throw new Exception("Antiforgery ausente do HTML");
            return WebUtility.HtmlDecode(matches[0].Groups[1].Value);
        }
        async Task<(string Token,string Anti)> Form(int id,string action="Edit") {
            var html=await client.GetStringAsync($"/Requisitos/{action}/{id}");
            return(Hidden(html,"VersaoConcorrencia"),Hidden(html,"__RequestVerificationToken"));
        }
        List<KeyValuePair<string,string>> Data(Requisito r,string anti)=>new() {
            new("Titulo",r.Titulo), new("Descricao",r.Descricao), new("Tipo",((int)r.Tipo!).ToString()),
            new("Status",((int)r.Status!).ToString()), new("__RequestVerificationToken",anti)
        };
        async Task<HttpResponseMessage> Submit(int id,Requisito r,(string Token,string Anti) form,string action="Edit",string[]? tokens=null,HttpClient? user=null) {
            var values=Data(r,form.Anti);
            foreach(var token in tokens??[form.Token]) values.Add(new("VersaoConcorrencia",token));
            return await (user??client).PostAsync($"/Requisitos/{action}/{id}",new FormUrlEncodedContent(values));
        }
        async Task<int> Legacy(bool history=false) {
            await using var db=Db();
            var r=new Requisito {ProprietarioId=owner,Titulo="Legado sintético",Descricao="Descrição sintética",Tipo=TipoRequisito.Funcional,VersaoConcorrencia=""};
            db.Add(r); await db.SaveChangesAsync();
            if(history) { db.Add(new AnaliseRequisito {RequisitoId=r.Id,DataAnalise=DateTime.Now,ResultadoJson="{\"problemas\":[]}"});await db.SaveChangesAsync(); }
            return r.Id;
        }
        // O ID 103 é exercitado na cópia íntegra, com seu histórico existente.
        var original103=await Read(103); var form103=await Form(103);
        var changed103=await Read(103); changed103.Status=StatusRequisito.EmAnalise;
        check((await Submit(103,changed103,form103)).StatusCode==HttpStatusCode.Redirect,"ID 103 edição HTTP autorizada com vazio explícito");
        var saved103=await Read(103);
        check(saved103.Status==StatusRequisito.EmAnalise && saved103.Titulo==original103.Titulo && saved103.Descricao==original103.Descricao && Guid.TryParseExact(saved103.VersaoConcorrencia,"N",out _),"ID 103 altera somente status e gera GUID");
        check((await Submit(103,changed103,form103)).StatusCode==HttpStatusCode.Conflict,"ID 103 segundo formulário com vazio obsoleto recusado");
        foreach(var field in new[]{"Titulo","Descricao","Status"}) {
            var id=await Legacy();var before=await Read(id);var form=await Form(id);
            check(form.Token=="","token legado renderizado vazio");
            // Um envio sem mudanças não renova a versão nem preenche o legado.
            check((await Submit(id,before,form)).StatusCode==HttpStatusCode.Redirect && (await Read(id)).VersaoConcorrencia=="","edição sem alteração efetiva preserva vazio");
            var change=await Read(id);
            if(field=="Titulo") change.Titulo+=" alterado";
            if(field=="Descricao") change.Descricao+=" alterada";
            if(field=="Status") change.Status=StatusRequisito.EmAnalise;
            var first=await Form(id);var second=await Form(id);
            check((await Submit(id,change,first)).StatusCode==HttpStatusCode.Redirect,"edição isolada de "+field+" aceita token vazio");
            var saved=await Read(id);
            check(saved.Titulo==change.Titulo && saved.Descricao==change.Descricao && saved.Status==change.Status && saved.Tipo==before.Tipo && Guid.TryParseExact(saved.VersaoConcorrencia,"N",out _),"somente "+field+" alterado; GUID após primeira alteração efetiva");
            check((await Submit(id,change,second)).StatusCode==HttpStatusCode.Conflict,"dois formulários legados: segundo recusado após "+field);
            var guidForm=await Form(id); change.Titulo+=" com GUID";
            check((await Submit(id,change,guidForm)).StatusCode==HttpStatusCode.Redirect && (await Read(id)).VersaoConcorrencia!=saved.VersaoConcorrencia,"edição subsequente com GUID renova token");
        }
        foreach(var legacy in new[]{true,false}) {
            var id=await Legacy();
            if(!legacy) {await using var db=Db();var r=await db.Requisitos.FindAsync(id);r!.Titulo+=" novo";await db.SaveChangesAsync();}
            foreach(var action in new[]{"Edit","Delete"}) {
                foreach(var mode in new[]{"ausente","duplicado","adulterado","espaços","padding","obsoleto","vazio indevido","caixa alterada"}) {
                    var form=await Form(id,action);var before=await Read(id);
                    if(legacy && mode is "vazio indevido" or "caixa alterada") continue;
                    string[] tokens=mode switch {
                        "ausente"=>[], "duplicado"=>[form.Token,form.Token], "adulterado"=>["adulterado"],
                        "espaços"=>[" \t "], "padding"=>[" "+form.Token+" "], "obsoleto"=>[Guid.NewGuid().ToString("N")],
                        "vazio indevido"=>[""], _=>[form.Token.ToUpperInvariant()]
                    };
                    check((await Submit(id,before,form,action,tokens)).StatusCode==HttpStatusCode.Conflict,$"{action} {(legacy?"legado":"GUID")} rejeita token {mode}");
                    check(JsonSerializer.Serialize(await Read(id))==JsonSerializer.Serialize(before),"token inválido não altera requisito");
                }
                var valid=await Form(id,action);
                var noAnti=Data(await Read(id),valid.Anti);noAnti.RemoveAll(x=>x.Key=="__RequestVerificationToken");noAnti.Add(new("VersaoConcorrencia",valid.Token));
                check((await client.PostAsync($"/Requisitos/{action}/{id}",new FormUrlEncodedContent(noAnti))).StatusCode==HttpStatusCode.BadRequest,"antiforgery obrigatório em "+action);
                // Token colocado só na query não substitui campo ausente do formulário.
                check((await client.PostAsync($"/Requisitos/{action}/{id}?VersaoConcorrencia={valid.Token}",new FormUrlEncodedContent(Data(await Read(id),valid.Anti)))).StatusCode==HttpStatusCode.Conflict,"token deve estar no formulário: "+action);
            }
            var invalid=await Read(id); invalid.Titulo="";var invalidForm=await Form(id);
            var response=await Submit(id,invalid,invalidForm);var html=await response.Content.ReadAsStringAsync();
            check(response.StatusCode==HttpStatusCode.OK && Hidden(html,"VersaoConcorrencia")==invalidForm.Token && (await Read(id)).VersaoConcorrencia==invalidForm.Token,"erro de validação reapresenta token literal sem gravar");
            invalid.Titulo="Correção após erro";
            check((await Submit(id,invalid,(Hidden(html,"VersaoConcorrencia"),Hidden(html,"__RequestVerificationToken")))).StatusCode==HttpStatusCode.Redirect,"formulário reapresentado permite corrigir e salvar");
        }
        var deleteId=await Legacy();var deleteForm=await Form(deleteId,"Delete");
        check((await Submit(deleteId,await Read(deleteId),deleteForm,"Delete")).StatusCode==HttpStatusCode.Redirect,"exclusão HTTP autorizada de legado sem histórico");
        await using(var db=Db()) check(!await db.Requisitos.AnyAsync(r=>r.Id==deleteId),"legado sem histórico efetivamente removido apenas na cópia");
        var historyId=await Legacy(true);var historyForm=await Form(historyId,"Delete");
        var historyBefore=JsonSerializer.Serialize(await Read(historyId));
        var blocked=await Submit(historyId,await Read(historyId),historyForm,"Delete");
        check(blocked.StatusCode==HttpStatusCode.Redirect && blocked.Headers.Location!.OriginalString.Contains("Details") && JsonSerializer.Serialize(await Read(historyId))==historyBefore,"legado com histórico e token vazio tem exclusão bloqueada");
        await using(var db=Db()) check(await db.AnalisesRequisitos.AnyAsync(a=>a.RequisitoId==historyId),"histórico do legado preservado");
        foreach(var action in new[]{"Edit","Delete","Analisar"}) {
            var otherHtml=await other.GetStringAsync("/Requisitos/Create");var anti=Hidden(otherHtml,"__RequestVerificationToken");
            check((await other.GetAsync($"/Requisitos/{action}/{historyId}")).StatusCode==HttpStatusCode.NotFound,"GET legado de outro usuário bloqueado: "+action);
            check((await Submit(historyId,await Read(historyId),("",anti),action,user:other)).StatusCode==HttpStatusCode.NotFound,"POST legado alheio com token correto bloqueado: "+action);
            check(JsonSerializer.Serialize(await Read(historyId))==historyBefore,"isolamento preserva legado sem alterações");
        }
        // Concorrência EF também deve funcionar quando o valor original é vazio.
        var concurrent=await Legacy();
        await using(var first=Db()) await using(var second=Db()) {
            var x=await first.Requisitos.FindAsync(concurrent);var y=await second.Requisitos.FindAsync(concurrent);
            x!.Titulo+=" primeiro";await first.SaveChangesAsync();y!.Titulo+=" segundo";
            var rejected=false;try{await second.SaveChangesAsync();}catch(DbUpdateConcurrencyException){rejected=true;}
            check(rejected,"EF recusa sobrescrita quando versão original de ambos é vazia");
        }
        var staleDelete=await Legacy();var oldDelete=await Form(staleDelete,"Delete");var edit=await Form(staleDelete);var editRow=await Read(staleDelete);editRow.Titulo+=" editado";
        await Submit(staleDelete,editRow,edit);
        check((await Submit(staleDelete,editRow,oldDelete,"Delete")).StatusCode==HttpStatusCode.Conflict,"exclusão aberta antes da primeira edição é recusada");
        reset(0);var analysisId=await Legacy();var analysisRow=await Read(analysisId);var analysisForm=await Form(analysisId);analysisRow.Status=StatusRequisito.Revisado;
        await Submit(analysisId,analysisRow,analysisForm);
        var antiAnalysis=(await Form(analysisId)).Anti;
        check((await client.PostAsync("/Requisitos/Analisar/"+analysisId,new FormUrlEncodedContent([new KeyValuePair<string,string>("__RequestVerificationToken",antiAnalysis)]))).StatusCode==HttpStatusCode.OK && (await Read(analysisId)).Status==StatusRequisito.EmAnalise,"Gemini simulada sem sugestões reabre requisito Revisado");
        var human=await Read(analysisId);var humanForm=await Form(analysisId);human.Status=StatusRequisito.Revisado;
        check((await Submit(analysisId,human,humanForm)).StatusCode==HttpStatusCode.Redirect && (await Read(analysisId)).Status==StatusRequisito.Revisado,"revisão humana sem sugestões concluída explicitamente pela edição");
        await using(var db=Db()) {
            await db.Database.OpenConnectionAsync();using var cmd=db.Database.GetDbConnection().CreateCommand();cmd.CommandText="PRAGMA integrity_check";
            check((string?)await cmd.ExecuteScalarAsync()=="ok","cópia após regressão íntegra");cmd.CommandText="PRAGMA foreign_key_check";
            using var reader=await cmd.ExecuteReaderAsync();check(!await reader.ReadAsync(),"cópia após regressão sem violações de FK");
        }
        await app.StopAsync();
    }
}
