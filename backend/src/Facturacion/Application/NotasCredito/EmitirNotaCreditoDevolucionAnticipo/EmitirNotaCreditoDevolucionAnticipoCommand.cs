using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Bancos;

namespace Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoDevolucionAnticipo;

/// <summary>
/// F6.3 · CA6.17: devuelve en dinero un anticipo no aplicado. Emite la NC (relación 01 al CFDI del anticipo), descuenta
/// el saldo del anticipo y, al quedar timbrada, pide el reembolso a Tesorería con la cuenta destino. Si el timbre falla,
/// el saldo queda comprometido y el reintento de timbrado completa la solicitud.
/// </summary>
public sealed record EmitirNotaCreditoDevolucionAnticipoCommand(
    Guid AnticipoId,
    decimal MontoTotal,
    decimal? TasaIva,
    ReembolsoClienteRequest Reembolso,
    string? Comentarios = null) : IRequest<EmitirNotaCreditoDevolucionAnticipoResponse>;

public sealed record EmitirNotaCreditoDevolucionAnticipoResponse(
    Guid Id, string Estado, string? Uuid, string Folio, decimal Total, decimal SaldoAnticipo);

public sealed class EmitirNotaCreditoDevolucionAnticipoValidator : AbstractValidator<EmitirNotaCreditoDevolucionAnticipoCommand>
{
    public EmitirNotaCreditoDevolucionAnticipoValidator()
    {
        RuleFor(c => c.AnticipoId).NotEmpty();
        RuleFor(c => c.MontoTotal).GreaterThan(0);
        RuleFor(c => c.TasaIva).GreaterThanOrEqualTo(0).When(c => c.TasaIva.HasValue);
        RuleFor(c => c.Reembolso).NotNull().SetValidator(new ReembolsoClienteRequestValidator());
        RuleFor(c => c.Comentarios).MaximumLength(NotaCredito.ComentariosMaxLength);
    }
}

public sealed class EmitirNotaCreditoDevolucionAnticipoHandler(
    FacturacionDbContext db,
    ISender sender,
    IPeriodoContablePort periodo,
    ICfdiTimbradoPort fiscal,
    ICfdiRepositorioPort cfdiRepo,
    IIntegrationEventPublisher eventos,
    ICurrentEmpresaContext empresa,
    ICurrentUserContext user,
    ICurrentUserPermissions permisos,
    IClock clock)
    : IRequestHandler<EmitirNotaCreditoDevolucionAnticipoCommand, EmitirNotaCreditoDevolucionAnticipoResponse>
{
    public async Task<EmitirNotaCreditoDevolucionAnticipoResponse> Handle(
        EmitirNotaCreditoDevolucionAnticipoCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        await ReembolsoNotaCredito.ExigirPermisoAsync(permisos, cancellationToken);

        var anticipo = await db.Anticipos.Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.Id == command.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", $"No existe el anticipo {command.AnticipoId}.");
        var factura = await db.FacturasAnticipo.FirstOrDefaultAsync(f => f.Id == anticipo.FacturaAnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", "No existe el CFDI del anticipo.");
        if (factura.Estado != EstadoTimbrado.Timbrado || string.IsNullOrWhiteSpace(factura.Uuid))
            throw new BusinessRuleException("ANTICIPO_CFDI_NO_TIMBRADO",
                $"El CFDI del anticipo ({factura.Folio}) no está timbrado vigente (estado: {factura.Estado}); no puede devolverse.");

        var ahora = clock.UtcNow;
        if (!await periodo.EstaAbiertoAsync(ahora.Year, ahora.Month, cancellationToken))
            throw new BusinessRuleException("PERIODO_CERRADO",
                $"El período contable {ahora.Year}-{ahora.Month:D2} está cerrado; no se puede emitir.");

        // Antes de reservar folio: la CLABE y el saldo se validan aquí para no gastar un folio en una NC que no procede.
        _ = Clabe.Crear(command.Reembolso.Clabe);
        if (anticipo.Estado != Domain.Anticipos.EstadoAnticipo.Abierto || command.MontoTotal > anticipo.SaldoDisponible)
            throw new BusinessRuleException("ANTICIPO_SALDO_INSUFICIENTE",
                $"El importe a devolver ({command.MontoTotal:N2}) excede el saldo disponible del anticipo ({anticipo.SaldoDisponible:N2}).");

        var reserva = await sender.Send(new ReservarFolioCommand(empresaId, factura.SucursalId, TipoDocumentoSerie.NotaCredito,
            DateOnly.FromDateTime(ahora.UtcDateTime)), cancellationToken);

        var receptor = new DatosFiscalesReceptor(factura.ReceptorRfc, factura.ReceptorNombre, factura.ReceptorRegimenFiscal,
            factura.ReceptorCodigoPostal, factura.ReceptorUsoCfdi, factura.ReceptorPais, factura.ReceptorEsGenerico);
        var nc = NotaCredito.CrearDevolucionAnticipo(empresaId, reserva.Folio, reserva.Numero, factura.SucursalId, user.UserId,
            receptor, factura.SnapshotEmisor(), factura.FormaPago, factura.Moneda, factura.TipoCambio, ahora.Year, ahora.Month,
            factura.CanalVentaId, anticipo.Id, command.MontoTotal, command.TasaIva,
            command.Reembolso.Banco, command.Reembolso.Clabe, command.Reembolso.Titular);
        nc.FijarComentarios(command.Comentarios);
        nc.AgregarRelacion(factura.Uuid!, MotivoNotaCredito.DevolucionAnticipo.ClaveTipoRelacion());

        // El saldo se compromete antes de timbrar (como la vinculación): no se devuelve dos veces el mismo anticipo.
        anticipo.RegistrarDevolucion(nc.Total);

        await TimbradoEjecutor.TimbrarYAplicarAsync(db, nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            fiscal, cfdiRepo, ahora, cancellationToken);

        if (nc.Estado == EstadoTimbrado.Timbrado)
            await eventos.PublishAsync(EventosContablesFacturacion.NotaCreditoTimbrada(nc, ahora, clienteId: anticipo.ClienteId),
                cancellationToken);
        db.NotasCredito.Add(nc);
        await ReembolsoNotaCredito.SolicitarProgramadoSiAplicaAsync(db, eventos, nc, user.UserId, ahora, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new EmitirNotaCreditoDevolucionAnticipoResponse(nc.Id, nc.Estado.ToString(), nc.Uuid, nc.Folio, nc.Total, anticipo.Saldo);
    }
}
