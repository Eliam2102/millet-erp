using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
using System.Text.Json;

namespace Millet.ComercioExterior.Domain;
public enum EstadoOtrSalida { Pendiente = 1, Entregada = 2, Acusada = 3, Fallida = 4, Reemplazada = 5 }
public sealed class OtrSalida : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid OtrEnvioId { get; private set; }
    public string Tipo { get; private set; } = "";
    public string ValorJson { get; private set; } = "{}";
    public int VersionErp { get; private set; }
    public EstadoOtrSalida Estado { get; private set; } = EstadoOtrSalida.Pendiente;
    public int Intento { get; private set; } = 1;
    public Guid? ReemplazadaPorId { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }
    public DateTimeOffset? UltimoIntentoAt { get; private set; }
    public DateTimeOffset? AcuseAt { get; private set; }
    public string? AcuseResultado { get; private set; }
    public string? AcuseMotivo { get; private set; }
    private OtrSalida() { }
    public OtrSalida(Guid empresa, Guid envio, string tipo, int version, string json) : base(Guid.CreateVersion7())
    {
        if (envio == Guid.Empty || empresa == Guid.Empty || version < 1 || string.IsNullOrWhiteSpace(tipo) || tipo.Length > 50)
            throw new BusinessRuleException("CE_OTR_SALIDA_INVALIDA", "Envío, tipo y versión ERP positiva son obligatorios.");
        using var _ = JsonDocument.Parse(json);
        EmpresaId = empresa; OtrEnvioId = envio; Tipo = tipo; VersionErp = version; ValorJson = json;
    }
    public void Intentar(int maxIntentos, DateTimeOffset fecha)
    {
        if (maxIntentos < 1) throw new BusinessRuleException("CE_OTR_INTENTOS_INVALIDOS", "MaxIntentos debe ser positivo.");
        if (Estado != EstadoOtrSalida.Pendiente || Intentos >= maxIntentos)
            throw new BusinessRuleException("CE_OTR_SALIDA_NO_REINTENTABLE", "La salida requiere reintento manual o ya fue acusada.");
        Intentos++; UltimoIntentoAt = fecha;
    }
    public void Entregada() { Estado = EstadoOtrSalida.Entregada; UltimoError = null; }
    public void ErrorEntrega(int maxIntentos, string error)
    {
        UltimoError = error;
        if (Intentos >= maxIntentos) Estado = EstadoOtrSalida.Fallida;
    }
    public void Acusar(DateTimeOffset fecha, string resultado, string? motivo)
    {
        if (Estado is EstadoOtrSalida.Acusada or EstadoOtrSalida.Reemplazada) return;
        AcuseAt = fecha; AcuseResultado = resultado; AcuseMotivo = motivo;
        Estado = resultado == "Aplicada" ? EstadoOtrSalida.Acusada : EstadoOtrSalida.Fallida;
        UltimoError = Estado == EstadoOtrSalida.Fallida ? "CE_OTR_ACUSE_RECHAZADO: " + motivo : null;
    }
    public OtrSalida Reintentar()
    {
        if (Estado != EstadoOtrSalida.Fallida) throw new BusinessRuleException("CE_OTR_SALIDA_NO_FALLIDA", "Sólo las salidas fallidas admiten reintento manual.");
        var nueva = new OtrSalida(EmpresaId, OtrEnvioId, Tipo, VersionErp, ValorJson) { Intento = Intento + 1 };
        Estado = EstadoOtrSalida.Reemplazada; ReemplazadaPorId = nueva.Id;
        return nueva;
    }
}
