# Inventario funcional trazable · Fase 1

Este inventario concilia la matriz definitiva con el plan de olas y con la estructura real del repositorio. La presencia de una carpeta o prueba indica un punto de entrada, no acredita por sí sola que la funcionalidad esté terminada.

## Control de integridad

- Funcionalidades Fase 1: **139**.
- Horas funcionales base: **628 h**.
- Estados: Ya existe y funciona: 35, Existe a medias: 55, No existe: 49.
- Marcadas como existentes y con 0 h: **35**; las 35 conservan QA, regresión, evidencia y UAT pendientes.
- Sin módulo backend dedicado localizado: **16** (F1-CE-01, F1-CE-02, F1-CE-03, F1-CE-04, F1-CE-05, F1-CE-06, F1-CE-07, F1-AF-01, F1-AF-02, F1-AF-03, F1-AF-04, F1-AF-05, F1-AF-06, F1-AF-07, F1-AF-08, F1-AF-09).
- Archivo operativo completo: [inventario-funcional-fase1.csv](inventario-funcional-fase1.csv).

## Distribución por ola

- Ola 1A · Fundaciones: 15 funcionalidades.
- Ola 1B-1 · Abastecimiento e inventario: 37 funcionalidades.
- Ola 1B-2 · Pasivo y comercio exterior mínimo: 20 funcionalidades.
- Ola 1C · Venta, entrega y cartera: 37 funcionalidades.
- Ola 1D · Tesorería y cierre: 30 funcionalidades.

## Regla para las 35 existentes

Cada fila debe pasar por levantamiento local, ejecución del caso, evidencia, regresión y presentación en UAT. Hasta entonces su resultado permanece `Pendiente`; no se considera cerrada por tener 0 horas de construcción.

## Riesgos detectados por estructura

- Comercio Exterior y Activos Fijos no tienen módulo dedicado localizado en el repositorio actual.
- Contabilidad sólo tiene puntos parciales en Centros de Costo/Compartido; no se localizó un módulo contable dedicado.
- Las rutas candidatas deben sustituirse por archivo/endpoint/pantalla exactos durante la toma de cada tarea.
- La asignación singular se concilia con la base vigente de Notion: Dev 1 = Uzziel y Dev 2 = Geovany.

## Criterio de cierre por fila

No puede pasar a terminado sin: PR revisado, pruebas aplicables, migración/configuración documentada, recorrido reproducible, evidencia, regresión y aceptación funcional cuando corresponda.
