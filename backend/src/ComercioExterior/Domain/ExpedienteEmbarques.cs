using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.Domain;
public sealed partial class Expediente
{
    public string? NumeroPedimentoExportacion { get; private set; }
    public string? AduanaPedimentoExportacion { get; private set; }
    public string? ClavePedimento { get; private set; }
    public DateOnly? FechaPedimento { get; private set; }
    public Guid? DocumentoPedimentoExportacionId { get; private set; }
    private readonly List<ContenedorExpediente> _contenedores = [];
    public IReadOnlyCollection<ContenedorExpediente> Contenedores => _contenedores.AsReadOnly();
    private readonly List<PartidaEmbarcada> _partidas = [];
    public IReadOnlyCollection<PartidaEmbarcada> Partidas => _partidas.AsReadOnly();

    private readonly List<VinculoPedidoExpediente> _vinculosPedidos = [];
    public IReadOnlyCollection<VinculoPedidoExpediente> VinculosPedidos => _vinculosPedidos.AsReadOnly();

    private void ExigirExportacion()
    {
        ExigirAbierto();
        if (Tipo != TipoExpediente.Exportacion)
            throw new BusinessRuleException("CE_SOLO_EXPORTACION", "Los contenedores de exportación pertenecen a un expediente de exportación.");
    }
    public ContenedorExpediente Contenedor(Guid id) => _contenedores.SingleOrDefault(x => x.Id == id)
        ?? throw new EntityNotFoundException("CE_CONTENEDOR_NO_ENCONTRADO", "No existe el contenedor en este expediente.");
    public void GuardarContenedor(Guid id, DatosContenedor datos, Guid? actor = null)
    {
        ExigirAbierto();
        var actual = _contenedores.SingleOrDefault(x => x.Id == id);
        if (actual is null) _contenedores.Add(new(id, EmpresaId, Id, datos));
        else
        {
            if (actual.PreparadoParaFacturarEn is not null) actual.ExigirSinPreparacion();
            actual.Editar(datos);
        }
        VersionarEmbarque(actor ?? ResponsableUsuarioId); RevisionContenido++;
    }
    public void RevisarPartidasOtr(IEnumerable<Guid> ids)
    {
        var afectadas = _partidas.Where(p => ids.Contains(p.Id) && p.Estado != EstadoPartida.Liberada).ToArray();
        foreach (var p in afectadas) p.RequerirRevision();
        if (afectadas.Length > 0) VersionarEmbarque(ResponsableUsuarioId);
        if (afectadas.Length > 0) RevisionContenido++;
    }
    public void RegistrarPedimentoExportacion(string numero, string clave, DateOnly fecha, DateOnly hoy, string? aduana, Guid? documentoId = null)
    {
        ExigirExportacion();
        if (aduana is null || !CatalogoAduanas.Aduanas.ContainsKey(aduana))
            throw new BusinessRuleException("CE_ADUANA_INVALIDA", "Seleccione una aduana del catálogo");
        if (clave is not ("A1" or "RT") || fecha == default || fecha > hoy)
            throw new BusinessRuleException("CE_PEDIMENTO_INVALIDO", "Capture número (máximo 100 caracteres), clave A1 o RT y fecha no futura.");
        if (documentoId is Guid id)
        {
            var doc = _documentos.SingleOrDefault(d => d.Id == id && d.Estado == EstadoDocumento.Vigente && !d.NoAplica)
                ?? throw new BusinessRuleException("CE_PEDIMENTO_NO_VIGENTE", "Seleccione la versión vigente del pedimento de exportación.");
            numero = doc.NumeroPedimento ?? throw new BusinessRuleException("CE_PEDIMENTO_NO_VIGENTE", "Capture el número en la versión documental vigente.");
        }
        if (string.IsNullOrWhiteSpace(numero) || numero.Trim().Length > 100)
            throw new BusinessRuleException("CE_PEDIMENTO_INVALIDO", "Capture el número del pedimento (máximo 100 caracteres).");
        NumeroPedimentoExportacion = numero.Trim(); ClavePedimento = clave; FechaPedimento = fecha;
        AduanaPedimentoExportacion = aduana;
        DocumentoPedimentoExportacionId = documentoId;
        RevisionPendiente = true; RevisadoPorId = null; RevisionContenido++;
    }
    public bool PedimentoExportacionVigente(IReadOnlyCollection<Guid>? adjuntosVigentes = null) =>
        !string.IsNullOrWhiteSpace(NumeroPedimentoExportacion) && ClavePedimento is "A1" or "RT" && FechaPedimento is not null &&
        (DocumentoPedimentoExportacionId is null || _documentos.Any(d => d.Id == DocumentoPedimentoExportacionId &&
            d.Estado == EstadoDocumento.Vigente && !d.NoAplica && d.NumeroPedimento == NumeroPedimentoExportacion &&
            d.AdjuntoId is Guid a && (adjuntosVigentes is null || adjuntosVigentes.Contains(a))));

    public void RegistrarEmbarque(Guid id, DateOnly fecha, DateOnly hoy, IReadOnlyCollection<Guid>? adjuntosVigentes = null)
    {
        ExigirAbierto();
        if (Tipo == TipoExpediente.Importacion)
        {
            var c = Contenedor(id);
            ExigirTransporte(c);
            if (!_partidasImportacion.Any(p => p.ContenedorId == id))
                throw new BusinessRuleException("CE_EMBARQUE_SIN_PARTIDAS", "Asigne al menos una partida de la OC antes de embarcar.");
            c.Embarcar(fecha, hoy, permitirGuia: true); RevisionContenido++; return;
        }
        // Por confirmar con Comercio Exterior
        if (!PedimentoExportacionVigente(adjuntosVigentes))
            throw new BusinessRuleException("CE_EMBARQUE_SIN_PEDIMENTO", "Registre un pedimento de exportación vigente con número, clave A1 o RT y fecha.");
        if (!SalidaAutorizada) throw new BusinessRuleException("CE_SALIDA_SIN_AUTORIZAR", "Autorice la salida de la versión actual del embarque antes de registrar su fecha real.");
        Contenedor(id).Embarcar(fecha, hoy);
        RevisionContenido++;
    }
    public void ExigirPedidoAsignable(PedidoEmbarcable pedido)
    {
        ExigirExportacion();
        if (pedido.SucursalId != SucursalId)
            throw new BusinessRuleException("CE_SUCURSAL_DISTINTA", "El pedido y el expediente deben pertenecer a la misma sucursal.");
        if (!pedido.EsExportacion || pedido.Estado != "Importado" ||
            !_participantes.Any(p => p.ClienteId == pedido.ClienteId))
            throw new BusinessRuleException("CE_PEDIDO_NO_ASIGNABLE", "Seleccione un pedido de exportación Importado de esta sucursal y de un cliente vinculado en participantes.");
    }
    public void AsignarPartida(Guid contenedor, PedidoEmbarcable pedido, int posicion, decimal cantidad, Guid actor, bool propuesta = false)
    {
        ExigirPedidoAsignable(pedido);
        var destino = Contenedor(contenedor);
        destino.ExigirActivo();
        if (destino.PreparadoParaFacturarEn is not null) destino.ExigirSinPreparacion();
        var linea = pedido.Lineas.SingleOrDefault(l => l.Posicion == posicion)
            ?? throw new BusinessRuleException("CE_PEDIDO_NO_ASIGNABLE", "La posición ya no existe en el pedido; recargue las líneas de A+W.");
        ExigirCantidad(pedido.Id, linea, cantidad);
        var vinculo = _vinculosPedidos.SingleOrDefault(v => v.PedidoFacturableId == pedido.Id);
        if (vinculo is null) _vinculosPedidos.Add(new(EmpresaId, Id, pedido.Id));
        else vinculo.Activar();
        var nueva = new PartidaEmbarcada(EmpresaId, Id, contenedor, pedido.Id, posicion, pedido.ClienteId, cantidad, linea.Unidad, pedido.VersionOrigen, actor, propuesta);
        VersionarEmbarque(actor); _partidas.Add(nueva);
        RevisionContenido++;
    }
    private bool Activo(PartidaEmbarcada p) => p.Estado != EstadoPartida.Liberada && Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo;
    private void ExigirCantidad(Guid pedido, LineaEmbarcable linea, decimal adicional = 0)
    {
        if (_partidas.Where(p => p.PedidoFacturableId == pedido && p.Posicion == linea.Posicion && Activo(p)).Sum(p => p.Cantidad) + adicional > linea.Cantidad)
            throw new BusinessRuleException("CE_CANTIDAD_EXCEDE_PEDIDO", $"La posición {linea.Posicion} supera la cantidad del pedido ({linea.Cantidad} {linea.Unidad}).");
    }
    public void ConfirmarPartida(Guid partidaId, PedidoEmbarcable pedido, Guid actor)
    {
        ExigirPedidoAsignable(pedido);
        var p = _partidas.SingleOrDefault(x => x.Id == partidaId && x.PedidoFacturableId == pedido.Id)
            ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "No existe la partida.");
        var contenedor = Contenedor(p.ContenedorId);
        contenedor.ExigirActivo();
        if (contenedor.PreparadoParaFacturarEn is not null) contenedor.ExigirSinPreparacion();
        var linea = pedido.Lineas.SingleOrDefault(l => l.Posicion == p.Posicion)
            ?? throw new BusinessRuleException("CE_PEDIDO_NO_ASIGNABLE", "La posición fue eliminada en A+W; retire la partida y asigne de nuevo.");
        ExigirCantidad(pedido.Id, linea);
        p.Confirmar(pedido.VersionOrigen, linea.Unidad, actor);
        VersionarEmbarque(actor);
        RevisionContenido++;
    }
    public void ActualizarRevisiones(IEnumerable<PedidoEmbarcable> pedidos)
    {
        foreach (var pedido in pedidos)
            foreach (var p in _partidas.Where(p => p.PedidoFacturableId == pedido.Id && p.EstadoActual(pedido.VersionOrigen) != p.Estado))
            { p.Revisar(pedido.VersionOrigen); VersionarEmbarque(ResponsableUsuarioId); RevisionContenido++; }
    }
    public void QuitarPartida(Guid id, bool tieneFactura)
    {
        ExigirExportacion();
        var p = _partidas.SingleOrDefault(x => x.Id == id) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "No existe la partida.");
        var contenedor = Contenedor(p.ContenedorId);
        if (contenedor.PreparadoParaFacturarEn is not null) contenedor.ExigirSinPreparacion();
        if (tieneFactura) throw new ConflictException("CE_CONTENEDOR_CON_FACTURA", "Las partidas de un pedido facturado se conservan.");
        _partidas.Remove(p); VersionarEmbarque(ResponsableUsuarioId);
        if (!_partidas.Any(x => x.PedidoFacturableId == p.PedidoFacturableId && x.Estado != EstadoPartida.Liberada))
            _vinculosPedidos.SingleOrDefault(v => v.PedidoFacturableId == p.PedidoFacturableId)?.Liberar();
        RevisionContenido++;
    }
    public void CancelarContenedor(Guid id, string motivo, bool tieneFactura)
    {
        ExigirAbierto();
        if (tieneFactura) throw new ConflictException("CE_CONTENEDOR_CON_FACTURA", "El contenedor tiene partidas con factura vigente; no se puede quitar.");
        Contenedor(id).Cancelar(motivo); VersionarEmbarque(ResponsableUsuarioId); RevisionContenido++;
    }
    /// <summary>
    /// FAC-08: fracción y unidad aduana revisadas por posición del pedido para su factura. Solo cuentan las
    /// partidas de contenedores activos y, con <paramref name="contenedorId"/> (factura de la salida de un
    /// contenedor), solo las de ese contenedor. Si una posición va en varios contenedores, manda la última
    /// revisión: una corrección en CE no se pierde por tomar la primera partida.
    /// </summary>
    public IReadOnlyList<(int Posicion, string FraccionArancelaria, string UnidadAduana)> DatosAduanalesFactura(
        Guid pedidoFacturableId, Guid? contenedorId = null)
    {
        var activos = _contenedores.Where(c => c.Estado == EstadoContenedor.Activo && (contenedorId == null || c.Id == contenedorId))
            .Select(c => c.Id).ToHashSet();
        return _partidas
            .Where(p => p.PedidoFacturableId == pedidoFacturableId && p.Estado != EstadoPartida.Liberada
                && p.AduanalesRevisadoPorId != null && activos.Contains(p.ContenedorId))
            .GroupBy(p => p.Posicion)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderByDescending(p => p.AduanalesRevisadoEn).ThenBy(p => p.ContenedorId).First())
            .Select(p => (p.Posicion, p.FraccionArancelaria!, p.UnidadAduana!))
            .ToArray();
    }

    /// <summary>
    /// Compuerta de facturación del pedido. Con <paramref name="contenedorId"/> (FAC-08, factura de la salida de un
    /// contenedor) basta con que ese contenedor esté embarcado y activo: la salida ya trae lo que lleva el contenedor,
    /// así no se espera al 100 % del pedido para facturar cada contenedor.
    /// </summary>
    public (string? Codigo, string? Motivo) BloqueoFacturacion(PedidoEmbarcable pedido, IReadOnlyCollection<Guid>? adjuntos = null, Guid? contenedorId = null)
    {
        var partidas = _partidas.Where(p => p.PedidoFacturableId == pedido.Id && Activo(p)).ToArray();
        if (partidas.Any(p => p.EstadoActual(pedido.VersionOrigen) == EstadoPartida.RequiereRevision))
            return ("CE_PARTIDA_REQUIERE_REVISION", "Revisión: pedido modificado en A+W. Confirme de nuevo sus partidas en Comercio Exterior.");
        if (Estado != EstadoExpediente.Abierto || !PedimentoExportacionVigente(adjuntos))
            return ("FAC_PEDIDO_NO_EMBARCADO", "El expediente debe estar abierto y tener pedimento de exportación vigente.");
        if (AduanaPedimentoExportacion is null)
            return ("CE_PEDIMENTO_SIN_ADUANA", "Capture la aduana del pedimento de exportación en Comercio Exterior.");
        if (!pedido.EsExportacion || !_participantes.Any(p => p.ClienteId == pedido.ClienteId) || partidas.Any(p => p.ClienteId != pedido.ClienteId || pedido.Lineas.All(l => l.Posicion != p.Posicion)))
            return ("FAC_PEDIDO_NO_EMBARCADO", "El cliente o las posiciones del pedido ya no corresponden al expediente; revise la asignación.");
        if (contenedorId is Guid id)
        {
            var c = _contenedores.SingleOrDefault(x => x.Id == id);
            if (c is null || c.Estado != EstadoContenedor.Activo || (c.FechaEmbarqueReal is null && c.PreparadoParaFacturarEn is null) || !partidas.Any(p => p.ContenedorId == id))
                return ("FAC_PEDIDO_NO_EMBARCADO", "El contenedor aún no se prepara para facturar, se canceló o ya no lleva partidas de este pedido.");
            return BloqueoPreparacionFacturable(partidas.Where(p => p.ContenedorId == id));
        }
        var faltas = pedido.Lineas.Select(l => new { Linea = l, Embarcado = partidas.Where(p => p.Posicion == l.Posicion && p.Unidad == l.Unidad && Contenedor(p.ContenedorId).FechaEmbarqueReal != null).Sum(p => p.Cantidad) })
            .Where(x => x.Embarcado != x.Linea.Cantidad).Select(x => $"posición {x.Linea.Posicion}: embarcado {x.Embarcado} de {x.Linea.Cantidad} {x.Linea.Unidad}").ToArray();
        if (faltas.Length > 0 || pedido.Lineas.Count == 0)
            return ("FAC_PEDIDO_NO_EMBARCADO", "Falta embarcar el 100 % del pedido: " + string.Join("; ", faltas));
        return BloqueoPreparacionFacturable(partidas);
    }

    private (string? Codigo, string? Motivo) BloqueoPreparacionFacturable(IEnumerable<PartidaEmbarcada> partidas)
    {
        if (!SalidaAutorizada) return ("CE_SALIDA_SIN_AUTORIZAR", "Autorice la salida de la versión actual del embarque en Comercio Exterior.");
        if (partidas.Any(p => p.AduanalesRevisadoPorId is null))
            return ("CE_DATOS_ADUANALES_SIN_REVISAR", "Revise la fracción arancelaria y la unidad aduana de todas las partidas en Comercio Exterior antes de emitir la factura.");
        return (null, null);
    }
}
