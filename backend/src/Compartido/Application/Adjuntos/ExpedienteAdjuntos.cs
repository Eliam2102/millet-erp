using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Estado de un tipo de documento dentro del expediente.</summary>
public enum EstadoExpedienteDocumento
{
    Faltante = 0,
    Vigente = 1,
    PorVencer = 2,
    Vencido = 3,
}

/// <param name="Actual">El adjunto más reciente no dado de baja de ese tipo (null si falta).</param>
public sealed record ExpedienteDocumentoResponse(
    Guid TipoDocumentoId,
    string Codigo,
    string Nombre,
    bool Obligatorio,
    int? VigenciaMeses,
    EstadoExpedienteDocumento Estado,
    AdjuntoResponse? Actual);

/// <summary>
/// Expediente documental de una entidad. <c>Completo</c> = todos los tipos obligatorios aplicables
/// vigentes o por vencer. Faltantes/Vencidos/PorVencer listan códigos de tipos obligatorios.
/// </summary>
public sealed record ExpedienteResponse(
    string TipoEntidad,
    Guid EntidadId,
    bool Completo,
    IReadOnlyList<ExpedienteDocumentoResponse> Documentos,
    IReadOnlyList<string> Faltantes,
    IReadOnlyList<string> Vencidos,
    IReadOnlyList<string> PorVencer);

/// <summary>Cálculo del expediente (compartido por el handler HTTP y el puerto de lectura para CxP).</summary>
public static class ExpedienteAdjuntos
{
    public static async Task<ExpedienteResponse> CalcularAsync(
        CompartidoDbContext db,
        string tipoEntidad,
        Guid entidadId,
        bool? esPersonaMoral,
        DateOnly hoy,
        CancellationToken cancellationToken)
    {
        var tipos = await db.AdjuntoTiposDocumento.AsNoTracking()
            .Where(t => t.TipoEntidad == tipoEntidad && t.Activo)
            .OrderBy(t => t.Orden)
            .ToListAsync(cancellationToken);
        var adjuntos = await db.Adjuntos.AsNoTracking()
            .Where(a => a.TipoEntidad == tipoEntidad && a.EntidadId == entidadId && a.BajaEn == null)
            .ToListAsync(cancellationToken);

        var documentos = new List<ExpedienteDocumentoResponse>();
        foreach (var tipo in tipos)
        {
            // Los tipos "solo persona moral" no aplican a una persona física.
            if (tipo.SoloPersonaMoral && esPersonaMoral == false) continue;

            var actual = adjuntos.Where(a => a.TipoDocumentoId == tipo.Id)
                .OrderByDescending(a => a.SubidoEn).FirstOrDefault();
            var estado = actual is null
                ? EstadoExpedienteDocumento.Faltante
                : actual.EstadoEn(hoy) switch
                {
                    EstadoAdjunto.Vencido => EstadoExpedienteDocumento.Vencido,
                    EstadoAdjunto.PorVencer => EstadoExpedienteDocumento.PorVencer,
                    _ => EstadoExpedienteDocumento.Vigente, // Vigente o SinVigencia
                };
            documentos.Add(new ExpedienteDocumentoResponse(
                tipo.Id, tipo.Codigo, tipo.Nombre, tipo.Obligatorio, tipo.VigenciaMeses, estado,
                actual is null ? null : AdjuntoResponse.De(actual, tipo, hoy)));
        }

        var obligatorios = documentos.Where(d => d.Obligatorio).ToList();
        string[] Codigos(EstadoExpedienteDocumento e) =>
            obligatorios.Where(d => d.Estado == e).Select(d => d.Codigo).ToArray();

        var faltantes = Codigos(EstadoExpedienteDocumento.Faltante);
        var vencidos = Codigos(EstadoExpedienteDocumento.Vencido);
        return new ExpedienteResponse(
            tipoEntidad, entidadId,
            Completo: faltantes.Length == 0 && vencidos.Length == 0,
            documentos, faltantes, vencidos, Codigos(EstadoExpedienteDocumento.PorVencer));
    }
}
