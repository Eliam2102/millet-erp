using MediatR;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.SharedKernel.Infrastructure.Persistence;
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
/// La tolerancia del proveedor prevalece sobre el parámetro global de Administración.
/// El valor utilizado se conserva como foto en la factura.
/// </summary>
public sealed class CapturarFacturaConOcHandler : IRequestHandler<CapturarFacturaConOcCommand, CapturarFacturaConOcResponse>
{
    private readonly Domain.Ports.Administracion.IToleranciaGeneralReadPort _toleranciaGeneral;

    private readonly CuentasPorPagarDbContext _db;
    private readonly IComprasOcReadPort _ocPort;
    private readonly IProveedorReadPort _proveedorPort;
    private readonly ICfdiBlobStorage _blobs;
    private readonly IXmlCfdiParser _parser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public CapturarFacturaConOcHandler(
        CuentasPorPagarDbContext db,
        IComprasOcReadPort ocPort,
        IProveedorReadPort proveedorPort,
        ICfdiBlobStorage blobs,
        IXmlCfdiParser parser,
        ICurrentEmpresaContext currentEmpresa,
        IMediator mediator,
        IClock clock,
        Domain.Ports.Administracion.IToleranciaGeneralReadPort toleranciaGeneral)
    {
        _db = db;
        _ocPort = ocPort;
        _proveedorPort = proveedorPort;
        _blobs = blobs;
        _parser = parser;
        _currentEmpresa = currentEmpresa;
        _mediator = mediator;
        _clock = clock;
        _toleranciaGeneral = toleranciaGeneral;
    }

    public async Task<CapturarFacturaConOcResponse> Handle(
        CapturarFacturaConOcCommand command,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await CapturarAsync(command, cancellationToken);
        CapturarFacturaConOcResponse? resultado = null;
        await PostgresAdvisoryLock.ExecuteAsync(_db, BitConverter.ToInt64(command.OrdenCompraId.ToByteArray()),
            async ct => resultado = await CapturarAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<CapturarFacturaConOcResponse> CapturarAsync(
        CapturarFacturaConOcCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // CFDI/NC compartidos entre intentos de distintas OC: orden estable de locks, misma transacción.
        if (_db.Database.IsRelational())
            foreach (var id in (command.NotasCredito ?? []).Select(n => n.CfdiRecibidoId)
                .Concat(command.CfdiRecibidoId is Guid fiscalId ? [fiscalId] : Array.Empty<Guid>()).Distinct().Order())
            {
                var llave = BitConverter.ToInt64(id.ToByteArray());
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({llave})", cancellationToken);
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

        if (oc.SucursalId != command.SucursalId)
            throw new BusinessRuleException("OC_SUCURSAL_DISTINTA", "La sucursal de la factura debe coincidir con la OC.");
        if (oc.Moneda != command.Moneda)
            throw new BusinessRuleException("OC_MONEDA_DISTINTA", "La moneda de la factura debe coincidir con la OC para conciliar sus precios.");

        // 1.bis) Guarda de pertenencia: las líneas de factura con LineaOcId
        //        deben referenciar una línea de ESTA OC (atribución por línea
        //        del three-way match). El Validator no tiene oc.Lineas; aquí sí.
        LineaOcPertenenciaGuard.Validar(command.Lineas, oc.Lineas, oc.Folio);

        // 2) Resolver el proveedor y validar estatus operativo (F1-ADM-05 G1.1 / Plano G1 §3.1).
        //    Un proveedor EnRevision o Inactivo no admite captura de factura ni pago.
        var proveedor = await _proveedorPort.ObtenerAsync(command.ProveedorId, cancellationToken);
        if (proveedor is null)
        {
            throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró el proveedor '{command.ProveedorId}' en Datos Maestros.");
        }

        if (proveedor.EnRevision)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_EN_REVISION",
                $"El proveedor '{proveedor.RazonSocial}' está en revisión y no admite captura de facturas.");
        }

        if (!proveedor.Activo)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_NO_ACTIVO",
                $"El proveedor '{proveedor.RazonSocial}' no está activo y no admite captura de facturas.");
        }

        var tolerancia = await ResolverToleranciaAsync(proveedor, cancellationToken);

        var cfdi = command.CfdiRecibidoId is Guid cfdiId
            ? await LeerCfdiAsync(cfdiId, TipoCfdi.Ingreso, proveedor.Rfc, command.Moneda, cancellationToken)
            : null;
        var xml = cfdi is null ? null : await LeerXmlAsync(cfdi, cancellationToken);
        if (xml is not null && !string.Equals(xml.UuidCfdi, command.UuidCfdi, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("CFDI_UUID_DISTINTO", "El UUID capturado no coincide con el XML ligado.");
        if (xml is not null && (xml.TipoCambio ?? 1) != (command.TipoCambio ?? 1))
            throw new BusinessRuleException("CFDI_TIPO_CAMBIO_DISTINTO", "El tipo de cambio capturado debe coincidir con el XML ligado.");
        var uuidNormalizado = command.UuidCfdi?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(command.UuidCfdi) && await _db.FacturasProveedor.AnyAsync(
            f => f.UuidCfdi == uuidNormalizado && f.Estado != EstadoPasivo.Cancelada, cancellationToken))
            throw new BusinessRuleException("FACTURA_DUPLICADA", "El CFDI ya tiene una factura vigente.");

        var notas = new List<(CfdiRecibido Cfdi, DatosCfdiParseados Xml)>();
        foreach (var adjunta in command.NotasCredito ?? [])
        {
            if (xml is null) throw new BusinessRuleException("NC_REQUIERE_CFDI", "Liga el CFDI de la factura para adjuntar sus notas de crédito.");
            var documento = await LeerCfdiAsync(adjunta.CfdiRecibidoId, TipoCfdi.Egreso, proveedor.Rfc, command.Moneda, cancellationToken);
            var datos = await LeerXmlAsync(documento, cancellationToken);
            var uuidNcNormalizado = datos.UuidCfdi.Trim().ToUpperInvariant();
            if (notas.Any(n => n.Cfdi.Id == documento.Id) || await _db.NotasCreditoProveedor.AnyAsync(n => n.UuidCfdi == uuidNcNormalizado, cancellationToken))
                throw new BusinessRuleException("NC_DUPLICADA", "La nota de crédito ya fue capturada o está adjunta dos veces.");
            if (datos.CfdiRelacionados is not { Count: > 0 } || datos.CfdiRelacionados.Any(r => r.TipoRelacion != "01" ||
                    r.Uuids.Count != 1 || !string.Equals(r.Uuids[0], xml.UuidCfdi, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessRuleException("NC_RELACION_INVALIDA", "La NC debe tener relación 01 exclusivamente con esta factura.");
            if (datos.RfcReceptor != xml.RfcReceptor || datos.TipoCambio != xml.TipoCambio)
                throw new BusinessRuleException("NC_DATOS_DISTINTOS", "La NC debe tener el mismo receptor y tipo de cambio de la factura.");
            if (adjunta.Lineas.Count == 0 || adjunta.Lineas.Any(l => l.Base <= 0) ||
                !tolerancia.Pasa(adjunta.Lineas.Sum(l => l.Base) - (datos.Subtotal - datos.Descuentos), datos.Subtotal) ||
                !tolerancia.Pasa(datos.Total - (datos.Subtotal - datos.Descuentos + datos.ImpuestosTrasladados - datos.Retenciones), datos.Total))
                throw new BusinessRuleException("NC_IMPORTES_INVALIDOS", "La base asignada a las líneas debe cuadrar con el XML de la NC, y su neto fiscal debe ser correcto.");
            notas.Add((documento, datos));
        }
        var previas = await _db.FacturasProveedor.Where(f => f.OrdenCompraId == oc.Id && f.Estado != EstadoPasivo.Cancelada)
            .SelectMany(f => f.Lineas).Where(l => l.LineaOcId != null)
            .GroupBy(l => l.LineaOcId!.Value).Select(g => new { Id = g.Key, Cantidad = g.Sum(l => l.Cantidad) })
            .ToDictionaryAsync(l => l.Id, l => l.Cantidad, cancellationToken);
        var conciliacion = ConciliacionFacturaOc.Conciliar(command, oc, tolerancia, previas, xml);
        var diferencia = conciliacion.Diferencia;
        if (notas.Sum(n => n.Xml.Total) > command.Total)
            throw new BusinessRuleException("NC_EXCEDE_FACTURA", "Las notas de crédito exceden el total de la factura.");
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
            uuidCfdi: uuidNormalizado,
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

        factura.AsignarDatosP8(oc.Obra ?? command.Obra, command.ConceptoRetencion);

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
        if (conciliacion.Motivo is not null)
        {
            factura.Cancelar(
                motivo: MotivoCancelacion.RechazadaPorTolerancia,
                texto: conciliacion.Motivo,
                usuarioId: null,
                ahora: ahora);
        }

        if (factura.Estado != EstadoPasivo.Cancelada)
        {
            cfdi?.MarcarConvertidoEnPasivo(factura.Id);
            foreach (var (documento, datos) in notas)
            {
                var nc = global::Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
                    empresaId, documento.Id, datos.UuidCfdi, command.ProveedorId, datos.Folio, datos.Serie,
                    datos.FechaCfdi, datos.Moneda, datos.TipoCambio, datos.Subtotal - datos.Descuentos,
                    datos.ImpuestosTrasladados, datos.Retenciones, datos.Total, TipoNotaCredito.Descuento,
                    TipoRelacionCfdi.NotaCredito, xml!.UuidCfdi, factura.Id, null, ahora);
                nc.AplicarMonto(datos.Total);
                factura.AplicarNotaCredito(datos.Total, DateOnly.FromDateTime(ahora.UtcDateTime), nc.Id);
                documento.MarcarConvertidoEnPasivo(nc.Id);
                _db.NotasCreditoProveedor.Add(nc);
                await _mediator.Publish(new NotaCreditoProveedorRegistradaDomainEvent(
                    empresaId, nc.Id, nc.ProveedorId, factura.Id, 1, nc.Total, ahora,
                    Uuid: nc.UuidCfdi, Subtotal: nc.Subtotal, Iva: nc.ImpuestosTrasladados,
                    RetencionesTotal: nc.Retenciones, Retenciones: datos.RetencionesDetalle,
                    Moneda: nc.Moneda, TipoCambio: nc.TipoCambio, SucursalId: factura.SucursalId), cancellationToken);
            }
        }
        factura.AsignarMetodoPago(cfdi?.MetodoPago);
        factura.AsignarRetencionesDetalle(xml is null ? command.RetencionesDetalle : xml.RetencionesDetalle);

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
                OcurridoEn: ahora, Motivo: factura.MotivoCancelacionTexto), cancellationToken);
        }
        else
        {
            var lineasAcumuladasOc = await CalcularAcumuladosPorLineaOcAsync(
                factura.Lineas, factura.Id, cancellationToken);

            // G1.6 (P3): CeCo por línea = el de la línea de OC enlazada; en
            // cabecera solo si todas las líneas coinciden.
            var lineasEvento = factura.Lineas
                .OrderBy(l => l.Posicion)
                .Select(l => new LineaFacturada(
                    l.Id, l.LineaOcId, l.Cantidad, l.Importe,
                    CentroCostoId: oc.Lineas.FirstOrDefault(x => x.Id == l.LineaOcId)?.CentroCostoId))
                .ToList();
            var cecos = lineasEvento.Select(l => l.CentroCostoId).Distinct().ToList();

            await _mediator.Publish(new FacturaProveedorRegistradaDomainEvent(
                EmpresaId: empresaId,
                FacturaProveedorId: factura.Id,
                OrdenCompraId: command.OrdenCompraId,
                TotalFactura: factura.Total,
                Lineas: lineasEvento,
                LineasAcumuladasOc: lineasAcumuladasOc,
                OcurridoEn: ahora,
                ProveedorId: factura.ProveedorId,
                Uuid: factura.UuidCfdi,
                Subtotal: factura.Subtotal,
                Iva: factura.ImpuestosTrasladados,
                RetencionesTotal: factura.Retenciones,
                Retenciones: factura.RetencionesDetalle,
                Moneda: factura.Moneda,
                TipoCambio: factura.TipoCambio,
                SucursalId: factura.SucursalId,
                CentroCostoId: cecos.Count == 1 ? cecos[0] : null), cancellationToken);

        }

        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarFacturaConOcResponse(
            Id: factura.Id,
            Estado: factura.Estado,
            DiferenciaContraOc: factura.DiferenciaContraOc,
            SaldoPendiente: factura.SaldoPendiente,
            MotivoCancelacion: factura.MotivoDeCancelacion,
            Version: factura.Version,
            MotivoCancelacionTexto: factura.MotivoCancelacionTexto);
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

    private async Task<CfdiRecibido> LeerCfdiAsync(Guid id, TipoCfdi tipo, string rfc, string moneda, CancellationToken ct)
    {
        var cfdi = await _db.CfdisRecibidos.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("CFDI_NO_ENCONTRADO", "No se encontró el CFDI ligado.");
        if (cfdi.Estado != EstadoCfdiRecibido.PorProcesar || cfdi.Tipo != tipo ||
            !string.Equals(cfdi.RfcEmisor.Valor, rfc, StringComparison.OrdinalIgnoreCase) || cfdi.Moneda != moneda)
            throw new BusinessRuleException("CFDI_NO_UTILIZABLE", "El CFDI debe estar por procesar y coincidir en tipo, proveedor y moneda.");
        return cfdi;
    }

    private async Task<DatosCfdiParseados> LeerXmlAsync(CfdiRecibido cfdi, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cfdi.XmlBlobRef)) throw new BusinessRuleException("CFDI_SIN_XML", "El CFDI no tiene XML almacenado.");
        await using var stream = await _blobs.LeerXmlAsync(cfdi.XmlBlobRef, ct)
            ?? throw new BusinessRuleException("CFDI_XML_NO_DISPONIBLE", "No se pudo leer el XML ligado.");
        var datos = _parser.Parsear(stream);
        if (!string.Equals(datos.UuidCfdi, cfdi.UuidCfdi.Valor, StringComparison.OrdinalIgnoreCase) ||
            datos.RfcEmisor != cfdi.RfcEmisor.Valor || datos.Tipo != cfdi.Tipo || datos.Moneda != cfdi.Moneda)
            throw new BusinessRuleException("CFDI_XML_DISTINTO", "El XML almacenado no corresponde al CFDI seleccionado.");
        return datos;
    }

    private async Task<Tolerancia> ResolverToleranciaAsync(ProveedorDto proveedor, CancellationToken cancellationToken)
    {
        if (proveedor.Tolerancia is null)
            return Tolerancia.MontoAbsoluto(await _toleranciaGeneral.ObtenerMontoMxnAsync(cancellationToken));

        return proveedor.Tolerancia.Tipo switch
        {
            ToleranciaTipo.MontoAbsoluto => Tolerancia.MontoAbsoluto(proveedor.Tolerancia.Valor),
            ToleranciaTipo.Porcentaje => Tolerancia.Porcentaje(proveedor.Tolerancia.Valor),
            _ => throw new BusinessRuleException("TOLERANCIA_TIPO_INVALIDO", "El tipo de tolerancia no es válido."),
        };
    }
}
