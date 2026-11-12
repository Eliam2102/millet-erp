# Validar PDF e impresión

## Preparación
Desde `frontend/`, instalar las dependencias del proyecto y usar los datos ficticios de las pruebas. No requiere API, QA ni producción.

## Pruebas dirigidas

```sh
npm test -- src/lib/seguridad/csp.test.ts src/components/erp/reportes
npm test -- src/features/contabilidad/lib/documentos-impresion.test.ts src/features/activos-fijos/pages/conservacion.test.tsx
npm run build
npm run lint
```

Para conservar el PDF de muestra fuera del repositorio:

```sh
PDF_MUESTRA_OUTPUT=/tmp/analisis-proveedores-muestra.pdf npm test -- src/components/erp/reportes/exporters/reporte-pdf-exporter.test.tsx
```

Comprobar texto extraíble, primera y última fila, totales, encabezados repetidos, cuatro páginas carta horizontal y ausencia de imágenes de pantalla. La prueba de texto extenso comprueba que una celda mayor que una página conserva el final y las notas posteriores.

## Navegador
Servir el build con las cabeceras de `dist/staticwebapp.config.json`; un servidor sin CSP no reproduce el problema original. Usar una muestra identificada sin conexiones a ambientes compartidos. Elegir PDF e Imprimir y comprobar generación, apertura del visor y ajustes de impresión. En un detalle, reutilizar su documento PDF existente cuando corresponda.

Verificar errores recuperables ante ventana bloqueada o generación fallida, bloqueo de clics repetidos y cierre temprano del visor. Revisar que el documento completo no dependa de la página visible de la tabla.

Registrar resultados y límites en el PR o fuera del repositorio. El recorrido autenticado en QA y la impresión física se validan por separado, tras un despliegue autorizado.
