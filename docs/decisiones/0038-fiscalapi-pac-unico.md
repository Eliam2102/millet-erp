# ADR-0038: Adoptar FiscalAPI como PAC único para descarga + timbrado + cancelación

- **Estado**: Propuesta
- **Fecha**: 2026-05-24
- **Decisores**: Eduardo Paredes
- **Etiquetas**: fiscal, pac, integración, vendor-switch
- **Reemplaza a**: [ADR-0027](./0027-integracion-pac-onefactura.md)

## Contexto y problema

[ADR-0027](./0027-integracion-pac-onefactura.md) (mayo 2026) eligió a
**OneFactura** como PAC para la operación de timbrado/cancelación/consulta
de CFDIs emitidos. En paralelo, el módulo de CxP en producción ya estaba
usando **FiscalAPI** (api.fiscalapi.com) para la descarga de CFDIs
recibidos.

Tener dos PACs distintos implica:

- Dos contratos comerciales activos (costo + administración).
- Dos sets de credenciales por empresa (CSD para OneFactura,
  ApiKey para FiscalAPI).
- Dos abstracciones (`IPacProvider` para timbrado, `IFiscalApiClient`
  para descarga).
- Dos códigos de error a normalizar, dos canales de soporte, dos curvas
  de aprendizaje del equipo de operación.

Tras evaluación de FiscalAPI, **el vendor cubre todo el ciclo**: descarga,
timbrado, cancelación, consulta de estado, complementos (carta porte,
nómina, REP, etc.). El módulo Facturación todavía no se implementa —
estamos a tiempo de cambiar la decisión sin sacrificar trabajo previo.

## Drivers de la decisión

- **Costo operativo único** — un contrato, una cuenta, una factura.
- **Mismo cliente HTTP / mismas credenciales / misma admin UI** — el
  módulo `Millet.Integraciones.Fiscal` aloja todo bajo un solo techo.
- **Menos superficie de error en producción** — un solo PAC para
  monitorear, alertar, escalar a soporte.
- **El módulo Facturación aún no existe** — no hay código que migrar.
- **CxP ya está en producción con FiscalAPI** — sin reescribir su
  cliente actual.

## Opciones consideradas

1. **FiscalAPI como PAC único** — adoptar FiscalAPI para descarga +
   timbrado + cancelación + consulta. OneFactura abandonado.
2. **Mantener split: FiscalAPI descarga + OneFactura timbrado** — status
   quo de ADR-0027.
3. **Migrar todo a OneFactura** — descartar FiscalAPI también.

## Decisión

**Opción 1 — FiscalAPI como PAC único.**

Detalles:

- **Una sola abstracción** `IFiscalApiClient` cubre todos los flujos.
  Cuando el módulo Facturación arranque, **no se introduce
  `IPacProvider`** — el handler de timbrado consume `IFiscalApiClient`
  directamente.
- **Una sola fila** en `integraciones_fiscal.configuracion_pac` por
  empresa, con el ApiKey de FiscalAPI cifrado vía DataProtection
  (ADR-0037).
- La **CSD del SAT** sigue siendo de Millet (no de FiscalAPI), porque
  el sellado del CFDI debe hacerlo el emisor con su propio CSD antes de
  pasarlo al PAC. La CSD vive en Key Vault como `Certificate`
  (mismo manejo que describía ADR-0027 §"CSD" — esa parte se mantiene).
- **El XML CFDI sigue construyéndose del lado del ERP** (decisión clave
  de ADR-0027 §"Construcción del XML CFDI"). Esto se preserva: FiscalAPI
  recibe el XML ya sellado y solo agrega el TFD.
- **Errores normalizados** con codigos neutros (`CFDI_DUPLICADO`,
  `XML_INVALIDO`, etc.) — mismo patrón, distinto mapper.

### Componentes del módulo Integraciones.Fiscal (ampliado)

```
backend/src/Integraciones.Fiscal/
├── Domain/
│   ├── IFiscalApiClient.cs              -- puerto único
│   ├── ConfiguracionPac.cs              -- agregado
│   ├── RfcReceptor.cs                   -- entidad
│   └── Operaciones/
│       ├── DescargarRequest/Response    -- descarga (era CxP)
│       ├── TimbrarRequest/Response      -- timbrado (era OneFactura)
│       ├── CancelarRequest/Response     -- cancelación
│       ├── ConsultarEstatusRequest/Response
│       └── ResponderCancelacionRequest/Response
├── Application/
│   ├── GuardarConfiguracionPacCommand
│   ├── TestConexionPacCommand
│   └── (queries admin)
└── Infrastructure/
    ├── FiscalApiHttpClient.cs           -- impl única
    ├── NoOpFiscalApiClient.cs
    ├── ConfiguracionPacResolver.cs
    └── Workers/
        ├── DescargaMasivaSatWorker.cs
        └── EstadoSatRefreshWorker.cs
```

### Diferencias con ADR-0027

| Aspecto | ADR-0027 (descartado) | ADR-0038 (este) |
|---|---|---|
| Abstracción | `IPacProvider` | `IFiscalApiClient` (incluye timbrado) |
| Vendor | OneFactura | FiscalAPI |
| Módulo dueño | Fiscal (por crear) | `Integraciones.Fiscal` |
| Multi-PAC failover futuro | `FailoverPacProvider` composite | Mismo patrón aplica — composite con N implementaciones de `IFiscalApiClient` |
| CSD en KV | ✅ mismo | ✅ mismo |
| Construcción de XML del lado ERP | ✅ mismo | ✅ mismo |
| Catálogos SAT en BD | ✅ mismo (`compartido.formas_pago`, etc.) | ✅ mismo |
| Permisos canónicos | `fiscal.cfdi.timbrar`, etc. | `integraciones.fiscal.cfdi.timbrar`, etc. (re-namespacear) |

## Consecuencias

**Positivas**

- Un solo contrato comercial activo — simplifica negociación, soporte
  y administración de credenciales.
- Una sola admin UI cubre todo el ciclo fiscal (`/admin/integraciones/fiscal`).
- Un solo cliente HTTP con un solo conjunto de policies Polly, un solo
  conjunto de tests de mapeo de errores.
- Menos código a mantener — no hay `IPacProvider` separado.
- Cutover de ADR-0027 → ADR-0038 sin esfuerzo: módulo Facturación
  todavía no se implementa.

**Negativas**

- **Mayor lock-in al vendor**. Si FiscalAPI sube precios o tiene un
  outage prolongado, todo el flujo fiscal queda afectado. Mitigación:
  `IFiscalApiClient` sigue siendo abstracción — agregar un secundario
  (`SecondaryFiscalApiClient` apuntando a otro PAC) es modificación
  contenida cuando se justifique.
- Si OneFactura mejora su oferta o FiscalAPI degrada SLA, volver atrás
  implica trabajo. Mitigación: revisar contrato anualmente.
- ADR-0027 queda obsoleto antes de implementarse — desperdicio del
  ejercicio de diseño. Mitigación: el contenido técnico (construcción
  XML, manejo CSD, errores normalizados) se preserva en ADR-0038 y en
  el `01-diseno.md` del módulo.

## Descartadas

**Mantener split FiscalAPI + OneFactura.** Doble costo + doble
mantenimiento sin beneficio claro, dado que FiscalAPI cubre el catálogo
completo.

**Migrar todo a OneFactura.** Tendría sentido si OneFactura cubriera
también descarga **mejor** que FiscalAPI. No es el caso —
FiscalAPI cubre descarga, validación y timbrado con calidad probada en
producción para CxP.

## Notas de implementación

- Marcar [ADR-0027](./0027-integracion-pac-onefactura.md) como
  `Reemplazada por ADR-0038`.
- En el `01-diseno.md` de `Integraciones.Fiscal`, ampliar el alcance
  para incluir timbrado/cancelación/consulta como operaciones del mismo
  `IFiscalApiClient` (no abstracciones separadas).
- Cuando Facturación arranque, **NO se crea `IPacProvider`** — el
  handler de timbrado inyecta `IFiscalApiClient`.
- Tablas/migraciones del módulo Facturación referencian la **misma**
  `integraciones_fiscal.configuracion_pac` (vía `IConfiguracionPacResolver`)
  por empresa. No duplicar config.
- ADR-0027 §"CSD" se preserva textualmente — la CSD sigue gestionándose
  igual (KV Certificate, `ICsdProvider`, auditoría de uso).
- Permisos canónicos a re-namespacear en su PR correspondiente:
  - `integraciones.fiscal.cfdi.timbrar` (sustituye `fiscal.cfdi.timbrar`)
  - `integraciones.fiscal.cfdi.cancelar.solicitar`
  - `integraciones.fiscal.cfdi.cancelar.aprobar`
  - `integraciones.fiscal.cfdi.consultar-estatus`
  - `integraciones.fiscal.csd.gestionar`
  - `integraciones.fiscal.configurar` (renamed `fiscal.pac.configurar`)
