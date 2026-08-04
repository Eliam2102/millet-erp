using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Conteos;

// ============================================================================
// Comandos del flujo de conteo F7-PR1 (captura sin sesgo).
// Crear → Iniciar (toma snapshot) → CapturarLinea (sin teórico, A6).
// La aprobación/aplicación entra en F7-PR2.
// ============================================================================

public sealed record CrearConteoCommand(
    TipoConteo Tipo,
    DateOnly FechaPlanificada,
    Guid ResponsableId,
    Guid? SubAlmacenId,
    string? FiltroFamilia) : IRequest<CrearConteoResponse>;

public sealed record CrearConteoResponse(Guid ConteoId);

public sealed class CrearConteoValidator : AbstractValidator<CrearConteoCommand>
{
    public CrearConteoValidator()
    {
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.FechaPlanificada).Must(f => f.Year is >= 2020 and <= 2099);
        RuleFor(c => c.ResponsableId).NotEqual(Guid.Empty);
        RuleFor(c => c.FiltroFamilia!).MaximumLength(50).When(c => c.FiltroFamilia is not null);
    }
}

public sealed class CrearConteoHandler : IRequestHandler<CrearConteoCommand, CrearConteoResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;

    public CrearConteoHandler(AlmacenDbContext db, ICurrentEmpresaContext empresa)
    {
        _db = db; _empresa = empresa;
    }

    public async Task<CrearConteoResponse> Handle(CrearConteoCommand request, CancellationToken cancellationToken)
    {
        var empresaId = _empresa.Current ?? throw new BusinessRuleException(
            "CONTEO_SIN_EMPRESA", "Contexto de empresa requerido.");
        var conteo = new ConteoInventario(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            tipo: request.Tipo,
            fechaPlanificada: request.FechaPlanificada,
            responsableId: request.ResponsableId,
            subAlmacenId: request.SubAlmacenId,
            filtroFamilia: request.FiltroFamilia);
        _db.Set<ConteoInventario>().Add(conteo);
        await _db.SaveChangesAsync(cancellationToken);
        return new CrearConteoResponse(conteo.Id);
    }
}

// ─── Iniciar (toma snapshot) ─────────────────────────────────────────────────

public sealed record IniciarConteoCommand(Guid ConteoId) : IRequest;

public sealed class IniciarConteoHandler : IRequestHandler<IniciarConteoCommand>
{
    private readonly AlmacenDbContext _db;

    public IniciarConteoHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(IniciarConteoCommand request, CancellationToken cancellationToken)
    {
        var conteo = await _db.Set<ConteoInventario>()
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTEO_NO_ENCONTRADO",
                $"No existe conteo con id '{request.ConteoId}'.");

        if (conteo.Estado != EstadoConteo.Planificado)
        {
            throw new BusinessRuleException("CONTEO_NO_PLANIFICADO",
                $"Solo se puede iniciar desde Planificado (estado: {conteo.Estado}).");
        }

        // Snapshot: poblar líneas con cantidad teórica + costo promedio del saldo.
        // Filtro: sub_almacen específico si conteo.SubAlmacenId presente; cualquier
        // sub-almacén si null. F7-PR1 no filtra por familia (F7-PR2 lo agrega
        // cuando exista la columna `categoria` en saldo o vía IArticuloReadPort).
        var saldosQuery = _db.SaldosInventario.AsNoTracking().AsQueryable();
        if (conteo.SubAlmacenId is Guid sid)
            saldosQuery = saldosQuery.Where(s => s.SubAlmacenId == sid);

        // C7.2c: el conteo baja a NIVEL RACK. Cada fila de saldo (una por
        // (ubicacion, articulo)) genera UNA línea de conteo con su bin y su
        // costo propio — sin agregar. El índice único (conteo, articulo,
        // ubicacion) espeja la PK de saldos, así que no puede violarse. Las
        // filas-en-0 de las asignaciones también entran (teórica 0), que es
        // lo que el conteo inicial siembra por rack.
        var saldos = await saldosQuery.ToListAsync(cancellationToken);
        foreach (var saldo in saldos)
        {
            conteo.AgregarLinea(new LineaConteo(
                id: Guid.CreateVersion7(),
                conteoId: conteo.Id,
                articuloId: saldo.ArticuloId,
                subAlmacenId: saldo.SubAlmacenId,
                ubicacionId: saldo.UbicacionId,
                cantidadTeorica: saldo.Cantidad,
                costoPromedioSnapshot: saldo.CostoPromedioMxn));
        }

        if (conteo.Lineas.Count == 0)
        {
            throw new BusinessRuleException("CONTEO_SIN_SALDO",
                "No hay saldos para el sub-almacén; el conteo no se puede iniciar vacío.");
        }

        conteo.Iniciar();

        // F7-PR3 (A18): si es Anual, crea bloqueos para los sub-almacenes
        // afectados (salidas rechazadas durante el conteo).
        if (conteo.Tipo == Domain.Conteos.TipoConteo.Anual)
        {
            var subs = conteo.Lineas.Select(l => l.SubAlmacenId).Distinct().ToList();
            foreach (var subId in subs)
            {
                _db.Set<Domain.Conteos.BloqueoInventario>().Add(
                    new Domain.Conteos.BloqueoInventario(
                        id: Guid.CreateVersion7(),
                        conteoId: conteo.Id,
                        subAlmacenId: subId,
                        bloqueaSalidas: true,
                        bloqueaEntradas: false));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Capturar línea (SIN cantidad teórica — A6) ──────────────────────────────

public sealed record CapturarLineaConteoCommand(
    Guid ConteoId,
    Guid LineaConteoId,
    decimal CantidadReal) : IRequest;

public sealed class CapturarLineaConteoValidator : AbstractValidator<CapturarLineaConteoCommand>
{
    public CapturarLineaConteoValidator()
    {
        RuleFor(c => c.ConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.LineaConteoId).NotEqual(Guid.Empty);
        RuleFor(c => c.CantidadReal).GreaterThanOrEqualTo(0);
    }
}

public sealed class CapturarLineaConteoHandler : IRequestHandler<CapturarLineaConteoCommand>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public CapturarLineaConteoHandler(
        AlmacenDbContext db, ICurrentUserContext currentUser, IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db; _currentUser = currentUser; _decimalesGuard = decimalesGuard;
    }

    public async Task Handle(CapturarLineaConteoCommand request, CancellationToken cancellationToken)
    {
        var conteo = await _db.Set<ConteoInventario>()
            .Include(c => c.Lineas.Where(l => l.Id == request.LineaConteoId))
            .FirstOrDefaultAsync(c => c.Id == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTEO_NO_ENCONTRADO",
                $"No existe conteo con id '{request.ConteoId}'.");

        if (conteo.Estado != EstadoConteo.EnCurso)
        {
            throw new BusinessRuleException("CONTEO_NO_EN_CURSO",
                $"Solo se puede capturar en EnCurso (estado: {conteo.Estado}).");
        }

        var linea = conteo.Lineas.FirstOrDefault()
            ?? throw new EntityNotFoundException("LINEA_CONTEO_NO_ENCONTRADA",
                $"No existe línea '{request.LineaConteoId}' en el conteo.");

        var capturadoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "CAPTURA_SIN_USUARIO", "Se requiere usuario autenticado.");

        // ADR-0046 Etapa 2: valida los decimales de la cantidad real contra la
        // unidad del artículo de la línea (FK NULL → no valida).
        await _decimalesGuard.ValidarAsync(
            new[] { new CantidadAValidar(linea.ArticuloId, request.CantidadReal) },
            cancellationToken);

        linea.Capturar(request.CantidadReal, capturadoPor);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Enviar a conciliación ───────────────────────────────────────────────────

public sealed record EnviarConteoAConciliacionCommand(Guid ConteoId) : IRequest;

public sealed class EnviarConteoAConciliacionHandler
    : IRequestHandler<EnviarConteoAConciliacionCommand>
{
    private readonly AlmacenDbContext _db;
    public EnviarConteoAConciliacionHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(EnviarConteoAConciliacionCommand request, CancellationToken cancellationToken)
    {
        var conteo = await _db.Set<ConteoInventario>()
            .Include(c => c.Lineas)
            .FirstOrDefaultAsync(c => c.Id == request.ConteoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTEO_NO_ENCONTRADO",
                $"No existe conteo con id '{request.ConteoId}'.");

        conteo.EnviarAConciliacion();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
