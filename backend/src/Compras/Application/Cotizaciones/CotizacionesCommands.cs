using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Cotizaciones;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Compras.Application.Cotizaciones;

public sealed record LineaCotizacionRequest(
    Guid LineaRequisicionId, string UnidadCotizada, decimal FactorAUnidadRq, decimal PrecioUnitario, int? DiasEntrega);

public sealed record CotizacionGuardadaResponse(Guid Id, int Version);

/// <summary>Registra la cotización de un proveedor para todas las partidas con saldo por comprar de una RQ.</summary>
public sealed record RegistrarCotizacionCommand(
    Guid RequisicionId, Guid ProveedorId, string? FolioProveedor, string Moneda,
    DateOnly FechaCotizacion, DateOnly VigenteHasta, string? CondicionesCredito,
    IReadOnlyList<LineaCotizacionRequest> Lineas) : IRequest<CotizacionGuardadaResponse>;

public sealed class RegistrarCotizacionValidator : AbstractValidator<RegistrarCotizacionCommand>
{
    public RegistrarCotizacionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty();
        RuleFor(c => c.ProveedorId).NotEmpty().WithErrorCode("COT_PROVEEDOR_REQUERIDO");
        RuleFor(c => c.Moneda).NotEmpty().Length(3).WithErrorCode("COT_MONEDA_INVALIDA");
        RuleFor(c => c.FolioProveedor).MaximumLength(40).WithErrorCode("COT_FOLIO_INVALIDO");
        RuleFor(c => c.CondicionesCredito).MaximumLength(100).WithErrorCode("COT_CREDITO_INVALIDO");
        RuleFor(c => c.Lineas).NotEmpty().WithErrorCode("COT_SIN_PARTIDAS");
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.LineaRequisicionId).NotEmpty().WithErrorCode("COT_PARTIDA_AJENA");
            l.RuleFor(x => x.UnidadCotizada).NotEmpty().MaximumLength(10).WithErrorCode("COT_UNIDAD_REQUERIDA");
            l.RuleFor(x => x.FactorAUnidadRq).GreaterThan(0).LessThanOrEqualTo(1_000_000m).WithErrorCode("COT_FACTOR_INVALIDO");
            l.RuleFor(x => x.PrecioUnitario).GreaterThan(0).LessThan(100_000_000_000m).WithErrorCode("COT_PRECIO_INVALIDO");
            l.RuleFor(x => x.DiasEntrega!.Value).InclusiveBetween(0, 365).When(x => x.DiasEntrega.HasValue)
                .WithErrorCode("COT_DIAS_ENTREGA_INVALIDOS");
        });
    }
}

public sealed class RegistrarCotizacionHandler(
    ComprasDbContext db, CompartidoDbContext compartido, ICurrentUserContext currentUser,
    ICurrentEmpresaContext currentEmpresa, IClock clock) : IRequestHandler<RegistrarCotizacionCommand, CotizacionGuardadaResponse>
{
    public async Task<CotizacionGuardadaResponse> Handle(RegistrarCotizacionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }
        if (currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var rq = await db.Requisiciones.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{request.RequisicionId}'.");

        if (rq.Estado != EstadoRequisicion.EnSurtido || rq.ComprometidaEnOcId is not null)
        {
            throw new BusinessRuleException("COT_RQ_NO_COTIZABLE",
                "Solo se cotiza una requisición autorizada con saldo por comprar y sin orden de compra.");
        }

        var abiertas = await PartidasCotizacion.ValidarAsync(db, rq.Id, request.Lineas, cancellationToken);

        if (!await compartido.Proveedores.AsNoTracking().AnyAsync(p => p.Id == request.ProveedorId, cancellationToken))
        {
            throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");
        }

        var moneda = await PartidasCotizacion.ValidarMonedaAsync(compartido, request.Moneda, cancellationToken);

        var cot = Cotizacion.Registrar(Guid.CreateVersion7(), empresaId, rq.Id, rq.SucursalId, request.ProveedorId,
            request.FolioProveedor, moneda, request.FechaCotizacion, request.VigenteHasta, request.CondicionesCredito,
            PartidasCotizacion.Datos(request.Lineas, abiertas),
            userId, clock.UtcNow, FechasInicio.Hoy(clock));

        db.Cotizaciones.Add(cot);
        await db.SaveChangesAsync(cancellationToken);
        return new CotizacionGuardadaResponse(cot.Id, cot.Version);
    }
}

/// <summary>Validaciones de partidas y moneda compartidas por registrar y editar cotización.</summary>
internal static class PartidasCotizacion
{
    /// <summary>Devuelve las partidas de la RQ con saldo por comprar (id, unidad) tras validar que la cotización las cubre todas.</summary>
    public static async Task<Dictionary<Guid, (Guid Id, string UnidadMedida)>> ValidarAsync(
        ComprasDbContext db, Guid requisicionId, IReadOnlyList<LineaCotizacionRequest> lineas, CancellationToken ct)
    {
        // Proyección (no se materializa el VO Money de la línea).
        var lineasRq = await db.LineaRequisiciones.AsNoTracking().Where(l => l.RequisicionId == requisicionId)
            .Select(l => new { l.Id, l.UnidadMedida }).ToListAsync(ct);
        var saldos = await SaldoCompraRq.ObtenerAsync(db, lineasRq.Select(l => l.Id), ct);
        var abiertas = lineasRq.Where(l => saldos.GetValueOrDefault(l.Id) > 0)
            .ToDictionary(l => l.Id, l => (l.Id, l.UnidadMedida));

        if (lineas.Any(l => !abiertas.ContainsKey(l.LineaRequisicionId)))
        {
            throw new BusinessRuleException("COT_PARTIDA_AJENA",
                "La partida no pertenece a la requisición o ya no tiene cantidad por comprar.");
        }
        if (abiertas.Keys.Any(id => lineas.All(l => l.LineaRequisicionId != id)))
        {
            throw new BusinessRuleException("COT_PARTIDAS_INCOMPLETAS",
                "La cotización debe incluir todas las partidas por comprar de la requisición.");
        }

        return abiertas;
    }

    public static async Task<string> ValidarMonedaAsync(CompartidoDbContext compartido, string moneda, CancellationToken ct)
    {
        var norm = moneda.Trim().ToUpperInvariant();
        if (!await compartido.Monedas.AsNoTracking().AnyAsync(m => m.Codigo == norm && m.Activa, ct))
        {
            throw new BusinessRuleException("COT_MONEDA_INVALIDA", "La moneda no existe o está inactiva en el catálogo.");
        }
        return norm;
    }

    public static List<LineaCotizacionDatos> Datos(
        IReadOnlyList<LineaCotizacionRequest> lineas, Dictionary<Guid, (Guid Id, string UnidadMedida)> abiertas) =>
        lineas.Select(l => new LineaCotizacionDatos(l.LineaRequisicionId,
            abiertas[l.LineaRequisicionId].UnidadMedida, l.UnidadCotizada, l.FactorAUnidadRq, l.PrecioUnitario, l.DiasEntrega)).ToList();
}

/// <summary>Edita una cotización mientras ninguna de la requisición se haya elegido.</summary>
public sealed record EditarCotizacionCommand(
    Guid CotizacionId, Guid ProveedorId, string? FolioProveedor, string Moneda,
    DateOnly FechaCotizacion, DateOnly VigenteHasta, string? CondicionesCredito,
    IReadOnlyList<LineaCotizacionRequest> Lineas) : IRequest<CotizacionGuardadaResponse>;

public sealed class EditarCotizacionValidator : AbstractValidator<EditarCotizacionCommand>
{
    public EditarCotizacionValidator()
    {
        RuleFor(c => c.CotizacionId).NotEmpty();
        RuleFor(c => c.ProveedorId).NotEmpty().WithErrorCode("COT_PROVEEDOR_REQUERIDO");
        RuleFor(c => c.Moneda).NotEmpty().Length(3).WithErrorCode("COT_MONEDA_INVALIDA");
        RuleFor(c => c.FolioProveedor).MaximumLength(40).WithErrorCode("COT_FOLIO_INVALIDO");
        RuleFor(c => c.CondicionesCredito).MaximumLength(100).WithErrorCode("COT_CREDITO_INVALIDO");
        RuleFor(c => c.Lineas).NotEmpty().WithErrorCode("COT_SIN_PARTIDAS");
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.LineaRequisicionId).NotEmpty().WithErrorCode("COT_PARTIDA_AJENA");
            l.RuleFor(x => x.UnidadCotizada).NotEmpty().MaximumLength(10).WithErrorCode("COT_UNIDAD_REQUERIDA");
            l.RuleFor(x => x.FactorAUnidadRq).GreaterThan(0).LessThanOrEqualTo(1_000_000m).WithErrorCode("COT_FACTOR_INVALIDO");
            l.RuleFor(x => x.PrecioUnitario).GreaterThan(0).LessThan(100_000_000_000m).WithErrorCode("COT_PRECIO_INVALIDO");
            l.RuleFor(x => x.DiasEntrega!.Value).InclusiveBetween(0, 365).When(x => x.DiasEntrega.HasValue)
                .WithErrorCode("COT_DIAS_ENTREGA_INVALIDOS");
        });
    }
}

public sealed class EditarCotizacionHandler(
    ComprasDbContext db, CompartidoDbContext compartido, IClock clock) : IRequestHandler<EditarCotizacionCommand, CotizacionGuardadaResponse>
{
    public async Task<CotizacionGuardadaResponse> Handle(EditarCotizacionCommand request, CancellationToken cancellationToken)
    {
        var cot = await CotizacionesComparativo.CargarAbiertaAsync(db, request.CotizacionId, cancellationToken);

        var rq = await db.Requisiciones.AsNoTracking().FirstOrDefaultAsync(r => r.Id == cot.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{cot.RequisicionId}'.");
        if (rq.Estado != EstadoRequisicion.EnSurtido || rq.ComprometidaEnOcId is not null)
        {
            throw new BusinessRuleException("COT_RQ_NO_COTIZABLE",
                "Solo se cotiza una requisición autorizada con saldo por comprar y sin orden de compra.");
        }

        var abiertas = await PartidasCotizacion.ValidarAsync(db, rq.Id, request.Lineas, cancellationToken);

        if (!await compartido.Proveedores.AsNoTracking().AnyAsync(p => p.Id == request.ProveedorId, cancellationToken))
        {
            throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");
        }
        var moneda = await PartidasCotizacion.ValidarMonedaAsync(compartido, request.Moneda, cancellationToken);

        cot.Editar(request.ProveedorId, request.FolioProveedor, moneda, request.FechaCotizacion, request.VigenteHasta,
            request.CondicionesCredito, PartidasCotizacion.Datos(request.Lineas, abiertas), FechasInicio.Hoy(clock));
        await db.SaveChangesAsync(cancellationToken);
        return new CotizacionGuardadaResponse(cot.Id, cot.Version);
    }
}

/// <summary>Elimina una cotización (y sus líneas) mientras ninguna de la requisición se haya elegido. El PDF se conserva (ADR-0058).</summary>
public sealed record EliminarCotizacionCommand(Guid CotizacionId) : IRequest;

public sealed class EliminarCotizacionHandler(ComprasDbContext db) : IRequestHandler<EliminarCotizacionCommand>
{
    public async Task Handle(EliminarCotizacionCommand request, CancellationToken cancellationToken)
    {
        var cot = await CotizacionesComparativo.CargarAbiertaAsync(db, request.CotizacionId, cancellationToken);
        db.Cotizaciones.Remove(cot);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class CotizacionesComparativo
{
    /// <summary>Carga la cotización con líneas y exige que ninguna cotización de su RQ se haya elegido.</summary>
    public static async Task<Cotizacion> CargarAbiertaAsync(ComprasDbContext db, Guid id, CancellationToken ct)
    {
        var cot = await db.Cotizaciones.Include(c => c.Lineas).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("COTIZACION_NO_ENCONTRADA", $"No se encontró la cotización '{id}'.");
        if (await db.Cotizaciones.AnyAsync(c => c.RequisicionId == cot.RequisicionId && c.OrdenCompraId != null, ct))
        {
            throw new BusinessRuleException("COT_COMPARATIVO_CERRADO",
                "Ya se eligió una cotización de esta requisición; las cotizaciones se conservan sin cambios.");
        }
        return cot;
    }

    /// <summary>
    /// Libera la elección de las cotizaciones ligadas a la OC (cancelada o sin líneas), en la misma
    /// transacción del llamador (no hace SaveChanges). La cotización vuelve a ser elegible y conserva su historial.
    /// </summary>
    public static async Task LiberarPorOcAsync(
        ComprasDbContext db, Guid ordenCompraId, Guid usuarioId, DateTimeOffset ahora, string motivo, CancellationToken ct)
    {
        var cots = await db.Cotizaciones.Where(c => c.OrdenCompraId == ordenCompraId).ToListAsync(ct);
        foreach (var c in cots)
        {
            c.LiberarEleccion(usuarioId, ahora, motivo);
        }
    }

    /// <summary>D1: una RQ con cotizaciones registradas solo se convierte en OC eligiendo desde el comparativo.</summary>
    public static async Task ExigirSinCotizacionesAsync(ComprasDbContext db, Guid requisicionId, CancellationToken ct)
    {
        if (await db.Cotizaciones.AnyAsync(c => c.RequisicionId == requisicionId, ct))
        {
            throw new BusinessRuleException("COT_ELEGIR_DESDE_COMPARATIVO",
                "La requisición tiene cotizaciones registradas; elige la cotización desde el comparativo para generar la orden de compra.");
        }
    }
}
