using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed partial class Expediente
{
    public const string FinancieroPendiente = "Pendiente · se habilita con CE-03";
    private readonly List<HitoExpediente> _hitos = [];
    public IReadOnlyCollection<HitoExpediente> Hitos => _hitos.AsReadOnly();
    private readonly List<AvisoHitoVencido> _avisos = [];
    public IReadOnlyCollection<AvisoHitoVencido> Avisos => _avisos.AsReadOnly();

    public void InicializarHitos(DateOnly fechaSolicitud)
    {
        foreach (var (item, orden) in HitoExpediente.Catalogo.Select((h, i) => (h, i + 1)))
        {
            if (_hitos.Any(h => h.Clave == item.Clave)) continue;
            var hito = new HitoExpediente(EmpresaId, Id, item.Clave, orden, ResponsableUsuarioId);
            if (orden == 1) hito.Completar(fechaSolicitud, ResponsableUsuarioId, null, null);
            _hitos.Add(hito);
            RevisionContenido++;
        }
    }

    private HitoExpediente Hito(string clave) => _hitos.SingleOrDefault(h => h.Clave == clave)
        ?? throw new EntityNotFoundException("CE_HITO_NO_ENCONTRADO", "No existe el hito del expediente.");

    public void CambiarCompromiso(string clave, DateOnly fecha, Guid responsable, string? motivo)
    {
        ExigirAbierto();
        Hito(clave).Comprometer(fecha, responsable, motivo);
        RevisionContenido++;
    }

    public void RegistrarHito(string clave, DateOnly fecha, Guid responsable, Guid? evidencia, string? motivo,
        IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> adjuntosVigentes, DateOnly hoy)
    {
        ExigirAbierto();
        if (clave == "cerrado") Cerrar();
        var hito = Hito(clave);
        ExigirAnteriores(hito);
        var anteriorReal = _hitos.Where(h => h.Orden < hito.Orden && h.FechaReal != null).Max(h => h.FechaReal);
        if (fecha > hoy || fecha < anteriorReal)
            throw new BusinessRuleException("CE_FECHA_REAL_INVALIDA", "La fecha real debe estar entre el hito anterior y hoy.");
        if (evidencia is Guid a && !adjuntosVigentes.Contains(a))
            throw new BusinessRuleException("CE_ADJUNTO_INVALIDO", "La evidencia debe ser un adjunto vigente del expediente.");
        if (clave == "documentacion-completa")
        {
            var faltantes = Faltantes(requisitos, adjuntosVigentes);
            if (faltantes.Count > 0) throw new DocumentosFaltantesException(faltantes);
        }
        if (clave == "despacho-aduanal" && !_participantes.Any(p => p.Tipo == TipoParticipante.AgenteAduanal))
            throw new BusinessRuleException("CE_AGENTE_ADUANAL_REQUERIDO", "Agregue un agente aduanal antes del despacho.");
        hito.Completar(fecha, responsable, evidencia, motivo);
        RevisionContenido++;
    }

    private void ExigirAnteriores(HitoExpediente hito)
    {
        if (_hitos.Any(h => h.Orden < hito.Orden && h.FechaReal is null && !h.Omitido))
            throw new BusinessRuleException("CE_HITO_ANTERIOR_PENDIENTE", "Registre la fecha real u omita con motivo los hitos anteriores.");
    }

    public void OmitirHito(string clave, string motivo, Guid responsable)
    {
        ExigirAbierto();
        if (clave is "solicitado" or "documentacion-completa" or "despacho-aduanal" or "entregado" or "cerrado")
            throw new BusinessRuleException("CE_HITO_NO_OMITIBLE", "Este hito es obligatorio y no se puede omitir.");
        var hito = Hito(clave);
        ExigirAnteriores(hito);
        hito.Omitir(motivo, responsable);
        RevisionContenido++;
    }

    public int GenerarAvisos(DateOnly hoy)
    {
        if (Estado != EstadoExpediente.Abierto) return 0;
        var cantidad = 0;
        foreach (var hito in _hitos.Where(h => h.EstaAtrasado(hoy)))
        {
            if (_avisos.Any(a => a.HitoId == hito.Id && a.FechaCompromiso == hito.FechaCompromiso)) continue;
            _avisos.Add(new AvisoHitoVencido(hito, hoy));
            cantidad++;
        }
        return cantidad;
    }

    public string EstadoFisico(IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> adjuntosVigentes)
    {
        if (!_hitos.Any(h => h.Clave == "entregado" && h.FechaReal is not null)) return "Pendiente";
        if (Tipo == TipoExpediente.Importacion) return "Completo";
        var pod = requisitos.SingleOrDefault(r => r.Codigo == "EXP_POD");
        return pod is not null && _documentos.Any(d => d.TipoDocumentoId == pod.Id && d.Estado == EstadoDocumento.Vigente && !d.NoAplica && d.AdjuntoId is Guid a && adjuntosVigentes.Contains(a)) ? "Completo" : "POD pendiente";
    }

    public void Cerrar()
    {
        ExigirAbierto();
        throw new BusinessRuleException("CE_CIERRE_FINANCIERO_PENDIENTE", "El cierre financiero está pendiente; se habilita con CE-03.");
    }
}
