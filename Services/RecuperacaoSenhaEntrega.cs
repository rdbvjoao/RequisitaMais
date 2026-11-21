using System.Threading.Channels;

namespace RequisitaPlus.Web.Services;

// Desacopla o tempo de resposta HTTP do SMTP. A fila contém apenas identificadores,
// nunca códigos, senhas ou destinatários. Sem persistência e sem repetição automática.
public sealed class RecuperacaoSenhaEntrega(IServiceScopeFactory scopes) : BackgroundService
{
    private readonly Channel<string> fila = Channel.CreateBounded<string>(32);

    public bool Agendar(string handle) => fila.Writer.TryWrite(handle);

    // Também permite exercitar exatamente o processamento do worker em testes isolados.
    public async Task<bool> ProcessarProximoAsync(CancellationToken cancellationToken = default)
    {
        if (!fila.Reader.TryRead(out var handle)) return false;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RecuperacaoSenhaService>()
            .EntregarAsync(handle, cancellationToken);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await fila.Reader.WaitToReadAsync(stoppingToken))
                while (await ProcessarProximoAsync(stoppingToken)) { }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
