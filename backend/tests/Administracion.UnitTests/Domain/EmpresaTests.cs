using Millet.Administracion.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="Empresa"/> (F-Admin-PR2.1).
/// Cubren constructor, invariantes, ActualizarDatos y transiciones
/// Activar/Desactivar.
/// </summary>
public class EmpresaTests
{
    private static Empresa Crear(
        string rfc = "MIL010101AB1",
        string razonSocial = "Millet S.A. de C.V.",
        string regimenFiscal = "601",
        string? nombreComercial = null) =>
        new(
            id: Guid.CreateVersion7(),
            rfc: rfc,
            razonSocial: razonSocial,
            regimenFiscal: regimenFiscal,
            nombreComercial: nombreComercial);

    [Fact]
    public void Should_Create_WithDefaultActiva()
    {
        var empresa = Crear();
        empresa.Activa.Should().BeTrue();
    }

    [Theory]
    [InlineData("MIL010101AB1")]           // 12 chars — persona moral
    [InlineData("PEME010101AB1")]          // 13 chars — persona física
    public void Should_Accept_ValidRfc(string rfc)
    {
        var act = () => Crear(rfc: rfc);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("CORTO")]                  // < 12
    [InlineData("MILMILMILMIL1234")]       // > 13
    public void Should_Reject_InvalidRfc(string rfc)
    {
        var act = () => Crear(rfc: rfc);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPRESA_RFC_INVALIDO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Reject_RazonSocial_Empty(string razonSocial)
    {
        var act = () => Crear(razonSocial: razonSocial);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPRESA_RAZON_SOCIAL_INVALIDA");
    }

    [Fact]
    public void Should_Reject_RazonSocial_TooLong()
    {
        var act = () => Crear(razonSocial: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPRESA_RAZON_SOCIAL_INVALIDA");
    }

    [Fact]
    public void Should_Reject_RegimenFiscal_TooLong()
    {
        var act = () => Crear(regimenFiscal: new string('9', 11));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPRESA_REGIMEN_FISCAL_INVALIDO");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var empresa = Crear(razonSocial: "Original SA");
        empresa.ActualizarDatos(razonSocial: "Nueva SA");
        empresa.RazonSocial.Should().Be("Nueva SA");
    }

    [Fact]
    public void ActualizarDatos_With_LimpiarNombreComercial_Should_SetNull()
    {
        var empresa = Crear(nombreComercial: "Millet");
        empresa.ActualizarDatos(limpiarNombreComercial: true);
        empresa.NombreComercial.Should().BeNull();
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var empresa = Crear();

        empresa.Desactivar();
        empresa.Activa.Should().BeFalse();

        empresa.Desactivar();        // idempotent
        empresa.Activa.Should().BeFalse();

        empresa.Activar();
        empresa.Activa.Should().BeTrue();

        empresa.Activar();           // idempotent
        empresa.Activa.Should().BeTrue();
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new Empresa(Guid.Empty, "MIL010101AB1", "Millet SA", "601");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPRESA_ID_INVALIDO");
    }

    [Fact]
    public void ActualizarDatos_Should_Set_And_Clear_TasaIvaDefault()
    {
        var empresa = Crear();

        empresa.ActualizarDatos(tasaIvaDefault: 0.16m);
        empresa.TasaIvaDefault.Should().Be(0.16m);

        empresa.ActualizarDatos(razonSocial: "Otra SA"); // null = no tocar
        empresa.TasaIvaDefault.Should().Be(0.16m);

        empresa.ActualizarDatos(limpiarTasaIvaDefault: true);
        empresa.TasaIvaDefault.Should().BeNull();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ActualizarDatos_Should_Reject_TasaIvaDefault_FueraDeRango(decimal tasa)
    {
        var empresa = Crear();
        var act = () => empresa.ActualizarDatos(tasaIvaDefault: tasa);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPRESA_TASA_IVA_INVALIDA");
    }
}
