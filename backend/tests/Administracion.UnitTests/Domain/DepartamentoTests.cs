using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="Departamento"/> (F-Admin-PR2.2).
/// Cubren constructor, invariantes, ActualizarDatos y transiciones
/// Activar/Desactivar/EnRevision.
/// </summary>
public class DepartamentoTests
{
    private static readonly Guid EmpresaId = Guid.CreateVersion7();

    private static Departamento Crear(
        string clave = "COMPRAS",
        string nombre = "Compras y Adquisiciones",
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
        var depto = Crear();
        depto.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("CLAVE-DEPARTAMENTAL-MUY-LARGA")]    // > 20
    public void Should_Reject_InvalidClave(string clave)
    {
        var act = () => Crear(clave: clave);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DEPARTAMENTO_CLAVE_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Reject_Nombre_Empty(string nombre)
    {
        var act = () => Crear(nombre: nombre);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DEPARTAMENTO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Should_Reject_Nombre_TooLong()
    {
        var act = () => Crear(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DEPARTAMENTO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new Departamento(Guid.Empty, EmpresaId, "COMPRAS", "Compras");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DEPARTAMENTO_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyEmpresaId()
    {
        var act = () => new Departamento(Guid.CreateVersion7(), Guid.Empty, "COMPRAS", "Compras");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DEPARTAMENTO_EMPRESA_INVALIDA");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var depto = Crear(nombre: "Original");
        depto.ActualizarDatos(nombre: "Nuevo Nombre");
        depto.Nombre.Should().Be("Nuevo Nombre");
    }

    [Fact]
    public void ActualizarDatos_With_NullNombre_Should_NotChange()
    {
        var depto = Crear(nombre: "Original");
        depto.ActualizarDatos(nombre: null);
        depto.Nombre.Should().Be("Original");
    }

    [Fact]
    public void ActualizarDatos_Should_Reject_InvalidNombre()
    {
        var depto = Crear();
        var act = () => depto.ActualizarDatos(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DEPARTAMENTO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var depto = Crear();

        depto.Desactivar();
        depto.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        depto.Desactivar();   // idempotent
        depto.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        depto.Activar();
        depto.Estatus.Should().Be(EstatusCatalogo.Activo);

        depto.Activar();      // idempotent
        depto.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void CambiarEstatus_Should_AcceptEnRevision()
    {
        var depto = Crear();
        depto.CambiarEstatus(EstatusCatalogo.EnRevision);
        depto.Estatus.Should().Be(EstatusCatalogo.EnRevision);
    }
}
