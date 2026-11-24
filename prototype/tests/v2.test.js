const fs = require('fs');
const html = fs.readFileSync(process.argv[2], 'utf8');
const src = html.split('// ===== LOGIC START =====')[1].split('// ===== LOGIC END =====')[0];
const L = new Function(src + '; return {CONFIG,roundCfg,prepRunCfg,createGame,updateGame,isWall,trapAt,bfsDist,hasLOS,zoneAt,pickup,bfsPath};')();
let pass = 0, fail = 0;
const ok = (c, m) => { if (c) { pass++; console.log('  PASS', m); } else { fail++; console.log('  FAIL', m); } };
const mk = n => L.createGame(L.prepRunCfg(L.roundCfg(n)));

console.log('1) 고정 맵·함정');
const g1 = mk(1), g2 = mk(2);
ok(g1.cols === 25 && g1.rows === 18, `v1 고정 맵 25×18 (${g1.cols}×${g1.rows})`);
ok(g1.traps.length === 0, `라운드 1 함정 0 (${g1.traps.length})`);
ok(g2.traps.length === 7, `라운드 2 함정 7 (${g2.traps.length})`);
ok(g1.relics.length === 3 && g2.relics.length === 3, '유물 3개');
ok(g1.cfg.seed === 0 && /고정/.test(g1.log[0].msg), '고정 맵 표시(시드 0)');
ok(L.CONFIG.guard.creepChance === 0, '어슬렁거림 꺼짐');
ok(g1.guards[0].x === 10.5 && g1.guards[0].y === 9.5, '수호자 석상 자리 (9,10)');

console.log('2) 기름병');
ok(g1.oils.length === 5, `기름병 5개 (${g1.oils.length})`);
const D = L.bfsDist(g1, g1.start.r, g1.start.c);
ok(g1.oils.every(o => !g1.grid[o.r][o.c] && D[o.r][o.c] < Infinity), '모든 기름병이 바닥이며 도달 가능');
ok(g1.oils.every(o => !g1.relics.some(r => r.r === o.r && r.c === o.c)), '기름병이 유물 자리와 겹치지 않음');

console.log('3) 기름 안에 풀 수 있는가 (걷는 시간·횃불 필요량 추정)');
const sp = L.CONFIG.playerSpeed;
const dStart = L.bfsDist(g1, g1.start.r, g1.start.c);
const rel = g1.relics.slice().sort((a, b) => a.value - b.value);
let best = Infinity;   // 시작→세 유물 모든 순서→시작 최단 걷기(칸)
const perms = a => a.length < 2 ? [a] : a.flatMap((x, i) => perms([...a.slice(0, i), ...a.slice(i + 1)]).map(p => [x, ...p]));
for (const p of perms(rel)) {
  let pos = g1.start, tot = 0;
  for (const r of p) { const d = L.bfsDist(g1, pos.r, pos.c); tot += d[r.r][r.c]; pos = r; }
  tot += L.bfsDist(g1, pos.r, pos.c)[g1.start.r][g1.start.c]; best = Math.min(best, tot);
}
console.log(`  (참고) 세 유물 왕복 최단 ${best}칸 ≈ ${(best / sp).toFixed(1)}초`);
ok(best / sp < L.CONFIG.collapse.startTime / 2, `최단 왕복이 붕괴 시간의 절반 미만 (${(best / sp).toFixed(1)}s)`);
// 횃불은 유물 근처(반경 5칸)에서만 켠다고 가정: 유물마다 1.5초 + 켜는 시간 → 기름 18초로 충분한지
const litNeed = rel.length * (L.CONFIG.torch.lightDelay + 4);
ok(litNeed <= L.CONFIG.torch.fuelStart, `유물 3개를 각각 약 5초씩 비추는 데 ${litNeed}초 ≤ 시작 기름 ${L.CONFIG.torch.fuelStart}초`);

console.log('4) 수호자 위치와 유물 거리 (가설 확인용 수치)');
for (const r of rel) console.log(`  (참고) ${r.name}: 수호자 석상과 직선 ${Math.hypot(r.c - 10, r.r - 9).toFixed(1)}칸, 입구에서 걷기 ${dStart[r.r][r.c]}칸`);
ok(true, '수치 출력');

console.log('5) 구역 이름·반짝임 설정');
ok(L.zoneAt(g1, 15, 3) === '시작 방', `구역 이름 (${L.zoneAt(g1, 15, 3)})`);
ok(L.CONFIG.sparkleRange > 0, `반짝임 거리 ${L.CONFIG.sparkleRange}`);

console.log('6) 기름 획득');
const g = mk(1), o = g.oils[0]; g.fuel = 3; g.player.x = o.c + 0.5; g.player.y = o.r + 0.5; L.updateGame(g, 0.05, { dx: 0, dy: 0 });
ok(g.oils[0].taken && g.fuel > 10, `기름병을 밟으면 +${L.CONFIG.torch.oilRefill}초 (${g.fuel.toFixed(1)})`);

console.log(`\n${pass} passed, ${fail} failed`); process.exit(fail ? 1 : 0);
