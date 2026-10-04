using System.Net;
using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Core.Tests;

public class DiscoveryTests
{
    private static readonly Guid Op = Guid.NewGuid();

    // Port propre au test, sur la boucle locale : pas de dépendance au réseau de la machine.
    private static IPEndPoint[] Loopback(int port) => [new IPEndPoint(IPAddress.Loopback, port)];

    [Fact]
    public async Task Phone_finds_the_current_address_of_its_operation_pc()
    {
        const int port = 45811;
        var address = "http://192.168.1.20:5055";
        using var responder = new DiscoveryResponder(_ => new DiscoveryReply(address, "OP Tempête", Op), port);

        var found = await Discovery.FindAsync(Op, TimeSpan.FromSeconds(2), Loopback(port));
        Assert.Equal(address, found?.Server);

        // L'IP du PC change : la réponse suit.
        address = "http://10.0.0.7:5055";
        Assert.Equal(address, (await Discovery.FindAsync(Op, TimeSpan.FromSeconds(2), Loopback(port)))?.Server);
    }

    [Fact]
    public async Task Pc_of_another_operation_is_ignored()
    {
        const int port = 45812;
        using var responder = new DiscoveryResponder(_ => new DiscoveryReply("http://192.168.1.30:5055", "Autre OP", Guid.NewGuid()), port);

        Assert.Null(await Discovery.FindAsync(Op, TimeSpan.FromMilliseconds(800), Loopback(port)));
        Assert.NotNull(await Discovery.FindAsync(null, TimeSpan.FromSeconds(2), Loopback(port))); // avant l'enrôlement : toute OP
    }

    [Fact]
    public async Task No_answer_when_nobody_listens()
    {
        Assert.Null(await Discovery.FindAsync(Op, TimeSpan.FromMilliseconds(500), Loopback(45813)));
    }
}
