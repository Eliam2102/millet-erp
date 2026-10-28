# CONTEXTO · Producto Terminado (MOD-PT)

Paquete de contexto para desarrollo. No reemplaza el diseño: lo resume y apunta a él. Creado en la tarea 86e3jvuk2 (PT-01) a partir de `01-diseno.md` (E08, aprobado por Eliam el 27-oct-2026; Rev. 3 con las respuestas de Jorge del 27-oct). Solo hay diseño; todavía no hay código. Etiquetas: `[DISEÑO]` decidido en E08; `[SUPUESTO]` a falta de respuesta de Millet; `[ABIERTO]` sin respuesta.

## 1. Qué es y qué no es
- Módulo `ProductoTerminado` (esquema `producto_terminado`): existencia de la pieza terminada por pieza y sucursal, no stock de catálogo. `[DISEÑO]`
- No maneja estado de pedido ni liberación 69→70 (PT-02), ruta/carga (PT5.4), albarán (PT5.5), traspasos (PT5.9) ni valuación (PT5.12).

## 2. Documentos a leer
1. `docs/modulos/producto-terminado/01-diseno.md` (E08).
2. `docs/integration/09-aw-otr-exportacion.md` (patrón de worker y selector Demo/Real; la entrada de PT no usa tabla puente).
3. `docs/operacion/aw-canal-demo-pruebas.md` (canal Demo).

## 3. Reglas confirmadas
- El 69 es del pedido y lo pone A+W; el ERP no lo infiere y solo lleva el avance por posición. El 70 es la liberación. `[DISEÑO · Jorge 27-oct]`
- Identidad de la pieza = pedido + posición + número de pieza; sin existencia anónima. `[DISEÑO]`
- Idempotencia por (`etikettnr`, `bearb_zeit`); un renglón nuevo de una etiqueta ya en PT es `Reposicion`, no se descarta. `[DISEÑO · Jorge 27-oct]`
- El ERP solo lee A+W: escaneos en `alcimdb.awbar_bearbeit` (instancia AWPRODUCTION), sucursal en MILMAIN `OR_AVBEREICH`; solo `Integraciones.Aw` toca A+W. `[DISEÑO · Jorge 27-oct]`
- Lectura por lotes (sin CDC). `[DISEÑO]`

## 4. Contratos
- Puerto de lectura de pedido `IPedidosPtReadPort` (PT) con adaptador en Facturación.
- Evento `producto_terminado.pieza.ingresada.v1`.

## 5. Abiertos y supuestos
- `[ABIERTO]` Conexión a AWPRODUCTION y usuario de solo lectura (P-PT4). Solo bloquea encender el canal real.
- `[ABIERTO]` Cómo se ve una pieza rota en `awbar_bearbeit` (P-PT3).
- `[SUPUESTO]` S1–S12 de `01-diseno.md` §11.

## 6. Aprendizajes por tarea
- 86e3jvuk2 (PT-01): módulo creado.
