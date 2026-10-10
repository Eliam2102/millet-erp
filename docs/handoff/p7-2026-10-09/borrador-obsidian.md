# Borrador local · actualización puntual de Obsidian · P7 · 09-oct-2026

**No aplicado a la bóveda.** Su ubicación queda fuera de las raíces de escritura de esta sesión. La fuente de la decisión es la ficha P7 de Eliam de esta conversación (09-oct), y la fuente del avance es el diff local sobre `6718ffe`, los tests y el informe [P7](README.md). No implica publicación, merge, UAT ni aceptación de Millet. No se refrescaron ClickUp ni chats externos; los estados de gestión y acuerdos posteriores siguen por verificar en sus fuentes.

## Cambios que aplicar conservando la historia

En `03 Funcionalidades/F1-COM-03.md`, registrar D3 de Eliam: RQ autorizada aparta existencia libre y sólo compra el faltante. ADR-0061 reemplaza la regla de no reservar de ADR-0047; conserva jerarquía/bin/ledger anteriores. Parámetro por empresa activo por defecto y apagable, sin ocupar IDs de ParametroGlobal. Estado: construido localmente y unitarias verdes; integración/confirmación Millet 12-oct **Por confirmar**.

En `03 Funcionalidades/F1-COM-04.md`, registrar D4: cotización obligatoria aunque exista el campo histórico de excepción. Hay pruebas escritas de los tres errores documentales y camino válido; ejecución PostgreSQL **Por confirmar**.

En `03 Funcionalidades/F1-COM-12.md`, registrar árbol recursivo y entradas desde recepción/factura/aplicación de pago. Proveedores reales de lectura, sin cambiar el flujo P4. Prueba visual de render profundo en frontend verde; prueba con OC pagada escrita, PostgreSQL **Por confirmar**.

En `03 Funcionalidades/F1-ALM-07.md`, registrar avisos en Inicio para pendientes vencidos y por vencer en 24 h, vinculados a bandeja. Filtros unitarios verdes. No hay correo enviado ni remitente configurado; V08 **Por confirmar**.

En `03 Funcionalidades/F1-ALM-10.md`, registrar D6: físico + pedido vivo ≤ punto, máximo o cantidad fija. Motor unitario verificado para no generar sobre punto y suprimir otro borrador con cobertura viva. Integración y simultaneidad entre ciclos **Por confirmar**.

En `03 Funcionalidades/F1-ADM-04.md`, registrar D16: recepción/salida aplican FactorABase y conservan captura. Caso DEMO 2 CAJA × 12 = 24 PZA verificado en unitarias con factor de prueba y adaptador real. Equivalencias reales del cliente, migraciones en PostgreSQL y circuito por endpoints **Por confirmar**.

En `03 Funcionalidades/F1-ADM-08.md`, conservar ADR-0050 (Dim3 obligatorio por línea e independiente del departamento) y la petición nueva P7 (herencia del departamento/caso sin máquina), registrando el conflicto. Responsable de decidir: Eliam. V49 pendiente. No registrar implementación ni pruebas del punto.

Registrar D11 en las fichas de requisición/OC/CxP correspondientes: obra opcional en texto RQ → OC → pasivo; herencia construida y prueba de punta a punta escrita. Falta ejecución PostgreSQL. No confundir con catálogo real ni aceptación.

## Entrada propuesta para `10 Gobierno y cambios/Bitácora.md`

09-oct-2026 · P7 · Fuente: ficha de Eliam, ADR-0061 y evidencia local en `docs/handoff/p7-2026-10-09/`. Se construyeron apartado configurable de RQ, documentos obligatorios de OC, trazabilidad a pagos, avisos en Inicio, reorden por punto/máximo/fijo, conversión de unidades y herencia de obra. Build 0 errores/0 advertencias; 1,762 unitarias backend con runner real xUnit en proceso, 0 fallos/omisiones; frontend 354 archivos/2,061 pruebas verdes, tipos verdes, lint 0 errores/10 advertencias existentes. Integración PostgreSQL no ejecutada en sandbox sin Docker y VSTest estándar bloqueado por socket local. ADM-08 pendiente de conciliación con ADR-0050. Base inicial `6718ffe`; main avanzó a `b72f990` con P5/P9 durante la sesión, pendiente de conciliación. Sin commits, push, migraciones aplicadas, envíos ni despliegue. Responsable siguiente: Eliam (ADM-08), Claude (conciliación e integración completa + rojo/verde).

Actualizar `10 Gobierno y cambios/Cobertura y límites de esta entrega.md` y `00 Inicio/Maestro operativo ERP · Fase 1.md` únicamente para señalar construcción local parcial P7 y sus pendientes. Conservar sus cortes/historia y no promover pruebas unitarias a integración o aceptación.
