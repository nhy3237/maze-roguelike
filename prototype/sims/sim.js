const fs=require('fs');const html=fs.readFileSync(process.argv[2],'utf8');
const src=html.split('// ===== LOGIC START =====')[1].split('// ===== LOGIC END =====')[0];
const L=new Function(src+'; return {CONFIG,roundCfg,prepRunCfg,createGame,updateGame,toggleLight};')();
function run(label, after){
  const g=L.createGame(L.prepRunCfg(L.roundCfg(1)));
  g.pickups=1; g.invuln=0;                       // 수호자 깨움
  const gd=g.guards[0]; gd.state='patrol';
  g.player.x=14.5; g.player.y=9.5; gd.x=10.5; gd.y=9.5; gd.path=[];   // 중앙 홀, 4칸 거리
  g.light=true; g.litTime=0;
  const step=(n,inp)=>{for(let i=0;i<n;i++)L.updateGame(g,0.05,inp||{dx:0,dy:0});};
  step(8);                                      // 불빛을 봄 → 경계/추격
  const s1=gd.state, t1=gd.target&&[gd.target.r,gd.target.c];
  L.toggleLight(g);                             // 횃불 끔
  after(g,gd,step);
  console.log(label,'| 봄 직후:',s1,t1,'| 끈 뒤 상태:',gd.state,'| g.state:',g.state,'| 수호자:',gd.x.toFixed(1),gd.y.toFixed(1),'| 플레이어:',g.player.x.toFixed(1),g.player.y.toFixed(1));
}
run('A 끄고 제자리',(g,gd,step)=>{step(30);  console.log('   1.5초 뒤 상태',gd.state); step(120);});
run('B 끄고 3칸 비켜 정지',(g,gd,step)=>{step(10,{dx:0,dy:-1}); step(80); console.log('   중간',gd.state); step(120);});
run('C 끄고 동쪽으로 4칸 이동 후 정지',(g,gd,step)=>{step(20,{dx:1,dy:0}); const log=[]; for(let i=0;i<260;i++){step(1); if(i%20==0)log.push(gd.state+'@'+gd.x.toFixed(1)+','+gd.y.toFixed(1));} console.log('   ',log.join(' ')); });
