using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores.SubirFielReceptor;

/// <summary>
/// Sube la FIEL (e.firma) del RFC receptor a FiscalAPI y persiste los
/// IDs externos. Es el primer paso para que la descarga masiva funcione
/// contra el SAT real en producción.
///
/// <para>
/// Flujo end-to-end:
/// </para>
/// <list type="number">
///   <item>Resolver el RFC + Empresa (cross-tenant guard).</item>
///   <item>Asegurar el Person en FiscalAPI (idempotente — crea si no existe).</item>
///   <item>Subir cer + key como tax-files (POST /api/v4/tax-files).</item>
///   <item>Persistir IDs externos + vigencia en <see cref="RfcReceptor"/>.</item>
/// </list>
///
/// <para>
/// <b>Datos SAT del receptor</b> (razón social, CP, régimen, email) los
/// proporciona el admin en el form porque NO viven en la entidad
/// <c>Empresa</c> de Administración. El receptor del CFDI puede ser una
/// razón social distinta a la del tenant Millet.
/// </para>
///
/// <para>
/// La password de la FIEL se reenvía a FiscalAPI pero NO se persiste
/// localmente. El admin la captura cada vez que rota / renueva.
/// </para>
/// </summary>
public sealed record SubirFielReceptorCommand(
    Guid RfcReceptorId,
    string LegalName,
    string ZipCode,
    string SatTaxRegimeCode,
    string Email,
    byte[] CerBytes,
    byte[] KeyBytes,
    string Password) : IRequest<RfcReceptorResponse>;

public sealed class SubirFielReceptorValidator : AbstractValidator<SubirFielReceptorCommand>
{
    public SubirFielReceptorValidator()
    {
        RuleFor(c => c.RfcReceptorId).NotEqual(Guid.Empty);

        RuleFor(c => c.LegalName)
            .NotEmpty()
            .MaximumLength(254)
            .WithMessage("Razón social del receptor es requerida (debe coincidir EXACTO con SAT, sin régimen societario).");

        RuleFor(c => c.ZipCode)
            .NotEmpty()
            .Length(5)
            .Matches(@"^\d{5}$")
            .WithMessage("Código postal del receptor debe ser 5 dígitos (registrado en SAT).");

        RuleFor(c => c.SatTaxRegimeCode)
            .NotEmpty()
            .MaximumLength(10)
            .WithMessage("Régimen fiscal SAT es requerido (ej. 601, 612, 626).");

        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(c => c.CerBytes)
            .NotNull()
            .Must(b => b is { Length: > 0 and < 50_000 })
            .WithMessage("El archivo .cer debe estar presente y ser menor a 50KB.");

        RuleFor(c => c.KeyBytes)
            .NotNull()
            .Must(b => b is { Length: > 0 and < 50_000 })
            .WithMessage("El archivo .key debe estar presente y ser menor a 50KB.");

        RuleFor(c => c.Password)
            .NotEmpty()
            .MinimumLength(1)
            .MaximumLength(500)
            .WithMessage("Password de la FIEL es requerida.");
    }
}

public sealed class SubirFielReceptorHandler
    : IRequestHandler<SubirFielReceptorCommand, RfcReceptorResponse>
{
    private readonly IntegracionesFiscalDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IFiscalApiSdkClient _sdk;
    private readonly IClock _clock;
    private readonly ILogger<SubirFielReceptorHandler> _logger;

    public SubirFielReceptorHandler(
        IntegracionesFiscalDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IFiscalApiSdkClient sdk,
        IClock clock,
        ILogger<SubirFielReceptorHandler> logger)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _sdk = sdk;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RfcReceptorResponse> Handle(
        SubirFielReceptorCommand command, CancellationToken cancellationToken)
    {
        var empresaActual = _currentEmpresa.Current
            ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT.");

        var rfc = await _db.RfcsReceptores
            .FirstOrDefaultAsync(r => r.Id == command.RfcReceptorId, cancellationToken)
            ?? throw new EntityNotFoundException("RFC_RECEPTOR_NO_ENCONTRADO",
                $"No existe el RFC receptor {command.RfcReceptorId}.");

        if (rfc.EmpresaId != empresaActual)
            throw new CrossTenantViolationException(
                nameof(RfcReceptor), empresaActual, rfc.EmpresaId);

        // 1) Asegurar Person en FiscalAPI (idempotente).
        _logger.LogInformation("[SubirFielReceptor] Asegurando Person en FiscalAPI para RFC {Rfc}.", rfc.Rfc);
        var person = await _sdk.AsegurarPersonAsync(
            empresaId:         empresaActual,
            rfc:               rfc.Rfc,
            legalName:         command.LegalName,
            zipCode:           command.ZipCode,
            satTaxRegimeCode:  command.SatTaxRegimeCode,
            satCfdiUseCode:    "G03", // Gastos en general — default razonable para receptor
            email:             command.Email,
            cancellationToken: cancellationToken);

        rfc.AsignarPersonExterno(person.IdExterno);

        // Sincronizar datos del Person en caso de que ya existiera con datos
        // distintos (FiscalAPI valida match exacto SAT al timbrar/descargar).
        await _sdk.SincronizarPersonAsync(
            empresaId:         empresaActual,
            personIdExterno:   person.IdExterno,
            legalName:         command.LegalName,
            zipCode:           command.ZipCode,
            satCfdiUseCode:    "G03",
            cancellationToken: cancellationToken);

        // 2) Subir cer + key.
        _logger.LogInformation("[SubirFielReceptor] Subiendo tax-files al Person {PersonId}.", person.IdExterno);
        var taxFiles = await _sdk.SubirTaxFilesAsync(
            empresaId:         empresaActual,
            personIdExterno:   person.IdExterno,
            rfc:               rfc.Rfc,
            cerBytes:          command.CerBytes,
            keyBytes:          command.KeyBytes,
            password:          command.Password,
            cancellationToken: cancellationToken);

        // 3) Persistir IDs + vigencia.
        rfc.AsignarFiel(
            cerFileIdExterno: taxFiles.CerIdExterno,
            keyFileIdExterno: taxFiles.KeyIdExterno,
            validFrom:        new DateTimeOffset(taxFiles.ValidFrom, TimeSpan.Zero),
            validTo:          new DateTimeOffset(taxFiles.ValidTo, TimeSpan.Zero),
            ahora:            _clock.UtcNow);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[SubirFielReceptor] FIEL cargada para RFC {Rfc}: cer={CerId}, key={KeyId}, vigencia {From} → {To}.",
            rfc.Rfc, taxFiles.CerIdExterno, taxFiles.KeyIdExterno, taxFiles.ValidFrom, taxFiles.ValidTo);

        return rfc.ToResponse(_clock.UtcNow);
    }
}
