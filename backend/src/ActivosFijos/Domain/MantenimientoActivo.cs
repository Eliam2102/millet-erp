using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;
public enum TipoMantenimiento { Preventivo, Correctivo }
public sealed record MantenimientoDatos(DateOnly Fecha, TipoMantenimiento Tipo, Guid ProveedorId, string Descripcion, decimal Costo, Guid? FacturaProveedorId, DateOnly? Proximo);
public sealed class MantenimientoActivo : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ActivoId { get; private set; }
    public Guid AggregateRootId => ActivoId;
    public DateOnly Fecha { get; private set; }
    public TipoMantenimiento Tipo { get; private set; }
    public Guid ProveedorId { get; private set; }
    public string Descripcion { get; private set; } = "";
    public decimal Costo { get; private set; }
    public Guid? FacturaProveedorId { get; private set; }
    public DateOnly? Proximo { get; private set; }
    private MantenimientoActivo() { }
    public MantenimientoActivo(Activo activo, MantenimientoDatos d) : base(Guid.CreateVersion7())
    {
        if (d.Fecha == default || !Enum.IsDefined(d.Tipo) || d.ProveedorId == Guid.Empty || string.IsNullOrWhiteSpace(d.Descripcion) || d.Descripcion.Length > 2000 || d.Costo < 0 || d.Costo > 999999999999.99m || decimal.Round(d.Costo, 2) != d.Costo || d.FacturaProveedorId == Guid.Empty || d.Proximo <= d.Fecha)
            throw new BusinessRuleException("AF_MANTENIMIENTO_INVALIDO", "Revise fecha, proveedor, descripción, costo y próxima fecha posterior al servicio.");
        EmpresaId = activo.EmpresaId; ActivoId = activo.Id; Fecha = d.Fecha; Tipo = d.Tipo; ProveedorId = d.ProveedorId; Descripcion = d.Descripcion.Trim(); Costo = d.Costo; FacturaProveedorId = d.FacturaProveedorId; Proximo = d.Proximo;
    }
}
