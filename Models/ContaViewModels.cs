using System.ComponentModel.DataAnnotations;
namespace RequisitaPlus.Web.Models;
public class LoginViewModel
{
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "Informe um e-mail válido."), Display(Name = "E-mail")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Informe a senha."), DataType(DataType.Password), Display(Name = "Senha")]
    public string Senha { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public class CadastroViewModel : LoginViewModel
{
    [Required(ErrorMessage = "Confirme a senha."), DataType(DataType.Password), Display(Name = "Confirmar senha"), Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    public string ConfirmacaoSenha { get; set; } = "";
}
