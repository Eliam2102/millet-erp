namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Identidad (ADR-0042): resuelve, en batch,
/// <c>usuarioId → nombre</c> para el corte de caja (FIX-12: «corte por
/// cajero», Manual de Sucursales §2.4) y la liquidación de caja. Facturación
/// solo guarda el id del cajero (<c>CajaSesion.ResponsableUsuarioId</c>, sin
/// FK cross-schema). Los ids no encontrados no aparecen en el diccionario
/// (el handler hace fallback). Adapter real hospedado en <c>Millet.Api</c>
/// porque Facturación no referencia Identidad.
/// </summary>
public interface IUsuariosReadPort
{
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken);
}
