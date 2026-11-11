using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed partial class Expediente
{
    public const string FinancieroPendiente = "Pendiente · se habilita con CE-03";
    private readonly List<HitoExpediente> _hitos = [];
    public IReadOnlyCollection<HitoExpediente> Hitos => _hitos.AsReadOnly();
    private readonly List<AvisoHitoVencido> _avisos = [];
    public IReadOnlyCollection<AvisoHitoVencido> Avisos => _avisos.AsReadOnly();
    private readonly List<CorreccionHitoExpediente> _correccionesHitos = [];
    public IReadOnlyCollection<CorreccionHitoExpediente> CorreccionesHitos => _correccionesHitos.AsReadOnly();
    public const string MotivoCierreAvisoPorCorreccion = "Atraso eliminado al corregir el hito.";
    private static bool EsOmitible(string clave) => clave is not ("solicitado" or "documentacion-completa" or "despacho-aduanal" or "liberado" or "embarcado" or "entregado" or "cerrado");

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
        if (clave == "cerrado") throw new BusinessRuleException("CE_CIERRE_GERENCIA_REQUERIDO", "Complete los tres cierres y solicite la aprobación de Gerencia desde el resumen del expediente.");
        if (clave is "documentacion-completa" or "entregado" or "cerrado") ExigirSinIncidencias();
        if (clave == "cerrado" && MotivosFinancieros().Count > 0)
            throw new BusinessRuleException("CE_CIERRE_FINANCIERO_PENDIENTE", string.Join("; ", MotivosFinancieros()));
        if (clave == "embarcado" && Tipo == TipoExpediente.Exportacion &&
            (!_contenedores.Any(c => c.Estado == EstadoContenedor.Activo) ||
                _contenedores.Any(c => c.Estado == EstadoContenedor.Activo && c.FechaEmbarqueReal is null)))
            throw new BusinessRuleException("CE_EMBARQUE_PENDIENTE", "Registre el despacho físico de los contenedores antes de completar el hito.");
        if (clave == "embarcado" && Tipo == TipoExpediente.Importacion)
        {
            var activos = _contenedores.Where(c => c.Estado == EstadoContenedor.Activo).ToArray();
            if (activos.Length == 0 || activos.Any(c => c.FechaEmbarqueReal is null))
                throw new BusinessRuleException("CE_EMBARQUE_PENDIENTE", "Registre el embarque real de los contenedores y sus partidas de la OC antes de completar el hito.");
            foreach (var c in activos) ExigirTransporte(c);
        }
        var hito = Hito(clave);
        ExigirAnteriores(hito);
        var anteriorReal = _hitos.Where(h => h.Orden < hito.Orden && h.FechaReal != null).Max(h => h.FechaReal);
        if (fecha > hoy || fecha < anteriorReal)
            throw new BusinessRuleException("CE_FECHA_REAL_INVALIDA", "La fecha real debe estar entre el hito anterior y hoy.");
        if (evidencia is Guid a && !adjuntosVigentes.Contains(a))
            throw new BusinessRuleException("CE_ADJUNTO_INVALIDO", "La evidencia debe ser un adjunto vigente del expediente.");
        if (clave is "despacho-aduanal" or "liberado" && evidencia is null)
            throw new BusinessRuleException("CE_EVIDENCIA_HITO_REQUERIDA", clave == "despacho-aduanal" ? "Adjunte el pedimento pagado para registrar el despacho aduanal." : "Adjunte el documento de liberación para completar el hito.");
        {
            var faltantes = Faltantes(requisitos.Where(r => r.HitoClave == clave || Tipo == TipoExpediente.Importacion && r.HitoClave is not null && _hitos.Any(h => h.Clave == r.HitoClave && h.Orden <= hito.Orden)).ToArray(), adjuntosVigentes);
            if (faltantes.Count > 0) throw new DocumentosFaltantesException(faltantes);
        }
        if (clave == "despacho-aduanal" && !_participantes.Any(p => p.Tipo == TipoParticipante.AgenteAduanal))
            throw new BusinessRuleException("CE_AGENTE_ADUANAL_REQUERIDO", "Agregue un agente aduanal antes del despacho.");
        hito.Completar(fecha, responsable, evidencia, motivo);
        if (clave == "cerrado") Cerrar();
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
        if (!EsOmitible(clave))
            throw new BusinessRuleException("CE_HITO_NO_OMITIBLE", "Este hito es obligatorio y no se puede omitir.");
        var hito = Hito(clave);
        ExigirAnteriores(hito);
        hito.Omitir(motivo, responsable);
        RevisionContenido++;
    }

    /// <summary>
    /// M-373: corrige la fecha real o la omisión de un hito ya resuelto, con motivo obligatorio. Respeta el orden
    /// de los hitos (entre el anterior y el siguiente con fecha real, nunca futura) y recalcula el atraso: si la
    /// corrección lo elimina, el aviso de atraso se cierra solo; si lo crea, se abre con la fecha de hoy.
    /// </summary>
    public CorreccionHitoExpediente CorregirHito(string clave, DateOnly? fechaReal, bool omitir, string? motivo, Guid actor, DateTimeOffset ahora, DateOnly hoy,
        IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> adjuntosVigentes)
    {
        ExigirAbierto();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CE_MOTIVO_CORRECCION_REQUERIDO", "Capture el motivo de la corrección del hito.");
        var razon = MotivoValido(motivo);
        if (actor == Guid.Empty)
            throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Se requiere el usuario que corrige el hito.");
        if (clave == "cerrado")
            throw new BusinessRuleException("CE_HITO_NO_CORREGIBLE", "El cierre del expediente no se corrige desde los hitos.");
        var hito = Hito(clave);
        if (!hito.Resuelto)
            throw new BusinessRuleException("CE_HITO_PENDIENTE", "El hito no tiene fecha real ni omisión; regístrelo en lugar de corregirlo.");
        if (omitir)
        {
            if (fechaReal is not null)
                throw new BusinessRuleException("CE_CORRECCION_INVALIDA", "Indique una fecha real o la omisión, no ambas.");
            if (!EsOmitible(clave))
                throw new BusinessRuleException("CE_HITO_NO_OMITIBLE", "Este hito es obligatorio y no se puede omitir.");
            if (hito.Omitido)
                throw new BusinessRuleException("CE_CORRECCION_SIN_CAMBIO", "El hito ya está omitido.");
        }
        else
        {
            var fecha = fechaReal ?? throw new BusinessRuleException("CE_FECHA_REAL_REQUERIDA", "Capture la fecha real corregida.");
            if (!hito.Omitido && hito.FechaReal == fecha)
                throw new BusinessRuleException("CE_CORRECCION_SIN_CAMBIO", "La fecha real corregida es igual a la vigente.");
            var anterior = _hitos.Where(h => h.Orden < hito.Orden && h.FechaReal != null).Max(h => h.FechaReal);
            var siguiente = _hitos.Where(h => h.Orden > hito.Orden && h.FechaReal != null).Min(h => h.FechaReal);
            if (fecha > hoy || fecha < anterior || fecha > siguiente)
                throw new BusinessRuleException("CE_FECHA_REAL_INVALIDA", "La fecha real corregida debe quedar entre el hito anterior y el siguiente, y no ser futura.");
            if (hito.Omitido)
            {
                // Pasar de omitido a fecha real exige lo mismo que registrarlo: los documentos ligados a ese hito.
                var faltantes = Faltantes(requisitos.Where(r => r.HitoClave == clave).ToArray(), adjuntosVigentes);
                if (faltantes.Count > 0) throw new DocumentosFaltantesException(faltantes);
            }
        }
        var (fechaAnterior, omitidoAnterior, motivoAnterior) = (hito.FechaReal, hito.Omitido, hito.Motivo);
        hito.Corregir(omitir ? null : fechaReal, omitir, razon);
        var correccion = new CorreccionHitoExpediente(hito, fechaAnterior, omitidoAnterior, motivoAnterior, razon, actor, ahora);
        _correccionesHitos.Add(correccion);
        RecalcularAtraso(hito, hoy);
        RevisionContenido++;
        return correccion;
    }

    private void RecalcularAtraso(HitoExpediente hito, DateOnly hoy)
    {
        if (!hito.TerminoConAtraso)
        {
            foreach (var aviso in _avisos.Where(a => a.HitoId == hito.Id && a.Vigente))
                aviso.Cerrar(hoy, MotivoCierreAvisoPorCorreccion);
            return;
        }
        var actual = _avisos.SingleOrDefault(a => a.HitoId == hito.Id && a.FechaCompromiso == hito.FechaCompromiso);
        if (actual is null) _avisos.Add(new AvisoHitoVencido(hito, hoy));
        else actual.Reabrir(hoy);
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
        if (_incidencias.Any(i => i.Pendiente)) return "Con incidencias pendientes";
        if (!_hitos.Any(h => h.Clave == "entregado" && h.FechaReal is not null)) return "Pendiente";
        if (Tipo == TipoExpediente.Importacion) return "Completo";
        var pod = requisitos.SingleOrDefault(r => r.Codigo == "EXP_POD");
        return pod is not null && _documentos.Any(d => d.TipoDocumentoId == pod.Id && d.Estado == EstadoDocumento.Vigente && !d.NoAplica && d.AdjuntoId is Guid a && adjuntosVigentes.Contains(a)) ? "Completo" : "POD pendiente";
    }

    public void Cerrar()
    {
        ExigirAbierto();
        ExigirSinIncidencias();
        var motivos = MotivosFinancieros();
        if (motivos.Count > 0) throw new BusinessRuleException("CE_CIERRE_FINANCIERO_PENDIENTE", string.Join("; ", motivos));
        FinancieroCerrado = true;
        RevisionContenido++;
    }
}
