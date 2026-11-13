namespace Millet.ComercioExterior.Application.PublicPorts;

public sealed record PartidaAduanalLectura(int Posicion, string FraccionArancelaria, string UnidadAduana);
public sealed record ContenedorExportacionLectura(Guid Id, string Numero, string BlGuia, DateOnly? FechaEmbarqueReal);
public sealed record EmbarqueExportacionLectura(bool Ligado, Guid? ExpedienteId = null, Guid? SucursalId = null,
    bool ListoParaFacturar = false, string? CodigoBloqueo = null, string? Motivo = null,
    string? FolioExpediente = null, string? NumeroPedimento = null, string? ClavePedimento = null,
    DateOnly? FechaPedimento = null, string? Incoterm = null, IReadOnlyList<ContenedorExportacionLectura>? Contenedores = null,
    string? Aduana = null, string? AduanaNombre = null, int? VersionEmbarque = null, IReadOnlyList<PartidaAduanalLectura>? DatosAduanales = null);

public interface IEmbarqueExportacionReadPort
{
    Task<bool> PreparacionVigenteAsync(Guid empresaId, Guid contenedorId, CancellationToken ct) => Task.FromResult(false);

    Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, CancellationToken ct) => ObtenerAsync(pedidoFacturableId, null, ct);

    /// <summary>
    /// FAC-08: con <paramref name="contenedorId"/> (factura de la salida de un contenedor) la compuerta y los
    /// contenedores del CCE se limitan a ese contenedor.
    /// </summary>
    Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, Guid? contenedorId, CancellationToken ct);
}
