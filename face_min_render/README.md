# HS2 Face Min Render

獨立、**不部署／不寫入正式 HS2** 的最小面孔 renderer。  
遊戲資源只讀：`D:\HS2 - Copy`（可用環境變數 `HS2_COPY_ROOT` 覆寫）。

## 皮膚貼圖（依反編譯）

對齊 `ChaControl.CreateFaceTexture`：

1. 角色卡讀 `skinId` / `headId` / `skinColor`
2. `list/characustom/*` → MessagePack `ChaListData`（`ft_skin_f_*`）
3. `MainAB` + `MainTex` / `OcclusionMapTex` / `NormalMapTex`
4. 從 `D:\HS2 - Copy\abdata\...` 匯出 Texture2D，乘上 `skinColor` 後著色

```powershell
cd D:\HS2FaceMinRender\face_min_render
python scripts\build_demo_pack.py
python scripts\render_from_cards.py
```

輸出：`output\from_cards_textured\`
