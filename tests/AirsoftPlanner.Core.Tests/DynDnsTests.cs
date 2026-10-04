using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Core.Tests;

public class DynDnsTests
{
    [Fact]
    public void Ip_placeholder_is_replaced()
    {
        Assert.Equal("https://www.duckdns.org/update?domains=monop&token=abc&ip=192.168.1.20",
            DynDns.BuildUrl(" https://www.duckdns.org/update?domains=monop&token=abc&ip={ip} ", "192.168.1.20"));
        Assert.Equal("https://dynupdate.no-ip.com/nic/update?hostname=x", DynDns.BuildUrl("https://dynupdate.no-ip.com/nic/update?hostname=x", "10.0.0.1"));
    }

    [Theory]
    [InlineData("OK", true)]
    [InlineData("good 192.168.1.20", true)]
    [InlineData("nochg 192.168.1.20", true)]
    [InlineData("KO", false)]
    [InlineData("badauth", false)]
    [InlineData("nohost", false)]
    public void Responses_of_common_services_are_understood(string response, bool success) =>
        Assert.Equal(success, DynDns.IsSuccess(response));

    [Theory]
    [InlineData("monop.duckdns.org", "http://monop.duckdns.org:5055")]
    [InlineData("monop.duckdns.org:8080", "http://monop.duckdns.org:8080")]
    [InlineData("http://192.168.1.20:5055/", "http://192.168.1.20:5055")]
    [InlineData("https://op.example.org", "https://op.example.org:5055")]
    [InlineData("  ", "")]
    public void Server_address_is_completed(string address, string expected) =>
        Assert.Equal(expected, EnrollmentLink.ServerUrl(address, 5055));
}
