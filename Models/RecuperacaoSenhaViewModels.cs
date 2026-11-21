using System.ComponentModel.DataAnnotations;
namespace RequisitaPlus.Web.Models;
public sealed class SolicitarRecuperacaoViewModel
{
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "Informe um e-mail válido."), StringLength(256, ErrorMessage = "O e-mail deve ter no máximo 256 caracteres."), Display(Name = "E-mail")]
    public string Email { get; set; } = "";
}
public sealed class ValidarCodigoViewModel
{
    [Required(ErrorMessage = "Informe o código."), RegularExpression("^[0-9]{6}$", ErrorMessage = "Informe os 6 dígitos do código."), Display(Name = "Código de recuperação")]
    public string Codigo { get; set; } = "";
}
public sealed class NovaSenhaViewModel
{
    [Required(ErrorMessage = "Informe a nova senha."), DataType(DataType.Password), Display(Name = "Nova senha")]
    public string Senha { get; set; } = "";
    [Required(ErrorMessage = "Confirme a nova senha."), DataType(DataType.Password), Display(Name = "Confirmar nova senha"), Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
    public string ConfirmacaoSenha { get; set; } = "";
}
