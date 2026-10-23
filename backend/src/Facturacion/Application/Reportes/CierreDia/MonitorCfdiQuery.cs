using System.Globalization;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Facturacion.Application.Reportes.CierreDia;

/// <summary>
/// Monitor de estatus de CFDI por sucursal y día (F6.7, CA6.18; Manual de
/// Sucursales §2.4: «Facturas y Notas de Crédito: Timbradas, Canceladas con
/// timbre, Sin timbrar»).
///
/// <para>
/// Entran los comprobantes de la sucursal timbrados ese día, los que se
/// intentaron emitir ese día y quedaron sin timbre (incluidos los fallidos del
/// PAC) y los timbrados antes cuya cancelación el SAT aceptó ese día. Cada
/// uno trae su estado interno y el último estatus consultado al SAT
/// (<see cref="VerificarEstatusSatCommand"/>); una diferencia entre ambos se
/// marca como discrepancia. Aparte, los pedidos facturables de la sucursal que
/// al cierre del día seguían sin timbrar.
/// </para>
///
/// <para>
/// Con <see cref="CajeroId"/> se limita a lo que emitió ese cajero o se cobró
/// en sus sesiones del día; los pedidos no tienen cajero y se listan igual.
/// </para>
/// </summary>
public sealed record MonitorCfdiQuery(Guid SucursalId, DateOnly Dia, Guid? CajeroId = null)
    : IRequest<MonitorCfdiResponse>, ISucursalSeleccionadaQuery
{
    public string PermisoTodasSucursales => CierreDia.PermisoTodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    Guid? ISucursalSeleccionadaQuery.SucursalId => SucursalId;
}

public sealed record MonitorCfdiFila(
    Guid ComprobanteId,
    string Fecha,
    string Tipo,
    string Folio,
    string? Uuid,
    string Cliente,
    string MetodoPago,
    string Moneda,
    decimal Total,
    string Estado,
    string EstatusSat,
    string? EstatusSatConsultadoEn,
    bool Discrepancia,
    string? Cajero,
    string? Detalle);

public sealed record MonitorPedidoFila(
    Guid PedidoId,
    string Pedido,
    string Origen,
    string Cliente,
    string Estado,
    string Recibido,
    decimal Total);

public sealed record MonitorCfdiResumen(
    int Timbrados,
    int Cancelados,
    int CancelacionEnTramite,
    int SinTimbrar,
    int Fallidos,
    int PedidosSinTimbrar,
    int SinVerificarSat,
    int Discrepancias,
    decimal TotalVigente);

public sealed record MonitorCfdiResponse(
    string Sucursal,
    DateOnly Dia,
    string? Cajero,
    string FuenteSat,
    MonitorCfdiResumen Resumen,
    ReporteResponse<MonitorCfdiFila> Comprobantes,
    ReporteResponse<MonitorPedidoFila> PedidosSinTimbrar);

public sealed class MonitorCfdiHandler(
    FacturacionDbContext db,
    IUsuariosReadPort usuarios,
    ISucursalesReadPort sucursales,
    IConsultaEstatusCfdiPort estatusSat,
    IClock clock) : IRequestHandler<MonitorCfdiQuery, MonitorCfdiResponse>
{
    private static readonly EstadoPedidoFacturable[] PedidosPendientes =
        [EstadoPedidoFacturable.Importado, EstadoPedidoFacturable.Bloqueado, EstadoPedidoFacturable.Excepcion];

    public async Task<MonitorCfdiResponse> Handle(MonitorCfdiQuery query, CancellationToken cancellationToken)
    {
        CierreDia.ExigirSucursal(query.SucursalesPermitidas, query.SucursalId);

        var sucursal = await sucursales.ObtenerNombreAsync(query.SucursalId, cancellationToken)
            ?? query.SucursalId.ToString();
        var zona = await sucursales.ObtenerZonaHorariaAsync(query.SucursalId, cancellationToken);
        var (desde, hasta) = CierreDia.Ventana(query.Dia, zona);
        string Local(DateTimeOffset instante) =>
            DiaOperacion.ALocal(instante, zona).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var comprobantes = await ComprobantesDelDiaAsync(db, query.SucursalId, query.Dia, query.CajeroId, desde, hasta, cancellationToken);
        var nombres = await usuarios.ObtenerNombresAsync(
            [.. comprobantes.Select(c => c.UsuarioEmisorId).OfType<Guid>().Append(query.CajeroId ?? Guid.Empty).Distinct()],
            cancellationToken);

        var filas = comprobantes
            .OrderBy(c => c.FechaTimbrado ?? c.CreatedAt)
            .Select(c => new MonitorCfdiFila(
                ComprobanteId: c.Id,
                Fecha: Local(c.FechaTimbrado ?? c.CreatedAt),
                Tipo: CierreDia.TipoDocumento(c),
                Folio: c.Folio,
                Uuid: c.Uuid,
                Cliente: c.ReceptorNombre,
                MetodoPago: c.MetodoPago,
                Moneda: c.Moneda,
                Total: c.Total,
                Estado: CierreDia.EstadoMonitor(c.Estado),
                EstatusSat: c.Uuid is null ? "No aplica" : c.EstatusSat ?? "Sin verificar",
                EstatusSatConsultadoEn: c.EstatusSatConsultadoEn is DateTimeOffset en ? Local(en) : null,
                Discrepancia: EsDiscrepancia(c),
                Cajero: c.UsuarioEmisorId is Guid u ? nombres.GetValueOrDefault(u) : null,
                Detalle: c.Estado == EstadoTimbrado.TimbradoFallido ? c.TimbradoErrorMensaje : null))
            .ToList();

        var pedidos = await db.PedidosFacturables.AsNoTracking()
            .Where(p => p.SucursalId == query.SucursalId && PedidosPendientes.Contains(p.Estado) && p.CreatedAt < hasta)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        var filasPedidos = pedidos.Select(p => new MonitorPedidoFila(
                p.Id,
                p.NumeroPedido ?? "(manual)",
                p.Origen.ToString(),
                p.ClienteNombre,
                p.Estado switch
                {
                    EstadoPedidoFacturable.Bloqueado => "En facturación",
                    EstadoPedidoFacturable.Excepcion => "Con excepción",
                    _ => "Por facturar",
                },
                Local(p.CreatedAt),
                p.Total))
            .ToList();

        var vigentes = comprobantes.Where(c => CierreDia.EstadosVigentes.Contains(c.Estado)).ToList();
        var resumen = new MonitorCfdiResumen(
            Timbrados: comprobantes.Count(c => c.Estado == EstadoTimbrado.Timbrado),
            Cancelados: comprobantes.Count(c => c.Estado == EstadoTimbrado.Cancelado),
            CancelacionEnTramite: comprobantes.Count(c => c.Estado == EstadoTimbrado.CancelacionPendiente),
            SinTimbrar: comprobantes.Count(c => CierreDia.EstadosSinTimbrar.Contains(c.Estado) && c.Estado != EstadoTimbrado.TimbradoFallido),
            Fallidos: comprobantes.Count(c => c.Estado == EstadoTimbrado.TimbradoFallido),
            PedidosSinTimbrar: filasPedidos.Count,
            SinVerificarSat: comprobantes.Count(c => c.Uuid is not null && c.EstatusSat is null),
            Discrepancias: filas.Count(f => f.Discrepancia),
            TotalVigente: vigentes.Where(c => c.Moneda == "MXN" && c.Tipo == TipoComprobante.Ingreso).Sum(c => c.Total));

        var cajero = query.CajeroId is Guid id ? nombres.GetValueOrDefault(id) ?? id.ToString() : null;
        var filtros = new Dictionary<string, string?>
        {
            ["Sucursal"] = sucursal,
            ["Día"] = query.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Cajero"] = cajero ?? "Todos",
            ["Estatus SAT"] = estatusSat.Fuente,
            ["Timbrados"] = resumen.Timbrados.ToString(CultureInfo.InvariantCulture),
            ["Cancelados"] = resumen.Cancelados.ToString(CultureInfo.InvariantCulture),
            ["Sin timbrar"] = (resumen.SinTimbrar + resumen.Fallidos).ToString(CultureInfo.InvariantCulture),
            ["Discrepancias con el SAT"] = resumen.Discrepancias.ToString(CultureInfo.InvariantCulture),
        };

        var reporte = new ReporteResponse<MonitorCfdiFila>(
            Titulo: $"Monitor de CFDI · {sucursal} · {query.Dia:yyyy-MM-dd}",
            GeneradoEn: clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas:
            [
                new("fecha", "Fecha y hora", "texto"),
                new("tipo", "Tipo", "texto"),
                new("folio", "Folio", "texto"),
                new("uuid", "UUID", "texto"),
                new("cliente", "Cliente", "texto"),
                new("metodoPago", "Método", "texto"),
                new("moneda", "Moneda", "texto"),
                new("estado", "Estado", "texto"),
                new("estatusSat", "Estatus SAT", "texto"),
                new("estatusSatConsultadoEn", "Verificado", "texto"),
                new("cajero", "Emitió", "texto"),
                new("detalle", "Detalle", "texto"),
                new("total", "Total", "moneda", "derecha"),
            ],
            Filas: filas,
            Totales: new Dictionary<string, decimal> { ["total"] = resumen.TotalVigente });

        var reportePedidos = new ReporteResponse<MonitorPedidoFila>(
            Titulo: $"Pedidos sin timbrar · {sucursal} · al cierre del {query.Dia:yyyy-MM-dd}",
            GeneradoEn: clock.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["Sucursal"] = sucursal,
                ["Día"] = query.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Criterio"] = "Pedidos recibidos hasta el cierre del día que siguen sin CFDI vigente",
            },
            Columnas:
            [
                new("pedido", "Pedido", "texto"),
                new("origen", "Origen", "texto"),
                new("cliente", "Cliente", "texto"),
                new("estado", "Estado", "texto"),
                new("recibido", "Recibido", "texto"),
                new("total", "Total", "moneda", "derecha"),
            ],
            Filas: filasPedidos,
            Totales: new Dictionary<string, decimal> { ["total"] = filasPedidos.Sum(p => p.Total) });

        return new MonitorCfdiResponse(sucursal, query.Dia, cajero, estatusSat.Fuente, resumen, reporte, reportePedidos);
    }

    /// <summary>
    /// Comprobantes del día de la sucursal (ver resumen de la clase). Los
    /// descartados no se listan: quemaron folio a propósito y no son CFDI.
    /// </summary>
    internal static async Task<List<Comprobante>> ComprobantesDelDiaAsync(
        FacturacionDbContext db, Guid sucursalId, DateOnly dia, Guid? cajeroId,
        DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancellationToken)
    {
        var canceladosHoy = db.SolicitudesCancelacion.AsNoTracking()
            .Where(s => s.Estado == EstadoSolicitudCancelacion.Aceptada && s.ResueltaEn >= desde && s.ResueltaEn < hasta)
            .Select(s => s.ComprobanteId);

        var q = db.Comprobantes.AsNoTracking()
            .Where(c => c.SucursalId == sucursalId && c.Estado != EstadoTimbrado.Descartada
                && ((c.FechaTimbrado != null && c.FechaTimbrado >= desde && c.FechaTimbrado < hasta)
                    || (c.FechaTimbrado == null && CierreDia.EstadosSinTimbrar.Contains(c.Estado)
                        && c.CreatedAt >= desde && c.CreatedAt < hasta)
                    || canceladosHoy.Contains(c.Id)));

        if (cajeroId is Guid cajero)
        {
            var sesiones = db.CajaSesiones.AsNoTracking()
                .Where(s => s.ResponsableUsuarioId == cajero && s.SucursalId == sucursalId && s.DiaOperacion == dia)
                .Select(s => s.Id);
            var cobrados = db.CobrosMostrador.AsNoTracking()
                .Where(c => sesiones.Contains(c.CajaSesionId))
                .Select(c => c.ComprobanteId);
            q = q.Where(c => c.UsuarioEmisorId == cajero || cobrados.Contains(c.Id));
        }

        return await q.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// El SAT dice otra cosa que el estado interno: vigente aquí y cancelado o
    /// inexistente allá, o cancelado aquí y vigente allá. Sin consulta no hay discrepancia.
    /// </summary>
    internal static bool EsDiscrepancia(Comprobante c) => c.EstatusSat switch
    {
        null => false,
        EstatusSat.Vigente => c.Estado == EstadoTimbrado.Cancelado,
        EstatusSat.Cancelado => c.Estado is EstadoTimbrado.Timbrado,
        _ => c.Uuid is not null,
    };
}
