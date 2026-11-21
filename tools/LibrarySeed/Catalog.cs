using System.ComponentModel.DataAnnotations;
using RequisitaPlus.Web.Models;

namespace LibrarySeed;

internal static class Catalog
{
    // Ordem e códigos são permanentes na versão V1. Não renumerar nem reutilizar códigos.
    private const string Functional = """
Cadastrar obra|O bibliotecário deve cadastrar uma obra com título, autores, editora, ano e ISBN opcional, recebendo confirmação com seu identificador.
Editar dados bibliográficos|O bibliotecário deve alterar os dados de uma obra cadastrada e visualizar os novos valores ao consultar a ficha.
Consultar ficha da obra|O leitor deve consultar título, autores, edição, assuntos e quantidade de exemplares disponíveis na ficha de uma obra.
Pesquisar por título|O leitor deve pesquisar obras por parte do título e receber os resultados com título e autor principal.
Pesquisar por autor|O leitor deve localizar obras pelo nome completo ou parcial de um autor e abrir suas fichas.
Pesquisar por ISBN|O leitor deve consultar uma obra pelo ISBN, aceitando a entrada com ou sem separadores.
Filtrar por assunto|O leitor deve restringir os resultados da pesquisa a um assunto selecionado do catálogo.
Filtrar por idioma|O leitor deve filtrar obras por idioma e visualizar o filtro aplicado junto aos resultados.
Filtrar por disponibilidade|O leitor deve exibir somente obras com ao menos um exemplar disponível para empréstimo.
Ordenar resultados|O leitor deve ordenar os resultados por título ou ano de publicação, em ordem crescente ou decrescente.
Paginar catálogo|A pesquisa deve apresentar até vinte obras por página e oferecer navegação para as demais páginas.
Cadastrar autor|O bibliotecário deve cadastrar um autor com nome e identificador próprio para vinculá-lo às obras.
Vincular coautores|O bibliotecário deve associar vários autores a uma obra e definir a ordem em que aparecem na ficha.
Cadastrar editora|O bibliotecário deve cadastrar uma editora e selecioná-la ao preencher os dados de uma obra.
Classificar assuntos|O bibliotecário deve associar uma obra a um ou mais assuntos existentes e remover associações incorretas.
Registrar edição|O bibliotecário deve registrar o número da edição de uma obra e diferenciá-la das demais edições na consulta.
Cadastrar exemplar|O bibliotecário deve cadastrar um exemplar associado a uma obra e atribuir-lhe um código de patrimônio exclusivo.
Localizar exemplar|O atendente deve localizar um exemplar pelo código de patrimônio e consultar sua obra e situação atual.
Definir localização física|O bibliotecário deve informar unidade, estante e prateleira de cada exemplar para orientar sua localização.
Registrar aquisição|O bibliotecário deve registrar a data e a origem da aquisição de um exemplar, distinguindo compra e doação.
Registrar doação|O bibliotecário deve registrar uma doação com data, descrição e relação dos exemplares recebidos.
Enviar para manutenção|O atendente deve marcar um exemplar como em manutenção, impedindo novos empréstimos enquanto durar essa situação.
Concluir manutenção|O atendente deve devolver um exemplar em manutenção à situação disponível após registrar a conclusão do reparo.
Registrar perda|O bibliotecário deve registrar a perda de um exemplar com justificativa e impedir sua oferta como disponível.
Registrar baixa|O bibliotecário deve dar baixa em um exemplar com motivo e data, mantendo sua ficha consultável.
Transferir exemplar|O bibliotecário deve transferir um exemplar disponível entre unidades e atualizar sua localização de destino.
Inventariar estante|O atendente deve registrar os códigos encontrados em uma estante e comparar a leitura com a localização cadastrada.
Listar divergências de inventário|O bibliotecário deve consultar exemplares ausentes ou encontrados fora da localização esperada em um inventário.
Cadastrar leitor|O atendente deve cadastrar um leitor com nome, matrícula e contato, recusando matrícula já utilizada.
Atualizar contato do leitor|O atendente deve atualizar os contatos de um leitor sem alterar sua matrícula nem seus empréstimos existentes.
Consultar cadastro do leitor|O atendente deve localizar um leitor pela matrícula e visualizar sua situação cadastral e empréstimos ativos.
Suspender leitor|O bibliotecário deve suspender um leitor com motivo e período, impedindo novos empréstimos durante a suspensão.
Reativar leitor|O bibliotecário deve reativar um cadastro suspenso após encerrar a suspensão e registrar a justificativa.
Consultar situação pessoal|O leitor autenticado deve consultar seus empréstimos, reservas e débitos em uma área pessoal.
Registrar empréstimo|O atendente deve emprestar um exemplar disponível a um leitor ativo e apresentar a data prevista de devolução.
Recusar exemplar indisponível|O sistema deve recusar o empréstimo de um exemplar já emprestado, perdido, baixado ou em manutenção.
Aplicar limite de empréstimos|O sistema deve recusar um novo empréstimo quando o leitor já tiver cinco empréstimos ativos.
Calcular prazo de devolução|Ao registrar um empréstimo comum, o sistema deve calcular a devolução para quatorze dias corridos após a retirada.
Emitir comprovante de retirada|O atendente deve emitir um comprovante contendo leitor, exemplar, data de retirada e data prevista de devolução.
Registrar devolução|O atendente deve registrar a devolução de um exemplar emprestado e encerrar seu empréstimo ativo.
Identificar devolução atrasada|Ao devolver um exemplar após o prazo, o sistema deve informar o número de dias corridos de atraso.
Renovar empréstimo|O leitor deve renovar um empréstimo sem atraso por mais quatorze dias quando não houver reserva de outro leitor.
Limitar renovações|O sistema deve permitir no máximo duas renovações por empréstimo e informar quando o limite for atingido.
Listar empréstimos ativos|O atendente deve consultar empréstimos ainda não devolvidos, exibindo leitor, exemplar e prazo de devolução.
Filtrar empréstimos por leitor|O atendente deve restringir a lista de empréstimos ativos à matrícula de um leitor informado.
Consultar histórico de empréstimos|O leitor deve consultar seus empréstimos encerrados com as datas de retirada e devolução.
Listar atrasos|O atendente deve consultar empréstimos vencidos ainda não devolvidos, ordenados pela maior quantidade de dias de atraso.
Reservar obra|O leitor ativo deve reservar uma obra sem exemplares disponíveis e receber sua posição na fila.
Impedir reserva repetida|O sistema deve recusar uma segunda reserva ativa do mesmo leitor para a mesma obra.
Cancelar reserva|O leitor deve cancelar uma reserva própria ainda ativa, liberando sua posição na fila.
Consultar fila de reservas|O atendente deve consultar a fila de uma obra em ordem de data e horário da solicitação.
Separar exemplar reservado|Após uma devolução, o atendente deve separar o exemplar para o primeiro leitor elegível da fila de reservas.
Avisar disponibilidade de reserva|O sistema deve enviar ao contato do leitor um aviso quando um exemplar de sua reserva for separado.
Expirar retirada de reserva|O sistema deve encerrar a separação não retirada após dois dias corridos e disponibilizar o exemplar ao próximo leitor da fila.
Converter reserva em empréstimo|O atendente deve registrar a retirada de um exemplar separado e encerrar a reserva correspondente na mesma operação.
Calcular multa de atraso|O sistema deve calcular uma multa fictícia de um real por dia corrido de atraso após a devolução.
Consultar débitos|O leitor deve consultar os débitos em aberto, identificando o empréstimo de origem e o valor de cada cobrança.
Registrar pagamento de multa|O atendente deve registrar o pagamento integral de uma multa com data e forma de pagamento, encerrando o débito.
Isentar multa|O bibliotecário deve registrar uma isenção com justificativa e manter o valor original disponível para consulta.
Emitir recibo de pagamento|O atendente deve emitir recibo com leitor, multa, valor recebido e data do pagamento.
Notificar vencimento próximo|O sistema deve enviar um aviso ao leitor um dia antes do prazo de devolução de um empréstimo ativo.
Notificar atraso|O sistema deve enviar um aviso de atraso no primeiro dia posterior ao vencimento de um empréstimo não devolvido.
Configurar preferência de avisos|O leitor deve ativar ou desativar avisos de vencimento próximo em seu cadastro de preferências.
Relatar circulação por período|O bibliotecário deve gerar um relatório de empréstimos e devoluções entre duas datas informadas.
Relatar obras mais emprestadas|O bibliotecário deve consultar as dez obras mais emprestadas em um período, com a quantidade de empréstimos de cada uma.
Relatar acervo por assunto|O bibliotecário deve gerar um resumo com quantidades de obras e exemplares agrupadas por assunto.
Exportar relatório de atrasos|O atendente deve exportar a lista de atrasos em CSV contendo matrícula, patrimônio e prazo de devolução.
Registrar calendário de fechamento|O bibliotecário deve cadastrar datas de fechamento da unidade e disponibilizá-las para consulta dos leitores.
Sugerir aquisição|O leitor deve enviar uma sugestão de aquisição com título, autor e justificativa para avaliação do bibliotecário.
Acompanhar sugestão de aquisição|O leitor deve consultar suas sugestões com situação recebida, em avaliação, aprovada ou recusada e justificativa da resposta.
""";

    private const string NonFunctional = """
Tempo de resposta da pesquisa|Com dez mil obras e vinte consultas simultâneas, pelo menos 95% das pesquisas por título devem responder em até dois segundos no ambiente de homologação documentado.
Tempo de registro de empréstimo|Com vinte atendimentos simultâneos, pelo menos 95% dos registros de empréstimo devem concluir em até três segundos no ambiente de homologação documentado.
Disponibilidade mensal|O serviço de consulta do catálogo deve apresentar disponibilidade mensal de pelo menos 99%, medida por verificações externas a cada minuto.
Navegação por teclado|As telas de pesquisa, reserva e área pessoal devem permitir concluir todas as ações usando apenas teclado, com foco visível.
Rótulos acessíveis|Todos os campos dos formulários de leitor, empréstimo e reserva devem possuir rótulos programaticamente associados, verificáveis por leitor de tela.
Contraste do texto|Textos comuns das telas de catálogo devem ter razão de contraste de pelo menos 4,5 para 1 em relação ao fundo.
Ampliação de conteúdo|As telas de pesquisa e ficha da obra devem manter conteúdo e ações disponíveis com ampliação de 200% no navegador.
Adaptação a dispositivos móveis|O catálogo e a área pessoal devem funcionar em uma largura de 360 pixels sem rolagem horizontal para leitura do conteúdo principal.
Proteção do transporte|A implantação de homologação deve aceitar sessões autenticadas somente por HTTPS e redirecionar solicitações HTTP para HTTPS.
Proteção de senhas|As senhas devem ser armazenadas somente como hashes com salt por usuário, sem valores reversíveis ou texto original no banco.
Controle de acesso a empréstimos|Uma sessão de leitor não deve consultar empréstimos de outro leitor por alteração de identificadores nas URLs ou requisições.
Permissão de baixa de exemplar|Somente sessões com permissão de bibliotecário devem conseguir concluir a baixa de um exemplar, inclusive por requisição direta.
Expiração por inatividade|As sessões autenticadas devem exigir novo login após trinta minutos sem atividade do usuário.
Proteção contra tentativas de login|Após cinco falhas consecutivas de autenticação em uma conta, novas tentativas devem ser bloqueadas por cinco minutos.
Ausência de segredos nos logs|Os registros operacionais não devem conter senhas, tokens de sessão ou credenciais de serviços, verificado por inspeção dos cenários de falha.
Auditoria de empréstimos|Cada criação, renovação e devolução de empréstimo deve registrar operador, instante e identificador da operação para consulta autorizada.
Atomicidade da retirada reservada|A criação do empréstimo e o encerramento da reserva correspondente devem ser confirmados juntos ou integralmente revertidos diante de uma falha.
Concorrência sobre exemplar|Duas solicitações simultâneas para emprestar o mesmo exemplar devem resultar em no máximo um empréstimo ativo confirmado.
Integridade de referências|O armazenamento deve impedir empréstimos que referenciem leitores ou exemplares inexistentes, inclusive em operações fora da interface.
Cópia diária de segurança|O ambiente de homologação deve produzir uma cópia consistente diária do banco e conservar as sete cópias diárias mais recentes.
Restauração verificável|Uma cópia diária deve permitir restaurar o banco em ambiente isolado em até trinta minutos, com contagens e integridade verificadas.
Recuperação de falha de notificação|Uma falha no envio de aviso não deve desfazer um empréstimo confirmado e deve ficar registrada sem expor o contato do leitor.
Compatibilidade de navegadores|Pesquisa, reserva e renovação devem passar pelo roteiro funcional nas versões estáveis de Chrome, Firefox e Edge registradas na homologação.
Codificação de caracteres|O sistema deve preservar acentos de nomes e títulos em cadastro, consulta e exportação CSV usando codificação UTF-8.
Representação monetária|Os valores de multa devem ser calculados com precisão de centavos e apresentados com duas casas decimais e separador decimal brasileiro.
Consistência de horários|Datas de retirada, devolução e reserva devem apresentar o fuso da biblioteca explicitamente nos relatórios para evitar interpretação ambígua.
Limitação de arquivos exportados|A exportação CSV deve neutralizar células iniciadas por fórmulas para impedir sua execução ao abrir o arquivo em uma planilha.
Mensagens de falha seguras|Erros inesperados devem apresentar um identificador de ocorrência ao usuário sem revelar comandos SQL, caminhos internos ou rastreamentos de pilha.
Preservação após reinício|Empréstimos e reservas confirmados devem permanecer consultáveis após reinício do serviço, comprovado por comparação dos identificadores antes e depois.
Documentação de implantação|A entrega deve incluir instruções reproduzíveis de configuração, inicialização, backup e restauração, validadas em um ambiente de homologação vazio.
""";

    public static List<Requisito> Create()
    {
        var result = new List<Requisito>();
        Add(Functional, TipoRequisito.Funcional);
        Add(NonFunctional, TipoRequisito.NaoFuncional);
        if (result.Count != 100) throw new InvalidOperationException("O catálogo V1 deve conter exatamente 100 itens.");
        foreach (var item in result) Validate(item);
        if (result.Select(r => r.Titulo).Distinct(StringComparer.Ordinal).Count() != 100)
            throw new InvalidOperationException("Títulos duplicados no catálogo.");
        return result;

        void Add(string text, TipoRequisito type)
        {
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('|');
                if (parts.Length != 2) throw new InvalidOperationException("Entrada inválida no catálogo.");
                result.Add(new Requisito
                {
                    Titulo = $"[TESTE-BIB-V1-{result.Count + 1:000}] {parts[0]}",
                    Descricao = parts[1],
                    Tipo = type, Status = StatusRequisito.Pendente
                });
            }
        }
    }

    public static void Validate(Requisito item)
    {
        Validator.ValidateObject(item, new ValidationContext(item), validateAllProperties: true);
        if (item.Id != 0 || item.Status != StatusRequisito.Pendente || item.DataCriacao == default ||
            item.Titulo != item.Titulo.Trim() || item.Descricao != item.Descricao.Trim())
            throw new ValidationException("O novo requisito deve ter ID automático, data válida, status Pendente e textos normalizados.");
    }
}
