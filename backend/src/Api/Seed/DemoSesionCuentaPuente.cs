using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.CuentaPuente;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.Seed;

/// <summary>
/// K10.5 · cuenta puente recepción–factura con datos DEMO para el reporte de conciliación por OC. Crea (si faltan) cuatro cuentas
/// afectables y registra pólizas automáticas con el puerto del motor (<see cref="IPolizaContablePort"/>), y después sus movimientos
/// de puente con <see cref="RegistroMovimientosPuente"/> (la misma entrada que usa el observador de C1.4). Va directo y no por el
/// observador porque las OC son solo referencias (folio OC-DEMO-K105-0x en la partida): no se crean órdenes de compra en Compras, y
/// el observador toma el folio de Compras. Montos de la puente sin IVA.
/// <para>Escenarios: 01 recepción + factura (saldada) · 02 factura de reserva sin recepción (facturado sin recibir) · 03 recepción
/// sin factura de hace 61 días (más de 60) · 04 recepción + factura + devolución + nota de cargo (saldada; la nota resuelve la OC por
/// su factura de origen).</para>
/// </summary>
public sealed partial class DemoSesionSeedHostedService
{
    /// <summary>Código por omisión de la cuenta puente DEMO (el de appsettings.Development: Contabilidad:CuentaPuente:CodigoCuenta).</summary>
    public const string CodigoCuentaPuenteDemo = "DEMO-PUENTE";

    /// <summary>Importe sin IVA de la recepción de OC-DEMO-K105-03 (sin factura).</summary>
    public const decimal ImporteRecepcionSinFacturaDemo = 3_000m;

    private const decimal TasaIvaPuenteDemo = 0.16m;

    private enum RolCuentaPuente { Puente, Inventario, Proveedores, Iva }

    private static readonly (RolCuentaPuente Rol, string Codigo, string Nombre, NaturalezaCuenta Naturaleza)[] CuentasPuenteDemo =
    [
        (RolCuentaPuente.Puente, CodigoCuentaPuenteDemo, "DEMO Cuenta puente recepción–factura", NaturalezaCuenta.Acreedora),
        (RolCuentaPuente.Inventario, "DEMO-INVENTARIO", "DEMO Inventario de materiales", NaturalezaCuenta.Deudora),
        (RolCuentaPuente.Proveedores, "DEMO-PROVEEDORES", "DEMO Proveedores por pagar", NaturalezaCuenta.Acreedora),
        (RolCuentaPuente.Iva, "DEMO-IVA-PEND", "DEMO IVA acreditable pendiente de pago", NaturalezaCuenta.Deudora),
    ];

    /// <summary>Documento DEMO que genera una póliza con partida en la puente. <c>FacturaOrigen</c> = clave de la factura (nota de cargo).</summary>
    private sealed record DocumentoPuenteDemo(
        string Clave, TipoDocumentoPuente Tipo, DateOnly? Fecha, decimal Importe, string? FacturaOrigen = null);

    private sealed record OcPuenteDemo(string Folio, IReadOnlyList<DocumentoPuenteDemo> Documentos);

    /// <summary>Fecha null = hace 61 días respecto a hoy (escenario 03); se resuelve al sembrar.</summary>
    private static readonly OcPuenteDemo[] OcsPuenteDemo =
    [
        new("OC-DEMO-K105-01",
        [
            new("EM-DEMO-K105-01", TipoDocumentoPuente.Recepcion, new DateOnly(2026, 10, 1), 10_000m),
            new("FP-DEMO-K105-01", TipoDocumentoPuente.Factura, new DateOnly(2026, 10, 6), 10_000m),
        ]),
        new("OC-DEMO-K105-02",
        [
            new("FP-DEMO-K105-02", TipoDocumentoPuente.Factura, new DateOnly(2026, 10, 10), 5_000m),
        ]),
        new("OC-DEMO-K105-03",
        [
            new("EM-DEMO-K105-03", TipoDocumentoPuente.Recepcion, null, ImporteRecepcionSinFacturaDemo),
        ]),
        new("OC-DEMO-K105-04",
        [
            new("EM-DEMO-K105-04", TipoDocumentoPuente.Recepcion, new DateOnly(2026, 10, 1), 10_000m),
            new("FP-DEMO-K105-04", TipoDocumentoPuente.Factura, new DateOnly(2026, 10, 6), 10_000m),
            new("DV-DEMO-K105-04", TipoDocumentoPuente.Devolucion, new DateOnly(2026, 10, 12), 2_000m),
            new("NC-DEMO-K105-04", TipoDocumentoPuente.NotaCargo, new DateOnly(2026, 10, 14), 2_000m, FacturaOrigen: "FP-DEMO-K105-04"),
        ]),
    ];

    /// <summary>
    /// Etapa propia, FUERA del bypass multiempresa de las demás: el motor de pólizas toma la empresa del contexto actual (consecutivo
    /// de folio, empresa de la póliza y de sus partidas) y con el bypass activo la dejaría vacía. Igual que VentaActivoWorker, fija un
    /// contexto de empresa explícito y conserva el filtro. Se serializa con el mismo candado del sembrador.
    /// </summary>
    private async Task SembrarCuentaPuenteAsync(CancellationToken cancellationToken)
    {
        using var lockScope = scopes.CreateScope();
        await PostgresAdvisoryLock.ExecuteAsync(
            lockScope.ServiceProvider.GetRequiredService<CompartidoDbContext>(), SeedLockId,
            async ct =>
            {
                using var scope = scopes.CreateScope();
                var sp = scope.ServiceProvider;
                using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(DemoSesionSeedHostedService));
                var accessor = sp.GetRequiredService<IHttpContextAccessor>();
                var anterior = accessor.HttpContext;
                try
                {
                    accessor.HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(MilletClaimTypes.CurrentEmpresaId, EmpresaId.ToString())], nameof(DemoSesionSeedHostedService))),
                    };
                    await SembrarCuentaPuenteEnEmpresaAsync(sp, ct);
                }
                finally { accessor.HttpContext = anterior; }
            }, cancellationToken);
    }

    private async Task SembrarCuentaPuenteEnEmpresaAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ContabilidadDbContext>();
        // Sin la migración K105MovimientosPuente la tabla no existe: no se siembra nada (ni cuentas) y el API arranca igual.
        var tabla = await db.Database
            .SqlQueryRaw<bool>("SELECT (to_regclass('contabilidad.movimientos_puente') IS NOT NULL) AS \"Value\"")
            .SingleAsync(ct);
        if (!tabla)
        {
            logger.LogWarning("DEMO K10.5: falta la tabla contabilidad.movimientos_puente (migración K105MovimientosPuente); no se siembra la cuenta puente.");
            return;
        }

        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var sucursal = await maestros.Sucursales.SingleOrDefaultAsync(s => s.EmpresaId == EmpresaId && s.Clave == "MID", ct);
        // PROV-DEMO-01 = el proveedor DEMO ya validado del sembrador de maestros (DEMO-PROV-ACT), para que el reporte muestre su nombre.
        var proveedor = await maestros.Proveedores.SingleOrDefaultAsync(p => p.Clave == "DEMO-PROV-ACT", ct);
        if (sucursal is null || proveedor is null)
        {
            logger.LogWarning("DEMO K10.5: faltan la sucursal MID o el proveedor DEMO-PROV-ACT; no se siembra la cuenta puente.");
            return;
        }

        var cuentas = await AsegurarCuentasPuenteAsync(sp, db, ct);

        // Tipos de operación K10.9 sembrados por la migración F1Con04: EM recepción, TT factura, SM devolución, PC nota de proveedor.
        var claves = new Dictionary<TipoDocumentoPuente, string>
        {
            [TipoDocumentoPuente.Recepcion] = "EM",
            [TipoDocumentoPuente.Factura] = "TT",
            [TipoDocumentoPuente.Devolucion] = "SM",
            [TipoDocumentoPuente.NotaCargo] = "PC",
        };
        var buscadas = claves.Values.ToArray();
        var existentes = await db.TiposDocumento.AsNoTracking()
            .Where(t => buscadas.Contains(t.Clave) && t.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo)
            .Select(t => t.Clave).ToListAsync(ct);
        if (buscadas.Except(existentes).ToList() is { Count: > 0 } faltan)
        {
            logger.LogWarning("DEMO K10.5: faltan o están inactivos los tipos de operación {Tipos}; no se siembran pólizas de la cuenta puente.",
                string.Join(", ", faltan));
            return;
        }

        var clock = sp.GetRequiredService<IClock>();
        var hoy = FechaContable.HoyLocal(clock.UtcNow);
        foreach (var oc in OcsPuenteDemo)
        {
            try
            {
                foreach (var doc in oc.Documentos)
                {
                    var documentoId = Id("DEMO-K105-DOC/" + doc.Clave);
                    if (await db.MovimientosPuente.AsNoTracking().AnyAsync(m => m.DocumentoId == documentoId && !m.EsReversa, ct)) continue;
                    var fecha = doc.Fecha ?? await FechaHaceDiasAsync(db, hoy, 61, ct);
                    if (fecha is null)
                    {
                        logger.LogWarning("DEMO K10.5: no hay un periodo abierto para la recepción de {Oc}; se omite.", oc.Folio);
                        break;
                    }
                    await RegistrarDocumentoPuenteAsync(oc, doc, fecha.Value, claves[doc.Tipo], cuentas, sucursal.Id, proveedor.Id, ct);
                }
            }
            catch (BusinessRuleException e)
            {
                // Periodo cerrado o no abierto en esta base: la OC queda incompleta y se termina en un reinicio con el periodo abierto.
                logger.LogWarning("DEMO K10.5: no se registró la póliza de {Oc}: {Codigo} {Mensaje}", oc.Folio, e.Code, e.Message);
            }
            catch (ValidationException e)
            {
                logger.LogWarning("DEMO K10.5: las partidas de {Oc} no pasaron la validación contable: {Mensaje}", oc.Folio, e.Message);
            }
        }
    }

    /// <summary>Crea las cuentas DEMO que falten (por código); la puente usa el código configurado si existe en el catálogo.</summary>
    private static async Task<IReadOnlyDictionary<RolCuentaPuente, Guid>> AsegurarCuentasPuenteAsync(
        IServiceProvider sp, ContabilidadDbContext db, CancellationToken ct)
    {
        var codigoPuente = FormatoCatalogo.Codigo(sp.GetRequiredService<IOptions<CuentaPuenteOpciones>>().Value.CodigoCuenta)
            ?? CodigoCuentaPuenteDemo;
        // Cuelgan del título del catálogo DEMO (990.00.00.00) cuando existe; si no, quedan como raíces.
        var titulo = await db.Cuentas.AsNoTracking().Where(c => c.Id == Id("DEMO-CTA-TITULO"))
            .Select(c => new { c.Id, c.Nivel }).FirstOrDefaultAsync(ct);
        var ids = new Dictionary<RolCuentaPuente, Guid>();
        foreach (var (rol, codigoBase, nombre, naturaleza) in CuentasPuenteDemo)
        {
            var codigo = rol == RolCuentaPuente.Puente ? codigoPuente : codigoBase;
            var existente = await db.Cuentas.AsNoTracking().Where(c => c.Codigo == codigo).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (existente is { } id)
            {
                ids[rol] = id;
                continue;
            }
            var cuenta = new CuentaContable(Id("DEMO-K105-CUENTA/" + codigo), codigo, nombre, titulo?.Id, (titulo?.Nivel ?? 0) + 1,
                naturaleza, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null) { EmpresaId = EmpresaId };
            db.Cuentas.Add(cuenta);
            ids[rol] = cuenta.Id;
        }
        await db.SaveChangesAsync(ct);
        return ids;
    }

    /// <summary>
    /// <paramref name="dias"/> antes de hoy si su periodo está abierto. Si no (en la base DEMO los meses 1–9 de 2026 quedan cerrados),
    /// el primer día del periodo abierto más antiguo hasta hoy: la OC llega a «Más de 60» con un corte 61 días después de esa fecha.
    /// </summary>
    private async Task<DateOnly?> FechaHaceDiasAsync(ContabilidadDbContext db, DateOnly hoy, int dias, CancellationToken ct)
    {
        var objetivo = hoy.AddDays(-dias);
        if (await db.Periodos.AsNoTracking().AnyAsync(p => p.Anio == objetivo.Year && p.Numero == objetivo.Month
                && p.Estado == EstadoPeriodo.Abierto, ct))
            return objetivo;
        var inicio = await db.Periodos.AsNoTracking()
            .Where(p => p.Estado == EstadoPeriodo.Abierto && p.Numero <= 12 && p.FechaInicio <= hoy)
            .OrderBy(p => p.FechaInicio).Select(p => (DateOnly?)p.FechaInicio).FirstOrDefaultAsync(ct);
        if (inicio is { } f)
            logger.LogWarning("DEMO K10.5: el periodo de {Objetivo:yyyy-MM-dd} no está abierto; la recepción sin factura se registra el {Fecha:yyyy-MM-dd} (tramo «Más de 60» con corte desde {Corte:yyyy-MM-dd}).",
                objetivo, f, f.AddDays(dias));
        return inicio;
    }

    /// <summary>
    /// Póliza automática del documento (idempotente por llave) y sus movimientos de puente (idempotentes por partida). Cada documento
    /// en su propio scope: un rechazo no deja entidades a medio guardar en el contexto del siguiente.
    /// </summary>
    private async Task RegistrarDocumentoPuenteAsync(OcPuenteDemo oc, DocumentoPuenteDemo doc, DateOnly fecha, string tipoClave,
        IReadOnlyDictionary<RolCuentaPuente, Guid> cuentas, Guid sucursalId, Guid proveedorId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ContabilidadDbContext>();
        var documentoId = Id("DEMO-K105-DOC/" + doc.Clave);
        if (await db.MovimientosPuente.AsNoTracking().AnyAsync(m => m.DocumentoId == documentoId && !m.EsReversa, ct)) return;

        var importe = doc.Importe;
        var iva = decimal.Round(importe * TasaIvaPuenteDemo, 2);
        var (puente, inventario, proveedores, ivaPend) =
            (cuentas[RolCuentaPuente.Puente], cuentas[RolCuentaPuente.Inventario], cuentas[RolCuentaPuente.Proveedores], cuentas[RolCuentaPuente.Iva]);
        (Guid Cuenta, decimal Cargo, decimal Abono)[] partidas = doc.Tipo switch
        {
            // Entrada de mercancías: inventario contra la puente (recibido sin facturar).
            TipoDocumentoPuente.Recepcion => [(inventario, importe, 0m), (puente, 0m, importe)],
            // Factura de proveedor: la puente sin IVA + IVA pendiente contra proveedores.
            TipoDocumentoPuente.Factura => [(puente, importe, 0m), (ivaPend, iva, 0m), (proveedores, 0m, importe + iva)],
            // Devolución física al proveedor (8.B): baja el inventario y regresa la puente.
            TipoDocumentoPuente.Devolucion => [(puente, importe, 0m), (inventario, 0m, importe)],
            // Nota de cargo al proveedor por la devolución: disminuye el pasivo y cierra la puente.
            TipoDocumentoPuente.NotaCargo => [(proveedores, importe + iva, 0m), (puente, 0m, importe), (ivaPend, 0m, iva)],
            _ => throw new InvalidOperationException($"DEMO K10.5: tipo de documento sin plantilla ({doc.Tipo})."),
        };
        var (modulo, tipoOrigen) = doc.Tipo switch
        {
            TipoDocumentoPuente.Recepcion => ("almacen", "recepcion"),
            TipoDocumentoPuente.Devolucion => ("almacen", "devolucion-proveedor"),
            TipoDocumentoPuente.NotaCargo => ("cuentas_por_pagar", "nota-cargo"),
            _ => ("cuentas_por_pagar", "factura"),
        };

        var poliza = await sp.GetRequiredService<IPolizaContablePort>().RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(
            tipoClave, fecha, $"DEMO {doc.Clave} · {oc.Folio}", new DocumentoOrigenPoliza(modulo, tipoOrigen, documentoId, doc.Clave, null),
            $"demo.k105:{doc.Clave}", [.. partidas.Select(p => new PartidaRequest(p.Cuenta, null, p.Cargo, p.Abono, sucursalId))]), ct);

        // La nota de cargo no conoce la OC: se resuelve por la factura de origen, como lo hará CxP (FacturaOrigenId).
        var referencia = doc.FacturaOrigen is { } factura
            ? new ReferenciaPuente(Guid.Empty, null, doc.Tipo, documentoId, Id("DEMO-K105-DOC/" + factura))
            : new ReferenciaPuente(Id("DEMO-K105-OC/" + oc.Folio), null, doc.Tipo, documentoId);
        var fechaPoliza = await db.Polizas.AsNoTracking().Where(p => p.Id == poliza.Id).Select(p => p.Fecha).SingleAsync(ct);
        var registradas = await db.PolizasPartidas.AsNoTracking().Where(x => x.PolizaId == poliza.Id).OrderBy(x => x.Linea).ToListAsync(ct);
        var registro = sp.GetRequiredService<RegistroMovimientosPuente>();
        await registro.RegistrarAsync(EmpresaId, poliza.Id, poliza.Folio, fechaPoliza,
            [.. registradas.Select(x => new PartidaPuente(x.Id, x.Linea, x.CuentaId, x.Cargo, x.Abono, x.SucursalId, proveedorId, x.Moneda,
                x.TipoCambio, x.ImporteOriginal, x.CuentaId == puente ? referencia : null, oc.Folio))], ct);
        await registro.GuardarAsync(ct);
    }
}
