using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public enum ModalidadTransporte { Maritimo = 1, Terrestre = 2, Aereo = 3 }

public sealed class PartidaImportacion : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public Guid ContenedorId { get; private set; }
    public Guid OcLineaId { get; private set; }
    public int Posicion { get; private set; }
    public decimal Cantidad { get; private set; }
    public string Unidad { get; private set; } = "";
    public decimal? CantidadRecibida { get; private set; }
    public DateOnly? FechaRecepcion { get; private set; }
    public DateOnly? CompromisoRecepcion { get; private set; }
    public Guid[] EventosRecepcion { get; private set; } = [];
    internal void RegistrarRecepcion(Guid evento, decimal cantidad, DateOnly fecha, DateOnly compromiso)
    { CantidadRecibida = cantidad; FechaRecepcion = fecha; CompromisoRecepcion = compromiso; EventosRecepcion = [.. EventosRecepcion, evento]; }
    public Guid RegistradoPorId { get; private set; }
    internal void AjustarCantidad(decimal nueva) => Cantidad = nueva;
    private PartidaImportacion() { }
    internal PartidaImportacion(Expediente e, Guid contenedor, Guid linea, int posicion, decimal cantidad, string unidad, Guid actor) : base(Guid.CreateVersion7())
    {
        if (linea == Guid.Empty || posicion < 1 || actor == Guid.Empty || string.IsNullOrWhiteSpace(unidad) || unidad.Length > 20 || cantidad <= 0 || cantidad >= 1_000_000_000_000m || decimal.Round(cantidad, 6) != cantidad)
            throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "Seleccione una partida de la OC y una cantidad positiva con hasta seis decimales.");
        EmpresaId = e.EmpresaId; ExpedienteId = e.Id; ContenedorId = contenedor; OcLineaId = linea;
        Posicion = posicion; Cantidad = cantidad; Unidad = unidad; RegistradoPorId = actor;
    }
}

public sealed partial class Expediente
{
    public string? AduanaEntrada { get; private set; }
    public void RegistrarAduanaEntrada(string? aduana)
    {
        if (Tipo != TipoExpediente.Importacion || aduana is null || !CatalogoAduanas.Aduanas.ContainsKey(aduana))
            throw new BusinessRuleException("CE_ADUANA_INVALIDA", "Seleccione la aduana de entrada del catálogo.");
        AduanaEntrada = aduana; RevisionContenido++;
    }

    private readonly List<PartidaImportacion> _partidasImportacion = [];
    public IReadOnlyCollection<PartidaImportacion> PartidasImportacion => _partidasImportacion.AsReadOnly();
    public decimal RemanenteImportacion(Guid linea, decimal pedido) => pedido - _partidasImportacion
        .Where(p => p.OcLineaId == linea && Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo).Sum(p => p.Cantidad);
    public void AsignarPartidaImportacion(Guid contenedor, Guid linea, int posicion, decimal cantidad, decimal pedido, string unidad, Guid actor)
    {
        ExigirAbierto();
        if (Tipo != TipoExpediente.Importacion) throw new BusinessRuleException("CE_SOLO_IMPORTACION", "Seleccione un expediente de importación.");
        var c = Contenedor(contenedor); c.ExigirActivo();
        if (c.FechaEmbarqueReal is not null) throw new BusinessRuleException("CE_EMBARQUE_YA_REGISTRADO", "El embarque ya está registrado; solicite un ajuste de cantidades.");
        if (cantidad > RemanenteImportacion(linea, pedido)) throw new BusinessRuleException("CE_CANTIDAD_EXCEDE_OC", $"Partida {linea}: pedido {pedido}, ya embarcado {pedido - RemanenteImportacion(linea, pedido)}, intenta {cantidad}.");
        _partidasImportacion.Add(new(this, contenedor, linea, posicion, cantidad, unidad, actor)); RevisionContenido++;
    }
    public void QuitarPartidaImportacion(Guid id)
    {
        ExigirAbierto();
        var p = _partidasImportacion.SingleOrDefault(p => p.Id == id) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "No existe la partida de importación.");
        if (Contenedor(p.ContenedorId).FechaEmbarqueReal is not null) throw new BusinessRuleException("CE_EMBARQUE_YA_REGISTRADO", "El embarque ya está registrado; solicite un ajuste de cantidades.");
        _partidasImportacion.Remove(p); RevisionContenido++;
    }
    private void ExigirTransporte(ContenedorExpediente c)
    {
        if (!_participantes.Any(p => p.Tipo == TipoParticipante.AgenteAduanal))
            throw new BusinessRuleException("CE_AGENTE_ADUANAL_REQUERIDO", "Agregue el agente aduanal antes de embarcar.");
        var tipo = c.Modalidad switch
        {
            ModalidadTransporte.Maritimo => TipoParticipante.Naviera,
            ModalidadTransporte.Terrestre => TipoParticipante.TransportistaTerrestre,
            ModalidadTransporte.Aereo => TipoParticipante.Aerolinea,
            _ => throw new BusinessRuleException("CE_MODALIDAD_REQUERIDA", "Seleccione la modalidad de transporte del contenedor antes de embarcar.")
        };
        if (!_participantes.Any(p => p.Tipo == tipo))
            throw new BusinessRuleException("CE_TRANSPORTISTA_REQUERIDO", "Agregue " + (tipo == TipoParticipante.Naviera ? "la naviera" : tipo == TipoParticipante.Aerolinea ? "la línea aérea" : "el transportista terrestre") + " en participantes antes de embarcar.");
    }
}
