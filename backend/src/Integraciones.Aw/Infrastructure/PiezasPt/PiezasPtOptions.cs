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
    /// <summary>Estaciones de PT: rangos "a-b" o números sueltos. Solo vive en configuración.</summary>
    public string[] EstacionesPt { get; set; } = [];
    public bool ContratoI134Firmado { get; set; }
}
