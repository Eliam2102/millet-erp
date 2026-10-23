using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Reportes.CorteCaja;

/// <summary>
/// Corte de caja de una sesión (FIX-12; Manual de Sucursales §2.4, R18,
/// CA6.10; demo del 12-oct, M19): lo que la cajera entrega al cerrar — qué se
/// facturó, las notas de crédito, las cobranzas, los apartados (anticipos) y
/// la TPV — con el nombre del cajero.
///
/// <para>
/// <b>Facturado y NC del punto de venta.</b> Son los comprobantes timbrados
/// durante la sesión (de la apertura al cierre, o hasta ahora si sigue
/// abierta) que pertenecen a esta caja: los que se cobraron en ella
/// (<c>Comprobante.CajaId</c>), los que emitió el propio cajero, o los que no
/// tienen caja y caen en su alcance (sucursal de la sesión × canales de la
/// caja, `[Decisión 12-A]`/§4.1). La ventana termina, a más tardar, al final
/// del día de operación (una sesión extemporánea no toma lo del día siguiente). Se listan también los cancelados y los que
/// quedaron sin timbrar, con su estado; los totales suman solo los vigentes
/// en pesos. Dos cajas con el mismo alcance ven los mismos documentos sin
/// caja (mismo criterio que las bandejas, `[Decisión 12-B]`).
/// </para>
///
/// <para>
/// Las filas son los <c>caja_movimiento</c> de la sesión: la misma fuente que
/// el arqueo, así que la suma por forma de pago del corte es exactamente el
/// esperado del arqueo (sin duplicar ni omitir). Cada cobro se enriquece con
/// su comprobante; el total del CFDI y sus NC aparecen una sola vez por cobro
/// y son informativos (no suman al efectivo: la caja cobra el neto, Matriz I2).
/// La conciliación facturado contra ingresado y el monitor por sucursal viven
/// en F6.7.
/// </para>
/// </summary>
public sealed record CorteCajaSesionQuery(Guid SesionId) : IRequest<CorteCajaResponse>;

public sealed record CorteCajaFila(
    string Fecha,
    string Tipo,
    string FormaPago,
    string? Comprobante,
    string? Cliente,
    string? Origen,
    string? Referencia,
    decimal? TotalComprobante,
    decimal? NotasCredito,
    decimal Importe,
    string? EstadoCobro);

public sealed record CorteCajaEncabezado(
    Guid SesionId,
    string Caja,
    string Sucursal,
    Guid CajeroUsuarioId,
    string Cajero,
    string Estado,
    DateOnly DiaOperacion,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    decimal FondoApertura,
    decimal? EfectivoEsperado,
    decimal? EfectivoContado,
    decimal? Diferencia,
    bool CierreExtemporaneo,
    string? NotasCierre);

public sealed record CorteCajaFormaPago(
    string FormaPago,
    string Descripcion,
    int Cobros,
    decimal MontoSistema,
    decimal? MontoDeclarado);

/// <summary>Factura o anticipo emitido en la sesión (sección «Facturado»).</summary>
public sealed record CorteCajaFacturaFila(
    string Fecha,
    string Comprobante,
    string Cliente,
    string MetodoPago,
    string Moneda,
    string EstadoCfdi,
    string Cobro,
    decimal Total);

/// <summary>Nota de crédito emitida en la sesión (sección «Notas de crédito»).</summary>
public sealed record CorteCajaNotaCreditoFila(
    string Fecha,
    string Folio,
    string Motivo,
    string? FacturaRelacionada,
    string Cliente,
    string Moneda,
    string EstadoCfdi,
    decimal Total);

/// <summary>
/// Totales de lo que entrega la cajera (M19). Facturado y NC suman solo
/// comprobantes vigentes en pesos; cobranzas, apartados y TPV salen de los
/// movimientos de la sesión (netos de reversas).
/// </summary>
public sealed record CorteCajaResumen(
    decimal Facturado,
    int Facturas,
    decimal NotasCredito,
    int Notas,
    decimal FacturadoNeto,
    decimal Cobranzas,
    decimal Apartados,
    decimal Tpv,
    decimal TotalIngresado);

public sealed record CorteCajaResponse(
    CorteCajaEncabezado Encabezado,
    IReadOnlyList<CorteCajaFormaPago> PorFormaPago,
    ReporteResponse<CorteCajaFila> Reporte,
    CorteCajaResumen Resumen,
    ReporteResponse<CorteCajaFacturaFila> Facturado,
    ReporteResponse<CorteCajaNotaCreditoFila> NotasCredito);

public sealed class CorteCajaSesionHandler : IRequestHandler<CorteCajaSesionQuery, CorteCajaResponse>
{
    private const string Supervisar = "facturacion.caja.supervisar";
    private const string Administrar = "facturacion.caja.administrar";
    private const string Liquidar = "facturacion.caja.liquidar";

    /// <summary>Nombres SAT (c_FormaPago) de las formas que opera la caja.</summary>
    private static readonly Dictionary<string, string> NombresFormaPago = new(StringComparer.Ordinal)
    {
        ["01"] = "Efectivo",
        ["02"] = "Cheque nominativo",
        ["03"] = "Transferencia electrónica",
        ["04"] = "Tarjeta de crédito",
        ["28"] = "Tarjeta de débito",
        ["29"] = "Tarjeta de servicios",
    };

    /// <summary>Formas de pago que pasan por la terminal bancaria (TPV).</summary>
    private static readonly HashSet<string> FormasTpv = new(StringComparer.Ordinal) { "04", "28", "29" };

    /// <summary>Estados que cuentan como facturado: timbrado y aún no cancelado ante el SAT.</summary>
    private static readonly EstadoTimbrado[] EstadosVigentes =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente];

    /// <summary>Comprobantes que se intentaron emitir y no tienen timbre (se listan, no suman).</summary>
    private static readonly EstadoTimbrado[] EstadosSinTimbrar =
    [
        EstadoTimbrado.Borrador, EstadoTimbrado.PendientePedimento,
        EstadoTimbrado.TimbradoEnProceso, EstadoTimbrado.TimbradoFallido,
    ];

    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserContext _user;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuariosReadPort _usuarios;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IClock _clock;

    public CorteCajaSesionHandler(
        FacturacionDbContext db,
        ICurrentUserContext user,
        ICurrentUserPermissions permisos,
        IUsuariosReadPort usuarios,
        ISucursalesReadPort sucursales,
        IClock clock)
    {
        _db = db;
        _user = user;
        _permisos = permisos;
        _usuarios = usuarios;
        _sucursales = sucursales;
        _clock = clock;
    }

    public async Task<CorteCajaResponse> Handle(CorteCajaSesionQuery query, CancellationToken cancellationToken)
    {
        var sesion = await _db.CajaSesiones.AsNoTracking()
            .Include(s => s.Cortes)
            .FirstOrDefaultAsync(s => s.Id == query.SesionId, cancellationToken)
            ?? throw new EntityNotFoundException("SESION_NO_ENCONTRADA", $"No existe la sesión '{query.SesionId}'.");

        await ExigirAccesoAsync(sesion, cancellationToken);

        var cajaNombre = await _db.Cajas.AsNoTracking()
            .Where(c => c.Id == sesion.CajaId)
            .Select(c => c.Nombre)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var sucursal = await _sucursales.ObtenerNombreAsync(sesion.SucursalId, cancellationToken)
            ?? sesion.SucursalId.ToString();
        // Las horas del corte se leen en la zona de la sucursal de operación
        // (`[Decisión 12-8]`), igual que el día de operación.
        var zona = await _sucursales.ObtenerZonaHorariaAsync(sesion.SucursalId, cancellationToken);
        string Local(DateTimeOffset instante) =>
            DiaOperacion.ALocal(instante, zona).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var nombres = await _usuarios.ObtenerNombresAsync([sesion.ResponsableUsuarioId], cancellationToken);
        var cajero = nombres.GetValueOrDefault(sesion.ResponsableUsuarioId) ?? sesion.ResponsableUsuarioId.ToString();

        var movimientos = await _db.CajaMovimientos.AsNoTracking()
            .Where(m => m.CajaSesionId == sesion.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var cobroIds = movimientos
            .Where(m => m.CobroMostradorId != null)
            .Select(m => m.CobroMostradorId!.Value)
            .Distinct()
            .ToList();
        var cobros = await _db.CobrosMostrador.AsNoTracking()
            .Where(c => cobroIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var comprobanteIds = cobros.Values.Select(c => c.ComprobanteId).Distinct().ToList();
        var comprobantes = await _db.Comprobantes.AsNoTracking()
            .Where(c => comprobanteIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var importesPago = await _db.RecibosPago.AsNoTracking()
            .Where(r => comprobanteIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.ImporteTotalPago, cancellationToken);
        var acreditado = await SaldoPorCobrar.AcreditadoPorFacturaAsync(_db, comprobanteIds, cancellationToken);

        // El total del CFDI y sus NC van una sola vez por cobro (en su primer
        // movimiento de tipo CobroCliente): son informativos y repetirlos por
        // forma de pago haría que se leyeran como dinero duplicado.
        var cobrosYaDetallados = new HashSet<Guid>();
        var filas = new List<CorteCajaFila>(movimientos.Count);
        foreach (var m in movimientos)
        {
            CobroMostrador? cobro = null;
            Comprobante? comprobante = null;
            if (m.CobroMostradorId is Guid cobroId && cobros.TryGetValue(cobroId, out cobro))
                comprobantes.TryGetValue(cobro.ComprobanteId, out comprobante);

            decimal? totalComprobante = null;
            decimal? notasCredito = null;
            if (comprobante is not null && m.Tipo == TipoCajaMovimiento.CobroCliente
                && cobrosYaDetallados.Add(cobro!.Id))
            {
                if (comprobante.Tipo == TipoComprobante.Pago)
                {
                    totalComprobante = importesPago.GetValueOrDefault(comprobante.Id);
                }
                else
                {
                    totalComprobante = comprobante.Total;
                    var nc = acreditado.GetValueOrDefault(comprobante.Id);
                    notasCredito = nc == 0m ? null : nc;
                }
            }

            filas.Add(new CorteCajaFila(
                Fecha: Local(m.Tipo == TipoCajaMovimiento.CobroCliente && cobro is not null ? cobro.FechaCobro : m.CreatedAt),
                Tipo: NombreTipo(m.Tipo),
                FormaPago: NombreFormaPago(m.FormaPago),
                Comprobante: comprobante is null ? null : $"{NombreComprobante(comprobante)} {comprobante.Folio}",
                Cliente: comprobante?.ReceptorNombre,
                Origen: cobro is null ? null : NombreOrigen(cobro.Origen),
                Referencia: m.Referencia,
                TotalComprobante: totalComprobante,
                NotasCredito: notasCredito,
                Importe: m.Importe,
                EstadoCobro: cobro?.Estado == EstadoCobroMostrador.Cancelado ? "Cancelado" : null));
        }

        var declarados = sesion.Cortes.ToDictionary(c => c.FormaPago, c => c.MontoDeclarado, StringComparer.Ordinal);
        var porForma = movimientos
            .GroupBy(m => m.FormaPago)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CorteCajaFormaPago(
                g.Key,
                NombresFormaPago.GetValueOrDefault(g.Key, g.Key),
                g.Where(m => m.Tipo == TipoCajaMovimiento.CobroCliente)
                    .Select(m => m.CobroMostradorId).Distinct().Count(),
                g.Sum(m => m.Importe),
                declarados.GetValueOrDefault(g.Key)))
            .ToList();

        // Cobranzas, apartados y TPV: dinero que entró a la caja por cobros
        // (con sus reversas y ajustes), separado por tipo de comprobante.
        var movimientosCobro = movimientos
            .Where(m => m.CobroMostradorId is Guid id && cobros.ContainsKey(id))
            .Select(m => (Movimiento: m, Comprobante: comprobantes.GetValueOrDefault(cobros[m.CobroMostradorId!.Value].ComprobanteId)))
            .ToList();
        var apartados = movimientosCobro.Where(x => x.Comprobante is FacturaAnticipo).Sum(x => x.Movimiento.Importe);
        var cobranzas = movimientosCobro.Where(x => x.Comprobante is not FacturaAnticipo).Sum(x => x.Movimiento.Importe);
        var tpv = movimientosCobro.Where(x => FormasTpv.Contains(x.Movimiento.FormaPago)).Sum(x => x.Movimiento.Importe);

        var (facturado, notas) = await ComprobantesDeLaSesionAsync(sesion, zona, Local, cancellationToken);
        var facturasVigentes = facturado.Filas.Where(EsVigenteEnPesos).ToList();
        var notasVigentes = notas.Filas.Where(EsVigenteEnPesos).ToList();
        var resumen = new CorteCajaResumen(
            Facturado: facturasVigentes.Sum(f => f.Total),
            Facturas: facturasVigentes.Count,
            NotasCredito: notasVigentes.Sum(n => n.Total),
            Notas: notasVigentes.Count,
            FacturadoNeto: facturasVigentes.Sum(f => f.Total) - notasVigentes.Sum(n => n.Total),
            Cobranzas: cobranzas,
            Apartados: apartados,
            Tpv: tpv,
            TotalIngresado: cobranzas + apartados);

        var encabezado = new CorteCajaEncabezado(
            sesion.Id, cajaNombre, sucursal, sesion.ResponsableUsuarioId, cajero, sesion.Estado.ToString(),
            sesion.DiaOperacion, sesion.FechaApertura, sesion.FechaCierre, sesion.FondoApertura,
            sesion.EfectivoTeorico, sesion.EfectivoDeclarado, sesion.Diferencia,
            sesion.CierreExtemporaneo, sesion.NotasCierre);

        var filtros = new Dictionary<string, string?>
        {
            ["Caja"] = cajaNombre,
            ["Sucursal"] = sucursal,
            ["Cajero"] = cajero,
            ["Día de operación"] = sesion.DiaOperacion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Estado"] = NombreEstado(sesion.Estado) + (sesion.CierreExtemporaneo ? " (extemporáneo)" : string.Empty),
            ["Apertura"] = Local(sesion.FechaApertura),
            ["Cierre"] = sesion.FechaCierre is DateTimeOffset cierre ? Local(cierre) : null,
            ["Fondo de apertura"] = Moneda(sesion.FondoApertura),
        };
        foreach (var f in porForma)
        {
            filtros[$"{f.Descripcion} ({f.FormaPago})"] = f.MontoDeclarado is decimal declarado
                ? $"{Moneda(f.MontoSistema)} · contado {Moneda(declarado)}"
                : Moneda(f.MontoSistema);
        }
        if (sesion.Diferencia is decimal diferencia)
            filtros["Diferencia en efectivo"] = Moneda(diferencia);
        filtros["Facturado"] = $"{Moneda(resumen.Facturado)} ({resumen.Facturas})";
        filtros["Notas de crédito"] = $"{Moneda(resumen.NotasCredito)} ({resumen.Notas})";
        filtros["Cobranzas"] = Moneda(resumen.Cobranzas);
        filtros["Apartados (anticipos)"] = Moneda(resumen.Apartados);
        filtros["TPV (tarjetas)"] = Moneda(resumen.Tpv);

        var reporte = new ReporteResponse<CorteCajaFila>(
            Titulo: $"Corte de caja · {cajaNombre} · {sesion.DiaOperacion:yyyy-MM-dd}",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas:
            [
                new("fecha", "Fecha y hora", "texto"),
                new("tipo", "Movimiento", "texto"),
                new("formaPago", "Forma de pago", "texto"),
                new("comprobante", "Comprobante", "texto"),
                new("cliente", "Cliente", "texto"),
                new("origen", "Origen", "texto"),
                new("referencia", "Referencia / voucher", "texto"),
                new("totalComprobante", "Total CFDI", "moneda", "derecha"),
                new("notasCredito", "NC acreditadas", "moneda", "derecha"),
                new("importe", "Importe en caja", "moneda", "derecha"),
                new("estadoCobro", "Estado del cobro", "texto"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal> { ["importe"] = filas.Sum(f => f.Importe) });

        return new CorteCajaResponse(encabezado, porForma, reporte, resumen,
            Titular(facturado, $"Facturado · {cajaNombre} · {sesion.DiaOperacion:yyyy-MM-dd}", filtros),
            Titular(notas, $"Notas de crédito · {cajaNombre} · {sesion.DiaOperacion:yyyy-MM-dd}", filtros));
    }

    /// <summary>
    /// Facturas, anticipos y NC emitidos en la ventana de la sesión que
    /// pertenecen a esta caja (ver el resumen de la clase).
    /// </summary>
    private async Task<(ReporteResponse<CorteCajaFacturaFila> Facturado, ReporteResponse<CorteCajaNotaCreditoFila> NotasCredito)>
        ComprobantesDeLaSesionAsync(
            CajaSesion sesion, string? zona, Func<DateTimeOffset, string> local, CancellationToken cancellationToken)
    {
        // La ventana nunca pasa del día de operación: una sesión que se quedó
        // abierta y se cierra extemporánea (§5.2) no se lleva lo del día
        // siguiente, que pertenece a la sesión nueva.
        var desde = sesion.FechaApertura;
        var cierre = sesion.FechaCierre ?? _clock.UtcNow;
        var finDelDia = DiaOperacion.FinDelDia(sesion.DiaOperacion, zona);
        var hasta = cierre < finDelDia ? cierre : finDelDia;

        var canales = await _db.Cajas.AsNoTracking()
            .Where(c => c.Id == sesion.CajaId)
            .SelectMany(c => c.Canales.Select(x => x.CanalVentaId))
            .ToListAsync(cancellationToken);
        var alcance = AlcanceCajas.De(canales.Count == 0
            ? [new CombinacionAlcance(sesion.SucursalId, null)]
            : [.. canales.Select(c => new CombinacionAlcance(sesion.SucursalId, c))]);

        var cajaId = sesion.CajaId;
        var cajero = sesion.ResponsableUsuarioId;
        var enVentana = _db.Comprobantes.AsNoTracking()
            .Where(c => c.Estado != EstadoTimbrado.Descartada
                && ((c.FechaTimbrado != null && c.FechaTimbrado >= desde && c.FechaTimbrado <= hasta)
                    || (c.FechaTimbrado == null && EstadosSinTimbrar.Contains(c.Estado)
                        && c.CreatedAt >= desde && c.CreatedAt <= hasta)));
        var deLaCaja = enVentana.Where(c => c.CajaId == cajaId || c.UsuarioEmisorId == cajero);
        var delAlcance = alcance.AplicarA(enVentana.Where(c => c.CajaId == null));

        var documentos = (await deLaCaja.ToListAsync(cancellationToken))
            .Concat(await delAlcance.ToListAsync(cancellationToken))
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.FechaTimbrado ?? c.CreatedAt)
            .ToList();

        var facturas = documentos
            .Where(c => c.Tipo == TipoComprobante.Ingreso && c is FacturaVenta or FacturaAnticipo)
            .ToList();
        var notas = documentos.OfType<NotaCredito>().ToList();

        var idsFacturas = facturas.Select(f => f.Id).ToList();
        var cobrosVigentes = await _db.CobrosMostrador.AsNoTracking()
            .Where(c => idsFacturas.Contains(c.ComprobanteId) && c.Estado != EstadoCobroMostrador.Cancelado)
            .Select(c => new { c.ComprobanteId, c.CajaSesionId })
            .ToListAsync(cancellationToken);

        var idsRelacionadas = notas.Where(n => n.FacturaRelacionadaId != null)
            .Select(n => n.FacturaRelacionadaId!.Value).Distinct().ToList();
        var foliosRelacionados = await _db.Comprobantes.AsNoTracking()
            .Where(c => idsRelacionadas.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Folio, cancellationToken);

        var filasFacturas = facturas.Select(f => new CorteCajaFacturaFila(
                Fecha: local(f.FechaTimbrado ?? f.CreatedAt),
                Comprobante: $"{(f is FacturaAnticipo ? "Anticipo" : "Factura")} {f.Folio}",
                Cliente: f.ReceptorNombre,
                MetodoPago: f.MetodoPago,
                Moneda: f.Moneda,
                EstadoCfdi: NombreEstadoCfdi(f.Estado),
                Cobro: EstadoDeCobro(f, cobrosVigentes.Where(c => c.ComprobanteId == f.Id).Select(c => c.CajaSesionId), sesion.Id),
                Total: f.Total))
            .ToList();
        var filasNotas = notas.Select(n => new CorteCajaNotaCreditoFila(
                Fecha: local(n.FechaTimbrado ?? n.CreatedAt),
                Folio: n.Folio,
                Motivo: NombreMotivo(n.Motivo),
                FacturaRelacionada: n.FacturaRelacionadaId is Guid id ? foliosRelacionados.GetValueOrDefault(id) : null,
                Cliente: n.ReceptorNombre,
                Moneda: n.Moneda,
                EstadoCfdi: NombreEstadoCfdi(n.Estado),
                Total: n.Total))
            .ToList();

        var facturado = new ReporteResponse<CorteCajaFacturaFila>(
            Titulo: string.Empty,
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>(),
            Columnas:
            [
                new("fecha", "Fecha y hora", "texto"),
                new("comprobante", "Comprobante", "texto"),
                new("cliente", "Cliente", "texto"),
                new("metodoPago", "Método", "texto"),
                new("moneda", "Moneda", "texto"),
                new("estadoCfdi", "Estado del CFDI", "texto"),
                new("cobro", "Cobro", "texto"),
                new("total", "Total", "moneda", "derecha"),
            ],
            Filas: filasFacturas,
            Totales: new Dictionary<string, decimal> { ["total"] = filasFacturas.Where(EsVigenteEnPesos).Sum(f => f.Total) });
        var notasCredito = new ReporteResponse<CorteCajaNotaCreditoFila>(
            Titulo: string.Empty,
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>(),
            Columnas:
            [
                new("fecha", "Fecha y hora", "texto"),
                new("folio", "Folio", "texto"),
                new("motivo", "Motivo", "texto"),
                new("facturaRelacionada", "Factura relacionada", "texto"),
                new("cliente", "Cliente", "texto"),
                new("moneda", "Moneda", "texto"),
                new("estadoCfdi", "Estado del CFDI", "texto"),
                new("total", "Total", "moneda", "derecha"),
            ],
            Filas: filasNotas,
            Totales: new Dictionary<string, decimal> { ["total"] = filasNotas.Where(EsVigenteEnPesos).Sum(n => n.Total) });

        return (facturado, notasCredito);
    }

    /// <summary>Copia el encabezado (caja, cajero, sucursal, horario) del corte a una sección.</summary>
    private static ReporteResponse<T> Titular<T>(
        ReporteResponse<T> seccion, string titulo, Dictionary<string, string?> filtrosCorte)
    {
        string[] claves = ["Caja", "Sucursal", "Cajero", "Día de operación", "Apertura", "Cierre"];
        var filtros = claves.Where(filtrosCorte.ContainsKey).ToDictionary(c => c, c => filtrosCorte[c]);
        filtros["Totales"] = "Solo comprobantes vigentes en pesos";
        return seccion with { Titulo = titulo, FiltrosAplicados = filtros };
    }

    private static bool EsVigenteEnPesos(CorteCajaFacturaFila f) => EsVigenteEnPesos(f.EstadoCfdi, f.Moneda);

    private static bool EsVigenteEnPesos(CorteCajaNotaCreditoFila n) => EsVigenteEnPesos(n.EstadoCfdi, n.Moneda);

    private static bool EsVigenteEnPesos(string estado, string moneda) =>
        moneda == "MXN" && EstadosVigentes.Any(e => NombreEstadoCfdi(e) == estado);

    private static string EstadoDeCobro(Comprobante factura, IEnumerable<Guid> sesionesCobro, Guid sesionId)
    {
        var sesiones = sesionesCobro.ToList();
        if (sesiones.Contains(sesionId))
            return "Cobrada en esta sesión";
        if (sesiones.Count > 0)
            return "Cobrada en otra sesión";
        if (!EstadosVigentes.Contains(factura.Estado))
            return "—";
        return factura.MetodoPago == "PPD" ? "Crédito (PPD)" : "Por cobrar";
    }

    private static string NombreEstadoCfdi(EstadoTimbrado estado) => estado switch
    {
        EstadoTimbrado.Timbrado => "Timbrado",
        EstadoTimbrado.CancelacionPendiente => "Cancelación en trámite",
        EstadoTimbrado.Cancelado => "Cancelado",
        EstadoTimbrado.TimbradoFallido => "Sin timbrar (error del PAC)",
        _ => "Sin timbrar",
    };

    private static string NombreMotivo(MotivoNotaCredito motivo) => motivo switch
    {
        MotivoNotaCredito.Amortizacion => "Aplicación de anticipo",
        MotivoNotaCredito.Bonificacion => "Bonificación",
        MotivoNotaCredito.Devolucion => "Devolución",
        MotivoNotaCredito.Ranura => "Ranura",
        _ => motivo.ToString(),
    };

    /// <summary>
    /// El cajero responsable ve su propio corte; los demás requieren
    /// supervisar, administrar o liquidar caja (OR resuelto en el handler,
    /// mismo criterio que el detalle de sesión).
    /// </summary>
    private async Task ExigirAccesoAsync(CajaSesion sesion, CancellationToken cancellationToken)
    {
        if (_user.UserId is Guid usuarioId && usuarioId == sesion.ResponsableUsuarioId)
            return;
        if (await _permisos.TieneAsync(Supervisar, cancellationToken)
            || await _permisos.TieneAsync(Administrar, cancellationToken)
            || await _permisos.TieneAsync(Liquidar, cancellationToken))
            return;

        throw new ForbiddenException(
            "CORTE_PERMISO_INSUFICIENTE",
            "Solo el cajero de la sesión o quien supervisa, administra o liquida cajas puede consultar el corte.");
    }

    private static string NombreFormaPago(string clave) =>
        NombresFormaPago.TryGetValue(clave, out var nombre) ? $"{clave} · {nombre}" : clave;

    private static string NombreTipo(TipoCajaMovimiento tipo) => tipo switch
    {
        TipoCajaMovimiento.FondoApertura => "Fondo de apertura",
        TipoCajaMovimiento.CobroCliente => "Cobro",
        TipoCajaMovimiento.Deposito => "Depósito",
        TipoCajaMovimiento.Retiro => "Retiro",
        TipoCajaMovimiento.AjusteCorreccion => "Ajuste de sesión anterior",
        TipoCajaMovimiento.ReversaCobro => "Reversa de cobro",
        _ => tipo.ToString(),
    };

    private static string NombreComprobante(Comprobante comprobante) => comprobante switch
    {
        FacturaAnticipo => "Anticipo",
        _ when comprobante.Tipo == TipoComprobante.Pago => "Complemento de pago",
        _ => "Factura",
    };

    private static string NombreOrigen(OrigenCobroMostrador origen) => origen switch
    {
        OrigenCobroMostrador.LiquidacionRuta => "Liquidación de ruta",
        _ => "Mostrador",
    };

    private static string NombreEstado(EstadoCajaSesion estado) => estado switch
    {
        EstadoCajaSesion.Abierta => "Abierta",
        EstadoCajaSesion.EnArqueo => "En arqueo",
        EstadoCajaSesion.Cerrada => "Cerrada",
        _ => estado.ToString(),
    };

    private static string Moneda(decimal importe) =>
        importe.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
}
