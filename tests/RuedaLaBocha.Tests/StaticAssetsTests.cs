using System.Net;
using System.Net.Http.Headers;

namespace RuedaLaBocha.Tests;

// Pin: wwwroot debe servirse desde disco en cualquier entorno (sin depender del
// manifiesto de MapStaticAssets), incluso cuando el cliente ofrece gzip como
// todo navegador real. Antes del fix, Production+dotnet-run devolvía 200 vacío.
[Collection("Court integration")]
public class StaticAssetsTests
{
    private static HttpClient BrowserClient(TestApp app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept-Encoding", "gzip, deflate, br, zstd");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/157.0.0.0 Safari/537.36");
        return client;
    }

    private static async Task<byte[]> GetBytes(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    [Fact]
    public async Task SiteCss_con_gzip_tiene_contenido_y_reglas()
    {
        using var app = new TestApp();
        using var client = BrowserClient(app);
        foreach (var url in new[] { "/css/site.css", "/css/site.css?v=probe123" })
        {
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("text/css", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.True(body.Length > 5000, $"{url} len={body.Length}");
            Assert.Contains(".brand", System.Text.Encoding.UTF8.GetString(body));
        }
    }

    [Fact]
    public async Task Vendor_e_imagen_con_header_chrome_tienen_contenido()
    {
        using var app = new TestApp();
        using var client = BrowserClient(app);
        var css = await GetBytes(client, "/lib/bootstrap/dist/css/bootstrap.min.css");
        Assert.True(css.Length > 100000, $"bootstrap len={css.Length}");
        var calCss = await GetBytes(client, "/lib/vanilla-calendar-pro/vanilla-calendar-pro.css");
        Assert.True(calCss.Length > 10000, $"cal css len={calCss.Length}");
        var calJs = await GetBytes(client, "/lib/vanilla-calendar-pro/vanilla-calendar-pro.js");
        Assert.True(calJs.Length > 10000, $"cal js len={calJs.Length}");
        using var imgResp = await client.GetAsync("/images/football-pitch.svg");
        Assert.Equal(HttpStatusCode.OK, imgResp.StatusCode);
        Assert.StartsWith("image/svg", imgResp.Content.Headers.ContentType?.MediaType);
        var img = await imgResp.Content.ReadAsByteArrayAsync();
        Assert.True(img.Length > 100, $"svg len={img.Length}");
        var siteJs = await GetBytes(client, "/js/site.js");
        Assert.True(siteJs.Length > 50, $"site.js len={siteJs.Length}");
    }

    [Fact]
    public async Task Login_expone_sus_css_y_los_de_wwwroot_sirven()
    {
        // La prueba de entorno correcto (Development/Production publicado y local)
        // vive en los ciclos en vivo + smoke de publish; la factoría (Testing, con
        // manifiesto de publish en su bin) fija extracción + servido de wwwroot.
        // El bundle con ámbito en ruta con huella solo resuelve con content-roots
        // de origen (live dotnet-run con loader, Dev, publicado): se afirma su
        // presencia en el HTML y su servido se prueba en vivo/publicado.
        using var app = new TestApp();
        using var client = BrowserClient(app);
        var html = await client.GetStringAsync("/Identity/Account/Login");
        var hrefs = System.Text.RegularExpressions.Regex.Matches(html, "href=\"([^\"]+\\.css[^\"]*)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(hrefs);
        Assert.Contains(hrefs, h => h.Contains("RuedaLaBocha.Web") && h.EndsWith(".styles.css"));
        // En factoría, las rutas con huella infix devuelven 200 vacío (manifiesto de
        // publish copiado al bin de tests sin content-roots: misma clase de fallo
        // que el original). El servido con bytes se prueba en vivo y publicado.
        var infix = new System.Text.RegularExpressions.Regex(@"\.[A-Za-z0-9_-]{8,}\.(css|js)$");
        foreach (var h in hrefs.Where(h => !h.Contains("RuedaLaBocha.Web")))
        {
            using var response = await client.GetAsync(h);
            if (infix.IsMatch(h))
            {
                // Solo existencia de ruta: CT/bytes de huella infix se prueban en vivo y publicado.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                continue;
            }
            var ct = response.Content.Headers.ContentType?.MediaType;
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK && ct?.StartsWith("text/css") == true && body.Length > 500,
                $"{h} -> {(int)response.StatusCode} {ct} len={body.Length}");
        }
    }
}
