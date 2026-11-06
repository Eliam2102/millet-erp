using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Timbrado.Contingencias;

/// <summary>
/// Registro automático de la bitácora de contingencias (F6.8). Lo llama
/// <c>TimbradoEjecutor</c> tras cada intento y <c>DescartarComprobanteHandler</c>
/// al descartar, en el mismo change-tracker que la FSM del comprobante.
/// </summary>
public static class ContingenciasTimbrado
{
    /// <summary>
    /// Causa externa de un código de error del PAC, o <c>null</c> si el rechazo es
    /// del propio documento (CFDI40xxx, receptor, catálogo): eso se corrige, no es contingencia.
    /// </summary>
    public static TipoContingenciaTimbrado? Clasificar(string? errorCodigo)
    {
        if (string.IsNullOrWhiteSpace(errorCodigo)) return null;
        var codigo = errorCodigo.Trim().ToUpperInvariant();
        if (codigo.StartsWith("CSD_", StringComparison.Ordinal)) return TipoContingenciaTimbrado.Certificado;
        if (codigo.StartsWith("PAC_", StringComparison.Ordinal)) return TipoContingenciaTimbrado.Pac;
        // Código HTTP del PAC cuando no trae uno del SAT (FiscalApiTimbradoAdapter).
        return codigo switch
        {
            "INTERNALSERVERERROR" or "BADGATEWAY" or "SERVICEUNAVAILABLE" or "GATEWAYTIMEOUT"
                or "REQUESTTIMEOUT" or "TOOMANYREQUESTS" or "500" or "502" or "503" or "504" or "408" or "429"
                => TipoContingenciaTimbrado.Pac,
            "UNAUTHORIZED" or "FORBIDDEN" or "401" or "403" => TipoContingenciaTimbrado.Pac,
            _ => null,
        };
    }

    /// <summary>Aplica a la bitácora el resultado de un intento de timbrado.</summary>
    public static async Task AplicarIntentoAsync(
        FacturacionDbContext db,
        Comprobante comprobante,
        string? errorCodigo,
        string? errorMensaje,
        bool timbrado,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        var abierta = await AbiertaDeAsync(db, comprobante.Id, cancellationToken);
        if (timbrado)
        {
            abierta?.RegularizarConCfdi(comprobante.Uuid!, ahora);
            return;
        }

        if (Clasificar(errorCodigo) is not { } tipo) return;
        if (abierta is not null)
        {
            abierta.ActualizarError(errorCodigo, Motivo(errorCodigo, errorMensaje));
            return;
        }

        db.ContingenciasTimbrado.Add(ContingenciaTimbrado.Registrar(
            comprobante.EmpresaId, comprobante.SucursalId, tipo, OrigenContingenciaTimbrado.Automatica,
            Motivo(errorCodigo, errorMensaje), errorCodigo, ahora, comprobante.Id, comprobante.Folio,
            registradaPorId: null, registradaPorNombre: null));
    }

    /// <summary>El documento se descartó: su contingencia abierta queda descartada (folio quemado).</summary>
    public static async Task AplicarDescarteAsync(
        FacturacionDbContext db, Comprobante comprobante, DateTimeOffset ahora, string? usuarioNombre,
        CancellationToken cancellationToken)
    {
        var abierta = await AbiertaDeAsync(db, comprobante.Id, cancellationToken);
        abierta?.MarcarDescartada(ahora, usuarioNombre);
    }

    private static async Task<ContingenciaTimbrado?> AbiertaDeAsync(
        FacturacionDbContext db, Guid comprobanteId, CancellationToken cancellationToken) =>
        db.ContingenciasTimbrado.Local.FirstOrDefault(c =>
            c.ComprobanteId == comprobanteId && c.Estado == EstadoContingenciaTimbrado.Abierta)
        ?? await db.ContingenciasTimbrado.FirstOrDefaultAsync(c =>
            c.ComprobanteId == comprobanteId && c.Estado == EstadoContingenciaTimbrado.Abierta, cancellationToken);

    private static string Motivo(string? codigo, string? mensaje) =>
        string.IsNullOrWhiteSpace(mensaje) ? $"Timbrado fallido ({codigo})." : $"{codigo}: {mensaje}";
}
