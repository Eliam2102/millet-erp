namespace Millet.ComercioExterior.Application.PublicPorts;

public sealed record ContenedorExportacionLectura(Guid Id, string Numero, string BlGuia, DateOnly FechaEmbarqueReal);
public sealed record EmbarqueExportacionLectura(bool Ligado, Guid? ExpedienteId = null, Guid? SucursalId = null,
    bool ListoParaFacturar = false, string? CodigoBloqueo = null, string? Motivo = null,
    string? FolioExpediente = null, string? NumeroPedimento = null, string? ClavePedimento = null,
    DateOnly? FechaPedimento = null, string? Incoterm = null, IReadOnlyList<ContenedorExportacionLectura>? Contenedores = null,
    string? Aduana = null, string? AduanaNombre = null);

public interface IEmbarqueExportacionReadPort
{
    Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, CancellationToken ct);
}
