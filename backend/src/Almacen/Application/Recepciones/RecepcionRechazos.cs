using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Application.Incidencias;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// G-10: lo rechazado en el acto no entra al inventario ni cuenta como recibido (el pendiente sigue en la OC);
/// se conserva como <see cref="RecepcionRechazo"/> con su motivo y evidencia.
/// </summary>
internal static class RecepcionRechazos
{
    /// <summary>Reglas de la partida compartidas por las dos variantes: rechazo con motivo y evidencia obligatoria en dañado o rechazado.</summary>
    public static void ReglasDeCondicion(AbstractValidator<RegistrarRecepcionLineaInput> linea)
    {
        linea.RuleFor(l => l.CantidadRechazada).GreaterThanOrEqualTo(0).WithMessage("La cantidad rechazada no puede ser negativa.");
        linea.RuleFor(l => l.MotivoRechazo).NotEmpty().When(l => l.CantidadRechazada > 0).WithMessage("Indica el motivo del rechazo.");
        linea.RuleFor(l => l.MotivoRechazo!).MaximumLength(500).When(l => l.MotivoRechazo is not null);
        linea.RuleFor(l => l.EvidenciaBlobRef).NotEmpty().When(l => l.CantidadDanada > 0 || l.CantidadRechazada > 0)
            .WithMessage("Adjunta una foto o documento como evidencia del daño o del rechazo.");
        linea.RuleFor(l => l.EvidenciaBlobRef!).MaximumLength(500).When(l => l.EvidenciaBlobRef is not null);
    }

    /// <summary>Deja en la recepción solo las partidas con algo aceptado y devuelve las que traen rechazo.</summary>
    public static (IReadOnlyList<RegistrarRecepcionLineaInput> Aceptadas, IReadOnlyList<RegistrarRecepcionLineaInput> Rechazadas) Separar(
        IReadOnlyList<RegistrarRecepcionLineaInput> lineas)
    {
        var aceptadas = lineas.Where(l => l.Cantidad > 0).ToList();
        if (aceptadas.Count == 0)
            throw new BusinessRuleException("RECEPCION_TODO_RECHAZADO",
                "Todas las partidas se rechazaron en el acto: no hay nada que recibir. Avisa al proveedor y deja la orden pendiente.");
        return (aceptadas, lineas.Where(l => l.CantidadRechazada > 0).ToList());
    }

    public static void Registrar(AlmacenDbContext db, Guid empresaId, Guid recepcionId, OcLectura oc, IReadOnlyList<RegistrarRecepcionLineaInput> rechazadas)
    {
        foreach (var r in rechazadas)
        {
            var lineaOc = oc.Lineas.FirstOrDefault(l => l.LineaId == r.LineaOcId && l.ArticuloId == r.ArticuloId)
                ?? throw new BusinessRuleException("RECEPCION_RECHAZO_LINEA_INVALIDA", "La partida rechazada no corresponde a una línea de la orden de compra.");
            db.RecepcionRechazos.Add(new RecepcionRechazo(Guid.CreateVersion7(), empresaId, recepcionId, oc.Id, lineaOc.LineaId, r.ArticuloId,
                r.CantidadRechazada, string.IsNullOrWhiteSpace(r.UnidadCapturada) ? lineaOc.UnidadMedida : r.UnidadCapturada,
                r.MotivoRechazo ?? "", r.EvidenciaBlobRef ?? ""));
        }
    }

    /// <summary>
    /// U-4 (ALM-recepcion-09), después de guardar la recepción: primero resuelve las incidencias de las partidas que
    /// sí se recibieron ahora (el pendiente llegó), y luego abre una por cada partida rechazada en el acto, a cargo de
    /// quien recibió. La evidencia del rechazo queda en la recepción (ADR-0058: el archivo no viaja en la lectura).
    /// </summary>
    public static async Task IncidenciasAsync(IIncidenciasPort? incidencias, ILogger? logger, OcLectura oc,
        Guid recepcionId, string folio, Guid? sucursalId, Guid? responsable,
        IReadOnlyList<RegistrarRecepcionLineaInput> aceptadas, IReadOnlyList<RegistrarRecepcionLineaInput> rechazadas,
        CancellationToken ct)
    {
        if (incidencias is null)
            return;
        // N-11: la recepción de la orden ya quedó (con motivo si repetía la referencia): se cierra la incidencia de referencia duplicada.
        await IncidenciasAlmacen.SinRomperAsync(logger, "resolver la incidencia de referencia duplicada", () =>
            incidencias.ResolverPorDocumentoAsync(PasosIncidencia.AlmRecepcionDiferenciaOc, oc.Id, IncidenciasAlmacen.ClaveDuplicada,
                $"Se registró la recepción {folio} de la orden.", ct));
        foreach (var linea in aceptadas.Where(l => l.Cantidad > 0 && l.LineaOcId is not null).Select(l => l.LineaOcId!.Value).Distinct())
        {
            var resolucion = $"Se recibió la partida en la recepción {folio}.";
            await IncidenciasAlmacen.SinRomperAsync(logger, "resolver la incidencia de diferencia de recepción", async () =>
            {
                await incidencias.ResolverPorDocumentoAsync(PasosIncidencia.AlmRecepcionDiferenciaOc, oc.Id, IncidenciasAlmacen.ClaveRechazo(linea), resolucion, ct);
                await incidencias.ResolverPorDocumentoAsync(PasosIncidencia.AlmRecepcionDiferenciaOc, oc.Id, IncidenciasAlmacen.ClaveExcede(linea), resolucion, ct);
            });
        }
        if (responsable is not Guid quien || quien == Guid.Empty)
            return;
        foreach (var r in rechazadas.Where(r => r.LineaOcId is not null))
        {
            var unidad = string.IsNullOrWhiteSpace(r.UnidadCapturada)
                ? oc.Lineas.FirstOrDefault(l => l.LineaId == r.LineaOcId)?.UnidadMedida ?? string.Empty
                : r.UnidadCapturada;
            var cantidad = r.CantidadRechazada.ToString("0.####", CultureInfo.InvariantCulture);
            var motivo = Recortar($"Se rechazaron {cantidad} {unidad} al recibir: {r.MotivoRechazo}".Trim(), 1000);
            var detalle = $"La evidencia del rechazo quedó en la recepción {folio}. Lo rechazado no entró al inventario y sigue pendiente en la orden {oc.Folio}: "
                + "se resuelve al recibir esa partida, o con una nota si Compras ajusta la orden.";
            await IncidenciasAlmacen.SinRomperAsync(logger, "abrir la incidencia del rechazo en la recepción", () =>
                incidencias.AbrirAsync(IncidenciasAlmacen.RechazoRecepcion(oc.Id, oc.Folio, recepcionId, folio, r.LineaOcId!.Value,
                    sucursalId, quien, motivo, detalle), ct));
        }
    }

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..max];
}
