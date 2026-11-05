using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;
public enum EstadoVersionClase { EnRevision, Vigente, Rechazada }
public sealed class VersionClaseActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ClaseId { get; set; }
    public int Numero { get; set; }
    public DateOnly Vigencia { get; set; }
    public EstadoVersionClase Estado { get; private set; }
    public Guid PropuestoPor { get; set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public decimal? TasaContableAnual { get; set; }
    public decimal? TasaFiscalAnual { get; set; }
    public Guid? CuentaActivoId { get; set; }
    public Guid? CuentaDepreciacionId { get; set; }
    public Guid? CuentaGastoId { get; set; }
    public Guid? CuentaPuenteId { get; set; }
    public bool Depreciable { get; set; }
    public bool TasaConfirmada { get; set; }
    public VersionClaseActivo() : base(Guid.CreateVersion7()) { }
    public void ValidarVigencia(DateOnly? ultimoMes)
    {
        if (Vigencia == default || Vigencia.Day != 1 || (ultimoMes != null && Vigencia <= ultimoMes))
            throw new BusinessRuleException("AF_VIGENCIA_INVALIDA", "Seleccione el primer día de un mes posterior al último mes con corrida confirmada.");
    }
    public void Resolver(Guid actor, bool autorizar, string? motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoVersionClase.EnRevision) throw new BusinessRuleException("AF_VERSION_RESUELTA", "La propuesta ya fue resuelta.");
        if (actor == Guid.Empty || actor == PropuestoPor) throw new BusinessRuleException("AF_AUTORIZADOR_DISTINTO", "La propuesta debe resolverla otra persona.");
        if (!autorizar && (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)) throw new BusinessRuleException("AF_MOTIVO_REQUERIDO", "Capture un motivo de rechazo de hasta 500 caracteres.");
        Estado = autorizar ? EstadoVersionClase.Vigente : EstadoVersionClase.Rechazada;
        ResueltoPor = actor; ResueltoEn = ahora; MotivoRechazo = autorizar ? null : motivo!.Trim();
    }
    public static VersionClaseActivo Desde(ClaseActivo c, int numero, DateOnly vigencia, Guid actor, bool inicial = false) => new()
    {
        EmpresaId=c.EmpresaId, ClaseId=c.Id, Numero=numero, Vigencia=vigencia, PropuestoPor=actor,
        Estado=inicial ? EstadoVersionClase.Vigente : EstadoVersionClase.EnRevision,
        TasaContableAnual=c.TasaContableAnual, TasaFiscalAnual=c.TasaFiscalAnual, CuentaActivoId=c.CuentaActivoId,
        CuentaDepreciacionId=c.CuentaDepreciacionId, CuentaGastoId=c.CuentaGastoId, CuentaPuenteId=c.CuentaPuenteId,
        Depreciable=c.Depreciable,TasaConfirmada=c.TasaConfirmada
    };
}
public sealed class EquivalenciaClaseSap : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string ClaseSap { get; set; } = "";
    public string ClaveErp { get; set; } = "";
    public EquivalenciaClaseSap() : base(Guid.CreateVersion7()) { }
    public static readonly (string Sap, string Erp)[] Iniciales = [("AUT. CAMIONES","154"),("VEHICULOS MENORES","154"),("EDIFICIOS","152"),("MAQ Y EQUI","153"),("MOB Y EQUIPO OFI","155"),("EQUI. COMPUTO","156"),("EQ. COMUNICACION","157")];
}
