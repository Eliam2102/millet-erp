using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

/// <summary>
/// Tests de invariantes y transiciones de estado de
/// <see cref="UsuarioServicio"/>. Cubren las reglas que el handler de
/// auth y el bootstrap esperan.
/// </summary>
public sealed class UsuarioServicioTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Should_Set_All_Properties_When_Valid()
    {
        var id = Guid.CreateVersion7();
        var appId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();

        var sp = new UsuarioServicio(
            id,
            "Glass Agent - Test",
            appId,
            objectId,
            empresaId,
            Now,
            notes: "test sp");

        sp.Id.Should().Be(id);
        sp.Nombre.Should().Be("Glass Agent - Test");
        sp.EntraAppId.Should().Be(appId);
        sp.EntraObjectId.Should().Be(objectId);
        sp.EmpresaId.Should().Be(empresaId);
        sp.Activo.Should().BeTrue();
        sp.CreatedAtUtc.Should().Be(Now);
        sp.DeactivatedAtUtc.Should().BeNull();
        sp.Notes.Should().Be("test sp");
    }

    [Fact]
    public void Constructor_Should_Throw_When_Nombre_Empty()
    {
        var act = () => new UsuarioServicio(
            Guid.NewGuid(), "", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentException>().WithMessage("*Nombre*");
    }

    [Fact]
    public void Constructor_Should_Throw_When_AppId_Empty()
    {
        var act = () => new UsuarioServicio(
            Guid.NewGuid(), "x", Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentException>().WithMessage("*EntraAppId*");
    }

    [Fact]
    public void Desactivar_Should_Set_Activo_False_And_Stamp_Deactivated_At()
    {
        var sp = new UsuarioServicio(
            Guid.NewGuid(), "x", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var deactivateTime = Now.AddHours(1);

        sp.Desactivar(deactivateTime);

        sp.Activo.Should().BeFalse();
        sp.DeactivatedAtUtc.Should().Be(deactivateTime);
    }

    [Fact]
    public void Desactivar_Should_Be_Idempotent_When_Already_Inactive()
    {
        var sp = new UsuarioServicio(
            Guid.NewGuid(), "x", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        sp.Desactivar(Now.AddHours(1));
        var firstDeactivate = sp.DeactivatedAtUtc;

        sp.Desactivar(Now.AddHours(2));

        sp.DeactivatedAtUtc.Should().Be(firstDeactivate);
    }

    [Fact]
    public void Reactivar_Should_Set_Activo_True_And_Clear_Deactivated_At()
    {
        var sp = new UsuarioServicio(
            Guid.NewGuid(), "x", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        sp.Desactivar(Now.AddHours(1));

        sp.Reactivar();

        sp.Activo.Should().BeTrue();
        sp.DeactivatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void ActualizarMetadata_Should_Mutate_Nombre_ObjectId_Notes()
    {
        var sp = new UsuarioServicio(
            Guid.NewGuid(), "old", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        var newObjectId = Guid.NewGuid();

        sp.ActualizarMetadata("new name", newObjectId, "new notes");

        sp.Nombre.Should().Be("new name");
        sp.EntraObjectId.Should().Be(newObjectId);
        sp.Notes.Should().Be("new notes");
    }
}
