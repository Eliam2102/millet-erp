using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public enum TipoExpediente
{
    Importacion = 1,
    Exportacion = 2
}

public enum EstadoExpediente
{
    Abierto = 1,
    Cancelado = 2
}

public enum EstadoDocumento
{
    Vigente = 1,
    Reemplazada = 2
}

public enum PapelParticipante
{
    Captura = 1,
    Revisa = 2,
    Autoriza = 3
}

public sealed record RequisitoDocumento(Guid Id, string Codigo, string Nombre, bool Obligatorio, bool Condicional);
public sealed class DocumentosFaltantesException(IReadOnlyList<RequisitoDocumento> faltantes) : BusinessRuleException("CE_DOCUMENTOS_FALTANTES", "Faltan documentos obligatorios: " + string.Join(", ", faltantes.Select(r => r.Nombre)))
{
    public IReadOnlyList<RequisitoDocumento> Faltantes { get; } = faltantes;
}

public sealed class Expediente : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string Folio { get; private set; } = "";
    public TipoExpediente Tipo { get; private set; }
    public Guid SucursalId { get; private set; }
    public string Referencia { get; private set; } = "";
    public Guid? OcId { get; private set; }
    public Guid IncotermId { get; private set; }
    public string Moneda { get; private set; } = "";
    public decimal? Tc { get; private set; }
    public DateOnly? FechaTc { get; private set; }
    public string? FuenteTc { get; private set; }
    public Guid ResponsableUsuarioId { get; private set; }
    public EstadoExpediente Estado { get; private set; } = EstadoExpediente.Abierto;
    public bool RevisionPendiente { get; private set; }
    public Guid? RevisadoPorId { get; private set; }
    public string? MotivoCancelacion { get; private set; }
    // Fuerza el UPDATE de la raíz también cuando solo cambian hijos (ETag del agregado).
    public int RevisionContenido { get; private set; }

    private readonly List<ParticipanteExpediente> _participantes = [];
    public IReadOnlyCollection<ParticipanteExpediente> Participantes => _participantes.AsReadOnly();

    private readonly List<DocumentoExpediente> _documentos = [];
    public IReadOnlyCollection<DocumentoExpediente> Documentos => _documentos.AsReadOnly();

    private Expediente()
    {
    }

    public Expediente(Guid id, Guid empresaId, string folio, TipoExpediente tipo, Guid sucursalId, string referencia, Guid? ocId, Guid incotermId, string moneda, Guid responsable, IReadOnlyList<string> clientes) : base(id)
    {
        if (!Enum.IsDefined(tipo) || empresaId == Guid.Empty || sucursalId == Guid.Empty)
            throw new BusinessRuleException("CE_DATOS_INVALIDOS", "Tipo, empresa y sucursal son obligatorios.");
        if (tipo == TipoExpediente.Importacion && (ocId is null || ocId == Guid.Empty))
            throw new BusinessRuleException("CE_OC_SIN_IMPORTACION", "La importación requiere una OC con datos de importación.");
        if (tipo == TipoExpediente.Exportacion && ocId is not null)
            throw new BusinessRuleException("CE_ORIGEN_INVALIDO", "La exportación nace de una solicitud de contenedor.");
        if (tipo == TipoExpediente.Exportacion && (clientes.Count == 0 || clientes.Any(string.IsNullOrWhiteSpace)))
            throw new BusinessRuleException("CE_CLIENTE_REQUERIDO", "La solicitud de contenedor requiere al menos un cliente.");
        EmpresaId = empresaId;
        Folio = folio;
        Tipo = tipo;
        SucursalId = sucursalId;
        OcId = ocId;
        Editar(referencia, incotermId, moneda, null, null, null, responsable);
        foreach (var cliente in clientes)
            AgregarParticipante(Guid.CreateVersion7(), cliente, "Cliente", null, PapelParticipante.Captura, null, null);
    }

    public void ExigirAbierto()
    {
        if (Estado != EstadoExpediente.Abierto)
            throw new BusinessRuleException("CE_EXPEDIENTE_NO_ABIERTO", "El expediente está cancelado y conserva su histórico.");
    }

    internal static string MotivoValido(string? motivo)
    {
        var limpio = motivo?.Trim();
        if (limpio is null || limpio.Length is < 5 or > 500)
            throw new BusinessRuleException("CE_MOTIVO_INVALIDO", "El motivo debe tener entre 5 y 500 caracteres.");
        return limpio;
    }

    public void Editar(string referencia, Guid incoterm, string moneda, decimal? tc, DateOnly? fechaTc, string? fuenteTc, Guid responsable)
    {
        ExigirAbierto();
        if (incoterm == Guid.Empty)
            throw new BusinessRuleException("CE_INCOTERM_REQUERIDO", "Seleccione un Incoterm.");
        if (moneda is null || moneda.Length != 3 || moneda.Any(c => c is < 'A' or > 'Z'))
            throw new BusinessRuleException("CE_MONEDA_INVALIDA", "Seleccione una moneda ISO activa.");
        if (string.IsNullOrWhiteSpace(referencia) || referencia.Trim().Length > 200 || responsable == Guid.Empty)
            throw new BusinessRuleException("CE_DATOS_INVALIDOS", "Referencia (1-200 caracteres) y responsable son obligatorios.");
        if (tc is <= 0 || tc >= 1_000_000_000_000m || (tc is decimal valor && decimal.Round(valor, 6) != valor))
            throw new BusinessRuleException("CE_TC_INVALIDO", "El TC debe ser positivo con máximo seis decimales.");
        if ((tc is not null && (fechaTc is null || string.IsNullOrWhiteSpace(fuenteTc))) || (tc is null && (fechaTc is not null || !string.IsNullOrWhiteSpace(fuenteTc))) || fuenteTc?.Length > 200)
            throw new BusinessRuleException("CE_TC_DATOS_REQUERIDOS", "Capture juntos TC, fecha y fuente (máximo 200 caracteres).");
        Referencia = referencia.Trim();
        IncotermId = incoterm;
        Moneda = moneda;
        Tc = tc;
        FechaTc = fechaTc;
        FuenteTc = fuenteTc?.Trim();
        ResponsableUsuarioId = responsable;
        RevisionContenido++;
    }

    public void AgregarParticipante(Guid id, string nombre, string tipo, string? contacto, PapelParticipante papel, Guid? proveedorId, Guid? clienteId)
    {
        ExigirAbierto();
        _participantes.Add(new(id, EmpresaId, Id, nombre, tipo, contacto, papel, proveedorId, clienteId));
        RevisionContenido++;
    }

    public void EditarParticipante(Guid id, string nombre, string tipo, string? contacto, PapelParticipante papel, Guid? proveedorId, Guid? clienteId)
    {
        ExigirAbierto();
        var participante = _participantes.SingleOrDefault(p => p.Id == id) ?? throw new EntityNotFoundException("CE_PARTICIPANTE_NO_ENCONTRADO", "No existe el participante.");
        participante.Editar(nombre, tipo, contacto, papel, proveedorId, clienteId);
        RevisionContenido++;
    }

    public void RegistrarDocumento(Guid id, RequisitoDocumento requisito, Guid adjuntoId, string? motivo, Guid actor) => AgregarDocumento(id, requisito, adjuntoId, false, motivo, actor);
    public void MarcarNoAplica(Guid id, RequisitoDocumento requisito, string motivo, Guid actor) => AgregarDocumento(id, requisito, null, true, motivo, actor);
    private void AgregarDocumento(Guid id, RequisitoDocumento requisito, Guid? adjuntoId, bool noAplica, string? motivo, Guid actor)
    {
        ExigirAbierto();
        if (noAplica && !requisito.Condicional)
            throw new BusinessRuleException("CE_REQUISITO_NO_CONDICIONAL", "Este requisito no permite No aplica.");
        if (!noAplica && (adjuntoId is null || adjuntoId == Guid.Empty))
            throw new BusinessRuleException("CE_ADJUNTO_INVALIDO", "Seleccione un adjunto vigente de este expediente.");
        if (adjuntoId is not null && _documentos.Any(d => d.AdjuntoId == adjuntoId))
            throw new BusinessRuleException("CE_ADJUNTO_YA_VERSIONADO", "Este archivo ya tiene una versión documental.");
        var anterior = _documentos.SingleOrDefault(d => d.TipoDocumentoId == requisito.Id && d.Estado == EstadoDocumento.Vigente);
        var razon = noAplica || anterior is not null ? MotivoValido(motivo) : null;
        if (actor == Guid.Empty)
            throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Se requiere responsable del documento.");
        var nuevo = new DocumentoExpediente(id, EmpresaId, Id, requisito.Id, (anterior?.NumeroVersion ?? 0) + 1, adjuntoId, anterior?.Id, razon, noAplica, actor);
        anterior?.Reemplazar();
        _documentos.Add(nuevo);
        RevisionPendiente = true;
        RevisadoPorId = null;
        RevisionContenido++;
    }

    public void DarDeBajaAdjunto(Guid adjuntoId)
    {
        ExigirAbierto();
        if (!_documentos.Any(d => d.AdjuntoId == adjuntoId && d.Estado == EstadoDocumento.Vigente)) return;
        RevisionPendiente = true;
        RevisadoPorId = null;
        RevisionContenido++;
    }

    public IReadOnlyList<RequisitoDocumento> Faltantes(IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> adjuntosVigentes) => requisitos.Where(r => r.Obligatorio && !_documentos.Any(d => d.TipoDocumentoId == r.Id && d.Estado == EstadoDocumento.Vigente && ((d.NoAplica && r.Condicional) || (d.AdjuntoId is Guid a && adjuntosVigentes.Contains(a))))).ToArray();
    public void RevisarDocumentos(IReadOnlyList<RequisitoDocumento> requisitos, IReadOnlyCollection<Guid> adjuntosVigentes, Guid actor)
    {
        ExigirAbierto();
        var faltantes = Faltantes(requisitos, adjuntosVigentes);
        if (faltantes.Count > 0)
            throw new DocumentosFaltantesException(faltantes);
        if (actor == Guid.Empty)
            throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Se requiere revisor.");
        RevisadoPorId = actor;
        RevisionPendiente = false;
        RevisionContenido++;
    }

    public void Cancelar(string motivo)
    {
        ExigirAbierto();
        MotivoCancelacion = MotivoValido(motivo);
        Estado = EstadoExpediente.Cancelado;
        RevisionContenido++;
    }
}
