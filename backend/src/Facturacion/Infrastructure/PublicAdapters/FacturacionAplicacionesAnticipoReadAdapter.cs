using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter público de <see cref="IFacturacionAplicacionesAnticipoReadPort"/>
/// (F1-CXC-05): anticipos del cliente con sus aplicaciones a factura final
/// (factura final + CFDI de egreso tipo 07 que amortiza el anticipo).
///
/// <para>
/// Las reglas de cálculo son una copia acotada de
/// <c>Application.Reportes.ComparativaAnticipos.ComparativaAnticiposHandler</c>
/// (F6.2) a la fecha de hoy, sin periodo ni fecha de corte: una aplicación resta
/// saldo solo si su egreso está Timbrado o en CancelacionPendiente; un egreso
/// Cancelado se muestra y no resta; una vinculación sin egreso timbrado va a
/// «Comprometido» y no resta. Si el saldo calculado difiere del persistido se
/// avisa «Saldo en sistema …» (la restitución del saldo al cancelar el egreso
/// está fuera de alcance). La paridad con F6.2 la cubre
/// <c>AplicacionesAnticipoReadAdapterTests.Paridad_con_la_comparativa_de_facturacion</c>:
/// si cambia una regla allá, cambia aquí.
/// </para>
///
/// <para>
/// A diferencia de F6.2 no aplica el alcance de Cajas (Capa A): el alcance de
/// CxC es por sucursal (ADR-0051) y lo filtra la query consumidora con el
/// <see cref="AnticipoConAplicacionesDto.SucursalId"/>.
/// </para>
/// </summary>
public sealed class FacturacionAplicacionesAnticipoReadAdapter : IFacturacionAplicacionesAnticipoReadPort
{
    private static readonly CultureInfo EsMx = CultureInfo.GetCultureInfo("es-MX");

    private static readonly EstadoTimbrado[] EstadosAnticipoIncluidos =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente, EstadoTimbrado.Cancelado];

    private readonly FacturacionDbContext _db;
    private readonly ISucursalesReadPort _sucursales;

    public FacturacionAplicacionesAnticipoReadAdapter(FacturacionDbContext db, ISucursalesReadPort sucursales)
    {
        _db = db;
        _sucursales = sucursales;
    }

    public async Task<IReadOnlyList<AnticipoConAplicacionesDto>> ListarAsync(
        Guid? clienteId, CancellationToken cancellationToken)
    {
        // 1) Agregado Anticipo (saldo, vinculaciones), opcionalmente por cliente.
        var anticiposQ = _db.Anticipos.AsNoTracking().Include(a => a.Vinculaciones).AsQueryable();
        if (clienteId is { } id) anticiposQ = anticiposQ.Where(a => a.ClienteId == id);
        var anticipos = await anticiposQ.ToListAsync(cancellationToken);
        if (anticipos.Count == 0) return [];

        // 2) Su factura de anticipo timbrada (incluye canceladas: estado Cancelado).
        var facturaIds = anticipos.Select(a => a.FacturaAnticipoId).ToList();
        var facturas = await _db.FacturasAnticipo.AsNoTracking()
            .Where(f => facturaIds.Contains(f.Id)
                        && EstadosAnticipoIncluidos.Contains(f.Estado)
                        && f.FechaTimbrado != null)
            .Select(f => new
            {
                f.Id, f.Folio, f.FolioNumero, f.Uuid, f.FechaTimbrado, f.ReceptorNombre, f.SucursalId, f.Estado,
            })
            .ToListAsync(cancellationToken);
        var facturaPorId = facturas.ToDictionary(f => f.Id);

        // 3) Facturas finales y egresos (NC de amortización).
        var vinculaciones = anticipos.SelectMany(a => a.Vinculaciones).ToList();
        var facturaVentaIds = vinculaciones.Select(v => v.FacturaVentaId).Distinct().ToList();
        var ncIds = vinculaciones
            .Where(v => v.NcAmortizacionId is not null)
            .Select(v => v.NcAmortizacionId!.Value).Distinct().ToList();

        var facturasVenta = facturaVentaIds.Count == 0
            ? []
            : await _db.FacturasVenta.AsNoTracking()
                .Where(f => facturaVentaIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Folio, f.Uuid })
                .ToListAsync(cancellationToken);
        var facturaVentaPorId = facturasVenta.ToDictionary(f => f.Id);

        var ncs = ncIds.Count == 0
            ? []
            : await _db.NotasCredito.AsNoTracking()
                .Where(n => ncIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Folio, n.Uuid, n.Estado, n.FechaTimbrado, n.Total })
                .ToListAsync(cancellationToken);
        var ncPorId = ncs.ToDictionary(n => n.Id);

        // 4) Nombre de sucursal («CLAVE — Nombre»), una lectura por sucursal.
        var nombreSucursal = new Dictionary<Guid, string?>();
        foreach (var sucId in facturas.Select(f => f.SucursalId).Distinct())
            nombreSucursal[sucId] = await _sucursales.ObtenerNombreAsync(sucId, cancellationToken);

        var resultado = new List<(string Cliente, long FolioNumero, AnticipoConAplicacionesDto Dto)>();
        foreach (var anticipo in anticipos)
        {
            if (!facturaPorId.TryGetValue(anticipo.FacturaAnticipoId, out var f)) continue;

            var aplicaciones = new List<(DateTimeOffset? Orden, DateTimeOffset CreadoEn, AplicacionAnticipoDto Dto)>();
            decimal aplicado = 0m, comprometido = 0m;

            foreach (var v in anticipo.Vinculaciones)
            {
                var fv = facturaVentaPorId.GetValueOrDefault(v.FacturaVentaId);
                var nc = v.NcAmortizacionId is { } ncId ? ncPorId.GetValueOrDefault(ncId) : null;

                var egresoTimbrado = nc is not null && nc.FechaTimbrado is not null
                    && nc.Estado is EstadoTimbrado.Timbrado or EstadoTimbrado.CancelacionPendiente or EstadoTimbrado.Cancelado;

                AplicacionAnticipoDto dto;
                if (!egresoTimbrado)
                {
                    // Vinculación M2 sin NC timbrada: compromete saldo, no lo resta.
                    comprometido += v.Importe;
                    dto = new AplicacionAnticipoDto(
                        TipoAplicacionAnticipo.VinculacionSinEgreso, fv?.Folio, fv?.Uuid,
                        nc?.Folio, nc?.Uuid, null, nc is null ? null : DescribirEstado(nc.Estado), v.Importe);
                }
                else if (nc!.Estado == EstadoTimbrado.Cancelado)
                {
                    dto = new AplicacionAnticipoDto(
                        TipoAplicacionAnticipo.EgresoCancelado, fv?.Folio, fv?.Uuid,
                        nc.Folio, nc.Uuid, nc.FechaTimbrado, DescribirEstado(nc.Estado), nc.Total);
                }
                else
                {
                    aplicado += nc.Total;
                    dto = new AplicacionAnticipoDto(
                        TipoAplicacionAnticipo.Aplicacion, fv?.Folio, fv?.Uuid,
                        nc.Folio, nc.Uuid, nc.FechaTimbrado, DescribirEstado(nc.Estado), nc.Total);
                }

                aplicaciones.Add((dto.FechaAplicacion, v.CreadoEn, dto));
            }

            var cobrado = anticipo.MontoCobrado;
            var saldo = cobrado - aplicado;
            var estado = anticipo.Estado == EstadoAnticipo.Cancelado || f.Estado == EstadoTimbrado.Cancelado
                ? EstadoAnticipoAplicaciones.Cancelado
                : aplicado == 0m
                    ? EstadoAnticipoAplicaciones.Pendiente
                    : saldo <= 0m
                        ? EstadoAnticipoAplicaciones.Aplicado
                        : EstadoAnticipoAplicaciones.Parcial;

            var observacion = saldo != anticipo.Saldo
                ? $"Saldo en sistema {anticipo.Saldo.ToString("C2", EsMx)}"
                : null;

            var ordenadas = aplicaciones
                .OrderBy(a => a.Orden is null) // vinculaciones sin egreso al final
                .ThenBy(a => a.Orden)
                .ThenBy(a => a.CreadoEn)
                .Select(a => a.Dto)
                .ToList();

            resultado.Add((f.ReceptorNombre, f.FolioNumero, new AnticipoConAplicacionesDto(
                AnticipoId: anticipo.Id,
                FacturaAnticipoId: f.Id,
                ClienteId: anticipo.ClienteId,
                ClienteNombre: f.ReceptorNombre,
                Folio: f.Folio,
                Uuid: f.Uuid,
                FechaTimbrado: f.FechaTimbrado,
                SucursalId: f.SucursalId,
                SucursalNombre: nombreSucursal.GetValueOrDefault(f.SucursalId),
                Moneda: anticipo.Moneda,
                Estado: estado,
                Cobrado: cobrado,
                Aplicado: aplicado,
                Comprometido: comprometido,
                Saldo: saldo,
                SaldoSistema: anticipo.Saldo,
                PedidoOrigenRef: anticipo.PedidoOrigenRef,
                Observacion: observacion,
                Aplicaciones: ordenadas)));
        }

        return resultado
            .OrderBy(r => r.Cliente, StringComparer.Create(EsMx, ignoreCase: true))
            .ThenBy(r => r.FolioNumero)
            .Select(r => r.Dto)
            .ToList();
    }

    private static string DescribirEstado(EstadoTimbrado estado) => estado switch
    {
        EstadoTimbrado.Timbrado => "Timbrado",
        EstadoTimbrado.CancelacionPendiente => "Cancelación pendiente",
        EstadoTimbrado.Cancelado => "Cancelado",
        EstadoTimbrado.TimbradoFallido => "Timbrado fallido",
        EstadoTimbrado.TimbradoEnProceso => "Timbrado en proceso",
        EstadoTimbrado.Descartada => "Descartado",
        _ => "Borrador",
    };
}
