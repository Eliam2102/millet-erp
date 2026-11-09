using System.Text.Json;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// Evento normalizado que recibe el motor (ficha C1.4 §2): tipo, id, empresa, fecha contable, documento origen e importes con nombre
/// (E12: <c>Subtotal</c>, <c>Iva</c>, <c>RetencionesTotal</c>, <c>Total</c>, <c>TotalMenosRetenciones</c>). Un importe nulo es un dato
/// que el evento no trajo: si la plantilla lo usa, el motor rechaza con <see cref="CausaRechazoContable.DatoFaltante"/>.
/// CON-11: <see cref="TipoCambioPorRol"/> = TC propio de las líneas de esos roles (p. ej. BANCOS al TC del pago); el resto usa
/// <see cref="TipoCambio"/> (el del documento pagado).
/// B8: <see cref="CuentaPorRol"/> = código de la cuenta contable que el emisor ya conoce para un rol (p. ej. BANCOS = la cuenta
/// contable de la cuenta bancaria del movimiento). Manda sobre la cuenta del rol; sin dato, se usa el rol.
/// </summary>
public sealed record EventoContable(
    string Tipo, Guid EventoId, Guid EmpresaId, DateOnly FechaContable, DocumentoOrigenPoliza Documento,
    Guid? SucursalId, Guid? ProveedorId, Guid? ClienteId, Guid? CentroCostoId, Guid? OrdenCompraId,
    string Moneda, decimal? TipoCambio, IReadOnlyDictionary<string, decimal?> Importes, IReadOnlyList<LineaEventoContable> Lineas,
    string Payload, TipoDocumentoPuente TipoPuente = TipoDocumentoPuente.Factura, Guid? FacturaOrigenId = null, string? LlaveOverride = null,
    IReadOnlyDictionary<string, decimal>? TipoCambioPorRol = null, IReadOnlyDictionary<string, string>? CuentaPorRol = null)
{
    /// <summary>
    /// CON-11 (86e3kdypd): campo de las líneas de plantilla de diferencia cambiaria realizada. No es un importe del evento: con
    /// <see cref="TipoCambioPorRol"/>, el motor asienta ahí lo que separa el lado del documento (TC de la factura) del lado del banco
    /// (TC del pago), en pesos; cargo = pérdida, abono = utilidad.
    /// </summary>
    public const string CampoDiferenciaCambiaria = "DiferenciaCambiaria";

    /// <summary>C1.5: una reversa derivada (p. ej. cobro en efectivo cancelado ya liquidado) usa su propia llave.</summary>
    public string Llave => LlaveOverride ?? $"{Tipo}:{EventoId}";
}

/// <summary>
/// Línea del documento (importe antes de impuestos). <see cref="EsServicio"/> viene de la línea de OC (86e3vrh7u); sin dato, false.
/// C1.5 (aditivo): <see cref="ClienteId"/> = auxiliar por línea (liquidación de efectivo: un abono por cobro y su cliente);
/// <see cref="Clasificador"/> = sufijo de rol por línea (ventas por tipo de producto); <see cref="CuentaId"/> = cuenta fija de la línea
/// (devolución interna: la misma cuenta de la póliza de consumo, V64).
/// F1-ALM-13: <see cref="ArticuloId"/> = artículo de la línea de Almacén; <see cref="EnriquecedorGrupoArticulo"/> pone su grupo como
/// <see cref="Clasificador"/> para el rol <c>INVENTARIO_{grupo}</c>.
/// </summary>
/// 86e3zcbjw: <see cref="Grupo"/> separa líneas que no son del documento (p. ej. <see cref="GrupoRetencion"/>: una por concepto de
/// retención); solo las toma la línea de plantilla del mismo grupo.
public sealed record LineaEventoContable(Guid? LineaOcId, decimal Importe, Guid? CentroCostoId, bool EsServicio,
    Guid? ClienteId = null, string? Clasificador = null, Guid? CuentaId = null, Guid? ArticuloId = null, string? Grupo = null)
{
    public const string GrupoRetencion = "RETENCION";
}

/// <summary>El payload no se puede leer como el contrato del tipo (no es rechazo de negocio: es un evento corrupto).</summary>
public sealed class EventoIlegibleException(string mensaje) : Exception(mensaje);

/// <summary>
/// Convierte el JSON de cada tipo de evento soportado al <see cref="EventoContable"/>. Contabilidad no referencia a los módulos
/// emisores: lee su contrato JSON (mismo criterio que Almacén con <c>CxpContracts</c>). C1.5 agrega aquí los tipos #1 y #3–#15.
/// Un tipo desconocido se normaliza con lo mínimo (empresa y fecha) para que el motor lo mande a la bandeja con
/// <see cref="CausaRechazoContable.SinPlantilla"/> y no se pierda (C1.4-d).
/// </summary>
public static partial class NormalizadorEventos
{
    public const string FacturaProveedorRegistrada = "cuentas_por_pagar.factura.registrada.v1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EventoContable Normalizar(string tipo, Guid eventoId, string payload)
    {
        try
        {
            return tipo == DevolucionAnticipoAgente ? DevolucionAgente(eventoId, payload)
                : tipo == CostoImportacionAplicado ? CostoImportacion(eventoId, payload)
                : tipo == FacturaProveedorRegistrada ? FacturaProveedor(eventoId, payload)
                : NormalizarC15(tipo, eventoId, payload) ?? NormalizarC15b(tipo, eventoId, payload)
                    ?? NormalizarPcv(tipo, eventoId, payload) ?? NormalizarTerminal(tipo, eventoId, payload)
                    ?? Generico(tipo, eventoId, payload);
        }
        catch (JsonException e)
        {
            throw new EventoIlegibleException($"El payload de {tipo} no es JSON válido: {e.Message}");
        }
    }

    private static EventoContable Generico(string tipo, Guid eventoId, string payload)
    {
        var g = JsonSerializer.Deserialize<EventoGenerico>(payload, Json) ?? throw new EventoIlegibleException("Payload vacío.");
        return new(tipo, eventoId, g.EmpresaId, FechaContable.HoyLocal(g.OcurridoEn), new DocumentoOrigenPoliza(Modulo(tipo), tipo, eventoId, null, null),
            null, null, null, null, null, "MXN", null, new Dictionary<string, decimal?>(), [], payload);
    }

    /// <summary>
    /// Plantilla #2 (TT). Fecha contable = <c>OcurridoEn</c> en hora local (decisión del 117 del 26-oct; el evento no trae la fecha de
    /// la factura). El folio del proveedor no viaja en el evento y CxP no tiene puerto de lectura: queda nulo (duda en el PR); el UUID sí.
    /// </summary>
    private static EventoContable FacturaProveedor(Guid eventoId, string payload)
    {
        var f = JsonSerializer.Deserialize<FacturaProveedorRegistradaContrato>(payload, Json) ?? throw new EventoIlegibleException("Payload vacío.");
        var importes = new Dictionary<string, decimal?>
        {
            ["Subtotal"] = f.Subtotal,
            ["Iva"] = f.Iva,
            ["RetencionesTotal"] = f.RetencionesTotal,
            ["Total"] = f.TotalFactura,
            ["TotalMenosRetenciones"] = f.TotalFactura, // el Total del CFDI ya viene neto de retenciones
            // 86e3zcbjw: la factura se registra por el total; la retención se descuenta al pagar (#2 v3 y #5 v3).
            [RetencionesContables.CampoTotalMas] = f.TotalFactura + (f.RetencionesTotal ?? 0m),
        };
        var lineas = (f.Lineas ?? []).Select(l => new LineaEventoContable(l.LineaOcId, l.Importe, l.CentroCostoId, l.EsServicio ?? false)).ToList();
        lineas.AddRange(RetencionesContables.Lineas(f.Retenciones, f.ConceptoRetencion));
        return new(FacturaProveedorRegistrada, eventoId, f.EmpresaId, FechaContable.HoyLocal(f.OcurridoEn),
            new DocumentoOrigenPoliza("cuentas_por_pagar", "factura_proveedor", f.FacturaProveedorId, null, f.Uuid),
            f.SucursalId, f.ProveedorId, null, f.CentroCostoId, f.OrdenCompraId == Guid.Empty ? null : f.OrdenCompraId,
            string.IsNullOrWhiteSpace(f.Moneda) ? "MXN" : f.Moneda.Trim().ToUpperInvariant(), f.TipoCambio, importes, lineas, payload);
    }

    private static string Modulo(string tipo) => tipo.Split('.')[0];

    private sealed record EventoGenerico(Guid EmpresaId, DateTimeOffset OcurridoEn);

    /// <summary>Espejo del contrato <c>FacturaProveedorRegistradaIntegrationEvent</c> (CxP), solo los campos que usa el motor.</summary>
    private sealed record FacturaProveedorRegistradaContrato(
        Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaProveedorId, Guid OrdenCompraId, decimal TotalFactura,
        IReadOnlyList<LineaFacturaContrato>? Lineas, Guid? ProveedorId, string? Uuid, decimal? Subtotal, decimal? Iva,
        decimal? RetencionesTotal, string? Moneda, decimal? TipoCambio, Guid? SucursalId, Guid? CentroCostoId,
        IReadOnlyList<RetencionesContables.RetencionContrato>? Retenciones = null, string? ConceptoRetencion = null);

    private sealed record LineaFacturaContrato(Guid? LineaOcId, decimal Importe, Guid? CentroCostoId, bool? EsServicio);
}
