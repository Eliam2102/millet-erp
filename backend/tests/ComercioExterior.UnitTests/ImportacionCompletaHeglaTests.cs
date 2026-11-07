using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.ComercioExterior.UnitTests;

/// <summary>
/// Validación de punta a punta del expediente real de Importación HEGLA Corporation (OC3040891, Factura INV_260705),
/// flete aéreo FedEx (Guía 870030395514) y Pedimento de Importación 6017757 (Aduana 282 Mérida).
/// </summary>
public sealed class ImportacionCompletaHeglaTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid ProveedorHeglaId = Guid.NewGuid();
    private static readonly Guid AgenteAduanalId = Guid.NewGuid();
    private static readonly Guid FedexId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly FechaHegla = new(2026, 3, 11);
    private static readonly DateOnly FechaEmbarqueFedex = new(2026, 3, 19);
    private static readonly DateOnly FechaEntradaPedimento = new(2026, 8, 13);
    private static readonly DateOnly FechaPagoPedimento = new(2026, 8, 20);

    private static Expediente CrearExpedienteHegla()
    {
        var e = new Expediente(
            Guid.NewGuid(),
            EmpresaId,
            "IMP-HEGLA-3040891",
            TipoExpediente.Importacion,
            SucursalId,
            "Importación aérea refacciones HEGLA (Bomba, Guía lineal, Conexión caucho)",
            Guid.NewGuid(), // OcId 3040891
            Guid.NewGuid(), // IncotermId
            "USD",
            Actor,
            []);
        return e;
    }

    [Fact]
    public void Flujo_Completo_Hegla_Validacion_Transporte_Aereo_FedEx_Y_Agente()
    {
        var e = CrearExpedienteHegla();
        var contenedorId = Guid.NewGuid();

        // 1. Transporte Aéreo FedEx (Guía 870030395514)
        var guiaFedex = new DatosContenedor(
            Numero: "GUIA-870030395514",
            Sello: null,
            Booking: null,
            BlGuia: "870030395514",
            Ruta: "ATLANTA-MERIDA",
            FechaCargaPrevista: FechaEmbarqueFedex,
            PesoKg: 23.60m,
            Bultos: 1,
            Modalidad: ModalidadTransporte.Aereo);
        e.GuardarContenedor(contenedorId, guiaFedex, Actor);

        // Asignar partidas de la OC HEGLA
        var lineaBomba = Guid.NewGuid();
        var lineaGuiaLineal = Guid.NewGuid();
        var lineaCaucho = Guid.NewGuid();

        e.AsignarPartidaImportacion(contenedorId, lineaBomba, 1, 1, 1, "PZA", Actor);
        e.AsignarPartidaImportacion(contenedorId, lineaGuiaLineal, 2, 5, 5, "PZA", Actor);
        e.AsignarPartidaImportacion(contenedorId, lineaCaucho, 3, 6, 6, "PZA", Actor);

        // Sin participantes: falla por falta de Agente Aduanal
        var exSinAgente = Assert.Throws<BusinessRuleException>(() =>
            e.RegistrarEmbarque(contenedorId, FechaEmbarqueFedex, FechaEmbarqueFedex));
        exSinAgente.Code.Should().Be("CE_AGENTE_ADUANAL_REQUERIDO");

        // Agregamos sólo agente aduanal: falla por falta de Aerolínea (FedEx)
        e.AgregarParticipante(AgenteAduanalId, "Agencia Aduanal JVFI", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        var exSinFedex = Assert.Throws<BusinessRuleException>(() =>
            e.RegistrarEmbarque(contenedorId, FechaEmbarqueFedex, FechaEmbarqueFedex));
        exSinFedex.Code.Should().Be("CE_TRANSPORTISTA_REQUERIDO");
        exSinFedex.Message.Should().Contain("línea aérea");

        // Agregamos FedEx Express como transportista aéreo
        e.AgregarParticipante(FedexId, "FedEx Express International", TipoParticipante.Aerolinea, null, PapelParticipante.Captura, null, null);

        // Ahora sí permite registrar el embarque aéreo
        e.RegistrarEmbarque(contenedorId, FechaEmbarqueFedex, FechaEmbarqueFedex);
        e.Contenedor(contenedorId).FechaEmbarqueReal.Should().Be(FechaEmbarqueFedex);
    }

    [Fact]
    public void Aduana_282_Merida_Es_Valida_Y_Queda_Registrada_En_Expediente()
    {
        var e = CrearExpedienteHegla();
        e.RegistrarAduanaEntrada("282");
        e.AduanaEntrada.Should().Be("282");
        CatalogoAduanas.Aduanas["282"].Should().Be("Aeropuerto Internacional de Mérida");
    }

    [Fact]
    public void Checklist_Documental_Bloquea_Solo_La_Etapa_Correspondiente()
    {
        var e = CrearExpedienteHegla();
        var contenedorId = Guid.NewGuid();
        e.GuardarContenedor(contenedorId, new("GUIA-870030395514", null, null, "870030395514", null, null, 23.60m, 1, ModalidadTransporte.Aereo));
        var lineaBomba = Guid.NewGuid();
        e.AsignarPartidaImportacion(contenedorId, lineaBomba, 1, 1, 1, "PZA", Actor);
        e.AgregarParticipante(AgenteAduanalId, "Agente", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(FedexId, "FedEx", TipoParticipante.Aerolinea, null, PapelParticipante.Captura, null, null);
        e.RegistrarEmbarque(contenedorId, FechaEmbarqueFedex, FechaEmbarqueFedex);

        e.InicializarHitos(FechaHegla);
        var evidenciaDummy = Guid.NewGuid();
        var evidenciaFactura = Guid.NewGuid();
        var evidenciaPedimento = Guid.NewGuid();

        var reqFactura = new RequisitoDocumento(Guid.NewGuid(), "IMP_FACTURA", "Factura Hegla INV_260705", true, false, "embarcado");
        var reqPedimento = new RequisitoDocumento(Guid.NewGuid(), "IMP_PEDIMENTO", "Pedimento 6017757", true, false, "liberado");

        // Completar hitos anteriores a embarcado
        var ordenEmbarcado = e.Hitos.Single(h => h.Clave == "embarcado").Orden;
        foreach (var h in e.Hitos.OrderBy(h => h.Orden).Where(h => h.Orden > 1 && h.Orden < ordenEmbarcado))
            e.RegistrarHito(h.Clave, FechaEmbarqueFedex, Actor, evidenciaDummy, null, [], [evidenciaDummy], FechaEmbarqueFedex);

        // Intentar registrar embarcado sin registrar la factura comercial como documento debe fallar con DocumentosFaltantesException
        Assert.Throws<DocumentosFaltantesException>(() =>
            e.RegistrarHito("embarcado", FechaEmbarqueFedex, Actor, null, null, [reqFactura, reqPedimento], [evidenciaDummy], FechaEmbarqueFedex));

        // Registramos la factura comercial en el expediente
        e.RegistrarDocumento(Guid.NewGuid(), reqFactura, evidenciaFactura, "Factura Hegla INV_260705", Actor);

        // Con la factura comercial versionada, embarcado avanza (el pedimento pendiente para liberado NO bloquea embarcado)
        e.RegistrarHito("embarcado", FechaEmbarqueFedex, Actor, evidenciaFactura, null, [reqFactura, reqPedimento], [evidenciaDummy, evidenciaFactura], FechaEmbarqueFedex);
        e.Hitos.Single(h => h.Clave == "embarcado").FechaReal.Should().Be(FechaEmbarqueFedex);

        // Completar hitos intermedios entre embarcado y liberado
        var ordenLiberado = e.Hitos.Single(h => h.Clave == "liberado").Orden;
        foreach (var h in e.Hitos.OrderBy(h => h.Orden).Where(h => h.Orden > ordenEmbarcado && h.Orden < ordenLiberado))
            e.RegistrarHito(h.Clave, FechaPagoPedimento, Actor, evidenciaDummy, null, [reqFactura, reqPedimento], [evidenciaDummy, evidenciaFactura], FechaPagoPedimento);

        // La etapa de liberación exige el pedimento: como aún no está versionado, debe fallar
        Assert.Throws<DocumentosFaltantesException>(() =>
            e.RegistrarHito("liberado", FechaPagoPedimento, Actor, evidenciaDummy, null, [reqFactura, reqPedimento], [evidenciaDummy, evidenciaFactura], FechaPagoPedimento));

        // Registramos el pedimento de importación 6017757
        e.RegistrarDocumento(Guid.NewGuid(), reqPedimento, evidenciaPedimento, "Pedimento 6017757", Actor, "6017757");

        // Con ambos documentos vigentes, la etapa de liberación avanza exitosamente
        e.RegistrarHito("liberado", FechaPagoPedimento, Actor, evidenciaPedimento, null, [reqFactura, reqPedimento], [evidenciaDummy, evidenciaFactura, evidenciaPedimento], FechaPagoPedimento);
        e.Hitos.Single(h => h.Clave == "liberado").FechaReal.Should().Be(FechaPagoPedimento);
    }

    [Fact]
    public void Recepcion_Almacen_Contra_Embarcado_Detecta_Diferencias_Y_Cuadra_Con_Reentrega()
    {
        var e = CrearExpedienteHegla();
        var contenedorId = Guid.NewGuid();
        e.GuardarContenedor(contenedorId, new("GUIA-870030395514", null, null, "870030395514", null, null, 23.60m, 1, ModalidadTransporte.Aereo));
        var lineaGuia = Guid.NewGuid();
        e.AsignarPartidaImportacion(contenedorId, lineaGuia, 2, 5, 5, "PZA", Actor); // 5 piezas embarcadas

        e.AgregarParticipante(AgenteAduanalId, "Agente Aduanal", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(FedexId, "FedEx", TipoParticipante.Aerolinea, null, PapelParticipante.Captura, null, null);
        e.RegistrarEmbarque(contenedorId, FechaEmbarqueFedex, FechaEmbarqueFedex);

        var partidaImportadaId = e.PartidasImportacion.Single().Id;
        var ahora = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

        // Recepción en Almacén recibe solo 4 de 5 piezas
        e.ConciliarRecepcion(Guid.NewGuid(), partidaImportadaId, 4, FechaPagoPedimento, FechaPagoPedimento.AddDays(3), Actor, ahora);

        e.Incidencias.Should().ContainSingle();
        var inc = e.Incidencias.Single();
        inc.Tipo.Should().Be(TipoIncidencia.MercanciaFaltante);
        inc.DiferenciaCantidad.Should().Be(1);
        inc.Automatica.Should().BeTrue();
        Assert.Throws<BusinessRuleException>(e.ExigirSinIncidencias);

        // Se recibe la pieza faltante en almacén (total 5)
        e.ConciliarRecepcion(Guid.NewGuid(), partidaImportadaId, 5, FechaPagoPedimento, FechaPagoPedimento.AddDays(3), Actor, ahora.AddHours(2));

        inc.Estado.Should().Be(EstadoIncidencia.Resuelta);
        inc.Historial.Last().Motivo.Should().Be("Cuadrada por recepción");
        e.ExigirSinIncidencias(); // Desbloqueado
    }

    [Fact]
    public void Prorrateo_Incrementables_Pedimento_6017757_Flete_FedEx_Y_Contribuciones_DTA_IGI()
    {
        var e = CrearExpedienteHegla();

        // Partidas reales del Pedimento 6017757 / Factura Hegla
        // Total Comercial: $199,365 MXN repartido entre las 3 refacciones
        var p1 = new BasePartidaCosto(Guid.NewGuid(), null, "Bomba aceite Hegla", ValorAduanaMxn: 85000m, PesoKg: 6.0m, Cantidad: 1, CostoPromedioMxn: 85000m);
        var p2 = new BasePartidaCosto(Guid.NewGuid(), null, "Guía lineal X-axis Hegla", ValorAduanaMxn: 85400m, PesoKg: 12.0m, Cantidad: 5, CostoPromedioMxn: 17080m);
        var p3 = new BasePartidaCosto(Guid.NewGuid(), null, "Conexión caucho-metal", ValorAduanaMxn: 28965m, PesoKg: 5.6m, Cantidad: 6, CostoPromedioMxn: 4827.50m);
        var partidas = new[] { p1, p2, p3 };

        // Incrementables reales del Pedimento 6017757:
        // Flete FedEx: $1,915.00 MXN (base peso o valor)
        // DTA: $462.00 MXN
        // IGI: $36,954.00 MXN
        var fleteFedex = new ConceptoGasto(EmpresaId, "FLETE_FEDEX", "Flete Internacional FedEx", BaseDistribucion.Peso);
        var gastoDta = new ConceptoGasto(EmpresaId, "DTA", "Derecho de Trámite Aduanero", BaseDistribucion.ValorAduana);
        var gastoIgi = new ConceptoGasto(EmpresaId, "IGI", "Impuesto General de Importación", BaseDistribucion.ValorAduana);

        e.GuardarGasto(Guid.NewGuid(), new(fleteFedex.Id, FedexId, "FEDEX-870030395514", "MXN", 1915.00m, FechaEmbarqueFedex), fleteFedex, 1, "MXN");
        e.GuardarGasto(Guid.NewGuid(), new(gastoDta.Id, AgenteAduanalId, "PED-6017757-DTA", "MXN", 462.00m, FechaPagoPedimento), gastoDta, 1, "MXN");
        e.GuardarGasto(Guid.NewGuid(), new(gastoIgi.Id, AgenteAduanalId, "PED-6017757-IGI", "MXN", 36954.00m, FechaPagoPedimento), gastoIgi, 1, "MXN");

        var distribucion = e.PrepararDistribucion([fleteFedex, gastoDta, gastoIgi], partidas, Actor, DateTimeOffset.UtcNow);

        // Validación de cuadre exacto de incrementables prorrateados:
        // Suma total de fletes distribuidos = exactamente $1,915.00
        distribucion.Renglones.Where(r => r.ConceptoId == fleteFedex.Id).Sum(r => r.ImporteMxn).Should().Be(1915.00m);

        // Suma total de DTA distribuido = exactamente $462.00
        distribucion.Renglones.Where(r => r.ConceptoId == gastoDta.Id).Sum(r => r.ImporteMxn).Should().Be(462.00m);

        // Suma total de IGI distribuido = exactamente $36,954.00
        distribucion.Renglones.Where(r => r.ConceptoId == gastoIgi.Id).Sum(r => r.ImporteMxn).Should().Be(36954.00m);

        // Suma total de gastos distribuidos = $1,915 + $462 + $36,954 = $39,331.00 MXN
        distribucion.Partidas.Sum(p => p.ImporteMxn).Should().Be(39331.00m);
    }

    [Fact]
    public async Task Prorrateo_Bloqueado_Si_Periodo_Contable_Esta_Cerrado()
    {
        var mockPeriodo = new PeriodoCerradoConsultaPort();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ValidacionesFinancieras.PeriodoAbiertoAsync(mockPeriodo, FechaPagoPedimento, CancellationToken.None));

        ex.Code.Should().Be("CE_PERIODO_CERRADO");
        ex.Message.Should().Contain("está cerrado");
    }

    private sealed class PeriodoCerradoConsultaPort : IPeriodoContableConsultaPort
    {
        public Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct) =>
            Task.FromResult(new EstadoPeriodoContable(2026, fecha.Month, Millet.Contabilidad.Domain.EstadoPeriodo.Cerrado, Existe: true));

        public Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct) =>
            Task.FromResult(new EstadoPeriodoContable(anio, numero, Millet.Contabilidad.Domain.EstadoPeriodo.Cerrado, Existe: true));
    }
}
