using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.UnitTests.Domain;

/// <summary>
/// Tests unitarios de la entidad <see cref="RolGrupoEntraId"/> y la
/// factory <see cref="Rol.AsociarGrupoEntraId"/> (F-Admin-PR3.1).
/// </summary>
public class RolGrupoEntraIdTests
{
    private static Rol CrearRol() =>
        new(Guid.CreateVersion7(), "test-rol", "Test Rol");

    [Fact]
    public void Constructor_Should_Set_Properties()
    {
        var rolId = Guid.NewGuid();
        var rge = new RolGrupoEntraId(
            id: Guid.CreateVersion7(),
            rolId: rolId,
            objectId: "11111111-1111-1111-1111-111111111111",
            nombre: "Millet_Compras_Jefes");

        rge.RolId.Should().Be(rolId);
        rge.ObjectId.Should().Be("11111111-1111-1111-1111-111111111111");
        rge.Nombre.Should().Be("Millet_Compras_Jefes");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => new RolGrupoEntraId(Guid.Empty, Guid.NewGuid(), "obj", "n");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyRolId()
    {
        var act = () => new RolGrupoEntraId(Guid.CreateVersion7(), Guid.Empty, "obj", "n");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_ROL_INVALIDO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_Should_Reject_EmptyObjectId(string objectId)
    {
        var act = () => new RolGrupoEntraId(Guid.CreateVersion7(), Guid.NewGuid(), objectId, "n");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_OBJECT_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_ObjectId_TooLong()
    {
        var act = () => new RolGrupoEntraId(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            new string('x', 101),
            "n");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_OBJECT_ID_INVALIDO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_Should_Reject_EmptyNombre(string nombre)
    {
        var act = () => new RolGrupoEntraId(Guid.CreateVersion7(), Guid.NewGuid(), "obj", nombre);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_Nombre_TooLong()
    {
        var act = () => new RolGrupoEntraId(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            "obj",
            new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_NOMBRE_INVALIDO");
    }

    [Fact]
    public void ActualizarNombre_Should_UpdateOnlyName()
    {
        var rge = new RolGrupoEntraId(Guid.CreateVersion7(), Guid.NewGuid(), "obj", "Original");
        rge.ActualizarNombre("Renombrado");
        rge.Nombre.Should().Be("Renombrado");
        rge.ObjectId.Should().Be("obj");
    }

    [Fact]
    public void ActualizarNombre_Should_RejectInvalid()
    {
        var rge = new RolGrupoEntraId(Guid.CreateVersion7(), Guid.NewGuid(), "obj", "Original");
        var act = () => rge.ActualizarNombre("");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "ROL_GRUPO_ENTRAID_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Rol_AsociarGrupoEntraId_Should_CreateEntity_WithRolId()
    {
        var rol = CrearRol();
        var rge = rol.AsociarGrupoEntraId(
            objectId: "22222222-2222-2222-2222-222222222222",
            nombre: "Millet_Administradores");

        rge.RolId.Should().Be(rol.Id);
        rge.ObjectId.Should().Be("22222222-2222-2222-2222-222222222222");
        rge.Nombre.Should().Be("Millet_Administradores");
        rge.Id.Should().NotBe(Guid.Empty);
    }
}
