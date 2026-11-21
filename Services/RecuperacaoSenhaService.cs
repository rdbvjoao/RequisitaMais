using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace RequisitaPlus.Web.Services;

// Estado de uma única instância. Gate serializa validação, substituição e consumo.
public sealed class RecuperacaoSenhaStore
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal readonly Dictionary<string, Pedido> Pedidos = new();
    internal readonly Dictionary<string, List<DateTimeOffset>> Limites = new();
    internal readonly Dictionary<string, DateTimeOffset> UltimasSolicitacoes = new();
    internal sealed class Pedido(string email, DateTimeOffset criado)
    {
        internal string Email = email;
        internal DateTimeOffset Criado = criado;
        internal string? UsuarioId;
        internal string Proposito = "RecuperacaoSenha:" + Guid.NewGuid().ToString("N");
        internal int Tentativas;
        internal bool Enviado;
        internal string? TokenRedefinicao;
    }
}

public sealed class RecuperacaoSenhaService(
    RecuperacaoSenhaStore store, UserManager<IdentityUser> users,
    IEmailSender emailSender, TimeProvider clock, ILogger<RecuperacaoSenhaService> logger,
    RecuperacaoSenhaEntrega entregas)
{
    public const string MensagemSolicitacao = "Se houver uma conta com esse e-mail, enviaremos um código. Aguarde 60 segundos entre envios. São permitidas até 5 solicitações por e-mail a cada 15 minutos.";
    public const string ErroCodigo = "Código inválido ou expirado. São permitidas até 5 tentativas. Solicite um novo código se necessário.";
    public const string ErroAutorizacao = "A recuperação expirou ou não está autorizada. Solicite um novo código.";
    private DateTimeOffset Agora => clock.GetUtcNow();

    public async Task<string?> SolicitarAsync(string email, string ip)
    {
        var normalizado = users.NormalizeEmail(email.Trim());
        string handle;
        RecuperacaoSenhaStore.Pedido pedido;
        IdentityUser? user;
        await store.Gate.WaitAsync();
        try
        {
            Limpar();
            // Contabiliza contas inexistentes, falhas de SMTP e tentativas no cooldown.
            if (!Permitir("ip:" + ip, 20) || !Permitir("email:" + normalizado, 5)) return null;
            if (store.UltimasSolicitacoes.ContainsKey(normalizado)) return null;
            user = await users.FindByEmailAsync(email.Trim());
            foreach (var key in store.Pedidos.Where(x => x.Value.Email == normalizado ||
                (user != null && x.Value.UsuarioId == user.Id)).Select(x => x.Key).ToArray())
                store.Pedidos.Remove(key);
            if (store.Pedidos.Count >= 10000) return null;
            handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            pedido = new(normalizado, Agora) { UsuarioId = user?.Id };
            store.Pedidos.Add(handle, pedido);
            store.UltimasSolicitacoes[normalizado] = Agora;
        }
        finally { store.Gate.Release(); }

        // Inclusive contas inexistentes entram na mesma fila, sem expor a existência.
        // Se a fila estiver cheia, a solicitação permanece incapaz de validar.
        entregas.Agendar(handle);
        return handle;
    }

    public async Task EntregarAsync(string handle, CancellationToken cancellationToken = default)
    {
        RecuperacaoSenhaStore.Pedido? pedido;
        await store.Gate.WaitAsync(cancellationToken);
        try
        {
            pedido = Obter(handle);
            if (pedido?.UsuarioId == null || pedido.Enviado) return;
        }
        finally { store.Gate.Release(); }

        // SMTP fora do bloqueio: uma entrega obsoleta nunca reativa a solicitação.
        try
        {
            var user = await users.FindByIdAsync(pedido.UsuarioId);
            if (user == null) return;
            // API genérica: não exige confirmação prévia como a API de 2FA.
            var codigo = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, pedido.Proposito);
            await emailSender.SendAsync(user.Email!, "Recuperação de senha — Requisita+",
                $"Seu código de recuperação do Requisita+ é: {codigo}\n\nEle vale por 5 minutos a partir da solicitação, somente no navegador que iniciou a recuperação.\nSe você não solicitou, ignore esta mensagem.", cancellationToken);
            await store.Gate.WaitAsync(cancellationToken);
            try
            {
                if (Obter(handle) == pedido) pedido.Enviado = true;
            }
            finally { store.Gate.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            // Não registrar exceções, destinatários, códigos ou respostas SMTP.
            logger.LogWarning("Não foi possível concluir um envio de recuperação de senha.");
        }
    }

    public async Task<bool> ValidarAsync(string? handle, string codigo)
    {
        await store.Gate.WaitAsync();
        try
        {
            var p = Obter(handle);
            if (p == null || p.TokenRedefinicao != null || p.Tentativas >= 5) return false;
            p.Tentativas++;
            if (!p.Enviado || p.UsuarioId == null || codigo.Length != 6 || codigo.Any(c => c < '0' || c > '9')) return false;
            var user = await users.FindByIdAsync(p.UsuarioId);
            if (user == null || !await users.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider, p.Proposito, codigo)) return false;
            p.TokenRedefinicao = await users.GeneratePasswordResetTokenAsync(user);
            if (Obter(handle) != p) return false;
            return true;
        }
        finally { store.Gate.Release(); }
    }

    public async Task<bool> AutorizadoAsync(string? handle)
    {
        await store.Gate.WaitAsync();
        try { return Obter(handle)?.TokenRedefinicao != null; }
        finally { store.Gate.Release(); }
    }

    public async Task<IdentityResult> RedefinirAsync(string? handle, string senha)
    {
        await store.Gate.WaitAsync();
        try
        {
            var p = Obter(handle);
            if (p?.TokenRedefinicao == null || p.UsuarioId == null) return Falha();
            var user = await users.FindByIdAsync(p.UsuarioId);
            if (user == null) return Falha();
            if (Obter(handle) != p) return Falha();
            var result = await users.ResetPasswordAsync(user, p.TokenRedefinicao, senha);
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                store.Pedidos.Remove(handle!);
                return Falha();
            }
            if (result.Succeeded) store.Pedidos.Remove(handle!);
            return result;
        }
        finally { store.Gate.Release(); }
    }

    private static IdentityResult Falha() => IdentityResult.Failed(new IdentityError { Description = ErroAutorizacao });
    private RecuperacaoSenhaStore.Pedido? Obter(string? handle)
    {
        Limpar();
        return handle != null && store.Pedidos.TryGetValue(handle, out var p) ? p : null;
    }
    private bool Permitir(string key, int maximo)
    {
        if (!store.Limites.TryGetValue(key, out var lista))
        {
            if (store.Limites.Count >= 20000) return false;
            store.Limites[key] = lista = new();
        }
        if (lista.Count >= maximo) return false;
        lista.Add(Agora);
        return true;
    }
    private void Limpar()
    {
        var now = Agora;
        foreach (var key in store.UltimasSolicitacoes.Where(x => now - x.Value >= TimeSpan.FromSeconds(60)).Select(x => x.Key).ToArray()) store.UltimasSolicitacoes.Remove(key);
        foreach (var key in store.Pedidos.Where(x => now - x.Value.Criado >= TimeSpan.FromMinutes(5)).Select(x => x.Key).ToArray()) store.Pedidos.Remove(key);
        foreach (var key in store.Limites.Keys.ToArray())
        {
            store.Limites[key].RemoveAll(t => now - t >= TimeSpan.FromMinutes(15));
            if (store.Limites[key].Count == 0) store.Limites.Remove(key);
        }
    }
}
