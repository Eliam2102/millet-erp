using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.Oc.EnviarAAutorizacion;

/// <summary>
/// Handler de <see cref="EnviarAAutorizacionOcCommand"/>. Aplica
/// validaciones pre-auth del §7.1:
/// <list type="number">
///   <item>C10 — proveedor activo en <c>compartido.proveedores</c>.</item>
///   <item>C11 — cotización adjunta (tipo <c>cotizacion</c>) **o**
///         excepción <see cref="OrdenCompra.CotizacionExcepcionada"/> +
///         adjunto tipo <c>correo_autorizacion</c>.</item>
///   <item>Si <see cref="OrdenCompra.EsImportacion"/> = true: adjunto
///         tipo <c>ficha_tecnica</c>.</item>
///   <item>Si <see cref="OrdenCompra.SinRequisicionPrevia"/> = true:
///         motivo no nulo + adjunto tipo <c>correo_autorizacion</c>.</item>
/// </list>
///
/// Si todas pasan, invoca <see cref="OrdenCompra.EnviarAAutorizacion"/>
/// (que valida estado + ≥1 línea), persiste y publica el evento via
/// MediatR INotification.
///
/// Las claves del catálogo <c>tipos_documento_oc</c> (cotizacion,
/// ficha_tecnica, correo_autorizacion) se resuelven a IDs vía un
/// lookup al inicio del handler. Si alguna clave no existe en BD, falla
/// con <c>TIPO_DOCUMENTO_NO_ENCONTRADO</c>.
/// </summary>
public sealed class EnviarAAutorizacionOcHandler : IRequestHandler<EnviarAAutorizacionOcCommand>
{
    private static readonly string[] ClavesNecesarias = ["cotizacion", "ficha_tecnica", "correo_autorizacion"];

    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public EnviarAAutorizacionOcHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
        _compartido = compartido;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task Handle(EnviarAAutorizacionOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .Include(o => o.Adjuntos)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        // Resolver IDs del catálogo de tipos de documento por clave.
        var tipos = await _db.TiposDocumentoOc
            .AsNoTracking()
            .Where(t => ClavesNecesarias.Contains(t.Clave))
            .ToDictionaryAsync(t => t.Clave, t => t.Id, cancellationToken);

        if (!tipos.TryGetValue("cotizacion", out var idCotizacion)
            || !tipos.TryGetValue("ficha_tecnica", out var idFichaTecnica)
            || !tipos.TryGetValue("correo_autorizacion", out var idCorreoAutorizacion))
        {
            throw new EntityNotFoundException(
                "TIPO_DOCUMENTO_SEED_INCOMPLETO",
                "Faltan tipos de documento en el catálogo (cotizacion, ficha_tecnica, correo_autorizacion).");
        }

        // C10: proveedor activo cross-table.
        var proveedor = await _compartido.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == oc.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"Proveedor '{oc.ProveedorId}' no encontrado.");
        if (proveedor.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_INACTIVO",
                $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus} y no puede enviarse a autorización.");
        }

        // C11: cotización adjunta o excepción + correo.
        var tieneCotizacion = oc.Adjuntos.Any(a => a.TipoDocumentoId == idCotizacion);
        var tieneCorreoAutorizacion = oc.Adjuntos.Any(a => a.TipoDocumentoId == idCorreoAutorizacion);
        if (!tieneCotizacion)
        {
            if (!oc.CotizacionExcepcionada)
            {
                throw new BusinessRuleException(
                    "OC_COTIZACION_REQUERIDA",
                    "Antes de enviar a autorización, la OC requiere un adjunto tipo 'cotizacion' o activar CotizacionExcepcionada con correo de autorización.");
            }
            if (!tieneCorreoAutorizacion)
            {
                throw new BusinessRuleException(
                    "OC_EXCEPCION_COTIZACION_SIN_CORREO",
                    "CotizacionExcepcionada exige un adjunto tipo 'correo_autorizacion'.");
            }
        }

        // Ficha técnica si importación.
        if (oc.EsImportacion)
        {
            var tieneFichaTecnica = oc.Adjuntos.Any(a => a.TipoDocumentoId == idFichaTecnica);
            if (!tieneFichaTecnica)
            {
                throw new BusinessRuleException(
                    "OC_FICHA_TECNICA_REQUERIDA",
                    "Una OC de importación requiere adjunto tipo 'ficha_tecnica' antes de enviar a autorización.");
            }
        }

        // Sin RQ previa: motivo + correo.
        if (oc.SinRequisicionPrevia)
        {
            if (string.IsNullOrWhiteSpace(oc.MotivoSinRequisicion))
            {
                throw new BusinessRuleException(
                    "OC_SIN_RQ_MOTIVO_REQUERIDO",
                    "OC sin requisición previa requiere capturar MotivoSinRequisicion.");
            }
            if (!tieneCorreoAutorizacion)
            {
                throw new BusinessRuleException(
                    "OC_SIN_RQ_CORREO_REQUERIDO",
                    "OC sin requisición previa requiere adjunto tipo 'correo_autorizacion'.");
            }
        }

        // Transición + emisión de evento.
        var evento = oc.EnviarAAutorizacion(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _publisher.Publish(evento, cancellationToken);
    }
}
