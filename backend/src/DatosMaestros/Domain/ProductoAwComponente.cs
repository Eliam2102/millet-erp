using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Pieza del árbol de composición de un <see cref="ProductoAw"/> (A+W <c>BA_STUKL</c>, docs/integration/06 §3).
/// El árbol es posicional: <see cref="Orden"/> (1-based) identifica la fila y <see cref="PadreOrden"/> apunta a
/// la fila padre (null = raíz). Guarda COPIA de ref/descripción/tipo: procesos, rellenos y perfiles no están en
/// el catálogo sincronizado, por eso no hay FK a <see cref="ProductoAw"/>.
/// </summary>
public sealed class ProductoAwComponente : BaseEntity, INotAudited
{
    public Guid ProductoAwId { get; private set; }
    public int Orden { get; private set; }
    public int Nivel { get; private set; }
    public int? PadreOrden { get; private set; }
    public string ComponenteRef { get; private set; } = string.Empty;
    public string? Descripcion { get; private set; }
    public string? Tipo { get; private set; }
    public decimal? EspesorMm { get; private set; }

    private ProductoAwComponente() { }

    internal ProductoAwComponente(Guid id, Guid productoAwId, ProductoAwComponenteDato d) : base(id)
    {
        if (productoAwId == Guid.Empty)
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_PRODUCTO_INVALIDO", "El producto es requerido.");
        ProductoAwId = productoAwId;
        Orden = d.Orden;
        Asignar(d);
    }

    /// <summary>Reemplaza los datos de la fila (el <see cref="Orden"/> es inmutable).</summary>
    internal void Asignar(ProductoAwComponenteDato d)
    {
        Validar(d);
        Nivel = d.Nivel;
        PadreOrden = d.PadreOrden;
        ComponenteRef = d.ComponenteRef;
        Descripcion = d.Descripcion;
        Tipo = d.Tipo;
        EspesorMm = d.EspesorMm;
    }

    internal static void Validar(ProductoAwComponenteDato d)
    {
        if (d.Orden < 1 || d.Nivel < 1)
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_POSICION_INVALIDA",
                "El orden y el nivel del componente deben ser mayores o iguales a 1.");
        if (d.PadreOrden is { } p && (p < 1 || p >= d.Orden))
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_PADRE_INVALIDO",
                $"El padre del componente {d.Orden} debe ser una fila anterior (recibido {p}).");
        if (string.IsNullOrWhiteSpace(d.ComponenteRef) || d.ComponenteRef.Length > 50)
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_REF_INVALIDA",
                "La referencia del componente es requerida y no puede exceder 50 caracteres.");
        if (d.Descripcion is { Length: > 254 } || d.Tipo is { Length: > 50 })
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_TEXTO_INVALIDO",
                "La descripción del componente no puede exceder 254 caracteres ni el tipo 50.");
        if (d.EspesorMm is <= 0)
            throw new BusinessRuleException("PRODUCTO_AW_COMPONENTE_ESPESOR_INVALIDO",
                "El espesor debe ser mayor a 0 (o nulo si no se informa).");
    }
}

/// <summary>Fila del árbol recibida de A+W para <see cref="ProductoAw.ReemplazarComponentes"/>.</summary>
public sealed record ProductoAwComponenteDato(
    int Orden, int Nivel, int? PadreOrden, string ComponenteRef, string? Descripcion, string? Tipo, decimal? EspesorMm);
