using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.Certificados;

/// <summary>
/// Revoca el CSD en uso (F6.9, R24): queda en el historial con su motivo y el
/// adaptador deja de timbrar con él de inmediato (se invalida la caché del
/// resolver), aunque no haya vencido.
/// </summary>
public sealed record RevocarCsdCommand(Guid EmpresaId, ProveedorPac Proveedor, string Motivo) : IRequest<CertificadoCsdItem>;

public sealed class RevocarCsdValidator : AbstractValidator<RevocarCsdCommand>
{
    public RevocarCsdValidator()
    {
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(500);
    }
}

public sealed class RevocarCsdHandler(
    IntegracionesFiscalDbContext db,
    IConfiguracionPacResolver resolver,
    FiscalSecretCipher cipher,
    ICurrentEmpresaContext empresa,
    ICurrentUserContext user,
    IClock clock)
    : IRequestHandler<RevocarCsdCommand, CertificadoCsdItem>
{
    public async Task<CertificadoCsdItem> Handle(RevocarCsdCommand command, CancellationToken cancellationToken)
    {
        var empresaActual = empresa.Current
            ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada en el JWT.");
        if (empresaActual != command.EmpresaId)
            throw new CrossTenantViolationException(nameof(ConfiguracionPac), empresaActual, command.EmpresaId);

        var config = await db.ConfiguracionesPac
            .FirstOrDefaultAsync(c => c.EmpresaId == command.EmpresaId && c.Proveedor == command.Proveedor, cancellationToken)
            ?? throw new EntityNotFoundException("CONFIG_PAC_NO_ENCONTRADA", "La empresa no tiene configuración del PAC.");
        var ahora = clock.UtcNow;
        var certificado = await HistorialCsd.AsegurarAsync(db, cipher, config, ahora, cancellationToken)
            ?? throw new BusinessRuleException("CSD_NO_VIGENTE", "No hay un CSD en uso que revocar.");

        certificado.Revocar(command.Motivo, ahora, user.UserName);
        config.RevocarCsd(ahora);
        await db.SaveChangesAsync(cancellationToken);
        resolver.Invalidar(config.EmpresaId, config.Proveedor);

        return new CertificadoCsdItem(
            certificado.Id, certificado.NumeroSerie, certificado.Rfc, certificado.VigenteDesde, certificado.VenceEn,
            certificado.Estado.ToString(), certificado.RegistradoEn, certificado.RegistradoPor,
            certificado.FinalizadoEn, certificado.FinalizadoPor, certificado.MotivoRevocacion, []);
    }
}
