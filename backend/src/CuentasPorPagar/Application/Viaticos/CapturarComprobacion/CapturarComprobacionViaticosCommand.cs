using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Viaticos.CapturarComprobacion;

/// <summary>
/// El empleado captura su comprobación al regresar del viaje (§7.4.2
/// paso 5, F7-PR3). Cada llamada reemplaza las líneas previas
/// (idempotencia natural — el empleado puede recargar).
/// </summary>
public sealed record CapturarComprobacionViaticosCommand(
    Guid Id,
    int VersionEsperada,
    IReadOnlyList<CapturarLineaViaticosInput> Lineas) : IRequest<CapturarComprobacionViaticosResponse>;

public sealed record CapturarLineaViaticosInput(
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid? ProveedorId,
    string? FolioProveedor,
    DateTimeOffset FechaGasto,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    string Moneda,
    string Concepto,
    bool EsTicketNoFiscal);

public sealed record CapturarComprobacionViaticosResponse(
    Guid Id,
    EstadoSolicitudViaticos Estado,
    int NumeroLineas,
    decimal MontoComprobado,
    int Version);

public sealed class CapturarComprobacionViaticosValidator
    : AbstractValidator<CapturarComprobacionViaticosCommand>
{
    public CapturarComprobacionViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.Total).GreaterThan(0);
            l.RuleFor(x => x.Moneda).NotEmpty().Length(3);
            l.RuleFor(x => x.Concepto).NotEmpty().MaximumLength(400);
            l.RuleFor(x => x.CfdiRecibidoId)
                .Null()
                .When(x => x.EsTicketNoFiscal)
                .WithMessage("Una línea de ticket no fiscal no puede referenciar un CFDI recibido.");
            // P7-H4: sin esto, la línea se saltaba en silencio al liberar
            // y el gasto desaparecía de la liquidación contable.
            l.RuleFor(x => x.ProveedorId)
                .NotNull()
                .When(x => !x.EsTicketNoFiscal)
                .WithMessage("Una línea fiscal requiere proveedor para generar su factura.");
            l.RuleFor(x => x)
                .Must(x => x.CfdiRecibidoId is not null || !string.IsNullOrWhiteSpace(x.UuidCfdi))
                .When(x => !x.EsTicketNoFiscal)
                .WithMessage("Una línea fiscal requiere el CFDI vinculado o su UUID.");
        });
    }
}

public sealed class CapturarComprobacionViaticosHandler
    : IRequestHandler<CapturarComprobacionViaticosCommand, CapturarComprobacionViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;

    public CapturarComprobacionViaticosHandler(CuentasPorPagarDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<CapturarComprobacionViaticosResponse> Handle(
        CapturarComprobacionViaticosCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.SolicitudesViaticos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("VIA_NO_ENCONTRADA",
                $"No se encontró la solicitud '{command.Id}'.");
        if (s.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(SolicitudViaticos), s.Id);

        // Validación de CFDIs vinculados: deben existir en el repositorio y
        // seguir PorProcesar. Aquí NO se marcan como procesados — la captura
        // es reemplazable (idempotencia natural); el marcado ocurre en
        // LiberarComprobacionViaticosHandler al generar las facturas.
        var cfdiIds = command.Lineas
            .Where(l => l.CfdiRecibidoId is not null)
            .Select(l => l.CfdiRecibidoId!.Value)
            .ToList();
        if (cfdiIds.Distinct().Count() != cfdiIds.Count)
        {
            throw new BusinessRuleException(
                "VIA_LINEA_CFDI_DUPLICADO",
                "La comprobación tiene líneas que referencian el mismo CFDI recibido.");
        }

        var cfdisPorId = cfdiIds.Count == 0
            ? new Dictionary<Guid, CfdiRecibido>()
            : await _db.CfdisRecibidos
                .AsNoTracking()
                .Where(c => cfdiIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        foreach (var linea in command.Lineas)
        {
            if (linea.CfdiRecibidoId is not Guid cfdiId)
                continue;
            if (!cfdisPorId.TryGetValue(cfdiId, out var cfdi))
            {
                throw new EntityNotFoundException(
                    "VIA_CFDI_NO_ENCONTRADO",
                    $"No se encontró el CFDI recibido '{cfdiId}'.");
            }
            if (cfdi.Estado != EstadoCfdiRecibido.PorProcesar)
            {
                throw new BusinessRuleException(
                    "VIA_CFDI_YA_PROCESADO",
                    $"El CFDI {cfdi.UuidCfdi.Valor} ya no está PorProcesar (estado actual: {cfdi.Estado}).");
            }
            if (!string.IsNullOrWhiteSpace(linea.UuidCfdi) &&
                !string.Equals(linea.UuidCfdi.Trim(), cfdi.UuidCfdi.Valor, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException(
                    "VIA_CFDI_UUID_NO_COINCIDE",
                    $"El UUID de la línea ({linea.UuidCfdi}) no coincide con el del CFDI vinculado ({cfdi.UuidCfdi.Valor}).");
            }
        }

        var dominioLineas = command.Lineas.Select(l => new LineaComprobacionViaticosInput(
            CfdiRecibidoId: l.CfdiRecibidoId,
            UuidCfdi: l.UuidCfdi
                ?? (l.CfdiRecibidoId is Guid id ? cfdisPorId[id].UuidCfdi.Valor : null),
            ProveedorId: l.ProveedorId,
            FolioProveedor: l.FolioProveedor,
            FechaGasto: l.FechaGasto,
            Subtotal: l.Subtotal,
            ImpuestosTrasladados: l.ImpuestosTrasladados,
            Retenciones: l.Retenciones,
            Total: l.Total,
            Moneda: l.Moneda,
            Concepto: l.Concepto,
            EsTicketNoFiscal: l.EsTicketNoFiscal));

        s.CapturarComprobacion(dominioLineas, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return new CapturarComprobacionViaticosResponse(
            Id: s.Id,
            Estado: s.Estado,
            NumeroLineas: s.Lineas.Count,
            MontoComprobado: s.Lineas.Sum(l => l.Total),
            Version: s.Version);
    }
}
