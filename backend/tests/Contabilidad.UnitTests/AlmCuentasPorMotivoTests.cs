using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// Cuenta por motivo de los movimientos manuales de la app de Almacenes (Fernando Alejos, 10-nov-2026): el motivoClave llega en el evento,
/// el normalizador elige el campo y la plantilla sembrada (20261110120000_AlmCuentasPorMotivo) da rol y lado. Datos FIX.
/// </summary>
public class AlmCuentasPorMotivoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Articulo = Guid.NewGuid();

    private static EventoContable Salida(string causa, string? motivoClave) => N.Normalizar(N.SalidaInventarioRegistrada, Guid.NewGuid(),
        JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, MovimientoId = Guid.NewGuid(), FolioMovimiento = "BPD-2026-000009",
            FechaMovimiento = new DateOnly(2026, 11, 10), Causa = causa, SucursalId = Sucursal, MotivoClave = motivoClave,
            Lineas = new object[] { new { ArticuloId = Articulo, MontoMxn = 500m } },
        }));

    private static EventoContable Entrada(string? motivoClave) => N.Normalizar(N.EntradaInventarioRegistrada, Guid.NewGuid(),
        JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, MovimientoId = Guid.NewGuid(), FolioMovimiento = "AJP-2026-000009",
            FechaMovimiento = new DateOnly(2026, 11, 10), MotivoClave = motivoClave, SucursalId = Sucursal,
            Lineas = new object[] { new { ArticuloId = Articulo, MontoMxn = 300m }, new { ArticuloId = Guid.NewGuid(), MontoMxn = 200m } },
        }));

    /// <summary>Partidas que armaría el motor con la plantilla sembrada: (rol, lado) de cada línea con importe distinto de cero, y su suma.</summary>
    private static List<(string Rol, LadoPartida Lado, decimal Importe)> Partidas(string tipoEvento, int version, EventoContable e) =>
        [.. Plantilla(tipoEvento, version).OrderBy(l => l.Orden)
            .SelectMany(l => GenerarPolizaDesdeEventoHandler.Importes(l, e).Where(x => x.Importe != 0m).Select(x => (l.Rol, l.Lado, x.Importe)))];

    private static void Cuadra(List<(string Rol, LadoPartida Lado, decimal Importe)> p) =>
        p.Where(x => x.Lado == LadoPartida.Cargo).Sum(x => x.Importe).Should().Be(p.Where(x => x.Lado == LadoPartida.Abono).Sum(x => x.Importe));

    [Theory]
    [InlineData("salida_manual", "FALTANTE", "AJUSTE_INVENTARIO")]
    [InlineData("salida_manual", "MUESTRA", "PRODUCCION_EN_PROCESO")]
    [InlineData("salida_manual", "PRESTAMO", "INVENTARIO_EN_PRESTAMO")]
    [InlineData("salida_manual", "CONSUMO_INTERNO", "CONSUMO")]
    [InlineData("salida_manual", "faltante", "AJUSTE_INVENTARIO")]
    // Sin clave o con otra (CANCELA_ENTRADA, desconocida): la cuenta por causa, como antes.
    [InlineData("salida_manual", null, "AJUSTE_INVENTARIO")]
    [InlineData("salida_manual", "CANCELA_ENTRADA", "AJUSTE_INVENTARIO")]
    [InlineData("salida_manual", "OTRA", "AJUSTE_INVENTARIO")]
    [InlineData("consumo", null, "CONSUMO")]
    [InlineData("rotura", null, "MERMA")]
    [InlineData("rotura", "OTRA", "MERMA")]
    public void La_salida_carga_el_rol_de_su_motivo_contra_inventario(string causa, string? clave, string rolCargo)
    {
        var p = Partidas(N.SalidaInventarioRegistrada, 3, Salida(causa, clave));

        p.Should().Equal((rolCargo, LadoPartida.Cargo, 500m), ("INVENTARIO", LadoPartida.Abono, 500m));
        Cuadra(p);
    }

    [Theory]
    [InlineData("SOBRANTE", "MERMA")]
    [InlineData("SIN_COMPRA", "MERMA")]
    [InlineData("REGRESO_PRESTAMO", "INVENTARIO_EN_PRESTAMO")]
    [InlineData("DEVOLUCION_VENTA", "COSTO_DE_VENTAS")]
    public void La_entrada_abona_la_contrapartida_de_su_motivo(string clave, string rolAbono)
    {
        var p = Partidas(N.EntradaInventarioRegistrada, 1, Entrada(clave));
        var inventario = clave == "DEVOLUCION_VENTA" ? "INVENTARIO_PT" : "INVENTARIO";

        p.Should().Equal((inventario, LadoPartida.Cargo, 300m), (inventario, LadoPartida.Cargo, 200m), (rolAbono, LadoPartida.Abono, 500m));
        Cuadra(p);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CARGA_INICIAL")]
    public void La_entrada_con_clave_sin_poliza_no_arma_partidas(string? clave) =>
        Partidas(N.EntradaInventarioRegistrada, 1, Entrada(clave)).Should().BeEmpty();

    [Fact]
    public void La_cuenta_de_merma_y_la_de_produccion_en_proceso_son_las_de_Fernando()
    {
        Migracion("20261107170000_CuentasOficialesYRolesContabilidad").Should()
            .Contain("('MERMA',                           '504.25.02.00'").And.Contain("('AJUSTE_INVENTARIO',               '504.25.02.00'");
        Migracion("20261110120000_AlmCuentasPorMotivo").Should().Contain("('PRODUCCION_EN_PROCESO', '115.03.00.00'");
    }

    [Fact]
    public void El_oyente_de_almacen_contabiliza_la_entrada_manual() =>
        new Millet.Contabilidad.Infrastructure.Workers.AlmacenContabilidadListenerWorker(null, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Contabilidad.Infrastructure.Workers.AlmacenContabilidadListenerWorker>.Instance)
            .TiposContables.Should().Contain(N.EntradaInventarioRegistrada);

    private static string Migracion(string nombre)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        return File.ReadAllText(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations", nombre + ".cs"));
    }

    private static List<LineaPlantillaContable> Plantilla(string tipoEvento, int version)
    {
        var m = Regex.Match(Migracion("20261110120000_AlmCuentasPorMotivo"),
            $@"\('{Regex.Escape(tipoEvento)}', {version}, '[A-Z]+', '[^']*', '[^']*',\s*'(?<lineas>\[[^']*\])'\)");
        m.Success.Should().BeTrue();
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(m.Groups["lineas"].Value, Opciones)!;
    }

    private static readonly JsonSerializerOptions Opciones = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
}
