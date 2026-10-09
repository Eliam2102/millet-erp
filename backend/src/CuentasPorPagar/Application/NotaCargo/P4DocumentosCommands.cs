using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CuentasPorPagar.Application.NotaCargo;
public enum TipoDocumentoP4 { Anticipo = 1, NotaCredito = 2, NotaCargo = 3 }
public sealed record CancelarDocumentoP4Command(TipoDocumentoP4 Tipo, Guid Id, int VersionEsperada, string Motivo) : IRequest;
public sealed class CancelarDocumentoP4Validator : AbstractValidator<CancelarDocumentoP4Command>
{
    public CancelarDocumentoP4Validator()
    { RuleFor(c => c.Tipo).IsInEnum(); RuleFor(c => c.Id).NotEmpty(); RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0); RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400); }
}
public sealed class CancelarDocumentoP4Handler(CuentasPorPagarDbContext db, IClock clock) : IRequestHandler<CancelarDocumentoP4Command>
{
    public async Task Handle(CancelarDocumentoP4Command c, CancellationToken cancellationToken)
    {
        Millet.SharedKernel.Domain.BaseEntity documento = c.Tipo switch
        {
            TipoDocumentoP4.Anticipo => await db.AnticiposProveedor.FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken) ?? throw NoExiste(),
            TipoDocumentoP4.NotaCredito => await db.NotasCreditoProveedor.FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken) ?? throw NoExiste(),
            TipoDocumentoP4.NotaCargo => await db.NotasCargo.FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken) ?? throw NoExiste(),
            _ => throw new BusinessRuleException("DOCUMENTO_TIPO_INVALIDO", "Selecciona un tipo de documento válido.")
        };
        if (documento.Version != c.VersionEsperada) throw new ConcurrencyException("Documento", c.Id);
        switch (documento)
        {
            case Domain.AnticipoProveedor.AnticipoProveedor a: a.Cancelar(c.Motivo, clock.UtcNow); break;
            case Domain.NotaCreditoProveedor.NotaCreditoProveedor n:
                if (await db.NotasCargo.AnyAsync(x => x.NotaCreditoProveedorId == n.Id, cancellationToken))
                    throw new BusinessRuleException("NC_FORMALIZA_CARGO", "La NC formaliza una nota de cargo aplicada y requiere un reverso antes de cancelar.");
                n.Cancelar(c.Motivo, clock.UtcNow); break;
            case Domain.NotaCargo.NotaCargo n: n.Cancelar(c.Motivo, clock.UtcNow); break;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
    private static EntityNotFoundException NoExiste() => new("DOCUMENTO_NO_ENCONTRADO", "No se encontró el documento.");
}
public sealed record FormalizarNotaCargoCommand(Guid Id, int VersionEsperada, Guid NotaCreditoId) : IRequest;
public sealed class FormalizarNotaCargoValidator : AbstractValidator<FormalizarNotaCargoCommand>
{
    public FormalizarNotaCargoValidator() { RuleFor(c => c.Id).NotEmpty(); RuleFor(c => c.NotaCreditoId).NotEmpty(); RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0); }
}
public sealed class FormalizarNotaCargoHandler(CuentasPorPagarDbContext db, IPublisher publisher, IClock clock) : IRequestHandler<FormalizarNotaCargoCommand>
{
    public async Task Handle(FormalizarNotaCargoCommand c, CancellationToken cancellationToken)
    {
        var cargo = await db.NotasCargo.FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken) ?? throw new EntityNotFoundException("NCG_NO_ENCONTRADA", "No se encontró la nota de cargo.");
        if (cargo.Version != c.VersionEsperada) throw new ConcurrencyException("NotaCargo", c.Id);
        var nc = await db.NotasCreditoProveedor.FirstOrDefaultAsync(x => x.Id == c.NotaCreditoId, cancellationToken) ?? throw new EntityNotFoundException("NC_NO_ENCONTRADA", "No se encontró la NC.");
        await new FormalizacionNotaCargoService(db, publisher).FormalizarAsync(cargo, nc, clock.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
public sealed record AmortizarAnticipoConNcCommand(Guid AnticipoId, int VersionEsperada, Guid NotaCreditoId, int NcVersionEsperada, decimal Monto) : IRequest;
public sealed class AmortizarAnticipoConNcValidator : AbstractValidator<AmortizarAnticipoConNcCommand>
{
    public AmortizarAnticipoConNcValidator() { RuleFor(c => c.AnticipoId).NotEmpty(); RuleFor(c => c.NotaCreditoId).NotEmpty(); RuleFor(c => c.Monto).GreaterThan(0); RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0); RuleFor(c => c.NcVersionEsperada).GreaterThanOrEqualTo(0); }
}
public sealed class AmortizarAnticipoConNcHandler(CuentasPorPagarDbContext db, IClock clock) : IRequestHandler<AmortizarAnticipoConNcCommand>
{
    public async Task Handle(AmortizarAnticipoConNcCommand c, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) { await AmortizarAsync(c, cancellationToken); return; }
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(db, 0x50345F4E433037,
            ct => AmortizarAsync(c, ct), cancellationToken);
    }
    private async Task AmortizarAsync(AmortizarAnticipoConNcCommand c, CancellationToken cancellationToken)
    {
        var anticipo = await db.AnticiposProveedor.FirstOrDefaultAsync(a => a.Id == c.AnticipoId, cancellationToken) ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", "No se encontró el anticipo.");
        var nc = await db.NotasCreditoProveedor.FirstOrDefaultAsync(n => n.Id == c.NotaCreditoId, cancellationToken) ?? throw new EntityNotFoundException("NC_NO_ENCONTRADA", "No se encontró la NC.");
        if (anticipo.Version != c.VersionEsperada || nc.Version != c.NcVersionEsperada) throw new ConcurrencyException("Anticipo/NC", c.AnticipoId);
        if (nc.TipoRelacionCfdi != Domain.NotaCreditoProveedor.TipoRelacionCfdi.AmortizacionAnticipo || nc.UuidRelacionCfdi != anticipo.UuidCfdi || nc.ProveedorId != anticipo.ProveedorId || nc.Moneda != anticipo.Moneda)
            throw new BusinessRuleException("NC_ANTICIPO_NO_COINCIDE", "La NC tipo 07 debe relacionar el UUID del anticipo y coincidir en proveedor y moneda.");
        if (nc.AnticipoOrigenId is null) nc.VincularAnticipoOrigen(anticipo.Id, clock.UtcNow);
        // Si ya se aplicó internamente a una factura, la NC 07 documenta esa misma amortización.
        // Solo consume el remanente que aún no se haya reconocido mediante otras NC 07.
        var fiscalReconocido = await db.NotasCreditoProveedor.Where(n => n.AnticipoOrigenId == anticipo.Id && n.Estado != Domain.NotaCreditoProveedor.EstadoNotaCredito.Cancelada)
            .SumAsync(n => n.MontoAplicado, cancellationToken);
        var reconocido = Math.Min(c.Monto, Math.Max(0, anticipo.MontoAmortizado - fiscalReconocido));
        nc.AplicarMonto(c.Monto);
        if (c.Monto > reconocido) anticipo.Amortizar(c.Monto - reconocido, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}
