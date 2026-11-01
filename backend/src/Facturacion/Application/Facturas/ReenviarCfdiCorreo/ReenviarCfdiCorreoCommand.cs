using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Facturas.ReenviarCfdiCorreo;

/// <summary>
/// Encola el reenvío del CFDI de una factura al correo indicado (§7.1, §12.5).
/// Crea una <c>BitacoraEnvioCorreo</c> en <c>Pendiente</c> (o reencola la que
/// no ha salido para ese destinatario); el
/// <c>EnvioCfdiCorreoWorker</c> la procesa (genera PDF + adjunta XML + entrega).
/// </summary>
public sealed record ReenviarCfdiCorreoCommand(Guid ComprobanteId, string Destinatario)
    : IRequest<ReenviarCfdiCorreoResponse>;

public sealed record ReenviarCfdiCorreoResponse(Guid BitacoraId, string Estado);

public sealed class ReenviarCfdiCorreoValidator : AbstractValidator<ReenviarCfdiCorreoCommand>
{
    public ReenviarCfdiCorreoValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEmpty();
        RuleFor(c => c.Destinatario).NotEmpty().EmailAddress().MaximumLength(254);
    }
}

public sealed class ReenviarCfdiCorreoHandler
    : IRequestHandler<ReenviarCfdiCorreoCommand, ReenviarCfdiCorreoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;

    public ReenviarCfdiCorreoHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa)
    {
        _db = db;
        _empresa = empresa;
    }

    public async Task<ReenviarCfdiCorreoResponse> Handle(
        ReenviarCfdiCorreoCommand command,
        CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var factura = await _db.FacturasVenta
            .FirstOrDefaultAsync(f => f.Id == command.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No existe la factura '{command.ComprobanteId}'.");

        if (factura.Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException(
                "FACTURA_NO_TIMBRADA",
                $"Sólo se puede enviar por correo una factura timbrada (estado actual: {factura.Estado}).");

        // 86e3txv0n: si al mismo destinatario ya hay un envío que no ha salido
        // (pendiente o fallido), se reencola esa fila en lugar de crear otra: así
        // el reintento del worker y el reenvío no mandan dos correos.
        var destinatario = command.Destinatario.Trim();
        var abiertos = await _db.BitacorasEnvioCorreo
            .Where(b => b.ComprobanteId == factura.Id && b.Estado != EstadoEnvioCorreo.Enviado)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        var bitacora = abiertos.FirstOrDefault(b => string.Equals(b.Destinatario, destinatario, StringComparison.OrdinalIgnoreCase));
        if (bitacora is not null)
            bitacora.Reencolar();
        else
        {
            bitacora = BitacoraEnvioCorreo.Encolar(empresaId, factura.Id, destinatario);
            _db.BitacorasEnvioCorreo.Add(bitacora);
        }
        await _db.SaveChangesAsync(cancellationToken);

        return new ReenviarCfdiCorreoResponse(bitacora.Id, bitacora.Estado.ToString());
    }
}
