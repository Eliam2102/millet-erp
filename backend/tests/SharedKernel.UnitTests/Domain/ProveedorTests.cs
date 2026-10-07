using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.UnitTests.Domain;

public class ProveedorTests
{
    private static Proveedor Crear(
        string clave = "PROV-1",
        string razonSocial = "Empresa SA",
        string rfc = "EMP010101AAA",
        TipoPersonaProveedor tipoPersona = TipoPersonaProveedor.Moral,
        EstatusCatalogo estatus = EstatusCatalogo.EnRevision,
        short? condicionesPagoDias = null) =>
        new(
            id: Guid.CreateVersion7(),
            clave: clave,
            razonSocial: razonSocial,
            rfc: rfc,
            tipoPersona: tipoPersona,
            estatus: estatus,
            condicionesPagoDias: condicionesPagoDias);

    [Fact]
    public void Should_Create_WithDefaultEnRevision()
    {
        var p = new Proveedor(
            id: Guid.CreateVersion7(),
            clave: "PROV-TEST",
            razonSocial: "Proveedor En Revision SA",
            rfc: "PER010101AAA",
            tipoPersona: TipoPersonaProveedor.Moral);

        Assert.Equal(EstatusCatalogo.EnRevision, p.Estatus);
        Assert.Null(p.ValidadoPorId);
        Assert.Null(p.ValidadoEn);
        Assert.Null(p.MotivoRechazo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("THIS-CLAVE-EXCEEDS-20-CHARS")]
    public void Should_Throw_When_ClaveInvalida(string clave)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(clave: clave));
        Assert.Equal("PROVEEDOR_CLAVE_INVALIDA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_RazonSocialVacia()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(razonSocial: ""));
        Assert.Equal("PROVEEDOR_RAZON_SOCIAL_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData("RFC123")]            // demasiado corto
    [InlineData("RFC12345678901234")] // demasiado largo
    public void Should_Throw_When_RfcLongitudInvalida(string rfc)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(rfc: rfc));
        Assert.Equal("PROVEEDOR_RFC_INVALIDO", ex.Code);
    }

    [Theory]
    [InlineData("EMP010101AAA")]   // 12 chars (moral)
    [InlineData("ABCD010101ABC")]  // 13 chars (física)
    public void Should_Accept_RfcLongitudValida(string rfc)
    {
        var p = Crear(rfc: rfc);
        Assert.Equal(rfc, p.Rfc);
    }

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)400)]
    public void Should_Throw_When_CondicionesPagoFueraDeRango(short dias)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(condicionesPagoDias: dias));
        Assert.Equal("PROVEEDOR_CONDICIONES_PAGO_INVALIDAS", ex.Code);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)30)]
    [InlineData((short)365)]
    public void Should_Accept_CondicionesPagoEnRango(short dias)
    {
        var p = Crear(condicionesPagoDias: dias);
        Assert.Equal(dias, p.CondicionesPagoDias);
    }

    [Fact]
    public void Should_Accept_EstatusInactivo()
    {
        var p = Crear(estatus: EstatusCatalogo.Inactivo);
        Assert.Equal(EstatusCatalogo.Inactivo, p.Estatus);
    }

    // --- RFC único (F1-ADM-05): normalización y detección de genéricos ---

    [Fact]
    public void Should_Normalize_Rfc_TrimAndUpperInvariant_OnCreate()
    {
        var p = Crear(rfc: "  emp010101aaa  ");
        Assert.Equal("EMP010101AAA", p.Rfc);
    }

    [Fact]
    public void Should_Normalize_Rfc_TrimAndUpperInvariant_OnActualizarDatos()
    {
        var p = Crear();
        p.ActualizarDatos(rfc: "  abcd010101abc  ");
        Assert.Equal("ABCD010101ABC", p.Rfc);
    }

    [Theory]
    [InlineData("XAXX010101000")]
    [InlineData("XEXX010101000")]
    [InlineData("xaxx010101000")]
    [InlineData("  XEXX010101000  ")]
    public void EsRfcGenerico_Should_Detect_GenericosSat(string rfc)
    {
        Assert.True(Proveedor.EsRfcGenerico(rfc));
    }

    [Fact]
    public void EsRfcGenerico_Should_Return_False_Para_RfcNormal()
    {
        Assert.False(Proveedor.EsRfcGenerico("EMP010101AAA"));
    }

    // --- Validación y Rechazo por CxP (G1.1) ---

    [Fact]
    public void Validar_Should_TransitionToActivo_AndSetAuditoria()
    {
        var p = Crear(estatus: EstatusCatalogo.EnRevision);
        var validadorId = Guid.CreateVersion7();
        var fecha = DateTimeOffset.UtcNow;

        p.Validar(validadorId, fecha);

        Assert.Equal(EstatusCatalogo.Activo, p.Estatus);
        Assert.Equal(validadorId, p.ValidadoPorId);
        Assert.Equal(fecha, p.ValidadoEn);
        Assert.Null(p.MotivoRechazo);
    }

    [Fact]
    public void Validar_Should_Throw_When_NotEnRevision()
    {
        var p = Crear(estatus: EstatusCatalogo.Activo);
        var ex = Assert.Throws<BusinessRuleException>(() => p.Validar(Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        Assert.Equal("PROVEEDOR_NO_EN_REVISION", ex.Code);
    }

    [Theory]
    [InlineData("Expediente completo y validado por CxP")]
    [InlineData("12345")]
    public void Rechazar_Should_TransitionToInactivo_AndSetMotivo(string motivo)
    {
        var p = Crear(estatus: EstatusCatalogo.EnRevision);
        var validadorId = Guid.CreateVersion7();
        var fecha = DateTimeOffset.UtcNow;

        p.Rechazar(validadorId, motivo, fecha);

        Assert.Equal(EstatusCatalogo.Inactivo, p.Estatus);
        Assert.Equal(motivo.Trim(), p.MotivoRechazo);
        Assert.Equal(validadorId, p.ValidadoPorId);
        Assert.Equal(fecha, p.ValidadoEn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")] // Menos de 5 caracteres
    public void Rechazar_Should_Throw_When_MotivoDemasiadoCorto(string motivo)
    {
        var p = Crear(estatus: EstatusCatalogo.EnRevision);
        var ex = Assert.Throws<BusinessRuleException>(() => p.Rechazar(Guid.CreateVersion7(), motivo, DateTimeOffset.UtcNow));
        Assert.Equal("PROVEEDOR_MOTIVO_RECHAZO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Rechazar_Should_Throw_When_MotivoDemasiadoLargo()
    {
        var p = Crear(estatus: EstatusCatalogo.EnRevision);
        var motivoLargo = new string('A', 501);
        var ex = Assert.Throws<BusinessRuleException>(() => p.Rechazar(Guid.CreateVersion7(), motivoLargo, DateTimeOffset.UtcNow));
        Assert.Equal("PROVEEDOR_MOTIVO_RECHAZO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Rechazar_Should_Throw_When_NotEnRevision()
    {
        var p = Crear(estatus: EstatusCatalogo.Inactivo);
        var ex = Assert.Throws<BusinessRuleException>(() => p.Rechazar(Guid.CreateVersion7(), "Motivo de rechazo válido", DateTimeOffset.UtcNow));
        Assert.Equal("PROVEEDOR_NO_EN_REVISION", ex.Code);
    }
}
