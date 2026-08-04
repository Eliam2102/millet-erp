using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="Sucursal"/> (F-Admin-PR2.1).
/// Cubren constructor, invariantes, ActualizarDatos y transiciones
/// Activar/Desactivar.
/// </summary>
public class SucursalTests
{
    private static Sucursal Crear(
        string clave = "CDMX",
        string nombre = "Ciudad de México",
        EstatusCatalogo estatus = EstatusCatalogo.Activo) =>
        new(
            id: Guid.CreateVersion7(),
            clave: clave,
            nombre: nombre,
            estatus: estatus);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var sucursal = Crear();
        sucursal.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("CLAVE-MUY-LARGA-QUE-EXCEDE-LIMITE")]    // > 20
    public void Should_Reject_InvalidClave(string clave)
    {
        var act = () => Crear(clave: clave);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_CLAVE_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Reject_Nombre_Empty(string nombre)
    {
        var act = () => Crear(nombre: nombre);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Should_Reject_Nombre_TooLong()
    {
        var act = () => Crear(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new Sucursal(Guid.Empty, "CDMX", "Ciudad de México");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_ID_INVALIDO");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var sucursal = Crear(nombre: "Original");
        sucursal.ActualizarDatos(nombre: "Nueva");
        sucursal.Nombre.Should().Be("Nueva");
    }

    [Fact]
    public void ActualizarDatos_With_NullNombre_Should_NotChange()
    {
        var sucursal = Crear(nombre: "Original");
        sucursal.ActualizarDatos(nombre: null);
        sucursal.Nombre.Should().Be("Original");
    }

    [Fact]
    public void Constructor_Should_Trim_ClaveAw()
    {
        var sucursal = new Sucursal(
            Guid.CreateVersion7(), "CON", "CONKAL", claveAw: " CONKAL ");
        sucursal.ClaveAw.Should().Be("CONKAL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_Should_Reject_ClaveAw_Whitespace(string claveAw)
    {
        var act = () => new Sucursal(
            Guid.CreateVersion7(), "CON", "CONKAL", claveAw: claveAw);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_CLAVE_AW_INVALIDA");
    }

    [Fact]
    public void Constructor_Should_Reject_ClaveAw_TooLong()
    {
        var act = () => new Sucursal(
            Guid.CreateVersion7(), "CON", "CONKAL", claveAw: new string('x', 41));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_CLAVE_AW_INVALIDA");
    }

    [Fact]
    public void ActualizarDatos_Should_Set_And_Clear_ClaveAw()
    {
        var sucursal = Crear();
        sucursal.ClaveAw.Should().BeNull();

        sucursal.ActualizarDatos(claveAw: "CONKAL");
        sucursal.ClaveAw.Should().Be("CONKAL");

        // null = no tocar
        sucursal.ActualizarDatos(nombre: "Otro nombre");
        sucursal.ClaveAw.Should().Be("CONKAL");

        sucursal.ActualizarDatos(limpiarClaveAw: true);
        sucursal.ClaveAw.Should().BeNull();
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var sucursal = Crear();

        sucursal.Desactivar();
        sucursal.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        sucursal.Desactivar();   // idempotent
        sucursal.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        sucursal.Activar();
        sucursal.Estatus.Should().Be(EstatusCatalogo.Activo);

        sucursal.Activar();      // idempotent
        sucursal.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void CambiarEstatus_Should_AcceptEnRevision()
    {
        var sucursal = Crear();
        sucursal.CambiarEstatus(EstatusCatalogo.EnRevision);
        sucursal.Estatus.Should().Be(EstatusCatalogo.EnRevision);
    }
}
