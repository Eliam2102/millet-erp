using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="UsuarioSucursal"/> (F1-ADM-01
/// Fase 1). Cubren constructor, invariantes (id/usuario/sucursal/empresa
/// no vacíos) y transiciones Activar/Desactivar.
/// </summary>
public sealed class UsuarioSucursalTests
{
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid SucursalId = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly Guid EmpresaId = Guid.CreateVersion7();

    private static UsuarioSucursal Crear(
        Guid? usuarioId = null,
        Guid? sucursalId = null,
        Guid? empresaId = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) =>
        new(
            id: Guid.CreateVersion7(),
            usuarioId: usuarioId ?? UsuarioId,
            sucursalId: sucursalId ?? SucursalId,
            empresaId: empresaId ?? EmpresaId,
            estatus: estatus);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var asignacion = Crear();
        asignacion.Estatus.Should().Be(EstatusCatalogo.Activo);
        asignacion.UsuarioId.Should().Be(UsuarioId);
        asignacion.SucursalId.Should().Be(SucursalId);
        asignacion.EmpresaId.Should().Be(EmpresaId);
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
        var act = () => new UsuarioSucursal(Guid.Empty, UsuarioId, SucursalId, EmpresaId);
        act.Should().Throw<ArgumentException>().WithMessage("*id*");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyUsuarioId()
    {
        var act = () => Crear(usuarioId: Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*UsuarioId*");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptySucursalId()
    {
        var act = () => Crear(sucursalId: Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*SucursalId*");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyEmpresaId()
    {
        var act = () => Crear(empresaId: Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*EmpresaId*");
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
}
