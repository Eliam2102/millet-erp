using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Adapters;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Adapters;

/// <summary>
/// Tests de <see cref="PuestoReadPortAdapter"/> y
/// <see cref="EmpleadoReadPortAdapter"/> (ADM-PR2) contra
/// <c>CompartidoDbContext</c> in-memory: mapeo de DTOs, filtro de
/// activos en ListarAsync y nulls para ids inexistentes.
/// </summary>
public class PuestoEmpleadoReadAdaptersTests
{
    private static CompartidoDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"puestos-empleados-{Guid.NewGuid():N}")
            .Options;
        return new CompartidoDbContext(options, new BypassedEmpresaContext());
    }

    [Fact]
    public async Task PuestoAdapter_Obtener_mapea_codigo_y_activo()
    {
        await using var db = NewDb();
        var puesto = new Puesto(Guid.CreateVersion7(), Guid.CreateVersion7(), "GER", "Gerente");
        db.Puestos.Add(puesto);
        await db.SaveChangesAsync();

        var adapter = new PuestoReadPortAdapter(db, new BypassedEmpresaContext());

        var dto = await adapter.ObtenerAsync(puesto.Id, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Codigo.Should().Be("GER");
        dto.Nombre.Should().Be("Gerente");
        dto.Activo.Should().BeTrue();

        (await adapter.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PuestoAdapter_Listar_devuelve_solo_activos_ordenados_por_clave()
    {
        await using var db = NewDb();
        var inactivo = new Puesto(Guid.CreateVersion7(), Guid.CreateVersion7(), "OPER", "Operativo");
        inactivo.Desactivar();
        db.Puestos.AddRange(
            new Puesto(Guid.CreateVersion7(), Guid.CreateVersion7(), "GER", "Gerente"),
            new Puesto(Guid.CreateVersion7(), Guid.CreateVersion7(), "EJEC", "Ejecutivo"),
            inactivo);
        await db.SaveChangesAsync();

        var adapter = new PuestoReadPortAdapter(db, new BypassedEmpresaContext());

        var lista = await adapter.ListarAsync(CancellationToken.None);

        lista.Select(p => p.Codigo).Should().Equal("EJEC", "GER");
        lista.Should().OnlyContain(p => p.Activo);
    }

    [Fact]
    public async Task EmpleadoAdapter_mapea_dto_y_email_null_a_vacio()
    {
        await using var db = NewDb();
        var empresaId = Guid.CreateVersion7();
        var puestoId = Guid.CreateVersion7();
        var empleado = new Empleado(
            Guid.CreateVersion7(), empresaId, "EMP-001", "Juana Pérez",
            email: null, puestoId: puestoId);
        db.Empleados.Add(empleado);
        await db.SaveChangesAsync();

        var adapter = new EmpleadoReadPortAdapter(db, new BypassedEmpresaContext());

        var dto = await adapter.ObtenerAsync(empleado.Id, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.EmpresaId.Should().Be(empresaId);
        dto.Nombre.Should().Be("Juana Pérez");
        dto.Email.Should().Be(string.Empty);
        dto.PuestoId.Should().Be(puestoId);
        dto.Activo.Should().BeTrue();

        (await adapter.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task EmpleadoAdapter_inactivo_mapea_activo_false()
    {
        await using var db = NewDb();
        var empleado = new Empleado(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "EMP-002", "Pedro López",
            email: "pedro@millet.mx");
        empleado.Desactivar();
        db.Empleados.Add(empleado);
        await db.SaveChangesAsync();

        var adapter = new EmpleadoReadPortAdapter(db, new BypassedEmpresaContext());

        var dto = await adapter.ObtenerAsync(empleado.Id, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Activo.Should().BeFalse();
        dto.Email.Should().Be("pedro@millet.mx");
    }

    [Fact]
    public async Task EmpleadoAdapter_ObtenerPorUsuario_resuelve_solo_activos()
    {
        await using var db = NewDb();
        var usuarioId = Guid.CreateVersion7();
        var inactivo = new Empleado(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "EMP-010", "Baja Con Usuario",
            usuarioId: usuarioId);
        inactivo.Desactivar();
        var activo = new Empleado(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "EMP-011", "Jefe Vigente",
            usuarioId: usuarioId);
        db.Empleados.AddRange(inactivo, activo);
        await db.SaveChangesAsync();

        var adapter = new EmpleadoReadPortAdapter(db, new BypassedEmpresaContext());

        var dto = await adapter.ObtenerPorUsuarioAsync(usuarioId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(activo.Id);
        dto.Nombre.Should().Be("Jefe Vigente");

        (await adapter.ObtenerPorUsuarioAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().BeNull();
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
