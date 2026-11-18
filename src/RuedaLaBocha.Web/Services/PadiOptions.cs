namespace RuedaLaBocha.Web.Services;

public class PadiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://padi.runasp.net";
    public int TtlMinutes { get; set; } = 30;
}
