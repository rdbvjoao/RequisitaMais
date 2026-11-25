# Requisita+

Sistema de gerenciamento e revisão de requisitos de software desenvolvido para a disciplina **Laboratório de Engenharia de Software**, com entrega acadêmica em **26/11/2026**.

O Requisita+ permite cadastrar requisitos, acompanhar sua situação e registrar análises de qualidade com apoio da API Gemini. O histórico reúne os apontamentos da IA e as decisões tomadas pelo responsável pelo requisito.

**A inteligência artificial atua como ferramenta de apoio. A avaliação do contexto, a redação final e a validação dos requisitos dependem da revisão humana.** A ausência de sugestões ou o status Revisado não garantem, por si só, a qualidade ou a completude de um requisito.

## Tecnologias

| Componente | Tecnologia utilizada |
| --- | --- |
| Aplicação web | C# e ASP.NET Core MVC, com .NET 10 |
| Interface | Razor Views, HTML, CSS, JavaScript e Bootstrap |
| Validação no navegador | jQuery Validation e validação unobtrusive |
| Persistência | Entity Framework Core 10 e SQLite |
| Identidade e autenticação | ASP.NET Core Identity e cookies |
| Apoio à análise | API Gemini, por HTTP, com JSON |
| Recuperação de senha | Tokens do Identity e SMTP do Gmail com STARTTLS |

## Arquitetura e organização

A aplicação segue o padrão MVC e utiliza injeção de dependências. Os controladores atendem às requisições, os serviços concentram a integração com IA e a recuperação de senha, e o EF Core realiza o acesso ao SQLite.

```text
Controllers/      Autenticação, recuperação, requisitos, dashboard e ajuda
Data/             Contexto EF Core, configuração de identidade e factory de design
Models/           Entidades, enums, modelos de tela e regras de validação
Services/         Integração Gemini e serviços de recuperação e envio de e-mail
Views/            Páginas Razor e componentes compartilhados
Migrations/       Evolução do esquema e snapshot do modelo
wwwroot/          Estilos, scripts, imagens e bibliotecas da interface
tools/            Verificações isoladas e utilitários auxiliares
docs/             Documentação complementar
Program.cs        Registro de serviços, autenticação e rotas
```

As entidades de domínio são o requisito, a análise e a decisão sobre uma sugestão. Cada requisito possui um proprietário no Identity. As sugestões ficam no JSON original da análise; cada decisão referencia uma análise e a posição da sugestão nesse resultado.

O banco local é `requisita-plus.db`, na raiz da aplicação. A aplicação não cria a estrutura nem aplica migrations automaticamente ao iniciar. A factory utilizada pelas ferramentas do EF Core usa um banco em memória por padrão, separado do banco da aplicação.

## Contas e autenticação

O cadastro solicita e-mail, senha e confirmação de senha. O e-mail deve ser único. A senha exige pelo menos oito caracteres, com letra maiúscula, letra minúscula, número e símbolo. Após um cadastro bem-sucedido, o usuário já fica autenticado.

O login utiliza ASP.NET Core Identity. Cinco tentativas de senha incorreta bloqueiam a conta por cinco minutos. O logout é feito pelo botão **Sair**, com requisição POST protegida contra falsificação. O retorno após autenticação aceita somente URLs locais.

Os cookies de autenticação são HttpOnly e SameSite=Lax, com validade de duas horas e renovação durante o uso. Em Development, permitem o perfil HTTP local; nos demais ambientes, exigem HTTPS.

### Recuperação de senha

Na tela de login, selecione **Esqueci minha senha**, informe o e-mail e siga as etapas no mesmo navegador:

1. Receba e informe o código de seis dígitos, preservando eventuais zeros iniciais.
2. Defina e confirme a nova senha, respeitando a política de senhas.
3. Volte ao login e entre com a nova senha. A recuperação não autentica automaticamente.

O prazo para validar o código e concluir a troca é de cinco minutos a partir da solicitação. O código só pode ser validado uma vez, e a autorização é consumida após a troca de senha. São permitidas até cinco tentativas de validação por pedido, intervalo mínimo de 60 segundos entre solicitações aceitas, até cinco submissões por e-mail normalizado e até 20 por IP em uma janela de 15 minutos. Submissões durante o intervalo de reenvio e falhas de entrega também entram nas cotas.

Um novo pedido substitui o anterior e sua autorização. A sessão de recuperação usa um cookie opaco, HttpOnly e SameSite=Strict. As respostas públicas não confirmam a existência de uma conta. A entrega é processada por uma fila em memória, sem repetição automática de envio, usando SMTP do Gmail na porta 587 com STARTTLS.

## Isolamento por usuário

Cada requisito pertence exclusivamente à conta que o cadastrou. O proprietário é definido no servidor a partir da identidade autenticada e não é escolhido pelo formulário.

Listagem, busca, filtros, dashboard, detalhes, edição, exclusão, análises e decisões respeitam esse vínculo. Tentativas de acessar requisitos ou análises de outra conta por URL direta ou por POST retornam **404**, sem realizar a operação. Os requisitos não são compartilhados entre contas e não existe fluxo de transferência de propriedade.

## Gerenciamento de requisitos

O cadastro contém título, descrição e tipo **Funcional** ou **Não Funcional**. Todo requisito novo começa como **Pendente**. A data de criação é preenchida automaticamente.

| Campo ou operação | Regra |
| --- | --- |
| Título | Obrigatório, com até 150 caracteres |
| Descrição | Obrigatória, com até 5.000 caracteres |
| Tipo | Funcional ou Não Funcional |
| Consulta | Listagem e página de detalhes dos requisitos da própria conta |
| Edição | Permite atualizar título, descrição, tipo e status, conforme as regras de revisão |
| Exclusão | Exige confirmação e versão atual; é bloqueada quando existe histórico de análises |

Identificador, proprietário e data de criação não são campos editáveis. O servidor valida os dados recebidos e protege os formulários POST com token antifalsificação.

### Busca por título e filtros

A listagem permite buscar por parte do título, sem distinção entre maiúsculas e minúsculas, e combinar a busca com os filtros de tipo e status. Um código pode ser encontrado se fizer parte do título; não existe um campo separado de código para essa busca. A opção **Limpar** remove os critérios.

### Dashboard

A página inicial apresenta o total de requisitos, as quantidades de Pendentes, Em análise e Revisados e os cinco cadastros mais recentes. Todos os indicadores consideram somente os requisitos da conta autenticada.

## Análise de qualidade com Gemini

A opção **Analisar com IA** utiliza o título, o tipo e a descrição salvos no requisito. A descrição enviada para análise deve conter até **1.000 caracteres**, embora o cadastro e a redação final humana aceitem até 5.000. Uma descrição acima do limite é recusada antes da chamada externa, sem truncamento automático.

O prompt orienta a identificação de **Ambiguidade**, **Falta de clareza** e **Informação incompleta**, incluindo termos vagos sem critérios verificáveis. Ele solicita orientações sem inventar métricas, prazos ou regras de negócio. Cada apontamento apresenta categoria, problema, justificativa e sugestão para avaliação humana.

### Integração e validação das respostas

O serviço utiliza `IHttpClientFactory` e faz POST para:

```text
https://generativelanguage.googleapis.com/v1beta/models/{modelo}:generateContent
```

A configuração atual em `appsettings.json` é:

```json
{
  "Gemini": {
    "Modelo": "gemini-3.5-flash-lite",
    "TimeoutSegundos": 120
  }
}
```

Os parâmetros enviados em `generationConfig` são **`responseMimeType: application/json`** e **`maxOutputTokens: 8192`**. A implementação atual não envia `responseJsonSchema` nem `candidateCount`. A estrutura do resultado é validada localmente.

Cada solicitação autorizada que passa pelas validações locais faz uma única chamada, sem tentativas automáticas adicionais. Redirecionamentos HTTP estão desabilitados. A chave é enviada somente no cabeçalho `x-goog-api-key`, e os cabeçalhos do cliente são ocultados nos logs. O timeout configurável aceita de 1 a 600 segundos.

O serviço exige um único candidato com término `STOP`, JSON válido, campos obrigatórios, categorias permitidas, no máximo oito sugestões e textos não vazios de até 1.500 caracteres em problema, justificativa e sugestão. Campos inesperados no objeto do resultado são rejeitados. O conteúdo apresentado nas páginas é escapado pelo Razor.

Há tratamento para erros HTTP, indisponibilidade, limite de uso, timeout, conexão e respostas inválidas, incompletas ou bloqueadas. Diagnósticos da API são sanitizados antes de aparecerem na tela ou nos logs. Falhas de análise ou de persistência não deixam uma análise ou mudança de status parcialmente gravada.

Abrir páginas ou consultar o histórico não chama a Gemini. Solicitar uma análise faz uma chamada real, podendo consumir a cota disponível. Quando informados pela API, tokens de entrada, saída e total são armazenados e exibidos; valores ausentes não são estimados e o total não é recalculado.

## Histórico e revisão humana

Em **Detalhes → Histórico de análises**, os resultados aparecem do mais recente para o mais antigo, por data e depois por identificador. Cada entrada mantém o JSON original, data e tokens disponíveis. O requisito pode ter sido editado depois da análise; o histórico representa o resultado armazenado naquela ocasião.

Na análise mais recente, use **Avaliar sugestão** para registrar uma das decisões:

| Decisão | Comportamento |
| --- | --- |
| Aceitar | Exige a redação final completa e sua confirmação; substitui a descrição atual pelo texto confirmado |
| Ajustar manualmente | Exige uma redação própria completa e sua confirmação; substitui a descrição atual pelo texto confirmado |
| Descartar | Registra a rejeição e preserva a descrição atual |

A redação humana aceita até 5.000 caracteres. A IA não preenche automaticamente o texto final. Ao aceitar ou ajustar outra sugestão, o responsável deve preservar no texto completo as melhorias anteriores que deseja manter.

Cada decisão exige confirmação explícita e é definitiva no funcionamento atual. O registro guarda autoria, data, descrição anterior e redação final, quando aplicável. Decisões não sobrescrevem o JSON da análise. Análises antigas e suas decisões permanecem disponíveis para consulta; novas decisões só podem ser registradas na análise mais recente. A interface não oferece edição ou exclusão de análises e decisões, e bloqueia a exclusão de requisitos com histórico.

### Regras de status

| Evento | Resultado |
| --- | --- |
| Cadastro de requisito | Pendente |
| Análise concluída e salva com sucesso | Em análise, inclusive se o requisito estava Revisado ou se o resultado não contém sugestões |
| Falha na análise ou em sua gravação | Preserva o estado persistido anterior |
| Decisão com outras sugestões pendentes na análise mais recente | Em análise |
| Última decisão pendente da análise mais recente, salva com sucesso | Revisado automaticamente |
| Análise sem sugestões | Permanece Em análise até conclusão humana pela edição |
| Nova análise de um requisito Revisado | Reabre como Em análise quando salva; mantém as análises e decisões anteriores |

**Aceitar, Ajustar e Descartar contam igualmente para concluir a revisão**, inclusive quando todas as sugestões são descartadas. A última decisão, o texto resultante e o status são gravados na mesma transação.

A edição manual permite escolher o status, mas bloqueia Revisado enquanto houver sugestões pendentes na análise mais recente. Sem análise ou sem sugestões pendentes, é possível concluir pela edição. Se o usuário selecionar Pendente manualmente e depois registrar uma decisão parcial, o requisito volta para Em análise. Editar o texto, por si só, não solicita outra análise nem recalcula os registros históricos.

### Controle de concorrência

Cada requisito possui uma versão de concorrência usada pelo EF Core e pelos formulários. Alterações e decisões renovam essa versão, inclusive quando uma sugestão é descartada sem mudança de texto.

Edições e exclusões com formulário obsoleto são recusadas. Uma análise não é salva se o requisito mudou durante a chamada à Gemini. Revisões conferem a versão do requisito, a análise mais recente e a ausência de decisão anterior para a mesma sugestão. Transações e um índice único por análise e posição da sugestão protegem a persistência. Ao receber um aviso de conflito, reabra a operação a partir dos dados atuais.

## Central de Ajuda

A Central de Ajuda, acessível pelo menu e por `/Home/Ajuda`, reúne primeiros passos, navegação, gerenciamento, tipos de requisitos, filtros, análise com IA, revisão humana, histórico e glossário. É uma página informativa, disponível sem login, e não solicita análises à Gemini.

## Configuração e execução local

Os comandos desta seção são instruções para preparação e uso local. Eles não foram executados durante a atualização documental para a entrega.

### Pré-requisitos

- SDK do .NET 10.
- Dependências do projeto restauradas para compilar e executar.
- Ferramenta `dotnet ef` compatível com EF Core 10, caso seja necessário preparar um banco novo.
- Acesso à API Gemini e uma chave válida para solicitar análises reais.
- Acesso ao SMTP e credencial da conta remetente para recuperar senhas por e-mail.

Execute os comandos a partir da raiz do repositório. Em uma instalação nova, a restauração de pacotes é feita com:

```powershell
dotnet restore RequisitaPlus.Web.csproj
```

### Credenciais em User Secrets

O projeto já possui `UserSecretsId`; não é necessário inicializá-lo novamente. No perfil local Development, a aplicação carrega User Secrets. Os exemplos abaixo contêm apenas marcadores de posição, que devem ser substituídos localmente:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<SUA_CHAVE_GEMINI>" --project RequisitaPlus.Web.csproj
dotnet user-secrets set "Email:Smtp:Password" "<SUA_SENHA_DE_APP_SMTP>" --project RequisitaPlus.Web.csproj
```

A senha SMTP deve corresponder à conta remetente definida em `GmailEmailSender`. A implementação atual fixa remetente, servidor e porta no serviço; não existe configuração de remetente pelo formulário ou por uma chave adicional de User Secrets.

Mantenha valores reais fora do código, de `appsettings.json`, de scripts versionados, de capturas de tela e de históricos de terminal compartilhados. User Secrets mantém a configuração fora do repositório, mas não é um cofre criptografado para produção. A ausência da chave Gemini impede as análises; a ausência da senha SMTP impede o envio da recuperação. O gerenciamento de requisitos continua disponível quando o banco está preparado.

### Banco de dados

O banco e seus backups não são distribuídos pelo Git. Para o ambiente de apresentação já preparado, preserve o arquivo existente e não execute criação, recriação ou atualização do banco.

**Somente em uma instalação nova, sem banco existente**, prepare o esquema com as migrations versionadas. A conexão deve ser explícita, pois a factory de design utiliza memória por padrão:

```powershell
$novoBancoRequisita = Join-Path (Get-Location).Path "requisita-plus.db"
if (Test-Path -LiteralPath $novoBancoRequisita) {
    throw "O banco já existe. Preserve-o e não execute esta preparação."
}
dotnet ef database update --project RequisitaPlus.Web.csproj --connection "Data Source=$novoBancoRequisita"
```

Esse procedimento cria a estrutura de um banco novo; não fornece usuários nem requisitos de exemplo. O primeiro acesso pode ser feito pelo cadastro de uma conta. Não é necessário executar `LibrarySeed` para usar a aplicação. Alterações em um banco existente exigem planejamento e preservação dos dados; não fazem parte da preparação acima.

### Compilar e executar

Com as dependências e o banco preparados:

```powershell
dotnet build RequisitaPlus.Web.csproj --no-restore
dotnet run --project RequisitaPlus.Web.csproj --no-restore --launch-profile http
```

Acesse **http://localhost:5238** e encerre com **Ctrl+C**. O perfil `http` utiliza Development e carrega as credenciais locais. O perfil `https` também está definido em `Properties/launchSettings.json`, com endereço HTTPS na porta 7176, dependendo de certificado local válido.

## Validação técnica para a entrega

A auditoria final realizada em **25/11/2026** registrou:

- Build com `dotnet build --no-restore` concluído com **zero erros e zero avisos**.
- **593 verificações automatizadas aprovadas**, distribuídas conforme a tabela abaixo.
- Integridade SQLite confirmada por `PRAGMA integrity_check`, com resultado `ok`.
- Ausência de violações em `PRAGMA foreign_key_check` e consistência dos vínculos auditados.
- Ausência de migrations pendentes e de alterações do modelo em relação à última migration.
- Testes com bancos sintéticos, em memória ou cópias isoladas, HTTP simulado e e-mail simulado ou bloqueado antes de qualquer envio.
- Código-fonte e banco real preservados durante a auditoria, sem chamadas reais à Gemini ou envio de e-mails.

| Conjunto de verificações | Aprovadas |
| --- | ---: |
| IsolationChecks: isolamento, status, concorrência e revisão humana | 449 |
| PasswordRecoveryChecks: recuperação de senha | 95 |
| GeminiChecks: integração com HTTP simulado | 40 |
| EmailChecks: validações offline de e-mail | 9 |
| **Total contabilizado** | **593** |

Um teste local legado, `LimiteIAChecks`, foi interrompido antes da primeira verificação: sua preparação tenta inserir um requisito sem proprietário, incompatível com a obrigatoriedade atual de `ProprietarioId`. Esse roteiro não foi corrigido durante a auditoria e não integra as 593 aprovações.

Pelas restrições da auditoria, não foram executados os ensaios de migrations, a ferramenta LibrarySeed e suas verificações, roteiros históricos dependentes de migrations, procedimentos que iniciariam a aplicação principal ou o teste de SMTP real. A disponibilidade externa da Gemini e do SMTP não foi revalidada nessa auditoria. **Esses resultados não representam aprovação integral de toda a coleção histórica de testes.**

Os projetos `tools/IsolationChecks` e `tools/PasswordRecoveryChecks` estão no repositório. GeminiChecks, EmailChecks e outros roteiros auxiliares são artefatos locais em `.local/`, ignorados pelo Git; a reprodução das verificações auxiliares depende desses arquivos. Esta atualização do README apenas registra os resultados anteriores, sem executar novamente o build ou os testes.

## Limitações atuais

- A IA pode produzir apontamentos imprecisos ou omitir problemas. A validação de formato não comprova a qualidade semântica; a decisão final é humana.
- A análise aceita descrição de até 1.000 caracteres, enquanto cadastro e revisão humana aceitam até 5.000. Não há redução automática do conteúdo enviado.
- Análises reais dependem de conectividade, credencial, disponibilidade do modelo configurado e cotas da Gemini. O serviço não repete chamadas automaticamente.
- A recuperação depende do mesmo navegador, cookies habilitados e entrega SMTP. Pedidos, autorizações, fila e cotas ficam em memória de uma única instância e são perdidos ao reiniciar. A fila é limitada; atrasos ou falhas não ampliam o prazo de cinco minutos. Não há entrega persistente ou suporte distribuído desse fluxo.
- O remetente SMTP é fixado na implementação. Sua troca não está disponível na configuração local documentada.
- Não há colaboração entre contas, perfis administrativos, autenticação externa ou transferência de requisitos.
- Decisões confirmadas são definitivas. Não há edição ou exclusão de análises e decisões na interface, e requisitos com histórico não podem ser excluídos pelo fluxo atual.
- A busca por título ocorre em memória após carregar os registros filtrados da conta, e a listagem não possui paginação.
- Datas de criação, análise e decisão seguem o horário local do servidor.
- A preparação do banco é manual. A aplicação não executa migrations, recálculo de status históricos ou backups automaticamente ao iniciar.

## Cuidados com o repositório

O `.gitignore` protege bancos SQLite e arquivos auxiliares WAL/SHM/journal, backups, `.local/`, artefatos de compilação, configurações locais, credenciais, logs e temporários. Esses arquivos não devem ser adicionados à publicação. O README não distribui dados de contas, credenciais ou registros privados do ambiente de apresentação.
