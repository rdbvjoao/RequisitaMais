# Requisita+

Infraestrutura de e-mail: consulte [SMTP do Gmail e teste manual autorizado](docs/email-smtp.md).

Aplicação de gerenciamento e controle de requisitos para a disciplina Laboratório de Engenharia de Software. ASP.NET Core MVC (.NET 10), Razor, Bootstrap, EF Core 10 e SQLite.

## Executar

No PowerShell, dentro de `C:\Users\joaov\requisita-plus-simples`:

```powershell
dotnet run --no-restore --launch-profile http
```

Acesse http://localhost:5238. Encerre com Ctrl+C. É necessário o SDK do .NET 10. As dependências já foram restauradas neste ambiente. Em uma instalação nova, execute `dotnet restore` antes.

## Banco local

O arquivo `requisita-plus.db`, na raiz do projeto, foi criado pela migration `Inicial`. A aplicação usa somente esse arquivo. Não aplica migrations nem recria o banco automaticamente ao iniciar.

Somente em uma instalação nova, sem o arquivo de banco, crie a estrutura com as ferramentas EF Core 10:

```powershell
if (Test-Path -LiteralPath .\requisita-plus.db) {
    throw 'O banco já existe. Não recriar nem aplicar alterações sem autorização.'
}
dotnet ef database update
```

Não execute comandos de recriação, remoção ou atualização de um banco existente sem autorização.

## Funcionalidades

- Cadastro com título e descrição obrigatórios, tipo Funcional ou Não Funcional e data automática.
- Listagem, detalhes, edição e exclusão com confirmação.
- Status Pendente, Em análise ou Revisado; Pendente é o padrão do cadastro.
- Filtros combináveis por tipo e status.
- Validação no servidor e no navegador, proteção antifalsificação e mensagens em português.

A data e o identificador não são aceitos como campos editáveis do formulário. A edição preserva a data original. Títulos têm limite de 150 caracteres e descrições de 5.000 caracteres.

## Verificação inicial do gerenciamento

`dotnet build --no-restore`: compilação concluída com zero erros e zero avisos.

Verificação HTTP do cadastro, detalhes, edição, filtros, exclusão, campos obrigatórios, valores inválidos e preservação da data. Bootstrap servido localmente. O registro temporário usado nessa verificação foi excluído, deixando o banco sem requisitos de exemplo.

Neste ambiente restrito, a restauração foi feita pelo cache de pacotes já instalado, com configuração e diretórios temporários dentro do projeto. A verificação HTTP usou o provedor de log EventLog desabilitado somente no processo de teste, por falta de acesso ao log do Windows. Nenhuma dessas variáveis foi gravada no sistema.

## Arquivos da implementação

Criados:

- `Models/Requisito.cs` (única entidade de domínio e enums de tipo/status).
- `Data/RequisitosContext.cs`.
- `Controllers/RequisitosController.cs`.
- `Views/Requisitos/Index.cshtml`, `Create.cshtml`, `Details.cshtml`, `Edit.cshtml`, `Delete.cshtml`, `_Form.cshtml` e `_Details.cshtml`.
- `Migrations/20261112234642_Inicial.cs`, `20261112234642_Inicial.Designer.cs` e `RequisitosContextModelSnapshot.cs`.
- `NuGet.Config`, `.gitignore`, `README.md` e `requisita-plus.db`.

Modificados:

- `RequisitaPlus.Web.csproj`, `Program.cs`.
- `Views/Shared/_Layout.cshtml`, `Views/Shared/Error.cshtml`.
- `wwwroot/css/site.css`.

Artefatos locais de compilação e verificação ficam em `bin/`, `obj/` e `.local/`, ignorados pelo Git. O banco também está no `.gitignore` para preservar os dados locais.

## Autenticação (12/11/2026)

Cadastro em `/Conta/Cadastro`, login em `/Conta/Login` e saída pelo botão **Sair** do menu (POST com antiforgery). O cadastro autentica o usuário imediatamente. A senha exige pelo menos 8 caracteres, letra maiúscula, minúscula, número e símbolo. Cinco falhas de login bloqueiam a conta por cinco minutos.

ASP.NET Core Identity gerencia usuários e senhas com `UserManager` e `SignInManager`. O contexto usa `IdentityUserContext`, sem perfis administrativos. Todo o controlador de requisitos tem `[Authorize]`; visitantes são encaminhados ao login, com retorno apenas para URLs locais. Os requisitos continuam compartilhados entre usuários autenticados.

Cookies de sessão HttpOnly e SameSite=Lax, com validade de duas horas e renovação durante uso. Em Development, Secure acompanha HTTPS para permitir o perfil HTTP local; nos demais ambientes o cookie exige HTTPS. A aplicação continua sem aplicar migrations automaticamente.

A migration incremental `20261113001256_AdicionarIdentity` foi revisada e aplicada ao banco existente: cria somente quatro tabelas padrão do Identity e seus índices, sem alterações na tabela Requisitos. As tabelas padrão de logins e tokens fazem parte do armazenamento Identity; não há fluxos de autenticação externa. A recuperação de senha por código está descrita ao final deste documento.

Backups anteriores à migration preservados: `.local/backups/2026-11-13T00-11-14-469Z/requisita-plus.db` e `.local/backups/pre-identity-20261112-211153/requisita-plus.db`. Não havia arquivos auxiliares SQLite no momento da primeira cópia. Não use `EnsureCreated`, `EnsureDeleted` ou recriação do banco.

Revisão final: implementação existente aproveitada, sem duplicação de controllers, views ou configurações. As telas de conta usam os mesmos cartões e estilos da aplicação. A migration de autenticação já estava aplicada; nenhuma migration adicional foi necessária. `dotnet build --no-restore` passou com zero erros e avisos e `dotnet ef migrations has-pending-model-changes --no-build` confirmou que o modelo corresponde à última migration.

Cadastro, login, logout, senha fraca, confirmação de senha, e-mail duplicado, credenciais inválidas, bloqueio anônimo de todas as páginas de requisitos, retorno local e antiforgery foram verificados via HTTP em uma cópia isolada dentro de `.local/`. O roteiro está em `.local/validate-auth.cjs` e o resultado em `.local/auth-validation-result.json`. Nenhuma conta de teste foi criada no banco principal, que continua com zero usuários. O banco passou na verificação de integridade; o schema e todos os valores do requisito existente foram comparados com o primeiro backup e permanecem idênticos (1 registro). Não foram detectadas novas alterações simultâneas durante esta revisão.

Arquivos desta etapa: `RequisitaPlus.Web.csproj`, `Program.cs`, `Data/RequisitosContext.cs`, `Data/ErrosIdentity.cs`, `Controllers/ContaController.cs`, `Controllers/RequisitosController.cs`, `Models/ContaViewModels.cs`, `Views/Conta/Login.cshtml`, `Views/Conta/Cadastro.cshtml`, `Views/Shared/_Layout.cshtml`, a migration `AdicionarIdentity` com seu designer, `Migrations/RequisitosContextModelSnapshot.cs` e este README. O gerenciamento de requisitos recebeu apenas a proteção `[Authorize]`.

Referência técnica: [ASP.NET Core Identity — Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0).

## Análise de requisitos com Gemini API

Na listagem ou nos detalhes de um requisito salvo, clique em **Analisar com IA**.
O envio exige login e token antifalsificação. Somente o POST de análise chama a API;
abrir páginas não consome tokens. Cada solicitação faz uma única chamada, sem retries.
O botão e a tela de resultados foram preservados.

Configuração em `appsettings.json`, sem credenciais:

```json
"Gemini": {
  "Modelo": "gemini-3.5-flash-lite",
  "TimeoutSegundos": 120
}
```

A chave já configurada em `Gemini:ApiKey` nos User Secrets é carregada pelo ASP.NET
Core em Development (perfil `http`). Nunca coloque a chave no appsettings, código,
URLs ou logs. A chave é enviada somente no cabeçalho `x-goog-api-key`; os cabeçalhos
do cliente HTTP são ocultados nos logs e redirecionamentos estão desativados.
O timeout aceita de 1 a 600 segundos. A ausência de chave impede apenas a análise.

A integração usa `IHttpClientFactory` com cliente tipado e a API oficial:
`POST https://generativelanguage.googleapis.com/v1beta/models/{modelo}:generateContent`.
Título, tipo e descrição são enviados para identificar ambiguidades, falta de clareza
e informações incompletas. O prompt solicita problemas, justificativas e sugestões
em português brasileiro, sem inventar métricas, prazos ou regras de negócio.

A geração utiliza `responseMimeType: application/json` e `responseJsonSchema`.
A validação local exige resposta concluída (`finishReason: STOP`), campos obrigatórios,
categorias permitidas, até oito problemas e textos não vazios de até 1.500 caracteres.
Campos inesperados no resultado são rejeitados. Os textos são escapados pelo Razor.
Uma lista vazia não garante a perfeição do requisito: as sugestões exigem revisão humana.

Quando disponíveis em `usageMetadata`, `promptTokenCount`, `candidatesTokenCount` e
`totalTokenCount` aparecem como entrada, saída e total na tela e no log do serviço.
Contagens ausentes não são estimadas; o total é o informado pela API, podendo incluir
tokens de raciocínio além da saída. Contagens e diagnósticos sanitizados de erros são registrados, sem prompt,
resposta bruta ou chave. Há mensagens amigáveis para autenticação/permissões, cota
(incluindo gratuita), modelo indisponível, timeout, conexão e resposta inválida/bloqueada.

A análise não altera requisitos ou status. As análises concluídas com sucesso são
armazenadas no SQLite conforme a seção de histórico abaixo. Não foram adicionadas bibliotecas.

Validação desta etapa: compilação local e simulador em memória (`HttpMessageHandler`),
sem chamadas reais à Gemini. O roteiro está em `.local/GeminiChecks/Program.cs` e o
resultado em `.local/gemini-checks-result.json`. Foram verificados o envio, uma chamada
por solicitação, resultados válidos/vazios, tokens presentes/parciais/ausentes, rejeição
de JSON/schema/envelope inválidos, erros HTTP, timeout e cancelamento. A chave foi
verificada apenas em memória quanto à presença nos User Secrets e ausência nos arquivos
e logs do projeto. Banco, backups e migrations foram comparados por SHA-256.
A disponibilidade do modelo e a qualidade das sugestões reais não foram testadas nesta etapa.

Para executar, use o comando da seção **Executar**, acesse http://localhost:5238,
faça login e clique em **Analisar com IA** em um requisito salvo. Esse clique usa a API real.

Arquivos desta etapa: `Services/GeminiOptions.cs` (substitui `OllamaOptions.cs`),
`Services/AnaliseRequisitoService.cs`, `Models/AnaliseRequisitoViewModel.cs`,
`Views/Requisitos/Analisar.cshtml`, `Program.cs`, `appsettings.json` e este `README.md`.

Referências oficiais: [generateContent e usageMetadata](https://ai.google.dev/api/generate-content),
[JSON estruturado](https://ai.google.dev/gemini-api/docs/structured-output) e
[autenticação e chave](https://ai.google.dev/gemini-api/docs/api-key).

### Correção do diagnóstico da rejeição HTTP 400

Removido `generationConfig.candidateCount`: a documentação de
[parâmetros da Gemini](https://ai.google.dev/gemini-api/docs/troubleshooting#check-api)
informa que esse parâmetro não é suportado em Gemini 3. O padrão da API continua
sendo uma resposta. Modelo e endpoint foram preservados. O schema também foi
preservado: a documentação descreve `additionalProperties` e `maxItems` como aceitos
e `minLength`/`maxLength` como ignorados pelo servidor; os limites continuam validados localmente.

Os erros HTTP 400, 401, 403, 404 e 429 agora têm orientações distintas e diagnóstico
com `error.code`, `error.status` e `error.message`. A mensagem é sanitizada e limitada
a 600 caracteres; chave, título, descrição, URLs, identificadores de projeto, e-mails
e credenciais são ocultados. `error.details`, cabeçalhos e corpo bruto não são exibidos
nem registrados. Respostas não JSON ou com campos ausentes recebem diagnóstico padrão.

Validação: `dotnet build --no-restore`, zero erros e avisos; 40 verificações simuladas,
incluindo o parâmetro removido, erros estruturados por HTTP, ocultação de dados e consumo
de tokens. Nenhuma chamada real foi feita. A chave permanece nos User Secrets e não foi
encontrada nos arquivos textuais/configurações/logs inspecionados no projeto.
Banco, backups, Data e migrations permanecem idênticos por SHA-256.

A incompatibilidade de `candidateCount` foi identificada na documentação, mas não foi
confirmada como a causa do HTTP 400 observado. Para confirmar a causa específica falta
o diagnóstico real `error.code`, `error.status` e `error.message` da resposta de erro
de `generateContent`; ele agora será apresentado de forma sanitizada na tela existente.

## Histórico de análises (13/11/2026)

Após fazer login, abra os **Detalhes** de um requisito e clique em **Histórico de análises**.
A página `/Requisitos/Historico/{id}` mostra as análises mais recentes primeiro,
com data e horário local do servidor, categorias, problemas, justificativas, sugestões
e tokens de entrada, saída e total. Contagens ausentes aparecem como “não informado”;
o total informado pela API não é recalculado. Resultados sem problemas também são salvos.

Somente resultados validados e concluídos pelo serviço Gemini são persistidos, após
o retorno bem-sucedido do serviço. Falhas, respostas incompletas, timeout e cancelamento
não são registrados. O cancelamento da requisição também é passado à gravação no EF Core.
Uma falha de gravação apresenta uma mensagem informando que o resultado não foi salvo.
Não há operações de edição ou exclusão de análises na interface ou no controlador.

O histórico exige autenticação e faz apenas consultas ao SQLite, sem chamar a Gemini.
O POST de análise mantém a proteção antiforgery. As telas de análise e histórico
compartilham a apresentação dos resultados com escape de HTML pelo Razor.
O histórico armazena o resultado da época; o requisito pode ser editado posteriormente.
Análises realizadas antes desta implementação não são recuperadas retroativamente.

A migration incremental `20261113115408_AdicionarHistoricoAnalises` foi revisada,
testada em uma cópia e aplicada ao banco existente. Ela cria somente `AnalisesRequisitos`
e o índice por requisito/data, com chave estrangeira para `Requisitos`. Problemas,
justificativas e sugestões ficam em `ResultadoJson`; data e tokens têm colunas próprias.
O relacionamento acompanha a exclusão do requisito existente; não há exclusão independente
de análises. A aplicação continua sem executar migrations automaticamente.

Backup criado antes da geração e aplicação da migration:
`.local/backups/pre-historico-2026-11-13T11-50-41-561Z/requisita-plus.db`.
Os registros e schemas preexistentes foram comparados com o backup após a aplicação,
preservando integralmente o requisito e o usuário existentes. O banco passou nas
verificações de integridade e de chaves estrangeiras.

Validação: compilação sem erros ou avisos e modelo sem alterações pendentes para migration.
Foram executadas 24 verificações com HTTP simulado e 14 verificações HTTP da aplicação
em banco isolado, incluindo persistência, tokens, histórico vazio, ordenação, falhas,
cancelamento, erro do SQLite, autenticação, antiforgery e escape de HTML.
Roteiros: `.local/HistoricoChecks/Program.cs` e `.local/historico-http.cjs`;
resultados em `.local/historico-checks-result.json` e `.local/historico-http-result.json`.
Nenhuma chamada real à Gemini foi executada. Serviço, modelo, parâmetros de comunicação
e arquivos de configuração Gemini foram preservados; User Secrets não foram alterados.

## UC004 — Revisar Requisitos (13/11/2026)

Implementa RF012–RF014 e RN002–RN004 com as regras fornecidas para LES:
os status existentes **Pendente**, **Em análise** e **Revisado** são preservados,
e somente a análise mais recente (data decrescente, depois identificador decrescente)
controla a conclusão da revisão. Essa escolha e o mapeamento de status são adaptações
autorizadas para LES, não regras literais do documento original.

Em **Detalhes → Histórico de análises**, a análise mais recente apresenta
**Avaliar sugestão** para cada sugestão ainda pendente. Escolha **Aceitar**,
**Ajustar manualmente** ou **Descartar**, marque a confirmação e clique em
**Confirmar decisão**. Aceitar e ajustar exigem a descrição final completa, de até
5.000 caracteres, redigida e validada pelo analista. A orientação da IA não preenche
automaticamente esse campo. A confirmação humana é responsável pela completude e
validade do conteúdo; o sistema valida preenchimento, tamanho e confirmação explícita,
sem tentar inventar ou inferir critérios de negócio.

Ao aceitar ou ajustar, a descrição é substituída pela redação confirmada; título,
tipo e data de criação permanecem iguais. Ao descartar, a descrição atual é mantida,
inclusive as alterações de decisões anteriores. O analista deve incorporar essas
alterações anteriores ao confirmar uma nova redação completa. O status só é alterado
pela revisão na última decisão da análise mais recente, passando a **Revisado**,
inclusive quando todas as sugestões forem descartadas. Revisões parciais e análises
sem sugestões não promovem o status automaticamente. Não há atualização retroativa
dos status existentes nem mudança no comportamento do envio à Gemini.

Cada decisão é definitiva e fica disponível no histórico, com autor (identificador
Identity), data, descrição anterior e redação confirmada quando aplicável. Análises
antigas e suas decisões são apenas consultadas. `ResultadoJson`, data e tokens da
análise original não são sobrescritos. Para cumprir RN004, a exclusão de um requisito
com histórico é bloqueada pelo controlador; requisitos sem análises mantêm o fluxo
de exclusão existente. A edição manual também não permite marcar **Revisado** enquanto
houver sugestões pendentes na análise mais recente.

Todos os fluxos exigem login; gravações exigem antiforgery. Decisão, descrição e status
são salvos na mesma transação. O índice único por análise/posição impede decisões
duplicadas. Formulários são recusados se o requisito mudou desde a abertura, se surgiu
uma análise mais recente ou se a sugestão já foi avaliada; abra novamente pelo histórico.

Migration incremental aplicada: `20261113121937_AdicionarRevisaoSugestoes`.
Cria somente a tabela `DecisoesSugestoes`, sua chave estrangeira para análise e índice
único, sem alterar tabelas preexistentes. Backup SQLite consistente criado antes da
geração e aplicação: `.local/backups/pre-uc004-2026-11-13T12-17-40-087Z/requisita-plus.db`.
A migration foi validada primeiro em uma cópia isolada. Os schemas e todos os valores
preexistentes foram comparados ao backup após a aplicação: 1 usuário, 1 requisito e
1 análise preservados integralmente; nenhuma decisão de teste no banco principal.
Verificações SQLite de integridade e chaves estrangeiras passaram.

Validação em banco isolado: 28 verificações locais em
`.local/Uc004Checks/Program.cs` e 21 verificações HTTP em `.local/uc004-http.cjs`.
Resultados em `.local/uc004-checks-result.json`, `.local/uc004-http-result.json` e
`.local/uc004-preservation-result.json`. Cobrem decisões, validações, status parcial/final,
histórico original, autor/data, formulário obsoleto, duplicidade, falha SQLite com rollback,
análises antigas/vazias, exclusão protegida, autenticação, antiforgery e escape de HTML.
Os resultados de IA usados nos testes são sintéticos e foram inseridos somente na cópia.
Não houve chamada real à Gemini; serviço, configurações, modelo e User Secrets foram preservados.

Para validar no navegador, execute `dotnet run --no-restore --launch-profile http`,
acesse `http://localhost:5238` e faça login. Use a análise já existente em
**Detalhes → Histórico de análises**, sem clicar em **Analisar com IA** (esse botão
continua chamando a API real). Confirme uma decisão por sugestão, consulte seu registro
no histórico e confira a descrição e o status nos detalhes. Em uma análise com várias
sugestões, o status deve permanecer igual até a última confirmação. As confirmações
feitas no navegador são alterações reais no banco aberto pela aplicação.

## Recuperação de senha por código (13/11/2026)

No login, use **Esqueci minha senha**, informe o e-mail, digite o código recebido
no mesmo navegador e defina uma nova senha com confirmação. Após concluir, use
**Voltar ao login** e entre com a nova senha; não há autenticação automática.
As telas reutilizam o logo oficial e os estilos responsivos de login/cadastro,
incluindo o azul-marinho `#142D52`.

O fluxo usa `GenerateUserTokenAsync`/`VerifyUserTokenAsync` com o provedor nativo
`EmailTokenProvider<IdentityUser>` do Identity, com propósito exclusivo por pedido.
No .NET 10 esse provedor formata seis dígitos (`D6`), preservando zeros iniciais.
A API genérica não exige e-mail confirmado, ao contrário da elegibilidade para 2FA.
A janela TOTP nativa não representa uma expiração exata de cinco minutos; por isso
o estado em memória impõe o prazo de **5 minutos desde a solicitação**, inclusive
para concluir a troca de senha. Não há alteração da política global dos tokens.
Referências: [provedor de e-mail](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/EmailTokenProvider.cs),
[geração e validação TOTP](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/TotpSecurityStampBasedTokenProvider.cs).

- Até **5 tentativas incorretas** por pedido; entradas malformadas também contam.
- Pelo menos **60 segundos** entre solicitações aceitas, inclusive após sucesso/falha de envio.
- Até **5 submissões por e-mail normalizado em 15 minutos**. Submissões durante o
  intervalo de reenvio e falhas de SMTP também contam para essa cota.
- Até **20 submissões por IP em 15 minutos**, usando o IP da conexão, sem confiar
  em cabeçalhos encaminhados pelo cliente.
- Um único pedido ativo por usuário. Reenvio substitui pedido e autorização anteriores.
- Cookie opaco aleatório de 256 bits, HttpOnly, SameSite=Strict, restrito a
  `/RecuperacaoSenha`; Secure segue a mesma regra de ambiente da autenticação atual.
  O cookie identifica a sessão de recuperação e não contém e-mail, usuário ou token.
- Validação, limites, substituição e consumo são serializados por um semáforo.
  A autorização permanece no servidor, vinculada ao pedido e usuário. Após validar
  o código, o token nativo de redefinição é guardado apenas em memória e passado a
  `ResetPasswordAsync`. Sucesso consome a autorização; senha fraca permite correção.
  Mudança do security stamp invalida o token e remove a autorização correspondente.
- POSTs exigem antiforgery; respostas não são armazenadas em cache. Contas ausentes,
  limites e falhas de SMTP recebem o mesmo encaminhamento e mensagem pública.
- Entrega em uma fila limitada em memória, processada por `BackgroundService`,
  com o `IEmailSender` existente. O POST não aguarda SMTP. A fila guarda apenas
  identificadores e usa um novo escopo de serviços por entrega, sem retries.
  Códigos, senhas, destinatários e detalhes de exceções não são registrados pelo fluxo.

Limitações intencionais: uma única instância; reinício perde pedidos, fila e cotas.
Cookies devem estar habilitados e o navegador deve ser o mesmo em todas as etapas.
Sob sobrecarga (32 entregas pendentes, 10.000 pedidos ou 20.000 chaves de limitação),
a solicitação pode não ser enviada e deve ser refeita respeitando as cotas.
Atrasos de entrega não estendem o prazo original. Falhas de envio produzem apenas
um aviso técnico genérico no servidor; a tela orienta a solicitar outro código.
Uso atrás de proxy exigiria configuração explícita de proxies confiáveis para cota
por IP. Não há suporte a múltiplas instâncias ou entrega persistente nesta etapa.

### Validação isolada

```powershell
dotnet restore tools/PasswordRecoveryChecks/PasswordRecoveryChecks.csproj --configfile NuGet.Config
dotnet build --no-restore
dotnet run --project tools/PasswordRecoveryChecks --no-restore
```

O executável usa usuários `example.invalid`, SQLite `:memory:`, relógio controlado,
Data Protection efêmera, SMTP simulado e Kestrel local para testes HTTP. Não executa
o `Program.cs` da aplicação nem carrega seus appsettings/User Secrets. Cria a estrutura
somente no SQLite em memória, sem executar migrations. O worker é processado de forma
controlada pelo mesmo método usado em produção, permitindo simular atrasos e falhas.

Resultado: **95 verificações aprovadas** de solicitação existente/inexistente,
seis dígitos e zeros iniciais, código inválido/expirado, tentativas, cotas, reenvio,
consumo, adulteração e acesso direto, vínculo ao navegador e usuário, confirmação,
política de senha, login HTTP após recuperação, concorrência, SMTP e preservação de
requisitos/histórico/revisão sintéticos. Registro local: `.local/password-recovery-checks-result.txt`.
Também passaram **46 verificações** existentes em `.local/LimiteIAChecks` (Gemini
simulada, histórico e revisão) e **9** em `.local/EmailChecks` (validação offline,
sem conexão SMTP). Roteiros antigos que dependem de backups, banco real, migrations
ou leitura de User Secrets não foram executados por conflitarem com as restrições
desta etapa. Nenhum teste manual com conta real ou envio real foi realizado.

Arquivos criados: `Controllers/RecuperacaoSenhaController.cs`,
`Models/RecuperacaoSenhaViewModels.cs`, `Services/RecuperacaoSenhaService.cs`,
`Services/RecuperacaoSenhaEntrega.cs`, cinco views em `Views/RecuperacaoSenha/`
(`Solicitar`, `Codigo`, `NovaSenha`, `Concluida` e `_Logo`), e
`tools/PasswordRecoveryChecks/{PasswordRecoveryChecks.csproj,Program.cs}`.
Arquivos alterados: `Program.cs`, `Views/Conta/Login.cshtml` e `README.md`.
Nenhuma tabela, migration, configuração ou credencial foi alterada; banco principal,
usuários existentes e backups não foram acessados pelos testes.
