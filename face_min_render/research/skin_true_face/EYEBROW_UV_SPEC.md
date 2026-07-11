# EYEBROW_UV_SPEC — AIT/Skin True Face `_Texture3` 取樣規格

Authority: vanilla shader blob from `D:\HS2 - Copy\abdata\chara\00\fo_head_00.unity3d`
(`AIT/Skin True Face`), DXBC → HLSL via 3Dmigoto `cmd_Decompiler` 1.3.16.
Side evidence (same property names; Shader Helper replaces AIT→Hanmen at runtime):
`Hanmen/Next-Gen Face` from `[Hanmen] Hanmen NEXT-GEN SHADERS v2.1.zipmod`.

Artifacts: `research/skin_true_face/` (`export.txt`, `dxbc/pass0_fp_32.{dxbc,asm,hlsl}`,
`hanmen_face/fp0.hlsl`, `decompressed_blob.bin`).

---

## 1. UV 精確式

**Mesh 屬性是 UV1，不是 UV0。**
VS 輸出 `o1.zw = mesh.uv1 * _texcoord2_ST`（`pass0_vs_0.hlsl`）；
PS 以 `v1.zw` 為眉毛／mnpk 基底（vanilla `pass0_fp_32.hlsl`；Hanmen Face 同）。

`_Texture3UV = (tx, ty, sx, sy)`（ChaControl / prop desc「tx,ty,sx,sy」）。

**Vanilla 式（權威，`pass0_fp_32.hlsl` ≈158–170）：**

```hlsl
// cb holding (tx,ty,sx,sy) and rotator shown as cb0[8] / cb0[9] in this variant
float2 uv = v1.zw + float2(tx, ty);          // ADD offset (not subtract)
float angle = rotator * 3.14159274;          // see §2
sincos(angle, s, c);
float2 p = uv - float2(0.3, 0.4);            // rotate center
float2 r;
r.x = dot(p, float2(c, s));
r.y = dot(p, float2(-s, c));
uv = r + float2(0.3, 0.4);
// scale about (0.1, 0.43):
uv = uv * float2(sx, sy) + (float2(sx, sy) - 1.0) * float2(-0.1, -0.43);
// ≡ uv * scale + (1 - scale) * float2(0.1, 0.43);
sample(_Texture3, uv);
```

**不是** Unity `TRANSFORM_TEX`（`uv * _ST.xy + _ST.zw`）。
**不是** 中心 0.5 的 `(uv-0.5)*s+0.5 ± offset`。

順序：**offset → rotate(0.3,0.4) → scale(0.1,0.43)**。

Hanmen Face（旁證，`hanmen_face/fp0.hlsl` 127–138）同為 `uv1+offset`、`rotator*π`、
繞 (0.3,0.4) 旋轉；scale 寫成 `uv*scale + (0.1,0.43)`（無 `(1-scale)*center`），
旋轉後加的是 `(0.2,-0.03)` 而非加回 (0.3,0.4)。**實作以 vanilla 式為準。**

`o_head`：UV0≠UV1；眉脊 UV0 帶對應的 UV1 落在 stamp 島（約 u∈[0.05,0.95], v∈[0.05,0.85]）。

---

## 2. `_Texture3Rotator`

| 項 | 值 |
|----|-----|
| ChaControl | `Lerp(-0.15, 0.15, eyebrowTilt)` → `SetFloat` |
| 單位 | **半圈標度**：`sincos(rotator * π)`（vanilla L158–159；Hanmen L128–129） |
| 順序 | 在 offset 之後、scale 之前 |
| 旋轉中心 | **(0.3, 0.4)**（硬編碼，非 0.5） |

---

## 3. `_Color3U` / `_Color3V` / `_Color3Power`

- `cf_m_skin_head_*` material 上有這三個 float，預設皆 **1.0**。
- `AIT/Skin True Face` `m_PropInfo` / `export.txt` **未宣告**它們。
- FP0 反組譯 **無** 對應取樣／混合引用。

**結論：不參與眉毛 matDraw。** Coverage 只用 `_Color3`（見 §4）。

---

## 4. Coverage 與 `_Color3`

**Vanilla color 疊加**（t2 路徑，`pass0_fp_32.hlsl` ≈344–347）：以貼圖 **R**（`zxxx`/`x` 通道）× color 做 lerp。

**Hanmen Texture3**（`fp0.hlsl` 138–143）：`coverage = Color3.a * tex.y`（**G**），再 `* 1.5`，lerp 到 `Color3.rgb`。

Stock `st_eyebrow` / underhair 為 DXT1 灰階 **R=G=B**，故 `tex.r == tex.g == min(rgb)`。

**規格：**

```
coverage = tex.g;   // Hanmen Face; ≡ tex.r on stock grayscale DXT1
out.rgb  = lerp(base.rgb, Color3.rgb, saturate(coverage * Color3.a));
```

離線 bake 另加硬閾 `(g-0.45)/0.55`（**非 shader 原文**），避免紅紙／軟邊把整塊 UV1 島塗成灰板。

---

## 5. 左右兩眉

- Vanilla / Hanmen 眉毛路徑 **皆無** `abs(0.5-u)` 類 U-mirror（該 mirror 出現在 **BumpMap** 路徑，`t4`，與 `_Texture3` 無關）。
- `o_head` 左／右臉在 UV1 上落在**同一 stamp 島**（L/R UV1 分布幾乎相同）——雙眉靠 **mesh UV1 展開** 把單側 stamp 投到兩側眉脊，**不是** shader 鏡像。

禁止再發明 shader U-fold。

---

## 6. V 方向

| 空間 | 方向 |
|------|------|
| Mesh / shader `v1.zw` | Unity mesh UV（V 向上） |
| UnityPy 匯出 PNG / 本 repo `_sample_uv_bilinear` | 列 = V 向下 → 取樣用 `v_img = 1 - v_mesh` |
| 本離線 albedo bake | 與 renderer 一致：`alb(u, 1-mesh_v)` |

---

## 偽代碼（實作用）

```python
def sample_eyebrow_matdraw(uv1, tex, tx, ty, sx, sy, rot, color3):
    u = uv1 + (tx, ty)
    s, c = sin(rot * pi), cos(rot * pi)
    p = u - (0.3, 0.4)
    r = (p[0]*c + p[1]*s, -p[0]*s + p[1]*c)
    u = r + (0.3, 0.4)
    # scale about (0.1, 0.43)
    u = u * (sx, sy) + ((sx, sy) - 1) * (-0.1, -0.43)
    if not (0 <= u[0] <= 1 and 0 <= u[1] <= 1):
        return 0.0  # or still sample; clamp policy = sampler
    samp = tex_sample(tex, u[0], 1 - u[1])  # UnityPy PNG
    coverage = samp[0]  # R; stock == G
    return coverage * color3[3], color3[:3]
```

---

## 引用索引

| 聲明 | 檔案 |
|------|------|
| UV1 + offset/rotate/scale | `dxbc/pass0_fp_32.hlsl` L158–170 |
| Rotator × π | 同上 L158–159 |
| VS 傳 UV1 | `dxbc/pass0_vs_0.hlsl` L55–56 |
| Hanmen Texture3 同構 | `hanmen_face/fp0.hlsl` L127–143 |
| Prop 名 / 預設 UV | `export.txt`; material `_Texture3UV=(0,0.1,1,1)` |
| ChaControl 寫入 | `dll_decompiled/AIChara/ChaControl.cs` ≈4336–4351 |
| ChaShader 綁定 | `ChaShader.cs` Eyebrow* → `_Texture3` / `_Color3` / `_Texture3UV` / `_Texture3Rotator` |
