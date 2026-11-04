using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Domain;
public enum EstadoOtrSalida { Pendiente = 1, Entregada = 2, Acusada = 3, Fallida = 4, Reemplazada = 5 }
/// <summary>Historial de salidas hacia A+W OTR generadas antes del 4-nov-2026. Ya no se crean:
/// A+W no lee lo que el ERP deja (Jorge, 1-nov) y el regreso se queda en el ERP. La tabla se conserva.</summary>
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
}
