using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.DatosMaestros.Domain;

public sealed class ClasificacionCliente : BaseEntity, IAuditable
{
    public string Nombre { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool Activa { get; private set; } = true;
    private ClasificacionCliente() { }
    public ClasificacionCliente(Guid id, string nombre, string descripcion) : base(id) => Editar(nombre, descripcion, true);
    public void Editar(string nombre, string descripcion, bool activa)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 100 || descripcion.Length > 500)
            throw new BusinessRuleException("CLASIFICACION_INVALIDA", "Indica un nombre de hasta 100 caracteres y una descripción de hasta 500.");
        Nombre = nombre.Trim(); Descripcion = descripcion; Activa = activa;
    }
}
