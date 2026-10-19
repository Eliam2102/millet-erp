using Millet.CuentasPorPagar.Application.Viaticos.Queries;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Viaticos;

/// <summary>
/// ADR-0051 en viáticos: la sucursal de la solicitud es la del empleado. Fuera de sus
/// sucursales, el usuario solo ve sus propias solicitudes y las de sus subordinados.
/// </summary>
public sealed class ViaticosAlcanceTests
{
    private static readonly Guid SucursalA = Guid.NewGuid();
    private static readonly Guid SucursalB = Guid.NewGuid();

    private sealed class Empleados(Dictionary<Guid, Guid?> sucursalPorEmpleado, Guid? empleadoDelUsuario) : IEmpleadoReadPort
    {
        public Task<EmpleadoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<EmpleadoDto?>(
            sucursalPorEmpleado.TryGetValue(id, out var s) ? new(id, Guid.Empty, "FIX empleado", "fix@millet.test", null, s, true) : null);
        public Task<EmpleadoDto?> ObtenerPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
            empleadoDelUsuario is Guid e ? ObtenerAsync(e, ct) : Task.FromResult<EmpleadoDto?>(null);
    }

    private sealed class Puestos : IPuestoReadPort
    {
        public Task<PuestoDto?> ObtenerAsync(Guid puestoId, CancellationToken ct) => Task.FromResult<PuestoDto?>(null);
        public Task<IReadOnlyList<PuestoDto>> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PuestoDto>>([]);
    }

    private sealed class SinCompras : IComprasSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentoSucursales>>([]);
    }

    private static SolicitudViaticos Solicitud(P8Fixture a, Guid empleado, Guid jefe) => SolicitudViaticos.Solicitar(
        empresaId: a.Current!.Value, empleadoId: empleado, puestoId: Guid.NewGuid(), jefeDirectoId: jefe,
        destino: "FIX destino", tipoDestino: TipoDestinoViatico.Nacional,
        fechaSalida: new DateOnly(2026, 10, 20), fechaRegreso: new DateOnly(2026, 10, 21), moneda: "MXN",
        montoSolicitado: 1000m, topePolitica: 6000m, diasMaxPolitica: 10, justificacionExceso: null, ahora: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Sucursal_de_la_solicitud_es_la_del_empleado()
    {
        await using var a = new P8Fixture();
        var enA = Guid.NewGuid(); var sinSucursal = Guid.NewGuid();
        var s1 = Solicitud(a, enA, Guid.NewGuid()); var s2 = Solicitud(a, sinSucursal, Guid.NewGuid());
        a.Db.AddRange(s1, s2); await a.Db.SaveChangesAsync();

        var adapter = new CxpSucursalReadAdapter(a.Db, new SinCompras(),
            new Empleados(new() { [enA] = SucursalA, [sinSucursal] = null }, null));
        var docs = (await adapter.ListarAsync(ViaticosAlcance.TipoDocumento, default)).ToDictionary(d => d.Id, d => d.Sucursales);

        docs[s1.Id].Should().Equal(SucursalA);
        docs[s2.Id].Should().BeEmpty("sin sucursal solo la ve un usuario corporativo");
    }

    [Fact]
    public async Task Listado_operativo_ve_su_sucursal_sus_solicitudes_y_las_de_sus_subordinados()
    {
        await using var a = new P8Fixture();
        var yo = Guid.NewGuid(); var subordinado = Guid.NewGuid(); var otroA = Guid.NewGuid(); var otroB = Guid.NewGuid();
        var deSucursal = Solicitud(a, otroA, Guid.NewGuid());
        var ajena = Solicitud(a, otroB, Guid.NewGuid());
        var propia = Solicitud(a, yo, Guid.NewGuid());
        var deSubordinado = Solicitud(a, subordinado, yo);
        a.Db.AddRange(deSucursal, ajena, propia, deSubordinado); await a.Db.SaveChangesAsync();
        var empleados = new Empleados(new() { [yo] = SucursalB, [subordinado] = SucursalB, [otroA] = SucursalA, [otroB] = SucursalB }, yo);

        var query = new ListarSolicitudesViaticosQuery { DocumentosPermitidos = [deSucursal.Id] };
        var ids = (await new ListarSolicitudesViaticosHandler(a.Db, empleados, new Puestos(), a).Handle(query, default))
            .Items.Select(i => i.Id).ToList();

        ids.Should().BeEquivalentTo([deSucursal.Id, propia.Id, deSubordinado.Id]);

        var corporativo = new ListarSolicitudesViaticosQuery { DocumentosPermitidos = null };
        (await new ListarSolicitudesViaticosHandler(a.Db, empleados, new Puestos(), a).Handle(corporativo, default))
            .Items.Should().HaveCount(4);
    }

    [Fact]
    public async Task Detalle_ajeno_es_403_y_el_jefe_si_lo_ve()
    {
        await using var a = new P8Fixture();
        var yo = Guid.NewGuid(); var otro = Guid.NewGuid();
        var ajena = Solicitud(a, otro, Guid.NewGuid());
        var deSubordinado = Solicitud(a, otro, yo);
        a.Db.AddRange(ajena, deSubordinado); await a.Db.SaveChangesAsync();
        var handler = new ObtenerSolicitudViaticosHandler(a.Db,
            new Empleados(new() { [yo] = SucursalB, [otro] = SucursalA }, yo), new Puestos(), a);

        var verAjena = () => handler.Handle(new ObtenerSolicitudViaticosQuery(ajena.Id) { DocumentosPermitidos = [] }, default);
        (await verAjena.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("SUCURSAL_NO_ASOCIADA");
        (await handler.Handle(new ObtenerSolicitudViaticosQuery(deSubordinado.Id) { DocumentosPermitidos = [] }, default))
            .Id.Should().Be(deSubordinado.Id);
    }
}
