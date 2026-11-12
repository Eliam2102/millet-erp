using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Ports;
using Millet.Api.Endpoints.Almacen;
using Millet.Contabilidad.Application.PublicPorts;

namespace Millet.Api.UnitTests.Almacen;

/// <summary>
/// Jorge (TI), 11-nov: ROTURA y SALIDA_MANUAL son del área de Almacén; el centro sale de sucursal + almacén + motivo (CentroAlmacen).
/// Se prueba con la SALIDA_MANUAL real de AlmacenRQ_MP_ENSAYO (9-nov), que no trae área ni equipo.
/// </summary>
public sealed class CentroAlmacenAppTests
{
    private static readonly JsonElement SalidaManual = File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory, "Almacen", "Muestras", "alm_mensaje_ensayo_2026-11-09.jsonl"))
        .Where(l => l.Trim().Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone())
        .First(m => m.GetProperty("tipo").GetString() == "SALIDA_MANUAL").GetProperty("datos");

    [Fact]
    public async Task SALIDA_MANUAL_sin_area_toma_el_centro_de_Almacen_de_la_sucursal()
    {
        var dims = new Dimensiones();
        var (r, error) = await AlmacenesAppSalida.ManejarAsync("SALIDA_MANUAL", SalidaManual, new Resolver(), new UsuarioServicio(), default,
            dims, centrosAlmacen: new Dictionary<string, string> { ["CONKAL"] = "23AL00/ALGRA01" });

        Assert.Null(error);
        Assert.Equal(("23AL00", "ALGRA01"), (dims.Ultima!.AreaClave, dims.Ultima.EquipoClave));
        Assert.All(r!.Salidas, s => Assert.Equal(Dimensiones.Centro, s.CentroCostoId));
    }

    [Fact]
    public async Task Si_el_centro_configurado_no_resuelve_la_salida_sigue_sin_centro_y_no_se_frena()
    {
        // Sin la ubicación de la sucursal (contabilidad.ubicaciones_sucursal) el área no resuelve: antes de esta regla la salida
        // pasaba sin centro y su póliza iba a Rechazos; la configuración del ERP no debe convertir eso en ERROR para la app.
        var dims = new Dimensiones { Error = "El área 23AL00 no existe, está dada de baja o no es de la sucursal del movimiento." };
        var (r, error) = await AlmacenesAppSalida.ManejarAsync("SALIDA_MANUAL", SalidaManual, new Resolver(), new UsuarioServicio(), default,
            dims, centrosAlmacen: new Dictionary<string, string> { ["CONKAL"] = "23AL00/ALGRA01" });

        Assert.Null(error);
        Assert.All(r!.Salidas, s => Assert.Null(s.CentroCostoId));
    }

    [Fact]
    public void La_llave_mas_especifica_gana_y_sin_coincidencia_no_hay_centro()
    {
        var mapa = new Dictionary<string, string>
        {
            ["CONKAL"] = "23AL00/ALGRA01",
            ["conkal|exportacion|faltante"] = "23AL00",
        };
        Assert.Equal(("23AL00", (string?)null), AlmacenesAppSalida.CentroAlmacen(mapa, "CONKAL", "EXPORTACION", "FALTANTE"));
        Assert.Equal(("23AL00", "ALGRA01"), AlmacenesAppSalida.CentroAlmacen(mapa, "CONKAL", "NACIONAL", "FALTANTE"));
        Assert.Null(AlmacenesAppSalida.CentroAlmacen(mapa, "CANCUN", "NACIONAL", null));
        Assert.Null(AlmacenesAppSalida.CentroAlmacen(null, "CONKAL", null, null));
    }

    private sealed class Dimensiones : IDimensionesSalidaInventarioPort
    {
        public static readonly Guid Centro = Guid.NewGuid();
        public SolicitudDimensionesSalida? Ultima { get; private set; }
        public string? Error { get; init; }

        public Task<DimensionesSalidaResultado> ResolverAsync(SolicitudDimensionesSalida solicitud, CancellationToken ct)
        {
            Ultima = solicitud;
            return Task.FromResult(Error is null
                ? new DimensionesSalidaResultado(null, null, Centro, true, false, "504.25.02.00")
                : new DimensionesSalidaResultado(Error, null, null, false, false, null));
        }
    }

    private sealed class Resolver : IAppSalidaResolverPort
    {
        public Task<ResolverSalidaResultado> ResolverAsync(string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct) =>
            Task.FromResult(new ResolverSalidaResultado(null, Guid.NewGuid(), grupos.Select(g => g.AnchoMm is null
                ? new GrupoSalidaResuelto(Guid.NewGuid(), "PZA", null, null, null)
                : new GrupoSalidaResuelto(Guid.NewGuid(), "M2", Guid.NewGuid(), g.AnchoMm, g.AltoMm, AreaHoja.M2(g.AnchoMm.Value, g.AltoMm!.Value))).ToList()));
    }

    private sealed class UsuarioServicio : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UsuarioServicioLectura?>(new(Guid.NewGuid(), Guid.NewGuid(), true));
    }
}
