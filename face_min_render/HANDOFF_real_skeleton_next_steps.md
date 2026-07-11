# HANDOFF：真骨架/真蒙皮已接上，下一步清單

最新 commit：`cfe7353` feat(face_min_render): drive real cf_J_* skeleton from real shapeValueFace
（在此之前還有 `2144b31` / `76c62c8` / `98d08e9` / `b59c1fc` 等，都在 `main` 上）

## 現況（已驗證可用）

- **完全真實資料，零發明骨架/權重**：
  - `face_min/extract_skeleton.py`：從 `fo_head_*.unity3d` 挖出 `cf_J_*` Transform 樹（quaternion rest pose）、
    每個部件的真蒙皮權重（手動解 `Mesh.m_VertexData` 的 BlendWeight/BlendIndices channel）、
    真 bind pose（`SkinnedMeshRenderer.m_Bones` + `Mesh.m_BindPose`）。
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

- **驗證結果**：三張測試卡（Kawamoto_Nanako / Aragaki_Yoko / Kana）現在依各自
  `shapeValueFace` 產生**真的不同**頭型；`o_head` + `o_eyebase_L/R` + `o_eyelashes`
  共用同一組骨骼姿勢，會一起變形不脫節。跑法：

  ```
  cd face_min_render
  python scripts/render_from_cards.py --size 640
  python scripts/make_test_sheet.py
  ```
  輸出在 `output/from_cards_textured/TEST_sheet_three_cards.png`。

## 還沒做的（依優先順序）

1. **`o_eyeshadow`（眼皮/眼影 mesh）還沒接真材質貼圖**——`render_from_cards.py` 的
   `apply_makeup_and_eyes()` 裡有個 `pass`，需要照 `o_eyelashes` 的做法（`_part_main_tex`
   風格，從該 mesh 的 material 抓 `_MainTex`）補上。extract_skeleton.py 目前只抓了
   verts/faces/uvs/weights，沒抓材質貼圖路徑——需要加。
2. **`o_tooth` / `o_tang`（牙齒/舌頭）** 同樣缺材質貼圖，目前完全沒在 `render_from_cards.py`
   裡渲染（`extract_skeleton.REAL_RIG_PARTS` 有列進去、有蒙皮資料，但 `apply_makeup_and_eyes`
   沒幫它們設 `set_part_render`）。側視圖之前观察到嘴巴內部部件穿模，這裡沒解決前不會消失。
3. **眉毛 `ChangeEyebrowLayout` UV**：`compose_face_tex.py`/`render_from_cards.py` 裡
   眉毛目前是直接跳過（`eyebrow=None`），因為它需要 `_Texture3UV`/`_Texture3Rotator`
   的 UV 位移/旋轉，不是單純疊圖。要做的話去查 `dll_decompiled/AIChara/ChaControl.cs`
   的 `ChangeEyebrowLayout`/`ChangeEyebrowTilt`（約 4336 行附近），公式已經在上次審查記錄裡。
4. **mod 資源缺失**（Kana 的睫毛 id `666012`、眉毛 id `704` 在 `D:\HS2 - Copy` 裡查不到）：
   這不是 bug，是我們的清單解析器（`compose_face_tex.find_list_entry`）只掃
   `abdata/list/characustom/*.unity3d`，沒有合併 BepInEx/Sideloader 的 mod 清單。
   如果要支援 mod 卡，需要另開一條掃描路徑（掃 mod 目錄下的 ChaListData），現在完全沒有
   這層。
5. **身體其他部位**：目前只做了臉（`fo_head`）。身體、頭髮完全沒碰，使用者說過頭髮先不用管。

## 已知的架構債務（上次 code review 記錄，還沒處理完的部分）

- `extract_eyes.py` 裡的 `export_face_meshes_from_head`/`export_eyebase_from_head` 現在是
  **deprecated 死代碼**（已加註記），render_from_cards.py 不再用它，改用
  `extract_skeleton.export_real_head_rig`。之後可以考慮直接刪掉，減少「兩套頭」的困惑。
- `blend_addtex`（`compose_face_tex.py`）疊圖邏輯還是靠像素統計猜 alpha-mask vs
  RGB-silhouette，不是照 shader 語義查表。新卡如果貼圖統計特徵不一樣還是可能猜錯。
- 大量 `except Exception: continue/return None` 靜默吞錯誤，除錯時要注意。

## 唯讀邊界（不要碰）

`HS2_COPY_ROOT` 環境變數，預設 `D:\HS2 - Copy`。所有讀取都經過
`face_min/hs2_abdata.py`，從未寫入正式 `D:\HS2`。
