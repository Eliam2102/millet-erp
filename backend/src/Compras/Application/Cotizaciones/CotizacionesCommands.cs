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

        // Proyección (no se materializa el VO Money de la línea).
        var lineasRq = await db.LineaRequisiciones.AsNoTracking().Where(l => l.RequisicionId == rq.Id)
            .Select(l => new { l.Id, l.UnidadMedida }).ToListAsync(cancellationToken);
        var saldos = await SaldoCompraRq.ObtenerAsync(db, lineasRq.Select(l => l.Id), cancellationToken);
        var abiertas = lineasRq.Where(l => saldos.GetValueOrDefault(l.Id) > 0).ToDictionary(l => l.Id);

        if (request.Lineas.Any(l => !abiertas.ContainsKey(l.LineaRequisicionId)))
        {
            throw new BusinessRuleException("COT_PARTIDA_AJENA",
                "La partida no pertenece a la requisición o ya no tiene cantidad por comprar.");
        }
        if (abiertas.Keys.Any(id => request.Lineas.All(l => l.LineaRequisicionId != id)))
        {
            throw new BusinessRuleException("COT_PARTIDAS_INCOMPLETAS",
                "La cotización debe incluir todas las partidas por comprar de la requisición.");
        }

        if (!await compartido.Proveedores.AsNoTracking().AnyAsync(p => p.Id == request.ProveedorId, cancellationToken))
        {
            throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");
        }

        var moneda = request.Moneda.Trim().ToUpperInvariant();
        if (!await compartido.Monedas.AsNoTracking().AnyAsync(m => m.Codigo == moneda && m.Activa, cancellationToken))
        {
            throw new BusinessRuleException("COT_MONEDA_INVALIDA", "La moneda no existe o está inactiva en el catálogo.");
        }

        var cot = Cotizacion.Registrar(Guid.CreateVersion7(), empresaId, rq.Id, rq.SucursalId, request.ProveedorId,
            request.FolioProveedor, moneda, request.FechaCotizacion, request.VigenteHasta, request.CondicionesCredito,
            request.Lineas.Select(l => new LineaCotizacionDatos(l.LineaRequisicionId,
                abiertas[l.LineaRequisicionId].UnidadMedida, l.UnidadCotizada, l.FactorAUnidadRq, l.PrecioUnitario, l.DiasEntrega)).ToList(),
            userId, clock.UtcNow, FechasInicio.Hoy(clock));

        db.Cotizaciones.Add(cot);
        await db.SaveChangesAsync(cancellationToken);
        return new CotizacionGuardadaResponse(cot.Id, cot.Version);
    }
}
