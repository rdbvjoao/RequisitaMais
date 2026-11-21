using System.Text.Json;

namespace RequisitaPlus.Web.Models;

public class AnaliseRequisito
{
    public int Id { get; set; }
    public int RequisitoId { get; set; }
    public Requisito Requisito { get; set; } = null!;
    public DateTime DataAnalise { get; set; }
    public string ResultadoJson { get; set; } = "";
    public int? TokensEntrada { get; set; }
    public int? TokensSaida { get; set; }
    public int? TokensTotal { get; set; }
    public List<DecisaoSugestao> Decisoes { get; set; } = [];

    public ResultadoAnalise ObterResultado()
    {
        var resultado = JsonSerializer.Deserialize<ResultadoAnalise>(ResultadoJson)!;
        resultado.UsoTokens = new UsoTokensAnalise(TokensEntrada, TokensSaida, TokensTotal);
        return resultado;
    }
}
