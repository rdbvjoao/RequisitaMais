using Microsoft.Extensions.Configuration;
using RequisitaPlus.Web.Services;

// Sem a opção explícita, não carrega secrets nem abre conexão de rede.
if (args.Length != 1 || args[0] != "--send-one")
{
    Console.WriteLine("Nenhum envio realizado. Para teste manual autorizado, use --send-one.");
    return 0;
}

Console.WriteLine($"Será enviada uma única mensagem de teste para {GmailEmailSender.SenderAddress}.");
Console.Write("Para autorizar este envio real, digite ENVIAR UM EMAIL: ");
if (Console.ReadLine() != "ENVIAR UM EMAIL")
{
    Console.WriteLine("Teste cancelado. Nenhum envio realizado.");
    return 0;
}

try
{
    // Apenas User Secrets: sem argumentos, arquivos locais ou variáveis com senhas.
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<Program>(optional: true)
        .Build();
    var sender = new GmailEmailSender(configuration);
    await sender.SendAsync(GmailEmailSender.SenderAddress,
        "Requisita+ — teste manual de SMTP",
        "Teste manual autorizado da infraestrutura de e-mail do Requisita+. Nenhum código ou dado de usuário foi incluído.");
    Console.WriteLine("Uma mensagem foi aceita pelo servidor SMTP. Confira a caixa de entrada e o spam.");
    return 0;
}
catch (Exception)
{
    // Nunca imprimir exceções, configuração ou credenciais.
    Console.Error.WriteLine("O teste não confirmou o envio. Verifique a configuração e a conexão. Não repita automaticamente: confira a caixa de entrada antes de uma nova tentativa autorizada.");
    return 1;
}
