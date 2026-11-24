const fs=require('fs');const html=fs.readFileSync(process.argv[2],'utf8');
const src=html.split('// ===== LOGIC START =====')[1].split('// ===== LOGIC END =====')[0];
const L=new Function(src+'; return {CONFIG,roundCfg,prepRunCfg,createGame,updateGame,pickup,bfsPath};')();
function trial(warm, nPrev, viaShort, torchOff=true){
  const cfg=L.prepRunCfg(L.roundCfg(1)); cfg.collapse={...cfg.collapse,enabled:false};
  const g=L.createGame(cfg); g.invuln=0;
  const c=g.relics.find(r=>r.key==='c'), others=g.relics.filter(r=>r.key!=='c');
  g.player.x=c.c+0.5; g.player.y=c.r+0.5;
  if(nPrev>0){ g.pickups=1; for(let t=0;t<warm*20;t++) L.updateGame(g,0.05,{dx:0,dy:0}); }   // 수호자가 깨어 순찰한 시간(내 소지 없음 → 소리 없음)
  for(let i=0;i<nPrev;i++){ L.pickup(g,others[i]); }          // 그 뒤 먼저 주운 유물을 들고 도착했다고 가정
  L.pickup(g,c);
  const gd=g.guards[0], start=g.start;
  let t=0, minD=99;
  while(t<90 && g.state==='play'){
    // 우회로B 강제: 지름길B의 (7,20) 막기
    let saved=null; if(!viaShort){ saved=g.grid[7][20]; g.grid[7][20]=1; }
    const path=L.bfsPath(g,Math.floor(g.player.y),Math.floor(g.player.x),start.r,start.c);
    if(!viaShort) g.grid[7][20]=saved;
    if(!path.length) return {res:'escape',t,minD};
    const nx=path[0].c+0.5, ny=path[0].r+0.5;
    L.updateGame(g,0.05,{dx:nx-g.player.x,dy:ny-g.player.y}); t+=0.05;
    minD=Math.min(minD,Math.hypot(gd.x-g.player.x,gd.y-g.player.y));
  }
  return {res:g.state==='play'?'timeout':g.state,t,minD};
}
for(const nPrev of [0,1,2]) for(const via of [true,false]){
  let esc=0,tot=0,ts=[],close=0;
  for(let w=0;w<60;w+=2){ const r=trial(w,nPrev,via); tot++; if(r.res==='escaped'||r.res==='escape') {esc++; ts.push(r.t);} if(r.minD<2) close++; if(nPrev===0) break; }
  console.log(`앞서 주운 유물 ${nPrev}개 / ${via?'지름길B':'우회로B'}: 탈출 ${esc}/${tot}, 수호자와 2칸 이내로 붙은 판 ${close}${ts.length?', 평균 '+(ts.reduce((a,b)=>a+b,0)/ts.length).toFixed(1)+'초':''}`);
}
