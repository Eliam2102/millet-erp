using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="SucursalDepartamento"/> (PR-A1).
/// Cubren constructor, invariantes (id/sucursal/depto no vacíos) y
/// transiciones Activar/Desactivar/CambiarEstatus.
/// </summary>
public class SucursalDepartamentoTests
{
    private static readonly Guid EmpresaId = Guid.CreateVersion7();
    private static readonly Guid SucursalId = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly Guid DepartamentoId = Guid.Parse("00000005-0004-0000-0000-000000000001");

    private static SucursalDepartamento Crear(
        Guid? sucursalId = null,
        Guid? departamentoId = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) =>
        new(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaId,
            sucursalId: sucursalId ?? SucursalId,
            departamentoId: departamentoId ?? DepartamentoId,
            estatus: estatus);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var asignacion = Crear();
        asignacion.Estatus.Should().Be(EstatusCatalogo.Activo);
        asignacion.SucursalId.Should().Be(SucursalId);
        asignacion.DepartamentoId.Should().Be(DepartamentoId);
    }

    [Fact]
    public void Should_Create_WithExplicitEstatus()
    {
        var asignacion = Crear(estatus: EstatusCatalogo.Inactivo);
        asignacion.Estatus.Should().Be(EstatusCatalogo.Inactivo);
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new SucursalDepartamento(Guid.Empty, EmpresaId, SucursalId, DepartamentoId);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_DEPARTAMENTO_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyEmpresaId()
    {
        var act = () => new SucursalDepartamento(Guid.CreateVersion7(), Guid.Empty, SucursalId, DepartamentoId);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_DEPARTAMENTO_EMPRESA_INVALIDA");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptySucursal()
    {
        var act = () => Crear(sucursalId: Guid.Empty);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_DEPARTAMENTO_SUCURSAL_INVALIDA");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyDepartamento()
    {
        var act = () => Crear(departamentoId: Guid.Empty);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_DEPARTAMENTO_DEPARTAMENTO_INVALIDO");
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var asignacion = Crear();

        asignacion.Desactivar();
        asignacion.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        asignacion.Desactivar(); // idempotent
        asignacion.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        asignacion.Activar();
        asignacion.Estatus.Should().Be(EstatusCatalogo.Activo);

        asignacion.Activar(); // idempotent
        asignacion.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void CambiarEstatus_Should_AcceptEnRevision()
    {
        var asignacion = Crear();
        asignacion.CambiarEstatus(EstatusCatalogo.EnRevision);
        asignacion.Estatus.Should().Be(EstatusCatalogo.EnRevision);
    }
}
