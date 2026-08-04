using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Cobros;

/// <summary>Forma de pago aplicada al cobro (suma = total, invariante del agregado).</summary>
public sealed record CobroFormaPagoInput(
    string FormaPago,
    decimal Importe,
    string? Referencia = null,
    string? CuentaOrdenante = null,
    string? CuentaBeneficiaria = null);

public sealed record CobroMostradorResponse(
    Guid Id, Guid ComprobanteId, Guid CajaSesionId, string Estado, decimal Total);

// ---- Registrar (12-cajas.md §6; permiso caja.operar) ----

/// <summary>
/// Registra el cobro de un comprobante timbrado en la sesión abierta del
/// cajero (`[Decisión 12-3]`/`[12-E]`: emitir y cobrar son dos comandos).
/// Genera un <c>caja_movimiento</c> por forma de pago (`[12-5]`) y asigna
/// <c>Comprobante.CajaId</c> — única vía de escritura (§6). Aplica a
/// <c>FacturaVenta</c>/<c>FacturaAnticipo</c> PUE y a <c>ReciboPago</c>
/// (PPD cobrado en mostrador); reparto usa <c>Origen = LiquidacionRuta</c>
/// (`[12-7]`, batch en <see cref="LiquidarRutaCommand"/> desde CAJAS-PR7).
/// </summary>
public sealed record RegistrarCobroMostradorCommand(
    Guid ComprobanteId,
    IReadOnlyList<CobroFormaPagoInput> FormasPago,
    OrigenCobroMostrador Origen = OrigenCobroMostrador.Mostrador) : IRequest<CobroMostradorResponse>;

public sealed class RegistrarCobroMostradorValidator : AbstractValidator<RegistrarCobroMostradorCommand>
{
    public RegistrarCobroMostradorValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEmpty();
        RuleFor(c => c.Origen).IsInEnum();
        RuleFor(c => c.FormasPago).NotEmpty().WithMessage("El cobro debe llevar al menos una forma de pago.");
        RuleForEach(c => c.FormasPago).ChildRules(f =>
        {
            f.RuleFor(x => x.FormaPago).NotEmpty().MaximumLength(2);
            f.RuleFor(x => x.Importe).GreaterThan(0);
            f.RuleFor(x => x.Referencia!).MaximumLength(100).When(x => x.Referencia is not null);
            f.RuleFor(x => x.CuentaOrdenante!).MaximumLength(50).When(x => x.CuentaOrdenante is not null);
            f.RuleFor(x => x.CuentaBeneficiaria!).MaximumLength(50).When(x => x.CuentaBeneficiaria is not null);
        });
    }
}

public sealed class RegistrarCobroMostradorHandler
    : IRequestHandler<RegistrarCobroMostradorCommand, CobroMostradorResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly IIntegrationEventPublisher _eventos;

    public RegistrarCobroMostradorHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock,
        ISucursalesReadPort sucursales,
        IAlcanceCajaEvaluator alcance,
        IIntegrationEventPublisher eventos)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _clock = clock;
        _sucursales = sucursales;
        _alcance = alcance;
        _eventos = eventos;
    }

    public async Task<CobroMostradorResponse> Handle(
        RegistrarCobroMostradorCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);
        var ahora = _clock.UtcNow;

        // El cobro entra a la sesión ABIERTA del cajero (responsable).
        var sesion = await _db.CajaSesiones.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.ResponsableUsuarioId == usuarioId && s.Estado == EstadoCajaSesion.Abierta,
                cancellationToken)
            ?? throw new BusinessRuleException(
                "COBRO_SIN_SESION", "No tienes una sesión de caja abierta; abre tu sesión antes de cobrar.");

        await BloqueoDiaAnterior.ValidarAsync(sesion, ahora, _sucursales, cancellationToken);

        // Comprobante dentro del alcance del cajero (fuera de alcance → 404, §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var comprobante = await alcance.AplicarA(_db.Comprobantes)
            .FirstOrDefaultAsync(c => c.Id == command.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{command.ComprobanteId}'.");

        var cobro = await CobroMostradorRegistrador.RegistrarAsync(
            _db, _eventos, empresaId, usuarioId, sesion, comprobante,
            command.FormasPago, command.Origen, ahora, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new CobroMostradorResponse(cobro.Id, comprobante.Id, sesion.Id, cobro.Estado.ToString(), cobro.Total);
    }
}

/// <summary>
/// Núcleo compartido del registro de cobro (unitario y batch de liquidación
/// de ruta, CAJAS-PR7): valida el comprobante, crea el cobro + movimientos,
/// asigna la caja (§6) y publica el evento. NO llama <c>SaveChanges</c> — la
/// frontera transaccional la decide el handler (el batch persiste todo-o-nada
/// con un solo <c>SaveChanges</c>).
/// </summary>
internal static class CobroMostradorRegistrador
{
    public static async Task<CobroMostrador> RegistrarAsync(
        FacturacionDbContext db,
        IIntegrationEventPublisher eventos,
        Guid empresaId,
        Guid usuarioId,
        CajaSesion sesion,
        Comprobante comprobante,
        IReadOnlyList<CobroFormaPagoInput> formasPago,
        OrigenCobroMostrador origen,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        if (comprobante.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "COBRO_COMPROBANTE_NO_TIMBRADO",
                $"Solo un comprobante Timbrado admite cobro (actual: {comprobante.Estado}).");
        if (comprobante.Tipo is not (TipoComprobante.Ingreso or TipoComprobante.Pago))
            throw new BusinessRuleException(
                "COBRO_TIPO_INVALIDO", "Solo facturas/anticipos (Ingreso) y REPP (Pago) admiten cobro de mostrador.");
        if (comprobante.Tipo == TipoComprobante.Ingreso && comprobante.MetodoPago == "PPD")
            throw new BusinessRuleException(
                "COBRO_FACTURA_PPD", "Una factura PPD se cobra vía REPP; registra el cobro sobre el REPP emitido.");
        if (!string.Equals(comprobante.Moneda, "MXN", StringComparison.Ordinal)
            && comprobante.Tipo != TipoComprobante.Pago)
            throw new BusinessRuleException("COBRO_MONEDA_INVALIDA", "La caja solo cobra MXN en v1 (P6).");

        var cobroVigente = await db.CobrosMostrador.AsNoTracking()
            .AnyAsync(c => c.ComprobanteId == comprobante.Id && c.Estado == EstadoCobroMostrador.Registrado,
                cancellationToken);
        if (cobroVigente)
            throw new ConflictException(
                "COBRO_YA_REGISTRADO",
                $"El comprobante '{comprobante.Folio}' ya tiene un cobro vigente; cancélalo antes de re-cobrar.");

        // Total esperado: Total del CFDI neto de las NC timbradas que lo
        // acreditan ([Decisión 13-K]: amortización de anticipos y NC generales
        // reducen lo que se cobra, nunca se reembolsan); para el REPP (tipo P,
        // Total = 0) el importe vive en la cabecera del complemento.
        decimal totalEsperado;
        if (comprobante.Tipo == TipoComprobante.Pago)
        {
            totalEsperado = await db.RecibosPago.AsNoTracking()
                .Where(r => r.Id == comprobante.Id)
                .Select(r => r.ImporteTotalPago)
                .SingleAsync(cancellationToken);
        }
        else
        {
            var acreditado = await SaldoPorCobrar.AcreditadoAsync(db, comprobante.Id, cancellationToken);
            totalEsperado = comprobante.Total - acreditado;
            if (totalEsperado <= 0)
                throw new BusinessRuleException(
                    "COBRO_SIN_SALDO",
                    $"El comprobante '{comprobante.Folio}' no tiene monto por cobrar: las notas de " +
                    $"crédito acreditan {acreditado} de un total de {comprobante.Total}.");
        }

        var totalCobro = formasPago.Sum(f => f.Importe);
        if (totalCobro != totalEsperado)
            throw new BusinessRuleException(
                "COBRO_TOTAL_NO_COINCIDE",
                $"La suma de formas de pago ({totalCobro}) no coincide con el monto por cobrar del comprobante '{comprobante.Folio}' ({totalEsperado}).");

        var cobro = CobroMostrador.Registrar(
            empresaId, sesion.Id, comprobante.SucursalId, comprobante.CanalVentaId,
            comprobante.Id, origen, ahora, usuarioId,
            formasPago
                .Select(f => (f.FormaPago, f.Importe, f.Referencia, f.CuentaOrdenante, f.CuentaBeneficiaria))
                .ToList());
        db.CobrosMostrador.Add(cobro);

        // Un movimiento por forma de pago ([Decisión 12-5]).
        foreach (var forma in cobro.FormasPago)
        {
            db.CajaMovimientos.Add(CajaMovimiento.Crear(
                empresaId, sesion.Id, TipoCajaMovimiento.CobroCliente, forma.FormaPago, forma.Importe,
                usuarioId, $"Cobro {comprobante.Folio}", forma.Referencia, cobro.Id));
        }

        // Única vía de escritura de Comprobante.CajaId (§6).
        comprobante.AsignarCajaCobro(sesion.CajaId);

        await eventos.PublishAsync(new CobroMostradorRegistradoIntegrationEvent(
            empresaId, ahora, cobro.Id, sesion.Id, sesion.CajaId, comprobante.Id,
            comprobante.Tipo.ToString(), cobro.Origen.ToString(), cobro.Total,
            cobro.FormasPago.Select(f => new CobroFormaPagoAplicada(f.FormaPago, f.Importe)).ToList()),
            cancellationToken);

        return cobro;
    }
}

// ---- Liquidación de ruta (batch, CAJAS-PR7, [Decisión 12-7]) ----

/// <summary>Un cobro de la ruta: comprobante (REPP o factura PUE) + formas de pago.</summary>
public sealed record LiquidacionRutaCobroInput(
    Guid ComprobanteId,
    IReadOnlyList<CobroFormaPagoInput> FormasPago);

/// <summary>
/// Registra en batch los cobros de una liquidación de ruta (`[Decisión 12-7]`):
/// el chofer entrega lo cobrado y el cajero captura todos los comprobantes de
/// la ruta de una vez. Todos entran a la sesión abierta del cajero con
/// <c>Origen = LiquidacionRuta</c> y se persisten <b>todo-o-nada</b> — si un
/// comprobante falla (alcance, total, ya cobrado) no se registra ninguno.
/// </summary>
public sealed record LiquidarRutaCommand(
    IReadOnlyList<LiquidacionRutaCobroInput> Cobros) : IRequest<LiquidacionRutaResponse>;

public sealed record LiquidacionRutaResponse(
    Guid CajaSesionId, decimal Total, IReadOnlyList<CobroMostradorResponse> Cobros);

public sealed class LiquidarRutaValidator : AbstractValidator<LiquidarRutaCommand>
{
    public LiquidarRutaValidator()
    {
        RuleFor(c => c.Cobros)
            .NotEmpty().WithMessage("La liquidación debe llevar al menos un cobro.")
            .Must(cobros => cobros.Count <= 100)
            .WithMessage("La liquidación admite máximo 100 cobros por captura.")
            .Must(cobros => cobros.Select(x => x.ComprobanteId).Distinct().Count() == cobros.Count)
            .WithMessage("La liquidación no admite el mismo comprobante dos veces.");
        RuleForEach(c => c.Cobros).ChildRules(item =>
        {
            item.RuleFor(x => x.ComprobanteId).NotEmpty();
            item.RuleFor(x => x.FormasPago).NotEmpty()
                .WithMessage("Cada cobro debe llevar al menos una forma de pago.");
            item.RuleForEach(x => x.FormasPago).ChildRules(f =>
            {
                f.RuleFor(x => x.FormaPago).NotEmpty().MaximumLength(2);
                f.RuleFor(x => x.Importe).GreaterThan(0);
                f.RuleFor(x => x.Referencia!).MaximumLength(100).When(x => x.Referencia is not null);
                f.RuleFor(x => x.CuentaOrdenante!).MaximumLength(50).When(x => x.CuentaOrdenante is not null);
                f.RuleFor(x => x.CuentaBeneficiaria!).MaximumLength(50).When(x => x.CuentaBeneficiaria is not null);
            });
        });
    }
}

public sealed class LiquidarRutaHandler : IRequestHandler<LiquidarRutaCommand, LiquidacionRutaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly IIntegrationEventPublisher _eventos;

    public LiquidarRutaHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock,
        ISucursalesReadPort sucursales,
        IAlcanceCajaEvaluator alcance,
        IIntegrationEventPublisher eventos)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _clock = clock;
        _sucursales = sucursales;
        _alcance = alcance;
        _eventos = eventos;
    }

    public async Task<LiquidacionRutaResponse> Handle(
        LiquidarRutaCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);
        var ahora = _clock.UtcNow;

        var sesion = await _db.CajaSesiones.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.ResponsableUsuarioId == usuarioId && s.Estado == EstadoCajaSesion.Abierta,
                cancellationToken)
            ?? throw new BusinessRuleException(
                "COBRO_SIN_SESION", "No tienes una sesión de caja abierta; abre tu sesión antes de liquidar la ruta.");

        await BloqueoDiaAnterior.ValidarAsync(sesion, ahora, _sucursales, cancellationToken);

        // Todos los comprobantes dentro del alcance del cajero, cargados de
        // una vez; cualquier faltante (inexistente o fuera de alcance) → 404
        // y no se registra nada.
        var ids = command.Cobros.Select(c => c.ComprobanteId).ToList();
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var comprobantes = await alcance.AplicarA(_db.Comprobantes)
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var faltantes = ids.Where(id => !comprobantes.ContainsKey(id)).ToList();
        if (faltantes.Count > 0)
            throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO",
                $"No existen (o están fuera de tu alcance) {faltantes.Count} comprobante(s) de la liquidación; el primero: '{faltantes[0]}'.");

        var cobros = new List<CobroMostradorResponse>(command.Cobros.Count);
        foreach (var item in command.Cobros)
        {
            var comprobante = comprobantes[item.ComprobanteId];
            var cobro = await CobroMostradorRegistrador.RegistrarAsync(
                _db, _eventos, empresaId, usuarioId, sesion, comprobante,
                item.FormasPago, OrigenCobroMostrador.LiquidacionRuta, ahora, cancellationToken);
            cobros.Add(new CobroMostradorResponse(
                cobro.Id, comprobante.Id, sesion.Id, cobro.Estado.ToString(), cobro.Total));
        }

        // Un solo SaveChanges: la liquidación completa es atómica.
        await _db.SaveChangesAsync(cancellationToken);
        return new LiquidacionRutaResponse(sesion.Id, cobros.Sum(c => c.Total), cobros);
    }
}

// ---- Cancelar (12-cajas.md §6; permiso caja.supervisar) ----

/// <summary>
/// Cancela un cobro: con sesión abierta del ejecutor → movimientos
/// <c>ReversaCobro</c> en su sesión; sin sesión → filas de
/// <c>caja_ajuste_pendiente</c> drenadas en la próxima apertura de la caja
/// afectada (`[Decisión 12-C]`). La sesión origen cerrada jamás se modifica.
/// </summary>
public sealed record CancelarCobroMostradorCommand(Guid CobroId, string Motivo) : IRequest<CobroMostradorResponse>;

public sealed class CancelarCobroMostradorValidator : AbstractValidator<CancelarCobroMostradorCommand>
{
    public CancelarCobroMostradorValidator()
    {
        RuleFor(c => c.CobroId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(254);
    }
}

public sealed class CancelarCobroMostradorHandler
    : IRequestHandler<CancelarCobroMostradorCommand, CobroMostradorResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IIntegrationEventPublisher _eventos;

    public CancelarCobroMostradorHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock,
        ISucursalesReadPort sucursales,
        IIntegrationEventPublisher eventos)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _clock = clock;
        _sucursales = sucursales;
        _eventos = eventos;
    }

    public async Task<CobroMostradorResponse> Handle(
        CancelarCobroMostradorCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);
        var ahora = _clock.UtcNow;

        var cobro = await _db.CobrosMostrador
            .Include(c => c.FormasPago)
            .FirstOrDefaultAsync(c => c.Id == command.CobroId, cancellationToken)
            ?? throw new EntityNotFoundException("COBRO_NO_ENCONTRADO", $"No existe el cobro '{command.CobroId}'.");

        cobro.Cancelar();

        // Libera la caja del comprobante para permitir un re-cobro.
        var comprobante = await _db.Comprobantes
            .FirstOrDefaultAsync(c => c.Id == cobro.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{cobro.ComprobanteId}'.");
        comprobante.DesasignarCajaCobro();

        // Sesión de la caja del cobro (para el ajuste pendiente si no hay sesión del ejecutor).
        var cajaAfectadaId = await _db.CajaSesiones.AsNoTracking()
            .Where(s => s.Id == cobro.CajaSesionId)
            .Select(s => s.CajaId)
            .SingleAsync(cancellationToken);

        // ¿El ejecutor tiene sesión abierta y del día vigente? → ReversaCobro
        // directa; si no → ajuste pendiente de la caja afectada ([12-C]).
        var sesionEjecutor = await _db.CajaSesiones.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.ResponsableUsuarioId == usuarioId && s.Estado == EstadoCajaSesion.Abierta,
                cancellationToken);
        var reversadoEnSesion = false;
        if (sesionEjecutor is not null)
        {
            var zona = await _sucursales.ObtenerZonaHorariaAsync(sesionEjecutor.SucursalId, cancellationToken);
            reversadoEnSesion = DiaOperacion.HoyLocal(ahora, zona) == sesionEjecutor.DiaOperacion;
        }

        foreach (var forma in cobro.FormasPago)
        {
            if (reversadoEnSesion)
            {
                _db.CajaMovimientos.Add(CajaMovimiento.Crear(
                    empresaId, sesionEjecutor!.Id, TipoCajaMovimiento.ReversaCobro, forma.FormaPago,
                    -forma.Importe, usuarioId, $"Reversa de cobro: {command.Motivo}",
                    cobroMostradorId: cobro.Id));
            }
            else
            {
                _db.CajaAjustesPendientes.Add(CajaAjustePendiente.Crear(
                    empresaId, cajaAfectadaId, cobro.Id, -forma.Importe, forma.FormaPago,
                    command.Motivo, usuarioId));
            }
        }

        await _eventos.PublishAsync(new CobroMostradorCanceladoIntegrationEvent(
            empresaId, ahora, cobro.Id, cobro.ComprobanteId, cobro.Total, reversadoEnSesion), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new CobroMostradorResponse(
            cobro.Id, cobro.ComprobanteId, cobro.CajaSesionId, cobro.Estado.ToString(), cobro.Total);
    }
}
