using System.Text;
using MiServiceSharp.Protocol.LocalMiio;

namespace MiServiceSharp.Tests;

public sealed class LocalMiioCryptoTests
{
    [Fact]
    public void EncryptDecrypt_ShouldRoundTrip()
    {
        var token = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var plain = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"miIO.info\",\"params\":[]}");

        var cipher = MiioCrypto.Encrypt(token, plain);
        var back = MiioCrypto.Decrypt(token, cipher);

        Assert.Equal(plain, back);
    }

    [Fact]
    public void BuildHelloPacket_ShouldBe32BytesAndValidMagic()
    {
        var packet = MiioCrypto.BuildHelloPacket();

        Assert.Equal(32, packet.Length);
        Assert.Equal((byte)0x21, packet[0]);
        Assert.Equal((byte)0x31, packet[1]);
        Assert.Equal((byte)0x00, packet[2]);
        Assert.Equal((byte)0x20, packet[3]);
    }

    [Fact]
    public void BuildPacket_ShouldContainHeaderAndPayload()
    {
        var token = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var payload = new byte[] { 1, 2, 3, 4 };
        var packet = MiioCrypto.BuildPacket(1234, 5678, token, payload);

        Assert.True(packet.Length >= 36);
        Assert.Equal((byte)0x21, packet[0]);
        Assert.Equal((byte)0x31, packet[1]);
    }
}
