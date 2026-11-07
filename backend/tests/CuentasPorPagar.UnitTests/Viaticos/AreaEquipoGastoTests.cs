using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Viaticos.CapturarComprobacion;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Viaticos;

/// <summary>
/// H1 paso 2 (86e3z5w25): el área o equipo capturado en un gasto se valida contra la sucursal con la que se contabiliza (la del
/// empleado en viáticos) y queda en la línea para viajar en <c>gasto.registrado.v1</c>. Datos FIX.
/// </summary>
public sealed class AreaEquipoGastoTests
{
    private static readonly Guid SucursalEmpleado = Guid.NewGuid(), AreaValida = Guid.NewGuid(), AreaAjena = Guid.NewGuid();

    private sealed class Empleados(Guid empleadoId) : IEmpleadoReadPort
    {
        public Task<EmpleadoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<EmpleadoDto?>(
            id == empleadoId ? new EmpleadoDto(id, Guid.Empty, "FIX", "fix@ejemplo.test", null, SucursalEmpleado, true) : null);
        public Task<EmpleadoDto?> ObtenerPorUsuarioAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<EmpleadoDto?>(null);
    }

    /// <summary>Solo el área válida es de la sucursal del empleado; registra la sucursal con la que se validó.</summary>
    private sealed class Dimensiones : IDimensionesGastoPort
    {
        public Guid? SucursalValidada { get; private set; }
        public Task<ExigenciaDimensionesGasto> ExigenciasAsync(Guid e, Guid s, DateOnly f, CancellationToken ct) => Task.FromResult(new ExigenciaDimensionesGasto(true, false, "601.01"));
        public Task<IReadOnlyList<CentroGastoOpcion>> CentrosAsync(Guid e, Guid s, int n, Guid? d, string? q, int l, CancellationToken ct) => Task.FromResult<IReadOnlyList<CentroGastoOpcion>>([]);
        public Task<string?> ValidarCentroAsync(Guid e, Guid s, Guid c, CancellationToken ct)
        {
            SucursalValidada = s;
            return Task.FromResult(c == AreaValida && s == SucursalEmpleado ? null : "El centro FIX no es de la sucursal del gasto.");
        }
    }

    private static CapturarLineaViaticosInput Linea(Guid? centro) => new(
        null, Guid.NewGuid().ToString(), Guid.NewGuid(), "F-1", DateTimeOffset.UtcNow, 100m, 16m, 0m, 116m, "MXN", "Hospedaje", false,
        CentroCostoId: centro);

    private static async Task<SolicitudViaticos> SolicitudAnticipadaAsync(P8Fixture a)
    {
        var s = SolicitudViaticos.Solicitar(a.Current!.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "FIX", TipoDestinoViatico.Nacional,
            new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), "MXN", 1000m, 6000m, 10, null, DateTimeOffset.UtcNow);
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        a.Db.Add(s); await a.Db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task H1_el_area_de_la_sucursal_del_empleado_queda_en_la_linea_del_viatico()
    {
        await using var a = new P8Fixture();
        var s = await SolicitudAnticipadaAsync(a);
        var dims = new Dimensiones();

        await new CapturarComprobacionViaticosHandler(a.Db, a, new Empleados(s.EmpleadoId), dims).Handle(
            new CapturarComprobacionViaticosCommand(s.Id, s.Version, [Linea(AreaValida), Linea(null)]), default);

        dims.SucursalValidada.Should().Be(SucursalEmpleado);
        s.Lineas.Select(l => l.CentroCostoId).Should().BeEquivalentTo(new Guid?[] { AreaValida, null });
    }

    [Fact]
    public async Task H1_un_area_que_no_es_de_la_sucursal_del_empleado_se_rechaza()
    {
        await using var a = new P8Fixture();
        var s = await SolicitudAnticipadaAsync(a);

        var act = () => new CapturarComprobacionViaticosHandler(a.Db, a, new Empleados(s.EmpleadoId), new Dimensiones()).Handle(
            new CapturarComprobacionViaticosCommand(s.Id, s.Version, [Linea(AreaAjena)]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(CentroCostoGasto.CodigoInvalido);
    }

    [Fact]
    public async Task H1_sin_sucursal_o_sin_puerto_el_centro_se_conserva_y_lo_revisa_la_poliza()
    {
        var centro = Guid.NewGuid();
        (await CentroCostoGasto.ValidarAsync(new Dimensiones(), Guid.NewGuid(), null, centro, default)).Should().Be(centro);
        (await CentroCostoGasto.ValidarAsync(null, Guid.NewGuid(), SucursalEmpleado, centro, default)).Should().Be(centro);
        (await CentroCostoGasto.ValidarAsync(new Dimensiones(), Guid.NewGuid(), SucursalEmpleado, Guid.Empty, default)).Should().BeNull();
    }
}
