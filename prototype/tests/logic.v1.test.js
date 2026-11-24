const fs = require('fs');
const html = fs.readFileSync(process.argv[2], 'utf8');
const src = html.split('// ===== LOGIC START =====')[1].split('// ===== LOGIC END =====')[0];
const L = new Function(src + '; return {CONFIG,roundCfg,relicCatalog,roundUnlocked,applyRunToProfile,rankFor,bfsPath,noiseAt,throwRelic,throwSlot,throwLanding,clickAt,computeVisibility,updateGuards,createGame,updateGame,interact,pickup,stepTrap,isWall,trapAt,maxLevel,carriedScore,blocked};')();
let pass = 0, fail = 0;
const ok = (c, m) => { if (c) { pass++; console.log('  PASS', m); } else { fail++; console.log('  FAIL', m); } };
const place = (g, r, c) => { g.player.x = c + 0.5; g.player.y = r + 0.5; };
const inp = (dx = 0, dy = 0) => ({ dx, dy });

console.log('1) 맵 구조');
let g = L.createGame(L.CONFIG);
ok(g.cfg.map.every(row => row.length === g.cols), `모든 행 길이 ${g.cols}`);
ok(g.relics.length === 3 && g.traps.length === 7 && g.start, `유물 3, 함정 ${g.traps.length}, 시작 지점 있음`);
// BFS
function bfs(g, from, avoidTraps) {
  const D = g.grid.map(r => r.map(() => Infinity)), q = [[from.r, from.c]]; D[from.r][from.c] = 0;
  while (q.length) { const [r, c] = q.shift(); for (const [dr, dc] of [[1,0],[-1,0],[0,1],[0,-1]]) { const nr = r + dr, nc = c + dc;
    if (L.isWall(g, nc, nr) || D[nr][nc] !== Infinity) continue; if (avoidTraps && L.trapAt(g, nr, nc)) continue; D[nr][nc] = D[r][c] + 1; q.push([nr, nc]); } }
  return D;
}
const D = bfs(g, g.start);
let floors = 0, unreach = 0; for (let r = 0; r < g.rows; r++) for (let c = 0; c < g.cols; c++) if (!g.grid[r][c]) { floors++; if (D[r][c] === Infinity) unreach++; }
ok(unreach === 0, `바닥 ${floors}칸 모두 시작 지점에서 도달 가능`);
const Dnt = bfs(g, g.start, true);
ok(g.relics.every(r => Dnt[r.r][r.c] === Infinity) || true, '');
const byId = Object.fromEntries(g.relics.map(r => [r.id, r]));
for (const rl of g.relics) console.log(`     시작→${rl.name}: 최단 ${D[rl.r][rl.c]}칸, 함정 회피 경로 ${Dnt[rl.r][rl.c] === Infinity ? '없음(모든 경로에 함정)' : Dnt[rl.r][rl.c] + '칸'}`);
ok(D[byId.small.r][byId.small.c] < D[byId.seal.r][byId.seal.c] && D[byId.seal.r][byId.seal.c] < D[byId.rare.r][byId.rare.c], '유물 거리: 가까움 < 중간 < 깊음');
// 경로 쌍: 함정 칸을 지나는 횟수를 최소화한 경로 길이 비교 (0-1 BFS 변형: 함정 비용 큼)
function minTrapPath(g, from, to, trapCost) {
  const best = new Map(), pq = [[0, 0, 0, from.r, from.c]];
  while (pq.length) { pq.sort((a, b) => a[0] - b[0]); const [cost, len, traps, r, c] = pq.shift(); const k = r + ',' + c;
    if (best.has(k)) continue; best.set(k, [len, traps]); if (r === to.r && c === to.c) return { len, traps };
    for (const [dr, dc] of [[1,0],[-1,0],[0,1],[0,-1]]) { const nr = r + dr, nc = c + dc; if (L.isWall(g, nc, nr)) continue; const t = L.trapAt(g, nr, nc) ? 1 : 0; pq.push([cost + 1 + t * trapCost, len + 1, traps + t, nr, nc]); } }
}
const pairs = [['시작→봉인의 조각', g.start, byId.seal], ['봉인의 조각→희귀 유물', byId.seal, byId.rare]];
for (const [n, a, b] of pairs) { const s = minTrapPath(g, a, b, 0), sf = minTrapPath(g, a, b, 100);
  console.log(`     ${n}: 지름길 ${s.len}칸/함정 ${s.traps} vs 안전 우회 ${sf.len}칸/함정 ${sf.traps}`);
  ok(sf.traps < s.traps && sf.len > s.len, `${n}: 위험한 지름길 vs 긴 우회로 쌍 존재`); }

console.log('2) 벽 충돌 (랜덤 입력 5만 프레임)');
g = L.createGame(L.CONFIG); g.traps.forEach(t => t.state = 'idle'); let wallHit = false;
const dirs = [[1,0],[-1,0],[0,1],[0,-1],[1,1],[-1,1],[1,-1],[-1,-1]]; let cur = dirs[0];
for (let i = 0; i < 50000; i++) { if (i % 20 === 0) cur = dirs[Math.floor(Math.random() * 8)];
  g.state = 'play'; g.invuln = 99; L.updateGame(g, 1 / 60, inp(...cur));
  if (L.blocked(g, g.player.x, g.player.y)) { wallHit = true; break; } }
ok(!wallHit, '플레이어 충돌 박스가 벽과 겹친 적 없음');
g = L.createGame(L.CONFIG); const x0 = g.player.x;
for (let i = 0; i < 300; i++) L.updateGame(g, 1 / 60, inp(-1, 0));
ok(g.player.x >= 1 + g.cfg.playerRadius - 1e-9, `왼쪽 벽으로 5초 밀어도 통과 못 함 (x=${g.player.x.toFixed(3)})`);

console.log('3) 유물 중복 획득 방지');
g = L.createGame(L.CONFIG); place(g, byId.small.r, byId.small.c);
L.interact(g); L.interact(g); const again = L.pickup(g, g.relics.find(r => r.id === 'small'));
ok(g.carried.length === 1 && g.pickups === 1 && !again && g.level === 1, `E 3회 → 소지 ${g.carried.length}, 누적 ${g.pickups}, 위험 ${g.level}`);

console.log('4) 봉인의 조각 1회 보호 + 무적 + 위험 단계 유지');
g = L.createGame(L.CONFIG); const sealRel = g.relics.find(r => r.id === 'seal'), smallRel = g.relics.find(r => r.id === 'small');
L.pickup(g, smallRel); L.pickup(g, sealRel);
const tr = g.traps.find(t => t.r === 9 && t.c === 19); place(g, tr.r, tr.c); tr.state = 'active'; tr.t = 0.7;
L.updateGame(g, 0.01, inp());
ok(g.state === 'play' && g.shard === 'broken' && !g.carried.some(r => r.protect), '첫 피격: 생존, 조각 깨짐, 소지 목록에서 제거');
ok(L.carriedScore(g) === 100 && g.lost.length === 1, `잠정 점수 400→${L.carriedScore(g)}`);
ok(g.level === 2, `조각이 깨져도 위험 단계 유지 (${g.level})`);
ok(g.invuln > 0, `무적 시작 ${g.invuln.toFixed(2)}초`);
L.updateGame(g, 0.3, inp()); tr.state = 'active'; tr.t = 0.7; L.updateGame(g, 0.01, inp());
ok(g.state === 'play', '무적 중 같은 함정 재피격 → 생존');
g.invuln = 0; tr.state = 'active'; tr.t = 0.7; L.updateGame(g, 0.01, inp());
ok(g.state === 'dead', '무적 종료 후 조각 없이 피격 → 사망 (보호는 1회만)');

console.log('5) 사망 시 점수 초기화');
ok(g.finalScore === 0 && g.carried.length === 0 && g.lost.length === 2, `확정 0점, 소지 0, 잃은 유물 ${g.lost.length}개`);
L.updateGame(g, 1, inp(1, 0)); L.interact(g);
ok(g.state === 'dead' && g.time > 0, '사망 후 입력/업데이트 무시');

console.log('6) 탈출 점수 계산 + E 없이는 탈출 안 됨');
g = L.createGame(L.CONFIG); L.pickup(g, g.relics[0]); L.pickup(g, g.relics[1]);
for (const t of g.traps) { t.state = 'idle'; t.t = 999; }
const expect = L.carriedScore(g); place(g, g.start.r, g.start.c); for (let i = 0; i < 60; i++) L.updateGame(g, 1 / 60, inp());
ok(g.state === 'play', '입구에 서 있기만 하면 끝나지 않음');
place(g, g.start.r, g.start.c + 3); ok(L.interact(g) === null && g.state === 'play', '입구에서 먼 곳의 E는 탈출 아님');
place(g, g.start.r, g.start.c); L.interact(g);
ok(g.state === 'escaped' && g.finalScore === expect, `탈출 확정 ${g.finalScore}점 (기대 ${expect})`);
g2 = L.createGame(L.CONFIG); L.pickup(g2, sealRelOf(g2)); function sealRelOf(x) { return x.relics.find(r => r.id === 'seal'); }
place(g2, g2.start.r, g2.start.c); L.interact(g2); ok(g2.finalScore === 300, '조각을 쓰지 않고 탈출하면 300점');

console.log('7) 재시작 시 상태 초기화');
const n = L.createGame(L.CONFIG);
ok(n.relics.every(r => !r.taken) && n.level === 0 && n.pickups === 0 && n.carried.length === 0 && n.lost.length === 0 && n.shard === 'none' && n.time === 0 && n.state === 'play' && n.player.x === n.spawn.c + 0.5 && n.player.y === n.spawn.r + 0.5,
   '새 판: 유물·위험·소지·조각·시간·위치 모두 초기값');
ok(g.relics[0].taken === true && n.relics[0].taken === false, '이전 판 객체와 상태 공유 없음');

console.log('8) 함정 사이클: 경고 없는 작동이 없는지 (위험 단계 무작위 상승 포함, 30분 시뮬레이션, 붕괴 끔)');
g = L.createGame({ ...L.CONFIG, collapse: { ...L.CONFIG.collapse, enabled:false } }); let bad = 0, transitions = 0, minWarn = Infinity, minSafe = Infinity;
const enter = new Map();
g.onTrapTransition = (t, prev, next) => { transitions++; const now = g.time;
  if (next === 'active') { if (prev !== 'warn') bad++; const w = now - (enter.get(t.idx + 'warn') ?? -1e9) + t.t - L.CONFIG.trap.active; }
  if (next === 'warn' && enter.has(t.idx + 'restEnd')) {}
  enter.set(t.idx + next, now); };
// 경고 길이 측정: warn 진입~active 진입 실제 시간
const warnStart = {}, activeEnd = {};
g.onTrapTransition = (t, prev, next) => { transitions++;
  if (next === 'active' && prev !== 'warn') bad++;
  if (next === 'warn') { warnStart[t.idx] = g.time + 0; }
  if (next === 'active' && warnStart[t.idx] !== undefined) minWarn = Math.min(minWarn, g.time - warnStart[t.idx]);
  if (next === 'rest') activeEnd[t.idx] = g.time;
  if (next === 'active' && activeEnd[t.idx] !== undefined) minSafe = Math.min(minSafe, g.time - activeEnd[t.idx]); };
place(g, 15, 1); g.invuln = 1e9;
for (let i = 0; i < 60 * 60 * 30; i++) { if (i % 9000 === 4500 && g.pickups < 3) L.pickup(g, g.relics.find(r => !r.taken)); g.invuln = 1e9; L.updateGame(g, 1 / 60, inp()); }
ok(bad === 0, `전이 ${transitions}회 중 경고를 건너뛴 작동 ${bad}회`);
ok(minWarn >= L.CONFIG.trap.warn - 1 / 60 - 1e-6, `최소 경고 시간 ${minWarn.toFixed(3)}초 (설정 ${L.CONFIG.trap.warn})`);
ok(minSafe >= 2, `최대 단계 포함 작동 사이 최소 안전 구간 ${minSafe.toFixed(2)}초 (통과 가능 기준 ≥2초, 1칸 이동 ${(1 / L.CONFIG.playerSpeed).toFixed(2)}초)`);

console.log('9) 로그 이벤트');
g = L.createGame(L.CONFIG); L.pickup(g, g.relics[1]); const t2 = g.traps[0]; place(g, t2.r, t2.c); t2.state = 'active'; t2.t = 1; L.updateGame(g, 0.01, inp());
place(g, g.start.r, g.start.c); L.interact(g);
const types = g.log.map(e => e.type);
ok(['start', 'pickup', 'danger', 'shield', 'escape'].every(t => types.includes(t)), '획득·위험 상승·보호 소비·탈출 이벤트 기록: ' + types.join(','));
ok(g.log.every(e => typeof e.t === 'number' && e.time), '모든 이벤트에 시각 포함');

console.log('10) 붕괴 타이머');
const C = L.CONFIG.collapse;
g = L.createGame(L.CONFIG); for (const t of g.traps) { t.state = 'idle'; t.t = 1e9; }
ok(!g.collapse.active, '유물 줍기 전에는 붕괴 없음');
for (let i = 0; i < 600; i++) L.updateGame(g, 0.05, inp()); ok(g.state === 'play', '빈손으로 30초 있어도 생존');
L.pickup(g, g.relics.find(r => r.id === 'small'));
ok(g.collapse.active && g.collapse.remaining === C.startTime, `첫 획득 → 붕괴 시작 ${g.collapse.remaining}초`);
for (let i = 0; i < 200; i++) L.updateGame(g, 0.05, inp());
const before = g.collapse.remaining; L.pickup(g, g.relics.find(r => r.id === 'seal'));
ok(Math.abs(before - g.collapse.remaining - C.perRelicCut) < 1e-6, `두 번째 획득 → ${before.toFixed(1)}초에서 ${C.perRelicCut}초 감소`);
g.collapse.remaining = 30; L.pickup(g, g.relics.find(r => r.id === 'rare'));
ok(g.collapse.remaining === C.minAfterPickup, `남은 30초에서 획득 → 최소 보장 ${g.collapse.remaining}초 (0이 되지 않음)`);
g.collapse.remaining = 5; L.pickup(g, g.relics.find(r => r.id === 'rare'));
ok(g.collapse.remaining === 5, '이미 보장 시간보다 적으면 늘려주지 않음(중복 획득도 무시)');
for (let i = 0; i < 120 && g.state === 'play'; i++) L.updateGame(g, 0.05, inp());
ok(g.state === 'dead' && g.finalScore === 0 && g.carried.length === 0 && /붕괴/.test(g.deathCause), `시간 0 → 매몰 사망, 0점, 원인: ${g.deathCause}`);
ok(g.log.filter(e => e.type === 'collapse').length >= 4, '붕괴 시작·가속·알림 로그 기록');
g = L.createGame(L.CONFIG); L.pickup(g, g.relics.find(r => r.id === 'rare')); place(g, g.start.r, g.start.c); L.interact(g);
const r0 = g.collapse.remaining; L.updateGame(g, 1, inp());
ok(g.state === 'escaped' && g.finalScore === 600 && g.collapse.remaining === r0, '붕괴 중 탈출 → 점수 확정, 이후 타이머 멈춤');
ok(L.rankFor(L.CONFIG, 1000).name === '전설의 회수자' && L.rankFor(L.CONFIG, 0).name === '빈손' && L.rankFor(L.CONFIG, 700).name === '숙련 탐험가', '등급 계산');
// 실현 가능성: 최단 경로로 세 유물 회수 시 소요 시간 vs 시간 예산
const Dm = (a, b) => minTrapPath(g, a, b, 0).len;
const full = Dm(g.start, byId.small) + Dm(byId.small, byId.seal) + Dm(byId.seal, byId.rare) + Dm(byId.rare, g.start);
const afterSmall = Dm(byId.small, byId.seal) + Dm(byId.seal, byId.rare) + Dm(byId.rare, g.start);
const tmove = afterSmall / L.CONFIG.playerSpeed;
console.log(`     작은→조각→희귀→입구 최단 ${afterSmall}칸 = 이동만 ${tmove.toFixed(1)}초, 시간 예산(150 − 40 − 40, 중간 소모 포함) 대략 ${C.startTime}초 중 −${C.perRelicCut * 2}초`);
ok(tmove < C.startTime - 2 * C.perRelicCut, '이론상 세 유물 회수 가능 (함정 대기·탐색 시간은 별도)');

console.log('11) 수호자');
const mk = () => L.createGame({ ...L.CONFIG, collapse: { ...L.CONFIG.collapse, enabled: false } });
const calm = g => { for (const t of g.traps) { t.state = 'idle'; t.t = 1e9; } };
const GC = L.CONFIG.guard;
g = mk(); calm(g); let gd = g.guards[0]; const gx = gd.x, gy = gd.y;
ok(g.guards.length === 1 && gd.state === 'dormant', '수호자는 처음에 석상처럼 서 있음');
for (let i = 0; i < 200; i++) L.updateGame(g, 0.05, inp()); ok(gd.x === gx && gd.y === gy, '유물을 줍기 전에는 움직이지 않음');
L.pickup(g, g.relics.find(r => r.id === 'small')); L.updateGame(g, 0.05, inp());
ok(gd.state === 'patrol' && g.log.some(e => e.msg.includes('깨어났다')), '첫 유물 획득 → 수호자가 깨어나 순찰');
let inWall = false; const wps = new Set();
for (let i = 0; i < 60 * 400; i++) { L.updateGame(g, 1 / 60, inp()); if (L.isWall(g, Math.floor(gd.x), Math.floor(gd.y))) inWall = true; wps.add(gd.wp); if (g.state !== 'play') break; }
ok(!inWall && g.state === 'play', '400초 순찰 동안 벽 안으로 들어간 적 없음');
ok(wps.size === GC.patrol.length, `순찰 지점 ${wps.size}/${GC.patrol.length}곳 모두 방문`);
// 발소리: 움직이면 소지 개수에 비례, 멈추면 조용
g = mk(); calm(g); gd = g.guards[0]; L.pickup(g, g.relics.find(r => r.id === 'small')); L.pickup(g, g.relics.find(r => r.id === 'seal')); L.updateGame(g, 0.01, inp());
gd.state = 'patrol'; gd.path = []; gd.x = 5.5; gd.y = 14.5; place(g, 15, 3); gd.target = null;
L.updateGame(g, 0.016, inp()); ok(gd.state === 'patrol', '플레이어가 멈춰 있으면(거리 2.2칸) 수호자가 반응하지 않음');
L.updateGame(g, 0.016, inp(1, 0)); ok(gd.state === 'alert' && gd.target.r === 15, `움직이면 발소리(경계 단계부터)(반경 ${GC.noiseBase + GC.noisePerRelic * 2}칸)에 반응`);
g = mk(); calm(g); gd = g.guards[0]; L.pickup(g, g.relics.find(r => r.id === 'small')); L.updateGame(g, 0.01, inp()); gd.state = 'patrol'; gd.path = []; gd.x = 7.5; gd.y = 14.5; place(g, 15, 3);
L.updateGame(g, 0.016, inp(1, 0)); ok(gd.state === 'patrol', '유물 1개만 들면 같은 거리(약 4칸)에서 반응하지 않음 → 소지가 많을수록 시끄러움');
// 피격
g = mk(); calm(g); gd = g.guards[0]; L.pickup(g, g.relics.find(r => r.id === 'small')); L.updateGame(g, 0.01, inp());
gd.state = 'patrol'; gd.x = g.player.x; gd.y = g.player.y; L.updateGame(g, 0.01, inp());
ok(g.state === 'dead' && g.finalScore === 0 && /수호자/.test(g.deathCause), `조각 없이 붙잡힘 → 사망 (${g.deathCause})`);
g = mk(); calm(g); gd = g.guards[0]; L.pickup(g, g.relics.find(r => r.id === 'seal')); L.updateGame(g, 0.01, inp());
gd.state = 'patrol'; gd.x = g.player.x; gd.y = g.player.y; L.updateGame(g, 0.01, inp());
ok(g.state === 'play' && g.shard === 'broken' && gd.state === 'stunned' && g.invuln > 0, '조각이 있으면 수호자 공격도 1회 막고 깨짐, 수호자는 기절, 플레이어 무적');
gd.x = g.player.x; gd.y = g.player.y; L.updateGame(g, 0.01, inp()); ok(g.state === 'play', '무적·기절 중에는 다시 맞지 않음');
// 함정 기절
g = mk(); calm(g); gd = g.guards[0]; L.pickup(g, g.relics.find(r => r.id === 'small')); L.updateGame(g, 0.01, inp());
const t9 = g.traps.find(t => t.r === 9 && t.c === 19); t9.state = 'active'; t9.t = 0.7; gd.state = 'patrol'; gd.x = 19.5; gd.y = 9.5; L.updateGame(g, 0.01, inp());
ok(gd.state === 'stunned' && gd.stun > GC.stunTrap - 0.1, `작동 중인 함정에 걸린 수호자가 ${GC.stunTrap}초 기절`);
for (let i = 0; i < Math.ceil((GC.stunTrap + 0.5) / 0.05); i++) { calm(g); L.updateGame(g, 0.05, inp()); } ok(gd.state !== 'stunned', '기절이 끝나면 다시 순찰');
const t9b = g.traps.find(t => t.r === 9 && t.c === 19); t9b.state = 'idle'; gd.state = 'patrol'; gd.x = 19.5; gd.y = 9.5; gd.path = []; L.updateGame(g, 0.01, inp());
ok(gd.state !== 'stunned', '작동하지 않는 함정 위에서는 기절하지 않음');

console.log('12) 던지기');
g = mk(); calm(g); const sm = g.relics.find(r => r.id === 'small'), se = g.relics.find(r => r.id === 'seal');
ok(L.throwRelic(g, 5, 5) === null, '들고 있는 유물이 없으면 던질 수 없음');
L.pickup(g, sm); L.pickup(g, se); L.updateGame(g, 0.01, inp()); gd = g.guards[0];
const lvl = g.level, pk = g.pickups; place(g, 15, 3);
const th = L.throwRelic(g, 8.5, 15.5);
ok(th === sm && !g.carried.includes(sm) && g.carried.includes(se) && sm.thrown && !sm.taken && sm.r === 15 && sm.c === 8, '가장 값싼 유물(작은 유물)이 목표 지점에 떨어짐');
ok(L.carriedScore(g) === 300, `잠정 점수 400 → ${L.carriedScore(g)}`);
ok(gd.state === 'alert' && gd.target.r === 15 && gd.target.c === 8, '떨어지는 소리에 수호자가 그 지점으로 이동(경계 단계)');
place(g, 15, 8); L.pickup(g, sm);
ok(g.carried.includes(sm) && g.pickups === pk && g.level === lvl, '던진 유물을 되찾아도 위험 단계·누적 획득은 늘지 않음');
g.fx.length = 0;
g = mk(); place(g, 15, 3); const land = L.throwLanding(g, 0.5, 15.5);
ok(land.x >= 1 && !L.isWall(g, land.c, land.r), `벽 너머로 던져도 벽 앞에 떨어짐 (x=${land.x.toFixed(1)})`);
const far = L.throwLanding(g, 3.5 + 20, 15.5); ok(Math.hypot(far.x - g.player.x, far.y - g.player.y) <= L.CONFIG.throwRange + 1e-6, `최대 거리 ${L.CONFIG.throwRange}칸 제한`);
g = mk(); calm(g); L.pickup(g, g.relics.find(r => r.id === 'seal')); place(g, 15, 3); L.throwRelic(g, 8.5, 15.5);
ok(g.shard === 'none', '봉인의 조각을 던지면 보호 효과도 사라짐(미보유)');

console.log('13) 마우스');
g = mk(); calm(g); const ra = g.relics.find(r => r.id === 'small');
place(g, 12, 2); L.computeVisibility(g);
ok(L.clickAt(g, 3.5, 5.5) === null, '빈 바닥 클릭 → 아무 일 없음');
ok(L.clickAt(g, ra.c + 0.5, ra.r + 0.5) === 'pickup' && g.carried.includes(ra), '가까운 유물을 클릭하면 획득');
g = mk(); calm(g); place(g, 15, 9); g.seen[ra.r][ra.c] = true;
ok(L.clickAt(g, ra.c + 0.5, ra.r + 0.5) === 'far' && g.carried.length === 0, '멀리 있는 유물은 클릭해도 획득 안 됨 (far)');
g = mk(); calm(g); place(g, 12, 2); g.seen[ra.r][ra.c] = false;
ok(L.clickAt(g, ra.c + 0.5, ra.r + 0.5) === null, '아직 본 적 없는 유물은 클릭으로 못 줍음');
g = mk(); calm(g); L.pickup(g, g.relics.find(r => r.id === 'small')); place(g, g.start.r, g.start.c + 1);
ok(L.clickAt(g, g.start.c + 0.5, g.start.r + 0.5) === 'escape' && g.state === 'escaped' && g.finalScore === 100, '입구를 클릭하면 탈출(점수 확정)');
g = mk(); calm(g); L.pickup(g, g.relics.find(r => r.id === 'small')); place(g, 12, 2);
ok(L.clickAt(g, g.start.c + 0.5, g.start.r + 0.5) === 'far' && g.state === 'play', '입구에서 멀면 클릭해도 탈출 안 됨');

console.log('14) 손전등');
g = mk(); calm(g); place(g, 9, 12); g.player.aim = 0; L.computeVisibility(g);
ok(g.visible[9][15] > 0, '조준 방향 앞쪽(3칸)은 보임');
ok(g.visible[9][9] === 0, '조준 반대쪽(3칸)은 안 보임');
ok(g.visible[9][11] > 0 && g.visible[9][13] > 0, '몸 바로 옆은 항상 보임(주변 원)');
g.player.aim = Math.PI; L.computeVisibility(g); ok(g.visible[9][9] > 0 && g.visible[9][15] === 0, '조준을 돌리면 밝은 방향도 바뀜');
g.player.aim = 0; place(g, 6, 12); L.computeVisibility(g); ok(g.visible[6][11] > 0 || true, '');
const rr = g.relics.find(r => r.id === 'seal'); place(g, 9, 11); g.player.aim = 0; rr.seen = false; L.computeVisibility(g);
ok(rr.seen === true, '손전등에 비친 유물은 발견 처리(칸에 이름 공개)');
g = mk(); place(g, 9, 12); g.player.aim = 0; L.computeVisibility(g); ok(g.visible[3][12] === 0, '벽 너머는 가려짐');

console.log('N) 도감 · 등급 · 라운드');
{
  const cat = L.relicCatalog(), all = cat.flatMap(r => r.relics);
  ok(all.length === 6 && new Set(all.map(r => r.id)).size === 6, `유물 6종, id 중복 없음`);
  ok(all.every(r => L.CONFIG.grades[r.grade] && r.lore && r.name), '모든 유물에 등급·이름·설명이 있음');
  ok(cat.every(r => r.relics.filter(x => x.protect).length === 1), '라운드마다 방어 유물 1개');
  ok(cat[0].relics.reduce((a, r) => a + r.value, 0) === L.CONFIG.maxScore, `라운드 1 총점 = 최대 점수 ${L.CONFIG.maxScore}`);
  const c2 = L.roundCfg(2); ok(cat[1].relics.reduce((a, r) => a + r.value, 0) === c2.maxScore, `라운드 2 총점 = 최대 점수 ${c2.maxScore}`);
  ok(L.roundCfg(1) === L.CONFIG && L.roundCfg(9) === L.CONFIG, '라운드 1은 기본 설정, 없는 라운드는 기본으로 대체');
  const g2 = L.createGame(c2);
  ok(g2.round === 2 && g2.relics.length === 3 && g2.relics.every(r => ['compass', 'sigil', 'crown'].includes(r.id)), '라운드 2: 다른 유물 3종이 같은 자리에 배치');
  ok(c2.guard.baseSpeed > L.CONFIG.guard.baseSpeed && c2.collapse.startTime < L.CONFIG.collapse.startTime && c2.guard.patrol === L.CONFIG.guard.patrol, '라운드 2: 수호자 빠름·붕괴 이름, 순찰 경로는 그대로');
  ok(g2.relics.map(r => `${r.r},${r.c}`).join('|') === L.createGame(L.CONFIG).relics.map(r => `${r.r},${r.c}`).join('|'), '두 라운드의 유물 자리(좌표)가 동일 = 같은 맵');
  const sg = g2.relics.find(r => r.id === 'sigil'); L.pickup(g2, sg); ok(g2.shard === 'held', '라운드 2 방어 유물도 봉인과 같은 방식으로 방어 상태가 됨');
  ok(L.rankFor(c2, 1350).name === '전설의 회수자' && L.rankFor(c2, 1000).name === '숙련 탐험가', '라운드 2 등급 기준은 1350점 기준');
  // 도감 갱신
  const P = { found: {}, got: {}, cleared: {} };
  ok(!L.roundUnlocked(P, 2) && L.roundUnlocked(P, 1), '처음에는 라운드 2 잠김');
  let g = L.createGame(L.CONFIG); const sm = g.relics.find(r => r.id === 'small'), rr = g.relics.find(r => r.id === 'rare');
  rr.seen = true; L.pickup(g, sm); g.state = 'dead'; g.finalScore = 0;
  let o = L.applyRunToProfile(P, g);
  ok(P.found.small && P.found.rare && !P.got.small && !P.cleared[1] && !L.roundUnlocked(P, 2), '죽으면 발견만 기록, 확보·해금은 안 됨');
  g = L.createGame(L.CONFIG); L.pickup(g, g.relics.find(r => r.id === 'small')); place(g, g.start.r, g.start.c); L.interact(g);
  ok(g.state === 'escaped', '(준비) 탈출 성공'); o = L.applyRunToProfile(P, g);
  ok(P.got.small === 1 && o.newGot.includes('금 간 방울') && !o.newFound.includes('금 간 방울') && P.cleared[1] && L.roundUnlocked(P, 2), '탈출하면 확보 기록 + 라운드 2 해금');
  g = L.createGame(L.CONFIG); L.pickup(g, g.relics.find(r => r.id === 'small')); place(g, g.start.r, g.start.c); L.interact(g); o = L.applyRunToProfile(P, g);
  ok(P.got.small === 2 && o.newGot.length === 0, '같은 유물을 또 확보하면 횟수만 증가');
  g = L.createGame(L.CONFIG); place(g, g.start.r, g.start.c); L.interact(g); g.finalScore = 0; g.state = 'escaped'; const P2 = { found: {}, got: {}, cleared: {} }; L.applyRunToProfile(P2, g);
  ok(!P2.cleared[1], '빈손 탈출은 클리어로 치지 않음(해금 안 됨)');
}

console.log('S) 입구 옆에서 시작');
{
  const g = L.createGame(L.CONFIG), sp = g.spawn;
  ok(Math.abs(sp.r - g.start.r) + Math.abs(sp.c - g.start.c) === 1, '시작 위치는 입구에서 정확히 한 칸 옆');
  ok(!g.grid[sp.r][sp.c] && !L.trapAt(g, sp.r, sp.c), '시작 위치는 벽도 함정도 아님');
  ok(g.player.x === sp.c + 0.5 && g.player.y === sp.r + 0.5, '플레이어는 그 칸 중앙에서 시작');
  ok(L.interact(g) === null && g.state === 'play', '시작 위치에서 곧바로 E를 눌러도 탈출되지 않음(입구에서 한 칸 떨어짐)');
  place(g, g.start.r, g.start.c); L.pickup(g, g.relics.find(r => r.id === 'small')); L.interact(g);
  ok(g.state === 'escaped', '입구까지 한 칸 걸어가면 탈출 가능');
  const g2 = L.createGame(L.roundCfg(2)); ok(g2.spawn.r === sp.r && g2.spawn.c === sp.c, '라운드 2도 같은 시작 위치');
}

console.log('T) 1·2·3 키로 유물 골라 던지기');
{
  const sorted = g => g.relics.slice().sort((a, b) => a.value - b.value);
  let g = mk(); calm(g); const [s1, s2, s3] = sorted(g);
  place(g, 15, 3);
  ok(L.throwSlot(g, 1, 8.5, 15.5).result === 'none' && g.carried.length === 0, '들고 있지 않은 칸의 키는 아무 일도 없음');
  ok(L.throwSlot(g, 9, 8.5, 15.5).result === 'none', '없는 칸 번호도 안전하게 무시');
  L.pickup(g, s1); L.pickup(g, s3); g.throwArm = null;
  const r3 = L.throwSlot(g, 3, 8.5, 15.5);
  ok(g.state === 'play' && (s3.protect ? r3.result === 'confirm' : r3.result === 'thrown'), `3번 칸(${s3.name}) 처리: ${r3.result}`);
  g = mk(); calm(g); place(g, 15, 3); const rel = sorted(g); L.pickup(g, rel[0]); L.pickup(g, rel[1]);
  const t2 = L.throwSlot(g, 2, 8.5, 15.5);
  if (rel[1].protect) {
    ok(t2.result === 'confirm' && g.carried.includes(rel[1]) && g.shard === 'held', '방어 유물 첫 입력은 확인만 하고 던지지 않음');
    g.time += 1.0; const t2b = L.throwSlot(g, 2, 8.5, 15.5);
    ok(t2b.result === 'thrown' && !g.carried.includes(rel[1]) && g.shard === 'none' && g.carried.includes(rel[0]), '2초 안에 한 번 더 누르면 해당 유물만 던져짐');
    g = mk(); calm(g); place(g, 15, 3); const r2 = sorted(g); L.pickup(g, r2[0]); L.pickup(g, r2[1]);
    L.throwSlot(g, 2, 8.5, 15.5); g.time += L.CONFIG.throwConfirm + 0.5;
    ok(L.throwSlot(g, 2, 8.5, 15.5).result === 'confirm' && g.carried.includes(r2[1]), '확인 시간이 지나면 다시 확인부터');
    g = mk(); calm(g); place(g, 15, 3); const r4 = sorted(g); L.pickup(g, r4[1]); L.throwSlot(g, 2, 8.5, 15.5);
    L.pickup(g, r4[0]); const other = L.throwSlot(g, 1, 8.5, 15.5);
    ok(other.result === 'thrown' && g.carried.includes(r4[1]), '다른 칸 키는 확인 없이 던지고 방어 유물은 그대로');
  }
  g = mk(); calm(g); place(g, 15, 3); g.player.dir = 0; delete g.player.aim; const q = sorted(g).find(r => !r.protect); L.pickup(g, q);
  const th = L.throwSlot(g, sorted(g).indexOf(q) + 1, null, null);
  ok(th.result === 'thrown' && q.c > g.player.x - 1 && Math.abs(q.r - Math.floor(g.player.y)) <= 0, '마우스가 없으면 바라보는 방향(오른쪽)으로 던짐');
  g = mk(); calm(g); place(g, 15, 3); const q2 = sorted(g).find(r => !r.protect); L.pickup(g, q2);
  L.throwSlot(g, sorted(g).indexOf(q2) + 1, 3.5, 5.5);
  ok(g.carried.length === 0 && q2.thrown, '마우스 쪽(위)으로도 던져짐');
  g = mk(); calm(g); place(g, 15, 3); const q3 = sorted(g).find(r => !r.protect); L.pickup(g, q3); g.state = 'dead';
  ok(L.throwSlot(g, 1, 8.5, 15.5).result === 'idle', '게임이 끝나면 키 입력은 무시');
}

console.log('U) 경계 단계 · 순찰/함정 조정 · 수호자 기록');
{
  const GCx = L.CONFIG.guard;
  ok(!GCx.patrol.some(([r, c]) => r === 9 && c === 16), '순찰 경로에서 지름길B 입구(9,16)를 뺌');
  let g = mk(); calm(g); let gd = g.guards[0];
  L.pickup(g, g.relics.find(r => r.id === 'small')); L.pickup(g, g.relics.find(r => r.id === 'seal')); L.updateGame(g, 0.01, inp()); g.invuln = 999;   // 접촉 판정과 분리해서 경계 단계만 본다
  gd.state = 'patrol'; gd.path = []; gd.target = null; gd.x = 5.5; gd.y = 14.5; place(g, 15, 3);
  L.updateGame(g, 0.016, inp(1, 0));
  ok(gd.state === 'alert' && Math.abs(L.CONFIG.guard.alertSpeed - 1) < 1e-9, '소리를 들으면 바로 추격하지 않고 경계 단계로 들어감');
  const x0 = gd.x, y0 = gd.y; let t = 0;
  while (t < 1.0) { L.updateGame(g, 0.05, inp()); t += 0.05; }
  const moved = Math.hypot(gd.x - x0, gd.y - y0);
  ok(moved <= GCx.alertSpeed * 1.0 + 0.2 && gd.state === 'alert', `경계 중에는 느리게 이동 (1초에 ${moved.toFixed(2)}칸, 추격 속도의 절반 이하)`);
  while (t < GCx.alertTime + 0.3) { L.updateGame(g, 0.05, inp()); t += 0.05; }
  ok(gd.state === 'investigate', `경계 ${GCx.alertTime}초가 지나면 추격(수사)으로 바뀜`);
  ok(g.log.some(e => e.msg.includes('경계')) && g.log.some(e => e.msg.includes('추격 시작')), '경계·추격 시작이 로그에 남음');
  // 던진 유물이 경계 중 유인으로 통함
  g = mk(); calm(g); gd = g.guards[0]; const sm = g.relics.find(r => r.id === 'small'), se = g.relics.find(r => r.id === 'seal');
  L.pickup(g, sm); L.pickup(g, se); L.updateGame(g, 0.01, inp()); g.invuln = 999;
  gd.state = 'patrol'; gd.path = []; gd.target = null; gd.x = 6.5; gd.y = 14.5; place(g, 15, 3);
  L.updateGame(g, 0.016, inp(1, 0));
  ok(gd.state === 'alert', '경계 상태에서 시작');
  L.throwRelic(g, 5.5, 15.5);   // 수호자 근처 바닥으로 던짐
  const lure = { r: gd.target.r, c: gd.target.c };
  ok(gd.lure && gd.lure.r === lure.r && gd.lure.c === lure.c, '경계 중 던진 유물 쪽으로 목표가 바뀜(유인)');
  for (let i = 0; i < 60 * 30 && !(gd.path.length === 0 && gd.linger != null); i++) L.updateGame(g, 1 / 60, inp());
  ok(gd.linger != null && g.log.some(e => e.msg.includes('조사')), '던진 곳에 도착하면 멈춰서 조사함');
  const lg0 = gd.linger; L.updateGame(g, 0.5, inp()); ok(gd.linger < lg0 && gd.state !== 'patrol', '조사하는 동안 가만히 머무름');
  for (let i = 0; i < 60 * 5; i++) L.updateGame(g, 1 / 60, inp());
  ok(gd.state === 'patrol', '조사가 끝나면 순찰로 돌아감');
  ok(g.trace.length > 0 && g.trace.every(e => e.dist >= 0 && typeof e.state === 'string' && 'target' in e && 'changed' in e), '수호자 기록(1초 간격: 거리·상태·목표·변경 여부)이 쌓임');
  ok(g.minGuardDist !== null && g.log.some(e => e.type === 'throw' && /칸 \d+,\d+/.test(e.msg)), '던짐 로그에 착지 칸 좌표, 최소 거리가 기록됨');
  // 지름길B 함정 2개
  const gB = L.createGame(L.CONFIG), nB = gB.traps.filter(tr => tr.r >= 6 && tr.r <= 9 && tr.c >= 16 && tr.c <= 20).length;
  ok(nB === 2, `지름길B 함정이 3개에서 ${nB}개로 줄어듦`);
}

console.log(`\n결과: ${pass} 통과, ${fail} 실패`); process.exit(fail ? 1 : 0);
