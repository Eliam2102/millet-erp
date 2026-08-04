using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Conteos;

// ============================================================================
// F7-PR2: recuento obligatorio (A7) + aprobación por monto (A8) + Aplicar.
//
// Umbrales A7 (configurables — F7-PR2 los hardcodea, F9 los moverá a
// admin.parametros):
//   - Variación absoluta > 5% (en cantidad)
//   - Variación valor > 1,000 MXN
//   → marca línea como RequiereRecuento.
//
// Umbrales A8 (política por monto agregado del conteo):
//   - <  $1K MXN  → Nivel 1 (Almacenista).
//   - $1K – $10K  → Nivel 2 (Supervisor).
//   - >  $10K MXN → Nivel 3 (Jefe Almacén + notificación Finanzas).
// El permiso de aprobación se exige al endpoint según el monto neto.
// ============================================================================

public static class ConteoUmbrales
{
    public const decimal VariacionPctParaRecuento = 5m;
    public const decimal VariacionValorParaRecuento = 1000m;
    public const decimal UmbralNivel1Maximo = 1000m;
    public const decimal UmbralNivel2Maximo = 10000m;
}

// ─── Agregar recuento ────────────────────────────────────────────────────────

public sealed record AgregarRecuentoCommand(
    Guid ConteoId,
    Guid LineaConteoId,
    decimal CantidadRecontada) : IRequest<AgregarRecuentoResponse>;

public sealed record AgregarRecuentoResponse(Guid RecuentoId, int Secuencia);

public sealed class AgregarRecuentoValidator : AbstractValidator<AgregarRecuentoCommand>
{
    public AgregarRecuentoValidator()
    {
        RuleFor(c => c.ConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.LineaConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.CantidadRecontada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AgregarRecuentoHandler : IRequestHandler<AgregarRecuentoCommand, AgregarRecuentoResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public AgregarRecuentoHandler(
        AlmacenDbContext db, ICurrentUserContext currentUser, IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db; _currentUser = currentUser; _decimalesGuard = decimalesGuard;
    }

    public async Task<AgregarRecuentoResponse> Handle(
        AgregarRecuentoCommand request, CancellationToken cancellationToken)
    {
        var linea = await _db.Set<LineaConteo>()
            .FirstOrDefaultAsync(l => l.Id == request.LineaConteoId
                && l.ConteoId == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("LINEA_CONTEO_NO_ENCONTRADA",
                $"No existe línea '{request.LineaConteoId}' en conteo '{request.ConteoId}'.");

        // Determina siguiente secuencia.
        var maxSec = await _db.Set<RecuentoConteo>()
            .Where(r => r.LineaConteoId == request.LineaConteoId)
            .Select(r => (int?)r.Secuencia)
            .MaxAsync(cancellationToken) ?? 0;
        var siguiente = maxSec + 1;

        var capturadoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "RECUENTO_SIN_USUARIO", "Se requiere usuario autenticado.");

        // ADR-0046 Etapa 2: valida los decimales de la cantidad recontada contra
        // la unidad del artículo de la línea (FK NULL → no valida).
        await _decimalesGuard.ValidarAsync(
            new[] { new CantidadAValidar(linea.ArticuloId, request.CantidadRecontada) },
            cancellationToken);

        var recuento = new RecuentoConteo(
            id: Guid.CreateVersion7(),
            lineaConteoId: request.LineaConteoId,
            secuencia: siguiente,
            cantidadRecontada: request.CantidadRecontada,
            capturadoPor: capturadoPor);
        _db.Set<RecuentoConteo>().Add(recuento);
        await _db.SaveChangesAsync(cancellationToken);

        return new AgregarRecuentoResponse(recuento.Id, siguiente);
    }
}

// ─── Marcar líneas que requieren recuento (A7) ───────────────────────────────

public sealed record EvaluarVariacionesConteoCommand(Guid ConteoId) : IRequest<int>;

public sealed class EvaluarVariacionesConteoHandler : IRequestHandler<EvaluarVariacionesConteoCommand, int>
{
    private readonly AlmacenDbContext _db;
    public EvaluarVariacionesConteoHandler(AlmacenDbContext db) => _db = db;

    public async Task<int> Handle(
        EvaluarVariacionesConteoCommand request, CancellationToken cancellationToken)
    {
        var lineas = await _db.Set<LineaConteo>()
            .Where(l => l.ConteoId == request.ConteoId)
            .ToListAsync(cancellationToken);

        var marcadas = 0;
        foreach (var l in lineas)
        {
            if (l.CantidadRealCapturada is not decimal real) continue;
            var diff = real - l.CantidadTeorica;
            var diffAbs = Math.Abs(diff);
            var pct = l.CantidadTeorica == 0 ? 100m : diffAbs / l.CantidadTeorica * 100m;
            var valor = Math.Abs(diff * l.CostoPromedioSnapshot);
            var excedeUmbral = pct > ConteoUmbrales.VariacionPctParaRecuento
                || valor > ConteoUmbrales.VariacionValorParaRecuento;
            if (excedeUmbral && !l.RequiereRecuento)
            {
                l.MarcarRequiereRecuento();
                marcadas++;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return marcadas;
    }
}

// ─── Aprobar conteo ──────────────────────────────────────────────────────────

public sealed record AprobarConteoCommand(Guid ConteoId) : IRequest;

public sealed class AprobarConteoHandler : IRequestHandler<AprobarConteoCommand>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public AprobarConteoHandler(AlmacenDbContext db, ICurrentUserContext currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task Handle(AprobarConteoCommand request, CancellationToken cancellationToken)
    {
        var conteo = await _db.Set<ConteoInventario>()
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTEO_NO_ENCONTRADO",
                $"No existe conteo con id '{request.ConteoId}'.");

        // Validar: líneas con RequiereRecuento deben tener al menos un
        // recuento O estar aprobadas individualmente.
        var lineasConflictivas = conteo.Lineas
            .Where(l => l.RequiereRecuento && !l.AprobadoIndividualmente)
            .Select(l => l.Id)
            .ToList();
        if (lineasConflictivas.Count > 0)
        {
            var lineaIds = string.Join(",", lineasConflictivas);
            var recuentos = await _db.Set<RecuentoConteo>()
                .Where(r => lineasConflictivas.Contains(r.LineaConteoId))
                .Select(r => r.LineaConteoId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var sinRecuento = lineasConflictivas.Except(recuentos).ToList();
            if (sinRecuento.Count > 0)
            {
                throw new BusinessRuleException(
                    "CONTEO_APROBAR_LINEAS_PENDIENTES_RECUENTO",
                    $"Las siguientes líneas requieren recuento o aprobación individual: {string.Join(",", sinRecuento)}.");
            }
        }

        var aprobadorId = _currentUser.UserId ?? throw new BusinessRuleException(
            "CONTEO_SIN_APROBADOR", "Se requiere usuario autenticado.");
        conteo.Aprobar(aprobadorId);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record AprobarLineaIndividualmenteCommand(
    Guid ConteoId,
    Guid LineaConteoId,
    string Justificacion) : IRequest;

public sealed class AprobarLineaIndividualmenteValidator
    : AbstractValidator<AprobarLineaIndividualmenteCommand>
{
    public AprobarLineaIndividualmenteValidator()
    {
        RuleFor(c => c.ConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.LineaConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.Justificacion).NotEmpty().MaximumLength(500);
    }
}

public sealed class AprobarLineaIndividualmenteHandler
    : IRequestHandler<AprobarLineaIndividualmenteCommand>
{
    private readonly AlmacenDbContext _db;
    public AprobarLineaIndividualmenteHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(
        AprobarLineaIndividualmenteCommand request, CancellationToken cancellationToken)
    {
        var linea = await _db.Set<LineaConteo>()
            .FirstOrDefaultAsync(l => l.Id == request.LineaConteoId
                && l.ConteoId == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("LINEA_CONTEO_NO_ENCONTRADA",
                $"No existe línea '{request.LineaConteoId}' en el conteo.");
        linea.AprobarConJustificacion(request.Justificacion);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Aplicar conteo (genera ajustes en batch) ────────────────────────────────

public sealed record AplicarConteoCommand(Guid ConteoId) : IRequest<AplicarConteoResponse>;

public sealed record AplicarConteoResponse(
    int MovimientosGenerados,
    decimal MontoNetoMxn);

public sealed class AplicarConteoHandler : IRequestHandler<AplicarConteoCommand, AplicarConteoResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public AplicarConteoHandler(
        AlmacenDbContext db,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa)
    {
        _db = db; _events = events;
        _currentUser = currentUser; _currentEmpresa = currentEmpresa;
    }

    public async Task<AplicarConteoResponse> Handle(
        AplicarConteoCommand request, CancellationToken cancellationToken)
    {
        var conteo = await _db.Set<ConteoInventario>()
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTEO_NO_ENCONTRADO",
                $"No existe conteo con id '{request.ConteoId}'.");

        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "CONTEO_SIN_EMPRESA", "Contexto de empresa requerido.");
        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "CONTEO_SIN_USUARIO", "Se requiere usuario autenticado.");

        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        var anio = fecha.Year;

        // Reservar secuencias (una sola vez para AjustePositivo y AjusteNegativo).
        var secPos = await ObtenerOSecuenciaAsync(TipoMovimiento.AjustePositivo, anio, cancellationToken);
        var secNeg = await ObtenerOSecuenciaAsync(TipoMovimiento.AjusteNegativo, anio, cancellationToken);

        var payload = new List<AjusteInventarioPayload>();
        decimal montoNeto = 0m;
        var movimientosGenerados = 0;

        foreach (var linea in conteo.Lineas)
        {
            if (linea.CantidadRealCapturada is not decimal real) continue;
            var diff = real - linea.CantidadTeorica;
            if (diff == 0) continue;

            var tipo = diff > 0
                ? TipoMovimiento.AjustePositivo
                : TipoMovimiento.AjusteNegativo;
            var sec = tipo == TipoMovimiento.AjustePositivo ? secPos : secNeg;

            var movId = Guid.CreateVersion7();
            var movimiento = new MovimientoInventario(
                id: movId,
                tipo: tipo,
                empresaId: empresaId,
                fechaMovimiento: fecha);
            typeof(MovimientoInventario).GetProperty("ConteoId")!.SetValue(movimiento, conteo.Id);

            var cantidadAjuste = Math.Abs(diff);
            var monto = Math.Round(cantidadAjuste * linea.CostoPromedioSnapshot, 2);

            var movLinea = new LineaMovimiento(
                id: Guid.CreateVersion7(),
                movimientoId: movId,
                posicion: 1,
                articuloId: linea.ArticuloId,
                cantidad: cantidadAjuste,
                unidadMedida: "PZA",
                costoUnitarioMxn: linea.CostoPromedioSnapshot,
                // C7.2c: el ajuste golpea el rack contado. AjustePositivo a un
                // rack real lo permite el trigger; AjusteNegativo descuenta ese
                // bin (guard SALDO_* por bin).
                ubicacionId: linea.UbicacionId);
            movLinea.AsentarConteo(linea.CantidadTeorica, real);
            movimiento.AgregarLinea(movLinea);

            var siguiente = sec.Incrementar();
            var folio = FolioMovimiento.Construir(tipo, anio, siguiente);
            movimiento.Registrar(folio, registradoPor);
            _db.Movimientos.Add(movimiento);

            payload.Add(new AjusteInventarioPayload(
                MovimientoId: movId,
                FolioMovimiento: folio.Valor,
                Tipo: tipo.ToString(),
                SubAlmacenId: linea.SubAlmacenId,
                UbicacionId: linea.UbicacionId,
                ArticuloId: linea.ArticuloId,
                CantidadAjustada: cantidadAjuste,
                CostoUnitarioMxn: linea.CostoPromedioSnapshot,
                MontoMxn: monto));
            montoNeto += diff > 0 ? monto : -monto;
            movimientosGenerados++;
        }

        conteo.MarcarAplicado();

        // F7-PR3 (A18): libera bloqueos activos del conteo (Anual).
        var bloqueosActivos = await _db.Set<Domain.Conteos.BloqueoInventario>()
            .Where(b => b.ConteoId == conteo.Id && b.Activo)
            .ToListAsync(cancellationToken);
        foreach (var b in bloqueosActivos) b.Liberar();

        await _events.PublishAsync(new AjusteInventarioAplicadoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            ConteoId: conteo.Id,
            MontoNetoMxn: Math.Round(montoNeto, 2),
            AprobadorId: conteo.AprobadorId ?? Guid.Empty,
            MovimientosGenerados: payload), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new AplicarConteoResponse(movimientosGenerados, Math.Round(montoNeto, 2));
    }

    private async Task<FolioSecuenciaMovimiento> ObtenerOSecuenciaAsync(
        TipoMovimiento tipo, int anio, CancellationToken cancellationToken)
    {
        var prefijo = tipo.PrefijoFolio();
        var sec = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (sec is null)
        {
            sec = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(sec);
        }
        return sec;
    }
}

// ─── Rechazar conteo ────────────────────────────────────────────────────────

public sealed record RechazarConteoCommand(Guid ConteoId, string Motivo) : IRequest;

public sealed class RechazarConteoValidator : AbstractValidator<RechazarConteoCommand>
{
    public RechazarConteoValidator()
    {
        RuleFor(c => c.ConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(500);
    }
}

public sealed class RechazarConteoHandler : IRequestHandler<RechazarConteoCommand>
{
    private readonly AlmacenDbContext _db;
    public RechazarConteoHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(RechazarConteoCommand request, CancellationToken cancellationToken)
    {
        var conteo = await _db.Set<ConteoInventario>()
            .FirstOrDefaultAsync(c => c.Id == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTEO_NO_ENCONTRADO",
                $"No existe conteo con id '{request.ConteoId}'.");
        conteo.Rechazar(request.Motivo);

        // F7-PR3 (A18): liberar bloqueos también en rechazo.
        var bloqueos = await _db.Set<Domain.Conteos.BloqueoInventario>()
            .Where(b => b.ConteoId == conteo.Id && b.Activo)
            .ToListAsync(cancellationToken);
        foreach (var b in bloqueos) b.Liberar();

        await _db.SaveChangesAsync(cancellationToken);
    }
}
