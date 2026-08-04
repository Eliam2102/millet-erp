using MediatR;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;

/// <summary>
/// Handler de <see cref="CapturarFacturaConOcCommand"/> (§7.1, §3.bis.3,
/// §3.bis.5 del 01-diseno). Implementa el flujo "Factura con OC" del
/// MVP — la base de la mayoría de las facturas de Millet.
///
/// <para>
/// **Default global de tolerancia** (§3.bis.3): si el proveedor no tiene
/// tolerancia configurada, se usa $0.99 MXP absoluto. Esto es un
/// parámetro de Administración (<c>cuentas_por_pagar.tolerancia_default_mxp</c>)
/// — en F3-PR1 vive como constante; cuando exista el endpoint de
/// parámetros globales se mueve.
/// </para>
/// </summary>
public sealed class CapturarFacturaConOcHandler : IRequestHandler<CapturarFacturaConOcCommand, CapturarFacturaConOcResponse>
{
    public const decimal ToleranciaDefaultMxp = 0.99m;

    private readonly CuentasPorPagarDbContext _db;
    private readonly IComprasOcReadPort _ocPort;
    private readonly IProveedorReadPort _proveedorPort;
    private readonly IAlmacenRecepcionReadPort _recepcionPort;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public CapturarFacturaConOcHandler(
        CuentasPorPagarDbContext db,
        IComprasOcReadPort ocPort,
        IProveedorReadPort proveedorPort,
        IAlmacenRecepcionReadPort recepcionPort,
        ICurrentEmpresaContext currentEmpresa,
        IMediator mediator,
        IClock clock)
    {
        _db = db;
        _ocPort = ocPort;
        _proveedorPort = proveedorPort;
        _recepcionPort = recepcionPort;
        _currentEmpresa = currentEmpresa;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<CapturarFacturaConOcResponse> Handle(
        CapturarFacturaConOcCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // 1) Resolver la OC.
        var oc = await _ocPort.ObtenerAsync(command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "OC_NO_ENCONTRADA",
                $"No se encontró la OC '{command.OrdenCompraId}' en el módulo Compras.");

        if (!string.Equals(oc.Estado, "Autorizada", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(oc.Estado, "EnRecepcion", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(oc.Estado, "Recibida", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(oc.Estado, "EnFacturacion", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "OC_ESTADO_NO_FACTURABLE",
                $"La OC '{oc.Folio}' está en estado '{oc.Estado}' y no permite captura de factura.");
        }

        if (oc.ProveedorId != command.ProveedorId)
        {
            throw new BusinessRuleException(
                "OC_PROVEEDOR_MISMATCH",
                $"La OC '{oc.Folio}' pertenece al proveedor {oc.ProveedorId}, pero la factura indica {command.ProveedorId}.");
        }

        // 1.bis) Guarda de pertenencia: las líneas de factura con LineaOcId
        //        deben referenciar una línea de ESTA OC (atribución por línea
        //        del three-way match). El Validator no tiene oc.Lineas; aquí sí.
        LineaOcPertenenciaGuard.Validar(command.Lineas, oc.Lineas, oc.Folio);

        // 2) Resolver la tolerancia del proveedor (snapshot al momento
        //    de captura, §3.bis.3).
        var proveedor = await _proveedorPort.ObtenerAsync(command.ProveedorId, cancellationToken);
        var tolerancia = ResolverTolerancia(proveedor);

        // 3) Calcular diferencia (factura.Total vs oc.Total).
        var diferencia = command.Total - oc.Total;
        var redondeo = 0m;

        // 4) Construir la factura. Si pasa tolerancia → Capturada con
        //    snapshot. Si no pasa → la creamos y CANCELAMOS de inmediato
        //    con motivo `RechazadaPorTolerancia` para que CxP tenga
        //    registro y Compras pueda corregir la OC vía el evento de
        //    F3-PR2.
        var ahora = _clock.UtcNow;
        var factura = global::Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: empresaId,
            cfdiRecibidoId: command.CfdiRecibidoId,
            uuidCfdi: command.UuidCfdi,
            proveedorId: command.ProveedorId,
            sucursalId: command.SucursalId,
            folioProveedor: command.FolioProveedor,
            serieProveedor: command.SerieProveedor,
            fechaDocumento: command.FechaDocumento,
            fechaContabilizacion: command.FechaContabilizacion,
            fechaVencimiento: command.FechaVencimiento,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            subtotal: command.Subtotal,
            descuentos: command.Descuentos,
            impuestosTrasladados: command.ImpuestosTrasladados,
            retenciones: command.Retenciones,
            total: command.Total,
            ordenCompraId: command.OrdenCompraId,
            encargadoComprasSnapshot: null,
            tolerancia: tolerancia,
            diferenciaContraOc: Math.Abs(diferencia),
            redondeoAplicado: redondeo,
            ahora: ahora);

        // 5) Líneas.
        foreach (var l in command.Lineas)
        {
            factura.AgregarLinea(
                articuloId: l.ArticuloId,
                claveProdServ: l.ClaveProdServ,
                descripcion: l.Descripcion,
                cantidad: l.Cantidad,
                claveUnidad: l.ClaveUnidad,
                unidad: l.Unidad,
                precioUnitario: l.PrecioUnitario,
                importe: l.Importe,
                descuento: l.Descuento,
                lineaOcId: l.LineaOcId,
                conceptoContableId: l.ConceptoContableId);
        }

        // 6) Si la diferencia no pasa tolerancia, cancela. El evento
        //    de rechazo lo emite F3-PR2 (suscripción de Compras).
        if (!tolerancia.Pasa(diferencia, oc.Total))
        {
            factura.Cancelar(
                motivo: MotivoCancelacion.RechazadaPorTolerancia,
                texto: $"Diferencia {diferencia:N4} contra OC {oc.Folio} (total OC {oc.Total:N4}) excede tolerancia {tolerancia.Tipo} {tolerancia.Valor}.",
                usuarioId: null,
                ahora: ahora);
        }

        // 7) Si el CFDI viene referenciado, marcar como convertido y copiar
        //    su MetodoPago al pasivo (TES-PR8 [T-G11] — de aquí viaja a
        //    Tesorería en pasivo.autorizado-para-pago.v1).
        if (command.CfdiRecibidoId is Guid cfdiId)
        {
            var cfdi = await _db.CfdisRecibidos.FirstOrDefaultAsync(c => c.Id == cfdiId, cancellationToken);
            if (cfdi is not null && cfdi.Estado == EstadoCfdiRecibido.PorProcesar)
            {
                cfdi.MarcarConvertidoEnPasivo(factura.Id);
            }
            factura.AsignarMetodoPago(cfdi?.MetodoPago);
        }

        _db.FacturasProveedor.Add(factura);

        // 8) Publicar domain events ANTES de SaveChanges para que el
        //    OutboxSaveChangesInterceptor drene el buffer scoped a la
        //    tabla integration_events_outbox en la misma TX (atomicidad
        //    ADR-0009).
        if (factura.Estado == EstadoPasivo.Cancelada &&
            factura.MotivoDeCancelacion == MotivoCancelacion.RechazadaPorTolerancia)
        {
            await _mediator.Publish(new FacturaProveedorRechazadaPorToleranciaDomainEvent(
                EmpresaId: empresaId,
                FacturaProveedorId: factura.Id,
                OrdenCompraId: command.OrdenCompraId,
                TotalFactura: factura.Total,
                TotalOc: oc.Total,
                Diferencia: Math.Abs(diferencia),
                ToleranciaAplicada: $"{tolerancia.Tipo}:{tolerancia.Valor}",
                OcurridoEn: ahora), cancellationToken);
        }
        else
        {
            var lineasAcumuladasOc = await CalcularAcumuladosPorLineaOcAsync(
                factura.Lineas, factura.Id, cancellationToken);

            await _mediator.Publish(new FacturaProveedorRegistradaDomainEvent(
                EmpresaId: empresaId,
                FacturaProveedorId: factura.Id,
                OrdenCompraId: command.OrdenCompraId,
                TotalFactura: factura.Total,
                Lineas: factura.Lineas
                    .OrderBy(l => l.Posicion)
                    .Select(l => new LineaFacturada(l.Id, l.LineaOcId, l.Cantidad, l.Importe))
                    .ToList(),
                LineasAcumuladasOc: lineasAcumuladasOc,
                OcurridoEn: ahora), cancellationToken);

            // GAP-3 (§3.bis.5): dentro de tolerancia pero con diferencia
            // de precio unitario por línea, en OC variante B (recepción
            // con factura pendiente) → notificar a Almacén para que
            // re-valorice el remanente en stock. Uno por artículo.
            var recepcion = await _recepcionPort.ObtenerRecepcionDeOcAsync(
                command.OrdenCompraId, cancellationToken);
            if (recepcion?.FacturaPendiente == true)
            {
                foreach (var lineaFactura in factura.Lineas)
                {
                    if (lineaFactura.LineaOcId is not { } lineaOcId) continue;
                    var lineaOc = oc.Lineas.FirstOrDefault(x => x.Id == lineaOcId);
                    if (lineaOc is null) continue;

                    var diferenciaUnitaria = lineaFactura.PrecioUnitario - lineaOc.PrecioUnitario;
                    if (diferenciaUnitaria == 0m) continue;

                    await _mediator.Publish(new DiferenciaPrecioFacturaDetectadaDomainEvent(
                        EmpresaId: empresaId,
                        FacturaProveedorId: factura.Id,
                        OrdenCompraId: command.OrdenCompraId,
                        ArticuloId: lineaOc.ArticuloId,
                        CantidadFacturada: lineaFactura.Cantidad,
                        PrecioFacturaUnitarioMxn: lineaFactura.PrecioUnitario,
                        PrecioOcUnitarioMxn: lineaOc.PrecioUnitario,
                        DiferenciaUnitarioMxn: diferenciaUnitaria,
                        MontoDiferenciaTotalMxn: Math.Round(diferenciaUnitaria * lineaFactura.Cantidad, 2),
                        OcurridoEn: ahora), cancellationToken);
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarFacturaConOcResponse(
            Id: factura.Id,
            Estado: factura.Estado,
            DiferenciaContraOc: factura.DiferenciaContraOc,
            SaldoPendiente: factura.SaldoPendiente,
            MotivoCancelacion: factura.MotivoDeCancelacion,
            Version: factura.Version);
    }

    /// <summary>
    /// Calcula el acumulado total facturado por <c>LineaOcId</c>
    /// tras aplicar la factura corriente. Suma cantidades de líneas de
    /// facturas previas vigentes (no Cancelada, excluyendo la factura
    /// que se está capturando — aún no persistida) + los deltas de la
    /// factura corriente. Compras consume el resultado directo como
    /// <c>CantidadFacturadaAcumulada</c>.
    /// </summary>
    private async Task<IReadOnlyList<LineaOcAcumulada>> CalcularAcumuladosPorLineaOcAsync(
        IReadOnlyCollection<LineaFacturaProveedor> lineas,
        Guid facturaCorrienteId,
        CancellationToken cancellationToken)
    {
        var deltasPorLineaOc = lineas
            .Where(l => l.LineaOcId is not null)
            .GroupBy(l => l.LineaOcId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantidad));

        if (deltasPorLineaOc.Count == 0) return [];

        var lineasOcIds = deltasPorLineaOc.Keys.ToArray();

        // Suma cantidades de facturas previas (no Cancelada) que tocan
        // estas LineaOcId. El handler corre antes de SaveChanges, así
        // que el query SQL no ve la factura corriente — sumamos su
        // delta aparte.
        var previos = await _db.FacturasProveedor
            .Where(f => f.Estado != EstadoPasivo.Cancelada
                && f.Id != facturaCorrienteId)
            .SelectMany(f => f.Lineas)
            .Where(l => l.LineaOcId.HasValue && lineasOcIds.Contains(l.LineaOcId.Value))
            .GroupBy(l => l.LineaOcId!.Value)
            .Select(g => new { LineaOcId = g.Key, Suma = g.Sum(l => l.Cantidad) })
            .ToListAsync(cancellationToken);

        var previosDict = previos.ToDictionary(p => p.LineaOcId, p => p.Suma);

        return deltasPorLineaOc
            .Select(kv =>
            {
                previosDict.TryGetValue(kv.Key, out var previo);
                return new LineaOcAcumulada(kv.Key, previo + kv.Value);
            })
            .ToList();
    }

    internal static Tolerancia ResolverTolerancia(ProveedorDto? proveedor)
    {
        if (proveedor?.Tolerancia is null)
        {
            return Tolerancia.MontoAbsoluto(ToleranciaDefaultMxp);
        }

        return proveedor.Tolerancia.Tipo switch
        {
            ToleranciaTipo.MontoAbsoluto => Tolerancia.MontoAbsoluto(proveedor.Tolerancia.Valor),
            ToleranciaTipo.Porcentaje    => Tolerancia.Porcentaje(proveedor.Tolerancia.Valor),
            _ => Tolerancia.MontoAbsoluto(ToleranciaDefaultMxp),
        };
    }
}
