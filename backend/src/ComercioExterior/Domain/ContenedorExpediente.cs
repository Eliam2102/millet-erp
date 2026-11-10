using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public enum EstadoContenedor { Activo = 1, Cancelado = 2 }
public sealed record DatosContenedor(string? Numero, string? Sello, string? Booking, string? BlGuia, string? Ruta,
    DateOnly? FechaCargaPrevista, decimal? PesoKg, int? Bultos, ModalidadTransporte? Modalidad = null);

public sealed class ContenedorExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public ModalidadTransporte? Modalidad { get; private set; }
    public string? Numero { get; private set; }
    public string? Sello { get; private set; }
    public string? Booking { get; private set; }
    public string? BlGuia { get; private set; }
    public string? Ruta { get; private set; }
    public DateOnly? FechaCargaPrevista { get; private set; }
    public DateOnly? FechaEmbarqueReal { get; private set; }
    public DateTimeOffset? PreparadoParaFacturarEn { get; private set; }
    public string? AutorizacionParcialJson { get; private set; }
    public string? ExcepcionSinFacturaJson { get; private set; }
    public decimal? PesoKg { get; private set; }
    public int? Bultos { get; private set; }
    public EstadoContenedor Estado { get; private set; } = EstadoContenedor.Activo;
    public string? MotivoCancelacion { get; private set; }
    private ContenedorExpediente() { }
    internal ContenedorExpediente(Guid id, Guid empresa, Guid expediente, DatosContenedor datos) : base(id)
    {
        EmpresaId = empresa;
        ExpedienteId = expediente;
        Editar(datos);
    }
    internal void ExigirActivo()
    {
        if (Estado != EstadoContenedor.Activo)
            throw new BusinessRuleException("CE_CONTENEDOR_CANCELADO", "El contenedor está cancelado; se conserva su histórico.");
    }
    internal void ExigirSinPreparacion()
    {
        if (PreparadoParaFacturarEn is not null || FechaEmbarqueReal is not null)
            throw new BusinessRuleException("CE_CONTENEDOR_PREPARADO", "El contenido del contenedor preparado o embarcado queda fijo.");
    }
    internal bool PrepararFactura(DateTimeOffset ahora)
    {
        ExigirActivo();
        if (PreparadoParaFacturarEn is not null) return false;
        ExigirSinPreparacion();
        ExigirReferencias();
        PreparadoParaFacturarEn = ahora;
        return true;
    }
    internal void AutorizarDespacho(bool parcial, AutorizacionDespacho autorizacion)
    {
        ExigirActivo();
        if (FechaEmbarqueReal is not null)
            throw new BusinessRuleException("CE_EMBARQUE_REGISTRADO", "La salida física ya está registrada.");
        var json = System.Text.Json.JsonSerializer.Serialize(HistorialAutorizacion(parcial).Append(autorizacion));
        if (parcial) AutorizacionParcialJson = json;
        else ExcepcionSinFacturaJson = json;
    }
    internal AutorizacionDespacho? LeerAutorizacion(bool parcial)
    {
        var historial = HistorialAutorizacion(parcial);
        return historial.Length == 0 ? null : historial[^1];
    }

    private AutorizacionDespacho[] HistorialAutorizacion(bool parcial)
    {
        var json = parcial ? AutorizacionParcialJson : ExcepcionSinFacturaJson;
        return json is null ? [] : System.Text.Json.JsonSerializer.Deserialize<AutorizacionDespacho[]>(json)!;
    }
    private void ExigirReferencias()
    {
        if (string.IsNullOrWhiteSpace(Numero) || string.IsNullOrWhiteSpace(BlGuia))
            throw new BusinessRuleException("CE_EMBARQUE_DATOS_REQUERIDOS", "Capture número de contenedor y BL o guía.");
    }
    internal void Editar(DatosContenedor datos)
    {
        ExigirActivo();
        if (datos.Modalidad is { } modalidad && !Enum.IsDefined(modalidad))
            throw new BusinessRuleException("CE_MODALIDAD_INVALIDA", "Seleccione una modalidad de transporte válida.");
        if (new[] { datos.Numero, datos.Sello, datos.Booking, datos.BlGuia, datos.Ruta }.Any(x => x?.Length > 200)
            || datos.PesoKg is < 0 or >= 1_000_000_000_000m || (datos.PesoKg is decimal peso && decimal.Round(peso, 6) != peso) || datos.Bultos < 0)
            throw new BusinessRuleException("CE_CONTENEDOR_DATOS_INVALIDOS", "Referencias: máximo 200 caracteres; peso y bultos no negativos, peso con máximo seis decimales.");
        if (FechaEmbarqueReal is not null && (string.IsNullOrWhiteSpace(datos.Numero) || string.IsNullOrWhiteSpace(datos.BlGuia)))
            throw new BusinessRuleException("CE_EMBARQUE_DATOS_REQUERIDOS", "Un embarque real debe conservar número de contenedor y BL o guía.");
        Modalidad = datos.Modalidad;
        Numero = Limpiar(datos.Numero); Sello = Limpiar(datos.Sello); Booking = Limpiar(datos.Booking);
        BlGuia = Limpiar(datos.BlGuia); Ruta = Limpiar(datos.Ruta);
        FechaCargaPrevista = datos.FechaCargaPrevista; PesoKg = datos.PesoKg; Bultos = datos.Bultos;
    }
    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    internal void Embarcar(DateOnly fecha, DateOnly hoy)
    {
        ExigirActivo();
        ExigirReferencias();
        if (fecha > hoy || FechaEmbarqueReal is not null)
            throw new BusinessRuleException("CE_FECHA_EMBARQUE_INVALIDA", "La fecha real no puede ser futura ni reemplazar un embarque registrado.");
        FechaEmbarqueReal = fecha;
    }
    internal void Cancelar(string motivo)
    {
        ExigirActivo();
        MotivoCancelacion = Expediente.MotivoValido(motivo);
        Estado = EstadoContenedor.Cancelado;
    }
}

public sealed record AutorizacionDespacho(Guid Actor, DateTimeOffset Fecha, string Nivel, string Motivo, string? Suplencia, int VersionEmbarque);
