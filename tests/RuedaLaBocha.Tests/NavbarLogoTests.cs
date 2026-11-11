using System.Net;
using System.Security.Cryptography;

namespace RuedaLaBocha.Tests;

// Logo navbar header con el PNG original del usuario (Group(1).png 180x180):
// bytes exactos, sin redibujar. Footer conserva su marca intacta.
[Collection("Court integration")]
public sealed class NavbarLogoTests
{
    private static string FindRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = parts.Aggregate(d.FullName, Path.Combine);
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, Path.GetFileName(parts[^1]));
    }

    private static string WwwRootLogo() => FindRepoFile("src", "RuedaLaBocha.Web", "wwwroot", "images", "logo-rueda-la-bocha.png");
    private static string SourceLogo() => FindRepoFile("src", "RuedaLaBocha.Web", "img", "Group(1).png");

    private static (int Width, int Height) PngDims(byte[] b)
    {
        Assert.True(b.Length > 32, $"png len={b.Length}");
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(sig, b[..8]);
        var w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        var h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        return (w, h);
    }

    [Fact]
    public void Logo_asset_son_bytes_exactos_del_original()
    {
        var dest = WwwRootLogo();
        var src = SourceLogo();
        Assert.True(File.Exists(dest), $"falta {dest}");
        Assert.True(File.Exists(src), $"falta fuente {src}");
        var destBytes = File.ReadAllBytes(dest);
        var srcBytes = File.ReadAllBytes(src);
        Assert.True(destBytes.Length > 2048, $"png len={destBytes.Length}");
        Assert.Equal((180, 180), PngDims(destBytes));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(srcBytes)),
            Convert.ToHexString(SHA256.HashData(destBytes)));
    }

    [Fact]
    public async Task Header_usa_logo_y_footer_conserva_emoji()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = System.Net.WebUtility.HtmlDecode(await guest.GetStringAsync("/Reservas"));
        Assert.Contains("images/logo-rueda-la-bocha.png", html);
        Assert.Contains("alt=\"\"", html);
        Assert.Contains("width=\"180\"", html);
        Assert.Contains("height=\"180\"", html);
        Assert.Contains(">Rueda la Bocha<", html);
        Assert.Contains("COMPLEJO DE FÚTBOL 5", html);
        // Footer intacto: conserva su emoji y su texto (header ya no usa emoji).
        Assert.Contains("⚽", html);
        Assert.Contains("El punto de encuentro de tu equipo.", html);
    }

    [Fact]
    public async Task Logo_servido_como_png_con_bytes()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        using var response = await guest.GetAsync("/images/logo-rueda-la-bocha.png");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new FileInfo(WwwRootLogo()).Length, bytes.Length);
        Assert.Equal((180, 180), PngDims(bytes));
    }

    [Fact]
    public async Task Css_scoped_al_header_sin_tocar_footer()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var css = await guest.GetStringAsync("/css/site.css");
        Assert.Contains(".site-header .brand-mark img", css);
        var m = System.Text.RegularExpressions.Regex.Match(css, @"\.site-header \.brand-mark img\s*\{[^}]*\}");
        Assert.True(m.Success, "falta bloque .site-header .brand-mark img");
        Assert.Contains("object-fit: contain", m.Value);
        // Global intacta para el footer (emoji + círculo lima).
        Assert.Contains("var(--lime)", css);
    }
}
