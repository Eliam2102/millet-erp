using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Asignación artículo→ubicación (Nivel 4), estilo SAP OITW (ADR-0047, PR3).
/// Tabla <c>almacen.asignaciones_articulo_ubicacion</c>. Pura relación de
/// pertenencia: dónde vive un artículo (en qué <see cref="Ubicacion"/>).
///
/// <para>
/// <b>Enmienda ADR-0047 (PR C, 2026-07-07):</b> los niveles min/máx/punto-reorden
/// y la bandera/objetivo de reabasto ya NO viven en N4. El reorden que dispara
/// reabasto vive en <see cref="ConfiguracionReorden"/> (N1/N2), y el caso
/// informativo también (config con <c>AutoRequisicion=false</c> guarda min/máx sin
/// generar borradores). Por eso esta entidad quedó reducida a la relación
/// artículo↔ubicación + estatus.
/// </para>
/// <para>
/// Un artículo puede tener VARIAS asignaciones (varias ubicaciones). Clave única
/// <c>(ubicacion_id, articulo_id)</c>.
/// </para>
/// <para>
/// <c>ArticuloId</c> es FK <b>lógica</b> a <c>compartido.articulos</c> (otro
/// DbContext/esquema), como <c>saldos.articulo_id</c> — sin navegación EF.
/// </para>
/// <para>
/// Al asignar, el handler crea (idempotente) la fila de saldo en 0 para
/// <c>(ubicacion, articulo)</c>, de modo que el área "ve el 0" sin necesidad de
/// movimientos. Al desasignar (permitido solo con saldo 0) el handler borra esa
/// fila-en-0.
/// </para>
/// </summary>
public sealed class AsignacionArticuloUbicacion : BaseEntity, IAuditable
{
    public Guid UbicacionId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private AsignacionArticuloUbicacion() { }

    public AsignacionArticuloUbicacion(
        Guid id,
        Guid ubicacionId,
        Guid articuloId,
        EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        if (ubicacionId == Guid.Empty)
            throw new BusinessRuleException("ASIGNACION_SIN_UBICACION",
                "La asignación requiere ubicación.");
        if (articuloId == Guid.Empty)
            throw new BusinessRuleException("ASIGNACION_SIN_ARTICULO",
                "La asignación requiere artículo.");

        UbicacionId = ubicacionId;
        ArticuloId = articuloId;
        Estatus = estatus;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;
}
