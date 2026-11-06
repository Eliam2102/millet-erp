using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>Causa externa que impidió timbrar (F6.8).</summary>
public enum TipoContingenciaTimbrado : short
{
    Pac = 1,
    Sat = 2,
    Internet = 3,
    Certificado = 4,
    Otra = 5,
}

public enum OrigenContingenciaTimbrado : short
{
    /// <summary>La registró el ERP al fallar el timbrado por causa externa.</summary>
    Automatica = 1,

    /// <summary>La capturó una persona (SAT, internet u otra causa sin intento fallido).</summary>
    Manual = 2,
}

public enum EstadoContingenciaTimbrado : short
{
    Abierta = 1,

    /// <summary>El documento afectado quedó timbrado (CFDI final) o se cerró a mano.</summary>
    Regularizada = 2,

    /// <summary>El documento afectado se descartó: el folio queda quemado.</summary>
    Descartada = 3,
}

/// <summary>
/// Bitácora de contingencias de timbrado (F6.8, regla R5 del cuestionario 04):
/// evento, usuario, fecha y hora, motivo y documento afectado. Se regulariza
/// dentro de los 3 días posteriores al cierre del mes en que ocurrió.
/// </summary>
public sealed class ContingenciaTimbrado : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    /// <summary>Días naturales del mes siguiente para regularizar (R5).</summary>
    public const int DiasParaRegularizar = 3;

    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public TipoContingenciaTimbrado Tipo { get; private set; }
    public OrigenContingenciaTimbrado Origen { get; private set; }
    public EstadoContingenciaTimbrado Estado { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public string? ErrorCodigo { get; private set; }

    /// <summary>Momento del evento (primer intento fallido o lo que capture la persona).</summary>
    public DateTimeOffset OcurridaAt { get; private set; }

    /// <summary>Último día para regularizar, inclusive (día 3 del mes siguiente, hora de Mérida).</summary>
    public DateOnly FechaLimite { get; private set; }

    /// <summary>Documento afectado (supuesto a validar: uno por contingencia).</summary>
    public Guid? ComprobanteId { get; private set; }
    public string? ComprobanteFolio { get; private set; }

    public Guid? RegistradaPorId { get; private set; }
    public string? RegistradaPorNombre { get; private set; }

    public DateTimeOffset? CerradaAt { get; private set; }
    public string? UuidFinal { get; private set; }
    public string? NotaCierre { get; private set; }
    public string? CerradaPorNombre { get; private set; }

    /// <summary>Cuándo salió el aviso a Fiscal; evita repetirlo cada barrido.</summary>
    public DateTimeOffset? AlertaEnviadaAt { get; private set; }

    private ContingenciaTimbrado() { }

    public static ContingenciaTimbrado Registrar(
        Guid empresaId,
        Guid sucursalId,
        TipoContingenciaTimbrado tipo,
        OrigenContingenciaTimbrado origen,
        string motivo,
        string? errorCodigo,
        DateTimeOffset ocurridaAt,
        Guid? comprobanteId,
        string? comprobanteFolio,
        Guid? registradaPorId,
        string? registradaPorNombre)
    {
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("CONTINGENCIA_SUCURSAL_REQUERIDA", "Indica la sucursal de la contingencia.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CONTINGENCIA_MOTIVO_REQUERIDO", "Indica el motivo de la contingencia.");
        if (!Enum.IsDefined(tipo))
            throw new BusinessRuleException("CONTINGENCIA_TIPO_INVALIDO", "El tipo de contingencia no es válido.");

        return new ContingenciaTimbrado
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            SucursalId = sucursalId,
            Tipo = tipo,
            Origen = origen,
            Estado = EstadoContingenciaTimbrado.Abierta,
            Motivo = Recortar(motivo.Trim(), 2000)!,
            ErrorCodigo = Recortar(errorCodigo, 50),
            // Postgres (timestamptz) solo acepta UTC: la captura manual llega con la hora local de la sucursal.
            OcurridaAt = ocurridaAt.ToUniversalTime(),
            FechaLimite = CalcularFechaLimite(ocurridaAt),
            ComprobanteId = comprobanteId,
            ComprobanteFolio = comprobanteFolio,
            RegistradaPorId = registradaPorId,
            RegistradaPorNombre = registradaPorNombre,
        };
    }

    /// <summary>Día 3 del mes siguiente al del evento, en hora de Mérida.</summary>
    public static DateOnly CalcularFechaLimite(DateTimeOffset ocurridaAt)
    {
        var local = DateOnly.FromDateTime(ZonaMillet.ALocal(ocurridaAt).DateTime);
        return new DateOnly(local.Year, local.Month, 1).AddMonths(1).AddDays(DiasParaRegularizar - 1);
    }

    /// <summary>El mes del evento ya cerró y la contingencia sigue abierta.</summary>
    public bool RequiereAlerta(DateOnly hoy) =>
        Estado == EstadoContingenciaTimbrado.Abierta && hoy >= new DateOnly(FechaLimite.Year, FechaLimite.Month, 1);

    public bool EstaVencida(DateOnly hoy) => Estado == EstadoContingenciaTimbrado.Abierta && hoy > FechaLimite;

    /// <summary>El documento afectado quedó timbrado: la contingencia queda regularizada con su CFDI.</summary>
    public void RegularizarConCfdi(string uuid, DateTimeOffset ahora)
    {
        ExigirAbierta();
        Estado = EstadoContingenciaTimbrado.Regularizada;
        UuidFinal = uuid;
        CerradaAt = ahora;
    }

    /// <summary>El documento afectado se descartó (folio quemado).</summary>
    public void MarcarDescartada(DateTimeOffset ahora, string? usuarioNombre)
    {
        ExigirAbierta();
        Estado = EstadoContingenciaTimbrado.Descartada;
        CerradaAt = ahora;
        CerradaPorNombre = usuarioNombre;
        NotaCierre = "Documento descartado: el folio queda quemado.";
    }

    /// <summary>Cierre manual de una contingencia sin documento afectado.</summary>
    public void RegularizarManual(string nota, DateTimeOffset ahora, string? usuarioNombre)
    {
        ExigirAbierta();
        if (ComprobanteId is not null)
            throw new BusinessRuleException(
                "CONTINGENCIA_CON_DOCUMENTO",
                "Esta contingencia se regulariza sola al timbrar o descartar su documento.");
        if (string.IsNullOrWhiteSpace(nota))
            throw new BusinessRuleException("CONTINGENCIA_NOTA_REQUERIDA", "Indica cómo se regularizó.");
        Estado = EstadoContingenciaTimbrado.Regularizada;
        CerradaAt = ahora;
        NotaCierre = Recortar(nota.Trim(), 2000);
        CerradaPorNombre = usuarioNombre;
    }

    /// <summary>Un intento posterior volvió a fallar: conserva el último código.</summary>
    public void ActualizarError(string? errorCodigo, string? motivo)
    {
        if (Estado != EstadoContingenciaTimbrado.Abierta) return;
        ErrorCodigo = Recortar(errorCodigo, 50) ?? ErrorCodigo;
        if (!string.IsNullOrWhiteSpace(motivo)) Motivo = Recortar(motivo.Trim(), 2000)!;
    }

    public void MarcarAlertaEnviada(DateTimeOffset ahora) => AlertaEnviadaAt = ahora;

    private void ExigirAbierta()
    {
        if (Estado != EstadoContingenciaTimbrado.Abierta)
            throw new BusinessRuleException(
                "CONTINGENCIA_NO_ABIERTA", $"La contingencia ya está {Estado.ToString().ToLowerInvariant()}.");
    }

    private static string? Recortar(string? valor, int max) =>
        valor is null ? null : valor.Length <= max ? valor : valor[..max];
}

/// <summary>Hora local de Millet (las fechas fiscales se cuentan en Mérida).</summary>
public static class ZonaMillet
{
    public const string Id = "America/Merida";

    public static DateTimeOffset ALocal(DateTimeOffset instante) =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(instante, Id);

    public static DateOnly Hoy(DateTimeOffset utcAhora) => DateOnly.FromDateTime(ALocal(utcAhora).DateTime);
}
