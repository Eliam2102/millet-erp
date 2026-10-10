namespace Millet.SharedKernel.Application.Blob;

/// <summary>
/// Puerto genérico de almacenamiento de blobs para el servicio de adjuntos (F1-ADM-11 G1.2).
/// A diferencia de los puertos por módulo (Compras, Almacén, Integraciones.Aw), el caller
/// controla la <c>clave</c> (<c>{tipoEntidad}/{entidadId}/{adjuntoId}{ext}</c>) y esta es un
/// identificador INTERNO: jamás se expone en DTOs ni como URL al cliente.
/// </summary>
public interface IBlobStoragePort
{
    /// <summary>Escribe el contenido bajo la clave (sobrescribe si existe).</summary>
    Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken);

    /// <summary>Abre el contenido para lectura. Lanza <see cref="FileNotFoundException"/> si no existe.</summary>
    Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken);

    /// <summary>Elimina el blob. Idempotente: no lanza si no existe.</summary>
    Task EliminarAsync(string clave, CancellationToken cancellationToken);
}
