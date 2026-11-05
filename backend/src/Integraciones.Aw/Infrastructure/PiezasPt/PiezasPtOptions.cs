namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>Opciones de la entrada de pieza terminada (PT-01). Apagada por omisión.</summary>
public sealed class PiezasPtOptions
{
    public const string SectionName = "IntegracionesAw:PiezasPt";
    public bool EntradaHabilitada { get; set; }
    public Guid? EmpresaId { get; set; }
    public int IntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 200;
    public int MaxIntentos { get; set; } = 5;
    public int PospuestoHoras { get; set; } = 24;
    public int TraslapeMinutos { get; set; } = 10;
    /// <summary>Cada cuántos minutos se reevalúan los pedidos retenidos (PT5.2, hasta que Facturación publique el cambio de saldo).</summary>
    public int BarridoMinutos { get; set; } = 15;
    /// <summary>Estaciones de PT: rangos "a-b" o números sueltos. Solo vive en configuración.</summary>
    public string[] EstacionesPt { get; set; } = [];
    public bool ContratoI134Firmado { get; set; }
    /// <summary>A4.8: descuento de componentes (argón, separador, PVB) por composición de cada pieza terminada. Apagado por omisión.</summary>
    public bool ConsumoComponentesHabilitado { get; set; }
    /// <summary>PT5.10: lectura de pedidos internos Serie 9000 para descargar el inventario del taller. Apagado por omisión.</summary>
    public bool Serie9000Habilitada { get; set; }
}
