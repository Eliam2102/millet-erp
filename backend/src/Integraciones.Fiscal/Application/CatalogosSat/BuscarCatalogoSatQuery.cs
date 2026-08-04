using MediatR;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Application.CatalogosSat;

/// <summary>
/// Búsqueda typeahead de un catálogo SAT en vivo contra FiscalAPI
/// (FAC-DET-PR1). Forma canónica de resolver clave producto/servicio,
/// clave unidad y objeto de impuesto en todo el ERP — el catálogo del
/// PAC siempre refleja la versión vigente publicada por el SAT.
/// </summary>
/// <param name="Catalogo">Catálogo SAT a consultar.</param>
/// <param name="Buscar">
/// Texto libre (código o descripción). El PAC exige mínimo 4 caracteres
/// para búsqueda por texto; con menos, el handler intenta lookup exacto
/// por código (<c>MTK</c>, <c>H87</c>, <c>01</c>…). Vacío solo se admite
/// para <see cref="CatalogoSat.ObjetoImp"/> (catálogo chico — se listan
/// todas las entradas).
/// </param>
/// <param name="Limit">Máximo de resultados (1–50).</param>
public sealed record BuscarCatalogoSatQuery(
    CatalogoSat Catalogo,
    string Buscar,
    int Limit = 20) : IRequest<IReadOnlyList<CatalogoSatItem>>;
