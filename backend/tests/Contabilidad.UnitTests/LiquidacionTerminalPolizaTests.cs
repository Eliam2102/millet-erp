using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// Póliza de la liquidación de la terminal de tarjetas (<c>facturacion.terminal.liquidacion-registrada.v1</c>): comisión TPV a 601.75
/// (rol COMISIONES_TPV), distinta de las demás comisiones bancarias (701.10, COMISIONES_BANCARIAS). Datos FICTICIOS.
/// </summary>
public class LiquidacionTerminalPolizaTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Liquidacion = Guid.NewGuid(), SucA = Guid.NewGuid(), SucB = Guid.NewGuid();

    private static string Evento(Guid[]? sucursales = null) => JsonSerializer.Serialize(new
    {
        EventType = N.TerminalLiquidacionRegistrada, EmpresaId = Empresa, OcurridoEn = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero),
        LiquidacionId = Liquidacion, FolioLiquidacion = "LIQ-0001", Fecha = new DateOnly(2026, 11, 4),
        Bruto = 10_000m, Comision = 250m, IvaComision = 40m, Neto = 9_710m,
        Ventas = (sucursales ?? [SucA]).Select((s, i) => new { CobroMostradorId = Guid.NewGuid(), SucursalId = s, Importe = 5_000m + (i == 0 ? 1m : 0m), Autorizacion = "A" }).ToArray(),
    });

    private static EventoContable Normalizar(string payload) => N.Normalizar(N.TerminalLiquidacionRegistrada, Guid.NewGuid(), payload);

    [Fact]
    public void Se_normaliza_con_bruto_comision_iva_y_neto_en_la_fecha_de_liquidacion_del_banco()
    {
        var e = Normalizar(Evento());

        e.EmpresaId.Should().Be(Empresa);
        e.FechaContable.Should().Be(new DateOnly(2026, 11, 4));
        e.SucursalId.Should().Be(SucA);
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "facturacion" && d.Tipo == "liquidacion_terminal"
            && d.Id == Liquidacion && d.Folio == "LIQ-0001");
        e.Importes["Bruto"].Should().Be(10_000m);
        e.Importes["Comision"].Should().Be(250m);
        e.Importes["IvaComision"].Should().Be(40m);
        e.Importes["Neto"].Should().Be(9_710m);
        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public void Con_ventas_de_varias_sucursales_la_poliza_lleva_la_de_mayor_importe()
    {
        Normalizar(Evento([SucB, SucA])).SucursalId.Should().Be(SucB);
    }

    [Fact]
    public void Un_evento_sin_liquidacion_es_ilegible()
    {
        var payload = Evento().Replace(Liquidacion.ToString(), Guid.Empty.ToString());
        var act = () => Normalizar(payload);
        act.Should().Throw<EventoIlegibleException>();
    }

    [Fact]
    public void La_plantilla_sembrada_cuadra_y_manda_la_comision_a_COMISIONES_TPV_no_a_COMISIONES_BANCARIAS()
    {
        var e = Normalizar(Evento());
        var plantilla = PlantillaSembrada();
        var partidas = plantilla.OrderBy(l => l.Orden).Select(l => (l.Rol, Cargo: l.Lado == LadoPartida.Cargo ? e.Importes[l.Campo]!.Value : 0m,
            Abono: l.Lado == LadoPartida.Abono ? e.Importes[l.Campo]!.Value : 0m)).ToList();

        partidas.Sum(p => p.Cargo).Should().Be(partidas.Sum(p => p.Abono)).And.Be(10_000m);
        partidas.Should().ContainSingle(p => p.Rol == "COMISIONES_TPV").Which.Cargo.Should().Be(250m);
        partidas.Should().ContainSingle(p => p.Rol == "COBROS_TC_TD").Which.Abono.Should().Be(10_000m);
        partidas.Should().NotContain(p => p.Rol == "COMISIONES_BANCARIAS");
    }

    [Fact]
    public void La_migracion_siembra_el_rol_COMISIONES_TPV_sin_cuenta_con_la_sugerida_601_75()
    {
        var m = Migracion();
        m.Should().Contain("'COMISIONES_TPV'").And.Contain("601.75.00.00").And.Contain("701.10");
        m.Should().Contain("NULL, NULL, NULL, DATE '2000-01-01'", "el rol nace sin cuenta: Contabilidad lo mapea");
    }

    [Fact]
    public void El_oyente_de_Facturacion_contabiliza_la_liquidacion()
    {
        var w = new Millet.Contabilidad.Infrastructure.Workers.FacturacionContabilidadListenerWorker(null, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Contabilidad.Infrastructure.Workers.FacturacionContabilidadListenerWorker>.Instance);

        w.TiposContables.Should().Contain(N.TerminalLiquidacionRegistrada);
    }

    private static string Migracion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        return File.ReadAllText(Directory.GetFiles(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            "*_TerminalPolizaLiquidacion.cs").Single());
    }

    private static readonly JsonSerializerOptions OpcionesPlantilla = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static List<LineaPlantillaContable> PlantillaSembrada()
    {
        var m = Regex.Match(Migracion(), @"\('facturacion\.terminal\.liquidacion-registrada\.v1', 'PR', '[^']*', '[^']*', '(?<lineas>\[[^']*\])'\)");
        m.Success.Should().BeTrue();
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(m.Groups["lineas"].Value, OpcionesPlantilla)!;
    }
}
