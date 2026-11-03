using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Integration;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// <c>[Fact]</c> opt-in de la carga CA10.15: sin <c>MILLET_CARGA_C15_SALIDA</c> xUnit la reporta como OMITIDA (skip visible), no
/// como verde. No corre en la CI.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CargaContableFactAttribute : FactAttribute
{
    public CargaContableFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MILLET_CARGA_C15_SALIDA")))
            Skip = "OMITIDO: falta MILLET_CARGA_C15_SALIDA (carga CA10.15 opt-in, 25,000 eventos).";
    }
}

/// <summary>
/// CA10.15 (ficha C1.5 §5): un mes con 25,000 transacciones → todas se contabilizan y la balanza cuadra. Entrega los eventos por
/// los cuatro listeners (entrada local, en serie como una suscripción) en ciclos de 10: recepción, factura y pago de proveedor;
/// factura de venta cobrada con tarjeta; dos cobros en efectivo y el cierre de su sesión; salida a centro de costo; factura de
/// venta a crédito. Escribe en <c>MILLET_CARGA_C15_SALIDA</c> el avance y el resumen (conteos y tiempos; sin datos de negocio).
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private const int EventosCarga = 25_000;
    private const int EventosPorCiclo = 10;
    private const int PolizasPorCiclo = 8;

    [CargaContableFact]
    public async Task CA10_15_un_mes_con_25000_eventos_genera_todas_las_polizas_y_la_balanza_cuadra()
    {
        var salida = Environment.GetEnvironmentVariable("MILLET_CARGA_C15_SALIDA")!;
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var ciclos = EventosCarga / EventosPorCiclo;
            var reloj = Stopwatch.StartNew();
            var parcial = Stopwatch.StartNew();
            for (var i = 1; i <= ciclos; i++)
            {
                await CicloCargaAsync(e, e.Clientes[i % e.Clientes.Length]);
                if (i % 250 == 0)
                {
                    await File.AppendAllTextAsync(salida, string.Create(CultureInfo.InvariantCulture,
                        $"{DateTimeOffset.Now:HH:mm:ss} eventos={i * EventosPorCiclo} total={reloj.Elapsed.TotalSeconds:F0}s ultimos2500={parcial.Elapsed.TotalSeconds:F1}s\n"));
                    parcial.Restart();
                }
            }
            reloj.Stop();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var polizas = await db.Database.SqlQuery<int>(
                $"SELECT count(DISTINCT poliza_id)::int AS \"Value\" FROM contabilidad.polizas_partidas WHERE sucursal_id = {e.Sucursal}").SingleAsync();
            var rechazos = await db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM contabilidad.rechazos_contables WHERE payload::text LIKE {"%" + e.Sucursal + "%"}").SingleAsync();
            var descuadradas = await db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM (SELECT poliza_id FROM contabilidad.polizas_partidas WHERE sucursal_id = {e.Sucursal} GROUP BY poliza_id HAVING sum(cargo) <> sum(abono)) d").SingleAsync();
            var cargos = await db.Database.SqlQuery<decimal>(
                $"SELECT coalesce(sum(cargo), 0) AS \"Value\" FROM contabilidad.polizas_partidas WHERE sucursal_id = {e.Sucursal}").SingleAsync();
            var abonos = await db.Database.SqlQuery<decimal>(
                $"SELECT coalesce(sum(abono), 0) AS \"Value\" FROM contabilidad.polizas_partidas WHERE sucursal_id = {e.Sucursal}").SingleAsync();
            var puente = await db.Database.SqlQuery<decimal>(
                $"SELECT coalesce(sum(cargo - abono), 0) AS \"Value\" FROM contabilidad.polizas_partidas WHERE sucursal_id = {e.Sucursal} AND cuenta_id = {e.Cuentas["MERCANCIA_RECIBIDA_POR_FACTURAR"]}").SingleAsync();

            await File.AppendAllTextAsync(salida, string.Create(CultureInfo.InvariantCulture,
                $"""
                RESUMEN CA10.15
                eventos={ciclos * EventosPorCiclo} polizas={polizas} (esperadas {ciclos * PolizasPorCiclo}) rechazos={rechazos} descuadradas={descuadradas}
                cargos={cargos:F2} abonos={abonos:F2} puente={puente:F2}
                tiempo={reloj.Elapsed:hh\:mm\:ss} eventos_por_segundo={ciclos * EventosPorCiclo / reloj.Elapsed.TotalSeconds:F1}

                """));

            rechazos.Should().Be(0, "todas las transacciones se contabilizan");
            polizas.Should().Be(ciclos * PolizasPorCiclo);
            descuadradas.Should().Be(0);
            cargos.Should().Be(abonos, "la balanza del mes cuadra");
            puente.Should().Be(0m, "cada recepción quedó facturada");
        }
        finally { await LimpiarAsync(e); }
    }

    /// <summary>10 eventos, 8 pólizas (EM, TT, PP, RF, PR tarjeta, PR liquidación del efectivo, SM, RF a crédito).</summary>
    private async Task CicloCargaAsync(Escenario e, Guid cliente)
    {
        var oc = Guid.NewGuid();
        var lineaOc = Guid.NewGuid();
        var factura = Guid.NewGuid();
        await Almacen(new OcRecepcionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, Guid.NewGuid(), "OC-CARGA-R1", oc, Hoy(), false,
            null, null, null, [new LineaRecepcionPayload(Guid.NewGuid(), lineaOc, Guid.NewGuid(), "PZA", 10m, 100m, 1_000m, Guid.NewGuid())],
            SucursalId: e.Sucursal));
        await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, oc, 1_160m,
            [new LineaFacturadaPayload(Guid.NewGuid(), lineaOc, 10m, 1_000m)], [new LineaOcAcumuladaPayload(lineaOc, 10m)],
            ProveedorId: e.Proveedor, Uuid: $"UUID-{factura:N}", Subtotal: 1_000m, Iva: 160m, RetencionesTotal: 0m, Moneda: "MXN",
            SucursalId: e.Sucursal));
        await Tesoreria(new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), 1_160m, "MXN", Hoy(),
            "Transferencia", "REF-CARGA"));

        await Facturacion(FacturaVenta(e, Guid.NewGuid(), cliente));
        await Facturacion(Cobro(e, Guid.NewGuid(), cliente, ("04", 2_320m)));

        var sesion = Guid.NewGuid();
        await Facturacion(Cobro(e, sesion, cliente, ("01", 500m)));
        await Facturacion(Cobro(e, sesion, cliente, ("01", 300m)));
        await Facturacion(SesionCerrada(e, sesion, 800m));

        await Almacen(new SalidaRequisicionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, Guid.NewGuid(), "SAL-CARGA", Hoy(), null, false,
            null, null, [new LineaSalidaPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 2m, 100m, 200m, e.CentroCosto, null)], SucursalId: e.Sucursal));

        await Facturacion(FacturaVenta(e, Guid.NewGuid(), cliente, 1_000m, 160m));
    }
}
