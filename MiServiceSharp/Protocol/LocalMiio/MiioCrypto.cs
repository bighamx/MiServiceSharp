using System.Security.Cryptography;

namespace MiServiceSharp.Protocol.LocalMiio;

public static class MiioCrypto
{
    public const ushort Magic = 0x2131;

    public static byte[] Encrypt(byte[] token, byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = token;
        aes.IV = token;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }

    public static byte[] Decrypt(byte[] token, byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = token;
        aes.IV = token;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    public static byte[] BuildHelloPacket()
    {
        var packet = Enumerable.Repeat((byte)0xFF, 32).ToArray();
        packet[0] = 0x21;
        packet[1] = 0x31;
        packet[2] = 0x00;
        packet[3] = 0x20;
        return packet;
    }

    public static byte[] BuildPacket(uint deviceId, uint stamp, byte[] token, byte[] encryptedPayload)
    {
        var length = (ushort)(32 + encryptedPayload.Length);
        var packet = new byte[length];

        packet[0] = 0x21;
        packet[1] = 0x31;
        WriteUInt16BigEndian(packet, 2, length);
        WriteUInt32BigEndian(packet, 4, 0);
        WriteUInt32BigEndian(packet, 8, deviceId);
        WriteUInt32BigEndian(packet, 12, stamp);

        var checksumInput = new byte[16 + token.Length + encryptedPayload.Length];
        Buffer.BlockCopy(packet, 0, checksumInput, 0, 16);
        Buffer.BlockCopy(token, 0, checksumInput, 16, token.Length);
        Buffer.BlockCopy(encryptedPayload, 0, checksumInput, 16 + token.Length, encryptedPayload.Length);

        var checksum = MD5.HashData(checksumInput);
        Buffer.BlockCopy(checksum, 0, packet, 16, 16);
        Buffer.BlockCopy(encryptedPayload, 0, packet, 32, encryptedPayload.Length);
        return packet;
    }

    public static uint ReadUInt32BigEndian(byte[] source, int offset)
    {
        return ((uint)source[offset] << 24)
             | ((uint)source[offset + 1] << 16)
             | ((uint)source[offset + 2] << 8)
             | source[offset + 3];
    }

    private static void WriteUInt16BigEndian(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)((value >> 8) & 0xFF);
        target[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32BigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)((value >> 24) & 0xFF);
        target[offset + 1] = (byte)((value >> 16) & 0xFF);
        target[offset + 2] = (byte)((value >> 8) & 0xFF);
        target[offset + 3] = (byte)(value & 0xFF);
    }
}
