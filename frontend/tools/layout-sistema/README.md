# Validación transversal de layouts

Componentes reales MaestroDetalle, MaestroShell, PageHeader, TabsEstado, BarraFiltros,
ListadoModulo y ReporteShell. Datos ficticios; shell de soporte con medidas reales.
No prueba sesión QA, permisos de API ni todos los consumidores operativos.

Desde frontend:

```sh
npx vite --config tools/layout-sistema/vite.config.mjs --host 127.0.0.1 --port 5193
LAYOUT_PLAYWRIGHT=/ruta/al/playwright node tools/layout-sistema/verificar.cjs
```

Playwright se instala fuera del producto y requiere Google Chrome. LAYOUT_OUTPUT permite
elegir el directorio externo de capturas. No hay dependencia nueva en package.json.
Modos `?vista=detalle`, `maestros`, `listado` y `reporte`; `&largo=1` añade textos largos.
El verificador comprueba cuatro tamaños, desplazamiento local, lista plegable, captura,
pestañas por teclado e impresión. Detener la vista previa con Ctrl+C.
