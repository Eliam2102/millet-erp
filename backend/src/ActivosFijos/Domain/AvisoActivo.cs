using Millet.SharedKernel.Domain;
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
    private AvisoActivo() { }
    public AvisoActivo(Activo a, Guid fuente, bool mantenimiento, TipoAvisoActivo tipo, DateOnly vence, DateOnly hoy) : base(Guid.CreateVersion7())
    { EmpresaId = a.EmpresaId; ActivoId = a.Id; ResponsableId = a.ResponsableId; FuenteId = fuente; EsMantenimiento = mantenimiento; Tipo = tipo; Vencimiento = vence; FechaAviso = hoy; }
    public static TipoAvisoActivo? TipoPendiente(DateOnly vence, DateOnly hoy, int dias) => vence <= hoy ? TipoAvisoActivo.Vencido : vence <= hoy.AddDays(dias) ? TipoAvisoActivo.PorVencer : null;
}
