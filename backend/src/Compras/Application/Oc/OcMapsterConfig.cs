using Mapster;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc;

/// <summary>
/// Registra los mappings de Mapster del submódulo Órdenes de Compra.
/// <see cref="Millet.SharedKernel.Application.MilletApplicationServiceCollectionExtensions.AddMilletApplication"/>
/// invoca <c>TypeAdapterConfig.GlobalSettings.Scan(...)</c> que descubre
/// implementaciones de <see cref="IRegister"/> en los assemblies dados.
/// </summary>
public sealed class OcMapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // OrdenCompra → OrdenCompraResponse: convención por nombre,
        // excepto el VO Folio (extraer string). Lineas se mapean por
        // el subconfig de abajo (Mapster los proyecta automáticamente
        // al detectar la collection en el destino).
        config.NewConfig<OrdenCompra, OrdenCompraResponse>()
            .Map(dest => dest.Folio, src => src.Folio.Valor)
            // ADR-0042 addendum: etiqueta del proveedor resuelta por el handler
            // tras el map (batch IProveedorReadPort). Null explícito (record posicional).
            .Map(dest => dest.ProveedorRazonSocial, src => (string?)null)
            .Map(dest => dest.ProveedorClave, src => (string?)null);

        // UF2-PR3-a: LineaOrdenCompra → LineaOrdenCompraResponse.
        // SubtotalLinea es computed (no propiedad seteable), Mapster
        // lo lee del getter automáticamente. El resto es 1-a-1 por
        // convención de nombres.
        //
        // RequisicionFolio NO existe en el dominio (la línea solo guarda
        // RequisicionId): lo resuelve ObtenerOrdenCompraPorIdHandler tras
        // el map, vía query directa intra-Compras (ADR-0042, intra-módulo).
        // Se mapea explícitamente a null (no .Ignore: en un record posicional
        // Mapster construye por constructor y .Ignore sobre un parámetro del
        // ctor desbalancea los argumentos — "Incorrect number of arguments").
        config.NewConfig<LineaOrdenCompra, LineaOrdenCompraResponse>()
            .Map(dest => dest.RequisicionFolio, src => (string?)null)
            // ADR-0042 addendum: etiqueta del artículo resuelta por el handler
            // tras el map (batch IArticuloReadPort). Null explícito.
            .Map(dest => dest.ArticuloClave, src => (string?)null)
            .Map(dest => dest.ArticuloNombre, src => (string?)null)
            // Fase E PR3: CentroCostoId lo mapea Mapster por nombre; Clave/Nombre
            // los resuelve el handler tras el map (batch IDim3ReadPort). Null explícito.
            .Map(dest => dest.CentroCostoClave, src => (string?)null)
            .Map(dest => dest.CentroCostoNombre, src => (string?)null);

        // UF3-PR2: AdjuntoOC → AdjuntoOcResponse. Mapeo explícito de
        // TamañoBytes → TamanoBytes (el response usa ASCII para evitar
        // caracteres no-ASCII en property names del JSON serializado;
        // el agregado mantiene TamañoBytes por convención del dominio
        // en español).
        config.NewConfig<AdjuntoOC, AdjuntoOcResponse>()
            .Map(dest => dest.TamanoBytes, src => src.TamañoBytes);
    }
}
