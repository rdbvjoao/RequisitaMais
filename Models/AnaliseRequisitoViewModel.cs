using System.Text.Json.Serialization;

namespace RequisitaPlus.Web.Models;

public class AnaliseRequisitoViewModel
{
    public required Requisito Requisito { get; init; }
    public ResultadoAnalise? Resultado { get; init; }
    public string? Erro { get; init; }
    public int CaracteresDescricao => Requisito.Descricao?.Length ?? 0;
    public int LimiteCaracteresDescricao => LimitesAnaliseIA.MaximoCaracteresDescricao;
    public string? ErroDescricao => LimitesAnaliseIA.ObterErroDescricao(Requisito.Descricao);
    public bool PodeAnalisar => ErroDescricao is null;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class ResultadoAnalise
{
    [JsonPropertyName("problemas")]
    public required List<ProblemaAnalise> Problemas { get; init; }

    [JsonIgnore]
    public UsoTokensAnalise? UsoTokens { get; set; }
}

public record UsoTokensAnalise(int? Entrada, int? Saida, int? Total);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class ProblemaAnalise
{
    [JsonPropertyName("categoria")]
    public required string Categoria { get; init; }
    [JsonPropertyName("problema")]
    public required string Problema { get; init; }
    [JsonPropertyName("justificativa")]
    public required string Justificativa { get; init; }
    [JsonPropertyName("sugestao")]
    public required string Sugestao { get; init; }
}
