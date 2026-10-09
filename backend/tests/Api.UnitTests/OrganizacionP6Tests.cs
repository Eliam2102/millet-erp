using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Administracion.Application.SucursalPuestos;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Viaticos.Transiciones;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Infrastructure.Adapters;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

public sealed class OrganizacionP6Tests
{
    [Fact]
    public async Task Departamento_no_asignado_a_sucursal_impide_asignacion_de_puesto_CA1_2()
    {
        var empresa = new EmpresaContext();
        using var db = Compartido(empresa);
        var sucursal = new Sucursal(Guid.NewGuid(), empresa.Current!.Value, "P6", "Sucursal prueba", TipoSucursal.Planta,
            "Calle prueba", "1", "Centro", "Mérida", "Mérida", "Yucatán", "97000", "México");
        var puesto = new Puesto(Guid.NewGuid(), empresa.Current.Value, "P6", "Puesto prueba");
        var departamento = new Departamento(Guid.NewGuid(), empresa.Current.Value, "P6", "Departamento prueba");
        db.AddRange(sucursal, puesto, departamento); await db.SaveChangesAsync();
        var handler = new AsignarPuestoASucursalHandler(db, new Roles());
        var command = new AsignarPuestoASucursalCommand(sucursal.Id, puesto.Id, departamento.Id);
        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("DEPARTAMENTO_NO_ASIGNADO_A_SUCURSAL", error.Code);
        Assert.Empty(db.SucursalPuestos);
        db.SucursalDepartamentos.Add(new SucursalDepartamento(Guid.NewGuid(), empresa.Current.Value, sucursal.Id, departamento.Id));
        await db.SaveChangesAsync();
        await handler.Handle(command, CancellationToken.None);
        Assert.Single(db.SucursalPuestos);
    }
    [Fact]
    public async Task Jefe_sin_usuario_no_firma_CA1_6_y_usuario_vinculado_si_firma()
    {
        var empresa = new EmpresaContext(); var usuario = new Usuario();
        using var catalogos = Compartido(empresa);
        using var cxp = CxpP6TestContext.Crear(empresa);
        var jefe = new Empleado(Guid.NewGuid(), empresa.Current!.Value, "P6", "Jefe sin acceso");
        catalogos.Empleados.Add(jefe); await catalogos.SaveChangesAsync();
        var ahora = DateTimeOffset.UtcNow;
        var solicitud = SolicitudViaticos.Solicitar(empresa.Current.Value, Guid.NewGuid(), Guid.NewGuid(), jefe.Id,
            "Cancún", TipoDestinoViatico.Nacional, DateOnly.FromDateTime(ahora.DateTime), DateOnly.FromDateTime(ahora.DateTime).AddDays(1),
            "MXN", 100m, 500m, 5, null, ahora);
        cxp.SolicitudesViaticos.Add(solicitud); await cxp.SaveChangesAsync();
        var handler = new AutorizarPorJefeViaticosHandler(cxp, usuario, new EmpleadoReadPortAdapter(catalogos, empresa), new Eventos(), new Reloj());
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(solicitud.Id, solicitud.Version), CancellationToken.None));
        Assert.Equal("VIA_USUARIO_SIN_EMPLEADO", error.Code);
        Assert.Equal(EstadoSolicitudViaticos.Solicitada, solicitud.Estado);
        jefe.ActualizarDatos(usuarioId: usuario.UserId);
        await catalogos.SaveChangesAsync();
        await handler.Handle(new(solicitud.Id, solicitud.Version), CancellationToken.None);
        Assert.Equal(EstadoSolicitudViaticos.AutorizadaPorJefe, solicitud.Estado);
    }
    private static CompartidoDbContext Compartido(EmpresaContext empresa) => new(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid(); public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } }
    }
    private sealed class Usuario : ICurrentUserContext
    { public Guid? UserId { get; } = Guid.NewGuid(); public string? UserName => "P6"; }
    private sealed class Eventos : IIntegrationEventPublisher
    { public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Roles : IRolReadPort
    {
        public Task<bool> ExisteActivoAsync(Guid rolId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IEnumerable<Guid> rolIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
