using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Vales;

/// <summary>
/// F5-PR1: registra una <b>salida urgente por vale</b> (Variante B —
/// el solicitante no tuvo tiempo de tramitar RQ). Marca el movimiento
/// con <c>pendiente_regularizacion=true</c> y <c>fecha_limite=+48h</c>
/// (A14). Publica <c>SalidaRequisicionRegistradaEvent</c> con
/// <c>EsPorVale=true</c> y <c>RqId=null</c>.
///
/// <para>
/// La regularización vincula la RQ posterior vía
/// <see cref="RegularizarSalidaPorValeCommand"/>. El
/// <c>RegularizacionValeSlaWorker</c> notifica día 1 al Coordinador
/// y día 2 al Jefe Almacén si no se regulariza (sin bloqueo
/// automático, A14).
/// </para>
/// </summary>
// Salida-por-línea (vale): el sub-almacén ya NO viaja en la cabecera. Se DERIVA
// del bin (Ubicacion.SubAlmacenId) de las líneas, obligatorio. Mismo molde que la
// salida-con-RQ (#724). RegularizarSalidaPorVale NO se toca (flag-flip puro).
public sealed record RegistrarSalidaPorValeCommand(
    DateOnly FechaMovimiento,
    string ValeBlobRef,
    Guid? PersonaDestinatariaId,
    string? Observaciones,
    IReadOnlyList<RegistrarSalidaLineaInput> Lineas) : IRequest<RegistrarSalidaResponse>;

public sealed class RegistrarSalidaPorValeValidator
    : AbstractValidator<RegistrarSalidaPorValeCommand>
{
    public RegistrarSalidaPorValeValidator()
    {
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099)
            .WithMessage("Fecha de movimiento fuera de rango razonable.");
        RuleFor(c => c.ValeBlobRef).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(linea =>
        {
            linea.RuleFor(l => l.ArticuloId).NotEqual(Guid.Empty);
            linea.RuleFor(l => l.Cantidad).GreaterThan(0);
            // Salida-por-línea (vale): el bin es obligatorio. El record compartido
            // RegistrarSalidaLineaInput se mantiene Guid? (decisión F1); la
            // obligatoriedad se impone acá, en el validador del vale.
            linea.RuleFor(l => l.UbicacionId)
                .NotNull()
                .WithMessage("Elige la ubicación (bin) de donde sale la línea.");
        });
    }
}

public sealed class RegistrarSalidaPorValeHandler
    : IRequestHandler<RegistrarSalidaPorValeCommand, RegistrarSalidaResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public RegistrarSalidaPorValeHandler(
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

    public async Task<RegistrarSalidaResponse> Handle(
        RegistrarSalidaPorValeCommand request, CancellationToken cancellationToken)
    {
        // Salida-por-línea (vale): el sub-almacén se DERIVA del bin de cada línea
        // (obligatorio; validado en el validator, re-validado acá por defensa). El
        // invariante "un movimiento = un sub-almacén" lo respalda el trigger PG
        // (MOVIMIENTO_MULTI_SUBALMACEN); acá se anticipa con error legible. Mismo
        // molde que RegistrarSalidaConRequisicionHandler (#724).
        if (request.Lineas.Any(l => l.UbicacionId is null))
        {
            throw new BusinessRuleException(
                "SALIDA_UBICACION_REQUERIDA",
                "Cada línea del vale debe indicar la ubicación (bin) de donde sale.");
        }
        var binIds = request.Lineas.Select(l => l.UbicacionId!.Value).Distinct().ToList();
        var subsDerivados = await _db.Ubicaciones.AsNoTracking()
            .Where(u => binIds.Contains(u.Id))
            .Select(u => u.SubAlmacenId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (subsDerivados.Count != 1)
        {
            throw new BusinessRuleException(
                "SALIDA_MULTI_SUBALMACEN",
                "Todas las líneas del vale deben salir de ubicaciones del mismo " +
                "sub-almacén. Revisa los bins elegidos.");
        }
        var subAlmacenId = subsDerivados[0];

        // F7-PR3 (A18): si hay bloqueo activo de salidas para el sub DERIVADO, rechazar.
        var bloqueado = await _db.Set<Domain.Conteos.BloqueoInventario>()
            .AsNoTracking()
            .AnyAsync(b => b.SubAlmacenId == subAlmacenId
                && b.Activo
                && b.BloqueaSalidas, cancellationToken);
        if (bloqueado)
        {
            throw new BusinessRuleException(
                "SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL",
                $"El sub-almacén '{subAlmacenId}' está en conteo anual; salidas bloqueadas.");
        }

        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "VALE_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado.
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, cancellationToken);

        // ADR-0046 Etapa 2: valida los decimales de cada línea contra la unidad
        // del artículo (FK NULL → no valida). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas.Select(l => new CantidadAValidar(l.ArticuloId, l.Cantidad)),
            cancellationToken);

        var movimientoId = Guid.CreateVersion7();
        var movimiento = new MovimientoInventario(
            id: movimientoId,
            tipo: TipoMovimiento.SalidaPorVale,
            empresaId: empresaId,
            fechaMovimiento: request.FechaMovimiento);

        // Vincular salida vale (marca pendiente_regularizacion + fecha_limite=+48h).
        typeof(MovimientoInventario)
            .GetMethod("VincularSalida", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(movimiento, new object?[]
            {
                null, // rqId — sin RQ todavía
                request.ValeBlobRef,
                request.PersonaDestinatariaId,
            });

        if (!string.IsNullOrWhiteSpace(request.Observaciones))
        {
            typeof(MovimientoInventario).GetProperty("ComentarioLibre")!
                .SetValue(movimiento, request.Observaciones);
        }

        // C7.2b / salida-por-línea (vale): costo snapshot por el bin ELEGIDO en
        // cada línea (ya obligatorio; sin fallback a la ÚNICA). Solo se llavea la
        // lectura; la fórmula del promedio NO cambia (candado de regresión de costeo).
        var posicion = 1;
        var payloadLineas = new List<LineaSalidaPayload>(request.Lineas.Count);
        foreach (var input in request.Lineas)
        {
            // Salida-por-línea (vale): el bin es obligatorio (validado arriba y en
            // el validator); ya no hay coalesce a la ÚNICA. Defensa por si el
            // handler se invoca fuera del pipeline.
            var ubicacionLinea = input.UbicacionId
                ?? throw new BusinessRuleException(
                    "SALIDA_UBICACION_REQUERIDA",
                    "Cada línea del vale debe indicar la ubicación (bin) de donde sale.");
            var saldo = await _db.SaldosInventario.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.UbicacionId == ubicacionLinea && s.ArticuloId == input.ArticuloId,
                    cancellationToken);
            var costoSnapshot = saldo?.CostoPromedioMxn ?? 0m;

            var lineaId = Guid.CreateVersion7();
            var linea = new LineaMovimiento(
                id: lineaId,
                movimientoId: movimientoId,
                posicion: posicion++,
                articuloId: input.ArticuloId,
                cantidad: input.Cantidad,
                unidadMedida: "PZA",
                costoUnitarioMxn: costoSnapshot,
                centroCostoId: input.CentroCostoId,
                proyectoId: input.ProyectoId,
                ubicacionReferencia: input.UbicacionReferencia,
                comentarioLinea: input.Comentario,
                ubicacionId: ubicacionLinea);
            movimiento.AgregarLinea(linea);

            payloadLineas.Add(new LineaSalidaPayload(
                LineaSalidaId: lineaId,
                ArticuloId: input.ArticuloId,
                UnidadMedida: "PZA",
                Cantidad: input.Cantidad,
                CostoUnitarioMxn: costoSnapshot,
                MontoTotalMxn: Math.Round(input.Cantidad * costoSnapshot, 2),
                CentroCostoId: input.CentroCostoId,
                ProyectoId: input.ProyectoId,
                // Salida-por-línea (vale): el bin elegido (obligatorio).
                UbicacionId: ubicacionLinea));
        }

        var anio = request.FechaMovimiento.Year;
        var prefijo = TipoMovimiento.SalidaPorVale.PrefijoFolio();
        var secuencia = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        var siguiente = secuencia.Incrementar();
        var folio = FolioMovimiento.Construir(TipoMovimiento.SalidaPorVale, anio, siguiente);

        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "VALE_SIN_USUARIO", "Se requiere usuario autenticado.");
        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);

        await _events.PublishAsync(new SalidaRequisicionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            SalidaId: movimientoId,
            FolioSalida: folio.Valor,
            // Salida-por-línea C3: el evento ya no lleva SubAlmacenId (campo
            // muerto — Compras nunca lo leyó). El vale conserva su propia lógica
            // de sub; solo deja de emitir el campo retirado del contrato.
            FechaMovimiento: request.FechaMovimiento,
            RqId: null,
            EsPorVale: true,
            PersonaDestinatariaId: request.PersonaDestinatariaId,
            ValeBlobRef: request.ValeBlobRef,
            Lineas: payloadLineas), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new RegistrarSalidaResponse(movimientoId, folio.Valor);
    }
}

// ─── Regularización ─────────────────────────────────────────────────────────

/// <summary>
/// Vincula una RQ posterior al vale, cumpliendo la regularización en
/// 48h (A14). El movimiento permanece registrado; solo cambia el flag
/// <c>pendiente_regularizacion → false</c> y se vincula
/// <c>rq_regularizadora_id</c>.
/// </summary>
public sealed record RegularizarSalidaPorValeCommand(
    Guid MovimientoValeId,
    Guid RqRegularizadoraId) : IRequest;

public sealed class RegularizarSalidaPorValeValidator
    : AbstractValidator<RegularizarSalidaPorValeCommand>
{
    public RegularizarSalidaPorValeValidator()
    {
        RuleFor(c => c.MovimientoValeId).NotEqual(Guid.Empty);
        RuleFor(c => c.RqRegularizadoraId).NotEqual(Guid.Empty);
    }
}

public sealed class RegularizarSalidaPorValeHandler
    : IRequestHandler<RegularizarSalidaPorValeCommand>
{
    private readonly AlmacenDbContext _db;

    public RegularizarSalidaPorValeHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(
        RegularizarSalidaPorValeCommand request, CancellationToken cancellationToken)
    {
        var mov = await _db.Movimientos
            .FirstOrDefaultAsync(m => m.Id == request.MovimientoValeId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "VALE_NO_ENCONTRADO",
                $"No existe movimiento con id '{request.MovimientoValeId}'.");

        mov.RegularizarVale(request.RqRegularizadoraId);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
