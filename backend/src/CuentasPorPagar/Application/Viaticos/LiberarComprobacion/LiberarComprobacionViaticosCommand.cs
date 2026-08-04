using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.SharedKernel.Application.Integration;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Viaticos.LiberarComprobacion;

/// <summary>
/// CxP libera la comprobación de viáticos (§7.4.2 paso 8, F7-PR3). El
/// handler:
///
/// <list type="number">
///   <item>Genera una <c>FacturaProveedor</c> sin OC por cada línea
///   fiscal (con UUID y ProveedorId).</item>
///   <item>Vincula cada línea con la factura generada (FK).</item>
///   <item>Calcula <c>MontoComprobado</c>, <c>DiferenciaLiquidacion</c>
///   y deja la solicitud en <c>Liquidada</c>.</item>
/// </list>
///
/// <para>
/// El responsable contable contra el que se carga la <c>FacturaProveedor</c>
/// es el <c>PRESTAMO_EMPLEADO</c> — esta lógica vive en Contabilidad
/// cuando se conecte; por ahora basta con persistir la factura.
/// </para>
/// </summary>
public sealed record LiberarComprobacionViaticosCommand(Guid Id, int VersionEsperada)
    : IRequest<LiberarComprobacionViaticosResponse>;

public sealed record LiberarComprobacionViaticosResponse(
    Guid Id,
    EstadoSolicitudViaticos Estado,
    decimal MontoComprobado,
    decimal DiferenciaLiquidacion,
    IReadOnlyList<Guid> FacturasGeneradasIds,
    int Version);

public sealed class LiberarComprobacionViaticosValidator
    : AbstractValidator<LiberarComprobacionViaticosCommand>
{
    public LiberarComprobacionViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class LiberarComprobacionViaticosHandler
    : IRequestHandler<LiberarComprobacionViaticosCommand, LiberarComprobacionViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IEmpleadoReadPort _empleados;
    private readonly IIntegrationEventPublisher _events;
    private readonly IClock _clock;

    public LiberarComprobacionViaticosHandler(
        CuentasPorPagarDbContext db,
        IEmpleadoReadPort empleados,
        IIntegrationEventPublisher events,
        IClock clock)
    {
        _db = db; _empleados = empleados; _events = events; _clock = clock;
    }

    public async Task<LiberarComprobacionViaticosResponse> Handle(
        LiberarComprobacionViaticosCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.SolicitudesViaticos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("VIA_NO_ENCONTRADA",
                $"No se encontró la solicitud '{command.Id}'.");
        if (s.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(SolicitudViaticos), s.Id);

        // Las líneas fiscales deben poder generar factura: sin proveedor
        // o sin UUID antes se saltaban en silencio (P7-H4) y el gasto
        // desaparecía de la liquidación contable.
        var incompletas = s.Lineas
            .Where(l => !l.EsTicketNoFiscal && (l.UuidCfdi is null || l.ProveedorId is null))
            .ToList();
        if (incompletas.Count > 0)
        {
            throw new BusinessRuleException(
                "VIA_LINEA_FISCAL_INCOMPLETA",
                $"{incompletas.Count} línea(s) fiscal(es) sin proveedor o sin UUID — corrígelas " +
                "en la comprobación (o márcalas como ticket no fiscal) antes de liberar.");
        }

        // Validar duplicidad de UUIDs contra facturas ya capturadas
        // (las Canceladas no cuentan — compensación P7-H1).
        var uuidsConValor = s.Lineas
            .Where(l => !string.IsNullOrWhiteSpace(l.UuidCfdi))
            .Select(l => l.UuidCfdi!)
            .ToList();
        if (uuidsConValor.Count > 0)
        {
            var yaCapturados = await _db.FacturasProveedor
                .AsNoTracking()
                .Where(f => f.UuidCfdi != null && uuidsConValor.Contains(f.UuidCfdi)
                    && f.Estado != Domain.FacturaProveedor.EstadoPasivo.Cancelada)
                .Select(f => f.UuidCfdi!)
                .ToListAsync(cancellationToken);
            if (yaCapturados.Count > 0)
            {
                throw new BusinessRuleException(
                    "VIA_LINEA_FACTURA_DUPLICADA",
                    $"Los CFDIs {string.Join(", ", yaCapturados)} ya están capturados como facturas.");
            }
        }

        var ahora = _clock.UtcNow;
        var facturasIds = new List<Guid>();

        // Sucursal de las facturas = la del empleado en el catálogo de
        // Administración (P7-H4 — antes quedaba Guid.Empty). Si el
        // empleado no tiene sucursal asignada, se conserva el placeholder.
        var empleado = await _empleados.ObtenerAsync(s.EmpleadoId, cancellationToken);
        var sucursalId = empleado?.SucursalId ?? Guid.Empty;

        // CFDIs vinculados en la captura: al generar la factura se marcan
        // ConvertidoEnPasivo y se copia su MetodoPago (mismo patrón que
        // CapturarFacturaConOcHandler; marcado condicional — si otro flujo
        // ya los procesó, la duplicidad la detecta el check de UUIDs).
        var cfdiIds = s.Lineas
            .Where(l => l.CfdiRecibidoId is not null)
            .Select(l => l.CfdiRecibidoId!.Value)
            .Distinct()
            .ToList();
        var cfdisPorId = cfdiIds.Count == 0
            ? new Dictionary<Guid, CfdiRecibido>()
            : await _db.CfdisRecibidos
                .Where(c => cfdiIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        // Genera una FacturaProveedor por cada línea fiscal y vincula.
        // Acceso al método interno vía reflection no — uso una porción
        // del agregado directamente. Para mantener el invariante, llamo
        // al método interno VincularFactura vía el agregado.
        // El agregado expone BuscarLineaPorUuid (internal) para que el
        // handler en el mismo proyecto pueda usarlo.
        foreach (var linea in s.Lineas.Where(l => !l.EsTicketNoFiscal).ToList())
        {
            var factura = Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
                empresaId: s.EmpresaId,
                cfdiRecibidoId: linea.CfdiRecibidoId,
                uuidCfdi: linea.UuidCfdi!,
                proveedorId: linea.ProveedorId!.Value,
                sucursalId: sucursalId,
                folioProveedor: linea.FolioProveedor,
                serieProveedor: null,
                fechaDocumento: linea.FechaGasto,
                fechaContabilizacion: linea.FechaGasto,
                fechaVencimiento: DateOnly.FromDateTime(ahora.UtcDateTime.AddDays(30)),
                moneda: linea.Moneda,
                tipoCambio: null,
                subtotal: linea.Subtotal,
                descuentos: 0m,
                impuestosTrasladados: linea.ImpuestosTrasladados,
                retenciones: linea.Retenciones,
                total: linea.Total,
                motivoCaptura: $"Viáticos — solicitud {s.Id}",
                ahora: ahora);

            _db.FacturasProveedor.Add(factura);

            if (linea.CfdiRecibidoId is Guid cfdiId &&
                cfdisPorId.TryGetValue(cfdiId, out var cfdi))
            {
                if (cfdi.Estado == EstadoCfdiRecibido.PorProcesar)
                    cfdi.MarcarConvertidoEnPasivo(factura.Id);
                factura.AsignarMetodoPago(cfdi.MetodoPago);
            }

            // El método VincularFactura es internal — accesible porque
            // Domain y Application viven en el mismo assembly Millet.CuentasPorPagar.
            linea.VincularFactura(factura.Id);
            facturasIds.Add(factura.Id);
        }

        s.LiberarComprobacion(ahora);

        // GI-PR4 (doc 12 §D4/Q3): la diferencia de liquidación cruza a
        // Tesorería. Positiva = reembolso al empleado (pasivo interno);
        // negativa = el empleado devuelve (expectativa de depósito RN-6).
        var diferencia = s.DiferenciaLiquidacion ?? 0m;
        if (diferencia > 0)
        {
            await _events.PublishAsync(new PasivoAutorizadoParaPagoIntegrationEvent(
                EmpresaId: s.EmpresaId,
                OcurridoEn: ahora,
                // OrigenId ≠ solicitud: el pasivo del PRÉSTAMO ya ocupa la
                // clave física con s.Id — se deriva un id determinístico
                // (idempotente ante re-entregas) para la liquidación.
                FacturaProveedorId: Guid.Empty,
                ProveedorId: Guid.Empty,
                OrdenCompraId: null,
                MontoTotal: diferencia,
                SaldoPendiente: diferencia,
                Moneda: s.Moneda,
                TipoCambio: null,
                FechaVencimiento: DateOnly.FromDateTime(ahora.UtcDateTime),
                UuidCfdi: null,
                FolioProveedor: null,
                MetodoPago: null,
                TipoBeneficiario: PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioEmpleado,
                BeneficiarioId: s.EmpleadoId,
                OrigenTipo: PasivoAutorizadoParaPagoIntegrationEvent.OrigenLiquidacionViaticos,
                OrigenId: DerivarIdLiquidacion(s.Id)), cancellationToken);
        }
        else if (diferencia < 0)
        {
            await _events.PublishAsync(new DepositoViaticosEsperadoIntegrationEvent(
                EmpresaId: s.EmpresaId,
                OcurridoEn: ahora,
                SolicitudViaticosId: s.Id,
                EmpleadoId: s.EmpleadoId,
                MontoEsperado: Math.Abs(diferencia),
                Moneda: s.Moneda), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new LiberarComprobacionViaticosResponse(
            Id: s.Id,
            Estado: s.Estado,
            MontoComprobado: s.MontoComprobado ?? 0m,
            DiferenciaLiquidacion: s.DiferenciaLiquidacion ?? 0m,
            FacturasGeneradasIds: facturasIds,
            Version: s.Version);
    }

    /// <summary>
    /// Id determinístico del pasivo de liquidación (UUID v3-like sobre la
    /// solicitud): estable ante reintentos y distinto del id del préstamo.
    /// </summary>
    internal static Guid DerivarIdLiquidacion(Guid solicitudId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{solicitudId:N}::liquidacion-viaticos"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
