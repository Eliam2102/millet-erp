const {chromium} = require(process.env.INI_ROL_PLAYWRIGHT || 'playwright');
const fs = require('node:fs'); const path = require('node:path');
(async()=>{
 const browser=await chromium.launch({headless:true});
 const page=await browser.newPage();
 await page.route('**/*',async route=>{
  const url=new URL(route.request().url());
  if(url.pathname.startsWith('/api/')){
   let data={items:[],total:12,offset:0,limit:1};
   // Datos con la forma real de cada endpoint: los circuitos traen etapas; el flujo, la serie por día.
   if(url.pathname.includes('/inicio/'))data={valor:412875,total:12,contexto:'12 documentos de muestra · solo MXN · 2 vencidos',urgentes:2,
    ...(url.pathname.endsWith('/circuito')?{etapas:[{etiqueta:'Requisiciones',total:18},{etiqueta:'Por autorizar',total:7},{etiqueta:'OC abiertas',total:14},{etiqueta:'Por recibir',total:5},{etiqueta:'Por pagar',total:9}]}:{}),
    ...(url.pathname.endsWith('/flujo')?{serie:['Lun','Mar','Mié','Jue','Vie','Sáb','Dom'].map((dia,i)=>({dia,cobros:40000+(i*2500),pagos:35000-(i*3000)}))}:{})};
   else if(url.pathname.endsWith('/admin/auditoria')) data={items:[1,2,3].map(i=>({id:`muestra-${i}`,entidad:'Factura',entidadEtiqueta:`Factura de muestra ${i}`,resumen:'Captura ficticia para prueba de layout',operacion:'Crear',timestamp:'2026-10-10T15:00:00Z'})),total:3};
   else if(url.pathname.endsWith('/kpis'))data={countPartidasAbiertas:11,countAtrasadas:2};
   else if(url.pathname.includes('/ordenes/pendientes'))data={totalCount:11,items:[]};
   return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(data)});
  }
  const file=path.join(process.env.INI_ROL_LAYOUT_DIST || '/tmp/ini-rol-layout-dist',url.pathname==='/'?'index.html':url.pathname);
  if(fs.existsSync(file))return route.fulfill({path:file});
  return route.abort();
 });
 const resultados=[];
 for(const [width,height] of [[1440,900],[1280,800],[390,844]]){
  await page.setViewportSize({width,height});
  for(const perfil of ['compras','almacen','cobranza','tesoreria','admin']){
   await page.goto(`http://inirol.test/?perfil=${perfil}`); await page.waitForSelector('[data-pieza]');
   await page.waitForFunction(()=>!document.querySelector('[aria-busy="true"]'));
   const medidas=await page.evaluate(()=>({
    scroll:document.documentElement.scrollHeight,viewport:innerHeight,width:document.documentElement.scrollWidth,
    tarjetasRecortadas:[...document.querySelectorAll('[aria-label="Resumen visual"] > div, [data-pieza]')].filter(el=>el.scrollHeight>el.clientHeight+1).map(el=>el.textContent.slice(0,100)),
   }));
   resultados.push({width,height,perfil,...medidas});
   if(width>=1280 && (medidas.scroll>height || medidas.width>width || medidas.tarjetasRecortadas.length))throw new Error(`Scroll de página ${perfil} ${width}x${height}: ${medidas.scroll}`);
   if(width===1440 && perfil==='admin')await page.screenshot({path:'/tmp/ini-rol-layout-1440.png',fullPage:true});
  }
 }
 fs.writeFileSync('/tmp/ini-rol-layout.json',JSON.stringify(resultados,null,2));
 console.log(JSON.stringify(resultados)); await browser.close();
})().catch(e=>{console.error(e.message);process.exit(1)});
