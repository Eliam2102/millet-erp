# Especificación · Navegación ocultable y cabecera adaptable

Fuente: solicitudes y correcciones directas de Eliam, chat01a1d17a-25da-76e2-9b3d-937fe4c0fa05,12-nov-2026. Es propuesta VILO construida localmente; aprobación visual parcial de Eliam («Así está mejor» al panel izquierdo), sin aceptación independiente de Millet ni publicación autorizada. Historial de alternativas en research y bóveda.

## Historias y aceptación
### US1 · Espacio de trabajo y navegación
El usuario dispone del ancho completo al capturar; puede abrir sus módulos desde Menú o acercar el cursor al borde izquierdo en escritorio. El panel entra desde izquierda, queda unido al borde y sale hacia la izquierda. Puede fijarlo reservando su ancho, sin cubrir campos. Al salir con el cursor, elegir pantalla, pulsar fuera oEscape, se oculta salvo el pin; buscar enfocado no se interrumpe. Un roce rápido del borde no lo deja abierto.
### US2 · Captura organizada
Nueva requisición muestra Datos del solicitante, Detalle de la solicitud y Condiciones de surtido con componentes existentes. Abrir/cerrar menú y herramientas no desmonta ni vacía los campos. Validaciones conservan captura. No se promete conservación al navegar a otra ruta, recargar o cambiar empresa.
### US3 · Cabecera y móvil
Cabecera limpia sin el texto Millet ERP, con menú/buscador/avisos/usuario prioritarios; acciones de creación/ayuda/admin agrupadas en Más opciones cuando falta espacio. Empresa/sucursal mantienen etiquetas y selección; móvil tiene objetivos44px y formulario de una columna. Escritorio conserva controles accesibles sin scroll horizontal de cabecera.

## Requisitos funcionales
- FR001: reutilizar SidebarNav76px y ModulePanel240px dentro de panel oculto por defecto, fijo x0 bajo cabecera, sin apariencia flotante, con animación lateral y movimiento reducido.
- FR002: módulos con orden completo, selección por ruta más específica y opciones filtradas por permisos.
- FR003: filtro de módulo sin acentos y botón para limpiar; títulos/iconos consistentes.
- FR004: piloto en tres grupos, mismos campos, obligatoriedad, límites, validaciones, persistencia y comandos.
- FR005: controles de sesión, sucursal, búsqueda, avisos, creación y ayuda conservados; Administración mantiene gating vigente.
- FR006: no agregar dependencias, consultas ni contratos nuevos; no modificar servidor/datos/reglas.
- FR007: Menú, borde de escritorio y cierre automático; excepción de búsqueda enfocada.
- FR008: pin reserva ancho316px; cierre/Escape libera ancho y mantiene formulario montado.
- FR009: superficies planas, títulos formales y tokens existentes.
- FR010: cabecera limpia, sin marca textual Millet ERP, con separación entre controles y Herramientas secundarias y contexto empresa/sucursal legible; paneles de cabecera limitados al viewport.
- FR011: borde izquierdo abre automáticamente sólo en escritorio; móvil abre al tocar el botón.

## Criterios de éxito
- SC001: cerrado libera ancho; abierto unido al borde; pin no cubre captura.
- SC002: regresiones de orden/filtro/permisos/captura aprobadas.
- SC003: comparación visual1366/1920/390 documentada fuera delrepo.
- SC004: pruebas afectadas, build y lint; warnings y límites registrados.
- SC005: navegador comprueba abrir/cerrar/pin/despin/Escape y muestra conservada; móvil conserva drawer.
- SC006: revisión320/360/390/768/1366/1920 y muestra multiempresa, sin desbordamiento horizontal de controles; búsqueda/avisos/herramientas accesibles.
- SC007: parche de código y borrador de paseQA/reversión, excluyendo harness ficticio. Publicación requiere autorización específica.

## Casos límite y límites
Sin permisos, múltiples empresas, nombres largos, rutas de administración sin panel, búsqueda enfocada, roce rápido del borde y movimiento reducido. No se acredita cobertura de todos los formularios, teléfono físico, teclado virtual, API real ni aceptaciónQA/UAT/producción. Datos de muestra identificados y guardado real bloqueado en revisión.

Corrección posterior de Eliam: los controles se ven amontonados; quitar el texto «Millet ERP» de la cabecera móvil y de escritorio. Mantener funciones y sidebar. Sustituye el criterio previo de marca textual visible.
