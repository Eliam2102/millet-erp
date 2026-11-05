using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed class AutorizacionSalida : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public int VersionEmbarque { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public bool Autorizada { get; private set; }
    public string? Motivo { get; private set; }
    public string ContenidoJson { get; private set; } = "";
    public int Secuencia { get; private set; }
    private AutorizacionSalida() { }
    internal AutorizacionSalida(Expediente e, Guid actor, DateTimeOffset fecha, bool autorizada, string? motivo) : base(Guid.CreateVersion7())
    {
        EmpresaId = e.EmpresaId; ExpedienteId = e.Id; VersionEmbarque = e.VersionEmbarque;
        ContenidoJson = System.Text.Json.JsonSerializer.Serialize(new { e.VersionEmbarque, e.Contenedores, e.Partidas });
        ActorId = actor; Fecha = fecha; Autorizada = autorizada; Motivo = motivo; Secuencia = e.AutorizacionesSalida.Count + 1;
    }
}
public sealed partial class Expediente
{
    public int VersionEmbarque { get; private set; }
    public Guid[] PreparadoresEmbarque { get; private set; } = [];
    public Guid? PreparadoPorId { get; private set; }
    private readonly List<AutorizacionSalida> _autorizacionesSalida = [];
    public IReadOnlyCollection<AutorizacionSalida> AutorizacionesSalida => _autorizacionesSalida.AsReadOnly();
    public bool SalidaAutorizada => _autorizacionesSalida.Where(a => a.VersionEmbarque == VersionEmbarque).MaxBy(a => a.Secuencia)?.Autorizada == true;
    private void VersionarEmbarque(Guid actor)
    {
        PreparadoresEmbarque = PreparadoresEmbarque.Append(actor).Distinct().ToArray();
        VersionEmbarque++; PreparadoPorId = actor; CierreFisicoPorId = null; CierreFisicoEn = null;
    }
    public void AutorizarSalida(Guid actor, DateTimeOffset fecha, string? motivo, bool autorizar)
    {
        ExigirExportacion();
        if (actor == Guid.Empty) throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Se requiere la persona que autoriza o rechaza la salida.");
        if (PreparadoresEmbarque.Contains(actor) || _partidas.Any(p => p.ConfirmadoPorId == actor)) throw new BusinessRuleException("CE_SALIDA_SEPARACION_FUNCIONES", "La salida debe autorizarla o rechazarla una persona distinta de quien preparó el embarque.");
        var razon = autorizar ? null : MotivoValido(motivo);
        _autorizacionesSalida.Add(new(this, actor, fecha, autorizar, razon)); RevisionContenido++;
    }
    public void GuardarPackingPartida(Guid id, decimal pesoKg, int bultos, Guid actor)
    {
        ExigirExportacion();
        var p = _partidas.SingleOrDefault(p => p.Id == id && p.Estado != EstadoPartida.Liberada) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "Seleccione una partida activa del embarque.");
        p.GuardarPacking(pesoKg, bultos); VersionarEmbarque(actor); RevisionContenido++;
    }
}
