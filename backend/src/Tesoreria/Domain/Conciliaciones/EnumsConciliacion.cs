namespace Millet.Tesoreria.Domain.Conciliaciones;

/// <summary>
/// Estado de la conciliación bancaria (§5 DDL: 1=Abierta 2=Cerrada).
/// </summary>
public enum EstadoConciliacion : short
{
    Abierta = 1,
    Cerrada = 2,
}

/// <summary>
/// Estado de una línea importada del extracto bancario.
/// </summary>
public enum EstadoExtractoLinea : short
{
    Pendiente = 1,
    Emparejada = 2,
    EnRevision = 3,
    EnTransito = 4,
}

/// <summary>
/// Tipo o regla de emparejamiento aplicada a la línea (1..3 auto, 4 manual, 5 alta).
/// </summary>
public enum MatchTipo : short
{
    AutoReferenciaImporte = 1,
    AutoImporteFecha = 2,
    AutoTolerancia = 3,
    Manual = 4,
    AltaAsistida = 5,
}

/// <summary>
/// Fuente de origen del extracto (§5 DDL: 1=Archivo 2=Api).
/// </summary>
public enum ExtractoFuente : short
{
    Archivo = 1,
    Api = 2,
}

/// <summary>
/// Tipo de partida en tránsito permitida (P9.4).
/// </summary>
public enum PartidaTransitoTipo : short
{
    ChequeNoCobrado = 1,
    DepositoUltimoDia = 2,
    Otro = 3,
}
