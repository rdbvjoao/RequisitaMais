# Carga fictícia de biblioteca — V1

Ferramenta de console separada da aplicação web. Reutiliza `Requisito`, seus enums,
DataAnnotations e `RequisitosContext` por referência de projeto. Não inicia o
`Program.cs` web, serviços de e-mail ou Gemini, nem carrega User Secrets.
Não executa migrations, `EnsureCreated`, exclusões ou atualizações.

O catálogo contém 100 requisitos fictícios: 70 funcionais e 30 não funcionais.
Os códigos `[TESTE-BIB-V1-001]` até `[TESTE-BIB-V1-100]` têm ordem permanente.
As regras, prazos e métricas dos textos pertencem apenas ao domínio fictício.
Cada inserção recebe `StatusRequisito.Pendente`, `DateTime.Now` e ID automático.

## Compilar e validar sem abrir banco

Execute a partir da raiz do repositório:

```powershell
dotnet restore tools/LibrarySeed/LibrarySeed.csproj --configfile NuGet.Config
dotnet build tools/LibrarySeed/LibrarySeed.csproj --no-restore
dotnet run --project tools/LibrarySeed --no-build -- --validate-catalog
```

`--validate-catalog` verifica os 100 itens, repetibilidade, códigos, enums, limites,
campos obrigatórios, conflitos, contagem e planejamento de reexecução em memória.
Não abre SQLite, não insere registros e não cria backup ou manifesto.

## Simular

```powershell
dotnet run --project tools/LibrarySeed --no-build -- --dry-run --database "C:\Users\joaov\requisita-plus-simples\requisita-plus.db"
```

Sem `--execute`, a simulação é o padrão. O caminho do banco é obrigatório; arquivo
inexistente é recusado, nunca criado. A saída mostra caminho absoluto, contagem
real, quantidade necessária e títulos, tipos e descrições propostos.
O número de novos registros é `max(0, 100 - contagem real)`; com seis existentes,
são selecionados 94 (70 funcionais e 24 não funcionais).

A conexão usa `Mode=ReadOnly`, `query_only=ON` e transação de leitura. Não há
gravação de dados, backup ou manifesto na simulação. **O SQLite pode criar ou
atualizar arquivos auxiliares WAL/SHM mesmo com conexão somente leitura.** Para
evitar qualquer efeito em arquivos do banco principal, simule em uma cópia
consistente previamente obtida, ou use `--validate-catalog` para a verificação sem
acesso ao banco. Não use `immutable=1` para ignorar um WAL que possa conter dados.

## Executar futuramente, somente após autorização

Pare a aplicação e outros escritores. Proteja o diretório de backups: as cópias
contêm também os dados de autenticação já existentes.

```powershell
dotnet run --project tools/LibrarySeed --no-build -- --execute --database "C:\Users\joaov\requisita-plus-simples\requisita-plus.db" --backup-dir "C:\Users\joaov\requisita-plus-simples\.local\backups"
```

Este comando **insere registros**. Não faz parte da validação sem inserções.
`--execute` e `--dry-run` juntos, opções desconhecidas e opções duplicadas são
recusados. O código de saída é 0 para sucesso e 1 para erro.

Fluxo preparado:

1. Abrir o banco existente em `ReadWrite`, sem criação; habilitar chaves estrangeiras.
2. Iniciar transação `IMMEDIATE` antes da contagem e do planejamento. Escritores
   concorrentes aguardam ou recebem erro após o timeout, sem execução paralela da carga.
3. Verificar integridade e referências e calcular os códigos ainda ausentes.
4. Se já houver pelo menos 100 requisitos, não inserir nem excluir registros.
5. Capturar em memória os valores de todas as tabelas preexistentes, exceto a
   sequência interna do SQLite, que avança com os novos IDs.
6. Criar diretório exclusivo datado e copiar o estado confirmado com a API
   `SqliteConnection.BackupDatabase`, usando uma segunda conexão de leitura.
   A transação já aberta impede outros escritores. Normalizar o destino para
   journal DELETE, conferir integridade, referências e equivalência dos valores,
   fechar as conexões do backup e registrar seu SHA-256.
7. Validar os candidatos com DataAnnotations e gravar somente novas entidades
   `Requisito`, dentro da mesma transação.
8. Conferir total 100, status Pendente, preservação de todos os valores originais
   e ausência de análises nos novos IDs. Gerar manifesto antes do commit.
9. Confirmar a transação e atualizar o manifesto para `Committed`.

Falhas anteriores ao commit revertem a transação. Backups e manifestos de tentativas
podem permanecer como evidência; não são apagados automaticamente. Um manifesto
`Prepared` significa resultado não confirmado: após uma interrupção, consulte os
IDs no banco antes de concluir se houve commit. Se a atualização final do manifesto
falhar após o commit, a ferramenta informa explicitamente que a carga foi confirmada.

## Repetição e identificação

O banco não possui índice único nos títulos. A combinação entre códigos estáveis,
consulta dos existentes e transação IMMEDIATE impede duplicações entre execuções
desta ferramenta. A aplicação web não é alterada e continua podendo editar títulos.

Não altere nem remova marcadores de itens de teste. Códigos repetidos, desconhecidos,
malformados ou com título, descrição ou tipo divergente do catálogo interrompem a
carga sem sobrescrever dados. Alterações posteriores de status e data não são
sobrescritas. Se o marcador for removido por completo, sua origem deixa de ser
reconhecível pelo título: consulte o manifesto antes de executar novamente.

Cada manifesto contém o caminho do banco, backup, contagens e os códigos, IDs,
títulos, tipos, status e datas dos itens inseridos. Não contém senhas ou conteúdos
dos históricos. Requisitos existentes contam na meta mesmo que não sejam fictícios.

Consultas de conferência (em uma conexão de leitura):

```sql
SELECT COUNT(*) AS Total FROM Requisitos;
SELECT Id, Titulo, Tipo, Status, DataCriacao
FROM Requisitos
WHERE Titulo GLOB '[[]TESTE-BIB-V1-[0-9][0-9][0-9][]] *'
ORDER BY Titulo;
PRAGMA integrity_check;
PRAGMA foreign_key_check;
```

O manifesto de cada execução identifica exatamente quais IDs devem ser novos e
Pendentes. Requisitos de teste de execuções antigas podem ter sido revisados depois;
não se deve redefinir seus status para conferir uma carga posterior.

## Restauração manual

Não há comando automático de restauração nesta ferramenta. Para restaurar, pare a
aplicação e feche todas as conexões. Preserve o estado atual usando backup consistente
antes de substituí-lo. Confira o SHA-256 do backup escolhido e suas verificações de
integridade em ambiente isolado. Com todas as conexões fechadas, substitua o `.db`
pelo backup autossuficiente e retire os antigos WAL/SHM do destino, preservando-os
com o estado anterior; nunca combine o backup com auxiliares de outra versão.
Reabra primeiro em leitura para conferir dados e integridade e depois reinicie a
aplicação. Restaurar a cópia integral desfaz também mudanças posteriores ao backup.

## Escopo da verificação inicial

Compilação e verificações puras do catálogo/plano não comprovam a execução de backup,
commit e rollback. Esses fluxos devem ser exercitados em uma cópia descartável, sob
autorização para inserções de teste, antes da carga no banco principal.

Verificação da implementação em 21/11/2026: `dotnet build ... --no-restore` passou
com zero erros e zero avisos; `--validate-catalog` aprovou 33 verificações puras.
Também passaram seis verificações de CLI (ajuda, validação do catálogo, ausência
de caminho, banco inexistente, opção repetida e opção desconhecida), sem abrir
banco. A tentativa de restore encontrou acesso negado ao `NuGet.Config` global
do usuário neste ambiente; a compilação utilizou os assets e pacotes já disponíveis.
Não foram executados backup, simulação no banco principal ou inserções em SQLite.
O SHA-256 do arquivo principal permaneceu
`12943C6993720C800C19CAC6E9A5A419C56AC6B6CB1377725B07B3B3A7597702`.
