using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.VincularAnticipo;

/// <summary>
/// Registra la vinculación anticipo ↔ factura final (M2). Valida que el anticipo
/// esté Abierto, que la factura esté timbrada y sea del mismo cliente (mismo RFC
/// receptor), y que el importe no exceda el saldo disponible. La reducción del
/// saldo amortizado es la NC de amortización (M3, F4-PR2).
/// </summary>
public sealed class VincularAnticipoHandler
    : IRequestHandler<VincularAnticipoCommand, VincularAnticipoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;

    public VincularAnticipoHandler(FacturacionDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<VincularAnticipoResponse> Handle(
        VincularAnticipoCommand command,
        CancellationToken cancellationToken)
    {
        var anticipo = await _db.Anticipos
            .Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.Id == command.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", $"No existe el anticipo {command.AnticipoId}.");

        // 13-J: no comprometer un anticipo cuyo CFDI no está timbrado — la
        // amortización (M3) exige la relación 07 al UUID del anticipo.
        var cfdiAnticipo = await _db.FacturasAnticipo.AsNoTracking()
            .Where(f => f.Id == anticipo.FacturaAnticipoId)
            .Select(f => new { f.Folio, f.Estado, f.Uuid })
            .FirstOrDefaultAsync(cancellationToken);
        if (cfdiAnticipo is null
            || cfdiAnticipo.Estado != EstadoTimbrado.Timbrado
            || string.IsNullOrWhiteSpace(cfdiAnticipo.Uuid))
            throw new BusinessRuleException(
                "ANTICIPO_CFDI_NO_TIMBRADO",
                $"El CFDI del anticipo ({cfdiAnticipo?.Folio ?? "?"}) no está timbrado " +
                $"(estado: {cfdiAnticipo?.Estado.ToString() ?? "inexistente"}); no puede vincularse.");

        var factura = await _db.FacturasVenta
            .FirstOrDefaultAsync(f => f.Id == command.FacturaVentaId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura {command.FacturaVentaId}.");

        if (factura.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "FACTURA_NO_TIMBRADA",
                $"Solo se puede vincular un anticipo a una factura timbrada (estado actual: {factura.Estado}).");

        if (!string.Equals(factura.ReceptorRfc, anticipo.ReceptorRfc, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "ANTICIPO_CLIENTE_DISTINTO",
                "El anticipo y la factura final deben ser del mismo cliente (RFC receptor).");

        // El dominio valida estado Abierto + importe ≤ saldo disponible + no duplicado.
        anticipo.Vincular(command.FacturaVentaId, command.Importe, _clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);

        return new VincularAnticipoResponse(
            AnticipoId: anticipo.Id,
            FacturaVentaId: command.FacturaVentaId,
            Importe: command.Importe,
            Saldo: anticipo.Saldo,
            SaldoDisponible: anticipo.SaldoDisponible,
            Estado: anticipo.Estado.ToString());
    }
}
