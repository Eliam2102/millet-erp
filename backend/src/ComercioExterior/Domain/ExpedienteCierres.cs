using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed record CierreDimension(string Dimension, Guid? UsuarioId, DateTimeOffset? Fecha, IReadOnlyList<string> Faltantes, string? UsuarioNombre = null);
public sealed partial class Expediente
{
    public Guid? CierreFisicoPorId { get; private set; }
    public DateTimeOffset? CierreFisicoEn { get; private set; }
    public Guid? CierreDocumentalPorId { get; private set; }
    public DateTimeOffset? CierreDocumentalEn { get; private set; }
    public Guid? CierreFinancieroPorId { get; private set; }
    public DateTimeOffset? CierreFinancieroEn { get; private set; }
    public Guid? CierreAprobadoPorId { get; private set; }
    public DateTimeOffset? CierreAprobadoEn { get; private set; }
    public IReadOnlyList<CierreDimension> Cierres(IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> vigentes, IReadOnlyDictionary<Guid, decimal> recibidas)
    {
        List<string> fisico = [];
        if (Tipo == TipoExpediente.Importacion)
        {
            var embarcadas = _partidasImportacion.Where(p => Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo && Contenedor(p.ContenedorId).FechaEmbarqueReal != null).GroupBy(p => p.OcLineaId).ToArray();
            if (embarcadas.Length == 0) fisico.Add("Registre las partidas embarcadas de la OC.");
            foreach (var linea in embarcadas)
            {
                var cantidad = linea.Sum(p => p.Cantidad); var recibido = linea.Any(p => p.CantidadRecibida is not null) ? linea.Sum(p => p.CantidadRecibida ?? 0) : recibidas.GetValueOrDefault(linea.Key);
                var ids = linea.Select(p => p.Id).ToHashSet();
                if (cantidad != recibido && !_incidencias.Any(i => i.Estado == EstadoIncidencia.Resuelta && i.Tipo is (TipoIncidencia.MercanciaFaltante or TipoIncidencia.MercanciaSobrante) && i.PartidaId is Guid p && ids.Contains(p)))
                    fisico.Add($"Partida {linea.First().Posicion}: embarcado {cantidad}, recibido {recibido}; concilie la diferencia o resuelva su incidencia.");
            }
        }
        else if (EstadoFisico(requisitos, vigentes) != "Completo") fisico.Add("Registre la entrega y el POD vigente.");
        if (_incidencias.Any(i => i.Pendiente)) fisico.Add("Resuelva las incidencias pendientes.");
        var documental = Faltantes(requisitos, vigentes).Select(r => "Adjunte " + r.Nombre + ".").ToList();
        foreach (var clave in new[] { "despacho-aduanal", "liberado" })
            if (!_hitos.Any(h => h.Clave == clave && h.FechaReal != null && h.EvidenciaAdjuntoId is Guid a && vigentes.Contains(a)))
                documental.Add(clave == "liberado" ? "Adjunte la evidencia de liberación y complete el hito." : "Adjunte el pedimento pagado y complete el despacho aduanal.");
        var financiero = MotivosFinancieros().ToList();
        if (_incidencias.Any(i => i.Pendiente)) financiero.Add("Resuelva las incidencias pendientes.");
        return [new("fisico", CierreFisicoPorId, CierreFisicoEn, fisico), new("documental", CierreDocumentalPorId, CierreDocumentalEn, documental), new("financiero", CierreFinancieroPorId, FinancieroCerrado ? CierreFinancieroEn : null, financiero)];
    }
    public void CerrarDimension(string dimension, Guid actor, DateTimeOffset fecha, IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> vigentes, IReadOnlyDictionary<Guid, decimal> recibidas)
    {
        ExigirAbierto();
        if (actor == Guid.Empty) throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Identifique a quien registra el cierre.");
        var cierre = Cierres(requisitos, vigentes, recibidas).SingleOrDefault(c => c.Dimension == dimension) ?? throw new BusinessRuleException("CE_CIERRE_INVALIDO", "Seleccione cierre físico, documental o financiero.");
        if (cierre.Faltantes.Count > 0) throw new BusinessRuleException("CE_CIERRE_PENDIENTE", string.Join(" ", cierre.Faltantes));
        switch (dimension)
        {
            case "fisico": CierreFisicoPorId = actor; CierreFisicoEn = fecha; break;
            case "documental": CierreDocumentalPorId = actor; CierreDocumentalEn = fecha; break;
            case "financiero": Cerrar(); CierreFinancieroPorId = actor; CierreFinancieroEn = fecha; break;
        }
        RevisionContenido++;
    }
    public void AprobarCierre(Guid actor, DateTimeOffset fecha, IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> vigentes, IReadOnlyDictionary<Guid, decimal> recibidas)
    {
        ExigirAbierto();
        if (actor == Guid.Empty) throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Identifique a la persona de Gerencia que aprueba el cierre.");
        var faltantes = Cierres(requisitos, vigentes, recibidas).Where(c => c.Fecha is null || c.Faltantes.Count > 0).ToArray();
        if (faltantes.Length > 0) throw new BusinessRuleException("CE_TRES_CIERRES_PENDIENTES", "Complete los tres cierres antes de la aprobación de Gerencia: " + string.Join("; ", faltantes.Select(c => c.Dimension + ": " + (c.Faltantes.Count > 0 ? string.Join(" ", c.Faltantes) : "pendiente de registrar"))));
        CierreAprobadoPorId = actor; CierreAprobadoEn = fecha; Estado = EstadoExpediente.Cerrado; RevisionContenido++;
    }
}
