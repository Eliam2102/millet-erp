using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.SharedKernel.UnitTests.Domain;

public class AdjuntoTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static Adjunto Crear(
        DateOnly? vigenteHasta = null,
        string nombre = "csf.pdf",
        long tamano = 1024,
        string? hash = null) =>
        new(Guid.CreateVersion7(), "proveedor", Guid.CreateVersion7(), null, Guid.CreateVersion7(),
            nombre, "application/pdf", tamano, hash ?? Hash, "proveedor/x/y.pdf", vigenteHasta, Usuario, Ahora);

    [Fact]
    public void Vigente_Al_31_Oct_Sigue_Vigente_Ese_Dia_Y_Vence_El_1_Nov()
    {
        var a = Crear(new DateOnly(2026, 10, 31));

        a.EstadoEn(new DateOnly(2026, 10, 31)).Should().Be(EstadoAdjunto.PorVencer);
        a.EstadoEn(new DateOnly(2026, 11, 1)).Should().Be(EstadoAdjunto.Vencido);
    }

    [Theory]
    [InlineData("2026-10-06", "2027-01-01", EstadoAdjunto.Vigente)]
    [InlineData("2026-10-06", "2026-11-05", EstadoAdjunto.PorVencer)] // exactamente 30 días
    [InlineData("2026-10-06", "2026-11-06", EstadoAdjunto.Vigente)] // 31 días
    public void EstadoEn_Considera_Ventana_De_Aviso(string hoy, string hasta, EstadoAdjunto esperado)
    {
        Crear(DateOnly.Parse(hasta)).EstadoEn(DateOnly.Parse(hoy)).Should().Be(esperado);
    }

    [Fact]
    public void Sin_Vigencia_Retorna_SinVigencia()
    {
        Crear().EstadoEn(new DateOnly(2030, 1, 1)).Should().Be(EstadoAdjunto.SinVigencia);
    }

    [Fact]
    public void Baja_Registra_Quien_Cuando_Y_Por_Que_Y_Gana_Sobre_Vigencia()
    {
        var a = Crear(new DateOnly(2026, 10, 31));
        var otro = Guid.CreateVersion7();

        a.DarDeBaja("  Documento equivocado  ", otro, Ahora.AddDays(1));

        a.EstaDeBaja.Should().BeTrue();
        a.BajaPorId.Should().Be(otro);
        a.BajaEn.Should().Be(Ahora.AddDays(1));
        a.BajaMotivo.Should().Be("Documento equivocado");
        a.EstadoEn(new DateOnly(2026, 10, 6)).Should().Be(EstadoAdjunto.Baja);
    }

    [Fact]
    public void Baja_Doble_Es_Error()
    {
        var a = Crear();
        a.DarDeBaja("Motivo valido", Usuario, Ahora);

        var act = () => a.DarDeBaja("Otro motivo", Usuario, Ahora);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_YA_DADO_DE_BAJA");
        a.BajaMotivo.Should().Be("Motivo valido");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("abcd")]
    public void Baja_Sin_Motivo_O_Muy_Corto_Es_Error(string? motivo)
    {
        var a = Crear();

        var act = () => a.DarDeBaja(motivo!, Usuario, Ahora);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_MOTIVO_BAJA_INVALIDO");
        a.EstaDeBaja.Should().BeFalse();
    }

    [Fact]
    public void Baja_Motivo_Limites_5_Y_500()
    {
        Crear().Invoking(a => a.DarDeBaja(new string('x', 5), Usuario, Ahora)).Should().NotThrow();
        Crear().Invoking(a => a.DarDeBaja(new string('x', 500), Usuario, Ahora)).Should().NotThrow();
        Crear().Invoking(a => a.DarDeBaja(new string('x', 501), Usuario, Ahora))
            .Should().Throw<BusinessRuleException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc/passwd")]
    [InlineData("a\\b.pdf")]
    public void Nombre_Invalido_O_Con_Ruta_Es_Error(string nombre)
    {
        var act = () => Crear(nombre: nombre);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Tamano_Cero_Es_Error()
    {
        var act = () => Crear(tamano: 0);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_TAMANO_INVALIDO");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // mayúsculas
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // no hex
    public void Hash_Invalido_Es_Error(string hash)
    {
        var act = () => Crear(hash: hash);

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_HASH_INVALIDO");
    }

    [Fact]
    public void AggregateRootId_Es_La_Entidad_Duena()
    {
        var a = Crear();

        a.AggregateRootId.Should().Be(a.EntidadId);
    }

    [Fact]
    public void TipoDocumento_Valida_Vigencia_Y_Nombre()
    {
        var ok = new AdjuntoTipoDocumento(Guid.CreateVersion7(), "proveedor", "contrato", "Contrato", 2, true, null, false);
        ok.Activo.Should().BeTrue();

        var act = () => new AdjuntoTipoDocumento(Guid.CreateVersion7(), "proveedor", "x", "X", 1, true, 0, false);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_TIPO_VIGENCIA_INVALIDA");
    }
}
