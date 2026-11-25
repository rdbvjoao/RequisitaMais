using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RequisitaPlus.Web.Controllers;
using RequisitaPlus.Web.Data;
using RequisitaPlus.Web.Models;
using RequisitaPlus.Web.Services;

internal static class HumanReviewRegression
{
    private const string Completed="Todas as sugestões foram avaliadas. O requisito foi marcado automaticamente como Revisado.";
    public static async Task Run(HttpClient owner, HttpClient other, Func<RequisitosContext> dbFactory,
        Action<bool,string> check, Action<int> reset, Func<AnaliseRequisitoService> service, string database)
    {
        Console.WriteLine("INÍCIO HumanReviewRegression");
        string Hidden(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        async Task<HttpResponseMessage> Post(string url,Dictionary<string,string> data,HttpClient? client=null) {
            client??=owner;
            var html=await client.GetStringAsync("/Requisitos/Create");
            data["__RequestVerificationToken"]=Hidden(html,"__RequestVerificationToken");
            return await client.PostAsync(url,new FormUrlEncodedContent(data));
        }
        async Task<Requisito> Read(int id) {await using var db=dbFactory();return await db.Requisitos.AsNoTracking().SingleAsync(r=>r.Id==id);}
        async Task<int> Latest(int id) {await using var db=dbFactory();return await db.AnalisesRequisitos.Where(a=>a.RequisitoId==id).OrderByDescending(a=>a.DataAnalise).ThenByDescending(a=>a.Id).Select(a=>a.Id).FirstAsync();}
        async Task<(int Id,int Analysis)> Create(int count) {
            int id;
            await using(var db=dbFactory()) {
                var r=new Requisito{ProprietarioId="user-a",Titulo="Revisão sintética",Descricao="Descrição original",Tipo=TipoRequisito.Funcional};
                db.Add(r);await db.SaveChangesAsync();id=r.Id;
            }
            check((await Read(id)).Status==StatusRequisito.Pendente,"requisito novo permanece Pendente");
            reset(count);
            check((await Post("/Requisitos/Analisar/"+id,new())).StatusCode==HttpStatusCode.OK && (await Read(id)).Status==StatusRequisito.EmAnalise,"análise simulada inicia revisão Em análise");
            return(id,await Latest(id));
        }
        async Task<Dictionary<string,string>> Form(int analysis,int index,TipoDecisaoSugestao decision) {
            var html=await owner.GetStringAsync($"/Requisitos/Revisar/{analysis}?indice={index}");
            return new(){["AnaliseId"]=analysis.ToString(),["IndiceSugestao"]=index.ToString(),["VersaoRequisito"]=Hidden(html,"VersaoRequisito"),
                ["Decisao"]=((int)decision).ToString(),["Confirmacao"]="true",["RedacaoFinal"]=decision==TipoDecisaoSugestao.Descartar?"":"Redação humana "+index};
        }
        async Task<string> History(int id) {
            await using var db=dbFactory();
            var analyses=await db.AnalisesRequisitos.AsNoTracking().Where(a=>a.RequisitoId==id).OrderBy(a=>a.Id).ToListAsync();
            var decisions=await db.DecisoesSugestoes.AsNoTracking().Where(d=>d.AnaliseRequisito.RequisitoId==id).OrderBy(d=>d.Id).ToListAsync();
            return JsonSerializer.Serialize(new {analyses,decisions});
        }
        foreach(var decisions in new[] {
            new[]{TipoDecisaoSugestao.Aceitar,TipoDecisaoSugestao.Aceitar,TipoDecisaoSugestao.Aceitar},
            new[]{TipoDecisaoSugestao.Ajustar,TipoDecisaoSugestao.Ajustar,TipoDecisaoSugestao.Ajustar},
            new[]{TipoDecisaoSugestao.Descartar,TipoDecisaoSugestao.Descartar,TipoDecisaoSugestao.Descartar},
            new[]{TipoDecisaoSugestao.Aceitar,TipoDecisaoSugestao.Ajustar,TipoDecisaoSugestao.Descartar},
            new[]{TipoDecisaoSugestao.Aceitar},new[]{TipoDecisaoSugestao.Ajustar},new[]{TipoDecisaoSugestao.Descartar}
        }) {
            var item=await Create(decisions.Length);
            string json;await using(var db=dbFactory()) json=(await db.AnalisesRequisitos.FindAsync(item.Analysis))!.ResultadoJson;
            for(var i=0;i<decisions.Length;i++) {
                var before=await Read(item.Id);var form=await Form(item.Analysis,i,decisions[i]);
                var response=await Post("/Requisitos/Revisar",form);
                check(response.StatusCode==HttpStatusCode.Redirect,"decisão HTTP confirmada: "+decisions[i]);
                var after=await Read(item.Id);var last=i==decisions.Length-1;
                check(after.Status==(last?StatusRequisito.Revisado:StatusRequisito.EmAnalise),last?"última decisão conclui Revisado":"revisão parcial mantém Em análise");
                check(after.VersaoConcorrencia!=before.VersaoConcorrencia && Guid.TryParseExact(after.VersaoConcorrencia,"N",out _),"cada decisão renova GUID inclusive Descartar");
                check(after.Descricao==(decisions[i]==TipoDecisaoSugestao.Descartar?before.Descricao:form["RedacaoFinal"]),"texto depende somente da decisão humana");
                var html=WebUtility.HtmlDecode(await owner.GetStringAsync(response.Headers.Location!));
                check(html.Contains(last?Completed:"Decisão registrada com sucesso."),"mensagem de sucesso corresponde à conclusão");
                await using(var db=dbFactory()) {
                    check(await db.DecisoesSugestoes.CountAsync(d=>d.AnaliseRequisitoId==item.Analysis)==i+1 &&
                        (await db.AnalisesRequisitos.FindAsync(item.Analysis))!.ResultadoJson==json,"decisões acumuladas e JSON original preservados");
                    check(await db.DecisoesSugestoes.Where(d=>d.AnaliseRequisitoId==item.Analysis).AllAsync(d=>d.UsuarioId=="user-a"),"autoria das decisões preservada");
                }
            }
            var oldHistory=await History(item.Id);reset(0);
            await Post("/Requisitos/Analisar/"+item.Id,new());
            check((await Read(item.Id)).Status==StatusRequisito.EmAnalise,"reanálise bem-sucedida de Revisado reabre Em análise");
            await using(var db=dbFactory()) {
                check((await db.AnalisesRequisitos.FindAsync(item.Analysis))!.ResultadoJson==json && await db.DecisoesSugestoes.CountAsync(d=>d.AnaliseRequisitoId==item.Analysis)==decisions.Length,"reanálise preserva análise e decisões anteriores");
            }
        }
        // Status manuais permitidos continuam disponíveis, mas uma nova decisão
        // parcial restabelece o status que corresponde à revisão em andamento.
        var partial=await Create(2);
        var editHtml=await owner.GetStringAsync("/Requisitos/Edit/"+partial.Id);var editRow=await Read(partial.Id);
        Dictionary<string,string> EditData(Requisito r,string token,string status)=>new(){["Titulo"]=r.Titulo,["Descricao"]=r.Descricao,["Tipo"]="1",["Status"]=status,["VersaoConcorrencia"]=token};
        check((await Post("/Requisitos/Edit/"+partial.Id,EditData(editRow,Hidden(editHtml,"VersaoConcorrencia"),"1"))).StatusCode==HttpStatusCode.Redirect && (await Read(partial.Id)).Status==StatusRequisito.Pendente,"alteração manual para Pendente preservada");
        await Post("/Requisitos/Revisar",await Form(partial.Analysis,0,TipoDecisaoSugestao.Descartar));
        check((await Read(partial.Id)).Status==StatusRequisito.EmAnalise,"decisão parcial após alteração manual retorna a Em análise");
        editHtml=await owner.GetStringAsync("/Requisitos/Edit/"+partial.Id);editRow=await Read(partial.Id);
        check((await Post("/Requisitos/Edit/"+partial.Id,EditData(editRow,Hidden(editHtml,"VersaoConcorrencia"),"3"))).StatusCode==HttpStatusCode.OK && (await Read(partial.Id)).Status==StatusRequisito.EmAnalise,"Revisado manual bloqueado com sugestões pendentes");
        // Dois formulários, a mesma decisão reenviada, edição interveniente e análise antiga.
        var stale=await Create(2);
        var first=await Form(stale.Analysis,0,TipoDecisaoSugestao.Descartar);var second=await Form(stale.Analysis,1,TipoDecisaoSugestao.Descartar);
        await Post("/Requisitos/Revisar",first);var snapshot=await History(stale.Id);var staleRow=JsonSerializer.Serialize(await Read(stale.Id));
        await Post("/Requisitos/Revisar",second);await Post("/Requisitos/Revisar",first);
        check(await History(stale.Id)==snapshot && JsonSerializer.Serialize(await Read(stale.Id))==staleRow,"formulários concorrentes e reenvio não concluem indevidamente");
        var fresh=await Form(stale.Analysis,1,TipoDecisaoSugestao.Descartar);
        await Post("/Requisitos/Revisar",fresh);
        check((await Read(stale.Id)).Status==StatusRequisito.Revisado,"última decisão reaberta com versão atual conclui revisão");
        var old=await Create(1);var oldForm=await Form(old.Analysis,0,TipoDecisaoSugestao.Aceitar);
        reset(2);await Post("/Requisitos/Analisar/"+old.Id,new());var oldSnapshot=await History(old.Id);var oldRow=JsonSerializer.Serialize(await Read(old.Id));
        check((await owner.GetAsync($"/Requisitos/Revisar/{old.Analysis}?indice=0")).StatusCode==HttpStatusCode.Redirect,"GET análise antiga somente consulta");
        await Post("/Requisitos/Revisar",oldForm);
        check(await History(old.Id)==oldSnapshot && JsonSerializer.Serialize(await Read(old.Id))==oldRow,"decisão de análise antiga não altera status nem histórico");
        var edited=await Create(1);var beforeEdit=await Form(edited.Analysis,0,TipoDecisaoSugestao.Aceitar);
        editHtml=await owner.GetStringAsync("/Requisitos/Edit/"+edited.Id);editRow=await Read(edited.Id);editRow.Titulo+=" alterado";
        await Post("/Requisitos/Edit/"+edited.Id,EditData(editRow,Hidden(editHtml,"VersaoConcorrencia"),"2"));
        var editedHistory=await History(edited.Id);await Post("/Requisitos/Revisar",beforeEdit);
        check((await Read(edited.Id)).Status==StatusRequisito.EmAnalise && await History(edited.Id)==editedHistory,"edição interveniente invalida última decisão antiga");
        var foreignForm=await Form(edited.Analysis,0,TipoDecisaoSugestao.Descartar);var foreignRow=JsonSerializer.Serialize(await Read(edited.Id));
        check((await other.GetAsync($"/Requisitos/Revisar/{edited.Analysis}?indice=0")).StatusCode==HttpStatusCode.NotFound,"outro usuário não abre revisão");
        check((await Post("/Requisitos/Revisar",foreignForm,other)).StatusCode==HttpStatusCode.NotFound && await History(edited.Id)==editedHistory && JsonSerializer.Serialize(await Read(edited.Id))==foreignRow,"POST alheio com hash correto não registra decisão nem conclui");
        foreignForm.Remove("__RequestVerificationToken");
        check((await owner.PostAsync("/Requisitos/Revisar",new FormUrlEncodedContent(foreignForm))).StatusCode==HttpStatusCode.BadRequest,"última decisão exige antiforgery");
        // Falha real no INSERT e no UPDATE, inclusive depois de o INSERT ser
        // executado. O estado persistido inteiro deve permanecer idêntico.
        foreach(var failure in new[]{"insert","update","concurrency","cancel"}) {
            var item=await Create(2);
            await Post("/Requisitos/Revisar",await Form(item.Analysis,0,TipoDecisaoSugestao.Descartar));
            var rowBefore=JsonSerializer.Serialize(await Read(item.Id));var historyBefore=await History(item.Id);
            if(failure is "insert" or "update") {
                await using var db=dbFactory();
                await db.Database.ExecuteSqlRawAsync(failure=="insert"
                    ? "CREATE TRIGGER FailLastDecision BEFORE INSERT ON DecisoesSugestoes BEGIN SELECT RAISE(ABORT, 'synthetic'); END;"
                    : "CREATE TRIGGER FailLastStatus BEFORE UPDATE ON Requisitos WHEN NEW.Status=3 BEGIN SELECT RAISE(ABORT, 'synthetic'); END;");
                var response=await Post("/Requisitos/Revisar",await Form(item.Analysis,1,TipoDecisaoSugestao.Aceitar));
                check(response.StatusCode==HttpStatusCode.OK && !WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains(Completed),"falha "+failure+" informa erro sem sucesso");
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS FailLastDecision;");await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS FailLastStatus;");
            } else {
                using var cancel=new CancellationTokenSource();
                IInterceptor interceptor=failure=="concurrency"?new StaleOriginalToken():new CancelSaved(cancel);
                await using var db=new RequisitosContext(new DbContextOptionsBuilder<RequisitosContext>().UseSqlite("Data Source="+database).AddInterceptors(interceptor).Options);
                var http=new DefaultHttpContext {User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,"user-a")],"test"))};
                var controller=new RequisitosController(db,NullLogger<RequisitosController>.Instance) {ControllerContext=new ControllerContext{HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())};
                var model=(RevisarSugestaoViewModel)((ViewResult)await controller.Revisar(item.Analysis,1,default)).Model!;
                model.Decisao=TipoDecisaoSugestao.Aceitar;model.Confirmacao=true;model.RedacaoFinal="Texto que não deve persistir";
                if(failure=="concurrency") check(await controller.Revisar(model,default) is ViewResult && !controller.ModelState.IsValid,"conflito EF na última decisão é tratado sem sucesso");
                else {
                    var canceled=false;try{await controller.Revisar(model,cancel.Token);}catch(OperationCanceledException){canceled=true;}
                    check(canceled,"cancelamento após SaveChanges impede confirmação da última decisão");
                }
                check(!controller.TempData.ContainsKey("Sucesso"),"falha não publica mensagem de conclusão");
            }
            check(JsonSerializer.Serialize(await Read(item.Id))==rowBefore && await History(item.Id)==historyBefore,"falha "+failure+" preserva status, texto, GUID, análises e decisões sem gravação parcial");
            await Post("/Requisitos/Revisar",await Form(item.Analysis,1,TipoDecisaoSugestao.Descartar));
            check((await Read(item.Id)).Status==StatusRequisito.Revisado,"reenvio após falha "+failure+" conclui normalmente");
        }
        Console.WriteLine("FIM HumanReviewRegression");
    }
    private sealed class EmptyTempData : ITempDataProvider {
        public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values) { }
    }
    private sealed class StaleOriginalToken : SaveChangesInterceptor {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default) {
            foreach(var entry in data.Context!.ChangeTracker.Entries<Requisito>().Where(e=>e.State==EntityState.Modified))
                entry.Property(r=>r.VersaoConcorrencia).OriginalValue=Guid.NewGuid().ToString("N");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CancelSaved(CancellationTokenSource cancel) : SaveChangesInterceptor {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,int result,CancellationToken cancellationToken=default) {
            cancel.Cancel();return ValueTask.FromResult(result);
        }
    }
}
