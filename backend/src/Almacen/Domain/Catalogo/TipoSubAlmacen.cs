namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Tipo del sub-almacén según el negocio (01-diseno §5.1). Determina
/// reglas operativas: <see cref="MaterialEnRevision"/> es el destino del
/// material dañado del sub-flujo 8.A (A15), <see cref="MaterialesDirectos"/>
/// recibe variante B (packing list), <see cref="Insumos"/> recibe
/// variante A (factura).
/// </summary>
public enum TipoSubAlmacen : short
{
    /// <summary>Insumos y refacciones — variante A (con factura).</summary>
    Insumos = 0,

    /// <summary>Materiales directos no-vidrio (interlayer, silicones, pinturas) — variante B (packing list).</summary>
    MaterialesDirectos = 1,

    /// <summary>Material en revisión por Calidad — destino del sub-flujo 8.A (A15).</summary>
    MaterialEnRevision = 2,

    /// <summary>Sub-almacén transitorio (uso ad-hoc).</summary>
    Transitorio = 3,
}
