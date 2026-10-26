using System.Globalization;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Facturacion.Application.Reportes.CierreDia;

/// <summary>
/// Conciliación diaria facturado contra ingresado por sucursal y cajero (F6.7,
/// CA6.18 y F6.7-a; Manual de Sucursales §2.4: «Conciliación automática entre
/// el Total Facturado vs. Total Ingresado»).
///
/// <para>
/// <b>Facturado</b>: facturas y anticipos de la sucursal timbrados ese día y
/// vigentes. Las PUE en pesos se esperan cobradas el mismo día por su neto de
/// NC (la caja cobra el neto, Matriz I2). Las PPD (crédito emitido en el
/// punto, comentario de Eliam del 14-oct) y las de otra moneda se listan como
/// informativas: su pago no pasa por la caja.
/// </para>
///
/// <para>
/// <b>Ingresado</b>: los movimientos de cobro (netos de reversas) de las
/// sesiones de caja de la sucursal con ese día de operación — la misma fuente
/// que el arqueo y el corte. Un cobro de un documento que no se facturó ese
/// día (otro día, complemento de pago, cancelado) entra como ingreso sin
/// facturado. Las transferencias se identifican por forma 03 y su referencia;
/// el folio de confirmación de Ingresos llega con F6.6.
/// </para>
///
/// <para>
/// Cada partida queda a cargo de un cajero: el de la sesión que la cobró o,
/// si nadie la cobró, quien la emitió. La diferencia del día es la suma de
/// las diferencias de las partidas (facturado de contado − ingresado).
/// </para>
/// </summary>
public sealed record ConciliacionFacturadoIngresadoQuery(Guid SucursalId, DateOnly Dia, Guid? CajeroId = null)
    : IRequest<ConciliacionFacturadoIngresadoResponse>, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => CierreDia.PermisoTodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    Guid? ISucursalSeleccionadaQuery.SucursalId => SucursalId;
}

public sealed record ConciliacionPartidaFila(
    Guid ComprobanteId,
    string Fecha,
    string Comprobante,
    string Cliente,
    string MetodoPago,
    string Cajero,
    string Situacion,
    decimal Facturado,
    decimal NotasCredito,
    decimal PorCobrar,
    decimal Ingresado,
    decimal Diferencia,
    string? FormasPago);

public sealed record ConciliacionCajeroFila(
    Guid? CajeroId,
    string Cajero,
    int Partidas,
    decimal PorCobrar,
    decimal Ingresado,
    decimal Diferencia,
    string Estado);

public sealed record ConciliacionResumen(
    decimal FacturadoContado,
    int FacturasContado,
    decimal NotasCredito,
    decimal PorCobrar,
    decimal Ingresado,
    decimal IngresadoEfectivo,
    decimal IngresadoTpv,
    decimal IngresadoTransferencia,
    decimal IngresadoOtras,
    decimal Diferencia,
    bool Cuadra,
    decimal FacturadoCredito,
    int FacturasCredito);

public sealed record ConciliacionFacturadoIngresadoResponse(
    string Sucursal,
    DateOnly Dia,
    string? Cajero,
    ConciliacionResumen Resumen,
    ReporteResponse<ConciliacionCajeroFila> PorCajero,
    ReporteResponse<ConciliacionPartidaFila> Partidas);

public sealed class ConciliacionFacturadoIngresadoHandler(
    FacturacionDbContext db,
    IUsuariosReadPort usuarios,
    ISucursalesReadPort sucursales,
    IClock clock) : IRequestHandler<ConciliacionFacturadoIngresadoQuery, ConciliacionFacturadoIngresadoResponse>
{
    public const string Cuadra = "Cuadra";
    public const string FacturadaSinCobro = "Facturada sin cobro";
    public const string CobroParcial = "Cobro parcial";
    public const string CobradaDeMas = "Cobrada de más";
    public const string Credito = "Crédito (PPD) · informativo";
    public const string OtraMoneda = "Otra moneda · informativo";
    public const string CobroComplemento = "Cobro de crédito (complemento de pago)";
    public const string CobroCancelado = "Cobro de documento cancelado";
    public const string CobroOtroDia = "Cobro de documento de otro día";

    private sealed record Cobrado(Guid CajeroId, decimal Importe, List<CajaMovimiento> Movimientos);

    public async Task<ConciliacionFacturadoIngresadoResponse> Handle(
        ConciliacionFacturadoIngresadoQuery query, CancellationToken cancellationToken)
    {
        CierreDia.ExigirSucursal(query.SucursalesPermitidas, query.SucursalId);

        var sucursal = await sucursales.ObtenerNombreAsync(query.SucursalId, cancellationToken)
            ?? query.SucursalId.ToString();
        var zona = await sucursales.ObtenerZonaHorariaAsync(query.SucursalId, cancellationToken);
        var (desde, hasta) = CierreDia.Ventana(query.Dia, zona);
        string Local(DateTimeOffset instante) =>
            DiaOperacion.ALocal(instante, zona).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        // Ingresado: movimientos de cobro de las sesiones del día de la sucursal.
        var sesiones = await db.CajaSesiones.AsNoTracking()
            .Where(s => s.SucursalId == query.SucursalId && s.DiaOperacion == query.Dia)
            .Select(s => new { s.Id, s.ResponsableUsuarioId })
            .ToDictionaryAsync(s => s.Id, s => s.ResponsableUsuarioId, cancellationToken);
        var idsSesiones = sesiones.Keys.ToList();
        var movimientos = await db.CajaMovimientos.AsNoTracking()
            .Where(m => idsSesiones.Contains(m.CajaSesionId) && m.CobroMostradorId != null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
        var idsCobros = movimientos.Select(m => m.CobroMostradorId!.Value).Distinct().ToList();
        var comprobantePorCobro = await db.CobrosMostrador.AsNoTracking()
            .Where(c => idsCobros.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.ComprobanteId, cancellationToken);
        var cobrado = movimientos
            .Where(m => comprobantePorCobro.ContainsKey(m.CobroMostradorId!.Value))
            .GroupBy(m => comprobantePorCobro[m.CobroMostradorId!.Value])
            .ToDictionary(
                g => g.Key,
                g => new Cobrado(sesiones[g.First().CajaSesionId], g.Sum(m => m.Importe), g.ToList()));

        // Facturado: facturas y anticipos vigentes timbrados en el día.
        var facturados = await db.Comprobantes.AsNoTracking()
            .Where(c => c.SucursalId == query.SucursalId && c.Tipo == TipoComprobante.Ingreso
                && (c is FacturaVenta || c is FacturaAnticipo)
                && CierreDia.EstadosVigentes.Contains(c.Estado)
                && c.FechaTimbrado >= desde && c.FechaTimbrado < hasta)
            .ToListAsync(cancellationToken);
        var idsFacturados = facturados.Select(c => c.Id).ToHashSet();
        var idsCobradosSinFacturar = cobrado.Keys.Where(id => !idsFacturados.Contains(id)).ToList();
        var cobradosSinFacturar = await db.Comprobantes.AsNoTracking()
            .Where(c => idsCobradosSinFacturar.Contains(c.Id))
            .ToListAsync(cancellationToken);
        var acreditado = await SaldoPorCobrar.AcreditadoPorFacturaAsync(db, [.. idsFacturados], cancellationToken);

        var partidas = new List<(Guid? CajeroId, Comprobante Doc, string Situacion, decimal Facturado, decimal Nc, decimal PorCobrar, Cobrado? Cobro)>();
        foreach (var f in facturados.OrderBy(f => f.FechaTimbrado))
        {
            var cobro = cobrado.GetValueOrDefault(f.Id);
            var ingresado = cobro?.Importe ?? 0m;
            var nc = acreditado.GetValueOrDefault(f.Id);
            var responsable = cobro?.CajeroId ?? f.UsuarioEmisorId;
            if (f.MetodoPago == "PPD" || f.Moneda != "MXN")
            {
                partidas.Add((responsable, f, f.MetodoPago == "PPD" ? Credito : OtraMoneda, f.Total, nc, 0m, cobro));
                continue;
            }

            var porCobrar = f.Total - nc;
            var situacion = ingresado == porCobrar ? Cuadra
                : ingresado == 0m ? FacturadaSinCobro
                : ingresado < porCobrar ? CobroParcial
                : CobradaDeMas;
            partidas.Add((responsable, f, situacion, f.Total, nc, porCobrar, cobro));
        }
        foreach (var d in cobradosSinFacturar.OrderBy(d => d.FechaTimbrado ?? d.CreatedAt))
        {
            var situacion = d.Tipo == TipoComprobante.Pago ? CobroComplemento
                : !CierreDia.EstadosVigentes.Contains(d.Estado) ? CobroCancelado
                : CobroOtroDia;
            partidas.Add((cobrado[d.Id].CajeroId, d, situacion, 0m, 0m, 0m, cobrado[d.Id]));
        }

        if (query.CajeroId is Guid filtro)
            partidas = partidas.Where(p => p.CajeroId == filtro).ToList();

        var nombres = await usuarios.ObtenerNombresAsync(
            [.. partidas.Select(p => p.CajeroId).OfType<Guid>().Append(query.CajeroId ?? Guid.Empty).Distinct()],
            cancellationToken);
        string NombreCajero(Guid? id) => id is Guid g ? nombres.GetValueOrDefault(g) ?? g.ToString() : "Sin cajero";

        var filas = partidas.Select(p =>
            {
                var ingresado = p.Cobro?.Importe ?? 0m;
                return new ConciliacionPartidaFila(
                    ComprobanteId: p.Doc.Id,
                    Fecha: Local(p.Doc.FechaTimbrado ?? p.Doc.CreatedAt),
                    Comprobante: $"{CierreDia.TipoDocumento(p.Doc)} {p.Doc.Folio}",
                    Cliente: p.Doc.ReceptorNombre,
                    MetodoPago: p.Doc.MetodoPago,
                    Cajero: NombreCajero(p.CajeroId),
                    Situacion: p.Situacion,
                    Facturado: p.Facturado,
                    NotasCredito: p.Nc,
                    PorCobrar: p.PorCobrar,
                    Ingresado: ingresado,
                    Diferencia: p.PorCobrar - ingresado,
                    FormasPago: p.Cobro is null ? null : DescribirFormas(p.Cobro.Movimientos));
            })
            .ToList();

        var movimientosFiltrados = partidas.Where(p => p.Cobro is not null).SelectMany(p => p.Cobro!.Movimientos).ToList();
        var contado = partidas.Where(p => p.Situacion is Cuadra or FacturadaSinCobro or CobroParcial or CobradaDeMas).ToList();
        var credito = partidas.Where(p => p.Situacion == Credito && p.Doc.Moneda == "MXN").ToList();
        var ingresadoTotal = filas.Sum(f => f.Ingresado);
        var diferencia = filas.Sum(f => f.Diferencia);
        var resumen = new ConciliacionResumen(
            FacturadoContado: contado.Sum(p => p.Facturado),
            FacturasContado: contado.Count,
            NotasCredito: contado.Sum(p => p.Nc),
            PorCobrar: contado.Sum(p => p.PorCobrar),
            Ingresado: ingresadoTotal,
            IngresadoEfectivo: movimientosFiltrados.Where(m => m.FormaPago == "01").Sum(m => m.Importe),
            IngresadoTpv: movimientosFiltrados.Where(m => CierreDia.FormasTpv.Contains(m.FormaPago)).Sum(m => m.Importe),
            IngresadoTransferencia: movimientosFiltrados.Where(m => m.FormaPago == "03").Sum(m => m.Importe),
            IngresadoOtras: movimientosFiltrados
                .Where(m => m.FormaPago is not ("01" or "03") && !CierreDia.FormasTpv.Contains(m.FormaPago))
                .Sum(m => m.Importe),
            Diferencia: diferencia,
            Cuadra: diferencia == 0m,
            FacturadoCredito: credito.Sum(p => p.Facturado),
            FacturasCredito: credito.Count);

        var porCajero = partidas.Zip(filas)
            .GroupBy(x => x.First.CajeroId, x => x.Second)
            .Select(g => new ConciliacionCajeroFila(
                g.Key, NombreCajero(g.Key), g.Count(), g.Sum(f => f.PorCobrar), g.Sum(f => f.Ingresado),
                g.Sum(f => f.Diferencia), g.Sum(f => f.Diferencia) == 0m ? Cuadra : "Con diferencia"))
            .OrderBy(f => f.Cajero, StringComparer.CurrentCulture)
            .ToList();

        var cajero = query.CajeroId is Guid id ? NombreCajero(id) : null;
        var filtros = new Dictionary<string, string?>
        {
            ["Sucursal"] = sucursal,
            ["Día"] = query.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Cajero"] = cajero ?? "Todos",
            ["Facturado de contado (neto de NC)"] = CierreDia.Moneda(resumen.PorCobrar),
            ["Ingresado"] = CierreDia.Moneda(resumen.Ingresado),
            ["Diferencia"] = resumen.Cuadra ? "Cuadra" : CierreDia.Moneda(resumen.Diferencia),
            ["Crédito emitido (informativo)"] = $"{CierreDia.Moneda(resumen.FacturadoCredito)} ({resumen.FacturasCredito})",
            ["Transferencias"] = "Por forma de pago 03 y su referencia (folio de Ingresos con F6.6)",
        };

        var reportePartidas = new ReporteResponse<ConciliacionPartidaFila>(
            Titulo: $"Facturado contra ingresado · {sucursal} · {query.Dia:yyyy-MM-dd}",
            GeneradoEn: clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas:
            [
                new("fecha", "Fecha y hora", "texto"),
                new("comprobante", "Comprobante", "texto"),
                new("cliente", "Cliente", "texto"),
                new("metodoPago", "Método", "texto"),
                new("cajero", "Cajero", "texto"),
                new("situacion", "Situación", "texto"),
                new("formasPago", "Formas de pago", "texto"),
                new("facturado", "Facturado", "moneda", "derecha"),
                new("notasCredito", "NC", "moneda", "derecha"),
                new("porCobrar", "Por cobrar", "moneda", "derecha"),
                new("ingresado", "Ingresado", "moneda", "derecha"),
                new("diferencia", "Diferencia", "moneda", "derecha"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal>
            {
                ["porCobrar"] = resumen.PorCobrar,
                ["ingresado"] = resumen.Ingresado,
                ["diferencia"] = resumen.Diferencia,
            });

        var reporteCajeros = new ReporteResponse<ConciliacionCajeroFila>(
            Titulo: $"Facturado contra ingresado por cajero · {sucursal} · {query.Dia:yyyy-MM-dd}",
            GeneradoEn: clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas:
            [
                new("cajero", "Cajero", "texto"),
                new("partidas", "Partidas", "numero", "derecha"),
                new("porCobrar", "Facturado de contado", "moneda", "derecha"),
                new("ingresado", "Ingresado", "moneda", "derecha"),
                new("diferencia", "Diferencia", "moneda", "derecha"),
                new("estado", "Estado", "texto"),
            ],
            Filas: porCajero,
            Totales: new Dictionary<string, decimal>
            {
                ["porCobrar"] = resumen.PorCobrar,
                ["ingresado"] = resumen.Ingresado,
                ["diferencia"] = resumen.Diferencia,
            });

        return new ConciliacionFacturadoIngresadoResponse(sucursal, query.Dia, cajero, resumen, reporteCajeros, reportePartidas);
    }

    private static string DescribirFormas(IEnumerable<CajaMovimiento> movimientos) =>
        string.Join(" · ", movimientos
            .GroupBy(m => m.FormaPago)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var referencias = g.Select(m => m.Referencia).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
                var texto = $"{g.Key} {CierreDia.Moneda(g.Sum(m => m.Importe))}";
                return referencias.Count == 0 ? texto : $"{texto} (ref. {string.Join(", ", referencias)})";
            }));
}
