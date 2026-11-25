using System.ComponentModel.DataAnnotations;

namespace RequisitaPlus.Web.Models;

public enum TipoRequisito
{
    [Display(Name = "Funcional")] Funcional = 1,
    [Display(Name = "Não Funcional")] NaoFuncional = 2
}

public enum StatusRequisito
{
    [Display(Name = "Pendente")] Pendente = 1,
    [Display(Name = "Em análise")] EmAnalise = 2,
    [Display(Name = "Revisado")] Revisado = 3
}

public class Requisito
{
    public int Id { get; set; }
    public string ProprietarioId { get; set; } = null!;
    public Microsoft.AspNetCore.Identity.IdentityUser? Proprietario { get; set; }
    public string VersaoConcorrencia { get; set; } = Guid.NewGuid().ToString("N");

    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(150, ErrorMessage = "O título deve ter no máximo 150 caracteres.")]
    [Display(Name = "Título")]
    public string Titulo { get; set; } = "";

    [Required(ErrorMessage = "Informe a descrição.")]
    [StringLength(5000, ErrorMessage = "A descrição deve ter no máximo 5.000 caracteres.")]
    [Display(Name = "Descrição")]
    public string Descricao { get; set; } = "";

    [Required(ErrorMessage = "Selecione o tipo.")]
    [EnumDataType(typeof(TipoRequisito), ErrorMessage = "Selecione um tipo válido.")]
    public TipoRequisito? Tipo { get; set; }

    [Required(ErrorMessage = "Selecione o status.")]
    [EnumDataType(typeof(StatusRequisito), ErrorMessage = "Selecione um status válido.")]
    public StatusRequisito? Status { get; set; } = StatusRequisito.Pendente;

    [Display(Name = "Data de criação")]
    public DateTime DataCriacao { get; set; } = DateTime.Now;
}
