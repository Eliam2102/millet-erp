using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;
public enum ResultadoConteo { Pendiente, Encontrado, NoEncontrado, OtraUbicacion, Sobrante }
public sealed class ConteoActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public string? Ubicacion { get; private set; }
    public Guid AbiertoPor { get; private set; }
    public DateTimeOffset? CerradoEn { get; private set; }
    public Guid? CerradoPor { get; private set; }
    public List<PartidaConteoActivo> Partidas { get; private set; } = [];
    private ConteoActivo() { }
    public ConteoActivo(Guid empresa, Guid sucursal, string? ubicacion, Guid actor, IEnumerable<Activo> esperados) : base(Guid.CreateVersion7())
    {
        if (empresa == Guid.Empty || sucursal == Guid.Empty || actor == Guid.Empty || ubicacion?.Length > 500)
            throw new BusinessRuleException("AF_CONTEO_INVALIDO", "Seleccione sucursal y ubicación válidas.");
        EmpresaId = empresa; SucursalId = sucursal; Ubicacion = string.IsNullOrWhiteSpace(ubicacion) ? null : ubicacion.Trim(); AbiertoPor = actor;
        foreach (var a in esperados)
        {
            if (a.EmpresaId != empresa || a.SucursalId != sucursal || Ubicacion != null && a.Ubicacion != Ubicacion || a.Estado == EstadoActivo.DadoDeBaja)
                throw new BusinessRuleException("AF_CONTEO_ALCANCE", "La lista esperada no corresponde al alcance del conteo.");
            Partidas.Add(new PartidaConteoActivo(this, a));
        }
    }
    public void Capturar(string? numero, ResultadoConteo resultado, string? ubicacionReal, string evidencia, Guid actor)
    {
        if (CerradoEn != null) throw new BusinessRuleException("AF_CONTEO_CERRADO", "El conteo está cerrado y no admite cambios.");
        numero = string.IsNullOrWhiteSpace(numero) ? null : numero.Trim();
        if (numero?.Length > 60 || actor == Guid.Empty || !Enum.IsDefined(resultado) || resultado == ResultadoConteo.Pendiente || string.IsNullOrWhiteSpace(evidencia) || evidencia.Length > 2000 || ubicacionReal?.Length > 500)
            throw new BusinessRuleException("AF_CAPTURA_INVALIDA", "Capture un resultado y su evidencia (hasta 2000 caracteres).");
        var partida = numero == null ? null : Partidas.SingleOrDefault(x => x.Numero == numero);
        if (partida == null)
        {
            if (resultado != ResultadoConteo.Sobrante) throw new BusinessRuleException("AF_NO_ESPERADO", "El número no está en la lista; regístrelo como sobrante.");
            partida = new PartidaConteoActivo(this, null, numero); Partidas.Add(partida);
        }
        partida.Capturar(resultado, ubicacionReal, evidencia, actor);
    }
    public void Cerrar(Guid actor, DateTimeOffset ahora)
    {
        if (CerradoEn != null) throw new BusinessRuleException("AF_CONTEO_CERRADO", "El conteo ya está cerrado.");
        if (Partidas.Any(x => x.Resultado == ResultadoConteo.Pendiente)) throw new BusinessRuleException("AF_CONTEO_INCOMPLETO", "Marque todos los activos esperados antes de cerrar.");
        CerradoEn = ahora; CerradoPor = actor;
    }
    public void Autorizar(Guid partidaId, Guid actor, string evidencia, DateTimeOffset ahora)
    {
        if (CerradoEn == null) throw new BusinessRuleException("AF_CONTEO_ABIERTO", "Cierre el conteo antes de autorizar diferencias.");
        var partida = Partidas.SingleOrDefault(x => x.Id == partidaId) ?? throw new BusinessRuleException("AF_PARTIDA_NO_EXISTE", "No existe la partida.");
        partida.Autorizar(actor, evidencia, ahora);
    }
}
public sealed class PartidaConteoActivo : BaseEntity, IPerteneceAEmpresa, IAuditable, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ConteoId { get; private set; }
    public Guid AggregateRootId => ConteoId;
    public Guid? ActivoId { get; private set; }
    public string? Numero { get; private set; }
    public string DescripcionEsperada { get; private set; } = "";
    public string? UbicacionEsperada { get; private set; }
    public ResultadoConteo Resultado { get; private set; }
    public string? UbicacionReal { get; private set; }
    public string? Evidencia { get; private set; }
    public Guid? CapturadoPor { get; private set; }
    public Guid? AutorizadoPor { get; private set; }
    public DateTimeOffset? AutorizadoEn { get; private set; }
    public string? EvidenciaAutorizacion { get; private set; }
    public string? Propuesta { get; private set; }
    private PartidaConteoActivo() { }
    internal PartidaConteoActivo(ConteoActivo c, Activo? a, string? numero = null) : base(Guid.CreateVersion7())
    { EmpresaId = c.EmpresaId; ConteoId = c.Id; ActivoId = a?.Id; Numero = a?.Numero ?? numero; DescripcionEsperada = a?.Descripcion ?? "Sobrante"; UbicacionEsperada = a?.Ubicacion; }
    internal void Capturar(ResultadoConteo resultado, string? ubicacion, string evidencia, Guid actor)
    {
        if ((ActivoId == null) != (resultado == ResultadoConteo.Sobrante) || resultado == ResultadoConteo.OtraUbicacion && (string.IsNullOrWhiteSpace(ubicacion) || ubicacion.Trim() == UbicacionEsperada))
            throw new BusinessRuleException("AF_RESULTADO_INVALIDO", "Revise el resultado y capture una ubicación real distinta de la esperada.");
        Resultado = resultado; UbicacionReal = ubicacion?.Trim(); Evidencia = evidencia.Trim(); CapturadoPor = actor;
    }
    internal void Autorizar(Guid actor, string evidencia, DateTimeOffset ahora)
    {
        if (actor == CapturadoPor) throw new ForbiddenException("AF_AUTORIZADOR_DISTINTO", "Otra persona debe autorizar la diferencia.");
        if (actor == Guid.Empty || Resultado is ResultadoConteo.Pendiente or ResultadoConteo.Encontrado || AutorizadoEn != null || string.IsNullOrWhiteSpace(evidencia) || evidencia.Length > 2000)
            throw new BusinessRuleException("AF_AUTORIZACION_INVALIDA", "Seleccione una diferencia pendiente y documente su autorización.");
        AutorizadoPor = actor; AutorizadoEn = ahora; EvidenciaAutorizacion = evidencia.Trim();
        Propuesta = Resultado switch { ResultadoConteo.OtraUbicacion => "Traslado", ResultadoConteo.NoEncontrado => "Baja", _ => "Alta o investigación" };
    }
}
