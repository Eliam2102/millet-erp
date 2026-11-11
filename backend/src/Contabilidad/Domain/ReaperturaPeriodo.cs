using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// Qué se intenta registrar en un periodo reabierto (M-213): lo que el alcance de la reapertura compara. <c>Modulo</c> es el módulo
/// del documento de origen (null si la póliza no lo trae); <c>CuentaCodigos</c> los códigos de las cuentas de las partidas.
/// </summary>
public sealed record ContextoRegistroPoliza(string? TipoClave, Guid? UsuarioId, string? Modulo, IReadOnlyList<string> CuentaCodigos);

/// <summary>
/// Reapertura acotada de un periodo (M-213): al reabrir se declara qué entra (tipos de póliza, cuentas por prefijo, módulos de
/// origen), quién puede registrar, la evidencia y el vencimiento. Cada dimensión declarada restringe (AND); una dimensión vacía no
/// restringe. Vencida (hora del servidor), el periodo vuelve a estar cerrado para todo movimiento; un proceso formaliza el cierre
/// y deja la bitácora. <see cref="Vigente"/> es false cuando el periodo se volvió a cerrar (por usuario o por vencimiento).
/// </summary>
public sealed class ReaperturaPeriodo : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int EvidenciaMinimo = 5, EvidenciaMaximo = 300;
    public const int MaxElementos = 50;

    public Guid EmpresaId { get; set; }
    public Guid PeriodoId { get; private set; }
    public int Anio { get; private set; }
    public int Numero { get; private set; }
    public string[] TiposPoliza { get; private set; } = [];
    public string[] PrefijosCuenta { get; private set; } = [];
    public string[] Modulos { get; private set; } = [];
    public Guid[] UsuariosIds { get; private set; } = [];
    /// <summary>Referencia de la evidencia (oficio, solicitud). Los archivos se adjuntan por el expediente G1.2.</summary>
    public string EvidenciaReferencia { get; private set; } = string.Empty;
    public DateTimeOffset VenceEn { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public string CreadaPor { get; private set; } = string.Empty;
    public bool Vigente { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }

    /// <summary>Sin ninguna dimensión declarada la reapertura no restringe el alcance (solo tiene vencimiento).</summary>
    public bool TieneAlcance => TiposPoliza.Length + PrefijosCuenta.Length + Modulos.Length + UsuariosIds.Length > 0;

    private ReaperturaPeriodo() { }

    public ReaperturaPeriodo(Guid id, PeriodoContable periodo, IEnumerable<string>? tiposPoliza, IEnumerable<string>? prefijosCuenta,
        IEnumerable<string>? modulos, IEnumerable<Guid>? usuariosIds, string? evidencia, DateTimeOffset venceEn, int maximoDias,
        string creadaPor, DateTimeOffset ahora) : base(id)
    {
        if (evidencia is null || evidencia.Trim().Length is < EvidenciaMinimo or > EvidenciaMaximo)
            throw new BusinessRuleException("CONTAB_REAPERTURA_EVIDENCIA_INVALIDA",
                $"Indique la referencia de la evidencia que respalda la reapertura (entre {EvidenciaMinimo} y {EvidenciaMaximo} caracteres).");
        if (venceEn <= ahora)
            throw new BusinessRuleException("CONTAB_REAPERTURA_VENCIMIENTO_INVALIDO", "El vencimiento de la reapertura debe ser posterior a este momento.");
        if (maximoDias > 0 && venceEn > ahora.AddDays(maximoDias))
            throw new BusinessRuleException("CONTAB_REAPERTURA_VENCIMIENTO_EXCEDE",
                $"La reapertura puede durar como máximo {maximoDias} días.");
        PeriodoId = periodo.Id;
        Anio = periodo.Anio;
        Numero = periodo.Numero;
        TiposPoliza = Normalizar(tiposPoliza, true, "tipos de póliza");
        PrefijosCuenta = Normalizar(prefijosCuenta, false, "prefijos de cuenta");
        Modulos = Normalizar(modulos, true, "módulos");
        UsuariosIds = [.. (usuariosIds ?? []).Distinct()];
        if (UsuariosIds.Length > MaxElementos)
            throw new BusinessRuleException("CONTAB_REAPERTURA_ALCANCE_INVALIDO", $"Se admiten hasta {MaxElementos} usuarios.");
        EvidenciaReferencia = evidencia.Trim();
        VenceEn = venceEn;
        CreadaEn = ahora;
        CreadaPor = creadaPor.Length > 256 ? creadaPor[..256] : creadaPor;
        Vigente = true;
    }

    public bool Vencida(DateTimeOffset ahora) => Vigente && ahora >= VenceEn;

    /// <summary>El periodo se volvió a cerrar (por usuario o por vencimiento): la reapertura deja de aplicar.</summary>
    public void Concluir(DateTimeOffset ahora)
    {
        Vigente = false;
        CerradaEn = ahora;
    }

    /// <summary>
    /// null si el registro está dentro del alcance. Con <paramref name="contexto"/> null (operación que no informa qué registra) y
    /// alcance declarado, falla cerrado.
    /// </summary>
    public BusinessRuleException? Rechazo(ContextoRegistroPoliza? contexto)
    {
        if (!TieneAlcance) return null;
        var clave = PeriodoContable.Etiqueta(Anio, Numero);
        var faltas = new List<string>();
        if (contexto is null)
            faltas.Add("no informa qué se registra");
        else
        {
            if (TiposPoliza.Length > 0 && (contexto.TipoClave is null || !TiposPoliza.Contains(contexto.TipoClave.Trim().ToUpperInvariant())))
                faltas.Add($"solo admite pólizas de tipo {string.Join(", ", TiposPoliza)}");
            if (UsuariosIds.Length > 0 && (contexto.UsuarioId is not { } u || !UsuariosIds.Contains(u)))
                faltas.Add("solo pueden registrar los usuarios autorizados en la reapertura");
            if (Modulos.Length > 0 && (contexto.Modulo is null || !Modulos.Contains(contexto.Modulo.Trim().ToUpperInvariant())))
                faltas.Add($"solo admite documentos de los módulos {string.Join(", ", Modulos)}");
            if (PrefijosCuenta.Length > 0
                && (contexto.CuentaCodigos.Count == 0 || contexto.CuentaCodigos.Any(c => !PrefijosCuenta.Any(p => c.StartsWith(p, StringComparison.Ordinal)))))
                faltas.Add($"solo admite cuentas que empiecen con {string.Join(", ", PrefijosCuenta)}");
        }
        return faltas.Count == 0
            ? null
            : new BusinessRuleException("CONTAB_PERIODO_REAPERTURA_FUERA_DE_ALCANCE",
                $"El periodo {clave} está reabierto solo para un alcance acotado: {string.Join("; ", faltas)}.");
    }

    private static string[] Normalizar(IEnumerable<string>? valores, bool mayusculas, string nombre)
    {
        var lista = (valores ?? []).Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0)
            .Select(v => mayusculas ? v.ToUpperInvariant() : v).Distinct(StringComparer.Ordinal).ToArray();
        if (lista.Length > MaxElementos || lista.Any(v => v.Length > 40))
            throw new BusinessRuleException("CONTAB_REAPERTURA_ALCANCE_INVALIDO", $"Los {nombre} admiten hasta {MaxElementos} elementos de 40 caracteres.");
        return lista;
    }
}
