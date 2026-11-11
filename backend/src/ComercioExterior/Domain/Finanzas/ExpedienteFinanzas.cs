using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed partial class Expediente
{
    private readonly List<GastoExpediente> _gastos = [];
    public IReadOnlyCollection<GastoExpediente> Gastos => _gastos.AsReadOnly();
    private readonly List<PartidaCosto> _partidasCosto = [];
    public IReadOnlyCollection<PartidaCosto> PartidasCosto => _partidasCosto.AsReadOnly();
    private readonly List<DistribucionGasto> _distribuciones = [];
    public IReadOnlyCollection<DistribucionGasto> Distribuciones => _distribuciones.AsReadOnly();
    private readonly List<AnticipoAgente> _anticipos = [];
    public IReadOnlyCollection<AnticipoAgente> Anticipos => _anticipos.AsReadOnly();
    public int RevisionFinanciera { get; private set; }
    public bool FinancieroCerrado { get; private set; }
    public string EstadoFinanciero => MotivosFinancieros().Count > 0 ? string.Join("; ", MotivosFinancieros()) : FinancieroCerrado ? "Cerrado" : "Listo para cierre";
    public IReadOnlyList<string> MotivosFinancieros()
    {
        List<string> motivos = [];
        if (Tipo == TipoExpediente.Importacion && _gastos.Any(g => !g.ExcluidoDelCosto) &&
            _distribuciones.OrderByDescending(d => d.NumeroVersion).FirstOrDefault()?.RevisionFinanciera != RevisionFinanciera)
            motivos.Add("Gastos sin distribuir");
        // M-327: el anticipo revertido se comprueba en Cuentas por pagar compensando su saldo a favor contra las facturas del agente.
        if (_anticipos.Any(a => a.MotivoCancelacion is null && a.Aplicado is null && !a.ComprobacionRevertida)) motivos.Add("Anticipos sin comprobar");
        if (_anticipos.Any(a => a.ReversaPendiente is not null)) motivos.Add("Reversa de comprobación por autorizar");
        if (_anticipos.Any(a => a.MotivoCancelacion is null && a.Saldo > 0)) motivos.Add("Saldo a favor abierto con el agente");
        if (_anticipos.Any(a => a.MotivoCancelacion is null && a.Saldo < 0)) motivos.Add("Por pagar al agente");
        return motivos;
    }
    public void MarcarCambioFinanciero(bool requiereDistribuir = false)
    {
        ExigirAbierto(); FinancieroCerrado = false; RevisionContenido++;
        if (requiereDistribuir) RevisionFinanciera++;
    }
    public void GuardarGasto(Guid id, DatosGasto datos, ConceptoGasto concepto, decimal tc, string fuente, OrdenServicioCopia? orden = null, decimal? sugerido = null, string? nota = null, Guid? usuario = null)
    {
        ExigirAbierto();
        if (concepto.EmpresaId != EmpresaId) throw new BusinessRuleException("CE_CONCEPTO_INVALIDO", "El concepto no pertenece a la empresa.");
        ValidarContenedorCosto(datos.ContenedorId);
        var gasto = _gastos.SingleOrDefault(g => g.Id == id);
        var nuevo = gasto is null;
        gasto ??= new(id, EmpresaId, Id);
        gasto.Editar(datos, concepto, tc, fuente, orden, sugerido, nota, usuario);
        if (nuevo) _gastos.Add(gasto);
        if (orden is not null)
            foreach (var ligado in _gastos.Where(g => g.OrdenServicioId == orden.Id)) ligado.AsignarOrdenServicio(orden.Asignado);
        MarcarCambioFinanciero(true);
    }
    private void ValidarContenedorCosto(Guid? contenedor)
    {
        if (contenedor is Guid c && !_contenedores.Any(x => x.Id == c && x.Estado == EstadoContenedor.Activo))
            throw new BusinessRuleException("CE_CONTENEDOR_NO_ENCONTRADO", "Seleccione un contenedor activo del expediente.");
    }
    public void GuardarPartidaCosto(Guid id, Guid? contenedor, string descripcion, decimal valor, decimal peso, string pedimento)
    {
        ExigirAbierto(); ValidarContenedorCosto(contenedor);
        var partida = _partidasCosto.SingleOrDefault(p => p.Id == id);
        var nueva = partida is null;
        partida ??= new(id, EmpresaId, Id);
        partida.Editar(contenedor, descripcion, valor, peso, pedimento);
        if (nueva) _partidasCosto.Add(partida);
        MarcarCambioFinanciero(true);
    }
    /// <param name="incrementablesAlInventario">Fernando 4-nov: false (configuración por omisión) = todo el ajuste de importación va a
    /// gasto y el costo unitario no cambia; true = reparto a inventario de Millet 26-oct (versión posterior).</param>
    /// <param name="proveedoresPersonaFisica">M-169: proveedores de los gastos que son persona física (honorarios aduanales PF).</param>
    public DistribucionGasto PrepararDistribucion(IReadOnlyList<ConceptoGasto> conceptos, IReadOnlyList<BasePartidaCosto> partidas, Guid usuario, DateTimeOffset fecha,
        bool incrementablesAlInventario = true, IReadOnlySet<Guid>? proveedoresPersonaFisica = null)
    {
        ExigirAbierto();
        if (usuario == Guid.Empty || partidas.Select(p => p.PartidaId).Distinct().Count() != partidas.Count || partidas.Any(p => p.Cantidad <= 0))
            throw new BusinessRuleException("CE_PARTIDA_COSTO_INVALIDA", "Las partidas deben ser únicas y tener cantidad positiva.");
        if (_gastos.Count == 0) throw new BusinessRuleException("CE_GASTOS_FALTANTES", "Registre gastos antes de distribuir.");
        var anterior = _distribuciones.OrderByDescending(d => d.NumeroVersion).FirstOrDefault();
        var renglones = ProrrateoGastos.Calcular(Tipo, _gastos, conceptos, partidas, proveedoresPersonaFisica);
        var totales = renglones.GroupBy(r => r.PartidaId).ToDictionary(g => g.Key, g => g.Sum(r => r.ImporteMxn));
        if (anterior?.Partidas.Any(p => !partidas.Any(b => b.PartidaId == p.PartidaId)) == true)
            throw new BusinessRuleException("CE_PARTIDA_DISTRIBUIDA", "No se puede retirar una partida con distribución previa.");
        var costos = partidas.Select(p =>
        {
            var total = totales.GetValueOrDefault(p.PartidaId);
            var delta = total - (anterior?.Partidas.SingleOrDefault(a => a.PartidaId == p.PartidaId)?.ImporteMxn ?? 0);
            // Millet, 26-oct-2026, Opción A: con salidas, todo el ajuste va a gastos de importación.
            var tieneSalidas = Tipo == TipoExpediente.Importacion && p.Existencia < p.Cantidad;
            var inventario = Tipo == TipoExpediente.Importacion && !tieneSalidas && incrementablesAlInventario ? delta : 0;
            return new CostoPartidaDistribuido(p.PartidaId, p.Descripcion, p.ClienteId, p.OcId, p.RecepcionId,
                p.ArticuloId, p.UbicacionId, p.Cantidad, total, delta, inventario, Tipo == TipoExpediente.Importacion ? delta - inventario : 0,
                p.CostoPromedioMxn, p.CostoPromedioMxn + (p.ExistenciaTotal > 0 ? inventario / p.ExistenciaTotal : 0))
                { TieneSalidas = tieneSalidas };
        }).ToArray();
        if (Tipo == TipoExpediente.Importacion)
            costos = costos.Select(c =>
            {
                var existencia = partidas.Single(p => p.PartidaId == c.PartidaId).ExistenciaTotal;
                var ajusteCompartido = costos.Where(x => x.PartidaId == c.PartidaId || (c.ArticuloId != null && c.UbicacionId != null && x.ArticuloId == c.ArticuloId && x.UbicacionId == c.UbicacionId)).Sum(x => x.InventarioMxn);
                return c with { CostoUnitarioDespues = c.CostoUnitarioAntes + (existencia > 0 ? ajusteCompartido / existencia : 0) };
            }).ToArray();
        return new(EmpresaId, Id, (anterior?.NumeroVersion ?? 0) + 1, RevisionFinanciera, usuario, fecha, renglones, costos)
        {
            GastosPorGrupo = Tipo == TipoExpediente.Importacion ? AjustePorGrupo(renglones, anterior, conceptos, proveedoresPersonaFisica) : [],
        };
    }
    /// <summary>
    /// M-169: ajuste por grupo contable = renglones de esta versión menos los de la anterior (misma regla que el ajuste por partida, así
    /// que suma lo mismo). Un renglón de una versión previa a M-169 no trae grupo: se toma el del concepto y el proveedor actuales.
    /// Si Contabilidad movió el concepto de grupo, la nueva versión reclasifica (un grupo baja y el otro sube).
    /// </summary>
    private List<GastoGrupoDistribuido> AjustePorGrupo(IReadOnlyList<RenglonDistribucion> renglones, DistribucionGasto? anterior,
        IReadOnlyList<ConceptoGasto> conceptos, IReadOnlySet<Guid>? proveedoresPersonaFisica)
    {
        string Grupo(RenglonDistribucion r)
        {
            if (r.Grupo is not null) return r.Grupo;
            var concepto = conceptos.SingleOrDefault(c => c.Id == r.ConceptoId);
            var proveedor = _gastos.SingleOrDefault(g => g.Id == r.GastoId)?.ProveedorId;
            return GrupoGastoImportacion.Codigo(concepto?.GrupoContable ?? GrupoContableGasto.OtrosGastosImportacion,
                proveedor is Guid p && proveedoresPersonaFisica?.Contains(p) == true);
        }
        return renglones.Select(r => (Grupo: Grupo(r), r.ImporteMxn))
            .Concat((anterior?.Renglones ?? []).Select(r => (Grupo: Grupo(r), ImporteMxn: -r.ImporteMxn)))
            .GroupBy(x => x.Grupo, StringComparer.Ordinal).Select(g => new GastoGrupoDistribuido(g.Key, g.Sum(x => x.ImporteMxn)))
            .Where(g => g.ImporteMxn != 0).OrderBy(g => g.Grupo, StringComparer.Ordinal).ToList();
    }
    public void ConfirmarDistribucion(DistribucionGasto distribucion)
    {
        ExigirAbierto();
        if (distribucion.ExpedienteId != Id || distribucion.RevisionFinanciera != RevisionFinanciera || distribucion.NumeroVersion != _distribuciones.Count + 1)
            throw new BusinessRuleException("CE_DISTRIBUCION_OBSOLETA", "Vuelva a consultar la vista previa.");
        _distribuciones.Add(distribucion); MarcarCambioFinanciero();
    }
    public AnticipoAgente RegistrarAnticipo(Guid id, Guid agente, string pedimento, ImpuestosPedimento impuestos, DateOnly fecha, string? referencia, bool cuentaPeca = false)
    {
        ExigirAbierto();
        var anticipo = new AnticipoAgente(id, EmpresaId, Id, agente, pedimento, impuestos, fecha, referencia, cuentaPeca);
        _anticipos.Add(anticipo); MarcarCambioFinanciero(); return anticipo;
    }
}
