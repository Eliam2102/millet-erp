using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas.Terminal;

/// <summary>
/// Catálogo terminal ↔ afiliación ↔ sucursal (M-111): varias terminales (código de terminal) cuelgan de un
/// número de afiliación y cada una pertenece a una sucursal. El statement del banco trae ambos datos por venta;
/// con este catálogo una venta solo se empareja con cobros de la sucursal de su terminal.
/// </summary>
public sealed class TerminalAfiliacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Afiliacion { get; private set; } = default!;
    public string CodTerminal { get; private set; } = default!;
    public Guid SucursalId { get; private set; }
    public string? Descripcion { get; private set; }
    public bool Activa { get; private set; } = true;

    private TerminalAfiliacion() { }

    public static TerminalAfiliacion Crear(Guid empresaId, string afiliacion, string codTerminal, Guid sucursalId, string? descripcion) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = empresaId,
        Afiliacion = Requerido(afiliacion, "la afiliación"),
        CodTerminal = Requerido(codTerminal, "el código de terminal"),
        SucursalId = sucursalId,
        Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
    };

    public void Actualizar(Guid sucursalId, string? descripcion)
    {
        SucursalId = sucursalId;
        Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        Activa = true;
    }

    public void Desactivar() => Activa = false;

    private static string Requerido(string valor, string nombre) =>
        string.IsNullOrWhiteSpace(valor)
            ? throw new BusinessRuleException("TERMINAL_CATALOGO_VACIO", $"Indica {nombre} de la terminal.")
            : valor.Trim();
}
