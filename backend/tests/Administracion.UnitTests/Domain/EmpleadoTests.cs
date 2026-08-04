using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="Empleado"/> (ADM-PR1).
/// Cubren constructor, invariantes (clave, nombre, email, código de
/// nómina, jefe ≠ self), ActualizarDatos con flags limpiar* y
/// transiciones Activar/Desactivar.
/// </summary>
public class EmpleadoTests
{
    private static readonly Guid EmpresaId = Guid.CreateVersion7();

    private static Empleado Crear(
        Guid? id = null,
        string clave = "EMP-001",
        string nombre = "Juana Pérez",
        string? email = "juana.perez@millet.mx",
        Guid? puestoId = null,
        Guid? jefeDirectoId = null,
        string? codigoNomina = null) =>
        new(
            id: id ?? Guid.CreateVersion7(),
            empresaId: EmpresaId,
            clave: clave,
            nombre: nombre,
            email: email,
            puestoId: puestoId,
            jefeDirectoId: jefeDirectoId,
            codigoNomina: codigoNomina);

    [Fact]
    public void Should_Create_WithDefaultActivo_AndTrimmedFields()
    {
        var empleado = Crear(email: "  juana@millet.mx  ", codigoNomina: " A1234 ");
        empleado.Estatus.Should().Be(EstatusCatalogo.Activo);
        empleado.Email.Should().Be("juana@millet.mx");
        empleado.CodigoNomina.Should().Be("A1234");
        empleado.EmpresaId.Should().Be(EmpresaId);
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new Empleado(Guid.Empty, EmpresaId, "EMP-001", "Juana");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyEmpresa()
    {
        var act = () => new Empleado(Guid.CreateVersion7(), Guid.Empty, "EMP-001", "Juana");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_EMPRESA_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("CLAVE-DE-EMPLEADO-MUY-LARGA")]    // > 20
    public void Should_Reject_InvalidClave(string clave)
    {
        var act = () => Crear(clave: clave);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_CLAVE_INVALIDA");
    }

    [Fact]
    public void Should_Reject_Nombre_TooLong()
    {
        var act = () => Crear(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Should_Reject_Email_TooLong()
    {
        var act = () => Crear(email: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_EMAIL_INVALIDO");
    }

    [Fact]
    public void Should_Reject_CodigoNomina_TooLong()
    {
        var act = () => Crear(codigoNomina: new string('x', 21));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_CODIGO_NOMINA_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_SelfAsJefe()
    {
        var id = Guid.CreateVersion7();
        var act = () => Crear(id: id, jefeDirectoId: id);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_JEFE_INVALIDO");
    }

    [Fact]
    public void ActualizarDatos_Should_Reject_SelfAsJefe()
    {
        var empleado = Crear();
        var act = () => empleado.ActualizarDatos(jefeDirectoId: empleado.Id);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EMPLEADO_JEFE_INVALIDO");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var puestoId = Guid.CreateVersion7();
        var empleado = Crear();

        empleado.ActualizarDatos(nombre: "Nuevo Nombre", puestoId: puestoId);

        empleado.Nombre.Should().Be("Nuevo Nombre");
        empleado.PuestoId.Should().Be(puestoId);
        empleado.Email.Should().Be("juana.perez@millet.mx");  // sin tocar
    }

    [Fact]
    public void ActualizarDatos_LimpiarFlags_Should_NullTheField()
    {
        var empleado = Crear(
            puestoId: Guid.CreateVersion7(),
            jefeDirectoId: Guid.CreateVersion7(),
            codigoNomina: "A1234");

        empleado.ActualizarDatos(
            limpiarEmail: true,
            limpiarPuesto: true,
            limpiarJefeDirecto: true,
            limpiarCodigoNomina: true);

        empleado.Email.Should().BeNull();
        empleado.PuestoId.Should().BeNull();
        empleado.JefeDirectoId.Should().BeNull();
        empleado.CodigoNomina.Should().BeNull();
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var empleado = Crear();

        empleado.Desactivar();
        empleado.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        empleado.Desactivar();   // idempotent
        empleado.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        empleado.Activar();
        empleado.Estatus.Should().Be(EstatusCatalogo.Activo);
    }
}
