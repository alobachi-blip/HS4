# Assets

| 檔案 | 說明 |
|------|------|
| `o_head.obj` | HS2 參考頭幾何（無蒙皮） |
| `demo_pack/` | `build_demo_pack.py` 產生：合成骨、權重、曲線 |

## 真實 HS2 插槽（手動匯出後）

建議目錄：

```text
assets/hs2/
  cf_customhead.txt
  cf_anmShapeFace.bin
  head_skinned.json   # verts, faces, bones[], weights[N,4], bone_indices[N,4], bindposes
```

`head_skinned.json` schema 見 `face_min/asset_schema.py`。
