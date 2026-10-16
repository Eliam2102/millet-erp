using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Umbrales;

/// <summary>
/// Crea o actualiza los umbrales N1/N2 de un departamento para la empresa
/// actual. Permiso en el endpoint: <c>compras.configuracion.editar</c>.
///
/// <para>
/// Versionado: si la fila vigente es de un día anterior, se cierra con
/// <c>VigenteHasta = hoy - 1</c> y se inserta una nueva con
/// <c>VigenteDesde = hoy</c>. Si ya existe una fila con
/// <c>VigenteDesde = hoy</c>, se actualiza en sitio.
/// </para>
/// </summary>
/// <param name="UmbralMenorCuantia">N1. <c>null</c> = sin umbral N1.</param>
/// <param name="UmbralMonto">N2. Debe ser mayor que N1 cuando N1 existe.</param>
/// <param name="ModoNivel2">Modo de nivel 2 de la requisición para el departamento.</param>
/// <param name="MontoMinimoDisponibilidad">Importe mínimo a comprar que dispara N2 por disponibilidad; <c>null</c> = sin mínimo.</param>
public sealed record UpsertUmbralCommand(
    Guid DepartamentoId,
    decimal? UmbralMenorCuantia,
    decimal UmbralMonto,
    string Moneda = "MXN",
    ModoNivel2 ModoNivel2 = ModoNivel2.Ambos,
    decimal? MontoMinimoDisponibilidad = null) : IRequest<UmbralAprobacionResponse>;
