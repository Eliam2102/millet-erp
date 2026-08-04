# ADR-0014: Modelo de dinero, multimoneda y tipos de cambio

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: dominio, dinero, multimoneda, fiscal, fundación

## Contexto y problema

Un ERP mexicano maneja dinero en cada módulo: cotizaciones, facturas,
órdenes de compra, pagos, asientos contables, presupuestos, costos. Aunque
la mayoría de las operaciones son en MXN, hay un porcentaje significativo
en moneda extranjera (típicamente USD, ocasionalmente EUR). Operar
multimoneda no es opcional.

Las decisiones que hay que tomar afectan **cada tabla** del sistema que
guarda un monto:

- ¿Cómo se modela el dinero en código? `decimal` simple es ambiguo (¿en qué moneda?), un value object `Money` agrega disciplina pero requiere infraestructura
- ¿Qué precisión usamos en BD? `decimal(18,2)` no alcanza para precios unitarios o tipos de cambio
- ¿De dónde viene el tipo de cambio? El DOF de Banxico es la referencia oficial del SAT, pero la realidad operativa incluye TCs pactados, manuales y bancarios
- ¿Cómo se manejan las diferencias entre TC del documento y TC efectivo del pago?

Sin convenciones explícitas, terminamos con bugs sutiles: facturas
guardadas con `decimal(18,2)` que pierden precisión en precios unitarios,
sumas heterogéneas que mezclan USD y MXN, reportes que convierten con TC
del día actual cuando deberían usar el TC histórico del documento.

## Drivers de la decisión

- Tipo seguro: imposible sumar USD con MXN sin conversión explícita
- Precisión adecuada: 4 decimales en montos, 6 en tipos de cambio
- Cumplimiento SAT: TC del DOF como referencia oficial; valores históricos preservados
- Realismo operativo: soporte para TC pactado por cliente, manual por documento, bancario al cobrar
- Auditoría: cada TC aplicado tiene origen identificable
- Reportería correcta: conversiones a moneda funcional usan TC histórico del documento

## Opciones consideradas

1. Value object `Money(Amount, Currency)` + tabla de TCs con múltiples fuentes
2. `decimal` simple con campo `currency_code` paralelo en cada tabla
3. Solo MXN, ignorar multimoneda hasta que sea necesario
4. Librería externa (NodaMoney) en lugar de tipo propio

## Decisión

Se adopta la **opción 1**: tipo `Money` propio como value object inmutable,
con catálogo de monedas, tabla de tipos de cambio del DOF, soporte para TCs
pactados/manuales/bancarios, y servicio de conversión.

### Tipo `Money` (value object)

```csharp
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Mxn(decimal amount) => new(amount, "MXN");
    public static Money Usd(decimal amount) => new(amount, "USD");
    public static Money Of(decimal amount, string currency) => new(amount, currency);

    public Money Add(Money other) =>
        Currency != other.Currency
            ? throw new IncompatibleCurrenciesException(Currency, other.Currency)
            : new Money(Amount + other.Amount, Currency);

    public Money Subtract(Money other) =>
        Currency != other.Currency
            ? throw new IncompatibleCurrenciesException(Currency, other.Currency)
            : new Money(Amount - other.Amount, Currency);

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Round(int decimals = 2,
        MidpointRounding mode = MidpointRounding.ToEven) =>
        new(Math.Round(Amount, decimals, mode), Currency);

    // Conversión explícita: requiere un TC del servicio
    public Money ConvertTo(string targetCurrency, decimal exchangeRate) =>
        new(Amount * exchangeRate, targetCurrency);
}
```

**Características**:
- Inmutable: cada operación retorna nuevo `Money`
- Operaciones aritméticas validan moneda; lanzan `IncompatibleCurrenciesException` si difieren
- Conversión es **explícita**: requiere pasar el TC. No hay conversión "implícita" mágica
- `Currency` es un código ISO 4217 de 3 letras (`MXN`, `USD`, `EUR`)

### Precisión y persistencia

- **Montos** (`amount`, `subtotal`, `total`, etc.): `decimal(18,4)` en BD, `decimal` en .NET
- **Tipos de cambio**: `decimal(18,6)` en BD (los TCs del DOF se publican con 4-6 decimales)
- **Importes finales** se redondean a 2 decimales al imprimir/presentar, pero se almacenan con 4
- Razón de los 4 decimales: precios unitarios (`$1,250.7350` por unidad), cantidades fraccionales (kg, litros), evitan acumulación de error de redondeo en sumas largas

### Modelo de columnas en entidades transaccionales

Cada entidad con monto lleva tres columnas mínimas:

- `monto` (decimal 18,4)
- `moneda` (text, FK lógica a `compartido.monedas`)
- En entidades multi-moneda: también `tipo_cambio_aplicado` (decimal 18,6) y `tipo_cambio_origen` (text)

Algunas entidades son inherentemente single-currency:
- Catálogo de cuentas contables: siempre en moneda funcional (MXN)
- Presupuestos en pesos: siempre MXN
- Saldos en cuentas bancarias: la moneda de la cuenta misma (no varía por movimiento)

Otras son multi-currency natas:
- CFDI, cotización, OC, recibo, pago: cada documento tiene su moneda

### Redondeo

- **Default**: `MidpointRounding.ToEven` (banker's rounding) — minimiza sesgo estadístico en sumas grandes
- **CFDI 4.0**: el SAT exige redondeo "comercial" (`AwayFromZero`) en algunos campos calculados (subtotal, total, IVA). Esto se aplica explícitamente en el módulo Fiscal con métodos especializados, NO se cambia el default global
- Convención: cualquier redondeo en código de dominio debe ser explícito con `.Round(decimals, mode)`; nunca implícito por casting o display

### Catálogo de monedas

Tabla `compartido.monedas` con el subset de ISO 4217 que el SAT acepta en
CFDI:

| codigo | nombre        | decimales | activa |
|--------|---------------|-----------|--------|
| MXN    | Peso Mexicano | 2         | true   |
| USD    | Dólar EUA     | 2         | true   |
| EUR    | Euro          | 2         | true   |
| CAD    | Dólar CAN     | 2         | true   |
| GBP    | Libra Esterlina | 2       | true   |
| JPY    | Yen           | 0         | true   |
| ...    | (subset SAT)  |           |        |

Toda asignación de moneda valida que el código exista en este catálogo y
esté activo. Migrarlo desde el catálogo SAT 4.0 (carga inicial vía seed +
sync periódico).

### Tipos de cambio: 5 fuentes

Cada documento en moneda extranjera almacena `tipo_cambio_aplicado` y
`tipo_cambio_origen` (enum):

- **`DOF`**: TC oficial publicado por Banxico para esa fecha. Default y fuente de referencia para reportes oficiales y validaciones SAT
- **`PACTADO`**: TC acordado con el cliente/proveedor a nivel de la relación comercial. Configurado en `comercial.clientes_tipos_cambio` (y equivalente en proveedores) con vigencias
- **`MANUAL`**: el usuario capturó o modificó el TC directamente en el documento. El sistema lo marca como `MANUAL` automáticamente cuando se altera el valor prefiliado
- **`BANCO`**: TC efectivo aplicado por el banco al momento del cobro/pago. Aplica solo a complementos de pago. Se usa para calcular ganancia/pérdida cambiaria
- **`AJUSTE`**: TC usado en revaluación contable de cierre (mensual/anual) para reexpresar saldos en moneda extranjera. Aplica a asientos de ajuste contable

### TCs pactados por cliente/proveedor

Tabla `comercial.clientes_tipos_cambio`:

```
clientes_tipos_cambio
├── id (uuid v7)
├── empresa_id (FK compartido.empresas)
├── cliente_id (FK comercial.clientes)
├── moneda (text)
├── tipo_cambio (decimal 18,6)
├── vigente_desde (date)
├── vigente_hasta (date, nullable)
├── activo (bool)
└── nota (text, nullable)
```

Equivalente para proveedores: `compras.proveedores_tipos_cambio`.

Al crear un documento (CFDI, cotización, OC) para un cliente/proveedor en
moneda extranjera, el sistema busca primero un TC pactado vigente; si
existe, lo prefilia con `origen = PACTADO`. Si no, prefilia con DOF del
día.

### Tabla `compartido.tipos_de_cambio_dof`

```
tipos_de_cambio_dof
├── fecha (date, PK con moneda)
├── moneda (text, PK con fecha)
├── tipo_cambio (decimal 18,6)
├── origen_url (text)         -- referencia al boletín del DOF
└── fetched_at (timestamptz)
```

- Job nocturno (`DofTipoCambioFetcherJob`) consulta la API de Banxico (SIE) y guarda los TCs publicados del día anterior
- Se conserva indefinidamente (es histórico fiscal del SAT)
- Para días no hábiles (sábados, domingos, feriados), se aplica el TC del último día hábil publicado
- Si Banxico no responde o falla la consulta, el job reintenta con backoff y notifica vía Application Insights

### UX de captura del TC

En el formulario de un documento en moneda extranjera, el campo TC tiene
tres elementos visibles:

1. **Input editable** con el valor actual prefiliado por prioridad: `PACTADO` > `DOF`
2. **Badge del origen**: "Pactado", "DOF", "Manual" (cambia automáticamente cuando el usuario edita)
3. **Referencia DOF del día** mostrada al lado, para comparación visual

Reglas:
- Si el usuario edita el valor del campo, `origen` cambia a `MANUAL` automáticamente
- Si la diferencia respecto al DOF supera un umbral configurable (default 5%), se muestra warning suave **no bloqueante**: "El TC ingresado difiere 7.2% del DOF del día"
- Campo opcional `tipo_cambio_nota` para justificar TCs manuales (queda en auditoría)
- Cambios de TC durante captura quedan en `audit_log` (operación `actualizar`, campo `tipo_cambio_aplicado`)

### Diferencia cambiaria al aplicar pagos

Escenario: CFDI USD timbrado con TC 19.50 (origen DOF). Cliente paga 30
días después; el banco convierte la transferencia a TC 19.80.

- El complemento de pago captura ambos TCs:
  - `tipo_cambio_documento` = 19.50 (heredado del CFDI original)
  - `tipo_cambio_pago` = 19.80 (origen `BANCO`, capturado por el cobrador)
- La diferencia (19.80 - 19.50) × monto USD se asienta automáticamente como **ganancia/pérdida cambiaria** en una cuenta contable parametrizable
- El módulo Financiero genera el asiento al aplicar el pago; el módulo Contabilidad lo procesa

### Servicio `IExchangeRateService`

```csharp
public interface IExchangeRateService
{
    // Devuelve el TC sugerido considerando contexto.
    // Prioridad: PACTADO (cliente/proveedor) > DOF
    Task<TipoCambioSugerido> GetSuggestedRateAsync(
        DateOnly fecha,
        string moneda,
        Guid? clienteId = null,
        Guid? proveedorId = null);

    // Devuelve solo el TC del DOF para esa fecha
    Task<decimal> GetDofRateAsync(DateOnly fecha, string moneda);

    // Calcula ganancia/pérdida cambiaria al aplicar un pago
    Task<DiferenciaCambiaria> CalcularDiferenciaAsync(
        Money documentoOriginal,
        decimal tcOriginal,
        decimal tcPago);
}

public record TipoCambioSugerido(
    decimal Valor,
    TipoCambioOrigen Origen,         // DOF, PACTADO, MANUAL, BANCO, AJUSTE
    decimal? ValorDofReferencia,     // Para mostrar al lado en UI
    string? Nota);

public record DiferenciaCambiaria(
    Money MontoEnMonedaDocumento,
    Money MontoEnMonedaFuncional,
    bool EsGanancia);
```

### Reportería en moneda funcional

Para reportes consolidados en MXN (estado de resultados, balance, etc.):

- Las facturas, pagos y asientos en moneda extranjera se convierten **con el TC histórico almacenado en el documento**, NO con el TC actual. Es regla del SAT y de NIF B-15
- Al cierre de periodo, los saldos en moneda extranjera de cuentas activas (cartera, proveedores) se reexpresan con TC de ajuste (`origen = AJUSTE`); la diferencia genera ganancia/pérdida cambiaria no realizada

### Frontend

- Componente `<MoneyDisplay value={money} />`:
  - MXN (moneda principal): `$1,234.56` (sin código)
  - Otra moneda: `$1,234.56 USD`
  - Negativos: `($1,234.56)` o `-$1,234.56` (config)
- Componente `<MoneyInput />`:
  - Acepta formato local con separadores de miles (`1,234.56`)
  - Valida según moneda: si `JPY` (sin decimales), no permite decimales
  - Selector de moneda al lado cuando aplica
- Componente `<TipoCambioInput />`:
  - Input numérico de 6 decimales
  - Badge de origen
  - Referencia DOF al lado
  - Tooltip con explicación de cada origen

## Consecuencias

**Positivas**
- Tipo seguro: imposible sumar USD con MXN sin conversión explícita; el compilador y los tests detectan errores
- Auditoría completa de TC: cada documento sabe qué TC se aplicó y de dónde salió
- Soporte para realidad operativa: pactado, manual, bancario, ajuste — no solo DOF
- Reportería correcta: conversiones usan TCs históricos, alineadas con SAT y NIF
- Diferencias cambiarias automáticas en pagos
- Frontend desacoplado: cambiar formatos de display no requiere tocar dominio

**Negativas**
- Más campos a llenar al capturar documentos en moneda extranjera (mitigado por prefiliados inteligentes)
- Disciplina obligatoria: olvido de pasar TC en `Money.ConvertTo` se convierte en error obvio (la API lo exige), pero olvido de aplicar TC histórico en queries de reportería es más sutil
- Catálogo SAT de monedas hay que mantenerlo sincronizado (cambia poco, pero hay que monitorearlo)
- Job de DOF debe ser robusto a fallos de Banxico; si el TC del día no se carga, los documentos creados ese día caen a fallback (último día hábil) y eso debe quedar visible en UI

## Descartadas

**`decimal` simple sin value object**. Sin tipo, no hay forma de prevenir sumas
incorrectas USD+MXN. Cada bug de este tipo se descubre solo cuando un cliente
reporta una factura mal calculada. Inaceptable.

**Solo MXN, multimoneda después**. Retrofittear `currency_code` en cada tabla
es trabajo enorme. Y la operación real de Millet ya tiene transacciones USD
desde el día 1.

**NodaMoney u otra librería externa**. Buenas pero adoptan convenciones
propias (rounding, exception types) que pueden chocar con las nuestras. El
`Money` propio es ~50 líneas y mantiene control total. Si surge necesidad
compleja (ej. operaciones con muchas monedas históricas), reevaluamos.

**TC único del DOF para todo**. Ignora la realidad operativa: pactados,
manuales y bancarios existen y se necesitan. Forzar todo a DOF causaría
trabajos manuales fuera del sistema.

## Notas de implementación

**Backend**
- Crear `Money` value object en `backend/src/SharedKernel/Domain/Money.cs`
- Excepción `IncompatibleCurrenciesException` que se mapea a HTTP 422 (ADR-0010)
- Configuración EF Core: convertidor de `Money` a dos columnas (`amount` y `currency`); usar `OwnsOne` o conversor custom
- Crear esquema `compartido` con tablas `monedas` y `tipos_de_cambio_dof`
- Crear tablas `clientes_tipos_cambio` (en módulo Comercial) y `proveedores_tipos_cambio` (en módulo Compras)
- Implementar `IExchangeRateService` con cache in-memory de TCs del día actual (refrescado cada hora)
- Hosted service `DofTipoCambioFetcherJob` que corre cada noche; configurable (CRON expression)
- Endpoints admin para forzar refetch del DOF si fallara el job

**Frontend**
- Crear tipos TypeScript: `interface Money { amount: number; currency: string }`
- Componentes `<MoneyDisplay>`, `<MoneyInput>`, `<TipoCambioInput>`
- Hook `useTipoCambioSugerido(fecha, moneda, clienteId?)` que consume `IExchangeRateService`
- Constantes de monedas y formatos en `frontend/src/lib/money.ts`

**Tests**
- Tests unitarios de `Money`: arithmetic, immutability, currency validation
- Tests de `IExchangeRateService`: prioridad PACTADO > DOF, fallback a último día hábil, errores de Banxico
- Test de cálculo de diferencia cambiaria con valores reales

**Documentación en `CLAUDE.md`**
- Patrón de uso de `Money` en handlers
- Cómo y cuándo aplicar TC histórico vs actual
- Reglas de redondeo (banker's vs comercial-CFDI)
- Cómo registrar nuevas monedas en el catálogo

**Cambios en otras ADRs**
- ADR-0005: convenciones de columnas monetarias en `BaseDbContext`
- ADR-0011: tabla `compartido.tipos_de_cambio_dof` queda en el esquema compartido (cross-empresa)
- ADR-0006: el job de fetch del DOF reporta a App Insights con métricas custom

**ADRs hijo posibles**
- Política de reexpresión de saldos en cierre (qué cuentas se reexpresan, con qué TC)
- Convenciones de presentación de montos negativos (paréntesis vs signo) y separadores
- Manejo de monedas con decimales no estándar (KWD: 3 decimales, JPY: 0 decimales) si surgen
