using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Domain.Bancos;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

/// <summary>
/// U-4 (86e3yzemw) · rama de falla de los dos pasos de proveedor de la oleada 1, sobre la pieza común de incidencias:
/// <list type="bullet">
///   <item><b>Documento observado</b> (ADM-proveedor-09): al rechazar el contenido de un documento del expediente se abre una
///   incidencia por proveedor y tipo de documento, con el documento observado como evidencia; responsable = quien lo subió
///   (si ya no existe, quien lo revisó). Se retoma en el expediente del proveedor y se resuelve sola al aceptar un documento
///   de ese tipo.</item>
///   <item><b>Cuenta bancaria rechazada</b> (ADM-cuentas-07): al rechazar una cuenta por validar se abre una incidencia por
///   proveedor y moneda, con el estado de cuenta vigente más reciente como evidencia; responsable = quien capturó la cuenta
///   (si no hay, quien la rechazó). Se retoma en las cuentas del proveedor y se resuelve sola al validar una cuenta de esa
///   moneda.</item>
/// </list>
/// Se llama <b>después</b> de guardar el rechazo o la aceptación (el puerto guarda en su propia operación).
/// </summary>
internal static class IncidenciasProveedor
{
    public const string Modulo = "datos_maestros";
    public const string TipoDocumento = "proveedor";
    public const string CodigoDocumentoObservado = "PROVEEDOR_DOCUMENTO_OBSERVADO";
    public const string CodigoCuentaRechazada = "PROVEEDOR_CUENTA_RECHAZADA";

    public static string Ruta(Guid proveedorId) => $"/admin/datos-maestros/proveedores/{proveedorId}";

    private static string Etiqueta(Proveedor p) => $"{p.Clave} · {p.RazonSocial}";

    public static Task<IncidenciaAbierta> AbrirDocumentoObservadoAsync(
        IIncidenciasPort incidencias, Proveedor proveedor, Adjunto adjunto, string nombreTipo, Guid? revisorId, CancellationToken ct) =>
        AbrirAsync(incidencias, new AbrirIncidenciaSolicitud(
            Modulo, TipoDocumento, proveedor.Id, Etiqueta(proveedor), PasosIncidencia.AdmProveedorDocumentoObservado,
            adjunto.ObservacionRevision ?? "Documento observado al revisar su contenido.", adjunto.SubidoPorId,
            CodigoMotivo: CodigoDocumentoObservado,
            Detalle: $"Documento «{nombreTipo}» ({adjunto.NombreArchivo}) observado al revisar su contenido: pide al proveedor uno correcto y súbelo al expediente.",
            DetalleClave: adjunto.TipoDocumentoId.ToString(),
            PasoReanudacion: PasosIncidencia.AdmProveedorExpediente, RutaReanudacion: Ruta(proveedor.Id),
            EvidenciaAdjuntoIds: [adjunto.Id]), revisorId, ct);

    public static Task<int> ResolverDocumentoAsync(IIncidenciasPort incidencias, Guid proveedorId, Guid tipoDocumentoId, string nombreTipo,
        CancellationToken ct) =>
        incidencias.ResolverPorDocumentoAsync(PasosIncidencia.AdmProveedorDocumentoObservado, proveedorId, tipoDocumentoId.ToString(),
            $"Se aceptó un documento «{nombreTipo}» tras revisar su contenido.", ct);

    public static async Task<IncidenciaAbierta> AbrirCuentaRechazadaAsync(
        IIncidenciasPort incidencias, CompartidoDbContext db, Proveedor proveedor, CuentaBancariaProveedor cuenta, Guid? rechazadaPor,
        DateOnly hoy, CancellationToken ct)
    {
        // Evidencia: el estado de cuenta o la carta bancaria vigente más reciente del proveedor (la cuenta por validar aún no tiene el suyo).
        var soportes = await (
            from a in db.Adjuntos.AsNoTracking()
            join t in db.AdjuntoTiposDocumento.AsNoTracking() on a.TipoDocumentoId equals t.Id
            where a.EntidadId == proveedor.Id && t.TipoEntidad == "proveedor"
                  && t.Codigo == ValidarCuentaBancariaProveedorHandler.TipoDocumentoSoporte && a.BajaEn == null
            orderby a.SubidoEn descending
            select a).Take(5).ToListAsync(ct);
        var evidencia = soportes.Where(a => a.EstadoEn(hoy) is not (EstadoAdjunto.Baja or EstadoAdjunto.Vencido)).Select(a => a.Id).Take(1).ToList();
        var identificador = Clabe.Enmascarar(cuenta.Identificador);
        return await AbrirAsync(incidencias, new AbrirIncidenciaSolicitud(
            Modulo, TipoDocumento, proveedor.Id, Etiqueta(proveedor), PasosIncidencia.AdmProveedorCuentaBancaria,
            cuenta.MotivoEstado ?? "Cuenta bancaria rechazada.", cuenta.CapturadaPorId ?? rechazadaPor ?? Guid.Empty,
            CodigoMotivo: CodigoCuentaRechazada,
            Detalle: $"Cambio de cuenta {cuenta.Banco} {identificador} ({cuenta.Moneda}) rechazado: aclara con el proveedor y captura la cuenta correcta con su estado de cuenta.",
            DetalleClave: cuenta.Moneda,
            PasoReanudacion: PasosIncidencia.AdmProveedorCuentas, RutaReanudacion: Ruta(proveedor.Id),
            EvidenciaAdjuntoIds: evidencia), rechazadaPor, ct);
    }

    public static Task<int> ResolverCuentaAsync(IIncidenciasPort incidencias, Guid proveedorId, CuentaBancariaProveedor validada, CancellationToken ct) =>
        incidencias.ResolverPorDocumentoAsync(PasosIncidencia.AdmProveedorCuentaBancaria, proveedorId, validada.Moneda,
            $"Se validó la cuenta {validada.Banco} {Clabe.Enmascarar(validada.Identificador)} ({validada.Moneda}) contra su estado de cuenta.", ct);

    /// <summary>
    /// Abre con el responsable propuesto; si ya no existe como usuario (p. ej. datos migrados), con quien hizo el rechazo, para que el
    /// rechazo ya guardado no se quede sin incidencia.
    /// </summary>
    private static async Task<IncidenciaAbierta> AbrirAsync(IIncidenciasPort incidencias, AbrirIncidenciaSolicitud s, Guid? respaldo,
        CancellationToken ct)
    {
        try
        {
            return await incidencias.AbrirAsync(s, ct);
        }
        catch (BusinessRuleException e) when (e.Code == "INCIDENCIA_RESPONSABLE_INVALIDO" && respaldo is Guid r && r != s.ResponsableUsuarioId)
        {
            return await incidencias.AbrirAsync(s with { ResponsableUsuarioId = r }, ct);
        }
    }
}
