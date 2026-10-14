using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Reportes.CorteCaja;

/// <summary>
/// Corte de caja de una sesión (FIX-12; Manual de Sucursales §2.4, R18,
/// CA6.10): lo que la cajera entrega al cerrar — cobros por forma de pago con
/// su voucher, comprobantes cobrados con sus NC acreditadas, anticipos,
/// complementos de pago, ajustes y reversas — con el nombre del cajero.
///
/// <para>
/// Las filas son los <c>caja_movimiento</c> de la sesión: la misma fuente que
/// el arqueo, así que la suma por forma de pago del corte es exactamente el
/// esperado del arqueo (sin duplicar ni omitir). Cada cobro se enriquece con
/// su comprobante; el total del CFDI y sus NC aparecen una sola vez por cobro
/// y son informativos (no suman al efectivo: la caja cobra el neto, Matriz I2).
/// La conciliación facturado contra ingresado por sucursal vive en F6.7.
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

public sealed record CorteCajaResponse(
    CorteCajaEncabezado Encabezado,
    IReadOnlyList<CorteCajaFormaPago> PorFormaPago,
    ReporteResponse<CorteCajaFila> Reporte);

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

        return new CorteCajaResponse(encabezado, porForma, reporte);
    }

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
