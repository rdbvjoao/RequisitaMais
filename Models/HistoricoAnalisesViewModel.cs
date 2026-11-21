namespace RequisitaPlus.Web.Models;

public class HistoricoAnalisesViewModel
{
    public required Requisito Requisito { get; init; }
    public required List<AnaliseRequisito> Analises { get; init; }
}
