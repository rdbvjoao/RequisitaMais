namespace RequisitaPlus.Web.Services;

public class GeminiOptions
{
    public string Modelo { get; set; } = "gemini-3.5-flash-lite";
    public int TimeoutSegundos { get; set; } = 120;
    public string ApiKey { get; set; } = "";
}
