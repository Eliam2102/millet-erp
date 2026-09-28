using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Referencia fiscal compartida, versionada por vigencia. No decide qué
/// impuesto aplica a una operación; esa regla pertenece al módulo fiscal.
/// No se siembran tasas reales hasta que Millet apruebe el catálogo.
/// </summary>
public sealed class ImpuestoReferencia : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string Tipo { get; private set; } = string.Empty;
    public string Factor { get; private set; } = string.Empty;
    public decimal Tasa { get; private set; }
    public DateOnly VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }
    public bool Activo { get; private set; }
    public string Fuente { get; private set; } = string.Empty;

    private ImpuestoReferencia() { }

    public ImpuestoReferencia(Guid id, string clave, string nombre, string tipo,
        string factor, decimal tasa, DateOnly vigenteDesde, DateOnly? vigenteHasta,
        bool activo, string fuente) : base(id)
    {
        Clave = NormalizarClave(clave);
        ValidarNombre(nombre);
        ValidarTipo(tipo);
        ValidarFactor(factor);
        ValidarTasa(factor, tasa);
        ValidarVigencia(vigenteDesde, vigenteHasta);
        ValidarFuente(fuente);
        Nombre = nombre.Trim();
        Tipo = tipo;
        Factor = factor;
        Tasa = tasa;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        Activo = activo;
        Fuente = fuente.Trim();
    }

    public void Actualizar(string nombre, DateOnly? vigenteHasta, bool activo, string fuente)
    {
        ValidarNombre(nombre);
        ValidarVigencia(VigenteDesde, vigenteHasta);
        ValidarFuente(fuente);
        Nombre = nombre.Trim();
        VigenteHasta = vigenteHasta;
        Activo = activo;
        Fuente = fuente.Trim();
    }

    public static string NormalizarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Trim().Length > 20 ||
            !clave.Trim().All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            throw new BusinessRuleException("IMPUESTO_CLAVE_INVALIDA",
                "La clave debe contener de 1 a 20 letras, números, guiones o guiones bajos.");
        return clave.Trim().ToUpperInvariant();
    }

    private static void ValidarNombre(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 120)
            throw new BusinessRuleException("IMPUESTO_NOMBRE_INVALIDO", "El nombre es obligatorio (máximo 120 caracteres).");
    }

    private static void ValidarTipo(string value)
    {
        if (value is not ("Traslado" or "Retencion"))
            throw new BusinessRuleException("IMPUESTO_TIPO_INVALIDO", "El tipo debe ser Traslado o Retencion.");
    }

    private static void ValidarFactor(string value)
    {
        if (value is not ("Tasa" or "Cuota" or "Exento"))
            throw new BusinessRuleException("IMPUESTO_FACTOR_INVALIDO", "El factor debe ser Tasa, Cuota o Exento.");
    }

    private static void ValidarTasa(string factor, decimal value)
    {
        if (value < 0 || value >= 1_000_000 || (factor == "Tasa" && value > 1) ||
            (factor == "Exento" && value != 0))
            throw new BusinessRuleException("IMPUESTO_TASA_INVALIDA",
                "La tasa debe estar entre 0 y 1; la cuota no puede ser negativa y Exento siempre usa 0.");
    }

    private static void ValidarVigencia(DateOnly desde, DateOnly? hasta)
    {
        if (desde == default || hasta < desde)
            throw new BusinessRuleException("IMPUESTO_VIGENCIA_INVALIDA",
                "La fecha final no puede ser anterior a la inicial.");
    }

    private static void ValidarFuente(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 120)
            throw new BusinessRuleException("IMPUESTO_FUENTE_INVALIDA", "La fuente es obligatoria (máximo 120 caracteres).");
    }
}
