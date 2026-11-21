using System.Globalization;

namespace RequisitaPlus.Web.Models;

public static class LimitesAnaliseIA
{
    public const int MaximoCaracteresDescricao = 1000;

    public static string? ObterErroDescricao(string? descricao)
    {
        if (string.IsNullOrWhiteSpace(descricao)) return "Informe a descrição do requisito antes de solicitar a análise com IA.";
        var excesso = descricao.Length - MaximoCaracteresDescricao;
        if (excesso <= 0) return null;
        var cultura = CultureInfo.GetCultureInfo("pt-BR");
        return $"A análise com IA permite até {MaximoCaracteresDescricao.ToString("N0", cultura)} caracteres na descrição. " +
            $"Reduza {excesso.ToString("N0", cultura)} {(excesso == 1 ? "caractere" : "caracteres")} na edição do requisito e salve antes de solicitar a análise novamente.";
    }
}
