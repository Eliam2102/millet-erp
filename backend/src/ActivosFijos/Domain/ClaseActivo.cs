using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Domain;

public sealed class ClaseActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Clave { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public Guid? CuentaActivoId { get; private set; }
    public Guid? CuentaDepreciacionId { get; private set; }
    public Guid? CuentaGastoId { get; private set; }
    public Guid? CuentaPuenteId { get; private set; }
    public decimal? TasaContableAnual { get; private set; }
    public decimal? TasaFiscalAnual { get; private set; }
    public bool Depreciable { get; private set; } = true;
    public bool TasaConfirmada { get; private set; }
    public bool RequiereRevision { get; private set; }
    public string Metodo { get; private set; } = "LineaRecta";
    public bool Activa { get; private set; } = true;
    public List<VersionClaseActivo> Versiones { get; private set; } = [];
    public bool PropuestaPendiente => Versiones.Any(v => v.Estado == EstadoVersionClase.EnRevision);
    public DateOnly? ProximaVersionDesde { get; private set; }
    public ClaseActivo EnMes(DateOnly mes)
    {
        var v = Versiones.Where(v => v.Estado == EstadoVersionClase.Vigente && v.Vigencia <= CalculoDepreciacion.Mes(mes))
            .OrderByDescending(v => v.Vigencia).ThenByDescending(v => v.Numero).FirstOrDefault();
        var copia = (ClaseActivo)MemberwiseClone();
        copia.ProximaVersionDesde = Versiones.Where(v => v.Estado == EstadoVersionClase.EnRevision || v.Estado == EstadoVersionClase.Vigente && v.Vigencia > CalculoDepreciacion.Mes(mes))
            .OrderBy(v => v.Estado == EstadoVersionClase.EnRevision ? 0 : 1).ThenBy(v => v.Vigencia).Select(v => (DateOnly?)v.Vigencia).FirstOrDefault();
        if (v != null) copia.Editar(Nombre, v.TasaFiscalAnual, v.CuentaActivoId, v.CuentaDepreciacionId, v.CuentaGastoId, v.CuentaPuenteId, Activa, v.Depreciable, v.TasaConfirmada, v.TasaContableAnual);
        return copia;
    }
    public void EditarIdentidad(string nombre, bool activa)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 200) throw new BusinessRuleException("AF_CLASE_INVALIDA", "Capture un nombre de hasta 200 caracteres.");
        Nombre=nombre.Trim(); Activa=activa;
    }
    private ClaseActivo() { }
    public ClaseActivo(Guid id, Guid empresa, string clave, string nombre, decimal? tasa) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20 || !clave.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new BusinessRuleException("AF_CLAVE_INVALIDA", "La clave debe tener de 1 a 20 letras, números o guiones.");
        EmpresaId = empresa; Clave = clave.ToUpperInvariant();
        Editar(nombre, tasa, null, null, null, null, true);
    }
    public void Editar(string nombre, decimal? tasa, Guid? activo, Guid? depreciacion, Guid? gasto, Guid? puente, bool activa, bool depreciable = true, bool tasaConfirmada = false) =>
        Editar(nombre, tasa, activo, depreciacion, gasto, puente, activa, depreciable, tasaConfirmada, tasa);
    public void Editar(string nombre, decimal? tasa, Guid? activo, Guid? depreciacion, Guid? gasto, Guid? puente, bool activa, bool depreciable, bool tasaConfirmada, decimal? tasaContable)
    {
        if ((tasaConfirmada && tasa == null && Clave is not ("151" or "159" or "160" or "170")) || string.IsNullOrWhiteSpace(nombre) || nombre.Length > 200 || tasa is < 0 or > 100 || tasaContable is < 0 or > 100 || activo == Guid.Empty || depreciacion == Guid.Empty || gasto == Guid.Empty || puente == Guid.Empty)
            throw new BusinessRuleException("AF_CLASE_INVALIDA", "Capture nombre, tasa de 0 a 100 y cuentas válidas.");
        Nombre = nombre.Trim(); TasaFiscalAnual = tasa; TasaContableAnual = tasaContable; CuentaActivoId = activo; CuentaDepreciacionId = depreciacion;
        CuentaGastoId = gasto; CuentaPuenteId = puente; Activa = activa; Depreciable = depreciable; TasaConfirmada = tasaConfirmada;
    }
}
