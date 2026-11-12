using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace NitroDrp.Tests;

/// <summary>
/// A stand-in for a phone's passkey: makes a key pair, and answers the server's two prompts the way a real
/// authenticator does (CBOR attestation, DER-signed assertion), so the server's real verification runs in tests.
/// </summary>
public sealed class SoftAuthenticator(string origin = "https://drop.example.com", string rpId = "drop.example.com")
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
    private byte[] _userHandle = [];
    private uint _counter;

    public string CredentialId => B64(_credentialId);

    /// <summary>Answers "create a passkey" (options is the server's JSON).</summary>
    public object Register(JsonElement options)
    {
        var challenge = options.GetProperty("challenge").GetString()!;
        _userHandle = WebEncoders.Base64UrlDecode(options.GetProperty("user").GetProperty("id").GetString()!);

        var p = _key.ExportParameters(false);
        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);        // kty: EC2
        cose.WriteInt32(3); cose.WriteInt32(-7);       // alg: ES256
        cose.WriteInt32(-1); cose.WriteInt32(1);       // crv: P-256
        cose.WriteInt32(-2); cose.WriteByteString(p.Q.X!);
        cose.WriteInt32(-3); cose.WriteByteString(p.Q.Y!);
        cose.WriteEndMap();

        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.Add(0x45); // user present + user verified + attested credential data
        authData.AddRange(Counter(_counter));
        authData.AddRange(new byte[16]); // AAGUID: unknown device, fine with "none" attestation
        authData.Add((byte)(_credentialId.Length >> 8)); authData.Add((byte)_credentialId.Length);
        authData.AddRange(_credentialId);
        authData.AddRange(cose.Encode());

        var att = new CborWriter(CborConformanceMode.Ctap2Canonical);
        att.WriteStartMap(3);
        att.WriteTextString("fmt"); att.WriteTextString("none");
        att.WriteTextString("attStmt"); att.WriteStartMap(0); att.WriteEndMap();
        att.WriteTextString("authData"); att.WriteByteString(authData.ToArray());
        att.WriteEndMap();

        return new
        {
            id = B64(_credentialId),
            rawId = B64(_credentialId),
            type = "public-key",
            response = new { attestationObject = B64(att.Encode()), clientDataJSON = B64(ClientData("webauthn.create", challenge)) },
        };
    }

    /// <summary>Answers "sign in" (options is the server's JSON). <paramref name="mutate"/> lets a test break something.</summary>
    public object Login(JsonElement options, Action<byte[]>? tamperSignature = null)
    {
        var challenge = options.GetProperty("challenge").GetString()!;
        _counter++;
        var authData = SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).Concat(new byte[] { 0x05 }).Concat(Counter(_counter)).ToArray();
        var clientData = ClientData("webauthn.get", challenge);
        var signature = _key.SignData(authData.Concat(SHA256.HashData(clientData)).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        tamperSignature?.Invoke(signature);
        return new
        {
            id = B64(_credentialId),
            rawId = B64(_credentialId),
            type = "public-key",
            response = new { authenticatorData = B64(authData), clientDataJSON = B64(clientData), signature = B64(signature), userHandle = B64(_userHandle) },
        };
    }

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false }));

    private static byte[] Counter(uint n) => [(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];
    private static string B64(byte[] b) => WebEncoders.Base64UrlEncode(b);
}
