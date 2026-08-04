using Mapster;
using Millet.Compras.Application.ObtenerRequisicionPorId;
using Millet.Compras.Domain;

namespace Millet.Compras.Application;

/// <summary>
/// Registra los mappings de Mapster del módulo Compras.
/// <see cref="MilletApplicationServiceCollectionExtensions.AddMilletApplication"/>
/// invoca <c>TypeAdapterConfig.GlobalSettings.Scan(...)</c> que descubre
/// implementaciones de <see cref="IRegister"/> en los assemblies dados.
/// </summary>
public sealed class ComprasMapsterConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // LineaRequisicion → LineaResponse (B.0): decompone Money y
        // computa CantPendiente. Pendiente = Cantidad - (CantDeAlmacen +
        // CantRecibida): lo todavía no cubierto por reserva ni por
        // recepción. CantDeCompra es el saldo asignado a OC; se expone
        // también para que el FE pinte el segmento "pendiente compra"
        // en CubrimientoBar.
        config.NewConfig<LineaRequisicion, LineaResponse>()
            .Map(dest => dest.PrecioEstimadoMonto, src => src.PrecioEstimado.Amount)
            .Map(dest => dest.PrecioEstimadoMoneda, src => src.PrecioEstimado.Currency)
            .Map(dest => dest.CantPendiente,
                src => src.Cantidad - (src.CantidadDeAlmacen + src.CantidadRecibida))
            .Map(dest => dest.CantDeAlmacen, src => src.CantidadDeAlmacen)
            .Map(dest => dest.CantDeCompra, src => src.CantidadDeCompra)
            .Map(dest => dest.CantRecibida, src => src.CantidadRecibida)
            // ADR-0043 #3: el pendiente de entregar se deriva del DOMINIO
            // (LineaRequisicion.CantidadPendienteEntregar = (almacén+recibida)
            // − entregada), fuente única. Ya NO se enriquece en el handler vía
            // IAlmacenEntregasReadPort. CantEntregadoDeAlmacen lleva ahora el
            // total entregado (CantidadEntregada) — el FE lo muestra como "Ya
            // entregado". (Rename a CantEntregada queda como follow-up de limpieza.)
            .Map(dest => dest.CantEntregadoDeAlmacen, src => src.CantidadEntregada)
            .Map(dest => dest.CantPendienteEntregar, src => src.CantidadPendienteEntregar)
            // ADR-0042 addendum: la etiqueta del artículo no existe en el dominio
            // (la línea solo guarda ArticuloId); la resuelve el handler tras el map
            // (batch IArticuloReadPort). Null explícito, no .Ignore (record posicional).
            .Map(dest => dest.ArticuloClave, src => (string?)null)
            .Map(dest => dest.ArticuloNombre, src => (string?)null)
            // Ídem CC-Máquina: la línea solo guarda CentroCostoId; el handler
            // resuelve clave/nombre tras el map (batch IDim3ReadPort). Null explícito.
            .Map(dest => dest.CentroCostoClave, src => (string?)null)
            .Map(dest => dest.CentroCostoNombre, src => (string?)null);

        // Autorizacion → AutorizacionResponse (B.0): convención.
        config.NewConfig<Autorizacion, AutorizacionResponse>();

        // Requisicion → RequisicionResponse: convención excepto el VO
        // Folio (extraer string) y las colecciones (ordenar
        // determinísticamente por Posicion / FechaHora).
        config.NewConfig<Requisicion, RequisicionResponse>()
            .Map(dest => dest.Folio, src => src.Folio.Valor)
            .Map(dest => dest.Lineas,
                src => src.Lineas.OrderBy(l => l.Posicion).ToList())
            .Map(dest => dest.Autorizaciones,
                src => src.Autorizaciones.OrderBy(a => a.FechaHora).ToList())
            // ADR-0042 addendum: etiqueta del proveedor sugerido resuelta por el
            // handler tras el map (batch IProveedorReadPort). Null explícito.
            .Map(dest => dest.ProveedorSugeridoRazonSocial, src => (string?)null)
            .Map(dest => dest.ProveedorSugeridoClave, src => (string?)null);
    }
}
