using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Application.Retorno;

/// <summary>Fila tipo Documento de <c>aw_retorno</c> (script 14 v2): una por factura. <c>TipoDocumento</c> = FacturaFinal | NotaCredito.</summary>
public sealed record AwRetornoDocumento(
    string NumeroPedido, string TipoDocumento, long VersionPedido, string? Folio, string Uuid, decimal Importe);

/// <summary>Fila Documento con el acuse de la tarea de TI (aplicada_at, resultado, motivo, intentos).</summary>
public sealed record AwRetornoAcuse(
    string NumeroPedido, int Secuencia, long VersionPedido, string TipoDocumento, string? Folio, string Uuid,
    decimal Importe, DateTimeOffset? AplicadaAt, byte? Resultado, string? Motivo, int Intentos)
{
    public const int MaxIntentos = 3;
    /// <summary>Rechazada con 3 intentos o más: el pedido espera a una persona (SRC-114 3c).</summary>
    public bool Detenida => Resultado == 2 && Intentos >= MaxIntentos;
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

    /// <summary>Filas Documento desfasadas sin reemplazo y rechazadas con 3 intentos o más.</summary>
    Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct);
}

/// <summary>Datos de la factura timbrada que importan a A+W (subconjunto de FacturaVentaTimbradaIntegrationEvent).</summary>
public sealed record FacturaTimbradaRetornoPayload(
    string Uuid, decimal Total, string Folio, string? NumeroPedidoAw = null, long? VersionPedidoAw = null);

public sealed record AwRetornoConciliacion(int Reemplazadas, int Esperando, int Detenidas);

/// <summary>TR-01: arma las filas Documento y concilia el acuse. Con el canal sin configurar no hace nada.</summary>
public sealed class AwRetornoDocumentoService(
    IAwRetornoPort retorno, IAwVersionPedidoReadPort versiones, ILogger<AwRetornoDocumentoService> logger)
{
    public const string TipoFacturaFinal = "FacturaFinal";

    /// <summary>Devuelve la secuencia asignada, o null si no aplica (pedido no A+W), ya existía o el canal está apagado.</summary>
    public async Task<int?> RegistrarFacturaAsync(FacturaTimbradaRetornoPayload factura, CancellationToken ct)
    {
        if (factura.NumeroPedidoAw is null || factura.VersionPedidoAw is null) return null;
        return await SinCanalApagadoAsync(() => retorno.AgregarDocumentoAsync(new AwRetornoDocumento(
            factura.NumeroPedidoAw, TipoFacturaFinal, factura.VersionPedidoAw.Value, factura.Folio, factura.Uuid, factura.Total), ct));
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
                        a.NumeroPedido, a.TipoDocumento, version.Value, a.Folio, a.Uuid, a.Importe), ct) is not null)
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
