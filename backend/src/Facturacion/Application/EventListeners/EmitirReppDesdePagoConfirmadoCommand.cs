using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Domain.Eventos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.EventListeners;

// ============================================================================
// PR gemelo de TES-PR7: automatización del REPP al confirmarse un cobro en
// Tesorería — cierra PLATFORM-TODO(<PagoClienteConfirmado>). El endpoint
// manual POST /facturacion/repp sigue vivo como fallback (eventos
// pre-extensión sin Facturas[], errores de negocio marcados procesados).
//
// Idempotencia: la marca EventoProcesado se agrega ANTES de invocar
// EmitirReppCommand — el SaveChanges interno del handler de REPP persiste
// marca + comprobante + outbox del timbrado en la MISMA transacción; si el
// timbrado truena antes de guardar, nada queda y el retry es limpio.
// ============================================================================

/// <summary>
/// Sucursal emisora de los REPP automáticos (los cobros bancarios no
/// nacen en una sucursal física; el folio se reserva por sucursal). Se
/// resuelve por <see cref="SucursalClave"/> contra el catálogo
/// <c>compartido.sucursales</c>; sin configuración, cae a la única
/// sucursal activa si solo hay una.
/// </summary>
public sealed class ReppAutomaticoOptions
{
    public const string SectionName = "Facturacion:ReppAutomatico";

    /// <summary>Clave de la sucursal emisora (ej. "CON"). App setting en appservice.bicep.</summary>
    public string? SucursalClave { get; set; }
}

public sealed record EmitirReppDesdePagoConfirmadoCommand(
    Guid EventoId,
    PagoClienteConfirmadoPayload Payload) : IRequest;

public sealed class EmitirReppDesdePagoConfirmadoHandler
    : IRequestHandler<EmitirReppDesdePagoConfirmadoCommand>
{
    public const string EventType = PagoClienteConfirmadoPayload.EventType;

    /// <summary>c_FormaPago SAT: 03 = transferencia electrónica de fondos (el hecho confirmado es bancario).</summary>
    private const string FormaPagoTransferencia = "03";

    private readonly FacturacionDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ISender _sender;
    private readonly ReppAutomaticoOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<EmitirReppDesdePagoConfirmadoHandler> _logger;

    public EmitirReppDesdePagoConfirmadoHandler(
        FacturacionDbContext db,
        CompartidoDbContext compartido,
        ISender sender,
        IOptions<ReppAutomaticoOptions> options,
        IClock clock,
        ILogger<EmitirReppDesdePagoConfirmadoHandler> logger)
    {
        _db = db; _compartido = compartido; _sender = sender;
        _options = options.Value; _clock = clock; _logger = logger;
    }

    public async Task Handle(
        EmitirReppDesdePagoConfirmadoCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        if (p.Facturas is not { Count: > 0 })
        {
            // Propuesta publicada antes de la extensión aditiva de CxC: sin
            // desglose no hay REPP automático — queda el endpoint manual.
            _logger.LogWarning(
                "pago-cliente.confirmado sin Facturas[] (evento pre-extensión). Movimiento={MovimientoId}. REPP manual requerido.",
                p.MovimientoBancarioId);
            await MarcarProcesadoAsync(command.EventoId, $"sin facturas; movimiento={p.MovimientoBancarioId}", cancellationToken);
            return;
        }

        var sucursalId = await ResolverSucursalAsync(cancellationToken);

        try
        {
            // La marca viaja en el mismo SaveChanges del handler de REPP:
            // atómica con el comprobante y su evento de timbrado.
            _db.EventosProcesados.Add(new EventoProcesado(
                command.EventoId, EventType, ahora,
                detalle: $"movimiento={p.MovimientoBancarioId} propuesta={p.PropuestaId} monto={p.Monto} {p.Moneda}"));

            var response = await _sender.Send(new EmitirReppCommand(
                SucursalId: sucursalId,
                FechaPago: new DateTimeOffset(p.FechaValor.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                MonedaPago: p.Moneda,
                TcPago: null,
                FormaPagoReal: FormaPagoTransferencia,
                CuentaOrdenante: null,
                CuentaBeneficiaria: null,
                ReferenciaPago: p.Referencia,
                Facturas: p.Facturas
                    .Select(f => new ReppFacturaPago(f.FacturaVentaId, f.ImporteAplicado))
                    .ToList(),
                CanalVentaId: null, // cobro bancario → bucket "Sin asignar" [12-D]
                EmpresaId: p.EmpresaId), cancellationToken);

            _logger.LogInformation(
                "REPP automático emitido desde pago-cliente.confirmado. ReppId={ReppId} Estado={Estado} Movimiento={MovimientoId}",
                response.Id, response.Estado, p.MovimientoBancarioId);
        }
        catch (BusinessRuleException ex)
        {
            // Regla de negocio (sin saldo, no PPD, período cerrado…): el
            // retry no la resuelve — se marca procesado con el error y el
            // caso se atiende por el endpoint manual. La marca agregada
            // arriba quedó sin persistir (el SaveChanges no corrió).
            _logger.LogError(ex,
                "REPP automático rechazado por regla de negocio ({Code}). Movimiento={MovimientoId}. Emitir manualmente si procede.",
                ex.Code, p.MovimientoBancarioId);
            _db.ChangeTracker.Clear();
            await MarcarProcesadoAsync(command.EventoId,
                $"error-negocio {ex.Code}; movimiento={p.MovimientoBancarioId}", cancellationToken);
        }
    }

    private async Task<Guid> ResolverSucursalAsync(CancellationToken cancellationToken)
    {
        var clave = _options.SucursalClave?.Trim();
        if (!string.IsNullOrEmpty(clave))
        {
            var porClave = await _compartido.Sucursales.AsNoTracking()
                .Where(s => s.Clave == clave && s.Estatus == EstatusCatalogo.Activo)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (porClave is Guid id) return id;

            // Config apuntando a una sucursal inexistente/inactiva es un
            // problema operativo: que truene (retry → dead-letter visible).
            throw new InvalidOperationException(
                $"Facturacion:ReppAutomatico:SucursalClave='{clave}' no corresponde a una sucursal activa.");
        }

        var activas = await _compartido.Sucursales.AsNoTracking()
            .Where(s => s.Estatus == EstatusCatalogo.Activo)
            .Select(s => s.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return activas.Count switch
        {
            1 => activas[0],
            0 => throw new InvalidOperationException("No hay sucursales activas para emitir el REPP automático."),
            _ => throw new InvalidOperationException(
                "Hay varias sucursales activas: configura Facturacion:ReppAutomatico:SucursalClave."),
        };
    }

    private async Task MarcarProcesadoAsync(Guid eventoId, string detalle, CancellationToken cancellationToken)
    {
        _db.EventosProcesados.Add(new EventoProcesado(eventoId, EventType, _clock.UtcNow, detalle));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
