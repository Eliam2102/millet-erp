using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.DevolucionesInternas;

// ============================================================================
// Sub-flujo 8.A — Devolución Interna (F5-PR1).
//
// Modelado como `MovimientoInventario` tipo `DevolucionSalida` con
// `salida_origen_id` apuntando a la salida original. El trigger PG ya
// trata `DevolucionSalida` como entrada (sumando al saldo del
// sub-almacén destino) — ver `TipoMovimientoExtensions.EsEntrada`.
//
// **A9 — Costo histórico**: la línea de la devolución usa el costo
// snapshot de la línea de salida original (no el costo promedio actual).
// Esto preserva consistencia contable.
//
// **A15 — Material dañado**: si `EstadoMaterial=Danado`, el sub-almacén
// destino debe ser tipo `MaterialEnRevision` (MAT-REV) de la misma
// jerarquía Almacén. El handler valida.
// ============================================================================

public sealed record AplicarDevolucionInternaCommand(
    Guid SalidaOrigenId,
    Guid SubAlmacenDestinoId,
    DateOnly FechaMovimiento,
    string EstadoMaterial,
    string Motivo,
    string? Observaciones,
    IReadOnlyList<DevolucionInternaLineaInput> Lineas)
    : IRequest<AplicarDevolucionInternaResponse>;

public sealed record DevolucionInternaLineaInput(
    Guid LineaSalidaOrigenId,
    decimal CantidadADevolver,
    // C7.2b: bin real destino. La devolución interna es una entrada: obligatoria,
    // asignada, no ÚNICA. El artículo se deriva de la línea de salida origen.
    Guid? UbicacionId = null);

public sealed record AplicarDevolucionInternaResponse(Guid DevolucionId, string Folio);

public sealed class AplicarDevolucionInternaValidator
    : AbstractValidator<AplicarDevolucionInternaCommand>
{
    private static readonly string[] EstadosMaterialValidos = ["Integro", "UsadoParcial", "Danado"];

    public AplicarDevolucionInternaValidator()
    {
        RuleFor(c => c.SalidaOrigenId).NotEqual(Guid.Empty);
        RuleFor(c => c.SubAlmacenDestinoId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099);
        RuleFor(c => c.EstadoMaterial).NotEmpty()
            .Must(e => EstadosMaterialValidos.Contains(e))
            .WithMessage($"EstadoMaterial debe ser uno de: {string.Join(", ", EstadosMaterialValidos)}.");
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.LineaSalidaOrigenId).NotEqual(Guid.Empty);
            l.RuleFor(x => x.CantidadADevolver).GreaterThan(0);
            l.RuleFor(x => x.UbicacionId)
                .NotNull().NotEqual(Guid.Empty)
                .WithMessage("La ubicación (rack) destino es obligatoria.");
        });
    }
}

public sealed class AplicarDevolucionInternaHandler
    : IRequestHandler<AplicarDevolucionInternaCommand, AplicarDevolucionInternaResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public AplicarDevolucionInternaHandler(
        AlmacenDbContext db,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _events = events;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<AplicarDevolucionInternaResponse> Handle(
        AplicarDevolucionInternaCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar salida origen.
        var salidaOrigen = await _db.Movimientos.AsNoTracking()
            .Include(m => m.Lineas)
            .FirstOrDefaultAsync(m => m.Id == request.SalidaOrigenId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SALIDA_ORIGEN_NO_ENCONTRADA",
                $"No existe salida origen con id '{request.SalidaOrigenId}'.");
        if (salidaOrigen.Tipo is not TipoMovimiento.SalidaConsumo
            and not TipoMovimiento.SalidaPorVale)
        {
            throw new BusinessRuleException(
                "DEV_INT_ORIGEN_NO_SALIDA",
                "La devolución interna solo puede aplicarse contra una salida.");
        }
        if (salidaOrigen.Estado != EstadoMovimiento.Registrado)
        {
            throw new BusinessRuleException(
                "DEV_INT_ORIGEN_NO_REGISTRADA",
                $"La salida origen está en estado '{salidaOrigen.Estado}'.");
        }

        // 2. Validar sub-almacén destino. Si EstadoMaterial=Danado,
        //    debe ser tipo MaterialEnRevision (A15).
        var subAlmacenDestino = await _db.SubAlmacenes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SubAlmacenDestinoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUBALMACEN_NO_ENCONTRADO",
                $"No existe sub-almacén con id '{request.SubAlmacenDestinoId}'.");
        if (request.EstadoMaterial == "Danado"
            && subAlmacenDestino.Tipo != TipoSubAlmacen.MaterialEnRevision)
        {
            throw new BusinessRuleException(
                "DEV_INT_DANADO_DEBE_IR_A_REVISION",
                "El material dañado debe enviarse a un sub-almacén MaterialEnRevision (A15).");
        }

        // 3. Construir el movimiento DevolucionSalida.
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "DEV_INT_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado.
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, cancellationToken);

        // ADR-0046 Etapa 2: valida los decimales de cada cantidad a devolver
        // contra la unidad del artículo de su línea de salida origen (FK NULL →
        // no valida). Las líneas sin match se omiten (el loop de abajo lanza su
        // propio error). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas
                .Select(input => new
                {
                    input,
                    ls = salidaOrigen.Lineas.FirstOrDefault(l => l.Id == input.LineaSalidaOrigenId),
                })
                .Where(x => x.ls is not null)
                .Select(x => new CantidadAValidar(x.ls!.ArticuloId, x.input.CantidadADevolver, x.ls.UnidadMedida)),
            cancellationToken);

        var movimientoId = Guid.CreateVersion7();
        var movimiento = new MovimientoInventario(
            id: movimientoId,
            tipo: TipoMovimiento.DevolucionSalida,
            empresaId: empresaId,
            fechaMovimiento: request.FechaMovimiento);

        typeof(MovimientoInventario).GetProperty("SalidaOrigenId")!
            .SetValue(movimiento, request.SalidaOrigenId);
        typeof(MovimientoInventario).GetProperty("Motivo")!
            .SetValue(movimiento, request.Motivo);
        typeof(MovimientoInventario).GetProperty("EstadoMaterial")!
            .SetValue(movimiento, request.EstadoMaterial);
        if (!string.IsNullOrWhiteSpace(request.Observaciones))
        {
            typeof(MovimientoInventario).GetProperty("ComentarioLibre")!
                .SetValue(movimiento, request.Observaciones);
        }

        // 4. Líneas — costo snapshot de la salida origen (A9).
        var posicion = 1;
        var payload = new List<LineaDevolucionPayload>(request.Lineas.Count);
        decimal costoTotal = 0m;
        foreach (var input in request.Lineas)
        {
            var lineaSalida = salidaOrigen.Lineas.FirstOrDefault(l => l.Id == input.LineaSalidaOrigenId)
                ?? throw new EntityNotFoundException(
                    "DEV_INT_LINEA_ORIGEN_NO_ENCONTRADA",
                    $"No existe línea '{input.LineaSalidaOrigenId}' en la salida origen.");

            // C7.2b: bin real destino (entrada) — el artículo viene de la salida
            // origen; debe estar asignado a la ubicación y no ser la ÚNICA.
            await UbicacionEntradaGuard.ValidarUbicacionEntradaAsync(
                _db, input.UbicacionId!.Value, request.SubAlmacenDestinoId,
                lineaSalida.ArticuloId, cancellationToken);

            // No se puede devolver más de lo que salió.
            // (F5-PR1 no rastrea devoluciones previas en BD; pre-flight básico.)
            if (input.CantidadADevolver > lineaSalida.Cantidad)
            {
                throw new BusinessRuleException(
                    "DEV_INT_EXCEDE_SALIDA",
                    $"Cantidad a devolver ({input.CantidadADevolver}) excede la salida origen ({lineaSalida.Cantidad}).");
            }

            var lineaId = Guid.CreateVersion7();
            var linea = new LineaMovimiento(
                id: lineaId,
                movimientoId: movimientoId,
                posicion: posicion++,
                articuloId: lineaSalida.ArticuloId,
                cantidad: input.CantidadADevolver,
                unidadMedida: lineaSalida.UnidadMedida,
                costoUnitarioMxn: lineaSalida.CostoUnitarioMxn, // A9 — snapshot de la salida
                ubicacionId: input.UbicacionId);
            movimiento.AgregarLinea(linea);

            var monto = Math.Round(input.CantidadADevolver * lineaSalida.CostoUnitarioMxn, 2);
            costoTotal += monto;
            payload.Add(new LineaDevolucionPayload(
                LineaDevolucionId: lineaId,
                ArticuloId: lineaSalida.ArticuloId,
                UnidadMedida: lineaSalida.UnidadMedida,
                Cantidad: input.CantidadADevolver,
                CostoUnitarioMxn: lineaSalida.CostoUnitarioMxn,
                MontoTotalMxn: monto));
        }

        // 5. Folio + Registrar.
        var anio = request.FechaMovimiento.Year;
        var prefijo = TipoMovimiento.DevolucionSalida.PrefijoFolio();
        var secuencia = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        var siguiente = secuencia.Incrementar();
        var folio = FolioMovimiento.Construir(TipoMovimiento.DevolucionSalida, anio, siguiente);

        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "DEV_INT_SIN_USUARIO", "Se requiere usuario autenticado.");
        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);

        // 6. Evento.
        await _events.PublishAsync(new DevolucionInternaAplicadaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            DevolucionId: movimientoId,
            FolioDevolucion: folio.Valor,
            SalidaOrigenId: request.SalidaOrigenId,
            SubAlmacenDestinoId: request.SubAlmacenDestinoId,
            EstadoMaterial: request.EstadoMaterial,
            CostoTotalRevertidoMxn: Math.Round(costoTotal, 2),
            Lineas: payload), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new AplicarDevolucionInternaResponse(movimientoId, folio.Valor);
    }
}

// ─── MAT-REV: BajaPorDano + ReincorporacionTrasRevision ──────────────────────

/// <summary>
/// Calidad decide que el material en MAT-REV debe destruirse.
/// Genera movimiento <c>BajaPorDano</c> (salida) en el sub-almacén
/// MAT-REV — el trigger PG decrementa el saldo.
/// </summary>
public sealed record BajaPorDanoCommand(
    Guid SubAlmacenMatRevId,
    DateOnly FechaMovimiento,
    string Motivo,
    IReadOnlyList<MatRevLineaInput> Lineas) : IRequest<BajaPorDanoResponse>;

public sealed record MatRevLineaInput(
    Guid ArticuloId,
    decimal Cantidad,
    // C7.2b: bin real. Reincorporación (entrada) lo exige asignado/no-ÚNICA;
    // BajaPorDano (salida) elige un bin con saldo del sub-almacén MAT-REV.
    Guid? UbicacionId = null);

public sealed record BajaPorDanoResponse(Guid MovimientoId, string Folio);

public sealed class BajaPorDanoValidator : AbstractValidator<BajaPorDanoCommand>
{
    public BajaPorDanoValidator()
    {
        RuleFor(c => c.SubAlmacenMatRevId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.ArticuloId).NotEqual(Guid.Empty);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
        });
    }
}

public sealed class BajaPorDanoHandler : IRequestHandler<BajaPorDanoCommand, BajaPorDanoResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public BajaPorDanoHandler(
        AlmacenDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<BajaPorDanoResponse> Handle(
        BajaPorDanoCommand request, CancellationToken cancellationToken)
    {
        var sub = await _db.SubAlmacenes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SubAlmacenMatRevId, cancellationToken)
            ?? throw new EntityNotFoundException("SUBALMACEN_NO_ENCONTRADO",
                $"No existe sub-almacén '{request.SubAlmacenMatRevId}'.");
        if (sub.Tipo != TipoSubAlmacen.MaterialEnRevision)
        {
            throw new BusinessRuleException(
                "BAJA_DEBE_SER_MAT_REV",
                "BajaPorDano solo aplica a sub-almacenes MaterialEnRevision (A15).");
        }

        return await CrearMovimientoMatRevAsync(
            request.SubAlmacenMatRevId, request.FechaMovimiento, request.Motivo,
            request.Lineas, TipoMovimiento.BajaPorDano, cancellationToken);
    }

    internal async Task<BajaPorDanoResponse> CrearMovimientoMatRevAsync(
        Guid subAlmacenId,
        DateOnly fechaMovimiento,
        string motivo,
        IReadOnlyList<MatRevLineaInput> lineas,
        TipoMovimiento tipo,
        CancellationToken cancellationToken)
    {
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "MOV_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // ADR-0046 Etapa 2: valida los decimales de cada línea MatRev contra la
        // unidad del artículo (FK NULL → no valida). Cubre BajaPorDano y
        // ReincorporacionTrasRevision (ambos pasan por aquí). Batch.
        await _decimalesGuard.ValidarAsync(
            lineas.Select(l => new CantidadAValidar(l.ArticuloId, l.Cantidad)),
            cancellationToken);

        var movimientoId = Guid.CreateVersion7();
        var movimiento = new MovimientoInventario(
            id: movimientoId, tipo: tipo, empresaId: empresaId,
            fechaMovimiento: fechaMovimiento);

        typeof(MovimientoInventario).GetProperty("Motivo")!.SetValue(movimiento, motivo);

        var esEntrada = tipo.EsEntrada();

        // C7.2b: costo snapshot por el bin elegido (fallback ÚNICA si la línea
        // no manda bin — path de compatibilidad). Solo se llavea la lectura;
        // la fórmula del promedio no cambia.
        var ubicacionDefaultId = await _db.Ubicaciones.AsNoTracking()
            .Where(u => u.SubAlmacenId == subAlmacenId && u.EsDefault)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var posicion = 1;
        foreach (var l in lineas)
        {
            // Reincorporación es entrada: bin obligatorio, asignado, no ÚNICA.
            // BajaPorDano es salida: elige un bin con saldo (guard del trigger).
            if (esEntrada)
            {
                await UbicacionEntradaGuard.ValidarUbicacionEntradaAsync(
                    _db, l.UbicacionId!.Value, subAlmacenId, l.ArticuloId, cancellationToken);
            }

            var ubicacionCosto = l.UbicacionId ?? ubicacionDefaultId;
            var saldo = await _db.SaldosInventario.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UbicacionId == ubicacionCosto && s.ArticuloId == l.ArticuloId,
                    cancellationToken);
            var costo = saldo?.CostoPromedioMxn ?? 0m;
            var linea = new LineaMovimiento(
                id: Guid.CreateVersion7(),
                movimientoId: movimientoId,
                posicion: posicion++,
                articuloId: l.ArticuloId,
                cantidad: l.Cantidad,
                unidadMedida: "PZA",
                costoUnitarioMxn: costo,
                ubicacionId: l.UbicacionId);
            movimiento.AgregarLinea(linea);
        }

        var anio = fechaMovimiento.Year;
        var prefijo = tipo.PrefijoFolio();
        var sec = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken)
            ?? new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
        if (_db.Entry(sec).State == EntityState.Detached)
        {
            _db.FolioSecuenciasMovimiento.Add(sec);
        }
        var folio = FolioMovimiento.Construir(tipo, anio, sec.Incrementar());

        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "MOV_SIN_USUARIO", "Se requiere usuario autenticado.");
        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);
        await _db.SaveChangesAsync(cancellationToken);

        return new BajaPorDanoResponse(movimientoId, folio.Valor);
    }
}

/// <summary>
/// Material en MAT-REV se reincorpora al inventario activo (Calidad
/// determina que sí es usable). Se modela como entrada al sub-almacén
/// destino — el caller indica a dónde reincorporar.
/// </summary>
public sealed record ReincorporacionTrasRevisionCommand(
    Guid SubAlmacenDestinoId,
    DateOnly FechaMovimiento,
    string Motivo,
    IReadOnlyList<MatRevLineaInput> Lineas) : IRequest<BajaPorDanoResponse>;

public sealed class ReincorporacionTrasRevisionValidator
    : AbstractValidator<ReincorporacionTrasRevisionCommand>
{
    public ReincorporacionTrasRevisionValidator()
    {
        RuleFor(c => c.SubAlmacenDestinoId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.ArticuloId).NotEqual(Guid.Empty);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
            // Entrada: bin obligatorio (BajaPorDano, salida, no lo exige).
            l.RuleFor(x => x.UbicacionId)
                .NotNull().NotEqual(Guid.Empty)
                .WithMessage("La ubicación (rack) destino es obligatoria.");
        });
    }
}

public sealed class ReincorporacionTrasRevisionHandler
    : IRequestHandler<ReincorporacionTrasRevisionCommand, BajaPorDanoResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public ReincorporacionTrasRevisionHandler(
        AlmacenDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<BajaPorDanoResponse> Handle(
        ReincorporacionTrasRevisionCommand request, CancellationToken cancellationToken)
    {
        // No requiere que el sub-almacén destino sea MAT-REV — al
        // contrario, se reincorpora al inventario activo (sub-almacén
        // tipo Insumos / MaterialesDirectos).
        return await new BajaPorDanoHandler(_db, _currentUser, _currentEmpresa, _decimalesGuard)
            .CrearMovimientoMatRevAsync(
                request.SubAlmacenDestinoId, request.FechaMovimiento,
                request.Motivo, request.Lineas,
                TipoMovimiento.ReincorporacionTrasRevision, cancellationToken);
    }
}
