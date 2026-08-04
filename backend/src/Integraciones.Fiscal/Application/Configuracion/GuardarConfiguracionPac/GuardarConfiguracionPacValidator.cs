using FluentValidation;

namespace Millet.Integraciones.Fiscal.Application.Configuracion.GuardarConfiguracionPac;

public sealed class GuardarConfiguracionPacValidator
    : AbstractValidator<GuardarConfiguracionPacCommand>
{
    public GuardarConfiguracionPacValidator()
    {
        RuleFor(c => c.EmpresaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Proveedor).IsInEnum();
        RuleFor(c => c.BaseUrl).NotEmpty().MaximumLength(500);

        // ApiKey es opcional en el comando (null = "no rotar"). Cuando viene,
        // validamos que tenga forma sensata. La validación de fondo
        // (cifrado, hash) la hace el domain en RotarApiKey.
        When(c => c.ApiKey is not null, () =>
        {
            RuleFor(c => c.ApiKey!).NotEmpty().MinimumLength(8).MaximumLength(500);
        });

        // Identidades de prueba: forma de los campos aquí; la invariante
        // "solo con BaseUrl sandbox" la impone el domain
        // (CONFIG_PAC_IDENTIDAD_REQUIERE_SANDBOX).
        RuleFor(c => c.EmisorSandbox!).SetValidator(new IdentidadSandboxDtoValidator())
            .When(c => c.EmisorSandbox is not null);
        RuleFor(c => c.ReceptorSandbox!).SetValidator(new IdentidadSandboxDtoValidator())
            .When(c => c.ReceptorSandbox is not null);

        // CSD: opcional (null = no rotar). Cuando viene, las tres piezas
        // son obligatorias y los archivos deben ser base64 válido.
        RuleFor(c => c.Csd!).SetValidator(new CsdDtoValidator())
            .When(c => c.Csd is not null);

        // PR-13: timeouts/retry/CB y schedule eliminados del comando.
    }
}

public sealed class CsdDtoValidator : AbstractValidator<CsdDto>
{
    // Un .cer CSD ronda 2-3 KB (≈4 KB en base64) y un .key ~2 KB; 64 KB de
    // tope corta payloads absurdos sin estorbar certificados legítimos.
    private const int MaxBase64Length = 64 * 1024;

    public CsdDtoValidator()
    {
        RuleFor(c => c.CertificadoBase64).NotEmpty().MaximumLength(MaxBase64Length)
            .Must(EsBase64).WithMessage("El certificado (.cer) debe venir como base64 válido.");
        RuleFor(c => c.LlavePrivadaBase64).NotEmpty().MaximumLength(MaxBase64Length)
            .Must(EsBase64).WithMessage("La llave privada (.key) debe venir como base64 válido.");
        RuleFor(c => c.Password).NotEmpty().MaximumLength(200);
    }

    private static bool EsBase64(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return false;
        var buffer = new byte[valor.Length];
        return Convert.TryFromBase64String(valor, buffer, out var written) && written > 0;
    }
}

public sealed class IdentidadSandboxDtoValidator : AbstractValidator<IdentidadSandboxDto>
{
    public IdentidadSandboxDtoValidator()
    {
        RuleFor(i => i.Rfc).NotEmpty().Length(12, 13)
            .WithMessage("RFC de prueba inválido (12-13 caracteres).");
        RuleFor(i => i.RazonSocial).NotEmpty().MaximumLength(254);
        RuleFor(i => i.RegimenFiscal).NotEmpty().MaximumLength(10);
        RuleFor(i => i.CodigoPostal).Matches(@"^\d{5}$")
            .WithMessage("El código postal de la identidad de prueba debe ser de 5 dígitos.");
    }
}
