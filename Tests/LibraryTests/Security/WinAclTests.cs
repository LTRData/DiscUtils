using DiscUtils.Core.WindowsSecurity;
using Xunit;

namespace LibraryTests.Security;


public class WinAclTests
{
    [Fact]
    public void SidTest()
    {
        var sid = new SecurityIdentifier("S-1-5-21-3445421715-2530590580-3149308974-500");
        var domain = sid.AccountDomainSid;
        Assert.Equal("S-1-5-21-3445421715-2530590580-3149308974", domain.Value);

        Assert.Equal(500u, sid.AccountUserId);

        var subsid = domain.CreateSubSid(500u);

        Assert.Equal(sid, subsid);
    }
}
