using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// RFC receptor de una empresa que el worker de descarga consulta
/// periódicamente. Una empresa puede tener varios RFCs (razones sociales
/// distintas con SAT separados — caso típico de holdings).
///
/// <para>
/// El checkpoint de descarga (<see cref="CheckpointDescargaAt"/>) lo
/// avanza el worker tras procesar exitosamente un batch. Reinicios del
/// worker recuperan desde aquí sin reprocesar.
/// </para>
///
/// <para>
/// Flags <see cref="DescargaHabilitada"/> y <see cref="RefreshHabilitada"/>
/// permiten apagar selectivamente un RFC sin borrarlo (auditoría).
/// </para>
/// </summary>
public sealed class RfcReceptor : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Rfc { get; private set; } = string.Empty;
    public bool DescargaHabilitada { get; private set; }
    public bool RefreshHabilitada { get; private set; }
    public DateTimeOffset? CheckpointDescargaAt { get; private set; }

    // ─── Aprovisionamiento FIEL en FiscalAPI (PR-10) ─────────────────────
    // FiscalAPI maneja certs como tax-files asociados a un Person. Para que
    // la descarga masiva funcione en prod, el receptor debe tener FIEL
    // cargada. Estos campos cachean los IDs que FiscalAPI devuelve al
    // subir, para evitar relistar en cada uso. Idempotencia: si ya
    // están, el upload se rechaza con un error claro (vía AsignarFiel).

    /// <summary>UUID del Person en FiscalAPI (response de POST /api/v4/people).</summary>
    public string? PersonIdExterno { get; private set; }

    /// <summary>UUID del tax-file .cer (response de POST /api/v4/tax-files fileType=0).</summary>
    public string? CerFileIdExterno { get; private set; }

    /// <summary>UUID del tax-file .key (response de POST /api/v4/tax-files fileType=1).</summary>
    public string? KeyFileIdExterno { get; private set; }

    /// <summary>Validez del cert SAT (auto-calculada por FiscalAPI al subir).</summary>
    public DateTimeOffset? FielValidFrom { get; private set; }

    public DateTimeOffset? FielValidTo { get; private set; }

    /// <summary>Cuándo se subió la FIEL actual a FiscalAPI.</summary>
    public DateTimeOffset? FielSubidaAt { get; private set; }

    private RfcReceptor() { } // EF Core

    public RfcReceptor(Guid id, Guid empresaId, string rfc) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("RFC_RECEPTOR_EMPRESA_INVALIDA",
                "EmpresaId es requerida.");
        ValidarRfc(rfc);

        EmpresaId = empresaId;
        Rfc = rfc.Trim().ToUpperInvariant();
        DescargaHabilitada = true;
        RefreshHabilitada = true;
    }

    public void HabilitarDescarga() => DescargaHabilitada = true;
    public void DeshabilitarDescarga() => DescargaHabilitada = false;
    public void HabilitarRefresh() => RefreshHabilitada = true;
    public void DeshabilitarRefresh() => RefreshHabilitada = false;

    /// <summary>
    /// Asocia el Person de FiscalAPI al RFC (idempotente). Lo invoca el
    /// handler de subir FIEL antes del upload de tax-files.
    /// </summary>
    public void AsignarPersonExterno(string personIdExterno)
    {
        if (string.IsNullOrWhiteSpace(personIdExterno))
            throw new BusinessRuleException("RFC_RECEPTOR_PERSON_EXTERNO_INVALIDO",
                "PersonIdExterno es requerido.");
        PersonIdExterno = personIdExterno;
    }

    /// <summary>
    /// Marca la FIEL como cargada con los IDs externos que FiscalAPI
    /// devolvió + validez. Si ya había FIEL previa, la sobreescribe (caso
    /// de renovación cuando la anterior está por vencer).
    /// </summary>
    public void AsignarFiel(
        string cerFileIdExterno,
        string keyFileIdExterno,
        DateTimeOffset validFrom,
        DateTimeOffset validTo,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(PersonIdExterno))
            throw new BusinessRuleException("RFC_RECEPTOR_FIEL_SIN_PERSON",
                "El Person en FiscalAPI debe estar asignado antes de la FIEL.");
        if (string.IsNullOrWhiteSpace(cerFileIdExterno))
            throw new BusinessRuleException("RFC_RECEPTOR_FIEL_CER_INVALIDO",
                "CerFileIdExterno es requerido.");
        if (string.IsNullOrWhiteSpace(keyFileIdExterno))
            throw new BusinessRuleException("RFC_RECEPTOR_FIEL_KEY_INVALIDO",
                "KeyFileIdExterno es requerido.");
        if (validTo <= validFrom)
            throw new BusinessRuleException("RFC_RECEPTOR_FIEL_VIGENCIA_INVALIDA",
                "FielValidTo debe ser mayor a FielValidFrom.");
        if (validTo <= ahora)
            throw new BusinessRuleException("RFC_RECEPTOR_FIEL_EXPIRADA",
                "La FIEL ya está vencida — no se debe cargar una expirada.");

        CerFileIdExterno = cerFileIdExterno;
        KeyFileIdExterno = keyFileIdExterno;
        FielValidFrom    = validFrom;
        FielValidTo      = validTo;
        FielSubidaAt     = ahora;
    }

    /// <summary>
    /// <c>true</c> si tiene FIEL cargada Y vigente para <paramref name="ahora"/>.
    /// El admin UI usa esto para mostrar el badge "FIEL OK" o "FIEL vencida".
    /// </summary>
    public bool TieneFielVigente(DateTimeOffset ahora)
        => FielValidFrom is { } from && FielValidTo is { } to
           && from <= ahora && ahora < to;

    /// <summary>
    /// Avanza el checkpoint tras un batch exitoso del worker de descarga.
    /// Solo se mueve hacia adelante — no se permite retroceder
    /// (idempotencia frente a reintentos).
    /// </summary>
    public void AvanzarCheckpoint(DateTimeOffset nuevoCheckpoint)
    {
        if (CheckpointDescargaAt is { } actual && nuevoCheckpoint <= actual)
            return; // idempotente / no retrocede
        CheckpointDescargaAt = nuevoCheckpoint;
    }

    private static void ValidarRfc(string rfc)
    {
        if (string.IsNullOrWhiteSpace(rfc))
            throw new BusinessRuleException("RFC_RECEPTOR_RFC_INVALIDO",
                "RFC es requerido.");
        var trimmed = rfc.Trim();
        if (trimmed.Length is < 12 or > 13)
            throw new BusinessRuleException("RFC_RECEPTOR_RFC_INVALIDO",
                "RFC debe tener 12 (moral) o 13 (física) caracteres.");
    }
}
