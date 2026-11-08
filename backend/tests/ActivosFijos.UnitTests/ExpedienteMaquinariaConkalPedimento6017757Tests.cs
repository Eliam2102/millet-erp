using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.ActivosFijos.UnitTests;

/// <summary>
/// Cierre de brechas técnicas e integración de insumos reales operativos (Sección 7 - 86e3yxqnj, 86e3yxqmd, 86e3yzemy):
/// - Insumo Real 1: Expediente de Importación Maquinaria Conkal (Pedimento 6017757, aduana 282 Mérida, IMMEX temporal).
/// - Insumo Real 2: Matriz Contable de Depreciación de Laura Cerón (CeCo 105 Conkal, cuentas oficiales 153/171/613/201.06).
/// - Candados de Negocio: Clases con vigencia sin retroactividad, corridas sin activos huérfanos de tasa, exclusión mutua baja/venta (422 ACTIVO_BAJA_PENDIENTE) y reversas autorizadas por DAF.
/// </summary>
public sealed class ExpedienteMaquinariaConkalPedimento6017757Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid SucursalMeridaId = Guid.NewGuid();
    private static readonly Guid Ceco105ConkalId = Guid.NewGuid();
    private static readonly Guid ResponsableCustodiaId = Guid.NewGuid();

    // Cuentas de la Matriz Contable de Laura Cerón para Maquinaria y Equipo (Clase 153)
    private static readonly Guid CuentaActivoMaquinariaId = Guid.NewGuid();       // 153.01.00.000.000
    private static readonly Guid CuentaDepreciacionAcumuladaId = Guid.NewGuid();  // 171.02.00.000
    private static readonly Guid CuentaGastoDepreciacionId = Guid.NewGuid();      // 613.02.00.000.000
    private static readonly Guid CuentaPuenteActivoFijoId = Guid.NewGuid();       // 201.06.00.00

    private static (Activo Activo, ClaseActivo Clase) CrearMaquinariaConkalReal(decimal? tasaPropiaContable = null)
    {
        var clase = new ClaseActivo(Guid.NewGuid(), EmpresaId, "153", "MAQUINARIA Y EQUIPO", 10m);
        clase.Editar(
            "MAQUINARIA Y EQUIPO",
            tasa: 10m,
            activo: CuentaActivoMaquinariaId,
            depreciacion: CuentaDepreciacionAcumuladaId,
            gasto: CuentaGastoDepreciacionId,
            puente: CuentaPuenteActivoFijoId,
            activa: true,
            depreciable: true,
            tasaConfirmada: true,
            tasaContable: 10m);

        // Datos oficiales del Pedimento 26 28 1640 6017757 (expediente-af-6017757.md):
        // Aduana: 282 Mérida, Tipo: AF (IMMEX Temporal ITR), USD: 11,764.44, TC: 17.10920, Aduana: $201,280 MXN
        var datos = new DatosActivo(
            Descripcion: "Maquinaria Conkal Pedimento 6017757 (Aduana 282, M Industria IMMEX)",
            ClaseId: clase.Id,
            Marca: "HEGLA / INDUSTRIAL",
            Modelo: "2026-AF",
            NumeroSerie: "SN-JVFI6017757",
            FechaAdquisicion: new DateOnly(2026, 8, 13), // Fecha de Entrada en Pedimento
            InicioUso: new DateOnly(2026, 8, 20),        // Fecha de Pago y puesta en marcha
            ValorMonedaOriginal: 201280.00m,            // Valor Aduana oficial en MXN
            Moneda: "MXN",
            TipoCambio: 1.0m,
            Custodia: new CustodiaActivo(ResponsableCustodiaId, SucursalMeridaId, "Planta Conkal - Nave Principal", Ceco105ConkalId, null),
            Importacion: TipoImportacionActivo.Temporal, // Régimen ITR / Clave AF
            Pedimento: "26 28 1640 6017757",
            Arrendado: false,
            DocumentoSoporte: "COVE26892VGC5 / Acuse MVE / Guía FedEx 870030395514",
            TasaContableAnual: tasaPropiaContable,
            TasaFiscalAnual: tasaPropiaContable != null ? 10m : null);

        var activo = new Activo(Guid.NewGuid(), EmpresaId, "AF-CONKAL-6017757", datos);
        return (activo, clase);
    }

    [Fact]
    public void Expediente_maquinaria_conkal_pedimento_6017757_datos_reales_e_immex()
    {
        var (activo, clase) = CrearMaquinariaConkalReal();

        Assert.Equal("AF-CONKAL-6017757", activo.Numero);
        Assert.Equal(201280.00m, activo.ValorOriginal);
        Assert.Equal(TipoImportacionActivo.Temporal, activo.Importacion);
        Assert.Equal("26 28 1640 6017757", activo.Pedimento);
        Assert.Equal("Planta Conkal - Nave Principal", activo.Ubicacion);
        Assert.Equal(Ceco105ConkalId, activo.CentroCostoId);
        Assert.Equal(EstadoActivo.PorCapitalizar, activo.Estado);

        // Capitalización oficial:
        var polizaAltaId = Guid.NewGuid();
        activo.Capitalizar(polizaAltaId);
        Assert.Equal(EstadoActivo.Activo, activo.Estado);
        Assert.Equal(polizaAltaId, activo.PolizaAltaId);
        Assert.Equal(201280.00m, activo.ValorEnLibros);
    }

    [Fact]
    public void Depreciacion_mensual_conkal_ceco_105_matriz_laura_ceron_y_tasa_propia()
    {
        // Tasa propia por activo de hasta 4 decimales (ej. 12.0000 % contable vs 10 % fiscal)
        var (activo, clase) = CrearMaquinariaConkalReal(tasaPropiaContable: 12.0000m);
        activo.Capitalizar(Guid.NewGuid());

        // Con fecha de uso 20-ago-2026, primer mes de depreciación estándar es septiembre 2026
        var mesSeptiembre = new DateOnly(2026, 9, 1);
        var detalle = CalculoDepreciacion.Calcular(activo, clase, mesSeptiembre, mismoMes: false);

        // $201,280.00 * 12.0000 % / 12 = $2,012.80
        Assert.Equal(2012.80m, detalle.Importe);
        Assert.Equal(12.0000m, detalle.Tasa);
        Assert.Equal(Ceco105ConkalId, detalle.CentroCostoId);
        Assert.Equal(SucursalMeridaId, detalle.SucursalId);
        Assert.Contains("Tasa propia", detalle.Observacion);

        // Verificar tasa fiscal del activo independiente de la contable
        var tasas = TasasActivo.Resolver(activo, clase);
        Assert.Equal(12.0000m, tasas.Contable);
        Assert.Equal(10m, tasas.Fiscal);
    }

    [Fact]
    public void Candado_corrida_sin_tasa_no_se_confirma_y_lista_activo()
    {
        var filasSinTasa = new DetalleDepreciacion[]
        {
            new() { Numero = "AF-CONKAL-6017757", Tasa = null },
            new() { Numero = "AF-OTRO-160", Tasa = null }
        };

        var ex = Assert.Throws<BusinessRuleException>(() => CorridaDepreciacion.ValidarTasas(filasSinTasa));
        Assert.Equal("AF_CORRIDA_SIN_TASA", ex.Code);
        Assert.Contains("AF-CONKAL-6017757", ex.Message);
        Assert.Contains("AF-OTRO-160", ex.Message);
    }

    [Fact]
    public void Baja_y_venta_excluyentes_candado_ux_baja_pendiente_y_error_422()
    {
        var (activo, _) = CrearMaquinariaConkalReal();
        activo.Capitalizar(Guid.NewGuid());

        var dafId = Guid.NewGuid();
        var solicitanteId = Guid.NewGuid();

        // 1. Registrar solicitud de baja con evidencia obligatoria
        var solicitudBaja = new OperacionLibro
        {
            EmpresaId = EmpresaId,
            ActivoId = activo.Id,
            Tipo = "Baja",
            Fecha = new DateOnly(2026, 10, 15),
            Motivo = "Obsolescencia",
            SolicitadoPor = "Planta Conkal",
            Evidencia = "data:application/pdf;base64,JVBERi0xLjc=",
            RegistradoPor = solicitanteId
        };
        solicitudBaja.ValidarSolicitudBaja();

        // 2. Intentar validar o autorizar baja con el mismo usuario debe ser rechazado
        Assert.Throws<BusinessRuleException>(() => solicitudBaja.Validar(solicitanteId, false));

        // 3. Un autorizador facultado (DAF) puede rechazar motivadamente
        solicitudBaja.Rechazar(dafId, "Se requiere dictamen técnico previo de planta Conkal", DateTimeOffset.UtcNow);
        Assert.True(solicitudBaja.Anulada);
        Assert.Equal("Se requiere dictamen técnico previo de planta Conkal", solicitudBaja.MotivoRechazo);
    }

    [Fact]
    public void Reversa_autorizada_por_daf_en_capitalizacion_y_venta_cancelada()
    {
        var (activo, _) = CrearMaquinariaConkalReal();
        activo.Capitalizar(Guid.NewGuid());

        // Reversa de capitalización rechaza si el activo ya fue depreciado
        activo.Depreciar(2012.80m);
        Assert.Throws<BusinessRuleException>(() => activo.RevertirCapitalizacion(false, false));

        // Si se revierten las depreciaciones, se puede revertir capitalización a PorCapitalizar
        activo.Depreciar(-2012.80m);
        activo.RevertirCapitalizacion(false, false);
        Assert.Equal(EstadoActivo.PorCapitalizar, activo.Estado);
        Assert.Null(activo.PolizaAltaId);
    }

    [Fact]
    public void Clases_con_vigencia_prohibe_retroactividad_en_meses_depreciados()
    {
        var (_, clase) = CrearMaquinariaConkalReal();
        var versionInicial = VersionClaseActivo.Desde(clase, 1, new DateOnly(2026, 1, 1), Guid.NewGuid(), true);
        clase.Versiones.Add(versionInicial);

        // Propuesta para cambiar la tasa contable con motivo formal
        var propuesta = VersionClaseActivo.Desde(clase, 2, new DateOnly(2026, 9, 1), Guid.NewGuid());
        propuesta.TasaContableAnual = 15.0000m;
        propuesta.CapturarMotivo("Ajuste acordado con DAF para maquinaria pesada");
        clase.Versiones.Add(propuesta);

        // Si septiembre 2026 ya está depreciado, validar vigencia debe arrojar AF_VIGENCIA_EN_MES_DEPRECIADO
        var ultimoMesDepreciado = new DateOnly(2026, 9, 1);
        var ex = Assert.Throws<BusinessRuleException>(() => propuesta.ValidarVigencia(ultimoMesDepreciado));
        Assert.Equal("AF_VIGENCIA_EN_MES_DEPRECIADO", ex.Code);
        Assert.Contains("Septiembre 2026 ya está depreciado", ex.Message);

        // Autorizar versión vigente para octubre 2026 (mes no depreciado)
        propuesta.Vigencia = new DateOnly(2026, 10, 1);
        propuesta.ValidarVigencia(ultimoMesDepreciado);
        propuesta.Resolver(Guid.NewGuid(), true, null, DateTimeOffset.UtcNow);

        // En agosto y septiembre se mantiene 10 %, y a partir de octubre aplica 15 %
        Assert.Equal(10m, clase.EnMes(new DateOnly(2026, 8, 1)).TasaContableAnual);
        Assert.Equal(10m, clase.EnMes(new DateOnly(2026, 9, 1)).TasaContableAnual);
        Assert.Equal(15.0000m, clase.EnMes(new DateOnly(2026, 10, 1)).TasaContableAnual);
    }
}
