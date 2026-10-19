namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia el servicio genérico de adjuntos (G1.2, Compartido).
/// Solo expone id y nombre: el <c>BlobRef</c> del servicio genérico nunca sale (ADR-0058).
/// Lo usa el dictamen de material en revisión (ALM-04) para copiar la evidencia a la devolución a proveedor.
/// </summary>
public interface IAdjuntosEntidadReadPort
{
    Task<IReadOnlyList<AdjuntoEvidenciaLectura>> ListarVigentesAsync(
        string tipoEntidad, Guid entidadId, CancellationToken ct);
}

public sealed record AdjuntoEvidenciaLectura(Guid Id, string NombreArchivo);
