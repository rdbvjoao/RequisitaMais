using System.ComponentModel.DataAnnotations;

namespace RequisitaPlus.Web.Models;

public class RevisarSugestaoViewModel
{
    public int AnaliseId { get; set; }
    public int IndiceSugestao { get; set; }
    public string VersaoRequisito { get; set; } = "";
    [Required(ErrorMessage = "Escolha uma decisão.")]
    [EnumDataType(typeof(TipoDecisaoSugestao), ErrorMessage = "Escolha uma decisão válida.")]
    public TipoDecisaoSugestao? Decisao { get; set; }
    [StringLength(5000, ErrorMessage = "A redação final deve ter no máximo 5.000 caracteres.")]
    [Display(Name = "Redação final completa do requisito")]
    public string? RedacaoFinal { get; set; }
    public bool Confirmacao { get; set; }
    public Requisito? Requisito { get; set; }
    public ProblemaAnalise? Sugestao { get; set; }
}
