using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.Integration;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.Contabilidad.Application.CuentaPuente;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Workers;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Facturacion.Application.Integration;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// <c>[Fact]</c> opt-in de la prueba de volumen de F1-CON-05: sin <c>MILLET_VOLUMEN_CON05_SALIDA</c> xUnit la reporta como OMITIDA
/// (skip visible), no como verde. No corre en la CI normal; se filtra con <c>Category=VolumenContable</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class VolumenContableFactAttribute : FactAttribute
{
    public const string Variable = "MILLET_VOLUMEN_CON05_SALIDA";

    public VolumenContableFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"OMITIDO: falta {Variable} (prueba de volumen F1-CON-05 opt-in, 25,000 eventos).";
    }
}

/// <summary>
/// F1-CON-05 criterio 1 (86e3jvvkf): un mes de prueba con 25,000 eventos mezclados de los módulos que ya tienen plantilla (C1.4/C1.5
/// #1–#15 y la cuenta puente de K10.5) → cada evento queda en póliza o en la bandeja con su causa, la balanza del mes cuadra por cuenta
/// y la cuenta puente concilia con el reporte de K10.5; se reporta el tiempo total.
/// <para>
/// Los eventos son los records de integración de cada módulo, serializados como el outbox y entregados por los cuatro listeners (entrada
/// local de C1.8, uno a la vez como la suscripción). La mezcla sale de una semilla fija (reproducible): escenarios de negocio completos
/// (compra a pago, venta y cobro, caja, consumo, devoluciones, cancelaciones, anticipos) intercalados entre sí, con fechas repartidas en
/// el mes anterior y en tres sucursales. ~2 % de los eventos van a la bandeja a propósito, con causa conocida. La prueba calcula el saldo
/// esperado de cada rol y lo compara con la balanza. Escribe en <c>MILLET_VOLUMEN_CON05_SALIDA</c> el avance y el resumen (solo conteos,
/// importes de prueba y tiempos). Datos ficticios.
/// </para>
/// </summary>
[Trait("Category", "VolumenContable")]
public sealed class PolizasAutomaticasVolumenTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const int EventosMes = 25_000;
    private const int Semilla = 20_261_101;
    private const int EscenariosActivos = 40;
    private const int MaxEventosEscenario = 5;
    private const string Puente = "MERCANCIA_RECIBIDA_POR_FACTURAR";
    private const string RolSinCuenta = "COBROS_OTRAS_FORMAS";

    /// <summary>Rol → (sufijo de código, naturaleza, control). Todas afectables bajo una raíz FIX; el puente es la cuenta 2.1.</summary>
    private static readonly (string Rol, string Codigo, string Naturaleza, string Control)[] Roles =
    [
        ("INVENTARIO", "1.1", "Deudora", "Ninguna"),
        ("IVA_ACREDITABLE_PENDIENTE", "1.2", "Deudora", "Ninguna"),
        ("BANCOS", "1.3", "Deudora", "Ninguna"),
        ("BANCOS_MN", "1.4", "Deudora", "Ninguna"),
        ("CLIENTES", "1.5", "Deudora", "Clientes"),
        ("COBROS_TC_TD", "1.6", "Deudora", "Ninguna"),
        ("COBROS_CHEQUE", "1.7", "Deudora", "Ninguna"),
        ("EFECTIVO_CAJA_COBRANZA", "1.8", "Deudora", "Ninguna"),
        ("ANTICIPO_A_PROVEEDORES", "1.9", "Deudora", "Ninguna"),
        (Puente, "2.1", "Acreedora", "Ninguna"),
        ("PROVEEDORES", "2.2", "Acreedora", "Proveedores"),
        ("RETENCIONES_POR_PAGAR", "2.3", "Acreedora", "Ninguna"),
        ("IVA_TRASLADADO_NO_COBRADO", "2.4", "Acreedora", "Ninguna"),
        ("ANTICIPOS_DE_CLIENTES", "2.5", "Acreedora", "Ninguna"),
        ("VENTAS", "4.1", "Acreedora", "Ninguna"),
        ("DESCUENTOS_SOBRE_VENTAS", "4.2", "Deudora", "Ninguna"),
        ("CONSUMO", "5.1", "Deudora", "Ninguna"),
        ("AJUSTE_INVENTARIO", "5.2", "Deudora", "Ninguna"),
    ];

    /// <summary>Escenarios: clave, descripción, peso en la mezcla y si van a la bandeja a propósito.</summary>
    private static readonly (string Clave, string Descripcion, int Peso, bool Bandeja)[] Catalogo =
    [
        ("A", "Compra a pago: recepción, factura y pago (EM, TT, PP)", 140, false),
        ("B", "Recepción sin factura al cierre (puente abierto, K10.5)", 40, false),
        ("C", "Pago que llega antes que su factura (espera y se reprocesa)", 30, false),
        ("D", "Devolución a proveedor con nota de cargo (#15b, #3c)", 40, false),
        ("E", "Venta cobrada con tarjeta (RF, PR #11a)", 120, false),
        ("F", "Sesión de caja con 2 a 4 cobros en efectivo (#11b)", 80, false),
        ("G", "Venta a crédito cobrada en banco (RF, PR #12)", 100, false),
        ("H", "Venta cobrada por transferencia o cheque en mostrador (RF, PR #11a)", 80, false),
        ("I", "Anticipo de cliente y su aplicación (AN #8, RC #9)", 50, false),
        ("J", "Venta con nota de crédito por descuento (RF, RC #9)", 50, false),
        ("K", "Venta cancelada ante el SAT (RF y anulación)", 40, false),
        ("L", "Salida a consumo y devolución interna (#14, #15a)", 80, false),
        ("M", "Ajuste de inventario: faltante o sobrante (#13)", 50, false),
        ("N", "Factura de proveedor cancelada (TT y anulación; puente abierto)", 30, false),
        ("O", "Pago a proveedor revertido (PP y anulación)", 30, false),
        ("P", "Recepción cancelada (EM y anulación)", 30, false),
        ("X1", "Bandeja · anticipo a proveedor sin sucursal (DatoFaltante)", 8, true),
        ("X2", "Bandeja · salida sin centro de costo (DatoFaltante)", 8, true),
        ("X3", "Bandeja · cobro con otra forma de pago y rol sin cuenta (RolSinCuenta)", 8, true),
        ("X4", "Bandeja · factura de venta en periodo inexistente (PeriodoCerrado)", 8, true),
        ("X5", "Bandeja · factura de venta anterior a la plantilla vigente (SinPlantilla)", 8, true),
        ("X6", "Bandeja · factura de venta con total que no cuadra (NoCuadra)", 8, true),
    ];

    private sealed record Sucursal(Guid Id, Guid CentroCosto);

    private sealed record Escenario(string Suf, WebApplicationFactory<Program> F, HttpClient Admin, Sucursal[] Sucursales, Guid Proveedor,
        Guid[] Clientes, Dictionary<string, Guid> Cuentas, Dictionary<string, Guid?> Previo, OrdenesDoble Ordenes);

    /// <summary>Doble del puerto de Compras para el reporte de K10.5: las OC del mes no existen en Compras.</summary>
    private sealed class OrdenesDoble : IOrdenCompraPuentePort
    {
        private readonly Dictionary<Guid, OrdenCompraPuente> _ocs = [];

        public void Agregar(Guid id, string folio, Guid proveedor) => _ocs[id] = new(id, folio, proveedor);

        public Task<IReadOnlyDictionary<Guid, OrdenCompraPuente>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, OrdenCompraPuente>>(ids.Where(_ocs.ContainsKey).ToDictionary(id => id, id => _ocs[id]));
    }

    /// <summary>Lo que la mezcla espera: pólizas, bandeja por causa y detalle, saldo por rol y la cuenta puente abierta.</summary>
    private sealed class Esperado
    {
        public int Polizas;
        public int Reprocesados;
        public decimal PuenteAbierto;
        public int OrdenesAbiertas;
        public readonly Dictionary<(CausaRechazoContable Causa, string Detalle), int> Bandeja = [];
        public readonly Dictionary<string, decimal> Neto = [];
        public readonly Dictionary<string, int> PorEscenario = [];
        public readonly Dictionary<string, int> PorTipo = [];

        public void Mover(string rol, decimal cargoMenosAbono) => Neto[rol] = Neto.GetValueOrDefault(rol) + cargoMenosAbono;
        public void Rechazo(CausaRechazoContable causa, string detalle) => Bandeja[(causa, detalle)] = Bandeja.GetValueOrDefault((causa, detalle)) + 1;
    }

    /// <summary>Un evento por entregar: listener destino y record de integración.</summary>
    private sealed record Paso(char Listener, IntegrationEvent Evento);

    [VolumenContableFact]
    public async Task CON05_un_mes_con_25000_eventos_mezclados_queda_en_poliza_o_en_bandeja_y_la_balanza_cuadra()
    {
        var salida = Environment.GetEnvironmentVariable(VolumenContableFactAttribute.Variable)!;
        var total = Stopwatch.StartNew();
        Escenario? e = null;
        var entregados = new List<Guid>(EventosMes);
        var fallidos = new List<(string Tipo, string Error)>();
        Exception? fallo = null;
        try
        {
            e = await PrepararAsync();
            var preparacion = total.Elapsed;
            var hoy = Millet.SharedKernel.Application.FechaContable.HoyLocal(DateTimeOffset.UtcNow);
            var mes = new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(-1);
            var corte = mes.AddMonths(1).AddDays(-1);
            await AsegurarPeriodosAbiertosAsync(e.Admin, mes);
            (await Escalar<long>(e.F.Services, "SELECT count(*) FROM contabilidad.ejercicios_contables WHERE anio = 2097"))
                .Should().Be(0, "la bandeja por periodo usa un ejercicio que no existe");

            var esperado = new Esperado();
            var pasos = Mezclar(e, mes, esperado);
            pasos.Should().HaveCount(EventosMes);
            await Anotar(salida, $"F1-CON-05 · prueba de volumen · mes {mes:yyyy-MM} · semilla {Semilla} · {EventosMes} eventos\n");

            var reloj = Stopwatch.StartNew();
            var parcial = Stopwatch.StartNew();
            for (var i = 0; i < pasos.Count; i++)
            {
                var (id, error) = await EntregarAsync(e, pasos[i]);
                entregados.Add(id);
                if (error is not null) fallidos.Add((pasos[i].Evento.EventType, error));
                if (fallidos.Count > 500) break; // falla sistemática: no tiene caso seguir media hora
                if ((i + 1) % 2_500 == 0)
                {
                    await Anotar(salida, string.Create(CultureInfo.InvariantCulture,
                        $"{DateTimeOffset.Now:HH:mm:ss} eventos={i + 1} total={reloj.Elapsed.TotalSeconds:F0}s ultimos2500={parcial.Elapsed.TotalSeconds:F1}s\n"));
                    parcial.Restart();
                }
            }
            reloj.Stop();

            // ---- Verificación
            var verificacion = Stopwatch.StartNew();
            using var scope = e.F.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var sucursales = e.Sucursales.Select(s => s.Id).ToArray();

            var procesados = await db.EventosProcesados.AsNoTracking().CountAsync(x => entregados.Contains(x.EventoId));
            var rechazos = await db.RechazosContables.IgnoreQueryFilters().AsNoTracking().Where(r => entregados.Contains(r.EventoId))
                .Select(r => new { r.Causa, r.Detalle, r.Estado }).ToListAsync();
            var bandeja = rechazos.Where(r => r.Estado == EstadoRechazoContable.Pendiente)
                .GroupBy(r => (r.Causa, Detalle: r.Detalle ?? string.Empty)).ToDictionary(g => g.Key, g => g.Count());
            var reprocesados = rechazos.Count(r => r.Estado == EstadoRechazoContable.Reprocesado);
            var partidas = db.PolizasPartidas.IgnoreQueryFilters().AsNoTracking().Where(x => sucursales.Contains(x.SucursalId));
            var polizas = await partidas.Select(x => x.PolizaId).Distinct().CountAsync();
            var descuadradas = await partidas.GroupBy(x => x.PolizaId)
                .Select(g => new { Cargos = g.Sum(x => x.Cargo), Abonos = g.Sum(x => x.Abono) }).CountAsync(p => p.Cargos != p.Abonos);
            var balanza = await partidas.GroupBy(x => x.CuentaId)
                .Select(g => new { Cuenta = g.Key, Cargos = g.Sum(x => x.Cargo), Abonos = g.Sum(x => x.Abono) }).ToListAsync();
            var efectivoPendiente = await db.CobrosEfectivoCaja.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.SucursalId != null && sucursales.Contains(x.SucursalId.Value) && x.Estado == EstadoCobroEfectivo.Pendiente);

            var rolDe = e.Cuentas.ToDictionary(kv => kv.Value, kv => kv.Key);
            var netoReal = balanza.ToDictionary(b => rolDe.GetValueOrDefault(b.Cuenta, b.Cuenta.ToString()), b => b.Cargos - b.Abonos);
            var cargos = balanza.Sum(b => b.Cargos);
            var abonos = balanza.Sum(b => b.Abonos);

            // Sesión nueva: la del arranque puede haber vencido durante la entrega (corridas largas o equipo suspendido).
            using var lector = await LoginAsync(e.F);
            var reporte = await Json(await lector.GetAsync($"{Base}/reportes/cuenta-puente?corte={corte:yyyy-MM-dd}&soloConSaldo=true"));
            var totales = reporte.GetProperty("totales");
            verificacion.Stop();
            total.Stop();

            foreach (var g in fallidos.GroupBy(x => (x.Tipo, x.Error)).OrderByDescending(g => g.Count()))
                await Anotar(salida, $"FALLO al consumir {g.Key.Tipo} ({g.Count()}): {g.Key.Error}\n");
            await Anotar(salida, Resumen(esperado, mes, procesados, polizas, bandeja, reprocesados, descuadradas, cargos, abonos, netoReal,
                efectivoPendiente, totales, preparacion, reloj.Elapsed, verificacion.Elapsed, total.Elapsed));

            fallidos.Should().BeEmpty("ningún evento revienta al consumirse: o genera póliza o entra a la bandeja");
            procesados.Should().Be(EventosMes, "cada evento entregado quedó registrado como procesado (ninguno se perdió)");
            polizas.Should().Be(esperado.Polizas, "cada evento contabilizable generó su póliza (cada cobro en efectivo la suya)");
            bandeja.Should().BeEquivalentTo(esperado.Bandeja, "la bandeja tiene exactamente los eventos enviados a propósito, con su causa");
            reprocesados.Should().Be(esperado.Reprocesados, "los pagos que llegaron antes que su factura salieron solos de la bandeja");
            efectivoPendiente.Should().Be(0, "todo cobro en efectivo quedó con su póliza");
            descuadradas.Should().Be(0);
            cargos.Should().Be(abonos, "la balanza del mes cuadra");
            netoReal.Where(kv => kv.Value != 0m).ToDictionary(kv => kv.Key, kv => kv.Value)
                .Should().BeEquivalentTo(esperado.Neto.Where(kv => kv.Value != 0m).ToDictionary(kv => kv.Key, kv => kv.Value),
                    "el saldo de cada cuenta en la balanza es el que esperan los documentos del mes");
            // K10.5 suma el saldo con signo (cargo − abono): lo recibido sin facturar es saldo acreedor, negativo.
            totales.GetProperty("recibidoSinFacturar").GetDecimal().Should().Be(-esperado.PuenteAbierto, "K10.5: lo recibido sin facturar");
            totales.GetProperty("ordenesConSaldo").GetInt32().Should().Be(esperado.OrdenesAbiertas);
            totales.GetProperty("cuadra").GetBoolean().Should().BeTrue("el reporte de la cuenta puente concilia con la balanza");
        }
        catch (Exception ex)
        {
            fallo = ex;
            await Anotar(salida, $"ERROR: {ex}\n");
            throw;
        }
        finally
        {
            try { await LimpiarAsync(e, entregados); }
            catch (Exception ex) when (fallo is not null)
            {
                await Anotar(salida, $"(limpieza también falló: {ex.Message})\n");
            }
        }
    }

    // ---------------------------------------------------------------- Mezcla

    /// <summary>
    /// Arma escenarios con la semilla fija hasta sumar <see cref="EventosMes"/> y los intercala: hasta <see cref="EscenariosActivos"/>
    /// escenarios abiertos a la vez y el siguiente evento sale de uno al azar (cada escenario conserva su propio orden).
    /// </summary>
    private static List<Paso> Mezclar(Escenario e, DateOnly mes, Esperado esperado)
    {
        var rng = new Random(Semilla);
        var dias = mes.AddMonths(1).AddDays(-1).Day;
        var pesoTotal = Catalogo.Sum(c => c.Peso);
        var restantes = EventosMes;
        var activos = new List<Queue<Paso>>();
        var pasos = new List<Paso>(EventosMes);

        while (restantes > 0 || activos.Count > 0)
        {
            while (restantes > 0 && activos.Count < EscenariosActivos)
            {
                var tirada = rng.Next(pesoTotal);
                var clave = Catalogo.First(c => (tirada -= c.Peso) < 0).Clave;
                // Relleno final: con menos eventos por repartir que el escenario más largo (5), recepciones sueltas de un evento.
                if (restantes < MaxEventosEscenario) clave = "B";
                var escenario = Generar(clave, e, mes.AddDays(rng.Next(dias)), rng, esperado);
                restantes -= escenario.Count;
                activos.Add(new Queue<Paso>(escenario));
            }
            var i = rng.Next(activos.Count);
            pasos.Add(activos[i].Dequeue());
            if (activos[i].Count == 0) activos.RemoveAt(i);
        }

        foreach (var p in pasos) esperado.PorTipo[p.Evento.EventType] = esperado.PorTipo.GetValueOrDefault(p.Evento.EventType) + 1;
        return pasos;
    }

    private static List<Paso> Generar(string clave, Escenario e, DateOnly dia, Random rng, Esperado x)
    {
        x.PorEscenario[clave] = x.PorEscenario.GetValueOrDefault(clave) + 1;
        // Mediodía de Ciudad de México expresado en UTC, como lo publican los módulos (reloj UtcNow).
        var ocurrido = new DateTimeOffset(dia.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-6)).ToUniversalTime();
        var suc = e.Sucursales[rng.Next(e.Sucursales.Length)];
        var cliente = e.Clientes[rng.Next(e.Clientes.Length)];
        var s = Importe(rng);
        var iva = Iva(s);
        var t = s + iva;
        var pasos = new List<Paso>();

        switch (clave)
        {
            case "A":
            case "C":
            case "O":
            {
                var (oc, lineaOc, factura, pago) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                e.Ordenes.Agregar(oc, $"OC-VOL-{oc:N}"[..14], e.Proveedor);
                var recepcion = Recepcion(e, ocurrido, dia, suc, oc, lineaOc, s);
                var tt = FacturaProveedor(e, ocurrido, suc, oc, lineaOc, factura, s, iva);
                var pp = new Paso('T', new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaBootstrapId, ocurrido, factura, pago, t, "MXN", dia,
                    "Transferencia", "REF-VOL"));
                pasos.AddRange(clave == "C" ? [recepcion, pp, tt] : [recepcion, tt, pp]);
                x.Mover("INVENTARIO", s);
                x.Mover("IVA_ACREDITABLE_PENDIENTE", iva);
                if (clave == "O")
                {
                    pasos.Add(new Paso('T', new PagoFacturaProveedorRevertidoIntegrationEvent(EmpresaBootstrapId, ocurrido, factura, pago, t, "MXN",
                        dia, "Rechazo bancario")));
                    x.Mover("PROVEEDORES", -t);
                    x.Polizas += 4;
                }
                else
                {
                    x.Mover("BANCOS", -t);
                    x.Polizas += 3;
                }
                if (clave == "C") x.Reprocesados++;
                break;
            }
            case "B":
            case "N":
            case "P":
            {
                var (oc, lineaOc) = (Guid.NewGuid(), Guid.NewGuid());
                e.Ordenes.Agregar(oc, $"OC-VOL-{oc:N}"[..14], e.Proveedor);
                var recepcionId = Guid.NewGuid();
                pasos.Add(Recepcion(e, ocurrido, dia, suc, oc, lineaOc, s, recepcionId));
                if (clave == "P")
                {
                    pasos.Add(new Paso('A', new OcRecepcionCanceladaIntegrationEvent(EmpresaBootstrapId, ocurrido, recepcionId, "REC-VOL", oc, dia,
                        "01", "Captura duplicada", Guid.NewGuid(), ["CAN-VOL"],
                        [new LineaRecepcionCanceladaPayload(Guid.NewGuid(), lineaOc, Guid.NewGuid(), "PZA", 1m, 1m, s, s, Guid.NewGuid())],
                        SucursalId: suc.Id)));
                    x.Polizas += 2;
                    break;
                }
                x.Mover("INVENTARIO", s);
                x.Mover(Puente, -s);
                x.PuenteAbierto += s;
                x.OrdenesAbiertas++;
                x.Polizas += 1;
                if (clave == "N")
                {
                    var factura = Guid.NewGuid();
                    pasos.Add(FacturaProveedor(e, ocurrido, suc, oc, lineaOc, factura, s, iva));
                    pasos.Add(new Paso('C', new FacturaProveedorCanceladaIntegrationEvent(EmpresaBootstrapId, ocurrido, factura, oc, "02",
                        "Factura con error", ProveedorId: e.Proveedor, SucursalId: suc.Id)));
                    x.Polizas += 2;
                }
                break;
            }
            case "D":
            {
                var (oc, lineaOc, factura) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                e.Ordenes.Agregar(oc, $"OC-VOL-{oc:N}"[..14], e.Proveedor);
                var d = Math.Round(s * 0.3m, 2);
                var ivaD = Iva(d);
                pasos.Add(Recepcion(e, ocurrido, dia, suc, oc, lineaOc, s));
                pasos.Add(FacturaProveedor(e, ocurrido, suc, oc, lineaOc, factura, s, iva));
                pasos.Add(new Paso('A', new OcDevolucionRegistradaIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), "DEV-VOL", e.Proveedor,
                    null, factura, oc, "Defecto", d,
                    [new LineaDevolucionProveedorPayload(Guid.NewGuid(), null, lineaOc, Guid.NewGuid(), "PZA", 1m, d, d)], SucursalId: suc.Id)));
                pasos.Add(new Paso('C', new NotaCargoAutorizadaIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), e.Proveedor, d + ivaD,
                    factura, Uuid: $"UUID-{Guid.NewGuid():N}", Subtotal: d, Iva: ivaD, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: suc.Id)));
                x.Mover("INVENTARIO", s - d);
                x.Mover("IVA_ACREDITABLE_PENDIENTE", iva - ivaD);
                x.Mover("PROVEEDORES", -t + d + ivaD);
                x.Polizas += 4;
                break;
            }
            case "E":
            case "G":
            case "H":
            case "J":
            case "K":
            {
                var factura = Guid.NewGuid();
                pasos.Add(new Paso('F', FacturaVenta(ocurrido, dia, suc, factura, cliente, s, iva)));
                x.Mover("VENTAS", -s);
                x.Mover("IVA_TRASLADADO_NO_COBRADO", -iva);
                x.Polizas += 2;
                switch (clave)
                {
                    case "E":
                        pasos.Add(new Paso('F', Cobro(ocurrido, suc, Guid.NewGuid(), factura, cliente, ("04", t))));
                        x.Mover("COBROS_TC_TD", t);
                        break;
                    case "G":
                        pasos.Add(new Paso('T', new PagoClienteConfirmadoIntegrationEvent(EmpresaBootstrapId, ocurrido, null, cliente, Guid.NewGuid(),
                            Guid.NewGuid(), t, "MXN", dia, "REF-VOL", [new PagoClienteFacturaAplicada(factura, t)])));
                        x.Mover("BANCOS", t);
                        break;
                    case "H":
                        var cheque = rng.Next(2) == 0;
                        pasos.Add(new Paso('F', Cobro(ocurrido, suc, Guid.NewGuid(), factura, cliente, (cheque ? "02" : "03", t))));
                        x.Mover(cheque ? "COBROS_CHEQUE" : "BANCOS_MN", t);
                        break;
                    case "J":
                        var dsc = Math.Round(s * 0.1m, 2);
                        var ivaDsc = Iva(dsc);
                        pasos.Add(new Paso('F', new NotaCreditoTimbradaIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), "Descuento",
                            $"UUID-{Guid.NewGuid():N}", dsc + ivaDsc, factura, null, Subtotal: dsc, Iva: ivaDsc, SucursalId: suc.Id,
                            ClienteId: cliente, Moneda: "MXN", FechaContable: dia)));
                        x.Mover("DESCUENTOS_SOBRE_VENTAS", dsc);
                        x.Mover("IVA_TRASLADADO_NO_COBRADO", ivaDsc);
                        x.Mover("CLIENTES", t - dsc - ivaDsc);
                        break;
                    case "K":
                        pasos.Add(new Paso('F', new ComprobanteCanceladoIntegrationEvent(EmpresaBootstrapId, ocurrido, factura, "Ingreso", $"UUID-{factura:N}")));
                        x.Mover("VENTAS", s);
                        x.Mover("IVA_TRASLADADO_NO_COBRADO", iva);
                        break;
                }
                break;
            }
            case "F":
            {
                var sesion = Guid.NewGuid();
                var efectivo = 0m;
                var cobrosSesion = 2 + rng.Next(3);
                for (var k = 0; k < cobrosSesion; k++)
                {
                    var importe = Importe(rng, maximo: 3_000);
                    efectivo += importe;
                    pasos.Add(new Paso('F', Cobro(ocurrido, suc, sesion, Guid.NewGuid(), e.Clientes[rng.Next(e.Clientes.Length)], ("01", importe))));
                }
                pasos.Add(new Paso('F', new CajaSesionCerradaIntegrationEvent(EmpresaBootstrapId, ocurrido, sesion, Guid.NewGuid(), suc.Id,
                    Guid.NewGuid(), dia, 500m, efectivo + 500m, efectivo + 500m, 0m, false, [new CajaSesionCorteTotal("01", efectivo + 500m, efectivo + 500m)])));
                x.Mover("EFECTIVO_CAJA_COBRANZA", efectivo);
                x.Mover("CLIENTES", -efectivo);
                x.Polizas += cobrosSesion;   // M-174: una póliza por cobro en efectivo; el cierre no genera asiento
                break;
            }
            case "I":
            {
                var anticipo = Guid.NewGuid();
                pasos.Add(new Paso('F', new FacturaAnticipoTimbradaIntegrationEvent(EmpresaBootstrapId, ocurrido, anticipo, Guid.NewGuid(),
                    $"UUID-{anticipo:N}", t, "MXN", Subtotal: s, Iva: iva, SucursalId: suc.Id, ClienteId: cliente, FechaContable: dia)));
                pasos.Add(new Paso('F', new NotaCreditoTimbradaIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), "AplicacionAnticipo",
                    $"UUID-{Guid.NewGuid():N}", t, null, anticipo, Subtotal: s, Iva: iva, SucursalId: suc.Id, ClienteId: cliente, Moneda: "MXN",
                    FechaContable: dia)));
                x.Mover("ANTICIPOS_DE_CLIENTES", iva);
                x.Mover("IVA_TRASLADADO_NO_COBRADO", -iva);
                x.Polizas += 2;
                break;
            }
            case "L":
            {
                var (salidaId, articulo) = (Guid.NewGuid(), Guid.NewGuid());
                var devuelto = Math.Round(s * 0.25m, 2);
                pasos.Add(Salida(ocurrido, dia, suc, salidaId, articulo, s, suc.CentroCosto));
                pasos.Add(new Paso('A', new DevolucionInternaAplicadaIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), "DEV-VOL", salidaId,
                    Guid.NewGuid(), "Bueno", devuelto, [new LineaDevolucionPayload(Guid.NewGuid(), articulo, "PZA", 1m, devuelto, devuelto)],
                    SucursalId: suc.Id)));
                x.Mover("CONSUMO", s - devuelto);
                x.Mover("INVENTARIO", -s + devuelto);
                x.Polizas += 2;
                break;
            }
            case "M":
            {
                var faltante = rng.Next(2) == 0;
                pasos.Add(new Paso('A', new AjusteInventarioAplicadoIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), faltante ? -s : s,
                    Guid.NewGuid(),
                    [new AjusteInventarioPayload(Guid.NewGuid(), "AJ-VOL", faltante ? "AjusteNegativo" : "AjustePositivo", Guid.NewGuid(), Guid.NewGuid(),
                        Guid.NewGuid(), 1m, s, faltante ? -s : s)],
                    SucursalId: suc.Id)));
                x.Mover("AJUSTE_INVENTARIO", faltante ? s : -s);
                x.Mover("INVENTARIO", faltante ? -s : s);
                x.Polizas += 1;
                break;
            }
            // ---- Bandeja a propósito (sin póliza)
            case "X1":
                pasos.Add(new Paso('C', new AnticipoProveedorCapturadoIntegrationEvent(EmpresaBootstrapId, ocurrido, Guid.NewGuid(), e.Proveedor, t,
                    null, Moneda: "MXN")));
                x.Rechazo(CausaRechazoContable.DatoFaltante, "SucursalId");
                break;
            case "X2":
                pasos.Add(Salida(ocurrido, dia, suc, Guid.NewGuid(), Guid.NewGuid(), s, centroCosto: null));
                x.Rechazo(CausaRechazoContable.DatoFaltante, "CentroCostoId");
                break;
            case "X3":
                pasos.Add(new Paso('F', Cobro(ocurrido, suc, Guid.NewGuid(), Guid.NewGuid(), cliente, ("05", t))));
                x.Rechazo(CausaRechazoContable.RolSinCuenta, RolSinCuenta);
                break;
            case "X4":
                pasos.Add(new Paso('F', FacturaVenta(ocurrido, new DateOnly(2097, dia.Month, dia.Day), suc, Guid.NewGuid(), cliente, s, iva)));
                x.Rechazo(CausaRechazoContable.PeriodoCerrado, "CONTAB_PERIODO_INEXISTENTE");
                break;
            case "X5":
                pasos.Add(new Paso('F', FacturaVenta(ocurrido, new DateOnly(2025, 12, 15), suc, Guid.NewGuid(), cliente, s, iva)));
                x.Rechazo(CausaRechazoContable.SinPlantilla, "facturacion.factura-venta.timbrada.v1");
                break;
            case "X6":
                pasos.Add(new Paso('F', FacturaVenta(ocurrido, dia, suc, Guid.NewGuid(), cliente, s, iva, total: t + 1m)));
                x.Rechazo(CausaRechazoContable.NoCuadra, $"{(t + 1m).ToString("N2", CultureInfo.CurrentCulture)} vs {t.ToString("N2", CultureInfo.CurrentCulture)}");
                break;
            default:
                throw new InvalidOperationException(clave);
        }
        return pasos;
    }

    private static decimal Importe(Random rng, int maximo = 20_000) => rng.Next(100, maximo) + rng.Next(100) / 100m;

    private static decimal Iva(decimal subtotal) => Math.Round(subtotal * 0.16m, 2, MidpointRounding.AwayFromZero);

    private static Paso Recepcion(Escenario e, DateTimeOffset ocurrido, DateOnly dia, Sucursal suc, Guid oc, Guid lineaOc, decimal s, Guid? id = null) =>
        new('A', new OcRecepcionRegistradaIntegrationEvent(EmpresaBootstrapId, ocurrido, id ?? Guid.NewGuid(), "REC-VOL", oc, dia, false, null, null,
            null, [new LineaRecepcionPayload(Guid.NewGuid(), lineaOc, Guid.NewGuid(), "PZA", 1m, s, s, Guid.NewGuid())], SucursalId: suc.Id));

    private static Paso FacturaProveedor(Escenario e, DateTimeOffset ocurrido, Sucursal suc, Guid oc, Guid lineaOc, Guid factura, decimal s, decimal iva) =>
        new('C', new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, ocurrido, factura, oc, s + iva,
            [new LineaFacturadaPayload(Guid.NewGuid(), lineaOc, 1m, s)], [new LineaOcAcumuladaPayload(lineaOc, 1m)], ProveedorId: e.Proveedor,
            Uuid: $"UUID-{factura:N}", Subtotal: s, Iva: iva, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: suc.Id));

    private static Paso Salida(DateTimeOffset ocurrido, DateOnly dia, Sucursal suc, Guid salida, Guid articulo, decimal s, Guid? centroCosto) =>
        new('A', new SalidaRequisicionRegistradaIntegrationEvent(EmpresaBootstrapId, ocurrido, salida, "SAL-VOL", dia, null, false, null, null,
            [new LineaSalidaPayload(Guid.NewGuid(), articulo, "PZA", 1m, s, s, centroCosto, null)], SucursalId: suc.Id));

    private static FacturaVentaTimbradaIntegrationEvent FacturaVenta(DateTimeOffset ocurrido, DateOnly fecha, Sucursal suc, Guid factura, Guid cliente,
        decimal s, decimal iva, decimal? total = null) =>
        new(EmpresaBootstrapId, ocurrido, factura, $"UUID-{factura:N}", total ?? s + iva, "MXN", null, "XAXX010101000", "FIX cliente", "FV-VOL",
            "PUE", ocurrido, Subtotal: s, Descuento: 0m, Iva: iva, RetencionesTotal: 0m, SucursalId: suc.Id, ClienteId: cliente,
            FechaContable: fecha, Lineas: [new FacturaLineaContablePayload(null, "30171500", "PT", s, 0m, iva)]);

    private static CobroMostradorRegistradoIntegrationEvent Cobro(DateTimeOffset ocurrido, Sucursal suc, Guid sesion, Guid comprobante, Guid cliente,
        params (string Forma, decimal Importe)[] formas) =>
        new(EmpresaBootstrapId, ocurrido, Guid.CreateVersion7(), sesion, Guid.NewGuid(), comprobante, "FacturaVenta", "Mostrador",
            formas.Sum(f => f.Importe), [.. formas.Select(f => new CobroFormaPagoAplicada(f.Forma, f.Importe))], suc.Id, cliente, "MXN");

    // ---------------------------------------------------------------- Entrega local

    private static async Task<(Guid Id, string? Error)> EntregarAsync(Escenario e, Paso paso)
    {
        var id = Guid.CreateVersion7();
        ILocalIntegrationEventConsumer consumidor = paso.Listener switch
        {
            'A' => Listener<AlmacenContabilidadListenerWorker>(e),
            'C' => Listener<CuentasPorPagarContabilidadListenerWorker>(e),
            'T' => Listener<TesoreriaContabilidadListenerWorker>(e),
            _ => Listener<FacturacionContabilidadListenerWorker>(e),
        };
        try
        {
            await consumidor.ConsumirLocalAsync(paso.Evento.EventType, id, JsonSerializer.Serialize(paso.Evento, paso.Evento.GetType()),
                CancellationToken.None);
            return (id, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var interna = ex.GetBaseException();
            return (id, $"{ex.GetType().Name}: {ex.Message}" + (interna == ex ? "" : $" ← {interna.GetType().Name}: {interna.Message}"));
        }
    }

    private static T Listener<T>(Escenario e) where T : ContabilidadEventListenerWorker =>
        (T)Activator.CreateInstance(typeof(T), null, e.F.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<T>.Instance)!;

    // ---------------------------------------------------------------- Preparación y limpieza

    private async Task<Escenario> PrepararAsync()
    {
        var suf = Sufijo();
        var ordenes = new OrdenesDoble();
        var f = factory.ConEmpresa(EmpresaBootstrapId, ("Contabilidad:CuentaPuente:CodigoCuenta", Codigo(suf, "2.1")))
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IOrdenCompraPuentePort>();
                s.AddSingleton<IOrdenCompraPuentePort>(ordenes);
            }));
        var admin = await LoginAsync(f);

        var sucursales = new List<Sucursal>();
        for (var i = 1; i <= 3; i++)
        {
            var resp = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales",
                new { Id = Guid.Empty, Clave = $"V{i}{suf}", Nombre = $"FIX Sucursal volumen {i} {suf}" });
            resp.EnsureSuccessStatusCode();
            var sucursal = (await Json(resp)).GetProperty("id").GetGuid();
            using var scope = f.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var g2 = await mediator.Send(new CrearGrupoDim2Command($"FIX G2 {suf} {i}"));
            var g3 = await mediator.Send(new CrearGrupoDim3Command($"FIX G3 {suf} {i}"));
            var d1 = await mediator.Send(new CrearDim1Command($"L{i}{suf}", $"FIX Ubicación {suf} {i}"));
            var d2 = await mediator.Send(new CrearDim2Command(d1.Id, $"M{i}{suf}", "FIX CeCo mantenimiento", g2.Id));
            var centro = (await mediator.Send(new CrearDim3Command(d2.Id, $"N{i}{suf}", "FIX CC-VOL-MANT", g3.Id))).Id;
            (await admin.PutAsJsonAsync($"/api/v1/contabilidad/ubicaciones-sucursal/{d1.Id}", new { sucursalId = sucursal })).EnsureSuccessStatusCode();
            sucursales.Add(new Sucursal(sucursal, centro));
        }

        var activo = (await CrearCuenta(admin, Codigo(suf, "1"), "FIX activo")).GetProperty("id").GetGuid();
        var pasivo = (await CrearCuenta(admin, Codigo(suf, "2"), "FIX pasivo", naturaleza: "Acreedora")).GetProperty("id").GetGuid();
        var ingresos = (await CrearCuenta(admin, Codigo(suf, "4"), "FIX ingresos", naturaleza: "Acreedora")).GetProperty("id").GetGuid();
        var gastos = (await CrearCuenta(admin, Codigo(suf, "5"), "FIX gastos")).GetProperty("id").GetGuid();
        var cuentas = new Dictionary<string, Guid>();
        foreach (var (rol, codigo, naturaleza, control) in Roles)
        {
            var padre = codigo[0] switch { '1' => activo, '2' => pasivo, '4' => ingresos, _ => gastos };
            cuentas[rol] = (await CrearCuenta(admin, Codigo(suf, codigo), $"FIX {rol}", padre, naturaleza: naturaleza, control: control))
                .GetProperty("id").GetGuid();
        }

        var (proveedor, cliente1) = await MotorTerceros.SembrarAsync(f);
        var (_, cliente2) = await MotorTerceros.SembrarAsync(f);
        var (_, cliente3) = await MotorTerceros.SembrarAsync(f);

        // Mapea los roles a las cuentas FIX y deja COBROS_OTRAS_FORMAS sin cuenta (bandeja X3); al terminar se restaura todo.
        var previo = new Dictionary<string, Guid?>();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var codigos = cuentas.Keys.Append(RolSinCuenta).ToList();
            foreach (var r in await db.RolesCuenta.IgnoreQueryFilters().Where(r => r.EmpresaId == EmpresaBootstrapId && codigos.Contains(r.Codigo)).ToListAsync())
                previo[r.Codigo] = r.CuentaId;
            foreach (var (rol, cuenta) in cuentas)
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = {2}",
                    cuenta, EmpresaBootstrapId, rol);
            await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = NULL WHERE empresa_id = {0} AND codigo = {1}",
                EmpresaBootstrapId, RolSinCuenta);
        }
        return new(suf, f, admin, [.. sucursales], proveedor, [cliente1, cliente2, cliente3], cuentas, previo, ordenes);
    }

    private static async Task LimpiarAsync(Escenario? e, List<Guid> eventos)
    {
        if (e is null) return;
        var ids = eventos.ToArray();
        var sucursales = e.Sucursales.Select(s => s.Id).ToArray();
        using (var scope = e.F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
            // La tabla temporal vive en la sesión: una sola conexión abierta para todas las sentencias.
            await db.Database.OpenConnectionAsync();
            foreach (var (codigo, cuenta) in e.Previo)
                await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.roles_cuenta SET cuenta_id = {0} WHERE empresa_id = {1} AND codigo = {2}",
                    cuenta!, EmpresaBootstrapId, codigo);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta_cambios WHERE cuenta_nueva_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.rechazos_contables WHERE evento_id = ANY({0})", ids);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.eventos_procesados WHERE evento_id = ANY({0})", ids);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cobros_efectivo_caja WHERE sucursal_id = ANY({0})", sucursales);
            await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE IF NOT EXISTS vol_polizas (id uuid PRIMARY KEY) ON COMMIT PRESERVE ROWS");
            await db.Database.ExecuteSqlRawAsync("TRUNCATE vol_polizas");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO vol_polizas SELECT DISTINCT poliza_id FROM contabilidad.polizas_partidas WHERE sucursal_id = ANY({0})", sucursales);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.movimientos_puente WHERE poliza_id IN (SELECT id FROM vol_polizas)");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consumos_contabilizados WHERE poliza_id IN (SELECT id FROM vol_polizas)");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM vol_polizas)");
            await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.polizas SET anulada_por_poliza_id = NULL, poliza_anulada_id = NULL WHERE id IN (SELECT id FROM vol_polizas)");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE id IN (SELECT id FROM vol_polizas)");
            await db.Database.ExecuteSqlRawAsync("DROP TABLE vol_polizas");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
        }
        await Limpiar(e.F.Services, e.Suf);
        e.F.Dispose();
    }

    // ---------------------------------------------------------------- Resumen

    private static Task Anotar(string salida, string texto) => File.AppendAllTextAsync(salida, texto);

    private static string Resumen(Esperado x, DateOnly mes, int procesados, int polizas,
        Dictionary<(CausaRechazoContable Causa, string Detalle), int> bandeja, int reprocesados, int descuadradas, decimal cargos, decimal abonos,
        Dictionary<string, decimal> neto, int efectivoPendiente, JsonElement puente, TimeSpan preparacion, TimeSpan entrega, TimeSpan verificacion,
        TimeSpan total)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"RESUMEN F1-CON-05 · mes {mes:yyyy-MM} · semilla {Semilla}");
        sb.AppendLine(inv, $"eventos entregados={EventosMes} procesados={procesados}");
        sb.AppendLine(inv, $"polizas={polizas} (esperadas {x.Polizas}) descuadradas={descuadradas}");
        sb.AppendLine(inv, $"bandeja pendiente={bandeja.Values.Sum()} (esperada {x.Bandeja.Values.Sum()}) · reprocesados solos={reprocesados} (esperados {x.Reprocesados})");
        foreach (var ((causa, detalle), n) in bandeja.OrderBy(kv => kv.Key.Causa))
            sb.AppendLine(inv, $"  bandeja {causa} · {detalle}: {n} (esperados {x.Bandeja.GetValueOrDefault((causa, detalle))})");
        sb.AppendLine(inv, $"cobros en efectivo sin liquidar={efectivoPendiente}");
        sb.AppendLine(inv, $"balanza cargos={cargos:F2} abonos={abonos:F2} diferencia={cargos - abonos:F2}");
        foreach (var (rol, _, _, _) in Roles)
            sb.AppendLine(inv, $"  {rol}: saldo={neto.GetValueOrDefault(rol):F2} esperado={x.Neto.GetValueOrDefault(rol):F2}");
        foreach (var otra in neto.Keys.Except(Roles.Select(r => r.Rol)))
            sb.AppendLine(inv, $"  (cuenta fuera de los roles) {otra}: saldo={neto[otra]:F2}");
        sb.AppendLine(inv, $"cuenta puente K10.5: recibidoSinFacturar={puente.GetProperty("recibidoSinFacturar").GetDecimal():F2} (esperado {-x.PuenteAbierto:F2}) " +
            $"ordenesConSaldo={puente.GetProperty("ordenesConSaldo").GetInt32()} (esperadas {x.OrdenesAbiertas}) " +
            $"saldoBalanza={puente.GetProperty("saldoBalanza")} partidasSinOc={puente.GetProperty("partidasSinOc")} cuadra={puente.GetProperty("cuadra")}");
        sb.AppendLine("escenarios:");
        foreach (var c in Catalogo)
            sb.AppendLine(inv, $"  {c.Clave} {c.Descripcion}: {x.PorEscenario.GetValueOrDefault(c.Clave)}");
        sb.AppendLine("eventos por tipo:");
        foreach (var (tipo, n) in x.PorTipo.OrderByDescending(kv => kv.Value))
            sb.AppendLine(inv, $"  {tipo}: {n}");
        sb.AppendLine(inv, $"tiempo preparacion={preparacion:hh\\:mm\\:ss} entrega={entrega:hh\\:mm\\:ss} verificacion={verificacion:hh\\:mm\\:ss} total={total:hh\\:mm\\:ss} " +
            $"eventos_por_segundo={EventosMes / entrega.TotalSeconds:F1}");
        return sb.ToString();
    }
}
