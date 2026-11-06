using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.Application.Incidencias;

/// <summary>Fila de bandeja («Mis incidencias», consulta por documento). Estado en texto: Abierta|EnAtencion|Resuelta|Cancelada.</summary>
public sealed record IncidenciaFila(
    Guid Id,
    long Numero,
    string Modulo,
    string TipoDocumento,
    Guid DocumentoId,
    string DocumentoEtiqueta,
    Guid? SucursalId,
    string Paso,
    string PasoNombre,
    string? CodigoMotivo,
    string Motivo,
    string Estado,
    Guid ResponsableUsuarioId,
    string ResponsableNombre,
    string AbiertaPorNombre,
    bool Automatica,
    DateTimeOffset AbiertaEn,
    DateOnly? FechaCompromiso,
    string? RutaReanudacion,
    int Version)
{
    public static IncidenciaFila De(Incidencia i) => new(
        i.Id, i.Numero, i.Modulo, i.TipoDocumento, i.DocumentoId, i.DocumentoEtiqueta, i.SucursalId, i.Paso, PasosIncidencia.Nombre(i.Paso),
        i.CodigoMotivo, i.Motivo, i.Estado.ToString(), i.ResponsableUsuarioId, i.ResponsableNombre, i.AbiertaPorNombre, i.Automatica,
        i.AbiertaEn, i.FechaCompromiso, i.RutaReanudacion, i.Version);
}

public sealed record IncidenciaMovimientoDto(
    DateTimeOffset En, string Accion, string? EstadoAnterior, string EstadoNuevo, string UsuarioNombre, string? Nota);

/// <summary>Detalle con historial y las acciones que el usuario actual puede hacer.</summary>
public sealed record IncidenciaDetalle(
    Guid Id,
    long Numero,
    string Modulo,
    string TipoDocumento,
    Guid DocumentoId,
    string DocumentoEtiqueta,
    Guid? SucursalId,
    string Paso,
    string PasoNombre,
    string? CodigoMotivo,
    string Motivo,
    string? Detalle,
    string? DetalleClave,
    string Estado,
    Guid ResponsableUsuarioId,
    string ResponsableNombre,
    Guid? AbiertaPorId,
    string AbiertaPorNombre,
    bool Automatica,
    DateTimeOffset AbiertaEn,
    DateOnly? FechaCompromiso,
    string? PasoReanudacion,
    string? PasoReanudacionNombre,
    string? RutaReanudacion,
    string? ResueltaPorNombre,
    DateTimeOffset? ResueltaEn,
    string? Resolucion,
    bool PuedeAtender,
    bool PuedeResolver,
    bool PuedeCancelar,
    bool PuedeReasignar,
    IReadOnlyList<IncidenciaMovimientoDto> Historial,
    int Version)
{
    public static IncidenciaDetalle De(Incidencia i, Acciones a) => new(
        i.Id, i.Numero, i.Modulo, i.TipoDocumento, i.DocumentoId, i.DocumentoEtiqueta, i.SucursalId, i.Paso, PasosIncidencia.Nombre(i.Paso),
        i.CodigoMotivo, i.Motivo, i.Detalle, i.DetalleClave.Length == 0 ? null : i.DetalleClave, i.Estado.ToString(),
        i.ResponsableUsuarioId, i.ResponsableNombre, i.AbiertaPorId, i.AbiertaPorNombre, i.Automatica, i.AbiertaEn, i.FechaCompromiso,
        i.PasoReanudacion, i.PasoReanudacion is null ? null : PasosIncidencia.Nombre(i.PasoReanudacion), i.RutaReanudacion,
        i.ResueltaPorNombre, i.ResueltaEn, i.Resolucion, a.PuedeAtender, a.PuedeResolver, a.PuedeCancelar, a.PuedeReasignar,
        i.Historial.OrderBy(m => m.Orden)
            .Select(m => new IncidenciaMovimientoDto(m.En, m.Accion, m.EstadoAnterior?.ToString(), m.EstadoNuevo.ToString(), m.UsuarioNombre, m.Nota))
            .ToList(),
        i.Version);
}

public sealed record IncidenciasPagina(IReadOnlyList<IncidenciaFila> Items, int Total, int Offset, int Limit);

public sealed record ResumenMisIncidencias(int Abiertas, int EnAtencion);

/// <summary>Resultado de abrir por HTTP: el detalle y si ya existía una pendiente igual.</summary>
public sealed record IncidenciaAbiertaDetalle(IncidenciaDetalle Detalle, bool YaExistia);
