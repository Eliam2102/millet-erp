using Microsoft.AspNetCore.DataProtection;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

/// <summary>
/// Tests del <see cref="FiscalSecretCipher"/> usando
/// <see cref="EphemeralDataProtectionProvider"/> — el provider in-memory
/// que ASP.NET expone para tests. No requiere KV ni filesystem.
/// </summary>
public sealed class FiscalSecretCipherTests
{
    private static FiscalSecretCipher NewCipher() =>
        new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Encrypt_Decrypt_round_trip_devuelve_el_plaintext_original()
    {
        var cipher = NewCipher();
        const string plaintext = "fiscal-api-key-supersecreta-12345";

        var ciphertext = cipher.Encrypt(plaintext);
        var roundTrip = cipher.Decrypt(ciphertext);

        roundTrip.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_produce_ciphertext_distinto_cada_vez_por_iv_random()
    {
        var cipher = NewCipher();
        const string plaintext = "fiscal-api-key";

        var c1 = cipher.Encrypt(plaintext);
        var c2 = cipher.Encrypt(plaintext);

        c1.Should().NotEqual(c2);
        cipher.Decrypt(c1).Should().Be(plaintext);
        cipher.Decrypt(c2).Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_rechaza_plaintext_vacio_o_nulo()
    {
        var cipher = NewCipher();

        Action emptyAct = () => cipher.Encrypt("");
        Action nullAct = () => cipher.Encrypt(null!);

        emptyAct.Should().Throw<ArgumentException>();
        nullAct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrypt_rechaza_ciphertext_vacio()
    {
        var cipher = NewCipher();

        var act = () => cipher.Decrypt([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HashForChangeDetection_es_determinista()
    {
        const string plaintext = "fiscal-api-key";

        var h1 = FiscalSecretCipher.HashForChangeDetection(plaintext);
        var h2 = FiscalSecretCipher.HashForChangeDetection(plaintext);

        h1.Should().Be(h2);
        h1.Should().HaveLength(64); // SHA256 hex
        h1.Should().MatchRegex("^[0-9a-f]+$"); // lowercase hex
    }

    [Fact]
    public void HashForChangeDetection_difiere_para_plaintexts_distintos()
    {
        var h1 = FiscalSecretCipher.HashForChangeDetection("key-uno");
        var h2 = FiscalSecretCipher.HashForChangeDetection("key-dos");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void HashForChangeDetection_rechaza_plaintext_vacio()
    {
        Action act = () => FiscalSecretCipher.HashForChangeDetection("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Cipher_con_provider_distinto_no_puede_descifrar_ciphertext_de_otro()
    {
        // Simula el caso real: si el ring de keys se pierde / se regenera,
        // los ciphertext existentes en BD quedan ilegibles. Confirma
        // fail-fast en lugar de retornar basura.
        var cipherA = NewCipher();
        var cipherB = NewCipher(); // provider distinto, ring distinto

        var ciphertext = cipherA.Encrypt("plaintext");
        var act = () => cipherB.Decrypt(ciphertext);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Purpose_string_es_el_contrato_estable_v1()
    {
        // Guarda-railes: si alguien cambia accidentalmente la constante,
        // los ciphertext en producción dejan de ser descifrables. Si
        // realmente hay que rotar el algoritmo, bump explícito a v2 + plan
        // de migración de columnas (no es un cambio menor).
        FiscalSecretCipher.Purpose.Should().Be("Integraciones.Fiscal.ApiKey.v1");
    }
}
