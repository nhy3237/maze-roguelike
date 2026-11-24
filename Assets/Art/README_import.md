# 울리는 전리품 — Unity 6 LTS(2D, URP) 스프라이트 가져오기

웹 프로토에서 코드로 그리던 도트 스프라이트를 PNG 76장으로 뽑은 것입니다.
원본은 `ringing-loot-v2.html`이며, 크기는 게임 화면에서 실제로 그려지던 크기(1 타일 = 36px) 그대로입니다.

## 1. 폴더에 넣기
`Sprites/` 폴더를 Unity 프로젝트의 `Assets/Art/` 아래에 그대로 복사합니다.

## 2. 임포트 설정 (전체 선택 후 한 번에)
| 항목 | 값 |
|---|---|
| Texture Type | Sprite (2D and UI) |
| Sprite Mode | Single |
| Pixels Per Unit | **36** (1 타일 = 1 유닛) |
| Mesh Type | Full Rect |
| Filter Mode | **Point (no filter)** |
| Compression | **None** |
| Generate Mip Maps | 끔 |

## 3. 피벗(Pivot)
| 스프라이트 | Pivot (x, y, 아래 기준) | 이유 |
|---|---|---|
| Characters/warrior_* (30×36) | (0.5, 0.333) | 웹에서 몸 위치가 이미지 아래에서 12px 위였음 |
| Characters/guardian_* (54×54) | (0.5, 0.333) | 위와 같음 (이미지 아래에서 18px 위) |
| Characters/shield_overlay (15×15) | (0.5, 0.5) | 전사 앞쪽에 겹침 |
| Relics/* (16×16), Props/*, Icons/* | (0.5, 0.5) | 칸 중심에 놓음 |
| Tiles/*, Traps/* (36×36) | (0, 0) 또는 (0.5, 0.5) | 칸 기준. 타일맵은 Pivot 무관 |

## 4. 좌표 변환 (웹 → Unity)
- 웹은 행(r)이 아래로 늘어납니다. Unity는 위로 늘어납니다.
- 칸 (행 r, 열 c)의 중심 → Unity 좌표 `(c + 0.5, -(r + 0.5))`.
- 플레이어/수호자 좌표 (x, y, 타일 단위, 웹) → Unity `(x, -y)`.

## 5. 카메라
- URP 2D Renderer + **Pixel Perfect Camera**: Assets Pixels Per Unit 36, Reference Resolution 1100×760 이상, Upscale Render Texture 끔.
- 웹의 확대율(ZOOM)은 `ringing-loot-v2.html`의 `const ZOOM`을 참고합니다.

## 6. 파일 설명
- `Sprites/Tiles/`  바닥 16종(`fl{변형}_{북쪽벽}{서쪽벽}`), 벽면 3종(`wf`), 벽 윗면 16종(`wt{변형}_{마스크}`)
- `Sprites/Characters/` 전사 3방향×2프레임(왼쪽은 오른쪽을 좌우 반전), 수호자 상태 5종(색만 다름)
- `Sprites/Relics/` 라운드 1(small/seal/rare), 라운드 2(compass/sigil/crown)
- `Sprites/Traps/` 함정 4상태(idle/warn/active/rest)
- `Sprites/Icons/` HUD·도감 아이콘 16종 (16×16)
- `assets_meta.json` 팔레트, MOOD 색상값, 고정 맵 25×18의 칸별 타일 이름(`tileGrid`), 모든 이미지 크기
