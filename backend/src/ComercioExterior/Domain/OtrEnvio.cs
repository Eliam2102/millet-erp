using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public enum EstadoOtrEnvio { PorVincular = 1, Vinculado = 2, Cancelado = 3 }
public enum EstadoOtrLinea { Pendiente = 1, Propuesta = 2, RequiereRevision = 3 }
public sealed record OtrLineaRecibida(string NumeroPedido, int Posicion, decimal Cantidad, string Unidad);
public sealed record OtrContenido(Guid ClienteId, Guid SucursalId, string Cliente, string DireccionEntrega,
    string? Producto, DatosContenedor Contenedor, IReadOnlyList<OtrLineaRecibida> Lineas, bool Cancelado = false, string? ClienteNumeroAw = null, string? SucursalClaveAw = null);
public sealed record OtrEdicion(string Actor, DateTimeOffset Fecha);

/// <summary>OTR es transporte de exportación; no origina compras ni usa controles de Facturación.</summary>
public sealed class OtrEnvio : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public string IdOrigen { get; private set; } = "";
    public long VersionAplicada { get; private set; }
    public string Hash { get; private set; } = "";
    public string ContenidoJson { get; private set; } = "{}";
    public string? MotivoVinculacion { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid? ExpedienteId { get; private set; }
    public Guid? ContenedorId { get; private set; }
    public EstadoOtrEnvio Estado { get; private set; }
    public int RevisionContenido { get; private set; }
    public void RegistrarCambio() => RevisionContenido++;
    public OtrSalida CrearSalida(string tipo, int version, string valor, IEnumerable<OtrSalida> existentes)
    {
        if (existentes.Any(s => s.OtrEnvioId == Id && s.Tipo == tipo && s.VersionErp == version))
            throw new BusinessRuleException("CE_OTR_SALIDA_DUPLICADA", "El envío ya tiene una salida de este tipo y versión ERP.");
        return new(EmpresaId, Id, tipo, version, valor);
    }
    public string EdicionesJson { get; private set; } = "{}";
    private readonly List<OtrLinea> _lineas = [];
    public IReadOnlyCollection<OtrLinea> Lineas => _lineas.AsReadOnly();
    private readonly List<OtrConflicto> _conflictos = [];
    public IReadOnlyCollection<OtrConflicto> Conflictos => _conflictos.AsReadOnly();
    private OtrEnvio() { }
    public OtrEnvio(Guid empresa, string origen) : base(Guid.CreateVersion7())
    {
        if (empresa == Guid.Empty || string.IsNullOrWhiteSpace(origen) || origen.Length > 100)
            throw new BusinessRuleException("CE_OTR_DATOS_INVALIDOS", "Empresa e id OTR (máximo 100 caracteres) son obligatorios.");
        EmpresaId = empresa; IdOrigen = origen; Estado = EstadoOtrEnvio.PorVincular;
    }
    public OtrContenido Contenido() => JsonSerializer.Deserialize<OtrContenido>(ContenidoJson)!;
    public bool Aplicar(long version, OtrContenido contenido)
    {
        if (version < 1) throw new BusinessRuleException("CE_OTR_VERSION_INVALIDA", "La versión OTR debe ser positiva.");
        var json = JsonSerializer.Serialize(contenido);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        if (version == VersionAplicada && hash != Hash)
            throw new BusinessRuleException("CE_OTR_VERSION_CONTENIDO_DISTINTO", "La misma versión OTR contiene datos distintos.");
        if (version <= VersionAplicada) return false;
        if ((contenido.ClienteId == Guid.Empty && string.IsNullOrWhiteSpace(contenido.ClienteNumeroAw)) || (contenido.SucursalId == Guid.Empty && string.IsNullOrWhiteSpace(contenido.SucursalClaveAw)) || string.IsNullOrWhiteSpace(contenido.Cliente)
            || contenido.Cliente.Length > 200 || string.IsNullOrWhiteSpace(contenido.DireccionEntrega) || contenido.DireccionEntrega.Length > 1000
            || contenido.Producto?.Length > 500 || contenido.Lineas.Count > 1000 || (!contenido.Cancelado && contenido.Lineas.Count == 0)
            || contenido.Lineas.Any(l => string.IsNullOrWhiteSpace(l.NumeroPedido) || l.NumeroPedido.Length > 50 || l.Posicion < 1 || l.Cantidad <= 0 || l.Cantidad >= 1_000_000_000_000m || decimal.Round(l.Cantidad, 6) != l.Cantidad || string.IsNullOrWhiteSpace(l.Unidad) || l.Unidad.Length > 20)
            || contenido.Lineas.GroupBy(l => (l.NumeroPedido, l.Posicion)).Any(g => g.Count() > 1))
            throw new BusinessRuleException("CE_OTR_DATOS_INVALIDOS", "Revise cliente, sucursal, entrega y líneas únicas con cantidad positiva hasta seis decimales.");
        // Valida también los límites de CE-02 antes de guardar una entrada aún sin expediente.
        _ = new ContenedorExpediente(Guid.NewGuid(), EmpresaId, Guid.NewGuid(), contenido.Contenedor);
        if (VersionAplicada > 0 && ((ClienteId != Guid.Empty && contenido.ClienteId != ClienteId) || (SucursalId != Guid.Empty && contenido.SucursalId != SucursalId)))
            throw new BusinessRuleException("CE_OTR_IDENTIDAD_CAMBIADA", "Un envío conserva cliente y sucursal; corrija el origen con otro id de envío.");
        if (!contenido.Cancelado && Estado == EstadoOtrEnvio.Cancelado)
            throw new BusinessRuleException("CE_OTR_CANCELADO", "Un envío cancelado conserva su histórico; use un nuevo id OTR.");
        VersionAplicada = version; ClienteId = contenido.ClienteId; SucursalId = contenido.SucursalId;
        MotivoVinculacion = ClienteId == Guid.Empty ? "CE_OTR_CLIENTE_DESCONOCIDO" : SucursalId == Guid.Empty ? "CE_OTR_SUCURSAL_DESCONOCIDA" : null;
        ContenidoJson = json; Hash = hash;
        foreach (var conflicto in _conflictos.Where(c => c.Estado == "Pendiente")) conflicto.Superar(version);
        foreach (var anterior in _lineas) anterior.Revisar();
        foreach (var linea in contenido.Lineas) _lineas.Add(new(EmpresaId, Id, version, linea));
        if (contenido.Cancelado) Estado = EstadoOtrEnvio.Cancelado;
        return true;
    }
    public void ExigirIdentidad()
    {
        if (MotivoVinculacion is not null) throw new BusinessRuleException(MotivoVinculacion, "Configure la equivalencia A+W del cliente o sucursal antes de vincular.");
    }
    public void Vincular(Expediente expediente)
    {
        if (Estado == EstadoOtrEnvio.Cancelado) throw new BusinessRuleException("CE_OTR_CANCELADO", "El envío está cancelado.");
        if (ExpedienteId is not null && ExpedienteId != expediente.Id)
            throw new BusinessRuleException("CE_OTR_YA_VINCULADO", "El envío ya está vinculado a otro expediente.");
        ExigirIdentidad();
        expediente.ExigirAbierto();
        if (expediente.EmpresaId != EmpresaId || expediente.SucursalId != SucursalId)
            throw new BusinessRuleException("CE_SUCURSAL_DISTINTA", "Envío y expediente deben pertenecer a la misma empresa y sucursal.");
        if (expediente.Tipo != TipoExpediente.Exportacion || !expediente.Participantes.Any(p => p.ClienteId == ClienteId))
            throw new BusinessRuleException("CE_OTR_CLIENTE_DISTINTO", "Seleccione un expediente de exportación abierto del mismo cliente.");
        ExpedienteId = expediente.Id; ContenedorId ??= Guid.CreateVersion7(); Estado = EstadoOtrEnvio.Vinculado;
    }
    public void RegistrarEdicion(DatosContenedor antes, DatosContenedor despues, string actor, DateTimeOffset fecha)
    {
        var ediciones = JsonSerializer.Deserialize<Dictionary<string, OtrEdicion>>(EdicionesJson)!;
        foreach (var campo in Campos(antes).Where(c => c.Value != Campos(despues)[c.Key])) ediciones[campo.Key] = new(actor, fecha);
        EdicionesJson = JsonSerializer.Serialize(ediciones);
    }
    public static string ClavePartida(string pedido, int posicion) => $"Partida:{pedido}:{posicion}";
    public static string ValorPartida(decimal cantidad, string unidad) => JsonSerializer.Serialize(new { Cantidad = cantidad, Unidad = unidad });
    public void RegistrarEdicionPartidas(IReadOnlyDictionary<string, string> antes, IReadOnlyDictionary<string, string> despues, string actor, DateTimeOffset fecha)
    {
        var ediciones = JsonSerializer.Deserialize<Dictionary<string, OtrEdicion>>(EdicionesJson)!;
        foreach (var campo in antes.Keys.Concat(despues.Keys).Distinct().Where(k => antes.GetValueOrDefault(k) != despues.GetValueOrDefault(k))) ediciones[campo] = new(actor, fecha);
        EdicionesJson = JsonSerializer.Serialize(ediciones);
    }
    public bool ConflictoPartida(OtrLinea linea, string valorActual, DateTimeOffset fecha)
    {
        var ediciones = JsonSerializer.Deserialize<Dictionary<string, OtrEdicion>>(EdicionesJson)!;
        var clave = ClavePartida(linea.NumeroPedido, linea.Posicion);
        var recibido = ValorPartida(linea.Cantidad, linea.Unidad);
        if (!ediciones.TryGetValue(clave, out var edicion) || recibido == valorActual) return false;
        var campo = "Partida:" + linea.Id;
        if (!_conflictos.Any(c => c.VersionOrigen == VersionAplicada && c.Campo == campo))
            _conflictos.Add(new(EmpresaId, Id, VersionAplicada, campo, recibido, valorActual, edicion.Actor, edicion.Fecha, fecha));
        linea.Pendiente("CE_OTR_CONFLICTO_PARTIDA: la asignación se editó a mano; resuelva los dos valores.");
        return true;
    }
    public OtrConflicto Conflicto(Guid id) => _conflictos.SingleOrDefault(c => c.Id == id)
        ?? throw new EntityNotFoundException("CE_OTR_CONFLICTO_NO_ENCONTRADO", "No existe el conflicto.");
    public void ResolverValor(Guid conflictoId, bool usarOtr, string motivo, Guid actor, DateTimeOffset fecha, string valorActual, string? claveProtegida = null)
    {
        var conflicto = Conflicto(conflictoId);
        if (conflicto.Estado != "Pendiente") throw new BusinessRuleException("CE_OTR_CONFLICTO_RESUELTO", "El conflicto ya fue resuelto.");
        if (conflicto.VersionOrigen != VersionAplicada || valorActual != conflicto.ValorErp)
            throw new BusinessRuleException("CE_OTR_CONFLICTO_DESACTUALIZADO", "Cambió el origen o el ERP; recargue el envío y revise los valores vigentes.");
        conflicto.Resolver(usarOtr, motivo, actor, fecha); RegistrarCambio();
        var ediciones = JsonSerializer.Deserialize<Dictionary<string, OtrEdicion>>(EdicionesJson)!;
        var clave = claveProtegida ?? conflicto.Campo;
        if (usarOtr) ediciones.Remove(clave); else ediciones[clave] = new(actor.ToString(), fecha);
        EdicionesJson = JsonSerializer.Serialize(ediciones);
    }
    public DatosContenedor Conciliar(ContenedorExpediente? actual, DateTimeOffset fecha)
    {
        var recibido = Contenido().Contenedor;
        var vigente = actual is null ? new DatosContenedor(null, null, null, null, null, null, null, null) : Datos(actual);
        var campos = Campos(vigente);
        var ediciones = JsonSerializer.Deserialize<Dictionary<string, OtrEdicion>>(EdicionesJson)!;
        foreach (var (campo, valor) in Campos(recibido))
        {
            if (valor == campos[campo]) continue;
            // null significa dato no informado para los campos ERP.
            var erp = campo is "Booking" or "BlGuia" or "Sello";
            if (erp && valor == "null") continue;
            if (erp || ediciones.ContainsKey(campo))
            {
                if (!_conflictos.Any(c => c.VersionOrigen == VersionAplicada && c.Campo == campo))
                    _conflictos.Add(new(EmpresaId, Id, VersionAplicada, campo, valor, campos[campo], ediciones.GetValueOrDefault(campo)?.Actor ?? actual?.UpdatedBy ?? "ERP", ediciones.GetValueOrDefault(campo)?.Fecha ?? actual?.UpdatedAt ?? fecha, fecha));
            }
            else vigente = Cambiar(vigente, campo, valor);
        }
        return vigente;
    }
    public DatosContenedor Resolver(Guid conflictoId, bool usarOtr, string motivo, Guid actor, DateTimeOffset fecha, ContenedorExpediente actual)
    {
        var conflicto = Conflicto(conflictoId);
        var datos = usarOtr ? Cambiar(Datos(actual), conflicto.Campo, conflicto.ValorOtr) : Datos(actual);
        ResolverValor(conflictoId, usarOtr, motivo, actor, fecha, Campos(Datos(actual))[conflicto.Campo]);
        return datos;
    }
    public static DatosContenedor Datos(ContenedorExpediente c) => new(c.Numero, c.Sello, c.Booking, c.BlGuia, c.Ruta, c.FechaCargaPrevista, c.PesoKg, c.Bultos);
    private static Dictionary<string, string> Campos(DatosContenedor d) => new()
    {
        ["Numero"] = JsonSerializer.Serialize(d.Numero), ["Sello"] = JsonSerializer.Serialize(d.Sello),
        ["Booking"] = JsonSerializer.Serialize(d.Booking), ["BlGuia"] = JsonSerializer.Serialize(d.BlGuia),
        ["Ruta"] = JsonSerializer.Serialize(d.Ruta), ["FechaCargaPrevista"] = JsonSerializer.Serialize(d.FechaCargaPrevista),
        ["PesoKg"] = JsonSerializer.Serialize(d.PesoKg), ["Bultos"] = JsonSerializer.Serialize(d.Bultos)
    };
    private static DatosContenedor Cambiar(DatosContenedor d, string campo, string valor) => campo switch
    {
        "Numero" => d with { Numero = JsonSerializer.Deserialize<string>(valor) }, "Sello" => d with { Sello = JsonSerializer.Deserialize<string>(valor) },
        "Booking" => d with { Booking = JsonSerializer.Deserialize<string>(valor) }, "BlGuia" => d with { BlGuia = JsonSerializer.Deserialize<string>(valor) },
        "Ruta" => d with { Ruta = JsonSerializer.Deserialize<string>(valor) }, "FechaCargaPrevista" => d with { FechaCargaPrevista = JsonSerializer.Deserialize<DateOnly?>(valor) },
        "PesoKg" => d with { PesoKg = JsonSerializer.Deserialize<decimal?>(valor) }, "Bultos" => d with { Bultos = JsonSerializer.Deserialize<int?>(valor) },
        _ => throw new BusinessRuleException("CE_OTR_CAMPO_INVALIDO", "El campo no corresponde al contrato OTR.")
    };
}
public sealed class OtrLinea : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid OtrEnvioId { get; private set; }
    public Guid AggregateRootId => OtrEnvioId;
    public long VersionOrigen { get; private set; }
    public string NumeroPedido { get; private set; } = "";
    public int Posicion { get; private set; }
    public decimal Cantidad { get; private set; }
    public string Unidad { get; private set; } = "";
    public EstadoOtrLinea Estado { get; private set; } = EstadoOtrLinea.Pendiente;
    public string? Motivo { get; private set; }
    public Guid? PartidaId { get; private set; }
    private OtrLinea() { }
    internal OtrLinea(Guid empresa, Guid envio, long version, OtrLineaRecibida l) : base(Guid.CreateVersion7())
    { EmpresaId = empresa; OtrEnvioId = envio; VersionOrigen = version; NumeroPedido = l.NumeroPedido; Posicion = l.Posicion; Cantidad = l.Cantidad; Unidad = l.Unidad; }
    public void Pendiente(string motivo) { Estado = EstadoOtrLinea.Pendiente; Motivo = motivo; }
    public void Proponer(Guid partida) { PartidaId = partida; Estado = EstadoOtrLinea.Propuesta; Motivo = "Confirme la partida en Contenedores después de revisarla."; }
    internal void Revisar() { if (PartidaId is not null) { Estado = EstadoOtrLinea.RequiereRevision; Motivo = "Cambió la versión OTR o se canceló el envío."; } }
}
public sealed class OtrConflicto : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid OtrEnvioId { get; private set; }
    public Guid AggregateRootId => OtrEnvioId;
    public long VersionOrigen { get; private set; }
    public string Campo { get; private set; } = "";
    public string ValorOtr { get; private set; } = "";
    public string ValorErp { get; private set; } = "";
    public string ActorErp { get; private set; } = "";
    public DateTimeOffset FechaErp { get; private set; }
    public DateTimeOffset RecibidoAt { get; private set; }
    public string Estado => SuperadoPorVersion is not null ? "Superado" : ResueltoPorId is not null ? "Resuelto" : "Pendiente";
    public long? SuperadoPorVersion { get; private set; }
    internal void Superar(long version) => SuperadoPorVersion = version;
    public Guid? ResueltoPorId { get; private set; }
    public DateTimeOffset? ResueltoAt { get; private set; }
    public bool? UsarOtr { get; private set; }
    public string? Motivo { get; private set; }
    private OtrConflicto() { }
    internal OtrConflicto(Guid empresa, Guid envio, long version, string campo, string otr, string erp, string actor, DateTimeOffset fechaErp, DateTimeOffset fecha) : base(Guid.CreateVersion7())
    { EmpresaId = empresa; OtrEnvioId = envio; VersionOrigen = version; Campo = campo; ValorOtr = otr; ValorErp = erp; ActorErp = actor; FechaErp = fechaErp; RecibidoAt = fecha; }
    internal void Resolver(bool usar, string motivo, Guid actor, DateTimeOffset fecha)
    {
        if (actor == Guid.Empty) throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Identifique al responsable de la decisión.");
        Motivo = Expediente.MotivoValido(motivo); UsarOtr = usar; ResueltoPorId = actor; ResueltoAt = fecha;
    }
}
