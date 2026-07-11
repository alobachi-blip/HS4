# HANDOFF：真骨架/真蒙皮已接上，下一步清單

最新進度（本輪）：眼壞掉（綠黑杏仁罩）已修——根因是 HS2 `st_eyelash`/`st_eye` 等 DXT1 AddTex
把 coverage 放在 R、A 恆為 1；先前把 RGB 當顏色＋A 當透明，整片不透明綠罩蓋住眼球。
現依 `ChangeEyelashes*` / `ChangeEyes*`：`normalize_hs2_addtex` 把 R→A，睫毛用 `_Color`，
瞳孔/眼黑/高光按 ChaControl layout（眼黑含 material `_texture3uv.z=4`）。

前一輪：`o_eyeshadow` / `o_tooth` / `o_tang` 已接真材質 `_MainTex`（+ `_Color`）。

先前 commit：`cfe7353` feat(face_min_render): drive real cf_J_* skeleton from real shapeValueFace
（在此之前還有 `2144b31` / `76c62c8` / `98d08e9` / `b59c1fc` 等，都在 `main` 上）

## 現況（已驗證可用）

- **完全真實資料，零發明骨架/權重**：
  - `face_min/extract_skeleton.py`：從 `fo_head_*.unity3d` 挖出 `cf_J_*` Transform 樹（quaternion rest pose）、
    每個部件的真蒙皮權重（手動解 `Mesh.m_VertexData` 的 BlendWeight/BlendIndices channel）、
    真 bind pose（`SkinnedMeshRenderer.m_Bones` + `Mesh.m_BindPose`）、
    **以及每個 part 的 material `_MainTex` / `_Color`（寫入 `main_tex` / `main_color`）**。
  - `face_min/shape_update_real.py`：**自動產生**，來源是 `tools/gen_shape_update_real.py` 解析
    `dll_decompiled/ShapeHeadInfoFemale.cs` 的 `Update()`。**不要手改這個檔案**，改
    `.cs` 邏輯理解錯了就重跑 `python tools/gen_shape_update_real.py`。
  - 真 59 類別表 `cf_customhead`（`list/customshape.unity3d`）、真曲線 `cf_anmShapeHead_XX`
    （就在每個 `fo_head` bundle 裡）都是直接讀遊戲資料，不是手刻。
  - `face_min/skeleton.py` 新增 `set_local_absolute`：對齊 `SetLocalPositionX/Y/Z` /
    `SetLocalRotation` / `SetLocalScale` 的**絕對覆寫**語意（被摸到的軸直接覆寫，沒被摸到的軸維持
    真實 rest 值）。舊的 `set_local_components`（demo 用，加法/乘法在 rest 上）只給
    `from_demo_pack` 路徑用，兩者不要混。

- **已修好的真 bug**：
  1. `AnimationKeyInfo` 二進位字串長度原本用 int32 讀，真資產其實是 C# 7-bit 變長編碼。
  2. 角度曲線在 0°/360° 邊界環繞，原本用普通線性內插，導致眼睛/下巴在某些滑桿值附近
     被轉了快 180 度飛出頭外。已改成 `Mathf.LerpAngle` 等價的最短路徑內插
     （`animation_key_info.py` 的 `_lerp_angle_deg`）。

- **CmpFace 非髮部件貼圖（本輪）**：
  - `o_eyeshadow`（`c_m_eyekage`）：`_MainTex`=`c_t_eyeshadow_00` × material `_Color`（預設 ~0.15 灰），
    `use_alpha` + `double_sided`。注意：這跟臉貼圖上的 makeup `st_eyeshadow_` **不是同一層**。
  - `o_tooth` / `o_tang`：各自 `_MainTex`（`c_tooth_t` / `c_tang_t`），opaque。
  - `o_eyelashes`：仍走 ChaList `st_eyelash_`；若 id 缺失（mod）則回退到 fo_head material MainTex。
  - 繪製順序：`tooth/tang → eyebase L/R → eyeshadow → eyelashes`。

- **眉毛 matDraw（進行中）**：已抽出 `AIT/Skin True Face` 並寫入
  `research/skin_true_face/EYEBROW_UV_SPEC.md`（UV1 + offset→rotate(0.3,0.4)→scale(0.1,0.43)；
  rotator×π；coverage=tex.g；無 shader U-mirror）。`blend_eyebrow_matdraw` 已改按該式
  把 UV1 柵格化進 UV0 albedo。**Albedo 可見兩道眉**；3D 正面仍偏三角面塊（bake
  解析度／應改 runtime 按 UV1 取樣）。見下「眉毛卡關」。

- **驗證結果**：三張測試卡（Kawamoto_Nanako / Aragaki_Yoko / Kana）現在依各自
  `shapeValueFace` 產生**真的不同**頭型；`o_head` + `o_eyebase_L/R` + `o_eyelashes` +
  `o_eyeshadow` + `o_tooth` + `o_tang` 共用同一組骨骼姿勢。跑法：

  ```
  cd face_min_render
  python scripts/render_from_cards.py --size 640
  python scripts/make_test_sheet.py
  ```
  輸出在 `output/from_cards_textured/TEST_sheet_three_cards.png`。

## 眉毛卡關（寫清楚，下一步別再猜）

現象：舊實作（UV0 + 中心 0.5 scale）成眼上橫帶。現依 shader 原文改 UV1 公式後，
**compose albedo 已有兩道眉形**；3D 正面仍偏 faceted 色塊（非整臉橫帶）。DoD「清楚眉」
未完全過——品質債在 bake→UV0，不是再猜 offset 符號。

### 已確認（有權威來源，可留）

1. ChaControl 寫入公式（`ChaControl.cs` ≈4336）：
   - layout xy → offset `Lerp(-0.2,0.2,*)` / `Lerp(0.16,0,*)`
   - layout zw → scale `Lerp(2,0.5,*)`
   - tilt → rotator `Lerp(-0.15,0.15,*)` → `_Texture3Rotator`
2. 屬性名：`ChaShader` EyebrowTex/Color/Layout/Tilt = `_Texture3` / `_Color3` /
   `_Texture3UV` / `_Texture3Rotator`；shader 註解 `_Texture3UV (tx,ty,sx,sy)`。
3. 眉毛在 **matDraw**（執行期 face shader），**不在** `CreateFaceTexture` bake。
4. stock `st_eyebrow` 多為 DXT1、A≡1、白描邊在黑底（灰階 RGB 剪影）；`c_t_eyebrow_17`
   清成紅底（R 高），不能當純 R-mask。
5. 離線 renderer：`render._sample_tex` 用 `v_img = 1 - mesh_v`。
6. `fo_head` brow-ridge 附近 mesh UV0 ≈ `v 0.52`；**眉毛取樣基底是 UV1**（≠ UV0）。

### 已鎖定（shader 原文 — 見 `research/skin_true_face/EYEBROW_UV_SPEC.md`）

| 項 | 鎖定內容 | 引用 |
|----|----------|------|
| UV 精確式 | `uv = UV1 + (tx,ty)`；繞 **(0.3,0.4)** 旋轉；繞 **(0.1,0.43)** scale（`uv*s+(s-1)*(-0.1,-0.43)`）。**非** TRANSFORM_TEX，**非** 中心 0.5 | vanilla `dxbc/pass0_fp_32.hlsl` L158–170 |
| Rotator | `sincos(rot * π)`；rotate 在 offset 後、scale 前 | 同上 L158–159 |
| Color3U/V/Power | material 孤兒預設 1；**PropInfo／PS 未用** | `export.txt`；FP HLSL 無引用 |
| Coverage | `tex.g`（Hanmen）；stock 灰階 ≡ R | Hanmen `hanmen_face/fp0.hlsl` L138–143 |
| 左右兩眉 | **無** shader U-mirror；靠 mesh UV1 雙側展開同一 stamp 島 | vanilla/Hanmen 無 `abs(0.5-u)` 於 Texture3 路徑；`inspect_head_uvs` |
| V 方向 | mesh V 向上；UnityPy PNG 列=`1-v` | 與既有 renderer 一致 |

旁證：Hanmen Next-Gen Face 替換 AIT 且保留同名 `_Texture3*` 屬性（Shader Helper）。

### 曾踩的坑（不要再走）

- 把 layout 後的圖丟回 `blend_addtex`：UV clip 讓 A 不再全 1 → 誤走 A-coverage → 整臉塗色。
- 宣稱「已完成」但正面仍是眼上橫帶——**視覺 DoD 未過**。
- 在沒有 shader 原文時用社群「mirrored eyebrows」發明 U 折疊——未採用，因無 DLL／shader 依據。
- **用 UV0 + 中心 0.5 scale／減 offset 碰運氣**——已證明與 DXBC 不符。

### 建議下一步（品質，非再猜 UV）

1. Runtime／painter 按 **UV1** 取樣 `_Texture3`（勿再 bake 進 UV0），消除三角面塊。
2. 紅紙 DLC 的 bake 閾值改為可選；权威路徑保持 `tex.g * Color3.a`。

## 還沒做的（依優先順序）

1. ~~`o_eyeshadow` 真材質貼圖~~ **已完成**。
2. ~~`o_tooth` / `o_tang` 真材質貼圖 + set_part_render~~ **已完成**（側視嘴巴內部穿模仍在，見下）。
3. **嘴巴內部穿模（側視）**：暫用 `skip_side=True` 讓側視不畫 `o_tooth`/`o_tang`（正面仍畫）。
   根因仍在（painter 深度 + 閉口內部 mesh）。之後可改成依嘴開合 shape 隱藏／裁切，
   或只畫落在口腔 AABB 內的面，再拿掉 skip_side。
4. **眉毛 `ChangeEyebrowLayout` UV**：**式已鎖定**（`EYEBROW_UV_SPEC.md`）；albedo bake 已接 UV1。
   3D 清楚度未完全過 DoD——下一步改 runtime UV1 取樣，勿再猜 offset。
5. **mod 資源缺失**（Kana 的睫毛 id `666012`、眉毛 id `704` 在 `D:\HS2 - Copy` 裡查不到）：
   這不是 bug，是我們的清單解析器（`compose_face_tex.find_list_entry`）只掃
   `abdata/list/characustom/*.unity3d`，沒有合併 BepInEx/Sideloader 的 mod 清單。
   如果要支援 mod 卡，需要另開一條掃描路徑（掃 mod 目錄下的 ChaListData），現在完全沒有
   這層。睫毛已有 fo_head material MainTex 回退；眉毛仍完全跳過。
6. **身體其他部位**：目前只做了臉（`fo_head`）。身體、頭髮完全沒碰，使用者說過頭髮先不用管。

## 已知的架構債務（上次 code review 記錄，還沒處理完的部分）

- ~~`extract_skeleton` 跨模組匯入 `_mat_tex_map`/`_export_texture_from_env`~~ **已清**：改為公開
  `mat_tex_map` / `export_texture_from_env`；`REAL_RIG_PARTS` 改由 `FACE_DRAW_PARTS` 減
  `DEFAULT_SKIP_PARTS` 推導，不再手動維護兩份清單。
- `extract_eyes.py` 裡的 `export_face_meshes_from_head`/`export_eyebase_from_head` 現在是
  **deprecated 死代碼**（已加註記），render_from_cards.py 不再用它，改用
  `extract_skeleton.export_real_head_rig`。之後可以考慮直接刪掉，減少「兩套頭」的困惑。
- `blend_addtex`（`compose_face_tex.py`）疊圖邏輯還是靠像素統計猜 alpha-mask vs
  RGB-silhouette，不是照 shader 語義查表。新卡如果貼圖統計特徵不一樣還是可能猜錯。
- 大量 `except Exception: continue/return None` 靜默吞錯誤，除錯時要注意。

## 唯讀邊界（不要碰）

`HS2_COPY_ROOT` 環境變數，預設 `D:\HS2 - Copy`。所有讀取都經過
`face_min/hs2_abdata.py`，從未寫入正式 `D:\HS2`。
