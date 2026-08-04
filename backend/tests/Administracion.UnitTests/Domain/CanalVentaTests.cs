using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios de la entidad <see cref="CanalVenta"/> (FAC-ING-PR2).
/// Espejo de <see cref="SucursalTests"/>: constructor, invariantes,
/// ActualizarDatos y transiciones Activar/Desactivar.
/// </summary>
public class CanalVentaTests
{
    private static CanalVenta Crear(
        short id = 1,
        string nombre = "Tienda Cancún",
        EstatusCatalogo estatus = EstatusCatalogo.Activo) =>
        new(id: id, nombre: nombre, estatus: estatus);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var canal = Crear();
        canal.Estatus.Should().Be(EstatusCatalogo.Activo);
        canal.Id.Should().Be((short)1);
        canal.ClaveAw.Should().BeNull();
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)-1)]
    public void Should_Reject_InvalidId(short id)
    {
        var act = () => Crear(id: id);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CANAL_VENTA_ID_INVALIDO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Reject_Nombre_Empty(string nombre)
    {
        var act = () => Crear(nombre: nombre);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CANAL_VENTA_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Should_Reject_Nombre_TooLong()
    {
        var act = () => Crear(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CANAL_VENTA_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Trim_ClaveAw()
    {
        var canal = new CanalVenta(1, "Tienda Cancún", claveAw: " Ventas Cancun ");
        canal.ClaveAw.Should().Be("Ventas Cancun");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_Should_Reject_ClaveAw_Whitespace(string claveAw)
    {
        var act = () => new CanalVenta(1, "Tienda Cancún", claveAw: claveAw);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CANAL_VENTA_CLAVE_AW_INVALIDA");
    }

    [Fact]
    public void Constructor_Should_Reject_ClaveAw_TooLong()
    {
        var act = () => new CanalVenta(1, "Tienda Cancún", claveAw: new string('x', 41));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CANAL_VENTA_CLAVE_AW_INVALIDA");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var canal = Crear(nombre: "Original");
        canal.ActualizarDatos(nombre: "Nuevo");
        canal.Nombre.Should().Be("Nuevo");
    }

    [Fact]
    public void ActualizarDatos_With_NullNombre_Should_NotChange()
    {
        var canal = Crear(nombre: "Original");
        canal.ActualizarDatos(nombre: null);
        canal.Nombre.Should().Be("Original");
    }

    [Fact]
    public void ActualizarDatos_Should_Set_And_Clear_ClaveAw()
    {
        var canal = Crear();
        canal.ClaveAw.Should().BeNull();

        canal.ActualizarDatos(claveAw: "Ventas Cancun");
        canal.ClaveAw.Should().Be("Ventas Cancun");

        // null = no tocar
        canal.ActualizarDatos(nombre: "Otro nombre");
        canal.ClaveAw.Should().Be("Ventas Cancun");

        canal.ActualizarDatos(limpiarClaveAw: true);
        canal.ClaveAw.Should().BeNull();
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var canal = Crear();

        canal.Desactivar();
        canal.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        canal.Desactivar();   // idempotent
        canal.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        canal.Activar();
        canal.Estatus.Should().Be(EstatusCatalogo.Activo);

        canal.Activar();      // idempotent
        canal.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void CambiarEstatus_Should_AcceptEnRevision()
    {
        var canal = Crear();
        canal.CambiarEstatus(EstatusCatalogo.EnRevision);
        canal.Estatus.Should().Be(EstatusCatalogo.EnRevision);
    }
}
