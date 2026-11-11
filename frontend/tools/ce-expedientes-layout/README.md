# Revisión aislada del detalle de Expedientes CE

Usa el componente de producción, ModulePanel y tokens reales, con la geometría del shell
(rail 76 px, panel 240 px, topbar 56 px y márgenes). Topbar y rail son soportes visuales;
no representa autenticación ni integración del AppShell completo.
Todas las respuestas son ficticias; `fetch` local rechaza escrituras y no contacta el ERP.
La fixture no está registrada como ruta del producto ni se incluye en el build de producción.

Desde frontend:

```sh
npx vite --config tools/ce-expedientes-layout/vite.config.mjs --host 127.0.0.1 --port 5187
```

Abrir `/`, `/?largo=1` y `/?lectura=1`. El botón «Mostrar expedientes» abre la lista.

El verificador requiere Playwright instalado y Google Chrome; no añade dependencias al producto:

```sh
CE_LAYOUT_PLAYWRIGHT=/ruta/a/playwright node tools/ce-expedientes-layout/verificar.cjs
```

`CE_LAYOUT_OUTPUT` permite elegir el directorio de capturas/medidas (fuera del repositorio).
Para detener la vista previa, interrumpir el proceso Vite.
