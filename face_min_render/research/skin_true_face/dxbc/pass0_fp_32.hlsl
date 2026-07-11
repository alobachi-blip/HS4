// ---- Created with 3Dmigoto v1.3.16 on Sat Jul 11 23:00:49 2026
Texture3D<float4> t14 : register(t14);

TextureCube<float4> t13 : register(t13);

TextureCube<float4> t12 : register(t12);

Texture2D<float4> t11 : register(t11);

Texture2D<float4> t10 : register(t10);

Texture2D<float4> t9 : register(t9);

Texture2D<float4> t8 : register(t8);

Texture2D<float4> t7 : register(t7);

Texture2D<float4> t6 : register(t6);

Texture2D<float4> t5 : register(t5);

Texture2D<float4> t4 : register(t4);

Texture2D<float4> t3 : register(t3);

Texture2D<float4> t2 : register(t2);

Texture2D<float4> t1 : register(t1);

Texture2D<float4> t0 : register(t0);

SamplerState s13_s : register(s13);

SamplerState s12_s : register(s12);

SamplerState s11_s : register(s11);

SamplerState s10_s : register(s10);

SamplerState s9_s : register(s9);

SamplerState s8_s : register(s8);

SamplerState s7_s : register(s7);

SamplerState s6_s : register(s6);

SamplerState s5_s : register(s5);

SamplerState s4_s : register(s4);

SamplerState s3_s : register(s3);

SamplerState s2_s : register(s2);

SamplerState s1_s : register(s1);

SamplerState s0_s : register(s0);

cbuffer cb4 : register(b4)
{
  float4 cb4[7];
}

cbuffer cb3 : register(b3)
{
  float4 cb3[8];
}

cbuffer cb2 : register(b2)
{
  float4 cb2[47];
}

cbuffer cb1 : register(b1)
{
  float4 cb1[5];
}

cbuffer cb0 : register(b0)
{
  float4 cb0[27];
}




// 3Dmigoto declarations
#define cmp -


void main(
  float4 v0 : SV_POSITION0,
  float4 v1 : TEXCOORD0,
  float4 v2 : TEXCOORD1,
  float4 v3 : TEXCOORD2,
  float4 v4 : TEXCOORD3,
  float4 v5 : COLOR0,
  float4 v6 : TEXCOORD6,
  float4 v7 : TEXCOORD7,
  out float4 o0 : SV_Target0)
{
  float4 r0,r1,r2,r3,r4,r5,r6,r7,r8,r9,r10,r11,r12,r13;
  uint4 bitmask, uiDest;
  float4 fDest;

  r0.x = v2.w;
  r0.y = v3.w;
  r0.z = v4.w;
  r1.xyz = cb1[4].xyz + -r0.xyz;
  r0.w = dot(r1.xyz, r1.xyz);
  r0.w = rsqrt(r0.w);
  r2.xyz = r1.xyz * r0.www;
  r3.xy = v1.xy * cb0[5].xy + cb0[5].zw;
  r4.xyzw = t0.Sample(s3_s, r3.xy).xyzw;
  r5.xyzw = t1.Sample(s4_s, v1.xy).xyzw;
  r1.w = cb0[6].x * r5.y;
  r1.w = cb0[6].y * r1.w;
  r1.w = 9 * r1.w;
  r3.zw = float2(0.00156250002,0.00156250002) + r3.xy;
  r6.xyzw = t0.Sample(s3_s, r3.zy).xyzw;
  r2.w = r6.y + -r4.y;
  r6.x = r2.w * r1.w;
  r3.xyzw = t0.Sample(s3_s, r3.xw).xyzw;
  r2.w = r3.y + -r4.y;
  r6.y = r2.w * r1.w;
  r6.z = 0;
  r3.xyz = float3(0,0,1) + -r6.xyz;
  r1.w = dot(r3.xyz, r3.xyz);
  r1.w = rsqrt(r1.w);
  r3.xyz = r3.xyz * r1.www;
  r4.xz = cb0[6].zz * float2(-4.19999981,-4.19999981) + float2(5,4);
  r1.w = -0.5 * r4.z;
  r4.xz = v1.zw * r4.xx + r1.ww;
  r6.xy = float2(-0.5,-0.5) + v1.zw;
  r1.w = dot(r6.xy, r6.xy);
  r1.w = sqrt(r1.w);
  r1.w = r1.w * -4 + 1;
  r1.w = max(0, r1.w);
  r6.xy = v1.zw + -r4.xz;
  r6.xy = saturate(r1.ww * r6.xy + r4.xz);
  r7.xyzw = t2.Sample(s5_s, r6.xy).xyzw;
  r4.xz = float2(1,1) + -v5.yz;
  r1.w = 8 * cb0[7].w;
  r1.w = r1.w * r4.x;
  r6.zw = float2(0.00156250002,0.00156250002) + r6.xy;
  r8.xyzw = t2.Sample(s5_s, r6.zy).xyzw;
  r2.w = r8.y + -r7.y;
  r8.x = r2.w * r1.w;
  r6.xyzw = t2.Sample(s5_s, r6.xw).xyzw;
  r2.w = r6.y + -r7.y;
  r8.y = r2.w * r1.w;
  r8.z = 0;
  r6.xyz = float3(0,0,1) + -r8.xyz;
  r1.w = dot(r6.xyz, r6.xyz);
  r1.w = rsqrt(r1.w);
  r6.xyz = r6.xyz * r1.www;
  r1.w = 3.14159274 * cb0[9].x;
  sincos(r1.w, r8.x, r9.x);
  r7.yw = cb0[8].xy + v1.zw;
  r7.yw = float2(-0.300000012,-0.400000006) + r7.yw;
  r10.x = -r8.x;
  r10.y = r9.x;
  r10.z = r8.x;
  r8.x = dot(r7.yw, r10.yz);
  r8.y = dot(r7.yw, r10.xy);
  r7.yw = float2(0.300000012,0.400000006) + r8.xy;
  r8.xy = float2(-1,-1) + cb0[8].zw;
  r8.xy = float2(-0.100000001,-0.430000007) * r8.xy;
  r8.xy = r7.yw * cb0[8].zw + r8.xy;
  r9.xyzw = t3.Sample(s6_s, r8.xy).xyzw;
  r8.zw = float2(0.00548719987,0.00548719987) + r8.xy;
  r10.xyzw = t3.Sample(s6_s, r8.zy).xyzw;
  r1.w = r10.y + -r9.y;
  r10.x = r1.w * r4.z;
  r8.xyzw = t3.Sample(s6_s, r8.xw).xyzw;
  r1.w = r8.y + -r9.y;
  r10.y = r1.w * r4.z;
  r10.z = 0;
  r8.xyz = float3(0,0,1) + -r10.xyz;
  r1.w = dot(r8.xyz, r8.xyz);
  r1.w = rsqrt(r1.w);
  r8.xyz = r8.xyz * r1.www;
  r10.yz = v1.xy * cb0[10].xy + cb0[10].zw;
  r1.w = 0.5 + -r10.y;
  r10.x = abs(r1.w);
  r11.xyzw = t4.Sample(s7_s, r10.xz).xyzw;
  r1.w = 1.5 * cb0[11].w;
  r10.w = 0.00548719987 + r10.x;
  r12.xyzw = t4.Sample(s7_s, r10.wz).xyzw;
  r2.w = r12.y + -r11.y;
  r12.x = r2.w * r1.w;
  r7.yw = float2(0,0.00548719987) + r10.xz;
  r10.xyzw = t4.Sample(s7_s, r7.yw).xyzw;
  r2.w = r10.y + -r11.y;
  r12.y = r2.w * r1.w;
  r12.z = 0;
  r9.xyw = float3(0,0,1) + -r12.xyz;
  r1.w = dot(r9.xyw, r9.xyw);
  r1.w = rsqrt(r1.w);
  r9.xyw = r9.xyw * r1.www;
  r10.xyzw = t5.Sample(s2_s, v1.xy).xyzw;
  r10.x = r10.x * r10.w;
  r7.yw = r10.xy * float2(2,2) + float2(-1,-1);
  r10.xy = cb0[4].xx * r7.yw;
  r1.w = dot(r10.xy, r10.xy);
  r1.w = min(1, r1.w);
  r1.w = 1 + -r1.w;
  r1.w = sqrt(r1.w);
  r10.xy = r7.yw * cb0[4].xx + r3.xy;
  r10.z = r1.w * r3.z;
  r1.w = dot(r10.xyz, r10.xyz);
  r1.w = rsqrt(r1.w);
  r2.w = r10.z * r1.w;
  r3.xy = r10.xy * r1.ww + r6.xy;
  r3.z = r2.w * r6.z;
  r1.w = dot(r3.xyz, r3.xyz);
  r1.w = rsqrt(r1.w);
  r2.w = r3.z * r1.w;
  r3.xy = r3.xy * r1.ww + r8.xy;
  r3.z = r2.w * r8.z;
  r1.w = dot(r3.xyz, r3.xyz);
  r1.w = rsqrt(r1.w);
  r2.w = r3.z * r1.w;
  r3.xy = r3.xy * r1.ww + r9.xy;
  r3.z = r2.w * r9.w;
  r1.w = dot(r3.xyz, r3.xyz);
  r1.w = rsqrt(r1.w);
  r2.w = r3.z * r1.w;
  r3.zw = v1.xy * cb0[13].xy + cb0[13].zw;
  r6.xyzw = t6.Sample(s10_s, v1.xy).xyzw;
  r6.w = 1 + -r6.w;
  r6.w = 3.14159274 * r6.w;
  sincos(r6.w, r8.x, r9.x);
  r10.x = -r8.x;
  r10.y = r9.x;
  r10.z = r8.x;
  r8.x = dot(r3.zw, r10.yz);
  r8.y = dot(r3.zw, r10.xy);
  r10.xyzw = t7.Sample(s9_s, r8.xy).xyzw;
  r9.xyw = max(r6.xxy, r6.yzz);
  r11.yzw = saturate(-r9.wyx + r6.xyz);
  r6.xyz = saturate(r9.xyw + -r6.zyx);
  r3.z = r11.y + r11.z;
  r3.z = r3.z + r11.w;
  r6.xyz = r6.xyz + -r3.zzz;
  r6.xyz = max(float3(0,0,0), r6.xyz);
  r9.xyw = min(cb0[14].xyz, r11.yzw);
  r3.z = r9.x + r9.y;
  r3.z = r3.z + r9.w;
  r3.w = min(cb0[14].w, r6.x);
  r3.z = r3.z + r3.w;
  r6.xy = min(cb0[15].xy, r6.yz);
  r3.z = r6.x + r3.z;
  r3.z = r3.z + r6.y;
  r3.z = saturate(cb0[15].z + r3.z);
  r3.w = cmp(0.5 < r3.z);
  r6.x = -0.5 + r3.z;
  r6.x = r6.x + r6.x;
  r6.x = min(r6.x, r10.x);
  r6.x = max(r6.x, r10.z);
  r6.y = cmp(r3.z == 0.500000);
  r6.z = cmp(r3.z < 0.5);
  r6.w = r3.z + r3.z;
  r6.w = min(r6.w, r10.z);
  r6.z = r6.z ? r6.w : 0;
  r6.y = r6.y ? r10.z : r6.z;
  r3.w = r3.w ? r6.x : r6.y;
  r6.xy = float2(6,0.150000006) * r3.ww;
  r8.zw = float2(0.00911249872,0.00911249872) + r8.xy;
  r12.xyzw = t7.Sample(s9_s, r8.zy).xyzw;
  r6.z = r12.y + -r10.y;
  r12.x = r6.z * r6.x;
  r8.xyzw = t7.Sample(s9_s, r8.xw).xyzw;
  r6.z = r8.y + -r10.y;
  r12.y = r6.z * r6.x;
  r12.z = 0;
  r6.xzw = float3(0,0,1) + -r12.xyz;
  r7.y = dot(r6.xzw, r6.xzw);
  r7.y = rsqrt(r7.y);
  r3.z = min(r3.z, r3.w);
  r8.xyzw = t8.Sample(s8_s, v1.xy).xyzw;
  r8.x = r8.x * r8.w;
  r8.xy = r8.xy * float2(2,2) + float2(-1,-1);
  r8.xy = cb0[12].xx * r8.xy;
  r7.w = dot(r8.xy, r8.xy);
  r7.w = min(1, r7.w);
  r7.w = 1 + -r7.w;
  r7.w = sqrt(r7.w);
  r8.xy = r3.xy * r1.ww + r8.xy;
  r8.z = r7.w * r2.w;
  r1.w = dot(r8.xyz, r8.xyz);
  r1.w = rsqrt(r1.w);
  r8.xyz = r8.xyz * r1.www;
  r6.xzw = r6.xzw * r7.yyy + -r8.xyz;
  r6.xzw = r3.zzz * r6.xzw + r8.xyz;
  r1.w = cb0[17].w * r5.x;
  r8.xyz = cb0[17].xyz + -cb0[16].xyz;
  r8.xyz = r1.www * r8.xyz + cb0[16].xyz;
  r10.xyzw = t9.Sample(s11_s, v1.xy).xyzw;
  r9.xyw = r10.xyz * r8.xyz;
  r1.w = cb0[19].x * r5.z;
  r1.w = cb0[18].w * r1.w;
  r8.xyz = -r10.xyz * r8.xyz + cb0[18].xyz;
  r8.xyz = r1.www * r8.xyz + r9.xyw;
  r1.w = cmp(r8.y >= r8.z);
  r1.w = r1.w ? 1.000000 : 0;
  r10.xy = r8.zy;
  r10.zw = float2(-1,0.666666687);
  r12.xy = -r10.xy + r8.yz;
  r12.zw = float2(1,-1);
  r10.xyzw = r1.wwww * r12.xyzw + r10.xyzw;
  r1.w = cmp(r8.x >= r10.x);
  r1.w = r1.w ? 1.000000 : 0;
  r12.xyz = r10.xyw;
  r12.w = r8.x;
  r10.xyw = r12.wyx;
  r10.xyzw = r10.xyzw + -r12.xyzw;
  r10.xyzw = r1.wwww * r10.xyzw + r12.xyzw;
  r1.w = min(r10.w, r10.y);
  r1.w = r10.x + -r1.w;
  r2.w = r10.w + -r10.y;
  r3.x = r1.w * 6 + 1.00000001e-010;
  r2.w = r2.w / r3.x;
  r2.w = r10.z + r2.w;
  r3.x = 1.00000001e-010 + r10.x;
  r1.w = r1.w / r3.x;
  r9.xyw = float3(1,0.666666687,0.333333343) + abs(r2.www);
  r9.xyw = frac(r9.xyw);
  r9.xyw = r9.xyw * float3(6,6,6) + float3(-3,-3,-3);
  r9.xyw = saturate(float3(-1,-1,-1) + abs(r9.xyw));
  r9.xyw = float3(-1,-1,-1) + r9.xyw;
  r9.xyw = r1.www * r9.xyw + float3(1,1,1);
  r9.xyw = float3(0.800000012,0.800000012,0.800000012) * r9.xyw;
  r10.xyzw = t10.Sample(s12_s, v1.xy).xyzw;
  r1.w = cb0[12].x * r10.z;
  r2.w = 0.150000006 * r1.w;
  r9.xyw = r8.xyz * r9.xyw + -r8.xyz;
  r8.xyw = r2.www * r9.ywx + r8.yzx;
  r2.w = cb0[20].w * r9.z;
  r3.x = r2.w * r4.z;
  r9.xyz = r9.zzz * cb0[20].xyz + -r8.wxy;
  r9.xyz = r3.xxx * r9.xyz + r8.wxy;
  r7.xyzw = cb0[7].wxyz * r7.zxxx;
  r3.y = r7.x * r4.x;
  r7.xyz = r7.yzw * float3(0.699999988,0.699999988,0.699999988) + -r9.xyz;
  r7.xyz = r3.yyy * r7.xyz + r9.xyz;
  r4.x = saturate(cb0[11].w + cb0[11].w);
  r5.z = cb0[11].w * -3 + 4;
  r7.w = log2(r11.x);
  r5.z = r7.w * r5.z;
  r5.z = exp2(r5.z);
  r4.x = r5.z * r4.x;
  r9.xyz = cb0[11].xyz + -r7.xyz;
  r7.xyz = r4.xxx * r9.xyz + r7.xyz;
  r9.xyz = float3(0.699000001,0.699000001,0.699000001) + -r7.xyz;
  r7.xyz = r6.yyy * r9.xyz + r7.xyz;
  r9.x = dot(v2.xyz, r6.xzw);
  r9.y = dot(v3.xyz, r6.xzw);
  r9.z = dot(v4.xyz, r6.xzw);
  r5.z = dot(r9.xyz, r2.xyz);
  r5.z = 1 + -r5.z;
  r5.z = log2(r5.z);
  r5.z = cb0[21].z * r5.z;
  r5.z = exp2(r5.z);
  r5.z = saturate(cb0[21].y * r5.z + cb0[21].x);
  r3.y = cb0[22].x * r3.y;
  r6.xy = cb0[22].yw * r10.xx;
  r6.z = 1 + -r5.y;
  r4.yw = max(r6.zz, r4.wy);
  r4.y = r6.x * r4.y;
  r3.y = max(r4.y, r3.y);
  r2.w = -r2.w * r4.z + 1;
  r2.w = min(r3.y, r2.w);
  r2.w = r3.x * 0.150000006 + r2.w;
  r3.x = min(cb0[22].z, r5.x);
  r2.w = saturate(r3.x + r2.w);
  r3.x = 0.5 * r4.x;
  r2.w = max(r3.x, r2.w);
  r2.w = max(r2.w, r6.y);
  r3.xy = v1.xy * cb0[23].xy + cb0[23].zw;
  r6.xyzw = t11.Sample(s13_s, r3.xy).xyzw;
  r3.x = dot(cb2[0].xyz, cb2[0].xyz);
  r3.x = rsqrt(r3.x);
  r4.xyz = cb2[0].xyz * r3.xxx;
  r3.x = saturate(dot(r4.xyz, r9.xyz));
  r3.y = 1 + -r5.z;
  r4.xy = cb0[24].xx * r6.zx;
  r2.w = max(r4.x, r2.w);
  r2.w = r2.w + r3.z;
  r3.y = r3.y * r2.w;
  r3.y = cb0[21].w * r3.y;
  r3.x = r3.y * r3.x;
  r3.xyz = saturate(r3.xxx * r5.yyy + r7.xyz);
  r4.x = max(cb0[24].y, r4.y);
  r3.w = -r3.w * 0.150000006 + 1;
  r3.w = min(r4.x, r3.w);
  r4.x = 1 + -cb0[24].z;
  r4.y = min(r10.y, r4.w);
  r4.y = -r1.w * 0.0799999982 + r4.y;
  r4.z = 1 + -r4.x;
  r4.x = r4.y * r4.z + r4.x;
  r4.y = cmp(r8.x >= r8.y);
  r4.y = r4.y ? 1.000000 : 0;
  r6.xy = r8.yx;
  r6.zw = float2(-1,0.666666687);
  r7.xy = r8.xy + -r6.xy;
  r7.zw = float2(1,-1);
  r6.xyzw = r4.yyyy * r7.xyzw + r6.xyzw;
  r4.y = cmp(r8.w >= r6.x);
  r4.y = r4.y ? 1.000000 : 0;
  r8.xyz = r6.xyw;
  r6.xyw = r8.wyx;
  r6.xyzw = r6.xyzw + -r8.xyzw;
  r6.xyzw = r4.yyyy * r6.xyzw + r8.xyzw;
  r4.y = min(r6.w, r6.y);
  r4.y = r6.x + -r4.y;
  r4.z = r6.w + -r6.y;
  r4.w = r4.y * 6 + 1.00000001e-010;
  r4.z = r4.z / r4.w;
  r4.z = r6.z + r4.z;
  r4.w = 1.00000001e-010 + r6.x;
  r4.y = r4.y / r4.w;
  r4.w = cmp(0 >= r6.x);
  r1.w = r1.w * 0.150000006 + r6.x;
  r5.xyz = float3(1,0.666666687,0.333333343) + abs(r4.zzz);
  r5.xyz = frac(r5.xyz);
  r5.xyz = r5.xyz * float3(6,6,6) + float3(-3,-3,-3);
  r5.xyz = saturate(float3(-1,-1,-1) + abs(r5.xyz));
  r5.xyz = float3(-1,-1,-1) + r5.xyz;
  r5.xyz = r4.www ? float3(0,-1,-1) : r5.xyz;
  r4.yzw = r4.yyy * r5.xyz + float3(1,1,1);
  r4.yzw = r4.yzw * r1.www;
  r4.yzw = r4.yzw * r5.www;
  r1.w = cmp(cb4[0].x == 1.000000);
  if (r1.w != 0) {
    r1.w = cmp(cb4[0].y == 1.000000);
    r5.xyz = cb4[2].xyz * v3.www;
    r5.xyz = cb4[1].xyz * v2.www + r5.xyz;
    r5.xyz = cb4[3].xyz * v4.www + r5.xyz;
    r5.xyz = cb4[4].xyz + r5.xyz;
    r5.xyz = r1.www ? r5.xyz : r0.xyz;
    r5.xyz = -cb4[6].xyz + r5.xyz;
    r5.yzw = cb4[5].xyz * r5.xyz;
    r1.w = r5.y * 0.25 + 0.75;
    r5.y = cb4[0].z * 0.5 + 0.75;
    r5.x = max(r5.y, r1.w);
    r5.xyzw = t14.Sample(s1_s, r5.xzw).xyzw;
  } else {
    r5.xyzw = float4(1,1,1,1);
  }
  r1.w = saturate(dot(r5.xyzw, cb2[46].xyzw));
  r5.x = dot(r9.xyz, r9.xyz);
  r5.x = rsqrt(r5.x);
  r5.xyz = r9.xyz * r5.xxx;
  r5.w = 1 + -r2.w;
  r6.x = dot(-r2.xyz, r5.xyz);
  r6.x = r6.x + r6.x;
  r6.xyz = r5.xyz * -r6.xxx + -r2.xyz;
  r7.xyz = cb0[2].xyz * r1.www;
  r6.w = cmp(0 < cb3[2].w);
  if (r6.w != 0) {
    r6.w = dot(r6.xyz, r6.xyz);
    r6.w = rsqrt(r6.w);
    r8.xyz = r6.xyz * r6.www;
    r9.xyz = cb3[0].xyz + -r0.xyz;
    r9.xyz = r9.xyz / r8.xyz;
    r10.xyz = cb3[1].xyz + -r0.xyz;
    r10.xyz = r10.xyz / r8.xyz;
    r11.xyz = cmp(float3(0,0,0) < r8.xyz);
    r9.xyz = r11.xyz ? r9.xyz : r10.xyz;
    r6.w = min(r9.x, r9.y);
    r6.w = min(r6.w, r9.z);
    r9.xyz = -cb3[2].xyz + r0.xyz;
    r8.xyz = r8.xyz * r6.www + r9.xyz;
  } else {
    r8.xyz = r6.xyz;
  }
  r6.w = -r5.w * 0.699999988 + 1.70000005;
  r6.w = r6.w * r5.w;
  r6.w = 6 * r6.w;
  r8.xyzw = t12.SampleLevel(s0_s, r8.xyz, r6.w).xyzw;
  r7.w = -1 + r8.w;
  r7.w = cb3[3].w * r7.w + 1;
  r7.w = log2(r7.w);
  r7.w = cb3[3].y * r7.w;
  r7.w = exp2(r7.w);
  r7.w = cb3[3].x * r7.w;
  r9.xyz = r7.www * r8.xyz;
  r8.w = cmp(cb3[1].w < 0.999989986);
  if (r8.w != 0) {
    r8.w = cmp(0 < cb3[6].w);
    if (r8.w != 0) {
      r8.w = dot(r6.xyz, r6.xyz);
      r8.w = rsqrt(r8.w);
      r10.xyz = r8.www * r6.xyz;
      r11.xyz = cb3[4].xyz + -r0.xyz;
      r11.xyz = r11.xyz / r10.xyz;
      r12.xyz = cb3[5].xyz + -r0.xyz;
      r12.xyz = r12.xyz / r10.xyz;
      r13.xyz = cmp(float3(0,0,0) < r10.xyz);
      r11.xyz = r13.xyz ? r11.xyz : r12.xyz;
      r8.w = min(r11.x, r11.y);
      r8.w = min(r8.w, r11.z);
      r0.xyz = -cb3[6].xyz + r0.xyz;
      r6.xyz = r10.xyz * r8.www + r0.xyz;
    }
    r6.xyzw = t13.SampleLevel(s0_s, r6.xyz, r6.w).xyzw;
    r0.x = -1 + r6.w;
    r0.x = cb3[7].w * r0.x + 1;
    r0.x = log2(r0.x);
    r0.x = cb3[7].y * r0.x;
    r0.x = exp2(r0.x);
    r0.x = cb3[7].x * r0.x;
    r0.xyz = r0.xxx * r6.xyz;
    r6.xyz = r7.www * r8.xyz + -r0.xyz;
    r9.xyz = cb3[1].www * r6.xyz + r0.xyz;
  }
  r0.xyz = r9.xyz * r4.xxx;
  r6.xyz = cb0[2].xyz * r1.www + -cb0[2].xyz;
  r6.xyz = cb0[26].xxx * r6.xyz + cb0[2].xyz;
  r8.xyz = r5.xyz * cb0[25].xxx + cb2[0].xyz;
  r1.w = saturate(dot(r2.xyz, -r8.xyz));
  r1.w = log2(r1.w);
  r1.w = cb0[25].y * r1.w;
  r1.w = exp2(r1.w);
  r1.w = cb0[25].z * r1.w;
  r6.xyz = r6.xyz * r1.www;
  r4.xyz = r6.xyz * r4.yzw;
  r4.xyz = r4.xyz * r3.xyz;
  r6.xyz = float3(-0.0399999991,-0.0399999991,-0.0399999991) + r3.xyz;
  r6.xyz = r3.www * r6.xyz + float3(0.0399999991,0.0399999991,0.0399999991);
  r1.w = -r3.w * 0.959999979 + 0.959999979;
  r3.xyz = r3.xyz * r1.www;
  r1.xyz = r1.xyz * r0.www + cb2[0].xyz;
  r0.w = dot(r1.xyz, r1.xyz);
  r0.w = max(0.00100000005, r0.w);
  r0.w = rsqrt(r0.w);
  r1.xyz = r1.xyz * r0.www;
  r0.w = dot(r5.xyz, r2.xyz);
  r2.x = saturate(dot(r5.xyz, cb2[0].xyz));
  r2.y = saturate(dot(r5.xyz, r1.xyz));
  r1.x = saturate(dot(cb2[0].xyz, r1.xyz));
  r1.y = r1.x * r1.x;
  r1.y = dot(r1.yy, r5.ww);
  r1.y = -0.5 + r1.y;
  r1.z = 1 + -r2.x;
  r2.z = r1.z * r1.z;
  r2.z = r2.z * r2.z;
  r1.z = r2.z * r1.z;
  r1.z = r1.y * r1.z + 1;
  r2.z = 1 + -abs(r0.w);
  r3.w = r2.z * r2.z;
  r3.w = r3.w * r3.w;
  r2.z = r3.w * r2.z;
  r1.y = r1.y * r2.z + 1;
  r1.y = r1.z * r1.y;
  r1.y = r1.y * r2.x;
  r1.z = r5.w * r5.w;
  r1.z = max(0.00200000009, r1.z);
  r3.w = 1 + -r1.z;
  r4.w = abs(r0.w) * r3.w + r1.z;
  r3.w = r2.x * r3.w + r1.z;
  r0.w = r3.w * abs(r0.w);
  r0.w = r2.x * r4.w + r0.w;
  r0.w = 9.99999975e-006 + r0.w;
  r0.w = 0.5 / r0.w;
  r3.w = r1.z * r1.z;
  r4.w = r2.y * r3.w + -r2.y;
  r2.y = r4.w * r2.y + 1;
  r3.w = 0.318309873 * r3.w;
  r2.y = r2.y * r2.y + 1.00000001e-007;
  r2.y = r3.w / r2.y;
  r0.w = r2.y * r0.w;
  r0.w = 3.14159274 * r0.w;
  r0.w = r0.w * r2.x;
  r0.w = max(0, r0.w);
  r1.z = r1.z * r1.z + 1;
  r1.z = 1 / r1.z;
  r2.x = dot(r6.xyz, r6.xyz);
  r2.x = cmp(r2.x != 0.000000);
  r2.x = r2.x ? 1.000000 : 0;
  r0.w = r2.x * r0.w;
  r1.w = 1 + -r1.w;
  r1.w = saturate(r2.w + r1.w);
  r2.xyw = r7.xyz * r1.yyy;
  r5.xyz = r0.www * r7.xyz;
  r0.w = 1 + -r1.x;
  r1.x = r0.w * r0.w;
  r1.x = r1.x * r1.x;
  r0.xyzw = r1.zzzx * r0.xyzw;
  r7.xyz = float3(1,1,1) + -r6.xyz;
  r7.xyz = r7.xyz * r0.www + r6.xyz;
  r5.xyz = r7.xyz * r5.xyz;
  r2.xyw = r3.xyz * r2.xyw + r5.xyz;
  r1.xyz = r1.www + -r6.xyz;
  r1.xyz = r2.zzz * r1.xyz + r6.xyz;
  r0.xyz = r0.xyz * r1.xyz + r2.xyw;
  o0.xyz = r4.xyz * cb0[24].www + r0.xyz;
  o0.w = 1;
  return;
}