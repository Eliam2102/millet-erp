using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.Retorno;

/// <summary>Fila tipo Documento de <c>aw_retorno</c> (script 14 v2): una por factura. <c>TipoDocumento</c> = FacturaFinal | NotaCredito.</summary>
public sealed record AwRetornoDocumento(
    string NumeroPedido, string TipoDocumento, long VersionPedido, string? Folio, string Uuid, decimal Importe);

/// <summary>Un grupo de pago de una factura (tipo 4, script 14 v3): forma de pago + moneda con el importe ACUMULADO.</summary>
public sealed record AwPagoGrupo(string FormaPago, string Moneda, decimal Importe);

/// <summary>Fila de cualquier tipo con el acuse de la tarea de TI (aplicada_at, resultado, motivo, intentos).</summary>
public sealed record AwRetornoAcuse(
    string NumeroPedido, int Secuencia, long VersionPedido, string? TipoDocumento, string? Folio, string? Uuid,
    decimal? Importe, DateTimeOffset? AplicadaAt, byte? Resultado, string? Motivo, int Intentos,
    byte Tipo = AwRetornoAcuse.TipoDocumentoFila, string? FormaPago = null, string? Moneda = null,
    short? EstatusDestino = null, byte? Origen = null, string? UsuarioLibera = null)
{
    public const byte TipoEstatusFila = 1;
    public const byte TipoSaldoFila = 2;
    public const byte OrigenManual = 2;
    public const byte TipoDocumentoFila = 3;
    public const byte TipoPagoFila = 4;
    public const int MaxIntentos = 3;
    /// <summary>
    /// 5-nov: un 70 a un pedido que el servicio ya había liberado vuelve como «nada que hacer»: no es un rechazo ni una falla.
    /// ponytail: se reconoce por el texto del motivo; si TI lo marca con otro resultado o texto, se ajusta aquí.
    /// </summary>
    public bool NadaQueHacer => Tipo == TipoEstatusFila && EstatusDestino == 70
        && Motivo?.Contains("nada que hacer", StringComparison.OrdinalIgnoreCase) == true;
    /// <summary>Rechazada con 3 intentos o más: el pedido espera a una persona (SRC-114 3c). Un «nada que hacer» no cuenta.</summary>
    public bool Detenida => Resultado == 2 && Intentos >= MaxIntentos && !NadaQueHacer;
    /// <summary>A+W ya tenía una versión mayor (resultado 3, SRC-114 3a).</summary>
    public bool Desfasada => Resultado == 3;
}

/// <summary>
/// Escritura/lectura de <c>aw_retorno</c> (ERP → A+W). El ERP solo INSERTa filas y lee el acuse; nunca
/// actualiza ni borra (script 14). Adapters: SQL Server (Real, apagado salvo configuración) y PostgreSQL (Demo).
/// </summary>
public interface IAwRetornoPort
{
    /// <summary>
    /// Agrega la fila Documento con el siguiente <c>secuencia</c> del pedido, bajo candado por pedido.
    /// No inserta (devuelve null) si ya hay una fila de ese UUID + tipo que no esté desfasada o que
    /// tenga una versión igual o mayor: reentregas del evento no duplican ni reabren una desfasada.
    /// </summary>
    Task<int?> AgregarDocumentoAsync(AwRetornoDocumento documento, CancellationToken ct);

    /// <summary>
    /// Filas Pago (tipo 4) de una factura, bajo el mismo candado por pedido. <paramref name="grupos"/> es el estado
    /// actual completo (forma + moneda con su importe acumulado): por cada grupo cuyo último importe cambió se agrega
    /// una fila que sustituye a la anterior (SRC-121); un grupo que ya no existe se manda en 0. Una reentrega no
    /// duplica. Con <paramref name="exigirDocumento"/>, falla si aún no existe la fila FacturaFinal de ese folio, para
    /// que el pago nunca quede antes que su factura, no escribe nada (devuelve 0): el evento de factura timbrada
    /// recalcula los pagos al llegar. Devuelve cuántas filas agregó.
    /// </summary>
    Task<int> AgregarPagosAsync(string numeroPedido, long versionPedido, string folio, IReadOnlyList<AwPagoGrupo> grupos,
        bool exigirDocumento, CancellationToken ct);

    /// <summary>
    /// Fila Estatus (tipo 1): 70 (origen 2 = manual de Crédito y Cobranza, con <paramref name="usuarioLibera"/>), 72 (hoja de salida emitida) o 115.
    /// Idempotente como el documento: no inserta (devuelve null) si ya hay una fila de ese estatus que no esté desfasada
    /// o que tenga una versión igual o mayor. Devuelve la secuencia asignada.
    /// </summary>
    Task<int?> AgregarEstatusAsync(string numeroPedido, long versionPedido, short estatus, byte? origen, string? usuarioLibera, CancellationToken ct);

    /// <summary>
    /// Fila Saldo (tipo 2), solo como cifra de control: una por pedido y solo si el saldo cambió respecto a la última
    /// fila de saldo. Devuelve la secuencia, o null si no cambió.
    /// </summary>
    Task<int?> AgregarSaldoAsync(string numeroPedido, long versionPedido, decimal saldo, CancellationToken ct);

    /// <summary>Filas de cualquier tipo desfasadas sin reemplazo y rechazadas con 3 intentos o más.</summary>
    Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct);
}

/// <summary>Datos de la factura timbrada que importan a A+W (subconjunto de FacturaVentaTimbradaIntegrationEvent).</summary>
public sealed record FacturaTimbradaRetornoPayload(
    string Uuid, decimal Total, string Folio, string? NumeroPedidoAw = null, long? VersionPedidoAw = null,
    Guid? FacturaVentaId = null);

/// <summary>Cancelación de una factura (comprobante.cancelado). Propuesta de VILO: fila Documento tipo «Cancelacion» con el UUID cancelado.</summary>
public sealed record CancelacionRetornoPayload(
    string Uuid, string? Folio = null, decimal? Total = null, string? NumeroPedidoAw = null, long? VersionPedidoAw = null);

/// <summary>
/// Disparo de las filas Pago: un cobro de mostrador (registrado o cancelado), un REPP timbrado o un comprobante
/// cancelado. Basta el id; los importes se recalculan del estado actual de Facturación, así la reentrega y el
/// orden de llegada no cambian el resultado.
/// </summary>
public sealed record PagoRetornoPayload(Guid? ComprobanteId = null, Guid? ReciboPagoId = null);

/// <summary>Pedido que PT pasó a 72 (hoja de salida emitida) o a 115 (facturado y entregado), o que Crédito y Cobranza liberó a mano.</summary>
public sealed record EstatusRetornoPayload(string NumeroPedido, string Estado, string? Motivo = null, string? LiberadoPor = null);

public sealed record AwRetornoConciliacion(int Reemplazadas, int Esperando, int Detenidas);

/// <summary>TR-01: arma las filas Documento y concilia el acuse. Con el canal sin configurar no hace nada.</summary>
public sealed class AwRetornoDocumentoService(
    IAwRetornoPort retorno, IAwVersionPedidoReadPort versiones, IAwPagosReadPort pagos,
    IOptions<AwPedidosOptions> opciones, ILogger<AwRetornoDocumentoService> logger)
{
    public const string TipoFacturaFinal = "FacturaFinal";
    public const string TipoCancelacion = "Cancelacion";
    public const string TipoNotaCredito = "NotaCredito";

    /// <summary>
    /// 86e3wjt5r: fila NotaCredito de una nota de A+W ya timbrada. El evento de la nota trae la misma forma
    /// que el de la factura (UUID, total, folio, pedido y versión), por eso comparte el payload.
    /// Idempotente por UUID + tipo; null si la nota no es de A+W.
    /// </summary>
    public async Task<int?> RegistrarNotaCreditoAsync(FacturaTimbradaRetornoPayload nota, CancellationToken ct)
    {
        if (nota.NumeroPedidoAw is null || nota.VersionPedidoAw is null) return null;
        return await SinCanalApagadoAsync(() => retorno.AgregarDocumentoAsync(new AwRetornoDocumento(
            nota.NumeroPedidoAw, TipoNotaCredito, nota.VersionPedidoAw.Value, nota.Folio, nota.Uuid, nota.Total), ct));
    }

    /// <summary>Devuelve la secuencia asignada, o null si no aplica (pedido no A+W), ya existía o el canal está apagado.</summary>
    public async Task<int?> RegistrarFacturaAsync(FacturaTimbradaRetornoPayload factura, CancellationToken ct)
    {
        if (factura.NumeroPedidoAw is null || factura.VersionPedidoAw is null) return null;
        return await SinCanalApagadoAsync(() => retorno.AgregarDocumentoAsync(new AwRetornoDocumento(
            factura.NumeroPedidoAw, TipoFacturaFinal, factura.VersionPedidoAw.Value, factura.Folio, factura.Uuid, factura.Total), ct));
    }

    /// <summary>Fila nueva de cancelación (idempotente por UUID + tipo); la fila de la factura no se toca.</summary>
    public async Task<int?> RegistrarCancelacionAsync(CancelacionRetornoPayload c, CancellationToken ct)
    {
        if (c.NumeroPedidoAw is null || c.VersionPedidoAw is null) return null;
        return await SinCanalApagadoAsync(() => retorno.AgregarDocumentoAsync(new AwRetornoDocumento(
            c.NumeroPedidoAw, TipoCancelacion, c.VersionPedidoAw.Value, c.Folio, c.Uuid, c.Total ?? 0m), ct));
    }

    /// <summary>
    /// 86e3jvv6z (5-nov): el 115 sigue yendo a A+W. Devuelve la secuencia, o null si no aplica (pedido sin versión aplicada
    /// = no es de A+W), ya existía o el canal está apagado.
    /// </summary>
    public async Task<int?> RegistrarFacturadoEntregadoAsync(string numeroPedido, CancellationToken ct) =>
        await RegistrarEstatusAsync(numeroPedido, 115, null, null, ct);

    /// <summary>
    /// 9-nov (respuesta de TI, script 14 v5): con el Portal de Salidas retirado el 72 lo manda el ERP la primera vez que emite la
    /// hoja de salida del pedido (el 71 lo sigue poniendo A+W con el albarán). Misma regla de versión y de idempotencia que el 115.
    /// </summary>
    public async Task<int?> RegistrarHojaSalidaEmitidaAsync(string numeroPedido, CancellationToken ct) =>
        await RegistrarEstatusAsync(numeroPedido, 72, null, null, ct);

    /// <summary>
    /// 86e3jvv6z (5-nov): solo la liberación MANUAL de Crédito y Cobranza viaja como 70 (origen manual + usuario que liberó);
    /// el 70 por pago, saldo o reparto lo decide el servicio de A+W. Sin usuario no se manda (CK_awr_usuario_libera).
    /// </summary>
    public async Task<int?> RegistrarLiberacionManualAsync(string numeroPedido, string usuario, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(usuario) ? null
            : await RegistrarEstatusAsync(numeroPedido, 70, AwRetornoAcuse.OrigenManual, usuario.Trim(), ct);

    private async Task<int?> RegistrarEstatusAsync(string numeroPedido, short estatus, byte? origen, string? usuario, CancellationToken ct)
    {
        var version = await versiones.VersionAplicadaAsync(numeroPedido, ct);
        if (version is null)
        {
            logger.LogWarning("aw_retorno: estatus {Estatus} omitido; el pedido {Pedido} aún no tiene versión aplicada.", estatus, numeroPedido);
            return null;
        }
        return await SinCanalApagadoAsync(() => retorno.AgregarEstatusAsync(numeroPedido, version.Value, estatus, origen, usuario, ct));
    }

    /// <summary>Saldo del pedido como cifra de control (nunca la fuente): una fila por pedido cuando cambia. Null si no hay fila nueva.</summary>
    public async Task<int?> RegistrarSaldoControlAsync(string numeroPedido, decimal saldo, CancellationToken ct)
    {
        var version = await versiones.VersionAplicadaAsync(numeroPedido, ct);
        return version is null ? null
            : await SinCanalApagadoAsync(() => retorno.AgregarSaldoAsync(numeroPedido, version.Value, saldo, ct));
    }

    /// <summary>
    /// 86e3jvv6z: filas Pago (tipo 4) de cada factura A+W que toca el comprobante, con el importe acumulado por forma
    /// de pago + moneda (SRC-114, SRC-121). Devuelve cuántas filas agregó. Una factura cuyo pedido aún no tiene
    /// versión aplicada se omite con aviso.
    /// </summary>
    public async Task<int> RegistrarPagosAsync(Guid comprobanteId, CancellationToken ct)
    {
        var agregadas = 0;
        foreach (var f in await pagos.PorComprobanteAsync(comprobanteId, ct))
        {
            var version = await versiones.VersionAplicadaAsync(f.NumeroPedidoAw, ct);
            if (version is null)
            {
                logger.LogWarning("aw_retorno: pago de la factura {Folio} omitido; el pedido {Pedido} aún no tiene versión aplicada.",
                    f.Folio, f.NumeroPedidoAw);
                continue;
            }
            var grupos = Mapear(f.Grupos);
            agregadas += await SinCanalApagadoAsync(() => retorno.AgregarPagosAsync(
                f.NumeroPedidoAw, version.Value, f.Folio, grupos, exigirDocumento: f.EsFacturaFinal, ct));
        }
        return agregadas;
    }

    /// <summary>Equivalencia SAT/ISO → valor de A+W (BEZ / WAEHRUNG). Dos claves que caen en el mismo valor de A+W se suman en un solo grupo.</summary>
    private List<AwPagoGrupo> Mapear(IReadOnlyList<PagoGrupoAw> grupos) =>
        grupos.Select(g => (Forma: Valor(opciones.Value.MapeoFormaPagoAw, g.FormaPagoSat, MaxFormaPago, "forma de pago"),
                            Moneda: Valor(opciones.Value.MapeoMonedaAw, g.Moneda, MaxMoneda, "moneda"), g.Importe))
            .GroupBy(g => (g.Forma, g.Moneda))
            .Select(x => new AwPagoGrupo(x.Key.Forma, x.Key.Moneda, x.Sum(g => g.Importe)))
            .ToList();

    /// <summary>Largo de KA_ZAHLWEG.BEZ y de KA_WAEHRUNGEN.WAEHRUNG en A+W (los valores que se mandan, p. ej. EFECTIVO y PESOSMX).</summary>
    public const int MaxFormaPago = 40, MaxMoneda = 8;

    private string Valor(Dictionary<string, string> mapeo, string clave, int maximo, string que)
    {
        // A+W cruza por nombre y, si alguien renombra la forma de pago o la moneda, deja de cruzar sin dar error: aquí sí se avisa.
        string valor;
        if (mapeo.Count == 0) valor = clave;
        else if (!mapeo.TryGetValue(clave, out valor!))
        {
            logger.LogWarning("aw_retorno: la {Que} «{Clave}» no está en la equivalencia hacia A+W (KA_ZAHLWEG.BEZ / KA_WAEHRUNGEN.WAEHRUNG); el pago no se manda hasta completarla.", que, clave);
            throw new BusinessRuleException("AW_RETORNO_MAPEO_PAGO", $"Sin equivalencia de {que} «{clave}» hacia A+W.");
        }
        return valor.Length <= maximo ? valor
            : throw new BusinessRuleException("AW_RETORNO_MAPEO_PAGO", $"La {que} «{valor}» excede {maximo} caracteres en aw_retorno.");
    }

    /// <summary>
    /// Lee el acuse: una desfasada se reemplaza con una fila nueva con la versión que el ERP ya aplicó
    /// (si aún no pasó de la de la fila, espera); una rechazada con 3 intentos solo se reporta.
    /// </summary>
    public async Task<AwRetornoConciliacion> ConciliarAcusesAsync(CancellationToken ct)
    {
        var acuses = await SinCanalApagadoAsync(() => retorno.LeerAcusesPorAtenderAsync(ct)) ?? [];
        int reemplazadas = 0, esperando = 0, detenidas = 0;
        foreach (var a in acuses)
        {
            if (a.Detenida)
            {
                detenidas++;
                logger.LogWarning("aw_retorno detenido: pedido {Pedido} secuencia {Secuencia} rechazada {Intentos} veces ({Motivo}). Requiere revisión de una persona.",
                    a.NumeroPedido, a.Secuencia, a.Intentos, a.Motivo);
            }
            else if (a.Desfasada && a.Tipo == AwRetornoAcuse.TipoPagoFila)
            {
                // El pago desfasado se recalcula del estado actual de la factura con la versión nueva.
                var version = await versiones.VersionAplicadaAsync(a.NumeroPedido, ct);
                var factura = version is null || version <= a.VersionPedido ? null
                    : await pagos.PorFolioAsync(a.NumeroPedido, a.Folio!, ct);
                if (factura is null)
                {
                    esperando++;
                    logger.LogWarning("aw_retorno pago desfasado sin reemplazo: pedido {Pedido} factura {Folio} secuencia {Secuencia}.",
                        a.NumeroPedido, a.Folio, a.Secuencia);
                    continue;
                }
                try
                {
                    if (await retorno.AgregarPagosAsync(a.NumeroPedido, version!.Value, factura.Folio, Mapear(factura.Grupos),
                            exigirDocumento: false, ct) > 0)
                        reemplazadas++;
                }
                catch (BusinessRuleException ex) when (ex.Code == "AW_RETORNO_MAPEO_PAGO")
                {
                    // Una equivalencia que falta no detiene la conciliación de las demás filas.
                    esperando++;
                    logger.LogWarning("aw_retorno pago desfasado sin reemplazo: pedido {Pedido} factura {Folio}: {Motivo}",
                        a.NumeroPedido, a.Folio, ex.Message);
                }
            }
            else if (a.Desfasada && a.Tipo == AwRetornoAcuse.TipoEstatusFila && a.EstatusDestino is { } estatus)
            {
                var version = await versiones.VersionAplicadaAsync(a.NumeroPedido, ct);
                if (version is null || version <= a.VersionPedido)
                {
                    esperando++;
                    logger.LogWarning("aw_retorno estatus desfasado sin reemplazo: pedido {Pedido} secuencia {Secuencia} (versión {Version}).",
                        a.NumeroPedido, a.Secuencia, a.VersionPedido);
                    continue;
                }
                if (await retorno.AgregarEstatusAsync(a.NumeroPedido, version.Value, estatus, a.Origen, a.UsuarioLibera, ct) is not null)
                    reemplazadas++;
            }
            else if (a.Desfasada && a.Tipo != AwRetornoAcuse.TipoDocumentoFila)
            {
                esperando++; // saldo (tipo 2): es solo cifra de control, no se reemplaza; la siguiente variación escribe la vigente.
                logger.LogWarning("aw_retorno desfasado sin reemplazo (tipo {Tipo}): pedido {Pedido} secuencia {Secuencia}.",
                    a.Tipo, a.NumeroPedido, a.Secuencia);
            }
            else if (a.Desfasada)
            {
                var version = await versiones.VersionAplicadaAsync(a.NumeroPedido, ct);
                if (version is null || version <= a.VersionPedido)
                {
                    esperando++;
                    logger.LogWarning("aw_retorno desfasado sin reemplazo: pedido {Pedido} secuencia {Secuencia} (versión {Version}); el ERP aún no aplica una versión mayor.",
                        a.NumeroPedido, a.Secuencia, a.VersionPedido);
                    continue;
                }
                if (await retorno.AgregarDocumentoAsync(new AwRetornoDocumento(
                        a.NumeroPedido, a.TipoDocumento!, version.Value, a.Folio, a.Uuid!, a.Importe ?? 0m), ct) is not null)
                    reemplazadas++;
            }
        }
        return new AwRetornoConciliacion(reemplazadas, esperando, detenidas);
    }

    private async Task<T?> SinCanalApagadoAsync<T>(Func<Task<T>> accion)
    {
        try { return await accion(); }
        catch (BusinessRuleException ex) when (ex.Code is "AW_ORIGEN_SIN_CONFIGURAR" or "AW_DEMO_NO_CONFIGURADA" or "AW_DEMO_NO_PERMITIDO")
        {
            logger.LogInformation("aw_retorno omitido: {Codigo}. El canal de A+W no está configurado.", ex.Code);
            return default;
        }
    }
}
