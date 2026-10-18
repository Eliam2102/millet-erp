using MediatR;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.ActualizarInformacionImportacion;

/// <summary>
/// Actualiza la información de importación (§4.7). Solo editable en
/// Borrador o Rechazada — excepto <c>NumeroPedimento</c>, <c>NumeroBl</c>,
/// <c>FechaEstimadaArribo</c> y <c>AgenteAduanalNaviera</c>, que tienen su
/// propio endpoint <c>PATCH .../numero-pedimento</c> editable post-autorización.
///
/// El campo <see cref="NumeroPedimento"/> en este comando se ignora
/// silenciosamente — el método del agregado no lo aplica para evitar
/// sorpresas.
///
/// Campos de comercio exterior PLATFORM-TODO(F1-COM-06): supuesto Vilo,
/// confirmar con lista de Millet.
/// </summary>
public sealed record ActualizarInformacionImportacionCommand(
    Guid OrdenCompraId,
    Guid? IncotermId,
    string? PaisOrigen,
    string? NumeroContenedor,
    string? CodigoRuta,
    string? SemanaEmbarque,
    string? NumeroPedimento,
    TipoOperacionComercioExterior? TipoOperacion = null,
    string? NumeroBl = null,
    TipoTransporte? TipoTransporte = null,
    string? AgenteAduanalNaviera = null,
    string? PuertoAduanaEntrada = null,
    DateOnly? FechaEstimadaArribo = null,
    bool CertificadoOrigen = false) : IRequest;
