using Msem001.Watchdog.Alerting;

namespace Msem001.Watchdog.Tests.Alerting;

public sealed class MailAddressResolverTests
{
    [Test]
    [Arguments("j.doe", "@atbas.de", "j.doe@atbas.de")]
    [Arguments(" j.doe ", "atbas.de", "j.doe@atbas.de")]
    [Arguments("ext@example.com", "@atbas.de", "ext@example.com")]
    [Arguments("j.doe", "", "j.doe")]
    public async Task Resolve_AppendsSuffixToShortAddresses(string address, string suffix, string expected)
    {
        await Assert.That(MailAddressResolver.Resolve(address, suffix)).IsEqualTo(expected);
    }

    [Test]
    public async Task Resolve_EmptyAddress_Throws()
    {
        await Assert.That(() => MailAddressResolver.Resolve(" ", "@atbas.de")).Throws<ArgumentException>();
    }

    [Test]
    public async Task ResolveAll_SkipsEmptyAndRemovesDuplicates()
    {
        var resolved = MailAddressResolver.ResolveAll(["a", "A@atbas.de", " ", "b@x.de"], "@atbas.de");

        await Assert.That(string.Join(";", resolved)).IsEqualTo("a@atbas.de;b@x.de");
    }
}
