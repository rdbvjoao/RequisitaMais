# Envio de e-mails pelo Gmail

`IEmailSender.SendAsync(destinatario, assunto, corpo, cancellationToken)` é registrado
como serviço transient. Nenhum controlador ou fluxo de autenticação o chama nesta
etapa. Não há endpoints de teste ou telas de recuperação de senha.

`GmailEmailSender` usa o cliente SMTP incluído no .NET, sem pacotes adicionais.
O servidor é `smtp.gmail.com`, porta 587, com `EnableSsl = true`: exige STARTTLS
antes da autenticação/envio e mantém a validação padrão do certificado. Não há
fallback para conexão sem TLS. Remetente e usuário de autenticação são fixos em
`requisitaplus.sistema@gmail.com`. Cada chamada envia texto simples em UTF-8 para
um único destinatário, com limite de 30 segundos e sem repetição automática.

A senha é lida somente de `Email:Smtp:Password` no `IConfiguration`. No ambiente
Development da aplicação, o ASP.NET Core carrega os User Secrets já existentes.
A ausência da senha impede o envio, mas não impede iniciar a aplicação. Não
adicionar senhas ao código, appsettings, linha de comando ou logs. O serviço não
registra conteúdo, destinatários ou credenciais; falhas SMTP recebem mensagem
genérica sem a resposta bruta do servidor. User Secrets ficam fora do projeto e
não são criptografados: dependem da proteção da conta e do sistema de arquivos.
Em produção, usar um provedor seguro para a mesma chave de configuração.
Nenhuma configuração da Gemini é alterada.

## Um único teste real, somente com autorização posterior

Este procedimento **não foi executado nesta implementação**. Executar somente
depois de autorização explícita para enviar uma mensagem real. Não usar em CI,
scripts automáticos ou durante a inicialização da aplicação.

No PowerShell, na raiz do projeto:

```powershell
dotnet run --project .\tools\EmailSmokeTest\EmailSmokeTest.csproj -- --send-one
```

O utilitário informa o destinatário fixo (a própria conta do projeto). Digite
`ENVIAR UM EMAIL` apenas quando autorizar o envio real. Sem essa confirmação,
encerra sem carregar secrets ou conectar ao Gmail. Sem `--send-one`, também não
envia nada. Ele carrega os User Secrets pelo mesmo identificador do projeto,
lê a senha pela mesma chave e faz uma única chamada ao serviço. Não inicia a
aplicação web, não referencia EF/Identity e não abre o SQLite. A mensagem contém
somente texto de teste, sem códigos de recuperação ou dados reais de usuários.

O sucesso indica aceitação pelo SMTP; confira manualmente a caixa de entrada e
o spam. Em falha ou timeout, confira a caixa antes de qualquer nova tentativa:
o servidor pode ter aceitado a mensagem sem o cliente receber a confirmação.
Não repetir sem nova autorização. Não listar ou imprimir User Secrets para
diagnosticar o teste.

## Validação desta implementação

Builds da aplicação web e do utilitário concluídos com zero erros e zero avisos,
com saídas em `.local/EmailBuild`. Nove verificações offline em
`.local/EmailChecks` passaram: registro e ciclo de vida do serviço, senha ausente,
destinatário vazio/inválido, múltiplos destinatários, quebras de linha nos
cabeçalhos, corpo vazio e cancelamento prévio. Também foram executados os caminhos
sem opção de envio e com confirmação recusada do utilitário manual.
Não foram carregadas credenciais reais, abertas conexões SMTP ou acessados bancos
e backups. Não foram executadas migrations nem iniciada a aplicação web.
Autenticação no Gmail e entrega real continuam pendentes do teste autorizado.

Referências: [SMTP do Gmail](https://developers.google.com/workspace/gmail/imap/imap-smtp)
e [STARTTLS no cliente .NET](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.enablessl?view=net-10.0).
