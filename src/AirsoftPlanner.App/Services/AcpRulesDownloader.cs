using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using AirsoftPlanner.Core.Documents;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services;

/// <summary>Règlement ACP téléchargé : contenu du PDF officiel et version indiquée dans son nom.</summary>
public record AcpRulesFile(byte[] Content, string Version, string SourceUrl);

/// <summary>Télécharge la dernière version du règlement ACP depuis son site officiel.</summary>
public static class AcpRulesDownloader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AirsoftPlanner", "1.0"));
        return client;
    }

    /// <exception cref="HttpRequestException">Site injoignable ou lien du règlement introuvable.</exception>
    public static async Task<AcpRulesFile> DownloadAsync()
    {
        var html = await Http.GetStringAsync(AcpRules.HomeUrl);
        var url = AcpRules.FindPdfUrl(html) ?? throw new HttpRequestException(L.T("lien_du_reglement_introuvable_sur_le_site_acp"));
        var content = await Http.GetByteArrayAsync(url);
        // Un PDF commence par « %PDF » : sinon le site a renvoyé autre chose (page d'erreur...).
        if (content.Length < 4 || content[0] != '%' || content[1] != 'P' || content[2] != 'D' || content[3] != 'F')
            throw new HttpRequestException(L.T("le_site_acp_n_a_pas_renvoye_un_pdf"));
        return new AcpRulesFile(content, AcpRules.VersionOf(url), url);
    }
}
