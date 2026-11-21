using System.ComponentModel.DataAnnotations;

namespace RequisitaPlus.Web.Models;

public enum TipoDecisaoSugestao
{
    [Display(Name = "Aceitar")] Aceitar = 1,
    [Display(Name = "Ajustar manualmente")] Ajustar = 2,
    [Display(Name = "Descartar")] Descartar = 3
}

public class DecisaoSugestao
{
    public int Id { get; set; }
    public int AnaliseRequisitoId { get; set; }
    public AnaliseRequisito AnaliseRequisito { get; set; } = null!;
    // Posição estável no ResultadoJson original, que nunca é modificado pela revisão.
    public int IndiceSugestao { get; set; }
    public TipoDecisaoSugestao Decisao { get; set; }
    public string DescricaoAnterior { get; set; } = "";
    public string? RedacaoFinal { get; set; }
    public string UsuarioId { get; set; } = "";
    public DateTime DataDecisao { get; set; }
}
