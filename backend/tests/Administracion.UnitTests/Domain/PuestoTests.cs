using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="Puesto"/> (ADM-PR1).
/// Cubren constructor, invariantes, ActualizarDatos y transiciones.
/// </summary>
public class PuestoTests
{
    private static readonly Guid EmpresaId = Guid.CreateVersion7();

    private static Puesto Crear(
        string clave = "GER",
        string nombre = "Gerente",
        EstatusCatalogo estatus = EstatusCatalogo.Activo) =>
        new(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaId,
            clave: clave,
            nombre: nombre,
            estatus: estatus);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var puesto = Crear();
        puesto.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("CLAVE-DE-PUESTO-MUY-LARGA")]    // > 20
    public void Should_Reject_InvalidClave(string clave)
    {
        var act = () => Crear(clave: clave);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "PUESTO_CLAVE_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Reject_Nombre_Empty(string nombre)
    {
        var act = () => Crear(nombre: nombre);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "PUESTO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Should_Reject_Nombre_TooLong()
    {
        var act = () => Crear(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "PUESTO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new Puesto(Guid.Empty, EmpresaId, "GER", "Gerente");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "PUESTO_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyEmpresaId()
    {
        var act = () => new Puesto(Guid.CreateVersion7(), Guid.Empty, "GER", "Gerente");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "PUESTO_EMPRESA_INVALIDA");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var puesto = Crear(nombre: "Original");
        puesto.ActualizarDatos(nombre: "Nuevo Nombre");
        puesto.Nombre.Should().Be("Nuevo Nombre");
    }

    [Fact]
    public void ActualizarDatos_With_NullNombre_Should_NotChange()
    {
        var puesto = Crear(nombre: "Original");
        puesto.ActualizarDatos(nombre: null);
        puesto.Nombre.Should().Be("Original");
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var puesto = Crear();

        puesto.Desactivar();
        puesto.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        puesto.Desactivar();   // idempotent
        puesto.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        puesto.Activar();
        puesto.Estatus.Should().Be(EstatusCatalogo.Activo);

        puesto.Activar();      // idempotent
        puesto.Estatus.Should().Be(EstatusCatalogo.Activo);
    }
}
