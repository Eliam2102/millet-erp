using MediatR;

namespace Millet.Compras.Application.Oc.ActualizarInformacionImportacion;

/// <summary>
/// Actualiza la información de importación (§4.7). Solo editable en
/// Borrador o Rechazada — excepto <c>NumeroPedimento</c> que tiene su
/// propio endpoint <c>PATCH .../numero-pedimento</c> editable
/// post-autorización.
///
/// El campo <see cref="NumeroPedimento"/> en este comando se ignora
/// silenciosamente — el método del agregado no lo aplica para evitar
/// sorpresas.
/// </summary>
public sealed record ActualizarInformacionImportacionCommand(
    Guid OrdenCompraId,
    Guid? IncotermId,
    string? PaisOrigen,
    string? NumeroContenedor,
    string? CodigoRuta,
    string? SemanaEmbarque,
    string? NumeroPedimento) : IRequest;
