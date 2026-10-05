# 08 — Evidencia de F1-CON-02 (dimensiones contables)

> Fecha: 2026-10-04 · Rama `feature/F1-CON-02-dimensiones-contables` · Datos: **solo de prueba** (`FIX-*`, `es_prueba = true`).
> Las reglas, tipos de documento y asignaciones centro ↔ sucursal de esta evidencia **no son política de Contabilidad**.

## Casos de la ficha → prueba que lo demuestra

| Caso de la ficha | Prueba (integración HTTP + Postgres real, BD desechable) |
|---|---|
| Combinación válida (cuenta + tipo + dimensión obligatoria) → acepta | `DimensionesHttpTests.Combinacion_valida_se_acepta_y_dimension_obligatoria_ausente_se_rechaza_indicando_cual_falta` |
| Dimensión requerida ausente → rechazo que indica cuál falta | misma prueba: `CONTAB_DIM_OBLIGATORIA_FALTANTE`, `campo = dim3Id`, mensaje con «Dimensión 3»; `POST /movimientos-prueba` 422 sin escribir |
| Centro inactivo → rechazo en movimiento nuevo | `Centro_inactivo_se_rechaza_en_movimiento_nuevo_y_no_se_ofrece` (equipo dado de baja y CeCo dado de baja en cadena) |
| Sucursal incorrecta → centro de otra sucursal no seleccionable | `Centro_de_otra_sucursal_no_es_seleccionable_y_la_api_lo_rechaza` (selector + `CONTAB_DIM_CENTRO_OTRA_SUCURSAL` + `CONTAB_DIM_CENTRO_SIN_SUCURSAL`) |
| Conservación histórica tras cambiar la regla | `Movimiento_registrado_conserva_la_regla_con_la_que_se_valido_tras_cambiar_la_politica` (cerrar regla, crear otra, el movimiento muestra la anterior; traslape 409; regla usada no se edita ni se borra; sin retroactividad) |
| Solo configuración contable edita reglas; alcance por sucursal | `Solo_la_configuracion_contable_edita_reglas_y_el_alcance_por_sucursal_se_respeta` (401, 403, `SUCURSAL_NO_ASOCIADA`, operativo vs corporativo) |
| Protección por uso (v0.3): regla sin uso se corrige y se borra; regla futura usada por un movimiento con fecha contable futura no se edita; cierre no antes del último uso | `La_proteccion_de_la_regla_depende_de_su_uso_no_de_si_es_futura` |
| Herencia por rama, «no aplica», jerarquía, cuenta que acumula | `Regla_de_la_rama_se_hereda_no_aplica_rechaza_lo_capturado_y_la_jerarquia_debe_ser_congruente` |

## Resultados

| Suite | Resultado |
|---|---|
| `Contabilidad.UnitTests` | 129/129 (20 nuevas en `DimensionesTests`: vigencia, traslape, herencia, especificidad, fecha contable, evaluación, opciones) |
| `Api.IntegrationTests` filtro Contabilidad + CentrosCosto + Identidad + Administración (`tools/validate-integration-isolated.sh`) | 339/339 (6 nuevas en `DimensionesHttpTests`) |
| `Identidad.UnitTests` · `Administracion.UnitTests` · `Compras.UnitTests` | 101/101 · 130/130 · 527/527 |
| Vitest `features/contabilidad`, `features/centros-costo`, `lib/nav`, `lib/auth`, `components/layout` | 186/186 (7 nuevas: `ReglasTab.test.tsx`, `MovimientosPruebaPage.test.tsx`) |
| `tsc -b tsconfig.app.json` · `eslint` (módulo, rutas, nav, permisos) · `vite build` | sin errores |
| `tsc -b tsconfig.test.json` | solo el error preexistente `src/lib/nav.test.ts(348,58)` de `main` |

## Migraciones

- `20261005015104_ContabilidadDimensionesReglas` (Contabilidad): `tipos_documento_contable`, `reglas_dimension`, `centros_costo_sucursal`,
  `movimientos_dimension_prueba`.
- `20261005013059_SeedPermisosContabilidadDimensiones` (Identidad): 4 permisos `0000000d-0002-*` / `0000000d-0003-*`.

Aplicadas en la BD desechable del gate (13 contextos) y en `millet_dev` local.

## Datos de prueba locales

`tools/datos-prueba-f1-con-02.sql` sobre los datos base existentes: 3 tipos `FIX-FP/EM/AM`, rama `FIX-600` con 2 afectables, 3 reglas
(CeCo obligatorio heredado en la rama, equipo obligatorio en mantenimiento facturado, equipo no aplica en papelería) y los CeCo activos
sembrados por ADM-08 asignados a la primera sucursal (los primeros 5 también a la segunda). Idempotente; bloque de retiro al final.

## Evidencia de UI

Pantallas: Contabilidad → **Dimensiones contables** (pestañas Reglas · Centros por sucursal · Tipos de documento) y **Probar movimientos**.
Verificadas con pruebas de componente; la revisión visual en navegador con sesión Entra queda a cargo del responsable (capturas para el PR).

## Corrección v0.3 (2026-10-05)

Protección de reglas por uso (ver plan §15.1). Migración `ContabilidadReglasDimensionUso`. Resultados tras el cambio: unitarias
Contabilidad 129/129; integración filtro Contabilidad (BD desechable) 51/51 (1 nueva); Vitest `features/contabilidad` 69/69
(1 nueva: eliminar regla sin usos); `tsc` y `eslint` limpios.
