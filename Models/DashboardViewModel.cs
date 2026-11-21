namespace RequisitaPlus.Web.Models;

public class DashboardViewModel
{
    public int Total { get; init; }
    public int Pendentes { get; init; }
    public int EmAnalise { get; init; }
    public int Revisados { get; init; }
    public IReadOnlyList<Requisito> Recentes { get; init; } = [];
}
