using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;
public enum TipoAvisoActivo { PorVencer, Vencido }
public sealed class AvisoActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; private set; }
    public Guid FuenteId { get; private set; }
    public bool EsMantenimiento { get; private set; }
    public Guid ResponsableId { get; private set; }
    public TipoAvisoActivo Tipo { get; private set; }
    public DateOnly Vencimiento { get; private set; }
    public DateOnly FechaAviso { get; private set; }
    public string? Resolucion { get; private set; }
    public string? MotivoResolucion { get; private set; }
    public string? SoporteResolucion { get; private set; }
    public Guid? DocumentoNuevoId { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public void Resolver(string resolucion, string? motivo, DocumentoActivo? nuevo, Guid actor, DateTimeOffset ahora, string? soporte = null)
    {
        if (ResueltoEn != null || actor == Guid.Empty || resolucion is not ("Renovado" or "Cerrado") || motivo?.Length > 2000 || soporte?.Length > 2000)
            throw new BusinessRuleException("AF_RESOLUCION_INVALIDA", "Seleccione un aviso pendiente y una resolución válida.");
        if (resolucion == "Cerrado" && string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("AF_MOTIVO_REQUERIDO", "Explique el motivo del cierre.");
        if (resolucion == "Renovado" && (EsMantenimiento || nuevo == null || nuevo.ActivoId != ActivoId || nuevo.ReemplazaA != FuenteId || nuevo.VigenteHasta == null || nuevo.VigenteHasta <= Vencimiento || nuevo.Reemplazado))
            throw new BusinessRuleException("AF_RENOVACION_INVALIDA", "Suba una nueva versión del documento con vigencia posterior al vencimiento del aviso.");
        Resolucion = resolucion; MotivoResolucion = motivo?.Trim(); SoporteResolucion = soporte; DocumentoNuevoId = nuevo?.Id; ResueltoPor = actor; ResueltoEn = ahora;
    }
    private AvisoActivo() { }
    public AvisoActivo(Activo a, Guid fuente, bool mantenimiento, TipoAvisoActivo tipo, DateOnly vence, DateOnly hoy) : base(Guid.CreateVersion7())
    { EmpresaId = a.EmpresaId; ActivoId = a.Id; ResponsableId = a.ResponsableId; FuenteId = fuente; EsMantenimiento = mantenimiento; Tipo = tipo; Vencimiento = vence; FechaAviso = hoy; }
    public static TipoAvisoActivo? TipoPendiente(DateOnly vence, DateOnly hoy, int dias) => vence <= hoy ? TipoAvisoActivo.Vencido : vence <= hoy.AddDays(dias) ? TipoAvisoActivo.PorVencer : null;
}
